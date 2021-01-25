using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
    class Run : XatCommand
	{
		List<XATest> failedTests;
		Dictionary<XATest, TimeSpan> testTimes;
		HashSet<string> globalInitCompleted;

		public List<string> GroupNames        { get; } = new List<string> ();
		public List<string> SuiteNames        { get; } = new List<string> ();
		public List<string> TestNames         { get; } = new List<string> ();
		public List<string> IncludeCategories { get; } = new List<string> ();
		public List<string> ExcludeCategories { get; } = new List<string> ();
		public List<string> IncludeTests      { get; } = new List<string> ();
		public List<string> ExcludeTests      { get; } = new List<string> ();

		public Run ()
		{
			failedTests = new List<XATest> ();
			testTimes = new Dictionary<XATest, TimeSpan> ();
			globalInitCompleted = new HashSet<string> (StringComparer.Ordinal);
		}

		void ReportFailedTests (bool needNewline)
		{
			if (failedTests.Count == 0) {
				if (SuiteNames.Count > 1) {
					Log.InfoLine ("All test suites succeeded");
				} else {
					Log.InfoLine ("Test suite succeeded");
				}
				return;
			}

			if (needNewline) {
				Log.StatusLine ();
			}

			string indent = "  ";
			Log.StatusLine ($"The following test suite{GetPluralSuffix(failedTests)} failed:", Log.ErrorColor);
			foreach (XATest suite in failedTests) {
				Log.Message ($"{indent}{Context.Characters.Bullet} ");
				Log.Message (suite.ID, ConsoleColor.Cyan);
				Log.Message ($" ({suite.KindName}", ConsoleColor.White);
				Log.MessageLine ($": '{suite.Name}')");

				ListFailedCommands ($"{indent}  ", suite);
				ReportExceptions ($"{indent}  ", suite.Exceptions);
			}
			Log.MessageLine ();

			void ListFailedCommands (string indent, XATest suite)
			{
				if (suite.FailedCommands.Count == 0) {
					return;
				}

				Log.MessageLine ($"{indent}Failed commands:");
				indent += "  ";
				foreach (XATest.FailedCommand fc in suite.FailedCommands) {
					string logFilePath;

					if (fc.Command.LogFilePath.Length > 0) {
						logFilePath = Utilities.GetPathRelativeToCWD (fc.Command.LogFilePath);
					} else {
						logFilePath = String.Empty;
					}

					Log.Message ($"{indent}{Context.Characters.Bullet} ");
					Log.Message (fc.Command.Name, ConsoleColor.Cyan);
					Log.Message ($" (phase: {fc.Phase}");
					if (logFilePath.Length > 0) {
						Log.Message ("; Log file: ");
						Log.Message (logFilePath, ConsoleColor.Cyan);
					}
					Log.MessageLine (")");
				}
			}
		}

		void ReportExceptions (string indent, List<Exception> exceptions)
		{
			if (exceptions.Count == 0) {
				return;
			}

			string waswere = exceptions.Count > 1 ? "were" : "was";
			Log.MessageLine ($"{indent}The following exception{GetPluralSuffix(exceptions)} {waswere} thrown:");
			indent = $"{indent}  ";

			foreach (Exception ex in exceptions) {
				Log.Message ($"{indent}{Context.Characters.Bullet} ");
				Log.Message (ex.GetType ().ToString (), ConsoleColor.DarkMagenta);
				Log.Message (": ");
				Log.MessageLine (ex.Message, ConsoleColor.White);
				Log.DebugLine (ex.ToString ());
				Log.DebugLine ();
			}
			Log.MessageLine ();
		}

		string GetPluralSuffix<T> (ICollection<T> coll)
		{
			if (coll.Count > 1) {
				return "s";
			}

			return String.Empty;
		}

		bool ReportTestTimes ()
		{
			if (testTimes.Count == 0) {
				return false;
			}

			Log.InfoLine ();
			Log.InfoLine ("Suite execution times:");
			foreach (var kvp in testTimes) {
				XATest suite = kvp.Key;
				TimeSpan time = kvp.Value;

				Log.Message ($"  {Context.Characters.Bullet} ");
				Log.Message (suite.ID, ConsoleColor.White);
				Log.Message ($" ({suite.KindName}): ");
				Log.MessageLine (time.ToString (), ConsoleColor.Cyan);
			}

			return true;
		}

		public override async Task<bool> Invoke ()
		{
			Log.StatusLine ("Main log file: ", Context.MainLogFilePath, tailColor: ConsoleColor.Cyan);
			Log.InfoLine ("Tool paths:");
			PrintToolPath ("ADB", Context.AdbPath);
			PrintToolPath ("AVD Manager", Context.AvdManagerPath);
			PrintToolPath ("BundleTool", Context.BundleToolJarPath);
			PrintToolPath ("Emulator", Context.EmulatorPath);
			PrintToolPath ("Java", Context.JavaPath);
			PrintToolPath ("NUnit", Context.NUnitPath);
			Log.MessageLine ();

			try {
				return await DoInvoke ();
			} finally {
				bool reportedSome = ReportTestTimes ();
				ReportFailedTests (needNewline: reportedSome);
				Log.StatusLine ("Main log file: ", Context.MainLogFilePath);
			}
		}

		void PrintToolPath (string name, string path)
		{
			Log.StatusLine ($"  {Context.Characters.Bullet} {name}: ", path, tailColor: ConsoleColor.DarkCyan);
		}

		// TODO: implement running some tests in parallel
		async Task<bool> DoInvoke ()
		{
			globalInitCompleted.Clear ();

			XATest? suite;
			TestCollection tc = Context.Tests;
			bool ret = true;
			var lastSuites = new Dictionary<string, XATest> (StringComparer.Ordinal);

			if (SuiteNames.Count > 0) {
				foreach (string suiteName in SuiteNames) {
					suite = FindSuite (suiteName, tc);
					if (suite == null) {
						Log.ErrorLine ($"Test suite '{suiteName}' is unknown");
						return false;
					}

					ret &= await RunTest (suite);
					lastSuites[MakeSuiteInitKey (suite)] = suite;
				}
			}

			if (GroupNames.Count > 0) {
				TestGroup? group;

				foreach (string groupName in GroupNames) {
					group = FindGroup (groupName, tc);
					if (group == null) {
						Log.ErrorLine ($"Test group '{groupName}' is unknown");
						return false;
					}

					foreach (string suiteID in group.SuitesByID.Keys) {
						suite = FindSuite (suiteID, tc);
						if (suite == null) {
							Log.ErrorLine ($"Test suite with ID '{suiteID}' in group '{groupName}' is unknown");
							return false;
						}

						ret &= await RunTest (suite, group);
						lastSuites[MakeSuiteInitKey (suite)] = suite;
					}
				}
			}

			if (SuiteNames.Count == 0 && GroupNames.Count == 0) {
				foreach (XATest s in tc.AllSuitesByID.Values) {
					ret &= await RunTest (s);
					lastSuites[MakeSuiteInitKey (s)] = s;
				}
			}

			if (lastSuites.Count > 0) {
				foreach (var kvp in lastSuites) {
					string kindName = kvp.Key;
					XATest lastSuite = kvp.Value;

					Log.StatusLine ($"Running global shutdown steps for suite kind '{lastSuite.KindName}, family '{lastSuite.TestFamilyName}'");
					if (!await lastSuite.RunGlobalShutdownCommands ()) {
						Log.WarningLine ($"Failed to run global shutdown steps for suite kind '{lastSuite.KindName}', family '{lastSuite.TestFamilyName}'");
					}
				}
			}

			return ret;
		}

		string MakeSuiteInitKey (XATest suite)
		{
			return $"{suite.KindName}:{suite.TestFamilyName}";
		}

		XATest? FindSuite (string suiteName, TestCollection tc)
		{
			if (tc.AllSuitesByID.TryGetValue (suiteName, out XATest suite)) {
				return suite;
			}

			if (tc.AllSuitesByName.TryGetValue (suiteName, out suite)) {
				return suite;
			}

			return null;
		}

		TestGroup? FindGroup (string groupName, TestCollection tc)
		{
			if (tc.GroupsByID.TryGetValue (groupName, out TestGroup group)) {
				return group;
			}

			if (tc.GroupsByName.TryGetValue (groupName, out group)) {
				return group;
			}

			return null;
		}

		async Task<bool> RunTest (XATest suite, TestGroup? group = null)
		{
			Log.InfoLine ($"Running suite: {suite.Name}");

			string? savedResultPath = null;
			if (group == null) {
				ClearAndAddRange (IncludeCategories, suite.IncludeCategories);
				ClearAndAddRange (ExcludeCategories, suite.ExcludeCategories);
				ClearAndAddRange (TestNames, suite.TestNames);
				ClearAndAddRange (IncludeTests, suite.IncludeTests);
				ClearAndAddRange (ExcludeTests, suite.ExcludeTests);
			} else {
				Log.InfoLine ($"Running as part of group '{group.Name}'");

				ClearAndAddRange (group.IncludeCategories, suite.IncludeCategories);
				ClearAndAddRange (group.ExcludeCategories, suite.ExcludeCategories);
				ClearAndAddRange (group.Tests, suite.TestNames);
				ClearAndAddRange (group.IncludeTests, suite.IncludeTests);
				ClearAndAddRange (group.ExcludeTests, suite.ExcludeTests);

				string? overrideResultPath = group.SuitesByID [suite.ID];
				if (!String.IsNullOrEmpty (overrideResultPath) && suite is TestHostUnit hostUnit) {
					savedResultPath = hostUnit.ResultPath;
					hostUnit.ResultPath = overrideResultPath!;
				}
			}

			bool success = true;
			var timer = new Stopwatch ();
			try {
				success = await DoRunTest (suite, timer);
			} catch (AggregateException aex) {
				foreach (Exception ex in aex.InnerExceptions) {
					suite.Exceptions.Add (ex);
				}
				success = false;
			} catch (Exception ex) {
				suite.Exceptions.Add (ex);
				success = false;
			} finally {
				testTimes[suite] = timer.Elapsed;
				if (savedResultPath != null && suite is TestHostUnit hostUnit) {
					hostUnit.ResultPath = savedResultPath;
				}
			}

			if (!success) {
				failedTests.Add (suite);
			}

			return success;

			void ClearAndAddRange (List<string> src, List<string> dest)
			{
				dest.Clear ();
				if (src.Count == 0) {
					return;
				}

				dest.AddRange (src);
			}
		}

		async Task<bool> DoRunTest (XATest suite, Stopwatch timer)
		{
			string suiteInitKey = MakeSuiteInitKey (suite);
			if (!globalInitCompleted.Contains (suiteInitKey)) {
				Log.StatusLine ($"Running global initialization steps for suite kind '{suite.KindName}', family '{suite.TestFamilyName}'");
				if (!await suite.RunGlobalInitCommands ()) {
					// This is fatal, we must not allow any further steps
					throw new InvalidOperationException ($"Failed to run global initialization steps in suite '{suite.Name}', family '{suite.TestFamilyName}'");
				}
				globalInitCompleted.Add (suiteInitKey);
			}

			if (!await suite.Build ()) {
				return false;
			}

			bool ret;
			try {
				timer.Start ();
				ret = await suite.Run ();
			} finally {
				timer.Stop ();
				if (!await suite.Cleanup ()) {
					Log.WarningLine ($"Suite '{suite.Name}' cleanup failed");
				}
			}

			if (!ret) {
				Context.FailedTests.Add ($"{suite.Name} ({suite.ID})");
			}

			return ret;
		}
	}
}
