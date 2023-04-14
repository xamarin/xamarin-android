using System;
using System.Collections.Generic;

using Xamarin.Android.Utilities;

namespace Xamarin.Android.Debug;

class LddDeviceLibraryCopier : DeviceLibraryCopier
{
	public LddDeviceLibraryCopier (XamarinLoggingHelper log, AdbRunner2 adb, bool appIs64Bit, string localDestinationDir, AndroidDevice device)
		: base (log, adb, appIs64Bit, localDestinationDir, device)
	{}

	public override bool Copy (out string? zygotePath)
	{
		string lddPath = Device.DeviceLddPath ?? throw new InvalidOperationException ("On-device `ldd` binary is required");

		zygotePath = FetchZygote ();
		if (String.IsNullOrEmpty (zygotePath)) {
			return false;
		}

		(bool success, string zygoteLibs) = Adb.Shell (lddPath, zygotePath).Result;
		if (!success) {
			Log.ErrorLine ($"On-device ldd ({lddPath}) failed to return list of dependencies for Android application process {zygotePath}");
			return false;
		}

		Log.DebugLine ("Zygote libs:");
		Log.DebugLine (zygoteLibs);

		(List<string> libraryPaths, List<string> libraryNames) = LddOutputToLibraryList (zygoteLibs);
		if (libraryPaths.Count == 0 || libraryNames.Count == 0) {
			Log.WarningLine ($"ldd didn't report any shared libraries on-device application process '{zygotePath}' depends on");
			return true; // Not an error, per se
		}

		var moduleCache = new LddLldbModuleCache (Log, Device, libraryPaths, libraryNames);
		moduleCache.Populate (zygotePath);

		return true;
	}

	(List<string> libraryPaths, List<string> libraryNames) LddOutputToLibraryList (string output)
	{
		var libraryPaths = new List<string> ();
		var libraryNames = new List<string> ();
		if (String.IsNullOrEmpty (output)) {
			return (libraryPaths, libraryNames);
		}

		// Overall line format is: LIBRARY_NAME => LIBRARY_PATH (HEX_ADDRESS)
		// Lines are split on space, in assumption (and hope) that Android will not use filenames with spaces in them. This way we don't have to worry about the `=>`
		// separator (which can, in theory, be changed to something else on some version of Android)
		foreach (string l in output.Split ('\n')) {
			string line = l.Trim ();
			if (line.Length == 0) {
				continue;
			}

			string[] parts = line.Split (' ');
			if (parts.Length != 4) {
				Log.WarningLine ($"ldd line has unsupported format, ignoring: '{line}'");
				continue;
			}

			string path = parts[2];
			if (String.Compare (path, "[vdso]", StringComparison.OrdinalIgnoreCase) == 0) {
				// virtual library, doesn't exist on disk
				continue;
			}

			libraryPaths.Add (path);
			libraryNames.Add (parts[0]);
		}

		return (libraryPaths, libraryNames);
	}
}
