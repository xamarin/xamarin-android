using System.Threading.Tasks;

namespace Xamarin.Android.Tests.Host
{
	class ReleaseAndroidTarget : HostTestCommand
	{
		public ReleaseAndroidTarget ()
			: base ("ReleaseAndroidTarget", "Stops the emulator instance we started, if any")
		{}

		protected override async Task<bool> Run (TestHostUnit test)
		{
			var androidDevice = new AndroidDevice ();
			return await androidDevice.Stop (State!.EmulatorProcessId, State!.AdbTarget);
		}
	}
}
