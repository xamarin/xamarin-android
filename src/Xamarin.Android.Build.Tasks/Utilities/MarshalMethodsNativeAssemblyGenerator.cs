using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Utilities;

using Xamarin.Android.Tasks.LLVMIR;
using Xamarin.Android.Tools;

using CecilMethodDefinition = global::Mono.Cecil.MethodDefinition;
using CecilParameterDefinition = global::Mono.Cecil.ParameterDefinition;

namespace Xamarin.Android.Tasks
{
	partial class MarshalMethodsNativeAssemblyGenerator : LlvmIrComposer
	{
		const string GetFunctionPointerVariableName = "get_function_pointer";

		// This is here only to generate strongly-typed IR
		internal sealed class MonoClass
		{}

		[NativeClass]
		sealed class _JNIEnv
		{}

		// Empty class must have at least one member so that the class address can be obtained
		[NativeClass]
		class _jobject
		{
			public byte b;
		}

		sealed class _jclass : _jobject
		{}

		sealed class _jstring : _jobject
		{}

		sealed class _jthrowable : _jobject
		{}

		class _jarray : _jobject
		{}

		sealed class _jobjectArray : _jarray
		{}

		sealed class _jbooleanArray : _jarray
		{}

		sealed class _jbyteArray : _jarray
		{}

		sealed class _jcharArray : _jarray
		{}

		sealed class _jshortArray : _jarray
		{}

		sealed class _jintArray : _jarray
		{}

		sealed class _jlongArray : _jarray
		{}

		sealed class _jfloatArray : _jarray
		{}

		sealed class _jdoubleArray : _jarray
		{}

		sealed class MarshalMethodInfo
		{
			public MarshalMethodEntry Method                { get; }
			public string NativeSymbolName                  { get; set; }
			public List<LlvmIrFunctionParameter> Parameters { get; }
			public Type ReturnType                          { get; }
			public uint ClassCacheIndex                     { get; }

			// This one isn't known until the generation time, which happens after we instantiate the class
			// in Init and it may be different between architectures/ABIs, hence it needs to be settable from
			// the outside.
			public uint AssemblyCacheIndex                  { get; set; }

			public MarshalMethodInfo (MarshalMethodEntry method, Type returnType, string nativeSymbolName, int classCacheIndex)
			{
				Method = method ?? throw new ArgumentNullException (nameof (method));
				ReturnType = returnType ?? throw new ArgumentNullException (nameof (returnType));
				if (String.IsNullOrEmpty (nativeSymbolName)) {
					throw new ArgumentException ("must not be null or empty", nameof (nativeSymbolName));
				}
				NativeSymbolName = nativeSymbolName;
				Parameters = new List<LlvmIrFunctionParameter> {
					new LlvmIrFunctionParameter (typeof (_JNIEnv), "env"), // JNIEnv *env
					new LlvmIrFunctionParameter (typeof (_jclass), "klass"), // jclass klass
				};
				ClassCacheIndex = (uint)classCacheIndex;
			}
		}

		sealed class MarshalMethodsManagedClassDataProvider : NativeAssemblerStructContextDataProvider
		{
			public override string GetComment (object data, string fieldName)
			{
				var klass = EnsureType<MarshalMethodsManagedClass> (data);

				if (String.Compare ("token", fieldName, StringComparison.Ordinal) == 0) {
					return $" class name: {klass.ClassName}";
				}

				return String.Empty;
			}
		}

		[NativeAssemblerStructContextDataProvider (typeof(MarshalMethodsManagedClassDataProvider))]
		sealed class MarshalMethodsManagedClass
		{
			[NativeAssembler (UsesDataProvider = true, NumberFormat = LlvmIrVariableNumberFormat.Hexadecimal)]
			public uint       token;

			[NativePointer (IsNull = true)]
			public MonoClass  klass;

			[NativeAssembler (Ignore = true)]
			public string ClassName;
		};

		sealed class MarshalMethodNameDataProvider : NativeAssemblerStructContextDataProvider
		{
			public override string GetComment (object data, string fieldName)
			{
				var methodName = EnsureType<MarshalMethodName> (data);

				if (String.Compare ("id", fieldName, StringComparison.Ordinal) == 0) {
					return $" name: {methodName.name}";
				}

				return String.Empty;
			}
		}

		[NativeAssemblerStructContextDataProvider (typeof(MarshalMethodNameDataProvider))]
		sealed class MarshalMethodName
		{
			[NativeAssembler (Ignore = true)]
			public ulong Id32;

			[NativeAssembler (Ignore = true)]
			public ulong Id64;

			[NativeAssembler (UsesDataProvider = true, NumberFormat = LlvmIrVariableNumberFormat.Hexadecimal)]
			public ulong  id;
			public string name;
		}

		sealed class AssemblyCacheState
		{
			public Dictionary<string, uint>? AsmNameToIndexData32;
			public Dictionary<uint, (string name, uint index)> Hashes32;
			public List<uint> Keys32;
			public List<uint> Indices32;

			public Dictionary<string, uint>? AsmNameToIndexData64;
			public Dictionary<ulong, (string name, uint index)> Hashes64;
			public List<ulong> Keys64;
			public List<uint> Indices64;
		}

		sealed class MarshalMethodsWriteState
		{
			public AssemblyCacheState AssemblyCacheState;
			public LlvmIrFunctionAttributeSet AttributeSet;
			public Dictionary<string, ulong> UniqueAssemblyId;
			public Dictionary<string, LlvmIrVariable> UsedBackingFields;
			public LlvmIrVariable GetFunctionPtrVariable;
			public LlvmIrFunction GetFunctionPtrFunction;
		}

		sealed class MarshalMethodAssemblyIndexValuePlaceholder : LlvmIrInstructionArgumentValuePlaceholder
		{
			MarshalMethodInfo mmi;
			AssemblyCacheState acs;

			public MarshalMethodAssemblyIndexValuePlaceholder (MarshalMethodInfo mmi, AssemblyCacheState acs)
			{
				this.mmi = mmi;
				this.acs = acs;
			}

			public override object? GetValue (LlvmIrModuleTarget target)
			{
				// What a monstrosity...
				string asmName = mmi.Method.NativeCallback.DeclaringType.Module.Assembly.Name.Name;
				Dictionary<string, uint> asmNameToIndex = target.Is64Bit ? acs.AsmNameToIndexData64 : acs.AsmNameToIndexData32;
				if (!asmNameToIndex.TryGetValue (asmName, out uint asmIndex)) {
					throw new InvalidOperationException ($"Unable to translate assembly name '{asmName}' to its index");
				}
				return asmIndex;
			}
		}

		static readonly Dictionary<char, Type> jniSimpleTypeMap = new Dictionary<char, Type> {
			{ 'Z', typeof(bool) },
			{ 'B', typeof(byte) },
			{ 'C', typeof(char) },
			{ 'S', typeof(short) },
			{ 'I', typeof(int) },
			{ 'J', typeof(long) },
			{ 'F', typeof(float) },
			{ 'D', typeof(double) },
		};

		static readonly Dictionary<char, Type> jniArrayTypeMap = new Dictionary<char, Type> {
			{ 'Z', typeof(_jbooleanArray) },
			{ 'B', typeof(_jbyteArray) },
			{ 'C', typeof(_jcharArray) },
			{ 'S', typeof(_jshortArray) },
			{ 'I', typeof(_jintArray) },
			{ 'J', typeof(_jlongArray) },
			{ 'F', typeof(_jfloatArray) },
			{ 'D', typeof(_jdoubleArray) },
			{ 'L', typeof(_jobjectArray) },
		};

		readonly ICollection<string> uniqueAssemblyNames;
		readonly int numberOfAssembliesInApk;

		StructureInfo marshalMethodsManagedClassStructureInfo;
		StructureInfo marshalMethodNameStructureInfo;

		List<MarshalMethodInfo> methods;
		List<StructureInstance<MarshalMethodsManagedClass>> classes = new List<StructureInstance<MarshalMethodsManagedClass>> ();

		readonly LlvmIrCallMarker defaultCallMarker;
		readonly bool generateEmptyCode;
		readonly AndroidTargetArch targetArch;
		readonly NativeCodeGenState? codeGenState;

		/// <summary>
		/// Constructor to be used ONLY when marshal methods are DISABLED
		/// </summary>
		public MarshalMethodsNativeAssemblyGenerator (TaskLoggingHelper log, AndroidTargetArch targetArch, int numberOfAssembliesInApk, ICollection<string> uniqueAssemblyNames)
			: base (log)
		{
			this.targetArch = targetArch;
			this.numberOfAssembliesInApk = numberOfAssembliesInApk;
			this.uniqueAssemblyNames = uniqueAssemblyNames ?? throw new ArgumentNullException (nameof (uniqueAssemblyNames));
			generateEmptyCode = true;
			defaultCallMarker = LlvmIrCallMarker.Tail;
		}

		/// <summary>
		/// Constructor to be used ONLY when marshal methods are ENABLED
		/// </summary>
		public MarshalMethodsNativeAssemblyGenerator (TaskLoggingHelper log, int numberOfAssembliesInApk, ICollection<string> uniqueAssemblyNames, NativeCodeGenState codeGenState, MarshalMethodsTracingMode tracingMode)
			: base (log)
		{
			this.numberOfAssembliesInApk = numberOfAssembliesInApk;
			this.uniqueAssemblyNames = uniqueAssemblyNames ?? throw new ArgumentNullException (nameof (uniqueAssemblyNames));
			this.codeGenState = codeGenState ?? throw new ArgumentNullException (nameof (codeGenState));

			generateEmptyCode = false;
			this.tracingMode = tracingMode;
			defaultCallMarker = tracingMode != MarshalMethodsTracingMode.None ? LlvmIrCallMarker.None : LlvmIrCallMarker.Tail;
		}

		void Init ()
		{
			if (generateEmptyCode || codeGenState.Classifier == null || codeGenState.Classifier.MarshalMethods.Count == 0) {
				return;
			}

			var seenClasses = new Dictionary<string, int> (StringComparer.Ordinal);
			var allMethods = new List<MarshalMethodInfo> ();
			IDictionary<string, IList<MarshalMethodEntry>> marshalMethods = codeGenState.Classifier.MarshalMethods;

			// It's possible that several otherwise different methods (from different classes, but with the same
			// names and similar signatures) will actually share the same **short** native symbol name. In this case we must
			// ensure that they all use long symbol names.  This has to be done as a post-processing step, after we
			// have already iterated over the entire method collection.
			//
			// A handful of examples from the Hello World MAUI app:
			//
			// Overloaded MM: Java_crc64e1fb321c08285b90_CellAdapter_n_1onActionItemClicked
			//   implemented in: Microsoft.Maui.Controls.Handlers.Compatibility.CellAdapter (System.Boolean Android.Views.ActionMode/ICallback::OnActionItemClicked(Android.Views.ActionMode,Android.Views.IMenuItem))
			//   implemented in: Microsoft.Maui.Controls.Handlers.Compatibility.CellAdapter (System.Boolean AndroidX.AppCompat.View.ActionMode/ICallback::OnActionItemClicked(AndroidX.AppCompat.View.ActionMode,Android.Views.IMenuItem))
			//   new native symbol name: Java_crc64e1fb321c08285b90_CellAdapter_n_1onActionItemClicked__Landroidx_appcompat_view_ActionMode_2Landroid_view_MenuItem_2
			//
			// Overloaded MM: Java_crc64e1fb321c08285b90_CellAdapter_n_1onCreateActionMode
			//   implemented in: Microsoft.Maui.Controls.Handlers.Compatibility.CellAdapter (System.Boolean Android.Views.ActionMode/ICallback::OnCreateActionMode(Android.Views.ActionMode,Android.Views.IMenu))
			//   implemented in: Microsoft.Maui.Controls.Handlers.Compatibility.CellAdapter (System.Boolean AndroidX.AppCompat.View.ActionMode/ICallback::OnCreateActionMode(AndroidX.AppCompat.View.ActionMode,Android.Views.IMenu))
			//   new native symbol name: Java_crc64e1fb321c08285b90_CellAdapter_n_1onCreateActionMode__Landroidx_appcompat_view_ActionMode_2Landroid_view_Menu_2
			//
			// Overloaded MM: Java_crc64e1fb321c08285b90_CellAdapter_n_1onDestroyActionMode
			//   implemented in: Microsoft.Maui.Controls.Handlers.Compatibility.CellAdapter (System.Void Android.Views.ActionMode/ICallback::OnDestroyActionMode(Android.Views.ActionMode))
			//   implemented in: Microsoft.Maui.Controls.Handlers.Compatibility.CellAdapter (System.Void AndroidX.AppCompat.View.ActionMode/ICallback::OnDestroyActionMode(AndroidX.AppCompat.View.ActionMode))
			//   new native symbol name: Java_crc64e1fb321c08285b90_CellAdapter_n_1onDestroyActionMode__Landroidx_appcompat_view_ActionMode_2
			//
			// Overloaded MM: Java_crc64e1fb321c08285b90_CellAdapter_n_1onPrepareActionMode
			//   implemented in: Microsoft.Maui.Controls.Handlers.Compatibility.CellAdapter (System.Boolean Android.Views.ActionMode/ICallback::OnPrepareActionMode(Android.Views.ActionMode,Android.Views.IMenu))
			//   implemented in: Microsoft.Maui.Controls.Handlers.Compatibility.CellAdapter (System.Boolean AndroidX.AppCompat.View.ActionMode/ICallback::OnPrepareActionMode(AndroidX.AppCompat.View.ActionMode,Android.Views.IMenu))
			//   new native symbol name: Java_crc64e1fb321c08285b90_CellAdapter_n_1onPrepareActionMode__Landroidx_appcompat_view_ActionMode_2Landroid_view_Menu_2
			//
			var overloadedNativeSymbolNames = new Dictionary<string, List<MarshalMethodInfo>> (StringComparer.Ordinal);
			foreach (IList<MarshalMethodEntry> entryList in marshalMethods.Values) {
				bool useFullNativeSignature = entryList.Count > 1;
				foreach (MarshalMethodEntry entry in entryList) {
					Log.LogDebugMessage ($"MM: processing {entry.DeclaringType.FullName} {entry.NativeCallback.FullName}");
					ProcessAndAddMethod (allMethods, entry, useFullNativeSignature, seenClasses, overloadedNativeSymbolNames);
				}
			}

			foreach (List<MarshalMethodInfo> mmiList in overloadedNativeSymbolNames.Values) {
				if (mmiList.Count <= 1) {
					continue;
				}

				foreach (MarshalMethodInfo overloadedMethod in mmiList) {
					overloadedMethod.NativeSymbolName = MakeNativeSymbolName (overloadedMethod.Method, useFullNativeSignature: true);
				}
			}

			// In some cases it's possible that a single type implements two different interfaces which have methods with the same native signature:
			//
			//   Microsoft.Maui.Controls.Handlers.TabbedPageManager/Listeners
			//      System.Void AndroidX.ViewPager.Widget.ViewPager/IOnPageChangeListener::OnPageSelected(System.Int32)
			//      System.Void AndroidX.ViewPager2.Widget.ViewPager2/OnPageChangeCallback::OnPageSelected(System.Int32)
			//
			// Both of the above methods will have the same native implementation and symbol name. e.g. (Java type name being `crc649ff77a65592e7d55/TabbedPageManager_Listeners`):
			//     Java_crc649ff77a65592e7d55_TabbedPageManager_1Listeners_n_1onPageSelected__I
			//
			// We need to de-duplicate the entries or the generated native code will fail to build.
			var seenNativeSymbols = new HashSet<string> (StringComparer.Ordinal);
			methods = new List<MarshalMethodInfo> ();

			foreach (MarshalMethodInfo method in allMethods) {
				if (seenNativeSymbols.Contains (method.NativeSymbolName)) {
					Log.LogDebugMessage ($"Removed MM duplicate '{method.NativeSymbolName}' (implemented: {method.Method.ImplementedMethod.FullName}; registered: {method.Method.RegisteredMethod.FullName}");
					continue;
				}

				seenNativeSymbols.Add (method.NativeSymbolName);
				methods.Add (method);
			}
		}

		string MakeNativeSymbolName (MarshalMethodEntry entry, bool useFullNativeSignature)
		{
			var sb = new StringBuilder ("Java_");
			sb.Append (MangleForJni (entry.JniTypeName));
			sb.Append ('_');
			sb.Append (MangleForJni ($"n_{entry.JniMethodName}"));

			if (useFullNativeSignature) {
				string signature = entry.JniMethodSignature;
				if (signature.Length < 2) {
					ThrowInvalidSignature (signature, "must be at least two characters long");
				}

				if (signature[0] != '(') {
					ThrowInvalidSignature (signature, "must start with '('");
				}

				int sigEndIdx = signature.LastIndexOf (')');
				if (sigEndIdx < 1) { // the first position where ')' can appear is 1, for a method without parameters
					ThrowInvalidSignature (signature, "missing closing parenthesis");
				}

				string sigParams = signature.Substring (1, sigEndIdx - 1);
				if (sigParams.Length > 0) {
					sb.Append ("__");
					sb.Append (MangleForJni (sigParams));
				}
			}

			return sb.ToString ();

			void ThrowInvalidSignature (string signature, string reason)
			{
				throw new InvalidOperationException ($"Invalid JNI signature '{signature}': {reason}");
			}
		}

		void ProcessAndAddMethod (List<MarshalMethodInfo> allMethods, MarshalMethodEntry entry, bool useFullNativeSignature, Dictionary<string, int> seenClasses, Dictionary<string, List<MarshalMethodInfo>> overloadedNativeSymbolNames)
		{
			CecilMethodDefinition nativeCallback = entry.NativeCallback;
			string nativeSymbolName = MakeNativeSymbolName (entry, useFullNativeSignature);
			string klass = $"{nativeCallback.DeclaringType.FullName}, {nativeCallback.Module.Assembly.FullName}";

			if (!seenClasses.TryGetValue (klass, out int classIndex)) {
				classIndex = classes.Count;
				seenClasses.Add (klass, classIndex);

				var mc = new MarshalMethodsManagedClass {
					token = nativeCallback.DeclaringType.MetadataToken.ToUInt32 (),
					ClassName = klass,
				};

				classes.Add (new StructureInstance<MarshalMethodsManagedClass> (marshalMethodsManagedClassStructureInfo, mc));
			}

			// Methods with `IsSpecial == true` are "synthetic" methods - they contain only the callback reference
			(Type returnType, List<LlvmIrFunctionParameter>? parameters) = ParseJniSignature (entry.JniMethodSignature, entry.IsSpecial ? entry.NativeCallback : entry.ImplementedMethod);

			var method = new MarshalMethodInfo (entry, returnType, nativeSymbolName: nativeSymbolName, classIndex);
			if (parameters != null && parameters.Count > 0) {
				method.Parameters.AddRange (parameters);
			}

			if (!overloadedNativeSymbolNames.TryGetValue (method.NativeSymbolName, out List<MarshalMethodInfo> overloadedMethods)) {
				overloadedMethods = new List<MarshalMethodInfo> ();
				overloadedNativeSymbolNames.Add (method.NativeSymbolName, overloadedMethods);
			}
			overloadedMethods.Add (method);

			allMethods.Add (method);
		}

		string MangleForJni (string name)
		{
			var sb = new StringBuilder ();

			foreach (char ch in name) {
				switch (ch) {
					case '/':
					case '.':
						sb.Append ('_');
						break;

					case '_':
						sb.Append ("_1");
						break;

					case ';':
						sb.Append ("_2");
						break;

					case '[':
						sb.Append ("_3");
						break;

					default:
						if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) {
							sb.Append (ch);
						} else {
							sb.Append ("_0");
							sb.Append (((int)ch).ToString ("x04"));
						}
						break;
				}
			}

			return sb.ToString ();
		}

		(Type returnType, List<LlvmIrFunctionParameter>? functionParams) ParseJniSignature (string signature, Mono.Cecil.MethodDefinition implementedMethod)
		{
			Type returnType = null;
			List<LlvmIrFunctionParameter>? parameters = null;
			bool paramsDone = false;
			int idx = 0;
			while (!paramsDone && idx < signature.Length) {
				char jniType = signature[idx];

				if (jniType == '(') {
					idx++;
					continue;
				}

				if (jniType == ')') {
					paramsDone = true;
					continue;
				}

				Type? managedType = JniTypeToManaged (jniType);
				if (managedType != null) {
					AddParameter (managedType);
					continue;
				}

				throw new InvalidOperationException ($"Unsupported JNI type '{jniType}' at position {idx} of signature '{signature}'");
			}

			if (!paramsDone || idx >= signature.Length || signature[idx] != ')') {
				throw new InvalidOperationException ($"Missing closing arguments parenthesis: '{signature}'");
			}

			idx++;
			if (signature[idx] == 'V') {
				returnType = typeof(void);
			} else {
				returnType = JniTypeToManaged (signature[idx]);
			}

			return (returnType, parameters);

			Type? JniTypeToManaged (char jniType)
			{
				if (jniSimpleTypeMap.TryGetValue (jniType, out Type managedType)) {
					idx++;
					return managedType;
				}

				if (jniType == 'L') {
					return JavaClassToManaged (justSkip: false);
				}

				if (jniType == '[') {
					// Arrays of arrays (any rank) are bound as a simple pointer, which makes the generated code much simpler (no need to generate pointers to
					// pointers to pointers etc), especially that we don't need to dereference these pointers in generated code, we simply pass them along to
					// the managed land after all.
					while (signature[idx] == '[') {
						idx++;
					}

					jniType = signature[idx];
					if (jniArrayTypeMap.TryGetValue (jniType, out managedType)) {
						if (jniType == 'L') {
							JavaClassToManaged (justSkip: true);
						} else {
							idx++;
						}

						return managedType;
					}

					throw new InvalidOperationException ($"Unsupported JNI array type '{jniType}' at index {idx} of signature '{signature}'");
				}

				return null;
			}

			Type? JavaClassToManaged (bool justSkip)
			{
				idx++;
				StringBuilder sb = null;
				if (!justSkip) {
					sb = new StringBuilder ();
				}

				while (idx < signature.Length) {
					if (signature[idx] == ')') {
						throw new InvalidOperationException ($"Syntax error: unterminated class type (missing ';' before closing parenthesis) in signature '{signature}'");
					}

					if (signature[idx] == ';') {
						idx++;
						break;
					}

					sb?.Append (signature[idx]);
					idx++;
				}

				if (justSkip) {
					return null;
				}

				string typeName = sb.ToString ();
				if (String.Compare (typeName, "java/lang/Class", StringComparison.Ordinal) == 0) {
					return typeof(_jclass);
				}

				if (String.Compare (typeName, "java/lang/String", StringComparison.Ordinal) == 0) {
					return typeof(_jstring);
				}

				if (String.Compare (typeName, "java/lang/Throwable", StringComparison.Ordinal) == 0) {
					return typeof(_jthrowable);
				}

				return typeof(_jobject);
			}

			void AddParameter (Type type)
			{
				if (parameters == null) {
					parameters = new List<LlvmIrFunctionParameter> ();
				}

				if (implementedMethod.Parameters.Count <= parameters.Count) {
					throw new InvalidOperationException ($"Method {implementedMethod.FullName} managed signature doesn't match its JNI signature '{signature}' (not enough parameters)");
				}

				// Every parameter which isn't a primitive type becomes a pointer
				parameters.Add (new LlvmIrFunctionParameter (type, implementedMethod.Parameters[parameters.Count].Name));
			}
		}

		protected override void Construct (LlvmIrModule module)
		{
			MapStructures (module);

			Init ();
			if (tracingMode != MarshalMethodsTracingMode.None) {
				InitTracing (module);
			}

			AddAssemblyImageCache (module, out AssemblyCacheState acs);

			// class cache
			module.AddGlobalVariable ("marshal_methods_number_of_classes", (uint)classes.Count, LlvmIrVariableOptions.GlobalConstant);
			module.AddGlobalVariable ("marshal_methods_class_cache", classes, LlvmIrVariableOptions.GlobalWritable);

			// Marshal methods class names
			var mm_class_names = new List<string> ();
			foreach (StructureInstance<MarshalMethodsManagedClass> klass in classes) {
				mm_class_names.Add (klass.Instance.ClassName);
			}
			module.AddGlobalVariable ("mm_class_names", mm_class_names, LlvmIrVariableOptions.GlobalConstant, comment: " Names of classes in which marshal methods reside");

			AddMarshalMethodNames (module, acs);
			(LlvmIrVariable getFunctionPtrVariable, LlvmIrFunction getFunctionPtrFunction) = AddXamarinAppInitFunction (module);

			AddMarshalMethods (module, acs, getFunctionPtrVariable, getFunctionPtrFunction);
		}

		void MapStructures (LlvmIrModule module)
		{
			marshalMethodsManagedClassStructureInfo = module.MapStructure<MarshalMethodsManagedClass> ();
			marshalMethodNameStructureInfo = module.MapStructure<MarshalMethodName> ();
		}

		void AddMarshalMethods (LlvmIrModule module, AssemblyCacheState acs, LlvmIrVariable getFunctionPtrVariable, LlvmIrFunction getFunctionPtrFunction)
		{
			if (generateEmptyCode || methods == null || methods.Count == 0) {
				return;
			}

			// This will make all the backing fields to appear in a block without empty lines separating them.
			module.Add (
				new LlvmIrGroupDelimiterVariable () {
					Comment = " Marshal methods backing fields, pointers to native functions"
				}
			);

			var writeState = new MarshalMethodsWriteState {
				AssemblyCacheState = acs,
				AttributeSet = MakeMarshalMethodAttributeSet (module),
				UsedBackingFields = new Dictionary<string, LlvmIrVariable> (StringComparer.Ordinal),
				UniqueAssemblyId = new Dictionary<string, ulong> (StringComparer.OrdinalIgnoreCase),
				GetFunctionPtrVariable = getFunctionPtrVariable,
				GetFunctionPtrFunction = getFunctionPtrFunction,
			};
			foreach (MarshalMethodInfo mmi in methods) {
				CecilMethodDefinition nativeCallback = mmi.Method.NativeCallback;
				string asmName = nativeCallback.DeclaringType.Module.Assembly.Name.Name;

				if (!writeState.UniqueAssemblyId.TryGetValue (asmName, out ulong asmId)) {
					asmId = (ulong)writeState.UniqueAssemblyId.Count;
					writeState.UniqueAssemblyId.Add (asmName, asmId);
				}

				AddMarshalMethod (module, mmi, asmId, writeState);
			}

			module.Add (new LlvmIrGroupDelimiterVariable ());
		}

		void AddMarshalMethod (LlvmIrModule module, MarshalMethodInfo method, ulong asmId, MarshalMethodsWriteState writeState)
		{
			Log.LogDebugMessage ($"MM: generating code for {method.Method.DeclaringType.FullName} {method.Method.NativeCallback.FullName}");
			CecilMethodDefinition nativeCallback = method.Method.NativeCallback;
			string backingFieldName = $"native_cb_{method.Method.JniMethodName}_{asmId}_{method.ClassCacheIndex}_{nativeCallback.MetadataToken.ToUInt32():x}";

			if (!writeState.UsedBackingFields.TryGetValue (backingFieldName, out LlvmIrVariable backingField)) {
				backingField = module.AddGlobalVariable (typeof(IntPtr), backingFieldName, null, LlvmIrVariableOptions.LocalWritableInsignificantAddr);
				writeState.UsedBackingFields.Add (backingFieldName, backingField);
			}

			var funcComment = new StringBuilder (" Method: ");
			funcComment.AppendLine (nativeCallback.FullName);
			funcComment.Append (" Assembly: ");
			funcComment.AppendLine (nativeCallback.Module.Assembly.Name.FullName);
			funcComment.Append (" Registered: ");
			funcComment.AppendLine (method.Method.RegisteredMethod?.FullName ?? "none");
			funcComment.Append (" Implemented: ");
			funcComment.AppendLine (method.Method.ImplementedMethod?.FullName ?? "none");

			var func = new LlvmIrFunction (method.NativeSymbolName, method.ReturnType, method.Parameters, writeState.AttributeSet) {
				Comment = funcComment.ToString (),
			};

			WriteBody (func.Body);
			module.Add (func);

			void WriteBody (LlvmIrFunctionBody body)
			{
				WriteTracingAtFunctionTop (module, method, body, func, writeState);

				LlvmIrLocalVariable cb1 = func.CreateLocalVariable (typeof(IntPtr), "cb1");
				body.Load (backingField, cb1, tbaa: module.TbaaAnyPointer);

				LlvmIrLocalVariable isNullResult = func.CreateLocalVariable (typeof(bool), "isNull");
				body.Icmp (LlvmIrIcmpCond.Equal, cb1, null, isNullResult);

				var loadCallbackLabel = new LlvmIrFunctionLabelItem ("loadCallback");
				var callbackLoadedLabel = new LlvmIrFunctionLabelItem ("callbackLoaded");
				body.Br (isNullResult, loadCallbackLabel, callbackLoadedLabel);

				// Callback variable was null
				body.Add (loadCallbackLabel);

				LlvmIrLocalVariable getFuncPtrResult = func.CreateLocalVariable (typeof(IntPtr), "get_func_ptr");
				body.Load (writeState.GetFunctionPtrVariable, getFuncPtrResult, tbaa: module.TbaaAnyPointer);

				var placeholder = new MarshalMethodAssemblyIndexValuePlaceholder (method, writeState.AssemblyCacheState);
				LlvmIrInstructions.Call call = body.Call (
					writeState.GetFunctionPtrFunction,
					arguments: new List<object?> { placeholder, method.ClassCacheIndex, nativeCallback.MetadataToken.ToUInt32 (), backingField },
					funcPointer: getFuncPtrResult
				);

				LlvmIrLocalVariable cb2 = func.CreateLocalVariable (typeof(IntPtr), "cb2");
				body.Load (backingField, cb2, tbaa: module.TbaaAnyPointer);
				body.Br (callbackLoadedLabel);

				// Callback variable has just been set or it wasn't null
				body.Add (callbackLoadedLabel);
				LlvmIrLocalVariable fn = func.CreateLocalVariable (typeof(IntPtr), "fn");

				// Preceding blocks are ordered from the newest to the oldest, so we need to pass the variables referring to our callback in "reverse" order
				body.Phi (fn, cb2, body.PrecedingBlock1, cb1, body.PrecedingBlock2);

				var nativeFunc = new LlvmIrFunction (method.NativeSymbolName, method.ReturnType, method.Parameters);
				nativeFunc.Signature.ReturnAttributes.NoUndef = true;

				var arguments = new List<object?> ();
				foreach (LlvmIrFunctionParameter parameter in nativeFunc.Signature.Parameters) {
					arguments.Add (new LlvmIrLocalVariable (parameter.Type, parameter.Name));
				}
				LlvmIrLocalVariable? result = nativeFunc.ReturnsValue ? func.CreateLocalVariable (nativeFunc.Signature.ReturnType) : null;
				call = body.Call (nativeFunc, result, arguments, funcPointer: fn);
				call.CallMarker = LlvmIrCallMarker.Tail;

				body.Ret (nativeFunc.Signature.ReturnType, result);
			}
		}

		LlvmIrFunctionAttributeSet MakeMarshalMethodAttributeSet (LlvmIrModule module)
		{
			var attrSet = new LlvmIrFunctionAttributeSet {
				new MustprogressFunctionAttribute (),
				new UwtableFunctionAttribute (),
				new MinLegalVectorWidthFunctionAttribute (0),
				new NoTrappingMathFunctionAttribute (true),
				new StackProtectorBufferSizeFunctionAttribute (8),
			};

			return module.AddAttributeSet (attrSet);
		}

		(LlvmIrVariable getFuncPtrVariable, LlvmIrFunction getFuncPtrFunction) AddXamarinAppInitFunction (LlvmIrModule module)
		{
			var getFunctionPtrParams = new List<LlvmIrFunctionParameter> {
				new (typeof(uint), "mono_image_index") {
					NoUndef = true,
				},
				new (typeof(uint), "class_index") {
					NoUndef = true,
				},
				new (typeof(uint), "method_token") {
					NoUndef = true,
				},
				new (typeof(IntPtr), "target_ptr") {
					NoUndef = true,
					NonNull = true,
					Align = 0, // 0 means use natural pointer alignment
					Dereferenceable = 0, // ditto 👆
					IsCplusPlusReference = true,
				},
			};

			var getFunctionPtrComment = new StringBuilder (" ");
			getFunctionPtrComment.Append (GetFunctionPointerVariableName);
			getFunctionPtrComment.Append (" (");
			for (int i = 0; i < getFunctionPtrParams.Count; i++) {
				if (i > 0) {
					getFunctionPtrComment.Append (", ");
				}
				LlvmIrFunctionParameter parameter = getFunctionPtrParams[i];
				getFunctionPtrComment.Append (LlvmIrGenerator.MapManagedTypeToNative (parameter.Type));
				if (parameter.IsCplusPlusReference.HasValue && parameter.IsCplusPlusReference.Value) {
				 	getFunctionPtrComment.Append ('&');
				}
				getFunctionPtrComment.Append (' ');
				getFunctionPtrComment.Append (parameter.Name);
			}
			getFunctionPtrComment.Append (')');

			LlvmIrFunction getFunctionPtrFunc = new LlvmIrFunction (
				name: GetFunctionPointerVariableName,
				returnType: typeof(void),
				parameters: getFunctionPtrParams
			);

			LlvmIrVariable getFunctionPtrVariable = module.AddGlobalVariable (
				typeof(IntPtr),
				GetFunctionPointerVariableName,
				null,
				LlvmIrVariableOptions.LocalWritableInsignificantAddr,
				getFunctionPtrComment.ToString ()
			);

			var init_params = new List<LlvmIrFunctionParameter> {
				new (typeof(_JNIEnv), "env") {
					NoCapture = true,
					NoUndef = true,
					ReadNone = true,
				},
				new (typeof(IntPtr), "fn") {
					NoUndef = true,
				},
			};

			var init_signature = new LlvmIrFunctionSignature (
				name: "xamarin_app_init",
				returnType: typeof(void),
				parameters: init_params
			);

			LlvmIrFunctionAttributeSet attrSet = MakeXamarinAppInitAttributeSet (module);
			var xamarin_app_init = new LlvmIrFunction (init_signature, attrSet);

			// If `fn` is nullptr, print a message and abort...
			//
			// We must allocate result variables for both the null comparison and puts call here and with names, because
			// labels and local unnamed variables must be numbered sequentially otherwise and the `AddIfThenElse` call will
			// allocate up to 3 labels which would have been **defined** after these labels, but **used** before them - and
			// thus the numbering sequence would be out of order and the .ll file wouldn't build.
			var fnNullResult = xamarin_app_init.CreateLocalVariable (typeof(bool), "fnIsNull");
			LlvmIrVariable putsResult = xamarin_app_init.CreateLocalVariable (typeof(int), "putsResult");
			var ifThenInstructions = new List<LlvmIrInstruction> {
				module.CreatePuts ("get_function_pointer MUST be specified\n", putsResult),
				module.CreateAbort (),
				new LlvmIrInstructions.Unreachable (),
			};

			module.AddIfThenElse (xamarin_app_init, fnNullResult, LlvmIrIcmpCond.Equal, init_params[1], null, ifThenInstructions);

			// ...otherwise store the pointer and return
			xamarin_app_init.Body.Store (init_params[1], getFunctionPtrVariable, module.TbaaAnyPointer);
			xamarin_app_init.Body.Ret (typeof(void));

			module.Add (xamarin_app_init);

			return (getFunctionPtrVariable, getFunctionPtrFunc);
		}

		LlvmIrFunctionAttributeSet MakeXamarinAppInitAttributeSet (LlvmIrModule module)
		{
			var attrSet = new LlvmIrFunctionAttributeSet {
				new MustprogressFunctionAttribute (),
				new NofreeFunctionAttribute (),
				new NorecurseFunctionAttribute (),
				new NosyncFunctionAttribute (),
				new NounwindFunctionAttribute (),
				new WillreturnFunctionAttribute (),
				new MemoryFunctionAttribute {
					Default = MemoryAttributeAccessKind.Write,
					Argmem = MemoryAttributeAccessKind.None,
					InaccessibleMem = MemoryAttributeAccessKind.None,
				},
				new UwtableFunctionAttribute (),
				new MinLegalVectorWidthFunctionAttribute (0),
				new NoTrappingMathFunctionAttribute (true),
				new StackProtectorBufferSizeFunctionAttribute (8),
			};

			return module.AddAttributeSet (attrSet);
		}

		void AddMarshalMethodNames (LlvmIrModule module, AssemblyCacheState acs)
		{
			var uniqueMethods = new Dictionary<ulong, (MarshalMethodInfo mmi, ulong id32, ulong id64)> ();

			if (!generateEmptyCode && methods != null) {
				foreach (MarshalMethodInfo mmi in methods) {
					string asmName = Path.GetFileName (mmi.Method.NativeCallback.Module.Assembly.MainModule.FileName);

					if (!acs.AsmNameToIndexData32.TryGetValue (asmName, out uint idx32)) {
						throw new InvalidOperationException ($"Internal error: failed to match assembly name '{asmName}' to 32-bit cache array index");
					}

					if (!acs.AsmNameToIndexData64.TryGetValue (asmName, out uint idx64)) {
						throw new InvalidOperationException ($"Internal error: failed to match assembly name '{asmName}' to 64-bit cache array index");
					}

					ulong methodToken = (ulong)mmi.Method.NativeCallback.MetadataToken.ToUInt32 ();
					ulong id32 = ((ulong)idx32 << 32) | methodToken;
					if (uniqueMethods.ContainsKey (id32)) {
						continue;
					}

					ulong id64 = ((ulong)idx64 << 32) | methodToken;
					uniqueMethods.Add (id32, (mmi, id32, id64));
				}
			}

			MarshalMethodName name;
			var methodName = new StringBuilder ();
			var mm_method_names = new List<StructureInstance<MarshalMethodName>> ();
			foreach (var kvp in uniqueMethods) {
				ulong id = kvp.Key;
				(MarshalMethodInfo mmi, ulong id32, ulong id64) = kvp.Value;

				RenderMethodNameWithParams (mmi.Method.NativeCallback, methodName);
				name = new MarshalMethodName {
					Id32 = id32,
					Id64 = id64,

					// Tokens are unique per assembly
					id = 0,
					name = methodName.ToString (),
				};
				mm_method_names.Add (new StructureInstance<MarshalMethodName> (marshalMethodNameStructureInfo, name));
			}

			// Must terminate with an "invalid" entry
			name = new MarshalMethodName {
				Id32 = 0,
				Id64 = 0,

				id = 0,
				name = String.Empty,
			};
			mm_method_names.Add (new StructureInstance<MarshalMethodName> (marshalMethodNameStructureInfo, name));

			var mm_method_names_variable = new LlvmIrGlobalVariable (mm_method_names, "mm_method_names", LlvmIrVariableOptions.GlobalConstant) {
				BeforeWriteCallback = UpdateMarshalMethodNameIds,
				BeforeWriteCallbackCallerState = acs,
			};
			module.Add (mm_method_names_variable);

			void RenderMethodNameWithParams (CecilMethodDefinition md, StringBuilder buffer)
			{
				buffer.Clear ();
				buffer.Append (md.Name);
				buffer.Append ('(');

				if (md.HasParameters) {
					bool first = true;
					foreach (CecilParameterDefinition pd in md.Parameters) {
						if (!first) {
							buffer.Append (',');
						} else {
							first = false;
						}

						buffer.Append (pd.ParameterType.Name);
					}
				}

				buffer.Append (')');
			}
		}

		void UpdateMarshalMethodNameIds (LlvmIrVariable variable, LlvmIrModuleTarget target, object? callerState)
		{
			var mm_method_names = (List<StructureInstance<MarshalMethodName>>)variable.Value;
			bool is64Bit = target.Is64Bit;

			foreach (StructureInstance<MarshalMethodName> mmn in mm_method_names) {
				mmn.Instance.id = is64Bit ? mmn.Instance.Id64 : mmn.Instance.Id32;
			}
		}

		// TODO: this should probably be moved to a separate writer, since not only marshal methods use the cache
		void AddAssemblyImageCache (LlvmIrModule module, out AssemblyCacheState acs)
		{
			var assembly_image_cache = new LlvmIrGlobalVariable (typeof(List<IntPtr>), "assembly_image_cache", LlvmIrVariableOptions.GlobalWritable) {
				ZeroInitializeArray = true,
				ArrayItemCount = (ulong)numberOfAssembliesInApk,
			};
			module.Add (assembly_image_cache);

			acs = new AssemblyCacheState {
				AsmNameToIndexData32 = new Dictionary<string, uint> (StringComparer.Ordinal),
				Indices32 = new List<uint> (),

				AsmNameToIndexData64 = new Dictionary<string, uint> (StringComparer.Ordinal),
				Indices64 = new List<uint> (),
			};

			acs.Hashes32 = new Dictionary<uint, (string name, uint index)> ();
			acs.Hashes64 = new Dictionary<ulong, (string name, uint index)> ();
			uint index = 0;

			foreach (string name in uniqueAssemblyNames) {
				// We must make sure we keep the possible culture prefix, which will be treated as "directory" path here
				string cultureName = Path.GetDirectoryName (name) ?? String.Empty;
				string clippedName = Path.Combine (cultureName, Path.GetFileNameWithoutExtension (name));
				string inArchiveName;

				if (cultureName.Length == 0) {
					// Regular assemblies get the 'lib_' prefix
					inArchiveName = $"{MonoAndroidHelper.MANGLED_ASSEMBLY_REGULAR_ASSEMBLY_MARKER}{name}{MonoAndroidHelper.MANGLED_ASSEMBLY_NAME_EXT}";
				} else {
					// Satellite assemblies get the 'lib-{CULTURE}-' prefix
					inArchiveName = $"{MonoAndroidHelper.MANGLED_ASSEMBLY_SATELLITE_ASSEMBLY_MARKER}{cultureName}-{Path.GetFileName (name)}{MonoAndroidHelper.MANGLED_ASSEMBLY_NAME_EXT}";
				}

				ulong hashFull32 = MonoAndroidHelper.GetXxHash (name, is64Bit: false);
				ulong hashInArchive32 = MonoAndroidHelper.GetXxHash (inArchiveName, is64Bit: false);
				ulong hashClipped32 = MonoAndroidHelper.GetXxHash (clippedName, is64Bit: false);

				ulong hashFull64 = MonoAndroidHelper.GetXxHash (name, is64Bit: true);
				ulong hashInArchive64 = MonoAndroidHelper.GetXxHash (inArchiveName, is64Bit: true);
				ulong hashClipped64 = MonoAndroidHelper.GetXxHash (clippedName, is64Bit: true);

				//
				// If the number of name forms changes, xamarin-app.hh MUST be updated to set value of the
				// `number_of_assembly_name_forms_in_image_cache` constant to the number of forms.
				//
				acs.Hashes32.Add ((uint)Convert.ChangeType (hashFull32, typeof(uint)), (name, index));
				acs.Hashes32.Add ((uint)Convert.ChangeType (hashInArchive32, typeof(uint)), (inArchiveName, index));
				acs.Hashes32.Add ((uint)Convert.ChangeType (hashClipped32, typeof(uint)), (clippedName, index));

				acs.Hashes64.Add (hashFull64, (name, index));
				acs.Hashes64.Add (hashInArchive64, (inArchiveName, index));
				acs.Hashes64.Add (hashClipped64, (clippedName, index));

				index++;
			}

			acs.Keys32 = acs.Hashes32.Keys.ToList ();
			acs.Keys32.Sort ();
			for (int i = 0; i < acs.Keys32.Count; i++) {
				(string name, uint idx) = acs.Hashes32[acs.Keys32[i]];
				acs.Indices32.Add (idx);
				acs.AsmNameToIndexData32.Add (name, idx);
			}

			acs.Keys64 = acs.Hashes64.Keys.ToList ();
			acs.Keys64.Sort ();
			for (int i = 0; i < acs.Keys64.Count; i++) {
				(string name, uint idx) = acs.Hashes64[acs.Keys64[i]];
				acs.Indices64.Add (idx);
				acs.AsmNameToIndexData64.Add (name, idx);
			}

			var assembly_image_cache_hashes = new LlvmIrGlobalVariable (typeof(List<ulong>), "assembly_image_cache_hashes", LlvmIrVariableOptions.GlobalConstant) {
				Comment = " Each entry maps hash of an assembly name to an index into the `assembly_image_cache` array",
				BeforeWriteCallback = UpdateAssemblyImageCacheHashes,
				BeforeWriteCallbackCallerState = acs,
				GetArrayItemCommentCallback = GetAssemblyImageCacheItemComment,
				GetArrayItemCommentCallbackCallerState = acs,
				NumberFormat = LlvmIrVariableNumberFormat.Hexadecimal,
			};
			module.Add (assembly_image_cache_hashes);

			var assembly_image_cache_indices = new LlvmIrGlobalVariable (typeof(List<uint>), "assembly_image_cache_indices", LlvmIrVariableOptions.GlobalConstant) {
				WriteOptions = LlvmIrVariableWriteOptions.ArrayWriteIndexComments | LlvmIrVariableWriteOptions.ArrayFormatInRows,
				BeforeWriteCallback = UpdateAssemblyImageCacheIndices,
				BeforeWriteCallbackCallerState = acs,
			};
			module.Add (assembly_image_cache_indices);
		}

		void UpdateAssemblyImageCacheHashes (LlvmIrVariable variable, LlvmIrModuleTarget target, object? callerState)
		{
			AssemblyCacheState acs = EnsureAssemblyCacheState (callerState);
			object value;
			Type type;

			if (target.Is64Bit) {
				value = acs.Keys64;
				type = typeof(List<ulong>);
			} else {
				value = acs.Keys32;
				type = typeof(List<uint>);
			}

			LlvmIrGlobalVariable gv = EnsureGlobalVariable (variable);
			gv.OverrideTypeAndValue (type, value);
		}

		string? GetAssemblyImageCacheItemComment (LlvmIrVariable v, LlvmIrModuleTarget target, ulong index, object? value, object? callerState)
		{
			AssemblyCacheState acs = EnsureAssemblyCacheState (callerState);

			string name;
			uint i;
			if (target.Is64Bit) {
				var v64 = (ulong)value;
				name = acs.Hashes64[v64].name;
				i = acs.Hashes64[v64].index;
			} else {
				var v32 = (uint)value;
				name = acs.Hashes32[v32].name;
				i = acs.Hashes32[v32].index;
			}

			return $" {index}: {name} => {i}";
		}

		void UpdateAssemblyImageCacheIndices (LlvmIrVariable variable, LlvmIrModuleTarget target, object? callerState)
		{
			AssemblyCacheState acs = EnsureAssemblyCacheState (callerState);
			object value;

			if (target.Is64Bit) {
				value = acs.Indices64;
			} else {
				value = acs.Indices32;
			}

			LlvmIrGlobalVariable gv = EnsureGlobalVariable (variable);
			gv.OverrideTypeAndValue (variable.Type, value);
		}

		AssemblyCacheState EnsureAssemblyCacheState (object? callerState)
		{
			var acs = callerState as AssemblyCacheState;
			if (acs == null) {
				throw new InvalidOperationException ("Internal error: construction state expected but not found");
			}

			return acs;
		}
	}
}
