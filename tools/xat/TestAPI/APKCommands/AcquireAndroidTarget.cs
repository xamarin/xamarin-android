using System.Threading.Tasks;

namespace Xamarin.Android.Tests.APK
{
	/// <summary>
	///   Makes sure that an Android device/emulator is running. This may involve creation of
	///   AVD (virtual Android device to be executed inside the emulator)
	/// </summary>
	class AcquireAndroidTarget : APKTestCommand
	{
		public AcquireAndroidTarget ()
			: base ("AcquireAndroidTarget", "Makes sure an Android device/emulator are running and available")
		{}

		protected override async Task<bool> Run (TestAPK test)
		{
			var androidDevice = new AndroidDevice ();

			(bool success, int emulatorProcessId, string adbTarget, string sdkVersion) = await androidDevice.Start ();
			if (!success || State == null) {
				return false;
			}

			State.EmulatorProcessId = emulatorProcessId;
			State.AdbTarget = adbTarget;
			State.SdkVersion = sdkVersion;
			return true;
		}
	}
}
