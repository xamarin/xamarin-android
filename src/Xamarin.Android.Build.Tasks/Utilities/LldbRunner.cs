using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Build.Utilities;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	class LldbRunner : ToolRunner
	{
		bool needPythonEnvvars;
		string scriptPath;

		public LldbRunner (TaskLoggingHelper logger, string lldbPath, string lldbScriptPath)
			: base (logger, lldbPath)
		{
			// If we're invoking the executable directly, we need to set up Python environment variables or lldb won't run
			string ext = Path.GetExtension (lldbPath);
			if (String.Compare (".sh", ext, StringComparison.OrdinalIgnoreCase) == 0 ||
			    String.Compare (".cmd", ext, StringComparison.OrdinalIgnoreCase) == 0 ||
			    String.Compare (".bat", ext, StringComparison.OrdinalIgnoreCase) == 0) {
				needPythonEnvvars = false;
			} else {
				needPythonEnvvars = true;
			}

			scriptPath = lldbScriptPath;

			EchoStandardError = false;
			EchoStandardOutput = false;
			ProcessTimeout = TimeSpan.MaxValue;
		}

		public bool Run ()
		{
			var runner = CreateProcessRunner ("--source", scriptPath);
			if (!OS.IsWindows) {
				// lldb bundled with the NDK is unable to find it on its own.
				// It searches for the database at "/buildbot/src/android/llvm-toolchain/out/lib/libncurses-linux-install/share/terminfo"
				runner.Environment.Add ("TERMINFO", "/usr/share/terminfo");
			}

			if (needPythonEnvvars) {
				// We assume our LLDB path is within the NDK root
				string pythonDir = Path.GetFullPath (Path.Combine (Path.GetDirectoryName (ToolPath), "..", "python3"));
				runner.Environment.Add ("PYTHONHOME", pythonDir);

				if (!OS.IsWindows) {
					string envvarName = OS.IsMac ? "DYLD_LIBRARY_PATH" : "LD_LIBRARY_PATH";
					string oldLibraryPath = Environment.GetEnvironmentVariable (envvarName) ?? String.Empty;
					string pythonLibDir = Path.Combine (pythonDir, "lib");
					runner.Environment.Add (envvarName, $"{pythonLibDir}:${oldLibraryPath}");
				}
			}

			try {
				return runner.Run ();
			} catch (Exception ex) {
				Logger.LogWarning ("LLDB failed with exception");
				Logger.LogWarningFromException (ex);
				return false;
			}
		}
	}
}
