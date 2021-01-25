using System.Threading.Tasks;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class ReleaseAndroidTarget : MSBuildTimingTestCommand
	{
		public override string Target => "UNUSED";
		public override string ID => nameof (ReleaseAndroidTarget);

		public ReleaseAndroidTarget ()
			: base ("ReleaseAndroidTarget", "Stops the emulator instance we started, if any")
		{}

		protected override async Task<bool> Run (TestMSBuildTiming test)
		{
			var androidDevice = new AndroidDevice ();
			return await androidDevice.Stop (State!.EmulatorProcessId, State!.AdbTarget);
		}
	}
}
