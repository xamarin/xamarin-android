using System;
using System.Collections.Generic;
using System.IO;

using Xamarin.Android.Utilities;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Debug;

class DebugSession
{
	static readonly Dictionary<string, string> KnownAbiDirs = new Dictionary<string, string> (StringComparer.Ordinal) {
		{ "lib/arm64-v8a", "arm64-v8a" },
		{ "lib/armeabi-v7a", "armeabi-v7a" },
		{ "lib/x86_64", "x86_64" },
		{ "lib/x86", "x86" },
	};

	readonly string apkPath;
	readonly ParsedOptions parsedOptions;
	readonly XamarinLoggingHelper log;
	readonly ZipArchive apk;
	readonly string workDirectory;
	readonly ApplicationInfo appInfo;

	const string socketScheme = "unix-abstract";
	string? socketDir = null;
	string? socketName = null;
	string? lldbScriptPath = null;
	AndroidDevice? device = null;
	AndroidNdk? ndk = null;

	public DebugSession (XamarinLoggingHelper logger, ApplicationInfo appInfo, string apkPath, ZipArchive apk, ParsedOptions parsedOptions)
	{
		log = logger;
		this.apkPath = apkPath;
		this.parsedOptions = parsedOptions;
		this.apk = apk;
		this.appInfo = appInfo;
		workDirectory = Path.Combine (parsedOptions.WorkDirectory, Utilities.StringHash (apkPath));
	}

	public bool Prepare ()
	{
		List<string> supportedAbis = DetectSupportedAbis ();
		if (supportedAbis.Count == 0) {
			log.ErrorLine ("Unable to detect ABIs supported by the application");
			return false;
		}

		ndk = new AndroidNdk (log, parsedOptions.NdkDirPath!, supportedAbis);
		device = new AndroidDevice (
			log, // general logger
			log, // process output logger
			ndk,
			workDirectory,
			parsedOptions.AdbPath,
			appInfo.PackageName,
			supportedAbis,
			parsedOptions.TargetDevice
		);

		// Install first, since there might already be a version of this application on device that is not debuggable
		if (!device.AdbRunner.Install (apkPath, apkIsDebuggable: true).Result) {
			log.ErrorLine ($"Failed to install package '{apkPath}' to device");
			return false;
		}

		if (!device.GatherInfo ()) {
			return false;
		}

		string? mainProcessPath;
		if (!device.Prepare (out mainProcessPath) || String.IsNullOrEmpty (mainProcessPath)) {
			return false;
		}

		string appLibsDirectory = Path.Combine (workDirectory, "lib", device.MainAbi);
		CopyAppLibs (device, appLibsDirectory);

		log.MessageLine ();

		if (supportedAbis.Count > 0) {
			log.StatusLine ("All supported ABIs", String.Join (", ", supportedAbis));
		}

		socketDir = $"/xa-{appInfo.PackageName}";

		var rnd = new Random ();
		socketName = $"xa-platform-{rnd.NextInt64 ()}.sock";
		lldbScriptPath = WriteLldbScript (appLibsDirectory, device, socketScheme, socketDir, socketName, mainProcessPath);

		LogABIs ("Application", supportedAbis);
		LogABIs ("     Device", device.DeviceAbis);
		log.StatusLine ("    Selected ABI", $"{device.MainAbi} (architecture: {device.MainArch})");
		log.MessageLine ();
		log.StatusLine ("Application data directory on device", device.AppDataDir);
		log.StatusLine ("Device serial number", device.SerialNumber);
		log.StatusLine ("Debug server path on device", device.DebugServerPath);
		log.StatusLine ("Debug server launcher script path on device", device.DebugServerLauncherScriptPath);
		log.MessageLine ();

		return true;

		void LogABIs (string which, IEnumerable<string> abis)
		{
			log.StatusLine ($"{which} ABIs", String.Join (", ", abis));
		}
	}

	public bool Run ()
	{
		if (!EnsureValidState (nameof (socketDir), socketDir) ||
		    !EnsureValidState (nameof (socketName), socketName) ||
		    !EnsureValidState (nameof (lldbScriptPath), socketName) ||
		    !EnsureValidState (nameof (device), device) ||
		    !EnsureValidState (nameof (ndk), ndk)
		) {
			return false;
		}

		log.InfoLine ("Starting lldb server on device");

		// TODO: either start the server in the background of the remote shell or keep this `RunAs` in the background
		(bool success, string output) = device!.AdbRunner.RunAs (
			    appInfo.PackageName,
			    device.DebugServerLauncherScriptPath,
			    device.LldbBaseDir,
			    socketScheme,
			    socketDir!,
			    socketName!,
			    "\\\"lldb process:gdb-remote packets\\\"").Result;

		if (!success) {
			return false;
		}

		// TODO: start the app
		// TODO: start the app so that it waits for the debugger (until monodroid_gdb_wait is set)

		return true;

		bool EnsureValidState<T> (string name, T? variable)
		{
			bool valid;

			if (typeof(T) == typeof(string)) {
				valid = !String.IsNullOrEmpty ((string?)(object?)variable);
			} else {
				valid = variable != null;
			}

			if (valid) {
				return true;
			}

			log.ErrorLine ($"Debug session hasn't been initialized properly. Required variable '{name}' not set");
			return false;
		}
	}

	string WriteLldbScript (string appLibsDir, AndroidDevice device, string socketScheme, string socketDir, string socketName, string mainProcessPath)
        {
                string outputFile = Path.Combine (workDirectory, "lldb.x");
                string fullLibsDir = Path.GetFullPath (appLibsDir);
                using FileStream fs = File.Open (outputFile, FileMode.Create, FileAccess.Write, FileShare.Read);
                using StreamWriter sw = new StreamWriter (fs, Utilities.UTF8NoBOM);

                // TODO: add support for appending user commands
                var searchPathsList = new List<string> {
                        $"\"{fullLibsDir}\""
                };

                string searchPaths = String.Join (" ", searchPathsList);
                sw.WriteLine ($"settings append target.exec-search-paths {searchPaths}");
                sw.WriteLine ("platform select remote-android");
                sw.WriteLine ($"platform connect {socketScheme}-connect://{socketDir}/{socketName}");
                sw.WriteLine ($"file \"{mainProcessPath}\"");

                log.DebugLine ($"Writing LLDB startup script: {outputFile}");
                sw.Flush ();

                return outputFile;
        }

	void CopyAppLibs (AndroidDevice device, string libDir)
	{
		log.DebugLine ($"Copying application shared libraries to '{libDir}'");
		Directory.CreateDirectory (libDir);

		string entryDir = $"lib/{device.MainAbi}/";
		log.DebugLine ($"Looking for shared libraries inside APK, stored in the {entryDir} directory");

		foreach (ZipEntry entry in apk) {
			if (entry.IsDirectory) {
				continue;
			}

			string dirName = Utilities.GetZipEntryDirName (entry.FullName);
			if (dirName.Length == 0 || String.Compare (entryDir, dirName, StringComparison.Ordinal) != 0) {
				continue;
			}

			string destPath = Path.Combine (libDir, Utilities.GetZipEntryFileName (entry.FullName));
			log.DebugLine ($"Copying app library '{entry.FullName}' to '{destPath}'");

			using var libraryData = File.Open (destPath, FileMode.Create);
			entry.Extract (libraryData);
			libraryData.Seek (0, SeekOrigin.Begin);
			libraryData.Flush ();
			libraryData.Close ();

			// TODO: fetch symbols for libs which don't start with `libaot*` and aren't known Xamarin.Android libraries
		}
	}

	List<string> DetectSupportedAbis ()
	{
		var ret = new List<string> ();

		log.DebugLine ($"Detecting ABIs supported by '{apkPath}'");
		HashSet<string> seenAbis = new HashSet<string> (StringComparer.Ordinal);
		foreach (ZipEntry entry in apk) {
			if (seenAbis.Count == KnownAbiDirs.Count) {
				break;
			}

			// There might not be separate entries for lib/{ARCH} directories, so we look for the first file
			// inside one of them to determine if an ABI is supported
			string entryDir = Path.GetDirectoryName (entry.FullName) ?? String.Empty;
			if (!KnownAbiDirs.TryGetValue (entryDir, out string? abi) || seenAbis.Contains (abi)) {
				continue;
			}

			seenAbis.Add (abi);
			ret.Add (abi);
		}

		return ret;
	}
}
