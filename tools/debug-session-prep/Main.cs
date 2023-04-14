using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Mono.Options;
using Xamarin.Android.Utilities;

namespace Xamarin.Debug.Session.Prep;

class App
{
	const string DefaultAdbPath = "adb";

	static readonly Dictionary<string, string> SupportedAbiMap = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase) {
		{"arm32",       "armeabi-v7a"},
		{"arm64",       "arm64-v8a"},
		{"arm64-v8a",   "arm64-v8a"},
		{"armeabi",     "armeabi-v7a"},
		{"armeabi-v7a", "armeabi-v7a"},
		{"x86",         "x86"},
		{"x86_64",      "x86_64"},
		{"x64",         "x86_64"}
	};

	sealed class ParsedOptions
	{
		public string?   AdbPath;
		public string?   PackageName;
		public string[]? SupportedABIs;
		public string?   TargetDevice;
		public bool      ShowHelp;
		public bool      Verbose = true; // TODO: remove the default once development is done
		public string?   AppNativeLibrariesDir;
		public string?   NdkDirPath;
		public string?   OutputDirPath;
		public string?   ConfigScriptName;
		public string?   LldbScriptName;
	}

	static int Main (string[] args)
	{
		bool haveOptionErrors = false;
		var parsedOptions = new ParsedOptions ();
		var log = new XamarinLoggingHelper {
			Verbose = parsedOptions.Verbose,
		};

		var opts = new OptionSet {
			"Usage: debug-session-prep [REQUIRED_OPTIONS] [OPTIONS]",
			"",
			"REQUIRED_OPTIONS are:",
			{ "p|package-name=", "name of the application package", v => parsedOptions.PackageName = EnsureNonEmptyString (log, "-p|--package-name", v, ref haveOptionErrors) },
			{ "s|supported-abis=", "comma-separated list of ABIs the application supports", v => parsedOptions.SupportedABIs = EnsureSupportedABIs (log, "-s|--supported-abis", v, ref haveOptionErrors) },
			{ "l|lib-dir=", "{PATH} to the directory where application native libraries were copied (relative to output directory, below)", v => parsedOptions.AppNativeLibrariesDir = v },
			{ "n|ndk-dir=", "{PATH} to to the Android NDK root directory", v => parsedOptions.NdkDirPath = v },
			{ "o|output-dir=", "{PATH} to directory which will contain various generated files (logs, scripts etc)", v => parsedOptions.OutputDirPath = v },
			{ "c|config-script=", "{NAME} of the launcher configuration script which will be created in the output directory", v => parsedOptions.ConfigScriptName = v },
			{ "g|lldb-script=", "{NAME} of the LLDB script which will be created in the output directory", v => parsedOptions.LldbScriptName = v },
			"",
			"OPTIONS are:",
			{ "a|adb=", "{PATH} to adb to use for this session", v => parsedOptions.AdbPath = EnsureNonEmptyString (log, "-a|--adb", v, ref haveOptionErrors) },
			{ "d|device=", "ID of {DEVICE} to target for this session", v => parsedOptions.TargetDevice = EnsureNonEmptyString (log, "-d|--device", v, ref haveOptionErrors) },
			"",
			{ "v|verbose", "Show debug messages", v => parsedOptions.Verbose = true },
			{ "h|help|?", "Show this help screen", v => parsedOptions.ShowHelp = true },
			"",
			$"Supported ABI names are: {GetSupportedAbiNames ()}",
			"",
		};

		List<string> rest = opts.Parse (args);
		log.Verbose = parsedOptions.Verbose;

		if (parsedOptions.ShowHelp) {
			opts.WriteOptionDescriptions (Console.Out);
			return 0;
		}

		if (haveOptionErrors) {
			return 1;
		}

		bool missingRequiredOptions = false;
		if (parsedOptions.SupportedABIs == null || parsedOptions.SupportedABIs.Length == 0) {
			log.ErrorLine ("The '-s|--supported-abis' option must be used to provide a non-empty list of Android ABIs supported by the application");
			missingRequiredOptions = true;
		}

		if (String.IsNullOrEmpty (parsedOptions.PackageName)) {
			log.ErrorLine ("The '-p|--package-name' option must be used to provide non-empty application package name");
			missingRequiredOptions = true;
		}

		if (String.IsNullOrEmpty (parsedOptions.NdkDirPath)) {
			log.ErrorLine ("The '-n|--ndk-dir' option must be used to specify the directory where Android NDK is installed");
			missingRequiredOptions = true;
		}

		if (String.IsNullOrEmpty (parsedOptions.OutputDirPath)) {
			log.ErrorLine ("The '-o|--output-dir' option must be used to specify the directory where generated files will be placed");
			missingRequiredOptions = true;
		}

		if (String.IsNullOrEmpty (parsedOptions.AppNativeLibrariesDir)) {
			log.ErrorLine ("The '-l|--lib-dir' option must be used to specify the directory where application shared libraries were copied");
			missingRequiredOptions = true;
		}

		if (String.IsNullOrEmpty (parsedOptions.ConfigScriptName)) {
			log.ErrorLine ("The '-c|--config-script' option must be used to specify name of the launcher configuration script");
			missingRequiredOptions = true;
		}

		if (String.IsNullOrEmpty (parsedOptions.LldbScriptName)) {
			log.ErrorLine ("The '-g|--lldb-script' option must be used to specify name of the LLDB script");
			missingRequiredOptions = true;
		}

		if (missingRequiredOptions) {
			return 1;
		}

		var ndk = new AndroidNdk (log, parsedOptions.NdkDirPath!, parsedOptions.SupportedABIs!);
		var device = new AndroidDevice (
			log,
			ndk,
			parsedOptions.OutputDirPath!,
			parsedOptions.AdbPath ?? DefaultAdbPath,
			parsedOptions.PackageName!,
			parsedOptions.SupportedABIs!,
			parsedOptions.TargetDevice
		);

		if (!device.GatherInfo ()) {
			return 1;
		}

		if (device.ApiLevel < 21) {
			log.ErrorLine ($"Only Android API level 21 and newer are supported");
			return 1;
		}

		if (!device.Prepare (out string? mainProcessPath) || String.IsNullOrEmpty (mainProcessPath)) {
			log.ErrorLine ("Failed to prepare for debugging session");
			return 1;
		}

		string socketScheme = "unix-abstract";
		string socketDir = $"/xa-{parsedOptions.PackageName}";

		var rnd = new Random ();
		string socketName = $"xa-platform-{rnd.NextInt64 ()}.sock";

		WriteConfigScript (parsedOptions, device, ndk, socketScheme, socketDir, socketName);
		WriteLldbScript (parsedOptions, device, socketScheme, socketDir, socketName, mainProcessPath);

		return 0;
	}

	static FileStream OpenScriptStream (string path)
	{
		return File.Open (path, FileMode.Create, FileAccess.Write, FileShare.Read);
	}

	static StreamWriter OpenScriptWriter (FileStream fs)
	{
		return new StreamWriter (fs, Utilities.UTF8NoBOM);
	}

	static void WriteLldbScript (ParsedOptions parsedOptions, AndroidDevice device, string socketScheme, string socketDir, string socketName, string mainProcessPath)
	{
		string outputFile = Path.Combine (parsedOptions.OutputDirPath!, parsedOptions.LldbScriptName!);
		string fullLibsDir = Path.GetFullPath (Path.Combine (parsedOptions.OutputDirPath!, parsedOptions.AppNativeLibrariesDir!));
		using FileStream fs = OpenScriptStream (outputFile);
		using StreamWriter sw = OpenScriptWriter (fs);

		// TODO: add support for appending user commands
		var searchPathsList = new List<string> {
			$"\"{Path.Combine (fullLibsDir, device.MainAbi)}\""
		};

		foreach (string abi in device.AvailableAbis) {
			if (String.Compare (abi, device.MainAbi, StringComparison.Ordinal) == 0) {
				continue;
			}

			searchPathsList.Add ($"\"{Path.Combine (fullLibsDir, abi)}\"");
		}

		string searchPaths = String.Join (" ", searchPathsList);
		sw.WriteLine ($"settings append target.exec-search-paths {searchPaths}");
		sw.WriteLine ("platform select remote-android");
		sw.WriteLine ($"platform connect {socketScheme}-connect://{socketDir}/{socketName}");
		sw.WriteLine ($"file \"{mainProcessPath}\"");

		sw.Flush ();
	}

	static void WriteConfigScript (ParsedOptions parsedOptions, AndroidDevice device, AndroidNdk ndk, string socketScheme, string socketDir, string socketName)
	{
		bool powershell = Utilities.IsWindows;
		string outputFile = Path.Combine (parsedOptions.OutputDirPath!, parsedOptions.ConfigScriptName!);
		using FileStream fs = OpenScriptStream (outputFile);
		using StreamWriter sw = OpenScriptWriter (fs);

		sw.WriteLine ($"DEVICE_API_LEVEL={device.ApiLevel}");
		sw.WriteLine ($"DEVICE_AVAILABLE_ABIS={FormatArray (device.AvailableAbis)}");
		sw.WriteLine ($"DEVICE_AVAILABLE_ARCHES={FormatArray (device.AvailableArches)}");
		sw.WriteLine ($"DEVICE_DEBUG_SERVER_LAUNCHER=\"{device.DebugServerLauncherScriptPath}\"");
		sw.WriteLine ($"DEVICE_LLDB_DIR=\"{device.LldbBaseDir}\"");
		sw.WriteLine ($"DEVICE_MAIN_ABI={device.MainAbi}");
		sw.WriteLine ($"DEVICE_MAIN_ARCH={device.MainArch}");
		sw.WriteLine ($"DEVICE_SERIAL=\"{device.SerialNumber}\"");
		sw.WriteLine ($"LLDB_PATH=\"{ndk.LldbPath}\"");
		sw.WriteLine ($"SOCKET_DIR={socketDir}");
		sw.WriteLine ($"SOCKET_NAME={socketName}");
		sw.WriteLine ($"SOCKET_SCHEME={socketScheme}");

		sw.Flush ();

		string FormatArray (string[] values)
		{
			var sb = new StringBuilder ();
			if (powershell) {
				sb.Append ('@');
			}
			sb.Append ('(');

			bool first = true;
			foreach (string v in values) {
				if (first) {
					first = false;
				} else {
					sb.Append (powershell ? ", " : " ");
				}
				sb.Append ('"');
				sb.Append (v);
				sb.Append ('"');
			}

			sb.Append (')');

			return sb.ToString ();
		}
	}

	static string[]? EnsureSupportedABIs (XamarinLoggingHelper log, string paramName, string? value, ref bool haveOptionErrors)
	{
		string? abis = EnsureNonEmptyString (log, paramName, value, ref haveOptionErrors);
		if (abis == null) {
			return null;
		}

		bool haveInvalidAbis = false;
		var list = new List<string> ();
		foreach (string s in abis.Split (',')) {
			string? abi = s?.Trim ();
			if (String.IsNullOrEmpty (abi)) {
				continue;
			}

			if (!SupportedAbiMap.TryGetValue (abi, out string? mappedAbi) || String.IsNullOrEmpty (mappedAbi)) {
				log.ErrorLine ($"Unsupported ABI: {abi}");
				haveInvalidAbis = true;
			}

			list.Add (mappedAbi!);
		}

		if (haveInvalidAbis) {
			return null;
		}

		return list.ToArray ();
	}

	static string? EnsureNonEmptyString (XamarinLoggingHelper log, string paramName, string? value, ref bool haveOptionErrors)
	{
		if (String.IsNullOrEmpty (value)) {
			haveOptionErrors = true;
			log.ErrorLine ($"Parameter '{paramName}' requires a non-empty string as its value");
			return null;
		}

		return value;
	}

	static string GetSupportedAbiNames () => String.Join (", ", SupportedAbiMap.Keys);
}
