using System.Threading.Tasks;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class SecondBuild : MSBuildTimingTestCommand
	{
		public override string Target => "SignAndroidPackage";
		public override string ID     => nameof (SecondBuild);

		public SecondBuild ()
			: base (nameof (SecondBuild), "A second build after a fresh build")
		{}

		protected async override Task<bool> Run (TestMSBuildTiming test)
		{
			return await RunMSBuild (test);
		}
	}
}
