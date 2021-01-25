//
// Code ported from build-tools/Xamarin.Android.Tools.BootstrapTasks/Xamarin.Android.Tools.BootstrapTasks/ApkDiffCheckRegression.cs
//
using System;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;
using Xamarin.Android.BuildTools.PrepTasks;

namespace Xamarin.Android.Tests.APK
{
	/// <summary>
	///   Check and record APK sizes
	/// </summary>
	class CheckAndRecordApkSizes : APKTestCommand
	{
		static readonly string apkSizesReferenceDirectory = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "tests", "apk-sizes-reference");
		static readonly string testResultDirectory = BuildPaths.XamarinAndroidSourceRoot;

		readonly string apkDescSuffix;
		readonly string testsFlavor;

		public CheckAndRecordApkSizes ()
			: base ("CheckAndRecordApkSizes", "Check and record APK sizes")
		{
			testsFlavor = Context.Properties.GetRequiredValue (KnownProperties.TestsFlavor);
			apkDescSuffix = $"{Context.Configuration}{testsFlavor}";
		}

		protected override async Task<bool> Run (TestAPK test)
		{
			string descriptionFileName = $"{Path.GetFileNameWithoutExtension (test.ApkPath)}-{Context.Configuration}{testsFlavor}{Path.GetExtension (test.ApkPath)}desc";
			string referenceDescription = Path.Combine (apkSizesReferenceDirectory, descriptionFileName);

			if (!Utilities.FileExists (referenceDescription)) {
				return true;
			}

			string apkdiffPath = GetFullApkdiffPath ();

			if (!OS.IsExecutable (apkdiffPath)) {
				Log.WarningLine ($"apkdiff not found in {apkdiffPath} or not executable");
				Log.WarningLine ("APK sizes check step skipped");
				return true;
			}

			var apkdiff = new ApkdiffRunner (Context, Log, apkdiffPath) {
				ApkDescDirectory = Configurables.Paths.ApkDescDirectory,
				ApkDescSuffix = $"{Context.Configuration}{testsFlavor}",
				ApkSizeThreshold = Configurables.ApkdiffApkSizeThreshold,
				AssemblySizeThreshold = Configurables.ApkdiffAssemblySizeThreshold,
			};

			(int exitCode, string output) = await apkdiff.Run (test.ApkPath, referenceDescription);
			if (exitCode == 0)
				return true;

			var errorMessage = $"apkdiff exited with error code: {exitCode}.";
			string testResultPath = Path.Combine (testResultDirectory, $"TestResult-apkdiff-{Path.GetFileNameWithoutExtension (referenceDescription)}.xml");
			ErrorResultsHelper.CreateErrorResultsFile (
				testResultPath,
				nameof (CheckAndRecordApkSizes),
				"check apk size regression",
				new Exception (errorMessage),
				$"apkdiff output:\n{output}");

			return false;
		}

		string GetFullApkdiffPath ()
		{
#if LINUX || MACOS
			return Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.Personal), ".dotnet", "tools", "apkdiff");
#elif WINDOWS
			throw new NotImplementedException ();
#endif
		}
	}
}
