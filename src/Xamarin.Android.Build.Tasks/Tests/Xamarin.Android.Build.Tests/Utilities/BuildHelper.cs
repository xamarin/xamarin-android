using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Xamarin.ProjectTools;
using NUnit.Framework;

namespace Xamarin.Android.Build.Tests
{
	public static class BuildHelper
	{
		// In general here I don't make use of IDisposable pattern for ProjectBuilder because
		// in case it failed I rather want to preserve the files to investigate.

		public static ProjectBuilder CreateApkBuilder (string directory, bool cleanupAfterSuccessfulBuild = false, bool cleanupOnDispose = false)
		{
			var ret = CreateDllBuilder (directory, cleanupAfterSuccessfulBuild, cleanupOnDispose);
			//NOTE: since $(BuildingInsideVisualStudio) is set, Build will not happen by default
			ret.Target = "Build,SignAndroidPackage";
			return ret;
		}

		public static ProjectBuilder CreateDllBuilder (string directory, bool cleanupAfterSuccessfulBuild = false, bool cleanupOnDispose = false)
		{
			return new ProjectBuilder (directory) {
				CleanupAfterSuccessfulBuild = cleanupAfterSuccessfulBuild,
				CleanupOnDispose = cleanupOnDispose,
				Root = XABuildPaths.TestOutputDirectory,
			};
		}
	}

	public static class StringAssertEx {
		[DebuggerHidden]
		public static void DoesNotContain (string text, IEnumerable<string> collection, string message = null)
		{
			foreach (var line in collection) {
				if (line.Contains (text)) {
					Assert.Fail (message);
					return;
				}
			}
		}

		[DebuggerHidden]
		public static void Contains (string text, IEnumerable<string> collection, string message = null)
		{
			foreach (var line in collection) {
				if (line.Contains (text))
				{
					return;
				}
			}
			Assert.Fail (message ?? $"String did not contain '{text}'!");
		}

		public static bool ContainsRegex (string pattern, IEnumerable<string> collection, RegexOptions additionalOptions = 0)
		{
			var regex = new Regex (pattern, RegexOptions.Multiline | additionalOptions);

			return regex.Match (string.Join ("\n", collection)).Success;
		}

		[DebuggerHidden]
		public static void ContainsRegex (string pattern, IEnumerable<string> collection, string message = null, RegexOptions additionalOptions = 0)
		{
			Assert.IsTrue (ContainsRegex (pattern, collection, additionalOptions), message);
		}

		[DebuggerHidden]
		public static void DoesNotContainRegex (string pattern, IEnumerable<string> collection, string message = null, RegexOptions additionalOptions = 0)
		{
			Assert.IsFalse (ContainsRegex (pattern, collection, additionalOptions), message);
		}

		public static bool ContainsText (this IEnumerable<string> collection, string expected) {
			foreach (var line in collection) {
				if (line.Contains (expected)) {
					return true;
				}
			}
			return false;
		}

		public static bool ContainsOccurances (this IEnumerable<string> collection, string expected, int count)
		{
			int found = 0;
			foreach (var line in collection) {
				if (line.Contains (expected)) {
					found++;
				}
			}
			return found == count;
		}

		// Checks if two string are equal after normalizing string line endings
		public static void AreMultiLineEqual (string expected, string actual)
		{
			expected = expected.ReplaceLineEndings ();
			actual = actual.ReplaceLineEndings ();

			Assert.AreEqual (expected, actual);
		}

		// Checks if actual contains expected after normalizing string line endings and removing whitespace
		public static void AreMultiLineContains (string expected, string actual)
		{
			expected = expected.ReplaceLineEndings ().Replace (" ", "").Replace ("\t", "");
			actual = actual.ReplaceLineEndings ().Replace (" ", "").Replace ("\t", "");

			Assert.IsTrue (actual.Contains (expected));
		}
	}
}

