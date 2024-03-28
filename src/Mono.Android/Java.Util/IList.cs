using System;
using Android.Runtime;

namespace Java.Util;

#if ANDROID_35
public partial interface IList
{
	// This gets generated as:
	// return Android.Runtime.JavaList.FromJniHandle (__rm.Handle, JniHandleOwnership.TransferLocalRef)!;
	// instead of:
	// return global::Java.Lang.Object.GetObject<Java.Util.ISequencedCollection> (__rm.Handle, JniHandleOwnership.TransferLocalRef)!;
	private static Delegate? cb_reversed;

#pragma warning disable 0169
	[global::System.Runtime.Versioning.SupportedOSPlatformAttribute ("android35.0")]
	private static Delegate GetReversedHandler ()
	{
		if (cb_reversed == null)
			cb_reversed = JNINativeWrapper.CreateDelegate (new _JniMarshal_PP_L (n_Reversed));
		return cb_reversed;
	}

	[global::System.Runtime.Versioning.SupportedOSPlatformAttribute ("android35.0")]
	private static IntPtr n_Reversed (IntPtr jnienv, IntPtr native__this)
	{
		var __this = global::Java.Lang.Object.GetObject<Java.Util.IList> (jnienv, native__this, JniHandleOwnership.DoNotTransfer)!;
		return JNIEnv.ToLocalJniHandle (__this.Reversed ());
	}
#pragma warning restore 0169

	// Metadata.xml XPath method reference: path="/api/package[@name='java.util']/interface[@name='List']/method[@name='reversed' and count(parameter)=0]"
	[global::System.Runtime.Versioning.SupportedOSPlatformAttribute ("android35.0")]
	[Register ("reversed", "()Ljava/util/List;", "GetReversedHandler:Java.Util.IList, Mono.Android, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", ApiSince = 35)]
	unsafe Java.Util.ISequencedCollection Java.Util.ISequencedCollection.Reversed ()
	{
		const string __id = "reversed.()Ljava/util/List;";
		try {
			var __rm = _members.InstanceMethods.InvokeVirtualObjectMethod (__id, this, null);
			return global::Java.Lang.Object.GetObject<Java.Util.ISequencedCollection> (__rm.Handle, JniHandleOwnership.TransferLocalRef)!;
		} finally {
		}
	}
}
#endif
