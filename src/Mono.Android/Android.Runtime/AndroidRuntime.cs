using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Reflection;

using Java.Interop;
using Java.Interop.Tools.TypeNameMappings;

#if JAVA_INTEROP
namespace Android.Runtime {

	class AndroidRuntime : JniRuntime {

		internal AndroidRuntime (IntPtr jnienv, IntPtr vm, bool allocNewObjectSupported, IntPtr classLoader, IntPtr classLoader_loadClass)
			: base (new AndroidRuntimeOptions (jnienv, vm, allocNewObjectSupported, classLoader, classLoader_loadClass))
		{
		}

		public override void FailFast (string message)
		{
			AndroidEnvironment.FailFast (message);
		}

		public override string GetCurrentManagedThreadName ()
		{
			return Thread.CurrentThread.Name;
		}

		public override string GetCurrentManagedThreadStackTrace (int skipFrames, bool fNeedFileInfo)
		{
			return new StackTrace (skipFrames, fNeedFileInfo)
				.ToString ();
		}

		public override Exception GetExceptionForThrowable (ref JniObjectReference value, JniObjectReferenceOptions transfer)
		{
			var throwable = Java.Lang.Object.GetObject<Java.Lang.Throwable>(value.Handle, JniHandleOwnership.DoNotTransfer);
			JniObjectReference.Dispose (ref value, transfer);
			var p = throwable as JavaProxyThrowable;
			if (p != null)
				return p.InnerException;
			return throwable;
		}

		public override void RaisePendingException (Exception pendingException)
		{
			var je  = pendingException as JavaProxyThrowable;
			if (je == null) {
				je  = new JavaProxyThrowable (pendingException);
			}
			var r = new JniObjectReference (je.Handle);
			JniEnvironment.Exceptions.Throw (r);
		}
	}

	class AndroidRuntimeOptions : JniRuntime.CreationOptions {

		public AndroidRuntimeOptions (IntPtr jnienv, IntPtr vm, bool allocNewObjectSupported, IntPtr classLoader, IntPtr classLoader_loadClass)
		{
			EnvironmentPointer      = jnienv;
			ClassLoader             = new JniObjectReference (classLoader, JniObjectReferenceType.Global);
			ClassLoader_LoadClass_id= classLoader_loadClass;
			InvocationPointer       = vm;
			NewObjectRequired       = !allocNewObjectSupported;
			ObjectReferenceManager  = new AndroidObjectReferenceManager ();
			TypeManager             = new AndroidTypeManager ();
			ValueManager            = new AndroidValueManager ();
			UseMarshalMemberBuilder = false;
		}
	}

	class AndroidObjectReferenceManager : JniRuntime.JniObjectReferenceManager {

		[DllImport ("__Internal")]
		static extern int _monodroid_gref_get ();

		public override int GlobalReferenceCount {
			get {return _monodroid_gref_get ();}
		}

		public override int WeakGlobalReferenceCount {
			get {return -1;}
		}

		public override JniObjectReference CreateLocalReference (JniObjectReference value, ref int localReferenceCount)
		{
			var r = base.CreateLocalReference (value, ref localReferenceCount);

			if (Logger.LogLocalRef) {
				var tname = Thread.CurrentThread.Name;
				var tid   = Thread.CurrentThread.ManagedThreadId;;
				var from  = new StringBuilder (new StackTrace (true).ToString ());
				JNIEnv._monodroid_lref_log_new (localReferenceCount, r.Handle, (byte) 'L', tname, tid, from, 1);
			}

			return r;
		}

		public override void DeleteLocalReference (ref JniObjectReference value, ref int localReferenceCount)
		{
			if (Logger.LogLocalRef) {
				var tname = Thread.CurrentThread.Name;
				var tid   = Thread.CurrentThread.ManagedThreadId;;
				var from  = new StringBuilder (new StackTrace (true).ToString ());
				JNIEnv._monodroid_lref_log_delete (localReferenceCount-1, value.Handle, (byte) 'L', tname, tid, from, 1);
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
				JNIEnv._monodroid_lref_log_new (localReferenceCount, value.Handle, (byte) 'L', tname, tid, from, 1);
			}
		}

		public override IntPtr ReleaseLocalReference (ref JniObjectReference value, ref int localReferenceCount)
		{
			var r = base.ReleaseLocalReference (ref value, ref localReferenceCount);
			if (Logger.LogLocalRef) {
				var tname = Thread.CurrentThread.Name;
				var tid   = Thread.CurrentThread.ManagedThreadId;;
				var from  = new StringBuilder (new StackTrace (true).ToString ());
				JNIEnv._monodroid_lref_log_delete (localReferenceCount-1, value.Handle, (byte) 'L', tname, tid, from, 1);
			}
			return r;
		}

		public override void WriteGlobalReferenceLine (string format, params object[] args)
		{
			JNIEnv._monodroid_gref_log (string.Format (format, args));
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
			int gc 		= JNIEnv._monodroid_gref_log_new (value.Handle, ctype, r.Handle, ntype, tname, tid, from, 1);
			if (gc >= JNIEnv.gref_gc_threshold) {
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
			JNIEnv._monodroid_gref_log_delete (value.Handle, ctype, tname, tid, from, 1);

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
			JNIEnv._monodroid_weak_gref_new (value.Handle, ctype, r.Handle, ntype, tname, tid, from, 1);

			return r;
		}

		public override void DeleteWeakGlobalReference (ref JniObjectReference value)
		{
			var log		= Logger.LogGlobalRef;
			var ctype	= log ? GetObjectRefType (value.Type) : (byte) '*';
			var tname = log ? Thread.CurrentThread.Name : null;
			var tid   = log ? Thread.CurrentThread.ManagedThreadId : 0;
			var from  = log ? new StringBuilder (new StackTrace (true).ToString ()) : null;
			JNIEnv._monodroid_weak_gref_delete (value.Handle, ctype, tname, tid, from, 1);

			base.DeleteWeakGlobalReference (ref value);
		}
	}

	class AndroidTypeManager : JniRuntime.JniTypeManager {

		protected override IEnumerable<Type> GetTypesForSimpleReference (string jniSimpleReference)
		{
			var t = Java.Interop.TypeManager.GetJavaToManagedType (jniSimpleReference);
			if (t == null)
				return base.GetTypesForSimpleReference (jniSimpleReference);
			return base.GetTypesForSimpleReference (jniSimpleReference)
				.Concat (Enumerable.Repeat (t, 1));
		}

		protected override IEnumerable<string> GetSimpleReferences (Type type)
		{
			var j = JNIEnv.GetJniName (type);
			if (j == null)
				return base.GetSimpleReferences (type);
			return base.GetSimpleReferences (type)
				.Concat (Enumerable.Repeat (j, 1));
		}

		delegate Delegate GetCallbackHandler ();

		static MethodInfo dynamic_callback_gen;

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
			return (Delegate)dynamic_callback_gen.Invoke (null, new object [] { method });
		}

		static List<JniNativeMethodRegistration> sharedRegistrations = new List<JniNativeMethodRegistration> ();

		static bool FastRegisterNativeMembers (JniType nativeClass, Type type, string methods)
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
				JniNativeMethodRegistrationArguments arguments = new JniNativeMethodRegistrationArguments (registrations, methods);
				rv = MagicRegistrationMap.CallRegisterMethod (arguments, type.FullName);

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
			// should stay in sync with MonoDroidMarkStep.GetStringHashCode
			static int GetStringHashCode (string str)
	                {
	                        int hash1 = 5381;
	                        int hash2 = hash1;

	                        unsafe {
	                                fixed (char *src = str) {
	                                        int c;
	                                        char *s = src;
	                                        while ((c = s[0]) != 0) {
	                                                hash1 = ((hash1 << 5) + hash1) ^ c;
	                                                c = s [1];
	                                                if (c == 0)
	                                                        break;
	                                                hash2 = ((hash2 << 5) + hash2) ^ c;
	                                                s += 2;
	                                        }
	                                }
	                        }

	                        return hash1 + (hash2 * 1566083941);
	                }

			static MagicRegistrationMap ()
			{
			}

			static public bool Filled {
				get {
					return false;
				}
			}

			internal static bool CallRegisterMethod (JniNativeMethodRegistrationArguments arguments, string typeName)
			{
				if (typeName == null)
					return false;

				return CallRegisterMethodByTypeName (arguments, typeName);
			}

			static bool CallRegisterMethodByTypeName (JniNativeMethodRegistrationArguments arguments, string name)
			{
				int hashCode;

				// updated by the linker to register known types

				return false;
			}
		}

		public override void RegisterNativeMembers (JniType jniType, Type type, string methods)
		{
			if (FastRegisterNativeMembers (jniType, type, methods))
				return;

			if (methods == null)
				return;

			string[] members = methods.Split ('\n');
			if (members.Length == 0)
				return;

			JniNativeMethodRegistration[] natives = new JniNativeMethodRegistration [members.Length-1];
			for (int i = 0; i < members.Length; ++i) {
				string method = members [i];
				if (string.IsNullOrEmpty (method))
					continue;
				string[] toks = members [i].Split (new[]{':'}, 4);
				Delegate callback;
				if (toks [2] == "__export__") {
					var mname = toks [0].Substring (2);
					var minfo = type.GetMethods (BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where (m => m.Name == mname && JavaNativeTypeManager.GetJniSignature (m) == toks [1]).FirstOrDefault ();
					if (minfo == null)
						throw new InvalidOperationException (String.Format ("Specified managed method '{0}' was not found. Signature: {1}", mname, toks [1]));
					callback = CreateDynamicCallback (minfo);
				} else {
					GetCallbackHandler connector = (GetCallbackHandler) Delegate.CreateDelegate (typeof (GetCallbackHandler), 
						toks.Length == 4 ? Type.GetType (toks [3], true) : type, toks [2]);
					callback = connector ();
				}
				natives [i] = new JniNativeMethodRegistration (toks [0], toks [1], callback);
			}

			JniEnvironment.Types.RegisterNatives (jniType.PeerReference, natives, natives.Length);
		}
	}

	class AndroidValueManager : JniRuntime.JniValueManager {

		public override void WaitForGCBridgeProcessing ()
		{
			JNIEnv.WaitForBridgeProcessing ();
		}

		public override void AddPeer (IJavaPeerable value)
		{
		}

		public override void RemovePeer (IJavaPeerable value)
		{
		}

		public override IJavaPeerable PeekPeer (JniObjectReference reference)
		{
			return (IJavaPeerable) Java.Lang.Object.GetObject (reference.Handle, JniHandleOwnership.DoNotTransfer);
		}

		public override void CollectPeers ()
		{
		}

		public override void FinalizePeer (IJavaPeerable value)
		{
		}

		public override List<JniSurfacedPeerInfo> GetSurfacedPeers ()
		{
			return null;
		}
	}
}
#endif // JAVA_INTEROP
