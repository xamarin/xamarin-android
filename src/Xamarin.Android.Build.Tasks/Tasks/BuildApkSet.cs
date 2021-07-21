using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.IO;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	/// <summary>
	/// Invokes `bundletool` to create an APK set (.apks file)
	///
	/// Usage: bundletool build-apks --bundle=foo.aab --output=foo.apks
	/// </summary>
	public class BuildApkSet : BundleToolAdbTask
	{
		public override string TaskPrefix => "BAS";

		[Required]
		public string AppBundle { get; set; }

		[Required]
		public string Output { get; set; }

		[Required]
		public string Aapt2ToolPath { get; set; }

		public string Aapt2ToolExe { get; set; }

		public string Aapt2ToolName => OS.IsWindows ? "aapt2.exe" : "aapt2";

		[Required]
		public string KeyStore { get; set; }

		[Required]
		public string KeyAlias { get; set; }

		[Required]
		public string KeyPass { get; set; }

		[Required]
		public string StorePass { get; set; }

		public string ExtraArgs { get; set; }

		public bool GenerateUniversalApkSet { get; set; } = false;

		public override bool RunTask ()
		{
			//NOTE: bundletool will not overwrite
			if (File.Exists (Output))
				File.Delete (Output);

			base.RunTask ();

			return !Log.HasLoggedErrors;
		}

		void AddStorePass (CommandLineBuilder cmd, string cmdLineSwitch, string value)
		{
			if (value.StartsWith ("env:", StringComparison.Ordinal)) {
				cmd.AppendSwitchIfNotNull ($"{cmdLineSwitch} ", value);
			} else if (value.StartsWith ("file:", StringComparison.Ordinal)) {
				cmd.AppendSwitchIfNotNull ($"{cmdLineSwitch} file:", value.Replace ("file:", string.Empty));
			} else {
				cmd.AppendSwitchIfNotNull ($"{cmdLineSwitch} pass:", value);
			}
		}

		internal override CommandLineBuilder GetCommandLineBuilder ()
		{
			var aapt2 = string.IsNullOrEmpty (Aapt2ToolExe) ? Aapt2ToolName : Aapt2ToolExe;
			var cmd   = base.GetCommandLineBuilder ();
			cmd.AppendSwitch ("build-apks");
			if (GenerateUniversalApkSet) {
				cmd.AppendSwitchIfNotNull ("--mode ", "universal");
			} else {
				cmd.AppendSwitch ("--connected-device");
				cmd.AppendSwitchIfNotNull ("--mode ", "default");
				AppendAdbOptions (cmd);
			}
			cmd.AppendSwitchIfNotNull ("--bundle ", AppBundle);
			cmd.AppendSwitchIfNotNull ("--output ", Output);
			cmd.AppendSwitchIfNotNull ("--aapt2 ", Path.Combine (Aapt2ToolPath, aapt2));
			cmd.AppendSwitchIfNotNull ("--ks ", KeyStore);
			cmd.AppendSwitchIfNotNull ("--ks-key-alias ", KeyAlias);
			AddStorePass (cmd, "--key-pass", KeyPass);
			AddStorePass (cmd, "--ks-pass", StorePass);
			if (!string.IsNullOrEmpty (ExtraArgs))
				cmd.AppendSwitch (ExtraArgs);
			return cmd;
		}
	}
}
