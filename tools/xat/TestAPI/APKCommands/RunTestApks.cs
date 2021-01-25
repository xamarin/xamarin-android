using System;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.BuildTools.PrepTasks;
using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.APK
{
	/// <summary>
	///   Execute tests found in the APK/AAB package
	/// </summary>
	class RunTestApks : APKTestCommand
	{
		public RunTestApks ()
			: base ("RunTestApks", "Execute tests in a APK/AAB package")
		{}

		protected override async Task<bool> Run (TestAPK test)
		{
			var adb = new AdbRunner (Context, toolPath: Context.AdbPath) {
				AdbTarget = State!.AdbTarget,
			};

			if (test.AndroidPermissions.Count > 0) {
				foreach (string p in test.AndroidPermissions) {
					string permission = p.Trim ();
					if (permission.Length == 0) {
						continue;
					}

					if (!await adb.GrantPermission (test.AndroidPackageName, $"android.permission.{permission}")) {
						Log.WarningLine ($"Grant of permission '{permission}' to package '{test.AndroidPackageName}' failed");
					}
				}
			}

			bool ret = true;
			if (!await RunInstrumentations (test)) {
				Log.WarningLine ("Test '{test.Name}' instrumentation(s) failed");
				ret = false;
			}

			if (!await RunActivities (test)) {
				Log.WarningLine ("Test '{test.Name}' activity failed");
				ret = false;
			}

			if (!await ProcessTimings (test)) {
				Log.WarningLine ("Test '{test.Name}' timing processing failed");
				ret = false;
			}

			return ret;
		}

		async Task<bool> ProcessTimings (TestAPK test)
		{
			if (test.TimingResultsFilename.Length == 0) {
				Log.DebugLine ($"Timing processing info absent in test {test.Name}");
				return true;
			}

			if (test.TimingDefinitionsFilename.Length == 0) {
				throw new InvalidOperationException ($"Path to timing definitions file must be provided");
			}

			string timingResultsPath = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, test.TimingResultsFilename);
			string timingDefinitionsPath = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, test.TimingDefinitionsFilename);

			Log.InfoLine ("Processing timing results");
			Log.InfoLine ("   Results path: ", timingResultsPath);
			Log.InfoLine ("   Definitions path: ", timingDefinitionsPath);

			string testsFlavor = Context.Properties.GetRequiredValue (KnownProperties.TestsFlavor);
			var process = new ProcessLogcatTiming {
				Activity = test.Activity,
				AddResults = true,
				ApplicationPackageName = test.AndroidPackageName,
				DefinitionsFilename = timingDefinitionsPath,
				InputFilename = $"{Configurables.Paths.LogcatFileBase}-{test.AndroidPackageName}.txt",
				LabelSuffix = $"-{Context.Configuration}{testsFlavor}",
				ResultsFilename = timingResultsPath,
			};

			return await Task.Run<bool> (() => process.Run ());
		}

		async Task<bool> RunActivities (TestAPK test)
		{
			if (test.Activity.Length == 0) {
				return true;
			}

			Log.InfoLine ("Running UI tests");
			var activity = new RunUITests {
				Activity = test.Activity,
				AdbTarget = State!.AdbTarget,
				LogcatFilename = $"{Configurables.Paths.LogcatFileBase}-{test.AndroidPackageName}.txt",
				Timeout = TimeSpan.FromMilliseconds (300000),
			};

			return await activity.Run ();
		}

		async Task<bool> RunInstrumentations (TestAPK test)
		{
			if (test.Instrumentations.Count == 0) {
				Log.DebugLine ($"No instrumentations in test {test.Name}");
				return true;
			}

			bool ret = true;
			Log.InfoLine ("Running instrumentation tests");
			foreach (TestAndroidInstrumentation instrumentation in test.Instrumentations) {
				var runner = new RunInstrumentationTests {
					AdbTarget = State!.AdbTarget,
					Component = $"{test.AndroidPackageName}/{instrumentation.TypeName}",
					ExcludedCategories = test.ExcludeCategories,
					IncludedCategories = test.IncludeCategories,
					LogcatFilename = $"{Configurables.Paths.LogcatFileBase}-{test.AndroidPackageName}{instrumentation.LogcatFilenameDistincion}.txt",
					LogLevel = "Verbose",
					NUnit2TestResultsFile = instrumentation.ResultsPath,
					PackageName = test.AndroidPackageName,
					TestFixtures = test.TestNames,
				};

				if (instrumentation.TimeoutInMS > 0) {
					runner.Timeout = TimeSpan.FromMilliseconds (instrumentation.TimeoutInMS);
					Log.DebugLine ($"Execution timeout: {runner.Timeout}");
				}

				if (!await runner.Run ()) {
					Log.WarningLine ($"APK test '{test.Name}', instrumentation '{runner.Component}' failed");
					Log.DebugLine ($"---- Logcat output start: '{runner.Component}' ----");
					// TODO: dump logcat to the main log file
					Log.DebugLine ($"---- Logcat output end: '{runner.Component}' ----");
					ret = false;
				}
			}

			return ret;
		}
	}
}
