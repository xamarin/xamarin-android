using System;
using System.Collections.Generic;
using System.Linq;

using Xamarin.Android.Utilities;
using Xamarin.Android.Tasks;

namespace Xamarin.Android.Debug;

class NoLddDeviceLibraryCopier : DeviceLibraryCopier
{
	const string LdConfigPath = "/system/etc/ld.config.txt";

	// To make things interesting, it turns out that API29 devices have **both** API 28 and 29 (they report 28) and for that reason they have TWO config files for ld...
	const string LdConfigPath28 = "/etc/ld.config.28.txt";
	const string LdConfigPath29 = "/etc/ld.config.29.txt";

	// TODO: We probably need a "provider" for the list of paths, since on ARM devices, /system/lib{64} directories contain x86/x64 binaries, and the ARM binaries are found in
	// /system/lib{64]/arm{64} (but not on all devices, of course... e.g. Pixel 6 Pro doesn't have these)
	//
	// List of directory paths to use when the device has neither ldd nor /system/etc/ld.config.txt
	static readonly string[] FallbackLibraryDirectories = {
		"/system/@LIB@",
		"/system/@LIB@/drm",
		"/system/@LIB@/egl",
		"/system/@LIB@/hw",
		"/system/@LIB@/soundfx",
		"/system/@LIB@/ssl",
		"/system/@LIB@/ssl/engines",

		// /system/vendor is a symlink to /vendor on some Android versions, we'll skip the latter then
		"/system/vendor/@LIB@",
		"/system/vendor/@LIB@/egl",
		"/system/vendor/@LIB@/mediadrm",
	};

	public NoLddDeviceLibraryCopier (XamarinLoggingHelper log, AdbRunner2 adb, bool appIs64Bit, string localDestinationDir, AndroidDevice device)
		: base (log, adb, appIs64Bit, localDestinationDir, device)
	{}

	public override bool Copy (out string? zygotePath)
	{
		zygotePath = FetchZygote ();
		if (String.IsNullOrEmpty (zygotePath)) {
			return false;
		}

		(List<string> searchPaths, HashSet<string> permittedPaths) = GetLibraryPaths ();

		// Collect file listings for all the search directories
		var sharedLibraries = new List<string> ();
		foreach (string path in searchPaths) {
			AddSharedLibraries (sharedLibraries, path, permittedPaths);
		}

		var moduleCache = new NoLddLldbModuleCache (Log, Device, sharedLibraries);
		moduleCache.Populate (zygotePath);

		return true;
	}

	void AddSharedLibraries (List<string> sharedLibraries, string deviceDirPath, HashSet<string> permittedPaths)
	{
		AdbRunner2.OutputLineFilter filterOutErrors = (bool isStdError, string line) => {
			if (!isStdError) {
				return false; // don't suppress any lines on stdout
			}

			// Ignore these, since we don't really care and there's no point in spamming the output with red
			return
				line.IndexOf ("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
				line.IndexOf ("No such file or directory", StringComparison.OrdinalIgnoreCase) >= 0;
		};

		(bool success, string output) = Adb.Shell (filterOutErrors, "ls", "-l", deviceDirPath).Result;
		if (!success) {
			// We can't rely on `success` because `ls -l` will return an error code if the directory exists but has any entries access to whose is not permitted
			if (output.IndexOf ("No such file or directory", StringComparison.OrdinalIgnoreCase) >= 0) {
				Log.DebugLine ($"Shared libraries directory {deviceDirPath} not found on device");
				return;
			}
		}

		Log.DebugLine ($"Adding shared libraries from {deviceDirPath}");
		foreach (string l in output.Split ('\n')) {
			string line = l.Trim ();
			if (line.Length == 0) {
				continue;
			}

			// `ls -l` output has 8 columns for filesystem entries
			string[] parts = line.Split (' ', 8, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != 8) {
				continue;
			}

			string permissions = parts[0].Trim ();
			string name = parts[7].Trim ();

			// First column, permissions: `drwxr-xr-x`, `-rw-r--r--` etc
			if (permissions[0] == 'd') {
				// Directory
				string nestedDirPath = $"{deviceDirPath}{name}/";
				if (permittedPaths.Count > 0 && !permittedPaths.Contains (nestedDirPath)) {
					Log.DebugLine ($"Directory '{nestedDirPath}' is not in the list of permitted directories, ignoring");
					continue;
				}

				AddSharedLibraries (sharedLibraries, nestedDirPath, permittedPaths);
				continue;
			}

			// Ignore entries that aren't regular .so files or symlinks
			if ((permissions[0] != '-' && permissions[0] != 'l') || !name.EndsWith (".so", StringComparison.Ordinal)) {
				continue;
			}

			string libPath;
			if (permissions[0] == 'l') {
				// Let's hope there are no libraries with -> in their name :P (if there are, we should use `readlink`)
				const string SymlinkArrow = "->";

				// Symlink, we'll add the target library instead
				int idx = name.IndexOf (SymlinkArrow, StringComparison.Ordinal);
				if (idx > 0) {
					libPath = name.Substring (idx + SymlinkArrow.Length).Trim ();
				} else {
					Log.WarningLine ($"'ls -l' output line contains a symbolic link, but I can't determine the target:");
					Log.WarningLine ($"  '{line}'");
					Log.WarningLine ("Ignoring this entry");
					continue;
				}
			} else {
				libPath = $"{deviceDirPath}{name}";
			}

			Log.DebugLine ($"  {libPath}");
			sharedLibraries.Add (libPath);
		}
	}

	(List<string> searchPaths, HashSet<string> permittedPaths) GetLibraryPaths ()
	{
		string lib = AppIs64Bit ? "lib64" : "lib";

		if (Device.ApiLevel == 21) {
			// API21 devices (at least emulators) don't return adb error codes, so to avoid awkward error message parsing, we're going to just skip detection since we
			// know what API21 has and doesn't have
			return (GetFallbackDirs (), new HashSet<string> ());
		}

		string localLdConfigPath = Utilities.MakeLocalPath (LocalDestinationDir, LdConfigPath);
		Utilities.MakeFileDirectory (localLdConfigPath);

		string deviceLdConfigPath;

		if (Device.ApiLevel == 28) {
			deviceLdConfigPath = LdConfigPath28;
		} else if (Device.ApiLevel == 29) {
			deviceLdConfigPath = LdConfigPath29;
		} else {
			deviceLdConfigPath = LdConfigPath;
		}

		if (!Adb.Pull (deviceLdConfigPath, localLdConfigPath).Result) {
			Log.DebugLine ($"Device doesn't have {LdConfigPath}");
			return (GetFallbackDirs (), new HashSet<string> ());
		} else {
			Log.DebugLine ($"Downloaded {deviceLdConfigPath} to {localLdConfigPath}");
		}

		var parser = new LdConfigParser (Log);

		// The app executables (app_process and app_process32) are both in /system/bin, so we can limit our
		// library search paths to this location.
		(List<string> searchPaths, HashSet<string> permittedPaths) = parser.Parse (localLdConfigPath, "/system/bin", lib);
		if (searchPaths.Count == 0) {
			searchPaths = GetFallbackDirs ();
		}

		return (searchPaths, permittedPaths);

		List<string> GetFallbackDirs ()
		{
			Log.DebugLine ("Using fallback library directories for this device");
			return FallbackLibraryDirectories.Select (l => l.Replace ("@LIB@", lib)).ToList ();
		}
	}
}
