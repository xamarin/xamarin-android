using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;

using Java.Interop;
using Java.Interop.Tools.TypeNameMappings;
using System.Diagnostics.CodeAnalysis;

namespace Android.Runtime {
#pragma warning disable 0649
	struct JnienvInitializeArgs {
		public IntPtr          javaVm;
		public IntPtr          env;
		public IntPtr          grefLoader;
		public IntPtr          Loader_loadClass;
		public IntPtr          grefClass;
		public IntPtr          Class_forName;
		public uint            logCategories;
		public int             version;
		public int             androidSdkVersion;
		public int             localRefsAreIndirect;
		public int             grefGcThreshold;
		public IntPtr          grefIGCUserPeer;
		public int             isRunningOnDesktop;
		public byte            brokenExceptionTransitions;
		public int             packageNamingPolicy;
		public byte            ioExceptionType;
		public int             jniAddNativeMethodRegistrationAttributePresent;
	}
#pragma warning restore 0649

	public static partial class JNIEnv {
		static IntPtr java_class_loader;
		static IntPtr java_vm;
		static IntPtr load_class_id;
		static IntPtr gref_class;
		static IntPtr mid_Class_forName;
		static int version;
		static int androidSdkVersion;

		static bool AllocObjectSupported;

		static IntPtr grefIGCUserPeer_class;

		internal static int    gref_gc_threshold;

		internal  static  bool  PropagateExceptions;

		internal static bool IsRunningOnDesktop;
		internal static bool LogTypemapMissStackTrace;

		static AndroidRuntime? androidRuntime;
		static BoundExceptionType BoundExceptionType;

		[ThreadStatic]
		static byte[]? mvid_bytes;

		internal    static      AndroidValueManager? AndroidValueManager;

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		extern static void monodroid_log (LogLevel level, LogCategories category, string message);

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal extern static IntPtr monodroid_timing_start (string? message);

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal extern static void monodroid_timing_stop (IntPtr sequence, string? message);

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal extern static void monodroid_free (IntPtr ptr);

		public static IntPtr Handle {
			get {
				return JniEnvironment.EnvironmentPointer;
			}
		}

		public static void CheckHandle (IntPtr jnienv)
		{
			new JniTransition (jnienv).Dispose ();
		}

		internal static bool IsGCUserPeer (IntPtr value)
		{
			if (value == IntPtr.Zero)
				return false;

			return IsInstanceOf (value, grefIGCUserPeer_class);
		}

		internal static bool ShouldWrapJavaException (Java.Lang.Throwable? t, [CallerMemberName] string? caller = null)
		{
			if (t == null) {
				monodroid_log (LogLevel.Warn,
				               LogCategories.Default,
				               $"ShouldWrapJavaException was not passed a valid `Java.Lang.Throwable` instance. Called from method `{caller}`");
				return false;
			}

			bool wrap = BoundExceptionType == BoundExceptionType.System;
			if (!wrap) {
				monodroid_log (LogLevel.Warn,
				               LogCategories.Default,
				               $"Not wrapping exception of type {t.GetType().FullName} from method `{caller}`. This will change in a future release.");
			}

			return wrap;
		}

		[DllImport ("libc")]
		static extern int gettid ();

		static unsafe void RegisterJniNatives (IntPtr typeName_ptr, int typeName_len, IntPtr jniClass, IntPtr methods_ptr, int methods_len)
		{
			string typeName = new string ((char*) typeName_ptr, 0, typeName_len);
			var type = Type.GetType (typeName);
			if (type == null) {
				monodroid_log (LogLevel.Error,
				               LogCategories.Default,
				               $"Could not load type '{typeName}'. Skipping JNI registration of type '{Java.Interop.TypeManager.GetClassName (jniClass)}'.");
				return;
			}

			var className = Java.Interop.TypeManager.GetClassName (jniClass);
			Java.Interop.TypeManager.RegisterType (className, type);

			JniType? jniType = null;
			JniType.GetCachedJniType (ref jniType, className);

			androidRuntime!.TypeManager.RegisterNativeMembers (jniType, type, methods_ptr == IntPtr.Zero ? null : new string ((char*) methods_ptr, 0, methods_len));
		}

		internal static unsafe void Initialize (JnienvInitializeArgs* args)
		{
			bool logTiming = (args->logCategories & (uint)LogCategories.Timing) != 0;
			IntPtr total_timing_sequence = IntPtr.Zero;
			IntPtr partial_timing_sequence = IntPtr.Zero;
			if (logTiming) {
				total_timing_sequence = monodroid_timing_start ("JNIEnv.Initialize start");
				partial_timing_sequence = monodroid_timing_start (null);
			}

			LogTypemapMissStackTrace = (args->logCategories & (uint)LogCategories.Assembly) != 0;

			gref_gc_threshold = args->grefGcThreshold;

			java_vm = args->javaVm;

			version = args->version;

			androidSdkVersion = args->androidSdkVersion;

			java_class_loader = args->grefLoader;
			load_class_id     = args->Loader_loadClass;
			gref_class        = args->grefClass;
			mid_Class_forName = args->Class_forName;

			if (args->localRefsAreIndirect == 1)
				IdentityHash = v => _monodroid_get_identity_hash_code (Handle, v);
			else
				IdentityHash = v => v;

#if MONOANDROID1_0
			Mono.SystemDependencyProvider.Initialize ();
#endif

			BoundExceptionType = (BoundExceptionType)args->ioExceptionType;
			androidRuntime = new AndroidRuntime (args->env, args->javaVm, androidSdkVersion > 10, args->grefLoader, args->Loader_loadClass, args->jniAddNativeMethodRegistrationAttributePresent != 0);
			AndroidValueManager = (AndroidValueManager) androidRuntime.ValueManager;

			AllocObjectSupported = androidSdkVersion > 10;
			IsRunningOnDesktop = args->isRunningOnDesktop == 1;

			grefIGCUserPeer_class = args->grefIGCUserPeer;

			PropagateExceptions = args->brokenExceptionTransitions == 0;

			JavaNativeTypeManager.PackageNamingPolicy = (PackageNamingPolicy)args->packageNamingPolicy;
			if (IsRunningOnDesktop) {
				var packageNamingPolicy = Environment.GetEnvironmentVariable ("__XA_PACKAGE_NAMING_POLICY__");
				if (Enum.TryParse (packageNamingPolicy, out PackageNamingPolicy pnp)) {
					JavaNativeTypeManager.PackageNamingPolicy = pnp;
				}
			}

#if !MONOANDROID1_0
			SynchronizationContext.SetSynchronizationContext (Android.App.Application.SynchronizationContext);
#endif

			if (logTiming) {
				monodroid_timing_stop (total_timing_sequence, "JNIEnv.Initialize end");
			}
		}

		internal static void Exit ()
		{
			/* Manually dispose surfaced objects and close the current JniEnvironment to
			 * avoid ObjectDisposedException thrown on finalizer threads after shutdown
			 */
			foreach (var surfacedObject in Java.Interop.Runtime.GetSurfacedObjects ()) {
				try {
					var obj = surfacedObject.Target as IDisposable;
					if (obj != null)
						obj.Dispose ();
					continue;
				} catch (Exception e) {
					monodroid_log (LogLevel.Warn, LogCategories.Default, $"Couldn't dispose object: {e}");
				}
				/* If calling Dispose failed, the assumption is that user-code in
				 * the Dispose(bool) overload is to blame for it. In that case we
				 * fallback to manual deletion of the surfaced object.
				 */
				var jobj = surfacedObject.Target as Java.Lang.Object;
				if (jobj != null)
					ManualJavaObjectDispose (jobj);
			}
			JniEnvironment.Runtime.Dispose ();
		}

		/* FIXME: This reproduces the minimal steps in Java.Lang.Object.Dispose
		 * that needs to be executed so that we don't leak any GREF and prevent
		 * code execution into an appdomain that we are disposing via a finalizer.
		 * Ideally it should be done via another more generic mechanism, likely
		 * from the Java.Interop.Runtime API.
		 */
		static void ManualJavaObjectDispose (Java.Lang.Object obj)
		{
			var peer = obj.PeerReference;
			var handle = peer.Handle;
			var keyHandle = ((IJavaObjectEx)obj).KeyHandle;
			Java.Lang.Object.Dispose (obj, ref handle, keyHandle, (JObjectRefType)peer.Type);
			GC.SuppressFinalize (obj);
		}

		static Action<AppDomain, UnhandledExceptionEventArgs> AppDomain_DoUnhandledException = null!;

		static void Initialize ()
		{
			if (AppDomain_DoUnhandledException == null) {
				var ad_due = typeof (AppDomain)
					.GetMethod ("DoUnhandledException",
					            bindingAttr:  BindingFlags.NonPublic | BindingFlags.Instance,
					            binder:       null,
					            types:        new []{typeof (UnhandledExceptionEventArgs)},
					            modifiers:    null);
				if (ad_due != null) {
					AppDomain_DoUnhandledException  = (Action<AppDomain, UnhandledExceptionEventArgs>) Delegate.CreateDelegate (
						typeof (Action<AppDomain, UnhandledExceptionEventArgs>), ad_due);
				}
			}
		}

		internal static void PropagateUncaughtException (IntPtr env, IntPtr javaThreadPtr, IntPtr javaExceptionPtr)
		{
			if (!PropagateExceptions)
				return;

			try {
				Initialize ();
			} catch (Exception e) {
				Android.Runtime.AndroidEnvironment.FailFast ($"Unable to initialize UncaughtExceptionHandler. Nested exception caught: {e}");
			}

			var javaException = JavaObject.GetObject<Java.Lang.Throwable> (env, javaExceptionPtr, JniHandleOwnership.DoNotTransfer)!;

			try {
				var jltp = javaException as JavaProxyThrowable;
				Exception? innerException = jltp?.InnerException;
				var args  = new UnhandledExceptionEventArgs (innerException ?? javaException, isTerminating: true);

				Logger.Log (LogLevel.Info, "MonoDroid", "UNHANDLED EXCEPTION:");
				Logger.Log (LogLevel.Info, "MonoDroid", javaException.ToString ());

				// Disabled until Linker error surfaced in https://github.com/xamarin/xamarin-android/pull/4302#issuecomment-596400025 is resolved
				//AppDomain.CurrentDomain.DoUnhandledException (args);
				AppDomain_DoUnhandledException?.Invoke (AppDomain.CurrentDomain, args);
			} catch (Exception e) {
				Logger.Log (LogLevel.Error, "monodroid", "Exception thrown while raising AppDomain.UnhandledException event: " + e.ToString ());
			}
		}

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		extern static void _monodroid_gc_wait_for_bridge_processing ();

#pragma warning disable CS0649 // Field is never assigned to.  This field is assigned from monodroid-glue.cc.
		static volatile bool BridgeProcessing; // = false
#pragma warning restore CS0649 // Field is never assigned to.

		public static void WaitForBridgeProcessing ()
		{
			if (!BridgeProcessing)
				return;
			_monodroid_gc_wait_for_bridge_processing ();
		}

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		extern static IntPtr _monodroid_get_identity_hash_code (IntPtr env, IntPtr value);

		internal static Func<IntPtr, IntPtr>? IdentityHash;

		public static IntPtr AllocObject (string jniClassName)
		{
			IntPtr jniClass = JNIEnv.FindClass (jniClassName);
			try {
				return AllocObject (jniClass);
			}
			finally {
				JNIEnv.DeleteGlobalRef (jniClass);
			}
		}

		public static IntPtr AllocObject (Type type)
		{
			IntPtr jniClass = JNIEnv.FindClass (type);
			try {
				return AllocObject (jniClass);
			}
			finally {
				JNIEnv.DeleteGlobalRef (jniClass);
			}
		}

		public static unsafe IntPtr StartCreateInstance (IntPtr jclass, IntPtr constructorId, JValue* constructorParameters)
		{
			if (AllocObjectSupported) {
				return AllocObject (jclass);
			}
			return NewObject (jclass, constructorId, constructorParameters);
		}

		public static unsafe IntPtr StartCreateInstance (IntPtr jclass, IntPtr constructorId, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return StartCreateInstance (jclass, constructorId, cp);
		}

		public static unsafe void FinishCreateInstance (IntPtr instance, IntPtr jclass, IntPtr constructorId, JValue* constructorParameters)
		{
			if (!AllocObjectSupported)
				return;
			CallNonvirtualVoidMethod (instance, jclass, constructorId, constructorParameters);
		}

		public static unsafe void FinishCreateInstance (IntPtr instance, IntPtr jclass, IntPtr constructorId, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				FinishCreateInstance (instance, jclass, constructorId, cp);
		}

		public static unsafe IntPtr StartCreateInstance (Type type, string jniCtorSignature, JValue* constructorParameters)
		{
			if (AllocObjectSupported) {
				return AllocObject (type);
			}
			return CreateInstance (type, jniCtorSignature, constructorParameters);
		}

		public static unsafe IntPtr StartCreateInstance (Type type, string jniCtorSignature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return StartCreateInstance (type, jniCtorSignature, cp);
		}

		public static unsafe IntPtr StartCreateInstance (string jniClassName, string jniCtorSignature, JValue* constructorParameters)
		{
			if (AllocObjectSupported)
				return AllocObject (jniClassName);
			return CreateInstance (jniClassName, jniCtorSignature, constructorParameters);
		}

		public static unsafe IntPtr StartCreateInstance (string jniClassName, string jniCtorSignature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return StartCreateInstance (jniClassName, jniCtorSignature, cp);
		}

		public static unsafe void FinishCreateInstance (IntPtr instance, string jniCtorSignature, JValue* constructorParameters)
		{
			if (!AllocObjectSupported)
				return;
			InvokeConstructor (instance, jniCtorSignature, constructorParameters);
		}

		public static unsafe void FinishCreateInstance (IntPtr instance, string jniCtorSignature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				FinishCreateInstance (instance, jniCtorSignature, cp);
		}

		public static unsafe void InvokeConstructor (IntPtr instance, string jniCtorSignature, JValue* constructorParameters)
		{
			IntPtr lrefClass = GetObjectClass (instance);
			try {
				IntPtr ctor = JNIEnv.GetMethodID (lrefClass, "<init>", jniCtorSignature);
				if (ctor == IntPtr.Zero)
					throw new ArgumentException (string.Format ("Could not find constructor JNI signature '{0}' on type '{1}'.",
								jniCtorSignature, Java.Interop.TypeManager.GetClassName (lrefClass)));
				CallNonvirtualVoidMethod (instance, lrefClass, ctor, constructorParameters);
			} finally {
				DeleteLocalRef (lrefClass);
			}
		}

		public static unsafe void InvokeConstructor (IntPtr instance, string jniCtorSignature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				InvokeConstructor (instance, jniCtorSignature, cp);
		}

		public static unsafe IntPtr CreateInstance (IntPtr jniClass, string signature, JValue* constructorParameters)
		{
			IntPtr ctor = JNIEnv.GetMethodID (jniClass, "<init>", signature);
			if (ctor == IntPtr.Zero)
				throw new ArgumentException (string.Format ("Could not find constructor JNI signature '{0}' on type '{1}'.",
							signature, Java.Interop.TypeManager.GetClassName (jniClass)));
			return JNIEnv.NewObject (jniClass, ctor, constructorParameters);
		}

		public static unsafe IntPtr CreateInstance (IntPtr jniClass, string signature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return CreateInstance (jniClass, signature, cp);
		}

		public static unsafe IntPtr CreateInstance (string jniClassName, string signature, JValue* constructorParameters)
		{
			IntPtr jniClass = JNIEnv.FindClass (jniClassName);
			try {
				return CreateInstance (jniClass, signature, constructorParameters);
			}
			finally {
				JNIEnv.DeleteGlobalRef (jniClass);
			}
		}

		public static unsafe IntPtr CreateInstance (string jniClassName, string signature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return CreateInstance (jniClassName, signature, cp);
		}

		public static unsafe IntPtr CreateInstance (Type type, string signature, JValue* constructorParameters)
		{
			IntPtr jniClass = JNIEnv.FindClass (type);
			try {
				return CreateInstance (jniClass, signature, constructorParameters);
			}
			finally {
				JNIEnv.DeleteGlobalRef (jniClass);
			}
		}

		public static unsafe IntPtr CreateInstance (Type type, string signature, params JValue[] constructorParameters)
		{
			fixed (JValue* cp = constructorParameters)
				return CreateInstance (type, signature, cp);
		}

		public static IntPtr FindClass (System.Type type)
		{
			int rank = JavaNativeTypeManager.GetArrayInfo (type, out type);
			try {
				return FindClass (JavaNativeTypeManager.ToJniName (GetJniName (type), rank));
			} catch (Java.Lang.Throwable e) {
				if (!((e is Java.Lang.NoClassDefFoundError) || (e is Java.Lang.ClassNotFoundException)))
					throw;
				monodroid_log (LogLevel.Warn, LogCategories.Default, $"JNIEnv.FindClass(Type) caught unexpected exception: {e}");
				var jni = Java.Interop.TypeManager.GetJniTypeName (type);
				if (jni != null) {
					e.Dispose ();
					return FindClass (JavaNativeTypeManager.ToJniName (jni, rank));
				}

				// Though it's tempting to call TypeManager.RegisterType() to avoid
				// calling GetCustomAttributes() again, this isn't necessary as
				// JNIEnv.FindClass() will invoke the static constructor for the type,
				// which will (indirectly) call TypeManager.RegisterType().
				jni = JavaNativeTypeManager.ToJniNameFromAttributes (type);
				if (jni != null) {
					e.Dispose ();
					return FindClass (JavaNativeTypeManager.ToJniName (jni, rank));
				}
				throw;
			}
		}

		static readonly int nameBufferLength = 1024;
		[ThreadStatic] static char[]? nameBuffer;

		static unsafe IntPtr BinaryName (string classname)
		{
			int index = classname.IndexOf ('/');

			if (index == -1)
				return NewString (classname);

			int length = classname.Length;
			if (length > nameBufferLength)
				return NewString (classname.Replace ('/', '.'));

			if (nameBuffer == null)
				nameBuffer = new char[nameBufferLength];

			fixed (char* src = classname, dst = nameBuffer) {
				char* src_ptr = src;
				char* dst_ptr = dst;
				char* end_ptr = src + length;
				while (src_ptr < end_ptr) {
					*dst_ptr = (*src_ptr == '/') ? '.' : *src_ptr;
					src_ptr++;
					dst_ptr++;
				}
			}
			return NewString (nameBuffer, length);
		}

		public static IntPtr FindClass (string classname)
		{
			IntPtr local_ref;

			IntPtr native_str = BinaryName (classname);
			try {
				local_ref = CallStaticObjectMethod (gref_class, mid_Class_forName, new JValue (native_str), new JValue (true), new JValue (java_class_loader));
			} finally {
				DeleteLocalRef (native_str);
			}

			IntPtr global_ref = NewGlobalRef (local_ref);
			DeleteLocalRef (local_ref);
			return global_ref;
		}

		public static IntPtr FindClass (string className, ref IntPtr cachedJniClassHandle)
		{
			if (cachedJniClassHandle != IntPtr.Zero)
				return cachedJniClassHandle;
			IntPtr h = FindClass (className);
			if (Interlocked.CompareExchange (ref cachedJniClassHandle, h, IntPtr.Zero) != IntPtr.Zero)
				DeleteGlobalRef (h);
			return cachedJniClassHandle;
		}

		public static void Throw (IntPtr obj)
		{
			if (obj == IntPtr.Zero)
				throw new ArgumentException ("'obj' must not be IntPtr.Zero.", "obj");

			JniEnvironment.Exceptions.Throw (new JniObjectReference (obj));
		}

		public static void ThrowNew (IntPtr clazz, string message)
		{
			if (message == null)
				throw new ArgumentNullException ("message");

			JniEnvironment.Exceptions.ThrowNew (new JniObjectReference (clazz), message);
		}

		public static void PushLocalFrame (int capacity)
		{
			JniEnvironment.References.PushLocalFrame (capacity);
		}

		public static void EnsureLocalCapacity (int capacity)
		{
			JniEnvironment.References.EnsureLocalCapacity (capacity);
		}

		internal static void DeleteRef (IntPtr handle, JniHandleOwnership transfer)
		{
			switch (transfer) {
			case JniHandleOwnership.DoNotTransfer:
				break;
			case JniHandleOwnership.TransferLocalRef:
				JNIEnv.DeleteLocalRef (handle);
				break;
			case JniHandleOwnership.TransferGlobalRef:
				JNIEnv.DeleteGlobalRef (handle);
				break;
			}
		}

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int _monodroid_gref_log (string message);

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int _monodroid_gref_log_new (IntPtr curHandle, byte curType, IntPtr newHandle, byte newType, string? threadName, int threadId, [In] StringBuilder? from, int from_writable);

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void _monodroid_gref_log_delete (IntPtr handle, byte type, string? threadName, int threadId, [In] StringBuilder? from, int from_writable);

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void _monodroid_weak_gref_new (IntPtr curHandle, byte curType, IntPtr newHandle, byte newType, string? threadName, int threadId, [In] StringBuilder? from, int from_writable);

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void _monodroid_weak_gref_delete (IntPtr handle, byte type, string? threadName, int threadId, [In] StringBuilder? from, int from_writable);

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern int _monodroid_lref_log_new (int lrefc, IntPtr handle, byte type, string? threadName, int threadId, [In] StringBuilder from, int from_writable);

		[DllImport (AndroidRuntime.InternalDllName, CallingConvention = CallingConvention.Cdecl)]
		internal static extern void _monodroid_lref_log_delete (int lrefc, IntPtr handle, byte type, string? threadName, int threadId, [In] StringBuilder from, int from_writable);

		public static IntPtr NewGlobalRef (IntPtr jobject)
		{
			var r = new JniObjectReference (jobject);
			return r.NewGlobalRef ().Handle;
		}

		public static void DeleteGlobalRef (IntPtr jobject)
		{
			var r = new JniObjectReference (jobject, JniObjectReferenceType.Global);
			JniObjectReference.Dispose (ref r);
		}

		public static IntPtr NewLocalRef (IntPtr jobject)
		{
			return new JniObjectReference (jobject).NewLocalRef ().Handle;
		}

		public static void DeleteLocalRef (IntPtr jobject)
		{
			var r = new JniObjectReference (jobject, JniObjectReferenceType.Local);
			JniObjectReference.Dispose (ref r);
		}

		public static void DeleteWeakGlobalRef (IntPtr jobject)
		{
			var r = new JniObjectReference (jobject, JniObjectReferenceType.WeakGlobal);
			JniObjectReference.Dispose (ref r);
		}

		public static IntPtr NewObject (IntPtr jclass, IntPtr jmethod)
		{
			var r = JniEnvironment.Object.NewObject (new JniObjectReference (jclass), new JniMethodInfo (jmethod, isStatic: false));
			return r.Handle;
		}

		public static unsafe IntPtr NewObject (IntPtr jclass, IntPtr jmethod, JValue* parms)
		{
			var r = JniEnvironment.Object.NewObject (new JniObjectReference (jclass), new JniMethodInfo (jmethod, isStatic: false), (JniArgumentValue*) parms);
			return r.Handle;
		}

		public static unsafe IntPtr NewObject (IntPtr jclass, IntPtr jmethod, params JValue[] parms)
		{
			fixed (JValue* p = parms)
				return NewObject (jclass, jmethod, p);
		}

		public static string GetClassNameFromInstance (IntPtr jobject)
		{
			IntPtr jclass = GetObjectClass (jobject);
			try {
				return Java.Interop.TypeManager.GetClassName (jclass);
			} finally {
				DeleteLocalRef (jclass);
			}
		}

		[MethodImplAttribute(MethodImplOptions.InternalCall)]
		static extern unsafe IntPtr monodroid_typemap_managed_to_java (Type type, byte* mvid);

		internal static void LogTypemapTrace (StackTrace st)
		{
			string? trace = st.ToString ()?.Trim ();
			if (String.IsNullOrEmpty (trace))
				return;

			monodroid_log (LogLevel.Warn, LogCategories.Assembly, "typemap: called from");
			foreach (string line in trace!.Split ('\n')) {
				monodroid_log (LogLevel.Warn, LogCategories.Assembly, line);
			}
		}

		internal static unsafe string? TypemapManagedToJava (Type type)
		{
			if (mvid_bytes == null)
				mvid_bytes = new byte[16];

			var mvid = new Span<byte>(mvid_bytes);
			byte[]? mvid_data = null;
			if (!type.Module.ModuleVersionId.TryWriteBytes (mvid)) {
				monodroid_log (LogLevel.Warn, LogCategories.Default, $"Failed to obtain module MVID using the fast method, falling back to the slow one");
				mvid_data = type.Module.ModuleVersionId.ToByteArray ();
			} else {
				mvid_data = mvid_bytes;
			}

			IntPtr ret;
			fixed (byte* mvidptr = mvid_data) {
				ret = monodroid_typemap_managed_to_java (type, mvidptr);
			}

			if (ret == IntPtr.Zero) {
				if (LogTypemapMissStackTrace) {
					monodroid_log (LogLevel.Warn, LogCategories.Default, $"typemap: failed to map managed type to Java type: {type.AssemblyQualifiedName} (Module ID: {type.Module.ModuleVersionId}; Type token: {type.MetadataToken})");
					LogTypemapTrace (new StackTrace (true));
				}

				return null;
			}

			return Marshal.PtrToStringAnsi (ret);
		}

		public static string GetJniName (Type type)
		{
			if (type == null)
				throw new ArgumentNullException ("type");

			string? java = TypemapManagedToJava (type);
			return java == null
				? JavaNativeTypeManager.ToJniName (type)
				: java;
		}

		public static IntPtr ToJniHandle (IJavaObject? value)
		{
			if (value == null)
				return IntPtr.Zero;
			return value.Handle;
		}

		public static IntPtr ToLocalJniHandle (IJavaObject? value)
		{
			if (value == null)
				return IntPtr.Zero;
			var ex = value as IJavaObjectEx;
			if (ex != null)
				return ex.ToLocalJniHandle ();
			return NewLocalRef (value.Handle);
		}

		public static string? GetCharSequence (IntPtr jobject, JniHandleOwnership transfer)
		{
			if (jobject == IntPtr.Zero)
				return null;

			var r = JniEnvironment.Object.ToString (new JniObjectReference (jobject));
			return JniEnvironment.Strings.ToString (ref r, JniObjectReferenceOptions.CopyAndDispose);
		}

		public static unsafe string? GetString (IntPtr value, JniHandleOwnership transfer)
		{
			if (value == IntPtr.Zero)
				return null;

			var s = JniEnvironment.Strings.ToString (new JniObjectReference (value));
			DeleteRef (value, transfer);
			return s;
		}

		public static unsafe IntPtr NewString (string? text)
		{
			if (text == null)
				return IntPtr.Zero;

			return JniEnvironment.Strings.NewString (text).Handle;
		}

		public static unsafe IntPtr NewString (char[]? text, int length)
		{
			if (text == null)
				return IntPtr.Zero;

			fixed (char *s = text)
				return JniEnvironment.Strings.NewString (s, length).Handle;
		}

		static void AssertCompatibleArrayTypes (Type sourceType, IntPtr destArray)
		{
			IntPtr grefSource = FindClass (sourceType);
			IntPtr lrefDest   = GetObjectClass (destArray);
			try {
				if (!IsAssignableFrom (grefSource, lrefDest)) {
					throw new InvalidCastException (string.Format ("Unable to cast from '{0}' to '{1}'.",
								Java.Interop.TypeManager.GetClassName (grefSource),
								Java.Interop.TypeManager.GetClassName (lrefDest)));
				}
			} finally {
				DeleteGlobalRef (grefSource);
				DeleteLocalRef (lrefDest);
			}
		}

		static void AssertCompatibleArrayTypes (IntPtr sourceArray, Type destType)
		{
			IntPtr grefDest   = FindClass (destType);
			IntPtr lrefSource = GetObjectClass (sourceArray);
			try {
				if (!IsAssignableFrom (lrefSource, grefDest)) {
					throw new InvalidCastException (string.Format ("Unable to cast from '{0}' to '{1}'.",
								Java.Interop.TypeManager.GetClassName (lrefSource),
								Java.Interop.TypeManager.GetClassName (grefDest)));
				}
			} finally {
				DeleteGlobalRef (grefDest);
				DeleteLocalRef (lrefSource);
			}
		}

		public static void CopyArray (IntPtr src, bool[] dest)
		{
			if (dest == null)
				throw new ArgumentNullException ("dest");

			AssertCompatibleArrayTypes (src, typeof (bool[]));

			_GetBooleanArrayRegion (src, 0, dest.Length, dest);
		}

		public static void CopyArray (IntPtr src, string[] dest)
		{
			if (dest == null)
				throw new ArgumentNullException ("dest");

			for (int i = 0; i < dest.Length; i++)
				dest [i] = GetString (GetObjectArrayElement (src, i), JniHandleOwnership.TransferLocalRef)!;
		}

		static Dictionary<Type, Func<Type?, IntPtr, int, object?>>? nativeArrayElementToManaged;
		static Dictionary<Type, Func<Type?, IntPtr, int, object?>> NativeArrayElementToManaged {
			get {
				if (nativeArrayElementToManaged != null)
					return nativeArrayElementToManaged;

				var newValue = CreateNativeArrayElementToManaged ();
				Interlocked.CompareExchange (ref nativeArrayElementToManaged, newValue, null);
				return nativeArrayElementToManaged;
			}
		}

		static Dictionary<Type, Func<Type?, IntPtr, int, object?>> CreateNativeArrayElementToManaged ()
		{
			return new Dictionary<Type, Func<Type?, IntPtr, int, object?>> () {
				{ typeof (bool), (type, source, index) => {
					var r = new bool [1];
					_GetBooleanArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (byte), (type, source, index) => {
					var r = new byte [1];
					_GetByteArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (char), (type, source, index) => {
					var r = new char [1];
					_GetCharArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (short), (type, source, index) => {
					var r = new short [1];
					_GetShortArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (int), (type, source, index) => {
					var r = new int [1];
					_GetIntArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (long), (type, source, index) => {
					var r = new long [1];
					_GetLongArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (float), (type, source, index) => {
					var r = new float [1];
					_GetFloatArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (double), (type, source, index) => {
					var r = new double [1];
					_GetDoubleArrayRegion (source, index, 1, r);
					return r [0];
				} },
				{ typeof (string), (type, source, index) => {
					IntPtr elem = GetObjectArrayElement (source, index);
					if (type == typeof (Java.Lang.String))
						return new Java.Lang.String (elem, JniHandleOwnership.TransferLocalRef);
					return GetString (elem, JniHandleOwnership.TransferLocalRef);
				} },
				{ typeof (IJavaObject), (type, source, index) => {
					AssertIsJavaObject (type);

					IntPtr elem = GetObjectArrayElement (source, index);
					return Java.Lang.Object.GetObject (elem, JniHandleOwnership.TransferLocalRef, type);
				} },
				{ typeof (Array), (type, source, index) => {
					IntPtr  elem      = GetObjectArrayElement (source, index);
					return GetArray (elem, JniHandleOwnership.TransferLocalRef, type);
				} },
			};
		}

		static TValue GetConverter<TValue>(Dictionary<Type, TValue> dict, Type? elementType, IntPtr array)
		{
			TValue converter;

			if (elementType != null) {
				if (elementType.IsEnum)
					elementType = Enum.GetUnderlyingType (elementType);
				if (dict.TryGetValue (elementType, out converter))
					return converter;
			}

			if (array != IntPtr.Zero) {
				string type = GetClassNameFromInstance (array);
				if (type == null || type.Length < 1 || type [0] != '[')
					throw new InvalidOperationException ("Unsupported java array type: " + type);

				switch (type [1]) {
				case 'B': return dict [typeof (byte)];
				case 'C': return dict [typeof (char)];
				case 'D': return dict [typeof (double)];
				case 'F': return dict [typeof (float)];
				case 'I': return dict [typeof (int)];
				case 'J': return dict [typeof (long)];
				case 'S': return dict [typeof (short)];
				case 'Z': return dict [typeof (bool)];
				case '[':
					if (elementType == null || elementType.IsArray)
						return dict [typeof (Array)];
					break;
				}

				if (type == "[Ljava/lang/String;")
					return dict [typeof (string)];
			}

			if (elementType != null && elementType.IsArray)
				return dict [typeof (Array)];

			AssertIsJavaObject (elementType);
			return dict [typeof (IJavaObject)];
		}

		static unsafe void _GetBooleanArrayRegion (IntPtr array, int start, int length, bool[] buffer)
		{
			fixed (bool* p = buffer)
				JniEnvironment.Arrays.GetBooleanArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetByteArrayRegion (IntPtr array, int start, int length, byte[] buffer)
		{
			fixed (byte* p = buffer)
				JniEnvironment.Arrays.GetByteArrayRegion (new JniObjectReference (array), start, length, (sbyte*) p);
		}

		static unsafe void _GetCharArrayRegion (IntPtr array, int start, int length, char[] buffer)
		{
			fixed (char* p = buffer)
				JniEnvironment.Arrays.GetCharArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetShortArrayRegion (IntPtr array, int start, int length, short[] buffer)
		{
			fixed (short* p = buffer)
				JniEnvironment.Arrays.GetShortArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetIntArrayRegion (IntPtr array, int start, int length, int[] buffer)
		{
			fixed (int* p = buffer)
				JniEnvironment.Arrays.GetIntArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetLongArrayRegion (IntPtr array, int start, int length, long[] buffer)
		{
			fixed (long* p = buffer)
				JniEnvironment.Arrays.GetLongArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetFloatArrayRegion (IntPtr array, int start, int length, float[] buffer)
		{
			fixed (float* p = buffer)
				JniEnvironment.Arrays.GetFloatArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _GetDoubleArrayRegion (IntPtr array, int start, int length, double[] buffer)
		{
			fixed (double* p = buffer)
				JniEnvironment.Arrays.GetDoubleArrayRegion (new JniObjectReference (array), start, length, p);
		}

		public static void CopyArray (IntPtr src, Array dest, Type? elementType = null)
		{
			if (dest == null)
				throw new ArgumentNullException ("dest");

			if (elementType != null && elementType.IsValueType)
				AssertCompatibleArrayTypes (src, elementType.MakeArrayType ());

			if (elementType != null && elementType.IsArray) {
				for (int i = 0; i < dest.Length; ++i) {
					IntPtr a = GetObjectArrayElement (src, i);
					try {
						var d = (Array?) dest.GetValue (i);
						if (d == null)
							dest.SetValue (GetArray (a, JniHandleOwnership.DoNotTransfer, elementType.GetElementType ()), i);
						else
							CopyArray (a, d, elementType.GetElementType ());
					} finally {
						DeleteLocalRef (a);
					}
				}
				return;
			}

			var converter = GetConverter (NativeArrayElementToManaged, elementType, src);

			for (int i = 0; i < dest.Length; i++)
				dest.SetValue (converter (elementType, src, i), i);
		}

		static void AssertIsJavaObject (Type? targetType)
		{
			if (targetType != null && !typeof (IJavaObject).IsAssignableFrom (targetType))
				throw new NotSupportedException ("Don't know how to convert type '" + targetType.FullName + "' to an Android.Runtime.IJavaObject.");
		}

		public static void CopyArray<T> (IntPtr src, T[] dest)
		{
			if (dest == null)
				throw new ArgumentNullException ("dest");

			if (typeof (T).IsValueType)
				AssertCompatibleArrayTypes (src, typeof (T[]));

			if (typeof (T).IsArray) {
				CopyArray (src, dest, typeof (T));
				return;
			}

			var converter = GetConverter (NativeArrayElementToManaged, typeof (T), src);

			for (int i = 0; i < dest.Length; i++)
				dest [i] = (T) converter (typeof (T), src, i)!;
		}

		public static unsafe void CopyArray (bool[] src, IntPtr dest)
		{
			if (src == null)
				throw new ArgumentNullException ("src");

			AssertCompatibleArrayTypes (typeof (bool[]), dest);

			fixed (bool* p = src)
				JniEnvironment.Arrays.SetBooleanArrayRegion (new JniObjectReference (dest), 0, src.Length, p);
		}

		public static void CopyArray (string[] src, IntPtr dest)
		{
			if (src == null)
				throw new ArgumentNullException ("src");

			for (int i = 0; i < src.Length; i++) {
				IntPtr native = NewString (src [i]);
				JniEnvironment.Arrays.SetObjectArrayElement (new JniObjectReference (dest), i, new JniObjectReference (native));
				DeleteLocalRef (native);
			}
		}

		public static void CopyArray (IJavaObject[] src, IntPtr dest)
		{
			if (src == null)
				throw new ArgumentNullException ("src");

			for (int i = 0; i < src.Length; i++) {
				IJavaObject o = src [i];
				JniEnvironment.Arrays.SetObjectArrayElement (new JniObjectReference (dest), i, new JniObjectReference (o == null ? IntPtr.Zero : o.Handle));
			}
		}

		static Dictionary<Type, Action<Array, IntPtr>>? copyManagedToNativeArray;
		static Dictionary<Type, Action<Array, IntPtr>> CopyManagedToNativeArray {
			get {
				if (copyManagedToNativeArray != null)
					return copyManagedToNativeArray;

				var newValue = CreateCopyManagedToNativeArray ();
				Interlocked.CompareExchange (ref copyManagedToNativeArray, newValue, null);
				return copyManagedToNativeArray;
			}
		}

		static Dictionary<Type, Action<Array, IntPtr>> CreateCopyManagedToNativeArray ()
		{
			return new Dictionary<Type, Action<Array, IntPtr>> () {
				{ typeof (bool),        (source, dest) => CopyArray ((bool[]) source, dest) },
				{ typeof (byte),        (source, dest) => CopyArray ((byte[]) source, dest) },
				{ typeof (char),        (source, dest) => CopyArray ((char[]) source, dest) },
				{ typeof (short),       (source, dest) => CopyArray ((short[]) source, dest) },
				{ typeof (int),         (source, dest) => CopyArray ((int[]) source, dest) },
				{ typeof (long),        (source, dest) => CopyArray ((long[]) source, dest) },
				{ typeof (float),       (source, dest) => CopyArray ((float[]) source, dest) },
				{ typeof (double),      (source, dest) => CopyArray ((double[]) source, dest) },
				{ typeof (string),      (source, dest) => {
					var s = source as string[];
					if (s != null) {
						CopyArray (s, dest);
						return;
					}
					var ijo = source as IJavaObject[];
					if (ijo != null) {
						CopyArray (ijo, dest);
						return;
					}
					throw new NotSupportedException ("Don't know how to copy '" +
							source.GetType ().FullName + "' to '" +
							GetClassNameFromInstance (dest) +
							"'.");
				} },
				{ typeof (IJavaObject), (source, dest) => CopyArray ((IJavaObject[]) source, dest) },
				{ typeof (Array),       (source, dest) => {
					int len = source.GetLength (0);
					for (int i = 0; i < len; ++i) {
						IntPtr _dest    = GetObjectArrayElement (dest, i);
						Array  _source  = (Array) source.GetValue (i)!;
						CopyArray (_source, _source.GetType ().GetElementType ()!, _dest);
						JNIEnv.DeleteLocalRef (_dest);
					}
				} },
			};
		}

		public static void CopyArray (Array source, Type elementType, IntPtr dest)
		{
			if (source == null)
				throw new ArgumentNullException ("source");
			if (elementType == null)
				throw new ArgumentNullException ("elementType");

			if (elementType.IsValueType)
				AssertCompatibleArrayTypes (elementType.MakeArrayType (), dest);

			Action<Array, IntPtr> converter = GetConverter (CopyManagedToNativeArray, elementType, dest);

			converter (source, dest);
		}

		public static void CopyArray<T> (T[] src, IntPtr dest)
		{
			if (src == null)
				throw new ArgumentNullException ("src");

			CopyArray (src, typeof (T), dest);
		}

		public static Array? GetArray (IntPtr array_ptr, JniHandleOwnership transfer, Type? element_type = null)
		{
			try {
				return _GetArray (array_ptr, element_type);
			}
			finally {
				DeleteRef (array_ptr, transfer);
			}
		}

		static Dictionary<Type, Func<Type?, IntPtr, int, Array>>? nativeArrayToManaged;
		static Dictionary<Type, Func<Type?, IntPtr, int, Array>> NativeArrayToManaged {
			get {
				if (nativeArrayToManaged != null)
					return nativeArrayToManaged;

				var newValue = CreateNativeArrayToManaged ();
				Interlocked.CompareExchange (ref nativeArrayToManaged, newValue, null);
				return nativeArrayToManaged;
			}
		}

		static Dictionary<Type, Func<Type?, IntPtr, int, Array>> CreateNativeArrayToManaged ()
		{
			return new Dictionary<Type, Func<Type?, IntPtr, int, Array>> () {
				{ typeof (bool), (type, source, len) => {
					var r = new bool [len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (byte), (type, source, len) => {
					var r = new byte[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (char), (type, source, len) => {
					var r = new char [len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (short), (type, source, len) => {
					var r = new short [len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (ushort), (type, source, len) => {
					var r = new ushort [len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (int), (type, source, len) => {
					var r = new int[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (uint), (type, source, len) => {
					var r = new uint[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (long), (type, source, len) => {
					var r = new long[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (ulong), (type, source, len) => {
					var r = new ulong[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (float), (type, source, len) => {
					var r = new float[len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (double), (type, source, len) => {
					var r = new double [len];
					CopyArray (source, r);
					return r;
				} },
				{ typeof (string), (type, source, len) => {
					if (type != null && typeof (Java.Lang.Object).IsAssignableFrom (type)) {
						var r = new Java.Lang.String [len];
						CopyArray (source, r);
						return r;
					} else {
						var r = new string [len];
						CopyArray (source, r);
						return r;
					}
				} },
				{ typeof (IJavaObject), (type, source, len) => {
					var r = Array.CreateInstance (type!, len);
					CopyArray (source, r, type);
					return r;
				} },
				{ typeof (Array), (type, source, len) => {
					var r = Array.CreateInstance (type!, len);
					CopyArray (source, r, type);
					return r;
				} },
			};
		}

		static Array? _GetArray (IntPtr array_ptr, Type? element_type)
		{
			if (array_ptr == IntPtr.Zero)
				return null;

			if (element_type != null && element_type.IsValueType)
				AssertCompatibleArrayTypes (array_ptr, element_type.MakeArrayType ());

			int cnt = _GetArrayLength (array_ptr);

			var converter = GetConverter (NativeArrayToManaged, element_type, array_ptr);

			return converter (element_type, array_ptr, cnt);
		}

		static int _GetArrayLength (IntPtr array_ptr)
		{
			return JniEnvironment.Arrays.GetArrayLength (new JniObjectReference (array_ptr));
		}

		public static object?[]? GetObjectArray (IntPtr array_ptr, Type[] element_types)
		{
			if (array_ptr == IntPtr.Zero)
				return null;

			int cnt = _GetArrayLength (array_ptr);

			var converter = GetConverter (NativeArrayElementToManaged, null, array_ptr);

			object?[] ret = new object [cnt];

			for (int i = 0; i < cnt; i++) {
				Type? targetType	= (element_types != null && i < element_types.Length) ? element_types [i] : null;
				object? value    = converter ((targetType == null || targetType.IsValueType) ? null : targetType,
						array_ptr, i);

				ret [i] = value;
				ret [i] = targetType == null || targetType.IsInstanceOfType (value)
					? value
					: Convert.ChangeType (value, targetType);
			}

			return ret;
		}

		public static T[]? GetArray<T> (IntPtr array_ptr)
		{
			if (array_ptr == IntPtr.Zero)
				return null;

			if (typeof (T).IsValueType)
				AssertCompatibleArrayTypes (array_ptr, typeof (T[]));

			int cnt = _GetArrayLength (array_ptr);
			T[] ret = new T [cnt];
			CopyArray<T> (array_ptr, ret);
			return ret;
		}

		public static T[]? GetArray<T> (Java.Lang.Object[] array)
		{
			if (array == null)
				return null;
			T[] ret = new T [array.Length];
			for (int i = 0; i < array.Length; i++)
				ret [i] = JavaConvert.FromJavaObject<T> (array [i])!;
			return ret;
		}

		public static T GetArrayItem<T> (IntPtr array_ptr, int index)
		{
			if (array_ptr == IntPtr.Zero)
				throw new ArgumentException ("array_ptr");
			if (index < 0 || index >= GetArrayLength (array_ptr))
				throw new ArgumentOutOfRangeException ("index");

			var converter = GetConverter (NativeArrayElementToManaged, typeof (T), array_ptr);

			return (T) converter (typeof (T), array_ptr, index)!;
		}

		public static int GetArrayLength (IntPtr array_ptr)
		{
			if (array_ptr == IntPtr.Zero)
				return 0;
			return _GetArrayLength (array_ptr);
		}

		public static unsafe IntPtr NewArray (bool[]? array)
		{
			if (array == null)
				return IntPtr.Zero;
			IntPtr result;

			var r   = JniEnvironment.Arrays.NewBooleanArray (array.Length);
			fixed (bool* p = array)
				JniEnvironment.Arrays.SetBooleanArrayRegion (r, 0, array.Length, p);
			result  = r.Handle;

			return result;
		}

		public static IntPtr NewArray (string[]? array)
		{
			if (array == null)
				return IntPtr.Zero;

			IntPtr result = NewObjectArray (array.Length, Java.Lang.Class.String, IntPtr.Zero);
			CopyArray (array, result);

			return result;
		}

		// We do these translations here instead of in the binding to force a compile
		// time error if using an older Java.Interop.dll instead of a runtime error
		public static IntPtr NewArray (uint[] array) => NewArray ((int[])(object)array);

		public static IntPtr NewArray (ushort[] array) => NewArray ((short[])(object)array);

		public static IntPtr NewArray (ulong[] array) => NewArray ((long[])(object)array);

		public static IntPtr NewObjectArray (int length, IntPtr elementClass)
		{
			return NewObjectArray (length, elementClass, IntPtr.Zero);
		}

		public static IntPtr NewObjectArray (int length, IntPtr elementClass, IntPtr initialElement)
		{
			return JniEnvironment.Arrays.NewObjectArray (length, new JniObjectReference (elementClass), new JniObjectReference (initialElement)).Handle;
		}

		public static IntPtr NewObjectArray<T>(params T[]? values)
		{
			if (values == null)
				return IntPtr.Zero;

			IntPtr grefArrayElementClass = GetArrayElementClass (values);
			if (Java.Interop.TypeManager.GetClassName (grefArrayElementClass) == "mono/android/runtime/JavaObject") {
				DeleteGlobalRef (grefArrayElementClass);
				grefArrayElementClass = NewGlobalRef (Java.Lang.Class.Object);
			}
			try {
				IntPtr lrefArray = NewObjectArray (values.Length, grefArrayElementClass, IntPtr.Zero);

				for (int i = 0; i < values.Length; ++i) {
					JavaConvert.WithLocalJniHandle (values [i], lref => {
							SetObjectArrayElement (lrefArray, i, lref);
							return IntPtr.Zero;
					});
				}

				return lrefArray;
			}
			finally {
				DeleteGlobalRef (grefArrayElementClass);
			}

		}

		static IntPtr GetArrayElementClass<T>(T[] values)
		{
			Type    elementType = typeof (T);
			var jniClass    = JavaConvert.GetJniClassForType (elementType);
			if (jniClass != null) {
				return FindClass (jniClass);
			}

			if (elementType.IsValueType)
				return NewGlobalRef (Java.Lang.Class.Object);

			return FindClass (elementType);
		}

		public static void CopyObjectArray<T>(IntPtr source, T[] destination)
		{
			if (source == IntPtr.Zero)
				return;
			if (destination == null)
				throw new ArgumentNullException ("destination");

			int len = Math.Min (GetArrayLength (source), destination.Length);
			for (int i = 0; i < len; ++i) {
				IntPtr value = GetObjectArrayElement (source, i);
				destination [i] = JavaConvert.FromJniHandle<T>(value, JniHandleOwnership.TransferLocalRef)!;
			}
		}

		public static void CopyObjectArray<T>(T[]? source, IntPtr destination)
		{
			if (source == null)
				return;
			if (destination == IntPtr.Zero)
				throw new ArgumentException ("Destination is a null JNI handle!", "destination");

			int len = Math.Min (source.Length, GetArrayLength (destination));
			for (int i = 0; i < len; ++i) {
				JavaConvert.WithLocalJniHandle (source [i], lref => {
						SetObjectArrayElement (destination, i, lref);
						return IntPtr.Zero;
				});
			}
		}

		public static IntPtr NewArray (IJavaObject[]? array)
		{
			if (array == null)
				return IntPtr.Zero;

			IntPtr result;
			IntPtr grefClass = FindClass (array.GetType ().GetElementType ()!);
			try {
				result = NewObjectArray (array.Length, grefClass, IntPtr.Zero);
			} finally {
				DeleteGlobalRef (grefClass);
			}

			CopyArray (array, result);

			return result;
		}

		static Dictionary<Type, Func<Array, IntPtr>>? createManagedToNativeArray;
		static Dictionary<Type, Func<Array, IntPtr>> CreateManagedToNativeArray {
			get {
				if (createManagedToNativeArray != null)
					return createManagedToNativeArray;

				var newValue = CreateCreateManagedToNativeArray ();
				Interlocked.CompareExchange (ref createManagedToNativeArray, newValue, null);
				return createManagedToNativeArray;
			}
		}

		static Dictionary<Type, Func<Array, IntPtr>> CreateCreateManagedToNativeArray ()
		{
			return new Dictionary<Type, Func<Array, IntPtr>> () {
				{ typeof (bool),          (source) => NewArray ((bool[]) source) },
				{ typeof (byte),          (source) => NewArray ((byte[]) source) },
				{ typeof (char),          (source) => NewArray ((char[]) source) },
				{ typeof (short),         (source) => NewArray ((short[]) source) },
				{ typeof (int),           (source) => NewArray ((int[]) source) },
				{ typeof (long),          (source) => NewArray ((long[]) source) },
				{ typeof (float),         (source) => NewArray ((float[]) source) },
				{ typeof (double),        (source) => NewArray ((double[]) source) },
				{ typeof (string),        (source) => NewArray ((string[]) source) },
				{ typeof (IJavaObject),   (source) => NewArray ((IJavaObject[]) source) },
				{ typeof (Array),         (source) => NewArray (source) },
			};
		}

		public static IntPtr NewArray (Array value, Type? elementType = null)
		{
			if (value == null)
				throw new ArgumentNullException ("value");

			elementType = elementType ?? value.GetType ().GetElementType ()!;

			if (elementType.IsArray) {
				IntPtr array = IntPtr.Zero;
				IntPtr grefArrayClass = FindClass (elementType);
				try {
					array = NewObjectArray (value.Length, grefArrayClass, IntPtr.Zero);

					for (int i = 0; i < value.Length; ++i) {
						IntPtr subarray = NewArray ((Array) value.GetValue (i)!, elementType.GetElementType ());
						SetObjectArrayElement (array, i, subarray);
						DeleteLocalRef (subarray);
					}

					return array;
				} catch {
					DeleteLocalRef (array);
					throw;
				} finally {
					DeleteGlobalRef (grefArrayClass);
				}
			}

			Func<Array, IntPtr> creator = GetConverter (CreateManagedToNativeArray, elementType, IntPtr.Zero);

			return creator (value);
		}

		public static IntPtr NewArray<T> (T[]? array)
		{
			if (array == null)
				return IntPtr.Zero;

			if (typeof (T).IsArray) {
				return NewArray (array, typeof (T));
			}

			Func<Array, IntPtr> creator = GetConverter (CreateManagedToNativeArray, typeof (T), IntPtr.Zero);

			return creator (array);
		}

		static Dictionary<Type, Action<IntPtr, int, object?>>? setNativeArrayElement;
		static Dictionary<Type, Action<IntPtr, int, object?>> SetNativeArrayElement {
			get {
				if (setNativeArrayElement != null)
					return setNativeArrayElement;

				var newValue = CreateSetNativeArrayElement ();
				Interlocked.CompareExchange (ref setNativeArrayElement, newValue, null);
				return setNativeArrayElement;
			}
		}

		static Dictionary<Type, Action<IntPtr, int, object?>> CreateSetNativeArrayElement ()
		{
			return new Dictionary<Type, Action<IntPtr, int, object?>> () {
				{ typeof (bool), (dest, index, value) => {
					var _value = new[]{(bool) value!};
					_SetBooleanArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (byte), (dest, index, value) => {
					var _value = new[]{(byte) value!};
					_SetByteArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (char), (dest, index, value) => {
					var _value = new[]{(char) value!};
					_SetCharArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (short), (dest, index, value) => {
					var _value = new[]{(short) value!};
					_SetShortArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (int), (dest, index, value) => {
					var _value = new[]{(int) value!};
					_SetIntArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (long), (dest, index, value) => {
					var _value = new[]{(long) value!};
					_SetLongArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (float), (dest, index, value) => {
					var _value = new[]{(float) value!};
					_SetFloatArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (double), (dest, index, value) => {
					var _value = new[]{(double) value!};
					_SetDoubleArrayRegion (dest, index, _value.Length, _value);
				} },
				{ typeof (string), (dest, index, value) => {
					IntPtr s = NewString (value!.ToString ());
					try {
						SetObjectArrayElement (dest, index, s);
					} finally {
						DeleteLocalRef (s);
					}
				} },
				{ typeof (IJavaObject), (dest, index, value) => {
					SetObjectArrayElement (dest, index, value == null ? IntPtr.Zero : ((IJavaObject) value).Handle);
				} },
				{ typeof (Array), (dest, index, value) => {
					IntPtr _v = NewArray ((Array) value!);
					SetObjectArrayElement (dest, index, _v);
					JNIEnv.DeleteLocalRef (_v);
				} },
			};
		}

		static unsafe void _SetBooleanArrayRegion (IntPtr array, int start, int length, bool[] buffer)
		{
			fixed (bool* p = buffer)
				JniEnvironment.Arrays.SetBooleanArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetByteArrayRegion (IntPtr array, int start, int length, byte[] buffer)
		{
			fixed (byte* p = buffer)
				JniEnvironment.Arrays.SetByteArrayRegion (new JniObjectReference (array), start, length, (sbyte*) p);
		}

		static unsafe void _SetCharArrayRegion (IntPtr array, int start, int length, char[] buffer)
		{
			fixed (char* p = buffer)
				JniEnvironment.Arrays.SetCharArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetShortArrayRegion (IntPtr array, int start, int length, short[] buffer)
		{
			fixed (short* p = buffer)
				JniEnvironment.Arrays.SetShortArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetIntArrayRegion (IntPtr array, int start, int length, int[] buffer)
		{
			fixed (int* p = buffer)
				JniEnvironment.Arrays.SetIntArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetLongArrayRegion (IntPtr array, int start, int length, long[] buffer)
		{
			fixed (long* p = buffer)
				JniEnvironment.Arrays.SetLongArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetFloatArrayRegion (IntPtr array, int start, int length, float[] buffer)
		{
			fixed (float* p = buffer)
				JniEnvironment.Arrays.SetFloatArrayRegion (new JniObjectReference (array), start, length, p);
		}

		static unsafe void _SetDoubleArrayRegion (IntPtr array, int start, int length, double[] buffer)
		{
			fixed (double* p = buffer)
				JniEnvironment.Arrays.SetDoubleArrayRegion (new JniObjectReference (array), start, length, p);
		}

		public static void SetArrayItem<T> (IntPtr array_ptr, int index, T value)
		{
			if (array_ptr == IntPtr.Zero)
				throw new ArgumentException ("array_ptr");
			if (index < 0 || index >= GetArrayLength (array_ptr))
				throw new ArgumentOutOfRangeException ("index");

			var setter = GetConverter (SetNativeArrayElement, typeof (T), array_ptr);

			setter (array_ptr, index, value);
		}

		[return: NotNullIfNotNull (parameterName: "array")]
		public static Java.Lang.Object[]? ToObjectArray<T> (T[]? array)
		{
			if (array == null)
				return null;
			Java.Lang.Object[] ret = new Java.Lang.Object [array.Length];
			for (int i = 0; i < array.Length; i++)
				ret [i] = JavaObjectExtensions.JavaCast<Java.Lang.Object>(JavaConvert.ToJavaObject (array [i]))!;
			return ret;
		}

		public static unsafe void CopyArray (IntPtr src, uint[] dest)
		{
			if (src == IntPtr.Zero)
				return;
			fixed (uint* __p = dest)
				JniEnvironment.Arrays.GetIntArrayRegion (new JniObjectReference (src), 0, dest.Length, (int*) __p);
		}

		public static unsafe void CopyArray (IntPtr src, ushort[] dest)
		{
			if (src == IntPtr.Zero)
				return;
			fixed (ushort* __p = dest)
				JniEnvironment.Arrays.GetShortArrayRegion (new JniObjectReference (src), 0, dest.Length, (short*) __p);
		}

		public static unsafe void CopyArray (IntPtr src, ulong[] dest)
		{
			if (src == IntPtr.Zero)
				return;
			fixed (ulong* __p = dest)
				JniEnvironment.Arrays.GetLongArrayRegion (new JniObjectReference (src), 0, dest.Length, (long*) __p);
		}
#if ANDROID_8
		[DllImport ("libjnigraphics.so")]
		static extern int AndroidBitmap_getInfo (IntPtr env, IntPtr jbitmap, out Android.Graphics.AndroidBitmapInfo info);

		[DllImport ("libjnigraphics.so")]
		static extern int AndroidBitmap_lockPixels (IntPtr env, IntPtr jbitmap, out IntPtr addrPtr);

		[DllImport ("libjnigraphics.so")]
		static extern int AndroidBitmap_unlockPixels(IntPtr env, IntPtr jbitmap);

		internal static int AndroidBitmap_getInfo (IntPtr jbitmap, out Android.Graphics.AndroidBitmapInfo info)
		{
			return AndroidBitmap_getInfo (Handle, jbitmap, out info);
		}

		internal static int AndroidBitmap_lockPixels (IntPtr jbitmap, out IntPtr addrPtr)
		{
			return AndroidBitmap_lockPixels (Handle, jbitmap, out addrPtr);
		}

		internal static int AndroidBitmap_unlockPixels (IntPtr jbitmap)
		{
			return AndroidBitmap_unlockPixels (Handle, jbitmap);
		}
#endif  // ANDROID_8
	}
}
