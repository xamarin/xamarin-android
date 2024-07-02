using System;
using System.Collections.Generic;
using System.Linq;

using Android.App;
using Android.Content;
using Android.Runtime;

using Java.Interop;

using NUnit.Framework;

namespace Java.InteropTests {

	[TestFixture]
	public class JavaObjectExtensionsTests {

		[Test]
		public void JavaCast_BaseToGenericWrapper ()
		{
			using (var list = new JavaList (new[]{ 1, 2, 3 }))
			using (var generic = JavaObjectExtensions.JavaCast<JavaList<int>> (list)) {
				// Yay, no exceptions!
				Assert.AreEqual (1, generic [0]);
			}
		}

		[Test]
		public void JavaCast_InterfaceCast ()
		{
			IntPtr g;
			using (var n = new Java.Lang.Integer (42)) {
				g = JNIEnv.NewGlobalRef (n.Handle);
			}
			// We want a Java.Lang.Object so that we create an IComparableInvoker
			// instead of just getting back the original instance.
			using (var o = Java.Lang.Object.GetObject<Java.Lang.Object> (g, JniHandleOwnership.TransferGlobalRef)) {
				var c = JavaObjectExtensions.JavaCast<Java.Lang.IComparable> (o);
				c.Dispose ();
			}
		}

		[Test]
		public void JavaCast_InvalidTypeCastThrows ()
		{
			using (var s = new Java.Lang.String ("value")) {
				Assert.Throws<InvalidCastException> (() => JavaObjectExtensions.JavaCast<Java.Lang.Integer> (s));
			}
		}

		[Test]
		public void JavaCast_CheckForManagedSubclasses ()
		{
			using (var o = CreateObject ()) {
				Assert.Throws<InvalidCastException> (() => JavaObjectExtensions.JavaCast<MyObject> (o));
			}
		}

		[Test]
		public void JavaAs ()
		{
			using var v     = new Java.InteropTests.MyJavaInterfaceImpl ();
			using var c     = v.JavaAs<Java.Lang.ICloneable>();
			Assert.IsNotNull (c);
		}

		static Java.Lang.Object CreateObject ()
		{
			var ctor    = JNIEnv.GetMethodID (Java.Lang.Class.Object, "<init>", "()V");
			var value   = JNIEnv.NewObject (Java.Lang.Class.Object, ctor);
			return new Java.Lang.Object (value, JniHandleOwnership.TransferLocalRef);
		}
	}

	class MyObject : Java.Lang.Object, Java.Lang.ICloneable {

		public MyObject ()
		{
		}

		public MyObject (IntPtr handle, JniHandleOwnership transfer)
			: base (handle, transfer)
		{
		}
	}
}

