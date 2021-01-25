using System.Threading.Tasks;

namespace Xamarin.Android.Tests.Host
{
	/// <summary>
	///   Makes sure that an Android device/emulator is running. This may involve creation of
	///   AVD (virtual Android device to be executed inside the emulator)
	/// </summary>
	class AcquireAndroidTarget : HostTestCommand
	{
		public AcquireAndroidTarget ()
			: base ("AcquireAndroidTarget", "Makes sure an Android device/emulator are running and available")
		{}

		protected override async Task<bool> Run (TestHostUnit test)
		{
			var androidDevice = new AndroidDevice ();
			(bool success, int emulatorProcessId, string adbTarget, string _) = await androidDevice.Start ();
			if (success && State != null) {
				State.EmulatorProcessId = emulatorProcessId;
				State.AdbTarget = adbTarget;
				return true;
			}
			return success;
		}
	}
}
