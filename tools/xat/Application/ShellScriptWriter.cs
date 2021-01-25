using System;
using System.IO;

namespace Xamarin.Android.Tests
{
	/// <summary>
	///   Base class for shell script writers/generators.
	/// </summary>
	abstract class ShellScriptWriter : IDisposable
	{
		StreamWriter sw;
		bool alreadyDisposed;

		protected StreamWriter Writer => sw;

		protected ShellScriptWriter (string outputPath)
			: this (File.Open (outputPath, FileMode.Create, FileAccess.Write, FileShare.Read), ownStream: true)
		{}

		protected ShellScriptWriter (Stream output, bool ownStream = false)
		{
			sw = new StreamWriter (output, encoding: null, bufferSize: -1, leaveOpen: !ownStream);
		}

		public abstract void WriteHeader ();
		public abstract void Write (TestCommand command);

		protected virtual void Dispose (bool disposing)
		{
			if (!alreadyDisposed) {
				if (disposing) {
					sw.Dispose ();
				}

				alreadyDisposed = true;
			}
		}

		public void Dispose()
		{
			Dispose (disposing: true);
			GC.SuppressFinalize (this);
		}
	}
}
