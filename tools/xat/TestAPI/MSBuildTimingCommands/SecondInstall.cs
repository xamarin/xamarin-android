using System.Threading.Tasks;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class SecondInstall : MSBuildTimingTestCommand
	{
		public override string Target => "Install";
		public override string ID     => nameof (SecondInstall);

		public SecondInstall ()
			: base (nameof (SecondInstall), "An install after a second build")
		{}

		protected async override Task<bool> Run (TestMSBuildTiming test)
		{
			return await RunMSBuild (test);
		}
	}
}
