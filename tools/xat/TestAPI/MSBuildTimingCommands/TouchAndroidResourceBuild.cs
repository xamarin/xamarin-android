using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class TouchAndroidResourceBuild : MSBuildTimingTestCommand
	{
		public override string Target => "SignAndroidPackage";
		public override string ID     => nameof (TouchAndroidResourceBuild);

		public TouchAndroidResourceBuild ()
			: base (nameof (TouchAndroidResourceBuild), "A build after a fresh build that touches an Android resource XML file")
		{}

		protected async override Task<bool> Run (TestMSBuildTiming test)
		{
			Utilities.Touch (test.AndroidResourceFile);
			return await RunMSBuild (test);
		}
	}
}
