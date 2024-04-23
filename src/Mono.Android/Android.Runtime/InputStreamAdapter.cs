using System;
using System.IO;

namespace Android.Runtime {

	[Register ("mono/android/runtime/InputStreamAdapter")]
	public sealed class InputStreamAdapter : Java.IO.InputStream {

		public Stream BaseStream {get; private set;}

		InputStreamAdapter () {
			Console.WriteLine ("InputStreamAdapter () invoked");
			Console.WriteLine (new System.Diagnostics.StackTrace(true));
		}

		public InputStreamAdapter (System.IO.Stream stream)
			: base (
					JNIEnv.StartCreateInstance ("mono/android/runtime/InputStreamAdapter", "()V"),
					JniHandleOwnership.TransferLocalRef)
		{
			Console.WriteLine ("InputStreamAdapter (System.IO.Stream) invoked");
			Console.WriteLine (new System.Diagnostics.StackTrace(true));
			JNIEnv.FinishCreateInstance (Handle, "()V");

			this.BaseStream = stream;
		}

		public override void Close ()
		{
			BaseStream.Close ();
		}

		public override int Read ()
		{
			return BaseStream.ReadByte ();
		}

		public override int Read (byte[] bytes)
		{
			return Read (bytes, 0, bytes.Length);
		}

		public override int Read (byte[] bytes, int offset, int length)
		{
			int res = BaseStream.Read (bytes, offset, length);
			if (res == 0)
				return -1;
			return res;
		}

		public static IntPtr ToLocalJniHandle (Stream? value)
		{
			if (value == null)
				return IntPtr.Zero;

			var v = value as InputStreamInvoker;
			if (v != null)
				return JNIEnv.ToLocalJniHandle (v.BaseInputStream);

			return JNIEnv.ToLocalJniHandle (new InputStreamAdapter (value));
		}
	}
}
