using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class AndroidDevice : AppObject
	{
		public async Task<bool> Stop (int emuPID, string adbTarget)
		{
			if (emuPID <= 1) {
				// We haven't started the emulator, or we're running on device - don't do anything
				Log.DebugLine ($"Emulator not started by us or using a real device, not attempting to kill the emulator.");
				return true;
			}

			var adb = new AdbRunner (Context, toolPath: Context.AdbPath) {
				AdbTarget = adbTarget,
			};

			if (adbTarget.Length > 0) {
				if (!await adb.EmuKill (timeout: TimeSpan.FromSeconds (120))) {
					Log.WarningLine ("ADB 'emu kill' command failed");
				}
			}

			Log.DebugLine ($"Attempting to kill process {emuPID}");
			using (var p = Process.GetProcessById (emuPID)) {
				p.Kill ();
			}

			if (!await adb.KillServer (timeout: TimeSpan.FromSeconds (60))) {
				Log.WarningLine ("Failed to kill ADB server");
			}

			if (!Utilities.ProcessHUP (emuPID)) {
				WarnSignalFailed ("SIGHUP", emuPID);
			}

			Thread.Sleep (5000);

			if (!Utilities.ProcessKILL (emuPID)) {
				WarnSignalFailed ("SIGKILL", emuPID);
			}

			return true;

			void WarnSignalFailed (string signalName, int emuPID)
			{
				Log.WarningLine ($"Failed to send {signalName} to process {emuPID}");
			}
		}

		public async Task<(bool success, int emulatorProcessId, string adbTarget, string sdkVersion)> Start ()
		{
			string sdkVersion = String.Empty;
			string androidAdbTarget = String.Empty;
			int emulatorProcessId = -1;

			bool isValidTarget = false;
			Properties properties = Context.Properties;
			string adbTarget = Context.AdbTarget;

			if (!Context.RequireNewEmulator) {
				var check = new CheckAdbTarget {
					AdbTarget  = adbTarget,
					SdkVersion = Configurables.Defaults.AndroidSdkVersion,
					Timeout    = TimeSpan.FromSeconds (6),
				};
				if (!await check.Run ()) {
					return (false, -1, String.Empty, String.Empty);
				}
				isValidTarget = check.IsValidTarget;
				string not = isValidTarget ? String.Empty : "not ";
				Log.DebugLine ($"ADB target is {not}valid");
				sdkVersion = check.SdkVersion;

				if (adbTarget.Length == 0) {
					adbTarget = check.AdbTarget;

					if (adbTarget.Length > 0) {
						Log.InfoLine ("Detected ADB target: ", adbTarget);
					}
				}
			}

			if (!isValidTarget) {
				string androidSdkHome = properties.GetValue (KnownProperties.AndroidSdkDirectory) ?? String.Empty;
				string avdName = Configurables.AVDName;

				var create = new CreateAndroidEmulator {
					AndroidAbi = "x86_64",
					AndroidSdkHome = androidSdkHome,
					DataPartitionSizeMB = "4096",
					ImageName = avdName,
					JavaSdkHome = properties.GetValue (KnownProperties.JavaSdkDirectory) ?? String.Empty,
					RamSizeMB = "3072",
					SdkVersion = "29",
				};

				if (!await create.Run ()) {
					Log.ErrorLine ($"Failed to create Android virtual device '{avdName}'");
					return (false, -1, String.Empty, String.Empty);
				}

				var startEmulator = new StartAndroidEmulator {
					AndroidSdkHome = androidSdkHome,
					ImageName = avdName,
					Port = Configurables.Defaults.AdbEmulatorPort,
				};

				if (!await startEmulator.Run ()) {
					Log.ErrorLine ($"Failed to start Android virtual device '{avdName}'");
					return (false, -1, String.Empty, String.Empty);
				}

				adbTarget = startEmulator.AdbTarget;
				emulatorProcessId = startEmulator.EmulatorProcessId;

				var waitForBoot = new WaitForDeviceBoot {
					AdbTarget = adbTarget,
				};

				if (!await waitForBoot.Run ()) {
					Log.ErrorLine ($"Android virtual device '{avdName}' boot timed out");
					return (false, -1, String.Empty, String.Empty);
				}

				Log.InfoLine ($"Launched Android emulator; `adb` target: {adbTarget}");
			}

			androidAdbTarget = adbTarget;

			var adb = new AdbRunner (Context, toolPath: Context.AdbPath) {
				AdbTarget = adbTarget,
			};

			if (!await adb.SetProperty ("debug.mono.log", "timing", timeout: TimeSpan.FromSeconds (6))) {
				Log.WarningLine ("Failed to set the `debug.mono.log` device property");
			}

			if (!await adb.LogcatSetBufferSize ("4M", timeout: TimeSpan.FromSeconds (6))) {
				Log.WarningLine ("Failed to set logcat buffer size");
			}

			if (!await adb.LogcatClear ()) {
				Log.WarningLine ("Failed to clear logcat buffer");
			}

			return (true, emulatorProcessId, androidAdbTarget, sdkVersion);
		}
	}
}
