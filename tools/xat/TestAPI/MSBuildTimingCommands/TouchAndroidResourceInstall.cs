using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class TouchAndroidResourceInstall : MSBuildTimingTestCommand
	{
		public override string Target => "Install";
		public override string ID     => nameof (TouchAndroidResourceInstall);

		public TouchAndroidResourceInstall ()
			: base (nameof (TouchAndroidResourceInstall), "An install after touching an Android resource XML file")
		{}

		protected async override Task<bool> Run (TestMSBuildTiming test)
		{
			Utilities.Touch (test.AndroidResourceFile);
			return await RunMSBuild (test);
		}
	}
}
