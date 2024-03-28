using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Reflection;

using Java.Interop;
using Java.Interop.Tools.TypeNameMappings;
using System.Diagnostics.CodeAnalysis;

#if JAVA_INTEROP
namespace Android.Runtime {

	class AndroidRuntime : JniRuntime {

		public const string InternalDllName = RuntimeConstants.InternalDllName;

		internal AndroidRuntime (IntPtr jnienv,
				IntPtr vm,
				bool allocNewObjectSupported,
				IntPtr classLoader,
				IntPtr classLoader_loadClass,
				bool jniAddNativeMethodRegistrationAttributePresent)
			: base (new AndroidRuntimeOptions (jnienv,
					vm,
					allocNewObjectSupported,
					classLoader,
					classLoader_loadClass,
					jniAddNativeMethodRegistrationAttributePresent))
		{
			// This is not ideal, but we need to set this while the runtime is initializing but we can't do it directly from the `JNIEnvInit.Initialize` method, since
			// it lives in an assembly that does not reference Mono.Android.  So we do it here, because this class is instantiated by JNIEnvInit.Initialize.
			AndroidEnvironmentInternal.UnhandledExceptionHandler = AndroidEnvironment.UnhandledException;
		}

		public override void FailFast (string? message)
		{
			AndroidEnvironment.FailFast (message);
		}

		public override string GetCurrentManagedThreadName ()
		{
			return Thread.CurrentThread.Name!;
		}

		public override string GetCurrentManagedThreadStackTrace (int skipFrames, bool fNeedFileInfo)
		{
			return new StackTrace (skipFrames, fNeedFileInfo)
				.ToString ();
		}

		public override Exception? GetExceptionForThrowable (ref JniObjectReference reference, JniObjectReferenceOptions options)
		{
			if (!reference.IsValid)
				return null;
			var peeked      = JNIEnvInit.AndroidValueManager?.PeekPeer (reference);
			var peekedExc   = peeked as Exception;
			if (peekedExc == null) {
				var throwable = Java.Lang.Object.GetObject<Java.Lang.Throwable> (reference.Handle, JniHandleOwnership.DoNotTransfer);
				JniObjectReference.Dispose (ref reference, options);
				return throwable;
			}
			JniObjectReference.Dispose (ref reference, options);
			var unwrapped = JNIEnvInit.AndroidValueManager?.UnboxException (peeked!);
			if (unwrapped != null) {
				return unwrapped;
			}
			return peekedExc;
		}

		public override void RaisePendingException (Exception pendingException)
		{
			var je  = pendingException as JavaProxyThrowable;
			if (je == null) {
				je  = JavaProxyThrowable.Create (pendingException);
			}
			var r = new JniObjectReference (je.Handle);
			JniEnvironment.Exceptions.Throw (r);
		}
	}

	class AndroidRuntimeOptions : JniRuntime.CreationOptions {
		public AndroidRuntimeOptions (IntPtr jnienv,
				IntPtr vm,
				bool allocNewObjectSupported,
				IntPtr classLoader,
				IntPtr classLoader_loadClass,
				bool jniAddNativeMethodRegistrationAttributePresent)
		{
			EnvironmentPointer      = jnienv;
			ClassLoader             = new JniObjectReference (classLoader, JniObjectReferenceType.Global);
			ClassLoader_LoadClass_id= classLoader_loadClass;
			InvocationPointer       = vm;
			NewObjectRequired       = !allocNewObjectSupported;
			ObjectReferenceManager  = new AndroidObjectReferenceManager ();
			TypeManager             = new AndroidTypeManager (jniAddNativeMethodRegistrationAttributePresent);
			ValueManager            = new AndroidValueManager ();
			UseMarshalMemberBuilder = false;
			JniAddNativeMethodRegistrationAttributePresent = jniAddNativeMethodRegistrationAttributePresent;
		}
	}

	class AndroidObjectReferenceManager : JniRuntime.JniObjectReferenceManager {
		public override int GlobalReferenceCount {
			get {return RuntimeNativeMethods._monodroid_gref_get ();}
		}

		public override int WeakGlobalReferenceCount {
			get {return RuntimeNativeMethods._monodroid_weak_gref_get ();}
		}

		public override JniObjectReference CreateLocalReference (JniObjectReference value, ref int localReferenceCount)
		{
			var r = base.CreateLocalReference (value, ref localReferenceCount);

			if (Logger.LogLocalRef) {
				var tname = Thread.CurrentThread.Name;
				var tid   = Thread.CurrentThread.ManagedThreadId;;
				var from  = new StringBuilder (new StackTrace (true).ToString ());
				RuntimeNativeMethods._monodroid_lref_log_new (localReferenceCount, r.Handle, (byte) 'L', tname, tid, from, 1);
			}

			return r;
		}

		public override void DeleteLocalReference (ref JniObjectReference value, ref int localReferenceCount)
		{
			if (Logger.LogLocalRef) {
				var tname = Thread.CurrentThread.Name;
				var tid   = Thread.CurrentThread.ManagedThreadId;;
				var from  = new StringBuilder (new StackTrace (true).ToString ());
				RuntimeNativeMethods._monodroid_lref_log_delete (localReferenceCount-1, value.Handle, (byte) 'L', tname, tid, from, 1);
			}
			base.DeleteLocalReference (ref value, ref localReferenceCount);
		}

		public override void CreatedLocalReference (JniObjectReference value, ref int localReferenceCount)
		{
			base.CreatedLocalReference (value, ref localReferenceCount);
			if (Logger.LogLocalRef) {
				var tname = Thread.CurrentThread.Name;
				var tid   = Thread.CurrentThread.ManagedThreadId;;
				var from  = new StringBuilder (new StackTrace (true).ToString ());
				RuntimeNativeMethods._monodroid_lref_log_new (localReferenceCount, value.Handle, (byte) 'L', tname, tid, from, 1);
			}
		}

		public override IntPtr ReleaseLocalReference (ref JniObjectReference value, ref int localReferenceCount)
		{
			var r = base.ReleaseLocalReference (ref value, ref localReferenceCount);
			if (Logger.LogLocalRef) {
				var tname = Thread.CurrentThread.Name;
				var tid   = Thread.CurrentThread.ManagedThreadId;;
				var from  = new StringBuilder (new StackTrace (true).ToString ());
				RuntimeNativeMethods._monodroid_lref_log_delete (localReferenceCount-1, value.Handle, (byte) 'L', tname, tid, from, 1);
			}
			return r;
		}

		public override void WriteGlobalReferenceLine (string format, params object?[] args)
		{
			RuntimeNativeMethods._monodroid_gref_log (string.Format (CultureInfo.InvariantCulture, format, args));
		}

		public override JniObjectReference CreateGlobalReference (JniObjectReference value)
		{
			var r     = base.CreateGlobalReference (value);

			var log		= Logger.LogGlobalRef;
			var ctype	= log ? GetObjectRefType (value.Type) : (byte) '*';
			var ntype	= log ? GetObjectRefType (r.Type) : (byte) '*';
			var tname = log ? Thread.CurrentThread.Name : null;
			var tid   = log ? Thread.CurrentThread.ManagedThreadId : 0;
			var from  = log ? new StringBuilder (new StackTrace (true).ToString ()) : null;
			int gc 		= RuntimeNativeMethods._monodroid_gref_log_new (value.Handle, ctype, r.Handle, ntype, tname, tid, from, 1);
			if (gc >= JNIEnvInit.gref_gc_threshold) {
				Logger.Log (LogLevel.Info, "monodroid-gc", gc + " outstanding GREFs. Performing a full GC!");
				System.GC.Collect ();
			}

			return r;
		}

		static byte GetObjectRefType (JniObjectReferenceType type)
		{
			switch (type) {
				case JniObjectReferenceType.Invalid:	    return (byte) 'I';
				case JniObjectReferenceType.Local:        return (byte) 'L';
				case JniObjectReferenceType.Global:       return (byte) 'G';
				case JniObjectReferenceType.WeakGlobal:   return (byte) 'W';
				default:                                  return (byte) '*';
			}
		}

		public override void DeleteGlobalReference (ref JniObjectReference value)
		{
			var log		= Logger.LogGlobalRef;
			var ctype	= log ? GetObjectRefType (value.Type) : (byte) '*';
			var tname = log ? Thread.CurrentThread.Name : null;
			var tid   = log ? Thread.CurrentThread.ManagedThreadId : 0;
			var from  = log ? new StringBuilder (new StackTrace (true).ToString ()) : null;
			RuntimeNativeMethods._monodroid_gref_log_delete (value.Handle, ctype, tname, tid, from, 1);

			base.DeleteGlobalReference (ref value);
		}

		public override JniObjectReference CreateWeakGlobalReference (JniObjectReference value)
		{
			var r = base.CreateWeakGlobalReference (value);

			var log		= Logger.LogGlobalRef;
			var ctype	= log ? GetObjectRefType (value.Type) : (byte) '*';
			var ntype	= log ? GetObjectRefType (r.Type) : (byte) '*';
			var tname = log ? Thread.CurrentThread.Name : null;
			var tid   = log ? Thread.CurrentThread.ManagedThreadId : 0;
			var from  = log ? new StringBuilder (new StackTrace (true).ToString ()) : null;
			RuntimeNativeMethods._monodroid_weak_gref_new (value.Handle, ctype, r.Handle, ntype, tname, tid, from, 1);

			return r;
		}

		public override void DeleteWeakGlobalReference (ref JniObjectReference value)
		{
			var log		= Logger.LogGlobalRef;
			var ctype	= log ? GetObjectRefType (value.Type) : (byte) '*';
			var tname = log ? Thread.CurrentThread.Name : null;
			var tid   = log ? Thread.CurrentThread.ManagedThreadId : 0;
			var from  = log ? new StringBuilder (new StackTrace (true).ToString ()) : null;
			RuntimeNativeMethods._monodroid_weak_gref_delete (value.Handle, ctype, tname, tid, from, 1);

			base.DeleteWeakGlobalReference (ref value);
		}
	}

	class AndroidTypeManager : JniRuntime.JniTypeManager {
		struct JniRemappingReplacementMethod
		{
			public string target_type;
			public string target_name;
			public bool     is_static;
		};

		bool jniAddNativeMethodRegistrationAttributePresent;

		const DynamicallyAccessedMemberTypes Methods = DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods;
		const DynamicallyAccessedMemberTypes MethodsAndPrivateNested = Methods | DynamicallyAccessedMemberTypes.NonPublicNestedTypes;

		public AndroidTypeManager (bool jniAddNativeMethodRegistrationAttributePresent)
		{
			this.jniAddNativeMethodRegistrationAttributePresent = jniAddNativeMethodRegistrationAttributePresent;
		}

		protected override IEnumerable<Type> GetTypesForSimpleReference (string jniSimpleReference)
		{
			foreach (var ti in base.GetTypesForSimpleReference (jniSimpleReference))
				yield return ti;

			var t = Java.Interop.TypeManager.GetJavaToManagedType (jniSimpleReference);
			if (t != null)
				yield return t;
		}

		protected override string? GetSimpleReference (Type type)
		{
			string? j = JNIEnv.TypemapManagedToJava (type);
			if (j != null) {
				return GetReplacementTypeCore (j) ?? j;
			}
			if (JNIEnvInit.IsRunningOnDesktop) {
				return JavaNativeTypeManager.ToJniName (type);
			}
			return null;
		}

		protected override IEnumerable<string> GetSimpleReferences (Type type)
		{
			string? j = JNIEnv.TypemapManagedToJava (type);
			j   = GetReplacementTypeCore (j) ?? j;

			if (JNIEnvInit.IsRunningOnDesktop) {
				string? d = JavaNativeTypeManager.ToJniName (type);
				if (j != null && d != null) {
					return new[]{j, d};
				}
				if (d != null) {
					return new[]{d};
				}
			}
			if (j != null) {
				return new[]{j};
			}
			return Array.Empty<string> ();
		}

		protected override IReadOnlyList<string>? GetStaticMethodFallbackTypesCore (string jniSimpleReference)
		{
			ReadOnlySpan<char>  name    = jniSimpleReference;
			int slash                   = name.LastIndexOf ('/');
			var desugarType             = new StringBuilder (jniSimpleReference.Length + "Desugar".Length);
			if (slash > 0) {
				desugarType.Append (name.Slice (0, slash+1))
					.Append ("Desugar")
					.Append (name.Slice (slash+1));
			} else {
				desugarType.Append ("Desugar").Append (name);
			}

			var typeWithPrefix  = desugarType.ToString ();
			var typeWithSuffix  = $"{jniSimpleReference}$-CC";

			var replacements    = new[]{
				GetReplacementTypeCore (typeWithPrefix) ?? typeWithPrefix,
				GetReplacementTypeCore (typeWithSuffix) ?? typeWithSuffix,
			};

			if (Logger.LogAssembly) {
				var message = $"Remapping type `{jniSimpleReference}` to one one of {{ `{replacements[0]}`, `{replacements[1]}` }}";
				Logger.Log (LogLevel.Debug, "monodroid-assembly", message);
			}
			return replacements;
		}

		protected override string? GetReplacementTypeCore (string jniSimpleReference)
		{
			if (!JNIEnvInit.jniRemappingInUse) {
				return null;
			}

			IntPtr ret = RuntimeNativeMethods._monodroid_lookup_replacement_type (jniSimpleReference);
			if (ret == IntPtr.Zero) {
				return null;
			}

			return Marshal.PtrToStringAnsi (ret);
		}

		protected override JniRuntime.ReplacementMethodInfo? GetReplacementMethodInfoCore (string jniSourceType, string jniMethodName, string jniMethodSignature)
		{
			if (!JNIEnvInit.jniRemappingInUse) {
				return null;
			}

			IntPtr retInfo = RuntimeNativeMethods._monodroid_lookup_replacement_method_info (jniSourceType, jniMethodName, jniMethodSignature);
			if (retInfo == IntPtr.Zero) {
				return null;
			}

			var method = new JniRemappingReplacementMethod ();
			method = Marshal.PtrToStructure<JniRemappingReplacementMethod>(retInfo);
			var newSignature = jniMethodSignature;

			int? paramCount = null;
			if (method.is_static) {
				paramCount = JniMemberSignature.GetParameterCountFromMethodSignature (jniMethodSignature) + 1;
				newSignature = $"(L{jniSourceType};" + jniMethodSignature.Substring ("(".Length);
			}

			if (Logger.LogAssembly) {
				var message = $"Remapping method `{jniSourceType}.{jniMethodName}{jniMethodSignature}` to " +
					$"`{method.target_type}.{method.target_name}{newSignature}`; " +
					$"param-count: {paramCount}; instance-to-static? {method.is_static}";
				Logger.Log (LogLevel.Debug, "monodroid-assembly", message);
			}

			return new JniRuntime.ReplacementMethodInfo {
					SourceJniType                   = jniSourceType,
					SourceJniMethodName             = jniMethodName,
					SourceJniMethodSignature        = jniMethodSignature,
					TargetJniType                   = method.target_type,
					TargetJniMethodName             = method.target_name,
					TargetJniMethodSignature        = newSignature,
					TargetJniMethodParameterCount   = paramCount,
					TargetJniMethodInstanceToStatic = method.is_static,
			};
		}

		delegate Delegate GetCallbackHandler ();

		static MethodInfo? dynamic_callback_gen;

		// See ExportAttribute.cs
		[UnconditionalSuppressMessage ("Trimming", "IL2026", Justification = "Mono.Android.Export.dll is preserved when [Export] is used via [DynamicDependency].")]
		[UnconditionalSuppressMessage ("Trimming", "IL2075", Justification = "Mono.Android.Export.dll is preserved when [Export] is used via [DynamicDependency].")]
		static Delegate CreateDynamicCallback (MethodInfo method)
		{
			if (dynamic_callback_gen == null) {
				var assembly = Assembly.Load ("Mono.Android.Export");
				if (assembly == null)
					throw new InvalidOperationException ("To use methods marked with ExportAttribute, Mono.Android.Export.dll needs to be referenced in the application");
				var type = assembly.GetType ("Java.Interop.DynamicCallbackCodeGenerator");
				if (type == null)
					throw new InvalidOperationException ("The referenced Mono.Android.Export.dll does not match the expected version. The required type was not found.");
				dynamic_callback_gen = type.GetMethod ("Create");
				if (dynamic_callback_gen == null)
					throw new InvalidOperationException ("The referenced Mono.Android.Export.dll does not match the expected version. The required method was not found.");
			}
			return (Delegate)dynamic_callback_gen.Invoke (null, new object [] { method })!;
		}

		static List<JniNativeMethodRegistration> sharedRegistrations = new List<JniNativeMethodRegistration> ();

		static bool FastRegisterNativeMembers (JniType nativeClass, Type type, ReadOnlySpan<char> methods)
		{
			if (!MagicRegistrationMap.Filled)
				return false;

			bool lockTaken = false;
			bool rv = false;

			try {
				Monitor.TryEnter (sharedRegistrations, ref lockTaken);
				List<JniNativeMethodRegistration> registrations;
				if (lockTaken) {
					sharedRegistrations.Clear ();
					registrations = sharedRegistrations;
				} else {
					registrations = new List<JniNativeMethodRegistration> ();
				}
				JniNativeMethodRegistrationArguments arguments = new JniNativeMethodRegistrationArguments (registrations, methods.ToString ());
				rv = MagicRegistrationMap.CallRegisterMethod (arguments, type.FullName!);

				if (registrations.Count > 0)
					nativeClass.RegisterNativeMethods (registrations.ToArray ());
			} finally {
				if (lockTaken) {
					Monitor.Exit (sharedRegistrations);
				}
			}

			return rv;
		}

		class MagicRegistrationMap {
#pragma warning disable CS0649 // Field is never assigned to;
			// assigned to in generated IL: https://github.com/xamarin/xamarin-android/blob/cbfa7e20acebd37b52ec4de9d5c1a4a66ddda799/src/Xamarin.Android.Build.Tasks/Linker/MonoDroid.Tuner/MonoDroidMarkStep.cs#L204
			static Dictionary<string, int>? typesMap;
#pragma warning restore CS0649

			static void Prefill ()
			{
				// fill code added by the linker
			}

			static MagicRegistrationMap ()
			{
				Prefill ();
			}

			static public bool Filled {
				get {
					return typesMap != null && typesMap.Count > 0;
				}
			}

			internal static bool CallRegisterMethod (JniNativeMethodRegistrationArguments arguments, string typeName)
			{
				int idx = 0;

				if (typeName == null || !(typesMap?.TryGetValue (typeName, out idx) == true))
					return false;

				return CallRegisterMethodByIndex (arguments, idx);
			}

			static bool CallRegisterMethodByIndex (JniNativeMethodRegistrationArguments arguments, int? typeIdx)
			{
				// updated by the linker to register known types
				return false;
			}
		}

		public override void RegisterNativeMembers (
				JniType nativeClass,
				[DynamicallyAccessedMembers (MethodsAndPrivateNested)]
				Type type,
				string? methods) =>
			RegisterNativeMembers (nativeClass, type, methods.AsSpan ());

		[UnconditionalSuppressMessage ("Trimming", "IL2057", Justification = "Type.GetType() can never statically know the string value parsed from parameter 'methods'.")]
		[UnconditionalSuppressMessage ("Trimming", "IL2067", Justification = "Delegate.CreateDelegate() can never statically know the string value parsed from parameter 'methods'.")]
		[UnconditionalSuppressMessage ("Trimming", "IL2072", Justification = "Delegate.CreateDelegate() can never statically know the string value parsed from parameter 'methods'.")]
		public void RegisterNativeMembers (
				JniType nativeClass,
				[DynamicallyAccessedMembers (MethodsAndPrivateNested)] Type type,
				ReadOnlySpan<char> methods)
		{
			try {
				if (methods.IsEmpty) {
					RuntimeNativeMethods.monodroid_log (LogLevel.Info, LogCategories.Assembly, $"RegisterJniNatives: Type {type.FullName} registers no methods");
					if (jniAddNativeMethodRegistrationAttributePresent) {
						RuntimeNativeMethods.monodroid_log (LogLevel.Info, LogCategories.Assembly, $"RegisterJniNatives: Type {type.FullName} has [AddNativeMethodRegistration] attribute");
						base.RegisterNativeMembers (nativeClass, type, methods.ToString ());
					}
					return;
				} else if (FastRegisterNativeMembers (nativeClass, type, methods)) {
					return;
				}

				int methodCount = CountMethods (methods);
				if (methodCount < 1) {
					if (jniAddNativeMethodRegistrationAttributePresent) {
						base.RegisterNativeMembers (nativeClass, type, methods.ToString ());
					}
					return;
				}

				JniNativeMethodRegistration [] natives = new JniNativeMethodRegistration [methodCount];
				int nativesIndex = 0;
				MethodInfo []? typeMethods = null;

				ReadOnlySpan<char> methodsSpan = methods;
				bool needToRegisterNatives = false;

				while (!methodsSpan.IsEmpty) {
					int newLineIndex = methodsSpan.IndexOf ('\n');

					ReadOnlySpan<char> methodLine = methodsSpan.Slice (0, newLineIndex != -1 ? newLineIndex : methodsSpan.Length);
					if (!methodLine.IsEmpty) {
						SplitMethodLine (methodLine,
							out ReadOnlySpan<char> name,
							out ReadOnlySpan<char> signature,
							out ReadOnlySpan<char> callbackString,
							out ReadOnlySpan<char> callbackDeclaringTypeString);

						Delegate? callback = null;
						if (callbackString.SequenceEqual ("__export__")) {
							var mname = name.Slice (2);
							MethodInfo? minfo = null;
							typeMethods ??= type.GetMethods (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
							foreach (var mi in typeMethods)
								if (mname.SequenceEqual (mi.Name) && signature.SequenceEqual (JavaNativeTypeManager.GetJniSignature (mi))) {
									minfo = mi;
									break;
								}

							if (minfo == null)
								throw new InvalidOperationException (FormattableString.Invariant ($"Specified managed method '{mname.ToString ()}' was not found. Signature: {signature.ToString ()}"));
							callback = CreateDynamicCallback (minfo);
							needToRegisterNatives = true;
							RuntimeNativeMethods.monodroid_log (LogLevel.Info, LogCategories.Assembly, $"RegisterJniNativesMethod: [ ] {type.FullName}: __export__ found for method '{name.ToString ()}'");
						} else {
							Type callbackDeclaringType = type;
							if (!callbackDeclaringTypeString.IsEmpty) {
								callbackDeclaringType = Type.GetType (callbackDeclaringTypeString.ToString (), throwOnError: true)!;
							}
							while (callbackDeclaringType.ContainsGenericParameters) {
								callbackDeclaringType = callbackDeclaringType.BaseType!;
							}

							GetCallbackHandler connector = (GetCallbackHandler) Delegate.CreateDelegate (typeof (GetCallbackHandler),
							                                                                             callbackDeclaringType, callbackString.ToString ());
							callback = connector ();
						}

						if (callback != null) {
							needToRegisterNatives = true;
							natives [nativesIndex++] = new JniNativeMethodRegistration (name.ToString (), signature.ToString (), callback);
							RuntimeNativeMethods.monodroid_log (LogLevel.Info, LogCategories.Assembly, $"RegisterJniNativesMethod: [ ] {type.FullName}.{name.ToString ()}");
						}
					}

					methodsSpan = newLineIndex != -1 ? methodsSpan.Slice (newLineIndex + 1) : default;
				}

				if (needToRegisterNatives) {
					RuntimeNativeMethods.monodroid_log (LogLevel.Info, LogCategories.Assembly, $"RegisterJniNatives: Type {type.FullName} calling JniEnvironment.Types.RegisterNatives");
					JniEnvironment.Types.RegisterNatives (nativeClass.PeerReference, natives, nativesIndex);
				}
			} catch (Exception e) {
				JniEnvironment.Runtime.RaisePendingException (e);
			}
		}

		static int CountMethods (ReadOnlySpan<char> methodsSpan)
		{
			int count = 0;
			while (!methodsSpan.IsEmpty) {
				count++;

				int newLineIndex = methodsSpan.IndexOf ('\n');
				methodsSpan = newLineIndex != -1 ? methodsSpan.Slice (newLineIndex + 1) : default;
			}
			return count;
		}

		static void SplitMethodLine (
			ReadOnlySpan<char> methodLine,
			out ReadOnlySpan<char> name,
			out ReadOnlySpan<char> signature,
			out ReadOnlySpan<char> callback,
			out ReadOnlySpan<char> callbackDeclaringType)
		{
			int colonIndex = methodLine.IndexOf (':');
			name = methodLine.Slice (0, colonIndex);
			methodLine = methodLine.Slice (colonIndex + 1);

			colonIndex = methodLine.IndexOf (':');
			signature = methodLine.Slice (0, colonIndex);
			methodLine = methodLine.Slice (colonIndex + 1);

			colonIndex = methodLine.IndexOf (':');
			callback = methodLine.Slice (0, colonIndex != -1 ? colonIndex : methodLine.Length);

			callbackDeclaringType = colonIndex != -1 ? methodLine.Slice (colonIndex + 1) : default;
		}
	}

	class AndroidValueManager : JniRuntime.JniValueManager {

		Dictionary<IntPtr, IdentityHashTargets>         instances       = new Dictionary<IntPtr, IdentityHashTargets> ();

		public override void WaitForGCBridgeProcessing ()
		{
			AndroidRuntimeInternal.WaitForBridgeProcessing ();
		}

		public override IJavaPeerable? CreatePeer (
				ref JniObjectReference reference,
				JniObjectReferenceOptions options,
				[DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
				Type? targetType)
		{
			if (!reference.IsValid)
				return null;

			var peer        = Java.Interop.TypeManager.CreateInstance (reference.Handle, JniHandleOwnership.DoNotTransfer, targetType) as IJavaPeerable;
			JniObjectReference.Dispose (ref reference, options);
			return peer;
		}

		public override void AddPeer (IJavaPeerable value)
		{
			if (value == null)
				throw new ArgumentNullException (nameof (value));
			if (!value.PeerReference.IsValid)
				throw new ArgumentException ("Must have a valid JNI object reference!", nameof (value));

			var reference       = value.PeerReference;
			var hash            = JNIEnv.IdentityHash (reference.Handle);

			AddPeer (value, reference, hash);
		}

		internal void AddPeer (IJavaPeerable value, JniObjectReference reference, IntPtr hash)
		{
			lock (instances) {
				if (!instances.TryGetValue (hash, out var targets)) {
					targets = new IdentityHashTargets (value);
					instances.Add (hash, targets);
					return;
				}
				bool found = false;
				for (int i = 0; i < targets.Count; ++i) {
					IJavaPeerable? target;
					var wref = targets [i];
					if (ShouldReplaceMapping (wref!, reference, out target)) {
						found = true;
						targets [i] = IdentityHashTargets.CreateWeakReference (value);
						break;
					}
					if (JniEnvironment.Types.IsSameObject (value.PeerReference, target!.PeerReference)) {
						found = true;
						if (Logger.LogGlobalRef) {
							Logger.Log (LogLevel.Info, "monodroid-gref", FormattableString.Invariant (
								$"warning: not replacing previous registered handle {target.PeerReference} with handle {reference} for key_handle 0x{hash:x}"));
						}
					}
				}
				if (!found) {
					targets.Add (value);
				}
			}
		}

		internal void AddPeer (IJavaPeerable value, IntPtr handle, JniHandleOwnership transfer, out IntPtr handleField)
		{
			if (handle == IntPtr.Zero) {
				handleField = handle;
				return;
			}

			var transferType = transfer & (JniHandleOwnership.DoNotTransfer | JniHandleOwnership.TransferLocalRef | JniHandleOwnership.TransferGlobalRef);
			switch (transferType) {
				case JniHandleOwnership.DoNotTransfer:
					handleField = JNIEnv.NewGlobalRef (handle);
					break;
				case JniHandleOwnership.TransferLocalRef:
					handleField = JNIEnv.NewGlobalRef (handle);
					JNIEnv.DeleteLocalRef (handle);
					break;
				case JniHandleOwnership.TransferGlobalRef:
					handleField = handle;
					break;
				default:
					throw new ArgumentOutOfRangeException ("transfer", transfer,
							"Invalid `transfer` value: " + transfer + " on type " + value.GetType ());
			}
			if (handleField == IntPtr.Zero)
				throw new InvalidOperationException ("Unable to allocate Global Reference for object '" + value.ToString () + "'!");

			IntPtr hash = JNIEnv.IdentityHash (handleField);
			value.SetJniIdentityHashCode ((int) hash);
			if ((transfer & JniHandleOwnership.DoNotRegister) == 0) {
				AddPeer (value, new JniObjectReference (handleField, JniObjectReferenceType.Global), hash);
			}

			if (Logger.LogGlobalRef) {
				RuntimeNativeMethods._monodroid_gref_log (
					FormattableString.Invariant (
						$"handle 0x{handleField:x}; key_handle 0x{hash:x}: Java Type: `{JNIEnv.GetClassNameFromInstance (handleField)}`; MCW type: `{value.GetType ().FullName}`\n"));
			}
		}

		bool ShouldReplaceMapping (WeakReference<IJavaPeerable> current, JniObjectReference reference, out IJavaPeerable? target)
		{
			target      = null;

			if (current == null)
				return true;

			// Target has been GC'd; see also FIXME, above, in finalizer
			if (!current.TryGetTarget (out target) || target == null)
				return true;

			// It's possible that the instance was GC'd, but the finalizer
			// hasn't executed yet, so the `instances` entry is stale.
			if (!target.PeerReference.IsValid)
				return true;

			if (!JniEnvironment.Types.IsSameObject (target.PeerReference, reference))
				return false;

			// JNIEnv.NewObject/JNIEnv.CreateInstance() compatibility.
			// When two MCW's are created for one Java instance [0],
			// we want the 2nd MCW to replace the 1st, as the 2nd is
			// the one the dev created; the 1st is an implicit intermediary.
			//
			// [0]: If Java ctor invokes overridden virtual method, we'll
			// transition into managed code w/o a registered instance, and
			// thus will create an "intermediary" via
			// (IntPtr, JniHandleOwnership) .ctor.
			if ((target.JniManagedPeerState & JniManagedPeerStates.Replaceable) == JniManagedPeerStates.Replaceable)
				return true;

			return false;
		}

		public override void RemovePeer (IJavaPeerable value)
		{
			if (value == null)
				throw new ArgumentNullException (nameof (value));

			var reference       = value.PeerReference;
			if (!reference.IsValid) {
				// Likely an idempotent DIspose(); ignore.
				return;
			}
			var hash            = JNIEnv.IdentityHash (reference.Handle);

			RemovePeer (value, hash);
		}

		internal void RemovePeer (IJavaPeerable value, IntPtr hash)
		{
			lock (instances) {
				if (!instances.TryGetValue (hash, out var targets)) {
					return;
				}
				for (int i = targets.Count - 1; i >= 0; i--) {
					var wref = targets [i];
					if (!wref!.TryGetTarget (out var target)) {
						// wref is invalidated; remove it.
						targets.RemoveAt (i);
						continue;
					}
					if (!object.ReferenceEquals (target, value)) {
						continue;
					}
					targets.RemoveAt (i);
				}
				if (targets.Count == 0) {
					instances.Remove (hash);
				}
			}
		}

		public override IJavaPeerable? PeekPeer (JniObjectReference reference)
		{
			if (!reference.IsValid)
				return null;

			var hash    = JNIEnv.IdentityHash (reference.Handle);
			lock (instances) {
				if (instances.TryGetValue (hash, out var targets)) {
					for (int i = targets.Count - 1; i >= 0; i--) {
						var wref    = targets [i];
						if (!wref!.TryGetTarget (out var result) || !result.PeerReference.IsValid) {
							targets.RemoveAt (i);
							continue;
						}
						if (!JniEnvironment.Types.IsSameObject (reference, result.PeerReference))
							continue;
						return result;
					}
				}
			}
			return null;
		}

		public override void ActivatePeer (IJavaPeerable? self, JniObjectReference reference, ConstructorInfo cinfo, object? []? argumentValues)
		{
			Java.Interop.TypeManager.Activate (reference.Handle, cinfo, argumentValues);
		}

		protected override bool TryUnboxPeerObject (IJavaPeerable value, [NotNullWhen (true)]out object? result)
		{
			var proxy = value as JavaProxyThrowable;
			if (proxy != null) {
				result  = proxy.InnerException;
				return true;
			}
			return base.TryUnboxPeerObject (value, out result);
		}

		internal Exception? UnboxException (IJavaPeerable value)
		{
			object? r;
			if (TryUnboxPeerObject (value, out r) && r is Exception e) {
				return e;
			}
			return null;
		}

		public override void CollectPeers ()
		{
			GC.Collect ();
		}

		public override void FinalizePeer (IJavaPeerable value)
		{
			if (value == null)
				throw new ArgumentNullException (nameof (value));

			if (Logger.LogGlobalRef) {
				RuntimeNativeMethods._monodroid_gref_log ($"Finalizing handle {value.PeerReference}\n");
			}

			// FIXME: need hash cleanup mechanism.
			// Finalization occurs after a test of java persistence.  If the
			// handle still contains a java reference, we can't finalize the
			// object and should "resurrect" it.
			if (value.PeerReference.IsValid) {
				GC.ReRegisterForFinalize (value);
			} else {
				RemovePeer (value, (IntPtr) value.JniIdentityHashCode);
				value.SetPeerReference (new JniObjectReference ());
				value.Finalized ();
			}
		}

		public override List<JniSurfacedPeerInfo> GetSurfacedPeers ()
		{
			lock (instances) {
				var surfacedPeers = new List<JniSurfacedPeerInfo> (instances.Count);
				foreach (var e in instances) {
					for (int i = 0; i < e.Value.Count; i++) {
						var value = e.Value [i];
						surfacedPeers.Add (new JniSurfacedPeerInfo (e.Key.ToInt32 (), value!));
					}
				}
				return surfacedPeers;
			}
		}
	}

	class InstancesKeyComparer : IEqualityComparer<IntPtr>
	{

		public bool Equals (IntPtr x, IntPtr y)
		{
			return x == y;
		}

		public int GetHashCode (IntPtr value)
		{
			return value.GetHashCode ();
		}
	}

	class IdentityHashTargets {
		WeakReference<IJavaPeerable>?            first;
		List<WeakReference<IJavaPeerable>?>?     rest;

		public static WeakReference<IJavaPeerable> CreateWeakReference (IJavaPeerable value)
		{
			return new WeakReference<IJavaPeerable> (value, trackResurrection: true);
		}

		public IdentityHashTargets (IJavaPeerable value)
		{
			first   = CreateWeakReference (value);
		}

		public int Count => (first != null ? 1 : 0) + (rest != null ? rest.Count : 0);

		public WeakReference<IJavaPeerable>? this [int index] {
			get {
				if (index == 0)
					return first;
				index -= 1;
				if (rest == null || index >= rest.Count)
					return null;
				return rest [index];
			}
			set {
				if (index == 0) {
					first = value;
					return;
				}
				index -= 1;

				if (rest != null)
					rest [index] = value;
			}
		}

		public void Add (IJavaPeerable value)
		{
			if (first == null) {
				first   = CreateWeakReference (value);
				return;
			}
			if (rest == null)
				rest    = new List<WeakReference<IJavaPeerable>?> ();
			rest.Add (CreateWeakReference (value));
		}

		public void RemoveAt (int index)
		{
			if (index == 0) {
				first   = null;
				if (rest?.Count > 0) {
					first   = rest [0];
					rest.RemoveAt (0);
				}
				return;
			}
			index -= 1;
			rest?.RemoveAt (index);
		}
	}
}
#endif // JAVA_INTEROP
