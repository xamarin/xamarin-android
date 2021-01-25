using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class TouchCSharpBuild : MSBuildTimingTestCommand
	{
		public override string Target => "SignAndroidPackage";
		public override string ID     => nameof (TouchCSharpBuild);

		public TouchCSharpBuild ()
			: base (nameof (TouchCSharpBuild), "A build after a fresh build that touches a C# file")
		{}

		protected async override Task<bool> Run (TestMSBuildTiming test)
		{
			Utilities.Touch (test.CSharpFile);
			return await RunMSBuild (test);
		}
	}
}
