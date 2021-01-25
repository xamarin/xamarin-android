using System;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class BundleToolRunner : ToolRunner
	{
		protected override string DefaultToolExecutableName => Path.GetFileName (Context.JavaPath);
		protected override string ToolName                  => "BundleTool";

		string FullAdbPath => Context.OS.Which (Context.AdbPath);

		public string AdbTarget { get; set; } = String.Empty;

		public BundleToolRunner (Context context, Log? log = null, string? toolPath = null)
			: base (context, log, toolPath)
		{}

		public async Task<bool> BuildApks (string bundlePath)
		{
			EnsureParameterValue (nameof (bundlePath), bundlePath);

			ProcessRunner runner = CreateProcessRunner ("build-apks");
			runner
				.AddArgument ("--connected-device")
				.AddArgument ("--overwrite")
				.AddArgument ("--mode")
				.AddArgument ("default")
				.AddArgument ("--bundle")
				.AddQuotedArgument (bundlePath)
				.AddArgument ("--output")
				.AddQuotedArgument ($"{bundlePath}.apks");

			if (!String.IsNullOrEmpty (Configurables.Paths.TestKeyStore)) {
				runner.AddArgument ($"--ks=\"{Configurables.Paths.TestKeyStore}\"");
			}

			if (!String.IsNullOrEmpty (Configurables.TestKeyStoreAlias)) {
				runner.AddArgument ($"--ks-key-alias={Configurables.TestKeyStoreAlias}");
			}

			if (!String.IsNullOrEmpty (Configurables.TestKeyStorePassword)) {
				runner.AddArgument ($"--key-pass=pass:{Configurables.TestKeyStorePassword}");
			}

			if (!String.IsNullOrEmpty (Configurables.TestStorePassword)) {
				runner.AddArgument ($"--ks-pass=pass:{Configurables.TestStorePassword}");
			}

			return await RunTool (() => runner.Run ());
		}

		public async Task<bool> InstallApks (string bundlePath)
		{
			EnsureParameterValue (nameof (bundlePath), bundlePath);

			ProcessRunner runner = CreateProcessRunner ("install-apks");
			runner
				.AddArgument ("--modules")
				.AddArgument ("_ALL_")
				.AddArgument ("--apks")
				.AddQuotedArgument ($"{bundlePath}.apks");

			return await RunTool (() => runner.Run ());
		}

		ProcessRunner CreateProcessRunner (string commandName)
		{
            ProcessRunner runner =  base.CreateProcessRunner ();
			runner
				.AddArgument ("-jar")
				.AddQuotedArgument (Context.BundleToolJarPath)
				.AddArgument (commandName);

			AddCommonArguments (runner);
			return runner;
		}

		void AddCommonArguments (ProcessRunner runner)
		{
			string adbPath = FullAdbPath;

			if (AdbTarget.Length > 0) {
				runner.AddArgument ($"--device-id={AdbTarget}");
			}

			if (!String.IsNullOrEmpty (adbPath)) {
				runner
					.AddArgument ("--adb")
					.AddQuotedArgument (FullAdbPath);
			}
		}
	}
}
