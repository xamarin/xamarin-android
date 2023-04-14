using System;

using Xamarin.Android.Utilities;
using Xamarin.Android.Tasks;

namespace Xamarin.Android.Debug;

abstract class DeviceLibraryCopier
{
	protected XamarinLoggingHelper Log   { get; }
	protected bool AppIs64Bit            { get; }
	protected string LocalDestinationDir { get; }
	protected AdbRunner2 Adb             { get; }
	protected AndroidDevice Device       { get; }

	protected DeviceLibraryCopier (XamarinLoggingHelper log, AdbRunner2 adb, bool appIs64Bit, string localDestinationDir, AndroidDevice device)
	{
		Log = log;
		Adb = adb;
		AppIs64Bit = appIs64Bit;
		LocalDestinationDir = localDestinationDir;
		Device = device;
	}

	protected string? FetchZygote ()
	{
		string zygotePath;
		string destination;

		if (AppIs64Bit) {
			zygotePath = "/system/bin/app_process64";
			destination = Utilities.MakeLocalPath (LocalDestinationDir, zygotePath);

			Utilities.MakeFileDirectory (destination);
			if (!Adb.Pull (zygotePath, destination).Result) {
				return ReportFailureAndReturn ();
			}
		} else {
			// /system/bin/app_process is 32-bit on 32-bit devices, but a symlink to
			// app_process64 on 64-bit. If we need the 32-bit version, try to pull
			// app_process32, and if that fails, pull app_process.
			destination = Utilities.MakeLocalPath (LocalDestinationDir, "/system/bin/app_process");
			string? source = "/system/bin/app_process32";

			Utilities.MakeFileDirectory (destination);
			if (!Adb.Pull (source, destination).Result) {
				source = "/system/bin/app_process";
				if (!Adb.Pull (source, destination).Result) {
					source = null;
				}
			}

			if (String.IsNullOrEmpty (source)) {
				return ReportFailureAndReturn ();
			}

			zygotePath = destination;
		}

		Log.DebugLine ($"Zygote path: {zygotePath}");
		return zygotePath;

		string? ReportFailureAndReturn ()
		{
			const string appProcess32 = "app_process";
			const string appProcess64 = appProcess32 + "64";

			string bitness = AppIs64Bit ? "64" : "32";
			string process = AppIs64Bit ? appProcess64 : appProcess32;

			Log.ErrorLine ($"Failed to copy {bitness}-bit {process}");
			Log.ErrorLine ("Unable to determine path of the zygote process on device");

			return null;
		}
	}

	public abstract bool Copy (out string? zygotePath);
}
