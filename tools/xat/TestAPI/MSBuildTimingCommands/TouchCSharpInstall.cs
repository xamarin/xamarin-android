using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class TouchCSharpInstall : MSBuildTimingTestCommand
	{
		public override string Target => "Install";
		public override string ID     => nameof (TouchCSharpInstall);

		public TouchCSharpInstall ()
			: base (nameof (TouchCSharpInstall), "An install after touching a C# file")
		{}

		protected async override Task<bool> Run (TestMSBuildTiming test)
		{
			Utilities.Touch (test.CSharpFile);
			return await RunMSBuild (test);
		}
	}
}
