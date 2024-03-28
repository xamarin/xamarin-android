using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	class MarshalMethodsAssemblyRewriter
	{
		sealed class AssemblyImports
		{
			public MethodReference MonoUnhandledExceptionMethod;
			public TypeReference   SystemException;
			public MethodReference UnhandledExceptionMethod;
			public CustomAttribute UnmanagedCallersOnlyAttribute;
			public MethodReference WaitForBridgeProcessingMethod;
		}

		readonly TaskLoggingHelper log;
		readonly MarshalMethodsClassifier classifier;
		readonly XAAssemblyResolver resolver;
		readonly AndroidTargetArch targetArch;

		public MarshalMethodsAssemblyRewriter (TaskLoggingHelper log, AndroidTargetArch targetArch, MarshalMethodsClassifier classifier, XAAssemblyResolver resolver)
		{
			this.log = log ?? throw new ArgumentNullException (nameof (log));
			this.targetArch = targetArch;
			this.classifier = classifier ?? throw new ArgumentNullException (nameof (classifier));;
			this.resolver = resolver ?? throw new ArgumentNullException (nameof (resolver));;
		}

		// TODO: do away with broken exception transitions, there's no point in supporting them
		public void Rewrite (bool brokenExceptionTransitions)
		{
			AssemblyDefinition? monoAndroidRuntime = resolver.Resolve ("Mono.Android.Runtime");
			if (monoAndroidRuntime == null) {
				throw new InvalidOperationException ($"[{targetArch}] Internal error: unable to load the Mono.Android.Runtime assembly");
			}

			TypeDefinition runtime = FindType (monoAndroidRuntime, "Android.Runtime.AndroidRuntimeInternal", required: true)!;
			MethodDefinition waitForBridgeProcessingMethod = FindMethod (runtime, "WaitForBridgeProcessing", required: true)!;

			TypeDefinition androidEnvironment = FindType (monoAndroidRuntime, "Android.Runtime.AndroidEnvironmentInternal", required: true)!;
			MethodDefinition unhandledExceptionMethod = FindMethod (androidEnvironment, "UnhandledException", required: true)!;

			TypeDefinition runtimeNativeMethods = FindType (monoAndroidRuntime, "Android.Runtime.RuntimeNativeMethods", required: true);
			MethodDefinition monoUnhandledExceptionMethod = FindMethod (runtimeNativeMethods, "monodroid_debugger_unhandled_exception", required: true);

			AssemblyDefinition corlib = resolver.Resolve ("System.Private.CoreLib");
			TypeDefinition systemException = FindType (corlib, "System.Exception", required: true);

			MethodDefinition unmanagedCallersOnlyAttributeCtor = GetUnmanagedCallersOnlyAttributeConstructor (resolver);

			var assemblyImports = new Dictionary<AssemblyDefinition, AssemblyImports> ();
			foreach (AssemblyDefinition asm in classifier.Assemblies) {
				var imports = new AssemblyImports {
					MonoUnhandledExceptionMethod  = asm.MainModule.ImportReference (monoUnhandledExceptionMethod),
					SystemException               = asm.MainModule.ImportReference (systemException),
					UnhandledExceptionMethod      = asm.MainModule.ImportReference (unhandledExceptionMethod),
					UnmanagedCallersOnlyAttribute = CreateImportedUnmanagedCallersOnlyAttribute (asm, unmanagedCallersOnlyAttributeCtor),
					WaitForBridgeProcessingMethod = asm.MainModule.ImportReference (waitForBridgeProcessingMethod),
				};

				assemblyImports.Add (asm, imports);
			}

			log.LogDebugMessage ($"[{targetArch}] Rewriting assemblies for marshal methods support");

			var processedMethods = new Dictionary<string, MethodDefinition> (StringComparer.Ordinal);
			foreach (IList<MarshalMethodEntry> methodList in classifier.MarshalMethods.Values) {
				foreach (MarshalMethodEntry method in methodList) {
					string fullNativeCallbackName = method.NativeCallback.FullName;
					if (processedMethods.TryGetValue (fullNativeCallbackName, out MethodDefinition nativeCallbackWrapper)) {
						method.NativeCallbackWrapper = nativeCallbackWrapper;
						continue;
					}

					method.NativeCallbackWrapper = GenerateWrapper (method, assemblyImports, brokenExceptionTransitions);
					if (method.Connector != null) {
						if (method.Connector.IsStatic && method.Connector.IsPrivate) {
							log.LogDebugMessage ($"[{targetArch}] Removing connector method {method.Connector.FullName}");
							method.Connector.DeclaringType?.Methods?.Remove (method.Connector);
						} else {
							log.LogWarning ($"[{targetArch}] NOT removing connector method {method.Connector.FullName} because it's either not static or not private");
						}
					}

					if (method.CallbackField != null) {
						if (method.CallbackField.IsStatic && method.CallbackField.IsPrivate) {
							log.LogDebugMessage ($"[{targetArch}] Removing callback delegate backing field {method.CallbackField.FullName}");
							method.CallbackField.DeclaringType?.Fields?.Remove (method.CallbackField);
						} else {
							log.LogWarning ($"[{targetArch}] NOT removing callback field {method.CallbackField.FullName} because it's either not static or not private");
						}
					}

					processedMethods.Add (fullNativeCallbackName, method.NativeCallback);
				}
			}

			foreach (AssemblyDefinition asm in classifier.Assemblies) {
				string? path = asm.MainModule.FileName;
				if (String.IsNullOrEmpty (path)) {
					throw new InvalidOperationException ($"[{targetArch}] Internal error: assembly '{asm}' does not specify path to its file");
				}

				string pathPdb = Path.ChangeExtension (path, ".pdb");
				bool havePdb = File.Exists (pathPdb);

				var writerParams = new WriterParameters {
					WriteSymbols = havePdb,
				};

				string directory = Path.Combine (Path.GetDirectoryName (path), "new");
				Directory.CreateDirectory (directory);
				string output = Path.Combine (directory, Path.GetFileName (path));
				log.LogDebugMessage ($"[{targetArch}] Writing new version of '{path}' assembly: {output}");

				// TODO: this should be used eventually, but it requires that all the types are reloaded from the assemblies before typemaps are generated
				// since Cecil doesn't update the MVID in the already loaded types
				//asm.MainModule.Mvid = Guid.NewGuid ();
				asm.Write (output, writerParams);

				CopyFile (output, path);
				RemoveFile (output);

				if (havePdb) {
					string outputPdb = Path.ChangeExtension (output, ".pdb");
					if (File.Exists (outputPdb)) {
						CopyFile (outputPdb, pathPdb);
					}
					RemoveFile (outputPdb);
				}
			}

			void CopyFile (string source, string target)
			{
				log.LogDebugMessage ($"[{targetArch}] Copying rewritten assembly: {source} -> {target}");

				string targetBackup = $"{target}.bak";
				if (File.Exists (target)) {
					// Try to avoid sharing violations by first renaming the target
					File.Move (target, targetBackup);
				}

				File.Copy (source, target, true);

				if (File.Exists (targetBackup)) {
					try {
						File.Delete (targetBackup);
					} catch (Exception ex) {
						// On Windows the deletion may fail, depending on lock state of the original `target` file before the move.
						log.LogDebugMessage ($"[{targetArch}] While trying to delete '{targetBackup}', exception was thrown: {ex}");
						log.LogDebugMessage ($"[{targetArch}] Failed to delete backup file '{targetBackup}', ignoring.");
					}
				}
			}

			void RemoveFile (string? path)
			{
				if (String.IsNullOrEmpty (path) || !File.Exists (path)) {
					return;
				}

				try {
					log.LogDebugMessage ($"[{targetArch}] Deleting: {path}");
					File.Delete (path);
				} catch (Exception ex) {
					log.LogWarning ($"[{targetArch}] Unable to delete source file '{path}'");
					log.LogDebugMessage ($"[{targetArch}] {ex.ToString ()}");
				}
			}
		}

		MethodDefinition GenerateWrapper (MarshalMethodEntry method, Dictionary<AssemblyDefinition, AssemblyImports> assemblyImports, bool brokenExceptionTransitions)
		{
			MethodDefinition callback = method.NativeCallback;
			AssemblyImports imports = assemblyImports [callback.Module.Assembly];
			string wrapperName = $"{callback.Name}_mm_wrapper";
			TypeReference retType = MapToBlittableTypeIfNecessary (callback.ReturnType, out bool returnTypeMapped);
			bool hasReturnValue = String.Compare ("System.Void", callback.ReturnType.FullName, StringComparison.Ordinal) != 0;
			var wrapperMethod = new MethodDefinition (wrapperName, callback.Attributes, retType);

			callback.DeclaringType.Methods.Add (wrapperMethod);
			wrapperMethod.CustomAttributes.Add (imports.UnmanagedCallersOnlyAttribute);

			MethodBody body = wrapperMethod.Body;
			body.InitLocals = true;

			VariableDefinition? retval = null;
			if (hasReturnValue) {
				retval = new VariableDefinition (retType);
				body.Variables.Add (retval);
			}

			body.Instructions.Add (Instruction.Create (OpCodes.Call, imports.WaitForBridgeProcessingMethod));
			var exceptionHandler = new ExceptionHandler (ExceptionHandlerType.Catch) {
				CatchType = imports.SystemException,
			};

			body.ExceptionHandlers.Add (exceptionHandler);

			Instruction? firstTryInstruction = null;
			Instruction? inst = null;
			uint nparam = 0;
			foreach (ParameterDefinition pdef in callback.Parameters) {
				TypeReference newType = MapToBlittableTypeIfNecessary (pdef.ParameterType, out _);
				wrapperMethod.Parameters.Add (new ParameterDefinition (pdef.Name, pdef.Attributes, newType));

				inst = GetLoadArgInstruction (nparam++, pdef);
				if (firstTryInstruction == null) {
					firstTryInstruction = inst;
				}

				body.Instructions.Add (inst);

				if (!pdef.ParameterType.IsBlittable ()) {
					GenerateNonBlittableConversion (pdef.ParameterType, newType);
				}
			}

			inst = Instruction.Create (OpCodes.Call, callback);
			if (firstTryInstruction == null) {
				firstTryInstruction = inst;
			}
			body.Instructions.Add (inst);

			exceptionHandler.TryStart = firstTryInstruction;

			if (hasReturnValue && !returnTypeMapped) {
				body.Instructions.Add (Instruction.Create (OpCodes.Stloc, retval));
			}

			Instruction ret = Instruction.Create (OpCodes.Ret);
			Instruction? retValLoadInst = null;
			Instruction leaveTarget;

			if (hasReturnValue) {
				if (returnTypeMapped) {
					GenerateRetValCast (callback.ReturnType, retType);
					body.Instructions.Add (Instruction.Create (OpCodes.Stloc, retval));
				}

				retValLoadInst = Instruction.Create (OpCodes.Ldloc, retval);
				leaveTarget = retValLoadInst;
			} else {
				leaveTarget = ret;
			}

			body.Instructions.Add (Instruction.Create (OpCodes.Leave_S, leaveTarget));

			var exceptionVar = new VariableDefinition (imports.SystemException);
			body.Variables.Add (exceptionVar);

			var catchStartInst = Instruction.Create (OpCodes.Stloc, exceptionVar);
			exceptionHandler.HandlerStart = catchStartInst;

			// TryEnd must point to the next instruction after the try block
			exceptionHandler.TryEnd = catchStartInst;

			body.Instructions.Add (catchStartInst);
			body.Instructions.Add (Instruction.Create (OpCodes.Ldarg_0));
			body.Instructions.Add (Instruction.Create (OpCodes.Ldloc, exceptionVar));

			if (brokenExceptionTransitions) {
				body.Instructions.Add (Instruction.Create (OpCodes.Call, imports.MonoUnhandledExceptionMethod));
				body.Instructions.Add (Instruction.Create (OpCodes.Throw));
			} else {
				body.Instructions.Add (Instruction.Create (OpCodes.Call, imports.UnhandledExceptionMethod));

				if (hasReturnValue) {
					AddSetDefaultValueInstructions (body, retType, retval);
				}
			}

			body.Instructions.Add (Instruction.Create (OpCodes.Leave_S, leaveTarget));

			// HandlerEnd must point to the next instruction after the catch block
			if (hasReturnValue) {
				body.Instructions.Add (retValLoadInst);
				exceptionHandler.HandlerEnd = retValLoadInst;
			} else {
				exceptionHandler.HandlerEnd = ret;
			}
			body.Instructions.Add (ret);

			return wrapperMethod;

			void GenerateNonBlittableConversion (TypeReference sourceType, TypeReference targetType)
			{
				if (IsBooleanConversion (sourceType, targetType)) {
					// We output equivalent of the `param != 0` C# code
					body.Instructions.Add (Instruction.Create (OpCodes.Ldc_I4_0));
					body.Instructions.Add (Instruction.Create (OpCodes.Cgt_Un));
					return;
				}

				if (IsCharConversion (sourceType, targetType)) {
					// No need to generate any code. Java's `char` is unsigned 16-bit type, the same as
					// .NET's
					return;
				}

				ThrowUnsupportedType (sourceType);
			}

			void GenerateRetValCast (TypeReference sourceType, TypeReference targetType)
			{
				if (IsBooleanConversion (sourceType, targetType)) {
					var insLoadOne = Instruction.Create (OpCodes.Ldc_I4_1);
					var insConvert = Instruction.Create (OpCodes.Conv_U1);

					body.Instructions.Add (Instruction.Create (OpCodes.Brtrue_S, insLoadOne));
					body.Instructions.Add (Instruction.Create (OpCodes.Ldc_I4_0));
					body.Instructions.Add (Instruction.Create (OpCodes.Br_S, insConvert));
					body.Instructions.Add (insLoadOne);
					body.Instructions.Add (insConvert);
					return;
				}

				ThrowUnsupportedType (sourceType);
			}

			bool IsBooleanConversion (TypeReference sourceType, TypeReference targetType)
			{
				if (String.Compare ("System.Boolean", sourceType.FullName, StringComparison.Ordinal) == 0) {
					if (String.Compare ("System.Byte", targetType.FullName, StringComparison.Ordinal) != 0) {
						throw new InvalidOperationException ($"[{targetArch}] Unexpected conversion from '{sourceType.FullName}' to '{targetType.FullName}'");
					}

					return true;
				}

				return false;
			}

			bool IsCharConversion (TypeReference sourceType, TypeReference targetType)
			{
				if (String.Compare ("System.Char", sourceType.FullName, StringComparison.Ordinal) == 0) {
					if (String.Compare ("System.UInt16", targetType.FullName, StringComparison.Ordinal) != 0) {
						throw new InvalidOperationException ($"Unexpected conversion from '{sourceType.FullName}' to '{targetType.FullName}'");
					}

					return true;
				}

				return false;
			}

			void ThrowUnsupportedType (TypeReference type)
			{
				throw new InvalidOperationException ($"[{targetArch}] Unsupported non-blittable type '{type.FullName}'");
			}
		}

		void AddSetDefaultValueInstructions (MethodBody body, TypeReference type, VariableDefinition retval)
		{
			bool supported = false;

			switch (type.FullName) {
				case "System.Boolean":
				case "System.Byte":
				case "System.Int16":
				case "System.Int32":
				case "System.SByte":
				case "System.UInt16":
				case "System.UInt32":
					supported = true;
					body.Instructions.Add (Instruction.Create (OpCodes.Ldc_I4_0));
					break;

				case "System.Int64":
				case "System.UInt64":
					supported = true;
					body.Instructions.Add (Instruction.Create (OpCodes.Ldc_I4_0));
					body.Instructions.Add (Instruction.Create (OpCodes.Conv_I8));
					break;

				case "System.IntPtr":
				case "System.UIntPtr":
					supported = true;
					body.Instructions.Add (Instruction.Create (OpCodes.Ldc_I4_0));
					body.Instructions.Add (Instruction.Create (OpCodes.Conv_I));
					break;

				case "System.Single":
					supported = true;
					body.Instructions.Add (Instruction.Create (OpCodes.Ldc_R4, 0.0F));
					break;

				case "System.Double":
					supported = true;
					body.Instructions.Add (Instruction.Create (OpCodes.Ldc_R8, 0.0));
					break;
			}

			if (supported) {
				body.Instructions.Add (Instruction.Create (OpCodes.Stloc, retval));
				return;
			}

			throw new InvalidOperationException ($"[{targetArch}] Unsupported type: '{type.FullName}'");
		}


		Instruction GetLoadArgInstruction (uint nparam, ParameterDefinition pdef)
		{
			OpCode ldargOp;
			bool paramRef = false;

			switch (nparam++) {
				case 0:
					ldargOp = OpCodes.Ldarg_0;
					break;

				case 1:
					ldargOp = OpCodes.Ldarg_1;
					break;

				case 2:
					ldargOp = OpCodes.Ldarg_2;
					break;

				case 3:
					ldargOp = OpCodes.Ldarg_3;
					break;

				default:
					ldargOp = OpCodes.Ldarg_S;
					paramRef = true;
					break;
			}

			if (!paramRef) {
				return Instruction.Create (ldargOp);
			}

			return Instruction.Create (ldargOp, pdef);
		}

		TypeReference MapToBlittableTypeIfNecessary (TypeReference type, out bool typeMapped)
		{
			if (type.IsBlittable () || String.Compare ("System.Void", type.FullName, StringComparison.Ordinal) == 0) {
				typeMapped = false;
				return type;
			}

			if (String.Compare ("System.Boolean", type.FullName, StringComparison.Ordinal) == 0) {
				// Maps to Java JNI's jboolean which is an unsigned 8-bit type
				typeMapped = true;
				return ReturnValid (typeof(byte));
			}

			if (String.Compare ("System.Char", type.FullName, StringComparison.Ordinal) == 0) {
				// Maps to Java JNI's jchar which is an unsigned 16-bit type
				typeMapped = true;
				return ReturnValid (typeof(ushort));
			}

			throw new NotSupportedException ($"[{targetArch}] Cannot map unsupported blittable type '{type.FullName}'");

			TypeReference ReturnValid (Type typeToLookUp)
			{
				TypeReference? mappedType = type.Module.Assembly.MainModule.ImportReference (typeToLookUp);
				if (mappedType == null) {
					throw new InvalidOperationException ($"[{targetArch}] Unable to obtain reference to type '{typeToLookUp.FullName}'");
				}

				return mappedType;
			}
		}

		MethodDefinition GetUnmanagedCallersOnlyAttributeConstructor (IAssemblyResolver resolver)
		{
			AssemblyDefinition asm = resolver.Resolve ("System.Runtime.InteropServices");
			TypeDefinition unmanagedCallersOnlyAttribute = null;
			foreach (ModuleDefinition md in asm.Modules) {
				foreach (ExportedType et in md.ExportedTypes) {
					if (!et.IsForwarder) {
						continue;
					}

					if (String.Compare ("System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute", et.FullName, StringComparison.Ordinal) != 0) {
						continue;
					}

					unmanagedCallersOnlyAttribute = et.Resolve ();
					break;
				}
			}

			if (unmanagedCallersOnlyAttribute == null) {
				throw new InvalidOperationException ("[{targetArch}] Unable to find the System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute type");
			}

			foreach (MethodDefinition md in unmanagedCallersOnlyAttribute.Methods) {
				if (!md.IsConstructor) {
					continue;
				}

				return md;
			}

			throw new InvalidOperationException ("[{targetArch}] Unable to find the System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute type constructor");
		}

		CustomAttribute CreateImportedUnmanagedCallersOnlyAttribute (AssemblyDefinition targetAssembly, MethodDefinition unmanagedCallersOnlyAtributeCtor)
		{
			return new CustomAttribute (targetAssembly.MainModule.ImportReference (unmanagedCallersOnlyAtributeCtor));
		}

		MethodDefinition? FindMethod (TypeDefinition type, string methodName, bool required)
		{
			log.LogDebugMessage ($"[{targetArch}] Looking for method '{methodName}' in type {type}");
			foreach (MethodDefinition method in type.Methods) {
				log.LogDebugMessage ($"[{targetArch}]   method: {method.Name}");
				if (String.Compare (methodName, method.Name, StringComparison.Ordinal) == 0) {
					log.LogDebugMessage ($"[{targetArch}]     match!");
					return method;
				}
			}

			if (required) {
				throw new InvalidOperationException ($"[{targetArch}] Internal error: required method '{methodName}' in type {type} not found");
			}

			return null;
		}

		TypeDefinition? FindType (AssemblyDefinition asm, string typeName, bool required)
		{
			log.LogDebugMessage ($"[{targetArch}] Looking for type '{typeName}' in assembly '{asm}' ({GetAssemblyPathInfo (asm)})");
			foreach (TypeDefinition t in asm.MainModule.Types) {
				log.LogDebugMessage ($"[{targetArch}]    checking {t.FullName}");
				if (String.Compare (typeName, t.FullName, StringComparison.Ordinal) == 0) {
					log.LogDebugMessage ($"[{targetArch}]     match!");
					return t;
				}
			}

			if (required) {
				throw new InvalidOperationException ($"[{targetArch}] Internal error: required type '{typeName}' in assembly {asm} not found");
			}

			return null;
		}

		static string GetAssemblyPathInfo (AssemblyDefinition asm)
		{
			string? path = asm.MainModule.FileName;
			if (String.IsNullOrEmpty (path)) {
				return "no assembly path";
			}

			return path;
		}
	}
}
