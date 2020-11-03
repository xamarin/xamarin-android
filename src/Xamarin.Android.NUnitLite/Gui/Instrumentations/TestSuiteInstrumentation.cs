using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;

using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Util;

using NUnitLite.Runner;
using NUnit.Framework.Api;
using NUnit.Framework.Internal;

namespace Xamarin.Android.NUnitLite {

	public abstract class TestSuiteInstrumentation : Instrumentation {
		const string TAG = "NUnitLite";

		protected bool GCAfterEachFixture {
			get { return AndroidRunner.Runner.GCAfterEachFixture; }
			set { AndroidRunner.Runner.GCAfterEachFixture = value; }
		}

		protected ITestFilter Filter {
			get { return AndroidRunner.Runner.Filter; }
			set { AndroidRunner.Runner.Filter = value; }
		}

		protected TestSuiteInstrumentation (IntPtr handle, JniHandleOwnership transfer)
			: base (handle, transfer)
		{
		}

		Bundle arguments;

		public override void OnCreate (Bundle arguments)
		{
			base.OnCreate (arguments);
			this.arguments = arguments;
			Start ();
		}

		public override void OnStart ()
		{
			base.OnStart ();

			AddTests ();

			AndroidRunner.Runner.Options.LoadFromBundle (arguments);
			AndroidRunner.Runner.AddTestFilters (GetIncludedCategories (), GetExcludedCategories ());

			UpdateFilter ();

			AndroidRunner.Runner.Initialized = true;
			var results = new Bundle ();
			int failed = 0;
			try {
				Log.Info (TAG, "NUnit automated tests started");
				var testResult  = AndroidRunner.Runner.Run (AndroidRunner.GetSetupTestTarget (arguments), TargetContext);
				var resultsFile = GetResultsPath ();
				Log.Info (TAG, $"NUnit resultsFile {resultsFile}");
				if (resultsFile != null) {
					var startTime   = DateTime.Now;
					var resultsXml  = new NUnit2XmlOutputWriter (startTime);
					resultsXml.WriteResultFile (testResult, resultsFile);
					Log.Info (TAG, $"NUnit resultsFile {resultsFile} written");
					results.PutString ("nunit2-results-path", resultsFile);
					Log.Info (TAG, $"NUnit PutString {resultsFile} done.");
				}
				Log.Info (TAG, "NUnit automated tests completed");
				int run = 0, passed = 0, skipped = 0, inconclusive = 0;
				foreach (TestResult result in AndroidRunner.Results.Values) {
					if (result.HasChildren)
						continue;
					run           += 1;
					inconclusive  += result.InconclusiveCount;
					failed        += result.FailCount;
					passed        += result.PassCount;
					skipped       += result.SkipCount;
					if (result.FailCount > 0) {
						Log.Error (TAG, "Test '{0}' failed: {1}", result.FullName, result.Message);
						// Avoid Java.Lang.NullPointerException: println needs a message
						if (!String.IsNullOrEmpty (result.StackTrace))
							Log.Error (TAG, result.StackTrace);
						results.PutString ("failure: " + result.FullName,
								result.Message + "\n" + result.StackTrace);
						Log.Error (TAG, "  "); // makes it easier to read the failures in logcat output
					}
				}
				results.PutInt ("run", run);
				results.PutInt ("passed", passed);
				results.PutInt ("failed", failed);
				results.PutInt ("skipped", skipped);
				results.PutInt ("inconclusive", inconclusive);
				string message = string.Format ("Passed: {0}, Failed: {1}, Skipped: {2}, Inconclusive: {3}",
						passed, failed, skipped, inconclusive);
				Log.Info (TAG, message);
			} catch (Exception e) {
				Log.Error (TAG, "Error: {0}", e);
				results.PutString ("error", e.ToString ());
			}
			Finish (failed == 0 ? Result.Ok : Result.Canceled, results);
		}

		string GetResultsPath ()
		{
			string pid = Guid.NewGuid ().ToString ();
			string filesDir = Path.Combine (Context.FilesDir.AbsolutePath);
			if (!Directory.Exists (filesDir))
				Directory.CreateDirectory (filesDir);
			return Path.Combine (filesDir, $"TestResults_{(pid.Replace ("-", "_"))}.xml");
		}

		protected abstract void AddTests ();

		protected void AddTest (Assembly assembly)
		{
			AndroidRunner.Runner.AddTest (assembly);
		}

		protected virtual IEnumerable <string> GetIncludedCategories ()
		{
			string include = arguments?.GetString ("include");
			if (!string.IsNullOrEmpty (include)) {
				foreach (var category in include.Split (':')) {
					yield return category;
				}
			}
		}

		protected virtual IEnumerable <string> GetExcludedCategories ()
		{
			string exclude = arguments?.GetString ("exclude");
			if (!string.IsNullOrEmpty (exclude)) {
				foreach (var category in exclude.Split (':')) {
					yield return category;
				}
			}
		}

		protected virtual void UpdateFilter ()
		{
		}
	}
}
