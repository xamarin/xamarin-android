using System;
using System.IO;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks {

	public class JniMarshalMethodGen : ToolTask {

		public string AdditionalArguments { get; set; }

		[Required]
		public ITaskItem [] Assemblies { get; set; }

		[Required]
		public string JvmPath { get; set; }

		[Required]
		public string MonoAndroidBinPath { get; set; }

		[Required]
		public string MonoAndroidToolsPath { get; set; }

		protected override string ToolName
	        {
			get { return OS.IsWindows ? "monow.exe" : "mono"; }
	        }

		string OSBits {
			get { return System.Environment.Is64BitOperatingSystem ? "64" : "32"; }
		}

		string OSDependentPath {
			get { return OS.IsWindows ? Path.Combine (MonoAndroidToolsPath, "lib", $"host-mxe-Win{OSBits}{Path.DirectorySeparatorChar}") : MonoAndroidBinPath; }
		}

		protected override string GenerateFullPathToTool ()
	        {
			return Path.GetFullPath (Path.Combine (OSDependentPath, ToolName));
	        }

		protected override string GenerateCommandLineCommands()
	        {
			CommandLineBuilder builder = new CommandLineBuilder();

			builder.AppendTextUnquoted ("--debug");

			builder.AppendFileNameIfNotNull (Path.Combine (MonoAndroidToolsPath, "jnimarshalmethod-gen.exe"));

			builder.AppendTextUnquoted ($" --jvm=\"{JvmPath}\"");

			if (!string.IsNullOrEmpty (AdditionalArguments))
				builder.AppendTextUnquoted (AdditionalArguments);

			builder.AppendFileNamesIfNotNull (Assemblies, " ");

			return builder.ToString ();
		}

		void CheckAndUpdateMonoPath ()
		{
			if (EnvironmentVariables == null)
				return;

			for (int i = 0; i < EnvironmentVariables.Length; i++) {
				if (EnvironmentVariables [i].StartsWith ("MONO_PATH=")) {
					EnvironmentVariables [i] = EnvironmentVariables [i].Replace (",", OS.IsWindows ? ";" : ":");
				}
			}
		}

		public override bool Execute ()
		{
			CheckAndUpdateMonoPath ();

			return base.Execute ();
		}
	}
}
