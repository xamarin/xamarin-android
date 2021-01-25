using System;
using System.Threading.Tasks;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class MSBuildTimingCommandContainer : MSBuildTimingTestCommand
	{
		public override string Target => "UNUSED";
		public override string ID     => "UNUSED";

		public MSBuildTimingCommandContainer ()
			: base (nameof (MSBuildTimingCommandContainer), "Container for nested MSBuild timing test commands")
		{}

#pragma warning disable 1998
		protected override async Task<bool> Run (TestMSBuildTiming test)
		{
			throw new InvalidOperationException ("Should never be called, did you add commands to the Commands property?");
		}
#pragma warning restore 1998
	}
}
