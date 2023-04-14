using System;
using System.Collections.Generic;
using System.IO;

using Xamarin.Android.Utilities;
using Xamarin.Android.Tasks;

namespace Xamarin.Android.Debug;

class AndroidNdk
{
	// We want the shell/batch scripts first, since they set up Python environment for the debugger
	static readonly string[] lldbNames = {
		"lldb.sh",
		"lldb",
		"lldb.cmd",
		"lldb.exe",
	};

	Dictionary<string, string> hostLldbServerPaths;
	XamarinLoggingHelper log;
	string? lldbPath;

	public string LldbPath => lldbPath ?? String.Empty;

	public AndroidNdk (XamarinLoggingHelper log, string ndkRootPath, List<string> supportedAbis)
	{
		this.log = log;
		hostLldbServerPaths = new Dictionary<string, string> (StringComparer.Ordinal);

		if (!FindTools (ndkRootPath, supportedAbis)) {
			throw new InvalidOperationException ("Failed to find all the required NDK tools");
		}
	}

	public string? GetDebugServerPath (string abi)
	{
		if (!hostLldbServerPaths.TryGetValue (abi, out string? debugServerPath) || String.IsNullOrEmpty (debugServerPath)) {
			log.ErrorLine ($"Debug server for abi '{abi}' not found.");
			return null;
		}

		return debugServerPath;
	}

	bool FindTools (string ndkRootPath, List<string> supportedAbis)
	{
		string toolchainDir = Path.Combine (ndkRootPath, NdkHelper.RelativeToolchainDir);
		string toolchainBinDir = Path.Combine (toolchainDir, "bin");
		string? path = null;

		foreach (string lldb in lldbNames) {
			path = Path.Combine (toolchainBinDir, lldb);
			if (File.Exists (path)) {
				break;
			}
		}

		if (String.IsNullOrEmpty (path)) {
			log.ErrorLine ($"Unable to locate lldb executable in '{toolchainBinDir}'");
			return false;
		}
		lldbPath = path;

		hostLldbServerPaths = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);
		string llvmVersion = GetLlvmVersion (toolchainDir);
		foreach (string abi in supportedAbis) {
			string llvmAbi = NdkHelper.TranslateAbiToLLVM (abi);
			path = Path.Combine (ndkRootPath, NdkHelper.RelativeToolchainDir, "lib64", "clang", llvmVersion, "lib", "linux", llvmAbi, "lldb-server");
			if (!File.Exists (path)) {
				log.ErrorLine ($"LLVM lldb server component for ABI '{abi}' not found at '{path}'");
				return false;
			}

			hostLldbServerPaths.Add (abi, path);
		}

		if (hostLldbServerPaths.Count == 0) {
			log.ErrorLine ("Unable to find any lldb-server executables, debugging not possible");
			return false;
		}

		return true;
	}

	string GetLlvmVersion (string toolchainDir)
	{
		string path = Path.Combine (toolchainDir, "AndroidVersion.txt");
		if (!File.Exists (path)) {
			throw new InvalidOperationException ($"LLVM version file not found at '{path}'");
		}

		string[] lines = File.ReadAllLines (path);
		string? line = lines.Length >= 1 ? lines[0].Trim () : null;
		if (String.IsNullOrEmpty (line)) {
			throw new InvalidOperationException ($"Unable to read LLVM version from '{path}'");
		}

		return line;
	}
}
