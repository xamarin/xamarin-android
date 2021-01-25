using System.Threading.Tasks;

namespace Xamarin.Android.Tests.APK
{
	class ReleaseAndroidTarget : APKTestCommand
	{
		public ReleaseAndroidTarget ()
			: base ("ReleaseAndroidTarget", "Stops the emulator instance we started, if any")
		{}

		protected override async Task<bool> Run (TestAPK test)
		{
			var androidDevice = new AndroidDevice ();
			return await androidDevice.Stop (State!.EmulatorProcessId, State!.AdbTarget);
		}
	}
}
