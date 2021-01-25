using System;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class WaitForDeviceBoot : AppObject
	{
		public string AdbTarget         { get; set; } = String.Empty;

		public async Task<bool> Run ()
		{
			Log.DebugLine ($"Task {nameof (WaitForDeviceBoot)}");
			Log.DebugLine ($"  {nameof (AdbTarget)}: {AdbTarget}");

			var adb = new AdbRunner (Context, toolPath: Context.AdbPath) {
				AdbTarget = AdbTarget,
			};
			if (!await adb.WaitForDevice (traceAdb: true)) {
				Log.WarningLine ($"ADB timed out waiting for device");
			}

			string command = "'counter=0; while [ $counter -lt 60 ] && [ \"`getprop sys.boot_completed`\" != \"1\" ]; do echo Waiting for device to fully boot; sleep 1; let \"counter++\"; done'";
			(bool success, string _) = await adb.Shell (command, timeout: TimeSpan.FromSeconds (120), traceAdb: true);
			if (!success) {
				Log.ErrorLine ($"ADB `shell` command failed");
				return false;
			}

			return true;
		}
	}
}
