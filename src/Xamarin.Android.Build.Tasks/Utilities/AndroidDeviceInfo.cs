using System;
using System.Threading.Tasks;

using Microsoft.Build.Utilities;

using TPLTask = System.Threading.Tasks.Task;

namespace Xamarin.Android.Tasks
{
	class AndroidDeviceInfo
	{
		string? targetDevice;
		TaskLoggingHelper log;
		string adbPath;

		public string? DeviceSdkVersion      { get; private set; }
		public bool DeviceIsEmulator         { get; private set; }
		public string[]? DeviceSupportedAbis { get; private set; }
		public string? DevicePrimaryABI      { get; private set; }

		public AndroidDeviceInfo (TaskLoggingHelper logger, string adbPath, string? targetDevice = null)
		{
			this.targetDevice = targetDevice;
			this.log = logger;
			this.adbPath = adbPath;
		}

		public async TPLTask Detect ()
		{
			var adb = new AdbRunner (log, adbPath, targetDevice);

			DeviceSdkVersion = await GetProperty (adb, "ro.build.version.sdk", "Android SDK version");
			DevicePrimaryABI = await GetProperty (adb, "ro.product.cpu.abi", "primary ABI");

			string abis = await GetProperty (adb, "ro.product.cpu.abilist", "ABI list");
			DeviceSupportedAbis = abis?.Split (',');

			string? fingerprint = await GetProperty (adb, "ro.build.fingerprint", "fingerprint");
			if (CheckProperty (fingerprint, (string v) => v.StartsWith ("generic", StringComparison.Ordinal))) {
				DeviceIsEmulator = true;
				return;
			}

			string? model = await GetProperty (adb, "ro.product.model", "product model");
			if (!String.IsNullOrEmpty (model)) {
				if (Contains (model, "google_sdk") ||
				    Contains (model, "droid4x", StringComparison.OrdinalIgnoreCase) ||
				    Contains (model, "Emulator") ||
				    Contains (model, "Android SDK built for x86", StringComparison.OrdinalIgnoreCase)
				) {
					DeviceIsEmulator = true;
					return;
				}
			}

			string? manufacturer = await GetProperty (adb, "ro.product.manufacturer", "product manufacturer");
			if (CheckProperty (manufacturer, (string v) => Contains (v, "Genymotion", StringComparison.OrdinalIgnoreCase))) {
				DeviceIsEmulator = true;
				return;
			}

			string? hardware = await GetProperty (adb, "ro.hardware", "hardware model");
			if (!String.IsNullOrEmpty (hardware)) {
				if (Contains (hardware, "goldfish") ||
				    Contains (hardware, "ranchu") ||
				    Contains (hardware, "vbox86")
				) {
					DeviceIsEmulator = true;
					return;
				}
			}

			string? product = await GetProperty (adb, "ro.product.name", "product name");
			if (!String.IsNullOrEmpty (product)) {
				if (Contains (product, "sdk_google") ||
				    Contains (product, "google_sdk") ||
				    Contains (product, "sdk") ||
				    Contains (product, "sdk_x86") ||
				    Contains (product, "sdk_gphone64_arm64") ||
				    Contains (product, "vbox86p") ||
				    Contains (product, "emulator") ||
				    Contains (product, "simulator")
				) {
					DeviceIsEmulator = true;
					return;
				}
			}

			bool Contains (string s, string sub, StringComparison comparison = StringComparison.Ordinal)
			{
#if NETCOREAPP
				return s.Contains (sub, comparison);
#else
				return s.IndexOf (sub, comparison) >= 0;
#endif
			}

			bool CheckProperty (string? value, Func<string, bool> checker)
			{
				if (String.IsNullOrEmpty (value)) {
					return false;
				}

				return checker (value);
			}
		}

		async Task<string?> GetProperty (AdbRunner adb, string propertyName, string errorWhat)
		{
			(bool success, string propertyValue) = await adb.GetPropertyValue (propertyName);
			if (!success) {
				log.LogWarning ($"Failed to get {errorWhat} from device");
				return default;
			}

			return propertyValue;
		}

		bool IsEmulator (string? model)
		{
			return false;
		}
	}
}
