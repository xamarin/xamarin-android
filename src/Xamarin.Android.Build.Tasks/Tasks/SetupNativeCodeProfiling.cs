using System;
using System.IO;

using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Framework;

namespace Xamarin.Android.Tasks
{
	public class SetupNativeCodeProfiling : AndroidAsyncTask
	{
		public override string TaskPrefix => "SNCP";

		[Required]
		public string AdbPath { get; set; }

		[Required]
		public string AndroidNdkPath { get; set; }

		public string TargetDeviceName { get; set; }

		[Output]
		public string DeviceSdkVersion { get; set; }

		[Output]
		public bool DeviceIsEmulator { get; set; }

		[Output]
		public string[] DeviceSupportedAbis { get; set; }

		[Output]
		public string DevicePrimaryABI { get; set; }

		[Output]
		public string SimplePerfDirectory { get; set; }

		[Output]
		public string NdkPythonDirectory { get; set; }

		public async override System.Threading.Tasks.Task RunTaskAsync ()
		{
			var adi = new AndroidDeviceInfo (Log, AdbPath, TargetDeviceName);
			await adi.Detect ();

			DeviceSdkVersion = adi.DeviceSdkVersion ?? String.Empty;
			DeviceIsEmulator = adi.DeviceIsEmulator;
			DeviceSupportedAbis = adi.DeviceSupportedAbis ?? new string[] {};
			DevicePrimaryABI = adi.DevicePrimaryABI ?? String.Empty;

			string simplePerfPath = Path.Combine (AndroidNdkPath, "simpleperf");
			if (Directory.Exists (simplePerfPath)) {
				SimplePerfDirectory = simplePerfPath;
			} else {
				Log.LogError ($"Simpleperf directory '{simplePerfPath}' not found");
			}

			string ndkPythonPath = Path.Combine (AndroidNdkPath, "toolchains", "llvm", "prebuilt", NdkHelper.ToolchainHostName, "python3");
			if (Directory.Exists (ndkPythonPath)) {
				NdkPythonDirectory = ndkPythonPath;
			} else {
				Log.LogWarning ($"NDK Python 3 directory '{ndkPythonPath}' does not exist");
			}
		}
	}
}
