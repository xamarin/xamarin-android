using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.IO;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	/// <summary>
	/// Invokes `bundletool` to install an APK set to an attached device
	///
	/// Usage: bundletool install-apks --apks=foo.apks
	/// </summary>
	public class InstallApkSet : BundleToolAdbTask
	{
		public override string TaskPrefix => "IAS";

		[Required]
		public string ApkSet { get; set; }

		public string[] Modules  { get; set; }

		internal override CommandLineBuilder GetCommandLineBuilder ()
		{
			var cmd = base.GetCommandLineBuilder ();
			cmd.AppendSwitch ("install-apks");
			cmd.AppendSwitchIfNotNull ("--apks ", ApkSet);
			AppendAdbOptions (cmd);
			cmd.AppendSwitch ("--allow-downgrade");

			// --modules: List of modules to be installed, or "_ALL_" for all modules.
			// Defaults to modules installed during first install, i.e. not on-demand.
			// Xamarin.Android won't support on-demand modules yet.
			if (Modules != null)
				cmd.AppendSwitchIfNotNull ("--modules ", $"\"{string.Join ("\",\"", Modules)}\"");

			return cmd;
		}
	}
}
