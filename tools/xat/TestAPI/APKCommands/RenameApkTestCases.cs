using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.APK
{
	class RenameApkTestCases : APKTestCommand
	{
		public RenameApkTestCases ()
			: base ("RenameApkTestCases", "Rename APK test cases")
		{}

		protected override async Task<bool> Run (TestAPK test)
		{
			if (test.Instrumentations.Count == 0) {
				Log.DebugLine ($"Test '{test.Name}' does have any instrumentations");
				return true;
			}

			foreach (TestAndroidInstrumentation instrumentation in test.Instrumentations) {
				Log.StatusLine ($"Renaming test cases for '{test.Name}': ", $"instrumentation '{instrumentation.TypeName}'");
				if (instrumentation.ResultsPath.Length == 0) {
					Log.WarningLine ("  Result path not defined");
					continue;
				}

				var renamer = new TestCaseRenamer {
					DeleteSourceFiles = true,
					DestinationFolder = BuildPaths.XamarinAndroidSourceRoot,
					SourceFile = instrumentation.ResultsPath,
				};

				if (!await Task.Run<bool> (() => renamer.Run ())) {
					Log.WarningLine ("Failed to rename test cases for '{test.Name}', instrumentation '{instrumentation.TypeName}'");
				}
			}

			return true;
		}
	}
}
