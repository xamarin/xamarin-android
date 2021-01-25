#if LINUX || MACOS
using System;
using System.Collections.Generic;

using Mono.Unix.Native;
using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	partial class OS
	{
		const FilePermissions ExecutableBits = FilePermissions.S_IXUSR | FilePermissions.S_IXGRP | FilePermissions.S_IXOTH;
		List<string>? ExecutableExtensions => null;

		void InitOS ()
		{}

		public static string AppendExecutableExtension (string filePath)
		{
			return filePath;
		}

		public static string AssertIsExecutable (string filePath)
		{
			if (!IsExecutable (filePath)) {
				throw new InvalidOperationException ($"File '{filePath}' is not executable");
			}
			return filePath;
		}

		public static bool IsExecutable (string filePath)
		{
			if (!IsExecutableEnsureValidFile (filePath)) {
				return false;
			}

			Stat sbuf;
			int ret = Syscall.stat (filePath, out sbuf);

			if (ret < 0) {
				Log.Instance.WarningLine ($"Failed to stat file '{filePath}': {Stdlib.strerror (Stdlib.GetLastError ())}");
				return false;
			}

			if ((sbuf.st_mode & ExecutableBits) == 0) {
				Log.Instance.DebugLine ($"File '{filePath}' is not executable");
				return false;
			}

			return true;
		}

		public string GetManagedProgramRunner (string programPath)
		{
			if (String.IsNullOrEmpty (programPath))
				return String.Empty;

			if (programPath.EndsWith (".exe", FilePathComparison) || programPath.EndsWith (".dll", FilePathComparison))
				return "mono"; // Caller will find the exact mono executable, we just provide a name

			return String.Empty;
		}
	}
}
#endif
