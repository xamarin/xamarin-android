using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	public class AndroidApkSigner : JavaToolTask
	{
		public override string TaskPrefix => "AAS";

		[Required]
		public string ApkToSign { get; set; }

		[Required]
		public ITaskItem ManifestFile { get; set; }

		[Required]
		public string KeyStore { get; set; }

		[Required]
		public string KeyAlias { get; set; }

		/// <summary>
		/// The Password for the Key.
		/// You can use the raw password here, however if you want to hide your password in logs
		/// you can use a preview of env: or file: to point it to an Environment variable or 
		/// a file.
		///
		///   env:<PasswordEnvironentVariable>
		///   file:<PasswordFile> 
		/// </summary>
		[Required]
		public string KeyPass { get; set; }

		/// <summary>
		/// The Password for the Keystore.
		/// You can use the raw password here, however if you want to hide your password in logs
		/// you can use a preview of env: or file: to point it to an Environment variable or 
		/// a file.
		///
		///   env:<PasswordEnvironentVariable>
		///   file:<PasswordFile> 
		/// </summary>
		[Required]
		public string StorePass { get; set; }

		public string AdditionalArguments { get; set; }

		protected override string MainClass => "com.android.apksigner.ApkSignerTool";

		public override bool RunTask ()
		{
			if (!File.Exists (GenerateFullPathToTool ())) {
				Log.LogError ($"'{GenerateFullPathToTool ()}' does not exist. You need to install android-sdk build-tools 26.0.1 or above.");
				return false;
			}

			return base.RunTask ();
		}

		void AddStorePass (CommandLineBuilder cmd, string cmdLineSwitch, string value)
		{
			if (value.StartsWith ("env:", StringComparison.Ordinal)) {
				cmd.AppendSwitchIfNotNull ($"{cmdLineSwitch} ", value);
			}
			else if (value.StartsWith ("file:", StringComparison.Ordinal)) {
				cmd.AppendSwitchIfNotNull ($"{cmdLineSwitch} file:", value.Replace ("file:", string.Empty));
			} else {
				cmd.AppendSwitchIfNotNull ($"{cmdLineSwitch} pass:", value);
			}
		}

		protected override CommandLineBuilder GetCommandLineBuilder ()
		{
			var cmd = base.GetCommandLineBuilder ();

			var manifest = AndroidAppManifest.Load (ManifestFile.ItemSpec, MonoAndroidHelper.SupportedVersions);
			int minSdk = MonoAndroidHelper.SupportedVersions.MinStableVersion.ApiLevel;
			int maxSdk = MonoAndroidHelper.SupportedVersions.MaxStableVersion.ApiLevel;
			if (manifest.MinSdkVersion.HasValue)
				minSdk = manifest.MinSdkVersion.Value;

			if (manifest.TargetSdkVersion.HasValue)
				maxSdk = manifest.TargetSdkVersion.Value;

			minSdk = Math.Min (minSdk, maxSdk);

			cmd.AppendSwitch ("sign");
			cmd.AppendSwitchIfNotNull ("--ks ", KeyStore);
			AddStorePass (cmd, "--ks-pass", StorePass);
			cmd.AppendSwitchIfNotNull ("--ks-key-alias ", KeyAlias);
			AddStorePass (cmd, "--key-pass", KeyPass);
			cmd.AppendSwitchIfNotNull ("--min-sdk-version ", minSdk.ToString ());
			cmd.AppendSwitchIfNotNull ("--max-sdk-version ", maxSdk.ToString ());
		
			if (!string.IsNullOrEmpty (AdditionalArguments))
				cmd.AppendSwitch (AdditionalArguments);

			cmd.AppendSwitchIfNotNull (" ", Path.GetFullPath (ApkToSign));

			return cmd;
		}

		protected override void LogEventsFromTextOutput (string singleLine, MessageImportance importance)
		{
			singleLine = singleLine.Trim ();
			if (singleLine.Length == 0)
				return;

			if (singleLine.StartsWith ("Warning:", StringComparison.OrdinalIgnoreCase)) {
				Log.LogCodedWarning ("ANDAS0000", JarPath, 0, singleLine);
				return;
			}

			Log.LogMessage (singleLine, importance);
		}

		protected override string ToolName {
			get { return OS.IsWindows ? "java.exe" : "java"; }
		}
	}
}
