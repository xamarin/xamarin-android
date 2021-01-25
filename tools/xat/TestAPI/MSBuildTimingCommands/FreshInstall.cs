using System.Threading.Tasks;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class FreshInstall : MSBuildTimingTestCommand
	{
		public override string Target => "Install";
		public override string ID     => nameof (FreshInstall);

		public FreshInstall ()
			: base (nameof (FreshInstall), "An install after a fresh build")
		{}

		protected async override Task<bool> Run (TestMSBuildTiming test)
		{
			return await RunMSBuild (test);
		}
	}
}
