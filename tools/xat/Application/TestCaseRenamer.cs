//
// Code ported from build-tools/Xamarin.Android.Tools.BootstrapTasks/Xamarin.Android.Tools.BootstrapTasks/RenameTestCases.cs
//
using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

using Xamarin.Android.BuildTools.PrepTasks;
using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class TestCaseRenamer : AppObject
	{
		public bool DeleteSourceFiles   { get; set; }
		public string SourceFile        { get; set; } = String.Empty;
		public string DestinationFolder { get; set; } = String.Empty;

		public bool Run ()
		{
			EnsurePropertyValue (nameof (DestinationFolder), DestinationFolder);
			EnsurePropertyValue (nameof (SourceFile), SourceFile);

			string testsFlavor = Context.Properties.GetValue (KnownProperties.TestsFlavor) ?? String.Empty;
			string testNameSuffix  = String.IsNullOrWhiteSpace (Context.Configuration)
				? String.Empty
				: $" / {Context.Configuration}";
			string dest = GetFixedUpPath (SourceFile, testNameSuffix, testsFlavor);
			bool fixedUp = false;

			try {
				FixupTestResultFile (SourceFile, dest, testNameSuffix);
				fixedUp = true;
			} catch (Exception e) {
				Log.WarningLine ($"Unable to process `{SourceFile}`.  Is it empty?  (Did a unit test runner SIGSEGV?)");
				Log.WarningLine ("Exception thrown:");
				Log.WarningLine (e.ToString ());
				CreateErrorResultsFile (SourceFile, dest, Context.Configuration, testsFlavor, e, m => {
					Log.DebugLine (m);
				});
			}

			if (DeleteSourceFiles && Path.GetFullPath (SourceFile) != Path.GetFullPath (dest)) {
				Utilities.DeleteFileSilent (SourceFile);
			}

			if (fixedUp) {
				Log.StatusLine ($"File fixed up: ", dest);
			} else {
				Log.WarningLine ($"File not fixed up: {SourceFile}");
			}

			return true;
		}

		string GetFixedUpPath (string source, string testNameSuffix, string testsFlavor)
		{
			string destFilename = Path.GetFileNameWithoutExtension (source) +
				(String.IsNullOrWhiteSpace (Context.Configuration) ? String.Empty : "-" + Context.Configuration) +
				(String.IsNullOrWhiteSpace (testsFlavor) ? String.Empty : testsFlavor) +
				Path.GetExtension (source);
			return Path.Combine (DestinationFolder, destFilename);
		}

		void FixupTestResultFile (string source, string dest, string testNameSuffix)
		{
			var doc = XDocument.Load (source);
			switch (doc.Root.Name.LocalName) {
				case "test-results":
					FixupNUnit2Results (doc, testNameSuffix);
					break;
			}

			doc.Save (dest);
		}

		void FixupNUnit2Results (XDocument doc, string testNameSuffix)
		{
			foreach (var e in doc.Descendants ("test-case")) {
				var name = (string) e.Attribute ("name");
				if (name.EndsWith (testNameSuffix, StringComparison.OrdinalIgnoreCase))
					continue;
				name += testNameSuffix;
				e.SetAttributeValue ("name", name);
			}
		}

		static void CreateErrorResultsFile (string sourceFile, string destFile, string config, string flavor, Exception e, Action<string> logDebugMessage)
		{
			GetTestCaseInfo (sourceFile, Path.GetDirectoryName (destFile), config, flavor, logDebugMessage, out string testSuiteName, out string testCaseName, out string? logcatPath);
			var contents = new StringBuilder ();
			if (Utilities.FileExists (sourceFile)) {
				contents.Append (File.ReadAllText (sourceFile));
			}

			bool adbCrashed = false;
			if (logcatPath != null) {
				if (contents.Length > 0) {
					contents.AppendLine ();
					contents.AppendLine ();
				}
				contents.AppendLine ("---`adb logcat`---");
				var logcat      = File.ReadAllText (logcatPath);
				var inBinRun    = false;
				foreach (char c in logcat) {
					if (!Char.IsControl (c) || Char.IsWhiteSpace (c)) {
						if (inBinRun) {
							contents.Append ("]");
						}
						inBinRun = false;
						contents.Append (c);
						continue;
					}
					if (!inBinRun) {
						inBinRun = true;
						contents.Append ("[!binary-block");
					}
					// Invalid XML char such as 0x0f; escape it
					contents.AppendFormat (" {0:x2}", (int) c);
				}
				if (inBinRun) {
					contents.Append ("]");
				}

				adbCrashed |= logcat.IndexOf (RunInstrumentationTests.AdbRestartText, StringComparison.Ordinal) >= 0;
			}

			string adbText = adbCrashed ? RunInstrumentationTests.AdbCrashErrorText : String.Empty;
			string message = $"{adbText}Error processing `{sourceFile}`.  " +
				$"Check the build log for execution errors.{Environment.NewLine}" +
				$"File contents:{Environment.NewLine}";

			ErrorResultsHelper.CreateErrorResultsFile (destFile, testSuiteName, testCaseName, e, message, contents.ToString ());
		}

		// Example `SourceFile`:
		//   /Users/builder/jenkins/workspace/xamarin-android-pr-builder-release/xamarin-android/bin/TestRelease/TestResult-Mono.Android_Tests.xml
		// Example `DestinationFolder`:
		//   /Users/builder/jenkins/workspace/xamarin-android-pr-builder-release/xamarin-android/
		// Example `adb logcat`:
		//   /Users/builder/jenkins/workspace/xamarin-android-pr-builder-release/xamarin-android/bin/TestRelease/logcat-Release-Mono.Android_Tests.txt
		//
		// We need to extract the "base" test name from `SourceFile`, and use that to construct `logcatPath`
		static void GetTestCaseInfo (string sourceFile, string destinationFolder, string config, string flavor, Action<string> logDebugMessage, out string testSuiteName, out string testCaseName, out string? logcatPath)
		{
			const string TestResult = "TestResult-";

			string name = Path.GetFileNameWithoutExtension (sourceFile);
			if (name.StartsWith (TestResult, StringComparison.OrdinalIgnoreCase)) {
				name = name.Substring (TestResult.Length);
			}
			testSuiteName   = name;
			testCaseName    = $"Possible Crash / {config}";
			logcatPath      = Path.Combine (destinationFolder, "bin", $"Test{config}", $"logcat-{config}{flavor}-{name}.txt");
			logDebugMessage ($"Looking for `adb logcat` output in the file: {logcatPath}");
			if (!File.Exists (logcatPath)) {
				logDebugMessage ($"Could not find file `{logcatPath}`.  Will not be including `adb logcat` output.");
				logcatPath = null;
			}
		}
	}
}
