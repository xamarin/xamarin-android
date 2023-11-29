using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Xamarin.Android.BuildTools.PrepTasks;

namespace Xamarin.Android.Tools.BootstrapTasks
{
	public class CreateAndroidEmulator : ToolTask
	{
		public                  string          SdkVersion      {get; set;}
		public                  string          AndroidAbi      {get; set;}
		/// <summary>
		/// Specifies $ANDROID_PREFS_ROOT. This is not the path to the Android SDK, but a root folder that contains the `.android` folder.
		/// </summary>
		[Required]
		public                  string          AvdManagerHome  {get; set;}
		public                  string          JavaSdkHome     {get; set;}

		public                  string          TargetId        {get; set;}

		public                  string          ImageName           {get; set;} = "XamarinAndroidTestRunner64";
		public                  string          DeviceName          {get; set;} = "pixel_4";
		public			string		ImageType	    {get; set;} = "google_atd";

		public                  string          DataPartitionSizeMB {get; set;} = "2048";
		public                  string          RamSizeMB           {get; set;} = "2048";

		protected override string ToolName => "avdmanager";

		public override bool Execute ()
		{
			if (string.IsNullOrEmpty (ToolExe))
				ToolExe = "avdmanager";

			var dirs = string.IsNullOrEmpty (ToolPath)
				? null
				: new [] { ToolPath };
			var path = Which.GetProgramLocation (ToolExe ?? ToolName, out var filename, dirs);
			if (path == null) {
				Log.LogError ($"Could not find `avdmanager`. Please set the `{nameof (CreateAndroidEmulator)}.{nameof (ToolPath)}` property appropriately.");
				return false;
			}
			ToolExe = filename;

			if (string.IsNullOrEmpty (TargetId) && !string.IsNullOrEmpty (SdkVersion)) {
				TargetId    = $"system-images;android-{SdkVersion};{ImageType};{AndroidAbi}";
			}

			var env = new List<string> ();
			env.Add ($"ANDROID_PREFS_ROOT={AvdManagerHome}");
			if (!string.IsNullOrEmpty (JavaSdkHome)) {
				env.Add ($"JAVA_HOME={JavaSdkHome}");
			}
			EnvironmentVariables = env.ToArray ();

			base.Execute ();

			if (Log.HasLoggedErrors)
				return false;

			var configPath = Path.Combine (AvdManagerHome, ".android", "avd", $"{ImageName}.avd", "config.ini");
			if (File.Exists (configPath)) {
				Log.LogMessage ($"Config file for AVD '{ImageName}' found at {configPath}");
				WriteConfigFile (configPath);
				return !Log.HasLoggedErrors;
			}

			Log.LogWarning ($"AVD '{ImageName}' will use default emulator settings (memory and data partition size)");

			return !Log.HasLoggedErrors;
		}

		void WriteConfigFile (string configPath)
		{
			if (!ulong.TryParse (DataPartitionSizeMB, out var diskSize))
				Log.LogError ($"Invalid data partition size '{DataPartitionSizeMB}' - must be a positive integer value expressing size in megabytes");

			if (!ulong.TryParse (RamSizeMB, out var ramSize))
				Log.LogError ($"Invalid RAM size '{RamSizeMB}' - must be a positive integer value expressing size in megabytes");

			if (Log.HasLoggedErrors)
				return;

			var values = new [] {
				new ConfigValue {
					Key = "disk.dataPartition.size=",
					Value = diskSize,
					Suffix = "M",
				},
				new ConfigValue {
					Key = "hw.ramSize=",
					Value = ramSize,
				}
			};

			var lines = new List<string> ();
			using (var reader = File.OpenText (configPath)) {
				while (!reader.EndOfStream) {
					var line = reader.ReadLine ();
					foreach (var value in values) {
						if (line == value.ToString ()) {
							value.Set = true;
							Log.LogMessage (MessageImportance.Low, $"Value already present: {line}");
						} else if (line.StartsWith (value.Key, StringComparison.OrdinalIgnoreCase)) {
							continue;
						}
					}
					lines.Add (line);
				}
			}

			var append = values.Where (v => !v.Set);
			if (!append.Any ()) {
				Log.LogMessage (MessageImportance.Low, $"Skip writing file: {configPath}");
				return;
			}

			lines.AddRange (append.Select (v => v.ToString ()));
			File.WriteAllLines (configPath, lines);
		}

		protected override string GenerateCommandLineCommands ()
		{
			var cmd = new CommandLineBuilder ();
			cmd.AppendSwitch ("create avd");
			cmd.AppendSwitchIfNotNull ("--abi ", AndroidAbi);
			cmd.AppendSwitch ("--force");
			cmd.AppendSwitchIfNotNull ("--name ", ImageName);
			cmd.AppendSwitchIfNotNull ("--package ", TargetId);
			cmd.AppendSwitchIfNotNull ("--device ", DeviceName);
			return cmd.ToString ();
		}

		protected override string GenerateFullPathToTool () => Path.Combine (ToolPath, ToolExe);

		class ConfigValue
		{
			/// <summary>
			/// Key including the `=` symbol
			/// </summary>
			public string Key { get; set; }

			public ulong Value { get; set; }

			/// <summary>
			/// Set to true if the file contains this value
			/// </summary>
			public bool Set { get; set; }

			/// <summary>
			/// Optional suffix such as `M`
			/// </summary>
			public string Suffix { get; set; } = "";

			public override string ToString ()
			{
				return $"{Key}{Value}{Suffix}";
			}
		}
	}
}
