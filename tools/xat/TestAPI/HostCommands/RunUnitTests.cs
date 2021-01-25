using System;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.Host
{
	class RunUnitTests : HostTestCommand
	{
		public RunUnitTests ()
			: base ("RunUnitTests", "Run NUnit tests")
		{}

		protected async override Task<bool> Run (TestHostUnit test)
		{
			var nunit = new NUnitRunner (Context, toolPath: Context.NUnitPath);
			if (test.EnvironmentVariables.Count > 0) {
				foreach (var kvp in test.EnvironmentVariables) {
					nunit.EnvironmentVariables [kvp.Key] = kvp.Value;
				}
			}

			var criteria = new NUnitRunner.TestCriteria {
				TestNames = test.TestNames,
				IncludeCategories = test.IncludeCategories,
				ExcludeCategories = test.ExcludeCategories,
				IncludeTests = test.IncludeTests,
				ExcludeTests = test.ExcludeTests,
			};
			return await nunit.Run (test.TestAssemblyPath, test.ResultPath, test.OutputFilePath, criteria, timeout: test.Timeout);
		}
	}
}
