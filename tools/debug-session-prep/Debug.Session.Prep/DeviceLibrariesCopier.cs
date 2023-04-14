using System;

using Xamarin.Android.Utilities;
using Xamarin.Android.Tasks;

namespace Xamarin.Debug.Session.Prep;

abstract class DeviceLibraryCopier
{
	protected XamarinLoggingHelper Log   { get; }
	protected bool AppIs64Bit            { get; }
	protected string LocalDestinationDir { get; }
	protected AdbRunner Adb              { get; }
	protected AndroidDevice Device       { get; }

	protected DeviceLibraryCopier (XamarinLoggingHelper log, AdbRunner adb, bool appIs64Bit, string localDestinationDir, AndroidDevice device)
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
				Log.ErrorLine ("Failed to copy 64-bit app_process64");
				return null;
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
                                Log.ErrorLine ("Failed to copy 32-bit app_process");
                                return null;
                        }

                        zygotePath = destination;
		}

		return zygotePath;
	}

	public abstract bool Copy (out string? zygotePath);
}
