using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Utilities;

using Mono.Cecil;

namespace Xamarin.Android.Tasks
{
	class NativeRuntimeTokenIdCollection
	{
		struct ParameterValidator
		{
			public string FullTypeName { get; }
			public Func<MethodDefinition, ParameterDefinition, bool>? IsValid { get; }

			public ParameterValidator (string fullTypeName, Func<MethodDefinition, ParameterDefinition, bool>? validator = null)
			{
				if (fullTypeName.Length == 0) {
					throw new ArgumentException ("must not be an empty string", nameof (fullTypeName));
				}

				FullTypeName = fullTypeName;
				IsValid = validator;
			}
		}

		TaskLoggingHelper log;

		public uint AndroidRuntimeJnienv { get; private set; } = 0;
		public uint AndroidRuntimeJnienvInitialize { get; private set; } = 0;
		public uint AndroidRuntimeJnienvRegisterJniNatives { get; private set; } = 0;
		public uint AndroidRuntimeJnienvBridgeProcessing { get; private set; } = 0;

		public uint JavaLangObject { get; private set; } = 0;
		public uint JavaLangObjectHandle { get; private set; } = 0;
		public uint JavaLangObjectHandleType { get; private set; } = 0;
		public uint JavaLangObjectRefsAdded { get; private set; } = 0;
		public uint JavaLangObjectWeakHandle { get; private set; } = 0;

		public uint JavaLangThrowable { get; private set; } = 0;
		public uint JavaLangThrowableHandle { get; private set; } = 0;
		public uint JavaLangThrowableHandleType { get; private set; } = 0;
		public uint JavaLangThrowableRefsAdded { get; private set; } = 0;
		public uint JavaLangThrowableWeakHandle { get; private set; } = 0;

		public uint JavaInteropJavaObject { get; private set; } = 0;
		public uint JavaInteropJavaObjectHandle { get; private set; } = 0;
		public uint JavaInteropJavaObjectHandleType { get; private set; } = 0;
		public uint JavaInteropJavaObjectRefsAdded { get; private set; } = 0;
		public uint JavaInteropJavaObjectWeakHandle { get; private set; } = 0;

		public uint JavaInteropJavaException { get; private set; } = 0;
		public uint JavaInteropJavaExceptionHandle { get; private set; } = 0;
		public uint JavaInteropJavaExceptionHandleType { get; private set; } = 0;
		public uint JavaInteropJavaExceptionRefsAdded { get; private set; } = 0;
		public uint JavaInteropJavaExceptionWeakHandle { get; private set; } = 0;

		public NativeRuntimeTokenIdCollection (TaskLoggingHelper log)
		{
			this.log = log;
		}

		public void ProcessMonoAndroid (string fullPath)
		{
			AssemblyDefinition asm = AssemblyDefinition.ReadAssembly (fullPath);

			TypeDefinition? androidRuntimeJNIEnv = null;
			TypeDefinition? javaLangObject = null;
			TypeDefinition? javaLangThrowable = null;

			foreach (ModuleDefinition module in asm.Modules) {
				if (!module.HasTypes) {
					continue;
				}

				foreach (TypeDefinition type in module.Types) {
					if (IsNeededClass ("Android.Runtime", "JNIEnv", type, ref androidRuntimeJNIEnv)) {
						continue;
					}

					if (IsNeededClass ("Java.Lang", "Object", type, ref javaLangObject)) {
						continue;
					}

					if (IsNeededClass ("Java.Lang", "Throwable", type, ref javaLangThrowable)) {
						continue;
					}

					if (HaveAllClasses ()) {
						break;
					}
				}

				if (HaveAllClasses ()) {
					break;
				}
			}

			if (!HaveAllClasses ()) {
				throw new InvalidOperationException ($"Couldn't find all required classes in {fullPath}");
			}

			AndroidRuntimeJnienv = EnsureValidTokenId (androidRuntimeJNIEnv);
			JavaLangObject = EnsureValidTokenId (javaLangObject);
			JavaLangThrowable = EnsureValidTokenId (javaLangThrowable);

			var initializeParams = new List<ParameterValidator> {
				new ParameterValidator ("Android.Runtime.JnienvInitializeArgs*", (MethodDefinition m, ParameterDefinition p) => {
					if (p.ParameterType.IsPointer) {
						return true;
					}

					LogInvalidParameterInfo (m, p, "must be a pointer");
					return false;
				}),
			};
			AndroidRuntimeJnienvInitialize = FindMethodTokenId (androidRuntimeJNIEnv, "Initialize", "System.Void", true, initializeParams);

			var registerJniNativesParams = new List<ParameterValidator> {
				new ParameterValidator ("System.IntPtr"), // typeName_ptr
				new ParameterValidator ("System.Int32"),  // typeName_len
				new ParameterValidator ("System.IntPtr"), // jniClass
				new ParameterValidator ("System.IntPtr"), // methods_ptr
				new ParameterValidator ("System.Int32"),  // methods_len
			};
			AndroidRuntimeJnienvRegisterJniNatives = FindMethodTokenId (androidRuntimeJNIEnv, "RegisterJniNatives", "System.Void", true, registerJniNativesParams);
			AndroidRuntimeJnienvBridgeProcessing = FindFieldTokenId (androidRuntimeJNIEnv, "BridgeProcessing", "System.Boolean modreq(System.Runtime.CompilerServices.IsVolatile)", true);

			const string handleTypeType = "Android.Runtime.JObjectRefType";
			(JavaLangObjectHandle, JavaLangObjectHandleType, JavaLangObjectRefsAdded, JavaLangObjectWeakHandle) = FindOsBridgeFields (javaLangObject, handleTypeType);
			(JavaLangThrowableHandle, JavaLangThrowableHandleType, JavaLangThrowableRefsAdded, JavaLangThrowableWeakHandle) = FindOsBridgeFields (javaLangThrowable, handleTypeType);

			bool HaveAllClasses ()
			{
				return androidRuntimeJNIEnv != null && javaLangObject != null && javaLangThrowable != null;
			}
		}

		public void ProcessJavaInterop (string fullPath)
		{
			AssemblyDefinition asm = AssemblyDefinition.ReadAssembly (fullPath);

			TypeDefinition? javaInteropJavaObject = null;
			TypeDefinition? javaInteropJavaException = null;

			foreach (ModuleDefinition module in asm.Modules) {
				if (!module.HasTypes) {
					continue;
				}

				foreach (TypeDefinition type in module.Types) {
					if (IsNeededClass ("Java.Interop", "JavaObject", type, ref javaInteropJavaObject)) {
						continue;
					}

					if (IsNeededClass ("Java.Interop", "JavaException", type, ref javaInteropJavaException)) {
						continue;
					}

					if (HaveAllClasses ()) {
						break;
					}
				}

				if (HaveAllClasses ()) {
					break;
				}
			}

			if (!HaveAllClasses ()) {
				throw new InvalidOperationException ($"Couldn't find all required classes in {fullPath}");
			}

			JavaInteropJavaObject = EnsureValidTokenId (javaInteropJavaObject);
			JavaInteropJavaException = EnsureValidTokenId (javaInteropJavaException);

			const string handleTypeType = "Java.Interop.JniObjectReferenceType";
			(JavaInteropJavaObjectHandle, JavaInteropJavaObjectHandleType, JavaInteropJavaObjectRefsAdded, JavaInteropJavaObjectWeakHandle) = FindOsBridgeFields (javaInteropJavaObject, handleTypeType);
			(JavaInteropJavaExceptionHandle, JavaInteropJavaExceptionHandleType, JavaInteropJavaExceptionRefsAdded, JavaInteropJavaExceptionWeakHandle) = FindOsBridgeFields (javaInteropJavaException, handleTypeType);

			bool HaveAllClasses ()
			{
				return javaInteropJavaObject != null && javaInteropJavaException != null;
			}
		}

		(uint handle, uint handleType, uint refsAdded, uint weakHandle) FindOsBridgeFields (TypeDefinition type, string handleTypeType)
		{
			return (
				FindFieldTokenId (type, "handle", "System.IntPtr", false),
				FindFieldTokenId (type, "handle_type", handleTypeType, false),
				FindFieldTokenId (type, "refs_added", "System.Int32", false),
				FindFieldTokenId (type, "weak_handle", "System.IntPtr", false)
			);
		}

		void LogInvalidParameterInfo (MethodDefinition m, ParameterDefinition p, string message)
		{
			log.LogDebugMessage ($"Method '{m.FullName}', parameter {p.Index} ({p.Name}): {message}");
		}

		uint FindFieldTokenId (TypeDefinition type, string fieldName, string expectedType, bool shouldBeStatic)
		{
			FieldDefinition? field = null;
			foreach (FieldDefinition f in type.Fields) {
				if (String.Compare (fieldName, f.Name, StringComparison.Ordinal) != 0) {
					continue;
				}

				if (String.Compare (expectedType, f.FieldType.FullName, StringComparison.Ordinal) != 0) {
					log.LogDebugMessage ($"Field '{f.FullName}' has incorrect type '{f.FieldType.FullName}', expected '{expectedType}'");
					break;
				}

				if (f.IsStatic != shouldBeStatic) {
					log.LogDebugMessage ($"Field '{f.FullName}' should be static");
					break;
				}

				field = f;
				break;
			}

			if (field == null) {
				throw new InvalidOperationException ($"Failed to find required '{fieldName}' field in '{type.FullName}'");
			}

			return EnsureValidTokenId (field);
		}

		uint FindMethodTokenId (TypeDefinition type, string methodName, string expectedReturnType, bool shouldBeStatic, List<ParameterValidator>? parameters = null)
		{
			List<MethodDefinition> methods = GetMethods (type, methodName);

			int expectedParameterCount = parameters == null ? 0 : parameters.Count;
			MethodDefinition? method = null;
			foreach (MethodDefinition m in methods) {
				log.LogDebugMessage ($"Checking method {m.FullName}");
				if (!MethodMeetsBasicRequirements (m, shouldBeStatic, expectedParameterCount, expectedReturnType)) {
					continue;
				}

				if (expectedParameterCount == 0) {
					break;
				}

				bool allParamsValid = true;
				for (int i = 0; i < expectedParameterCount; i++) {
					ParameterDefinition p = m.Parameters[i];
					ParameterValidator v = parameters[i];

					if (String.Compare (p.ParameterType.FullName, v.FullTypeName, StringComparison.Ordinal) != 0) {
						log.LogDebugMessage ($"Parameter {i} of method '{m.FullName}' has incorrect type '{p.ParameterType.FullName}', expected '{v.FullTypeName}'");
						allParamsValid = false;
						break;
					}

					if (v.IsValid == null) {
						continue;
					}

					if (!v.IsValid (m, p)) {
						allParamsValid = false;
						break;
					}
				}

				if (!allParamsValid) {
					continue;
				}

				method = m;
				break;
			}

			if (method == null) {
				throw new InvalidOperationException ($"Failed to find required '{methodName}' method overload in '{type.FullName}'");
			}

			return EnsureValidTokenId (method);
		}

		bool MethodMeetsBasicRequirements (MethodDefinition method, bool shouldBeStatic, int expectedParameterCount, string returnTypeName)
		{
			if (method.IsAbstract) {
				return LogFailure ("cannot be abstract");
			}

			if (expectedParameterCount > 0 && !method.HasParameters) {
				return LogFailure ($"does not have parameters, expected {expectedParameterCount}");
			}

			if (method.Parameters.Count != expectedParameterCount) {
				return LogFailure ($"does not have correct number of parameters, expected {expectedParameterCount} but found {method.Parameters.Count}");
			}

			if (method.IsStatic != shouldBeStatic) {
				string not = shouldBeStatic ? String.Empty : " not";
				return LogFailure ($"should{not} be static");
			}

			if (String.Compare (method.ReturnType.FullName, returnTypeName, StringComparison.Ordinal) != 0) {
				return LogFailure ($"return type is '{method.ReturnType.FullName}', expected '{returnTypeName}'");
			}

			return true;

			bool LogFailure (string reason)
			{
				log.LogDebugMessage ($"Method '{method.FullName}' {reason}");
				return false;
			}
		}

		bool IsNeededClass (string ns, string name, TypeDefinition type, ref TypeDefinition? storedType)
		{
			if (storedType != null || !type.IsClass) {
				return false;
			}

			if (String.Compare (ns, type.Namespace, StringComparison.Ordinal) != 0 || String.Compare (name, type.Name, StringComparison.Ordinal) != 0) {
				return false;
			}

			storedType = type;
			return true;
		}

		List<MethodDefinition> GetMethods (TypeDefinition type, string name)
		{
			if (!type.HasMethods) {
				ThrowNoMethod ();
			}

			var ret = new List<MethodDefinition> ();
			foreach (MethodDefinition method in type.Methods) {
				if (method.IsAbstract || String.Compare (method.Name, name, StringComparison.Ordinal) != 0) {
					continue;
				}

				ret.Add (method);
			}

			if (ret.Count == 0) {
				ThrowNoMethod ();
			}

			return ret;

			void ThrowNoMethod ()
			{
				throw new InvalidOperationException ($"'{type.FullName}' does not contain the required method '{name}'");
			}
		}

		uint EnsureValidTokenId (MemberReference member)
		{
			uint ret = member.MetadataToken.ToUInt32 ();
			if (ret == 0) {
				throw new InvalidOperationException ($"Member {member.FullName} doesn't have a valid token ID");
			}
			return ret;
		}
	}
}
