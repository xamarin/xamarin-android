//
// Code ported from build-tools/Xamarin.Android.Tools.BootstrapTasks/Xamarin.Android.Tools.BootstrapTasks/RunInstrumentationTests.cs
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class RunInstrumentationTests : Adb
	{
		static readonly char[] NameValueSplit = new [] { '=' };

		internal const string AdbRestartText     = "daemon not running; starting now at tcp:";
		internal const string AdbCrashErrorText  = "The adb might have crashed and was restarted. ";
		const string TestResultsPathResult       = "INSTRUMENTATION_RESULT: nunit2-results-path";
		const string InstrumentationExitCodeName = "INSTRUMENTATION_CODE: ";

		public List<string>? TestFixtures            { get; set; }
		public string Component                      { get; set; } = String.Empty;
		public string NUnit2TestResultsFile          { get; set; } = String.Empty;
		public string LogcatFilename                 { get; set; } = String.Empty;
		public string PackageName                    { get; set; } = String.Empty;
		public string LogLevel                       { get; set; } = String.Empty;

		public List<string>? ExcludedCategories      { get; set; }
		public List<string>? IncludedCategories      { get; set; }

		int instrumentationExitCode = 99;
		string? targetTestResultsPath;
		bool adbRestarted;

		public override async Task<bool> Run ()
		{
			EnsurePropertyValue (nameof (Component), Component);
			EnsurePropertyValue (nameof (LogcatFilename), LogcatFilename);
			EnsurePropertyValue (nameof (PackageName), PackageName);

			Log.StatusLine ("Starting test run for:");
			Log.InfoLine ($"  Package: ", PackageName);
			Log.InfoLine ($"  Instrumentation: ", Component);

			if (String.IsNullOrEmpty (NUnit2TestResultsFile)) {
				var n = new StringBuilder ("TestResult-").Append (Component);
				foreach (var c in Path.GetInvalidFileNameChars ()) {
					n.Replace (c, '_');
				}
				n.Append (".xml");
				NUnit2TestResultsFile = n.ToString ();
			}

			AdbRunner adb = CreateAdbRunner ();

			bool instrumentSuccess = await adb.AmInstrument (Component, GetInstrumentationArguments (), lineCallback: (string line) => {
				if (line.Length == 0) {
					return;
				}

				int testResultIndex = line.IndexOf (TestResultsPathResult, StringComparison.OrdinalIgnoreCase);
				int exitCodeIndex = line.IndexOf (InstrumentationExitCodeName, StringComparison.OrdinalIgnoreCase);

				if (testResultIndex < 0 && exitCodeIndex < 0) {
					return;
				}

				if (testResultIndex >= 0) {
					int equalSignIndex = line.IndexOf ('=');
					if (equalSignIndex >= 0) {
						targetTestResultsPath = line.Substring (equalSignIndex + 1);
					} else {
						targetTestResultsPath = line.Substring (testResultIndex + TestResultsPathResult.Length).Trim ();
					}
				} else if (exitCodeIndex >= 0) {
					instrumentationExitCode = Int32.Parse (line.Substring (exitCodeIndex + InstrumentationExitCodeName.Length).Trim ());
				}
			});

			Log.StatusLine ("Logcat output path: ", LogcatFilename);
			bool success = await adb.LogcatDump (LogcatFilename, format: "threadtime", lineCallback: (string line) => {
				if (line.Length == 0) {
					return;
				}

				adbRestarted |= line.IndexOf (AdbRestartText, StringComparison.Ordinal) >= 0;
			});

			if (!success) {
				Log.WarningLine ("Failed to dump logcat buffer");
			}

			if (!instrumentSuccess) {
				return false;
			}

			// Original code cleared the logcat buffer here, but we do that now before running the test case so that the
			// output is as free from irrelevant content as possible (some Android devices/OS versions can be quite
			// chatty)

			if (String.IsNullOrEmpty (targetTestResultsPath)) {
				var adbText = adbRestarted ? AdbCrashErrorText : String.Empty;
				Log.ErrorLine (
					$"{adbText}Could not find NUnit2 results file after running component `{Component}`: " +
					"no `nunit2-results-path` bundle value found in command output!");
				return false;
			}

			Log.StatusLine ("Test results output path: ", NUnit2TestResultsFile);
			success = await adb.Pull (targetTestResultsPath!, NUnit2TestResultsFile);
			if (!success) {
				Log.WarningLine ($"Failed to pull test results file: {targetTestResultsPath}");
			}

			if (instrumentationExitCode != -1) {
				Log.ErrorLine (
					$"Instrumentation for component `{Component}` did not exit successfully. " +
					"Process crashed or test failures occurred!");
				return false;
			}

			return true;
		}

		List<string>? GetInstrumentationArguments ()
		{
			var ret = new List<string> ();

			AddExtraParam (ret, "include", IncludedCategories);
			AddExtraParam (ret, "exclude", ExcludedCategories);

			if (LogLevel.Length > 0) {
				ret.Add ("-e");
				ret.Add ("loglevel");
				ret.Add (LogLevel);
			}

			AddExtraParam (ret, "suite", TestFixtures);

			if (ret.Count == 0) {
				return null;
			}

			return ret;
		}

		void AddExtraParam (List<string> args, string keyword, List<string>? value)
		{
			if (value == null || value.Count == 0) {
				return;
			}

			args.Add ("-e");
			args.Add (keyword);
			args.Add (String.Join (",", value));
		}
	}
}
