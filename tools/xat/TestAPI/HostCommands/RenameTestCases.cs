using System;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.Host
{
	class RenameTestCases : HostTestCommand
	{
		public RenameTestCases ()
			: base ("RenameTestCases", "Rename test cases")
		{}

		protected override async Task<bool> Run (TestHostUnit test)
		{
			bool deleteSourceFiles = true;
			if (Environment.GetEnvironmentVariable ("KEEP_TEST_SOURCES") != null) {
				deleteSourceFiles = false;
			}

			string deleteSourcesProp = Context.Properties.GetValue (KnownProperties.DeleteTestCaseSourceFiles) ?? String.Empty;
			if (Boolean.TryParse (deleteSourcesProp.Trim (), out bool v)) {
				deleteSourceFiles = v;
			}

			var renamer = new TestCaseRenamer {
				DeleteSourceFiles = deleteSourceFiles,
				DestinationFolder = BuildPaths.XamarinAndroidSourceRoot,
				SourceFile = test.ResultPath,
			};

			if (!await Task.Run<bool> (() => renamer.Run ())) {
				Log.WarningLine ("Failed to rename test cases for '{test.Name}', instrumentation '{instrumentation.TypeName}'");
			}

			return true;
		}
	}
}
