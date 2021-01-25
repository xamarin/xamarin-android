//
// Code ported from build-tools/Xamarin.Android.Tools.BootstrapTasks/Xamarin.Android.Tools.BootstrapTasks/ApkDiffCheckRegression.cs
//
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class ApkdiffRunner : ToolRunner
	{
		protected override string DefaultToolExecutableName => "apkdiff";
		protected override string ToolName                  => "APKdiff";

		public string ApkDescDirectory   { get; set; } = String.Empty;
		public string ApkDescSuffix      { get; set; } = String.Empty;
		public int ApkSizeThreshold      { get; set; } = -1;
		public int AssemblySizeThreshold { get; set; } = -1;

		public ApkdiffRunner (Context context, Log? log = null, string? toolPath = null)
			: base (context, log, toolPath)
		{
			EchoStandardOutput = true;
			EchoStandardError = true;
		}

		public async Task<(int exitCode, string output)> Run (string apkPath, string referenceDescPath)
		{
			EnsureParameterValue (nameof (apkPath), apkPath);
			EnsureParameterValue (nameof (referenceDescPath), referenceDescPath);

			EnsurePropertyValue (nameof (ApkDescDirectory), ApkDescDirectory);
			EnsurePropertyValue (nameof (ApkDescSuffix), ApkDescSuffix);
			EnsurePositivePropertyValue (nameof (ApkSizeThreshold), ApkSizeThreshold);
			EnsurePositivePropertyValue (nameof (AssemblySizeThreshold), AssemblySizeThreshold);

			string apkDescFile = Path.Combine (ApkDescDirectory, $"{Path.GetFileNameWithoutExtension (apkPath)}{ApkDescSuffix}{Path.GetExtension (apkPath)}desc");
			var runner = CreateProcessRunner ();
			runner
				.AddArgument ("-s")
				.AddArgument ($"--save-description-2=\"{apkDescFile}\"")
				.AddArgument ($"--test-apk-size-regression={ApkSizeThreshold}")
				.AddArgument ($"--test-assembly-size-regression={AssemblySizeThreshold}")
				.AddArgument (referenceDescPath)
				.AddArgument (apkPath);

			string output = await Task.Run<string>(() => Utilities.GetStringFromStdout (runner));

			return (runner.ExitCode, output);
		}
	}
}
