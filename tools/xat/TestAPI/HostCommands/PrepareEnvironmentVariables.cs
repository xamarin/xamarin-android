using System;
using System.Threading.Tasks;

namespace Xamarin.Android.Tests.Host
{
	class PrepareEnvironmentVariables : HostTestCommand
	{
		Action<TestHostUnit> creator;

		public PrepareEnvironmentVariables (Action<TestHostUnit> envCreator)
			: base (nameof (PrepareEnvironmentVariables), "Prepare environment variables for the tests running on the console")
		{
			this.creator = envCreator;
		}

#pragma warning disable 1998
		protected override async Task<bool> Run (TestHostUnit test)
		{
			creator (test);
			return true;
		}
#pragma warning restore 1998
	}
}
