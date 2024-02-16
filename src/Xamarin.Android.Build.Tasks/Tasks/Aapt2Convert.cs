using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks {

	public class Aapt2Convert : Aapt2 {
		public override string TaskPrefix => "A2C";

		[Required]
		public ITaskItem [] Files { get; set; }
		[Required]
		public ITaskItem OutputArchive { get; set; }
		public string OutputFormat { get; set; } = "binary";
		public string ExtraArgs { get; set; }

		protected override int GetRequiredDaemonInstances ()
		{
			return Math.Min (1, DaemonMaxInstanceCount);
		}

		public async override System.Threading.Tasks.Task RunTaskAsync ()
		{
			RunAapt (GenerateCommandLineCommands (Files, OutputArchive), OutputArchive.ItemSpec);
			ProcessOutput ();
			if (OutputFormat == "proto" && File.Exists (OutputArchive.ItemSpec)) {
				// move the manifest to the right place.
				using (var zip = new ZipArchiveEx (OutputArchive.ItemSpec, File.Exists (OutputArchive.ItemSpec) ? FileMode.Open : FileMode.Create)) {
					zip.MoveEntry ("AndroidManifest.xml", "manifest/AndroidManifest.xml");
				}
			}
		}

		protected string[] GenerateCommandLineCommands (IEnumerable<ITaskItem> files, ITaskItem output)
		{
			List<string> cmd = new List<string> ();
			cmd.Add ("convert");
			if (!string.IsNullOrEmpty (ExtraArgs))
				cmd.Add (ExtraArgs);
			if (MonoAndroidHelper.LogInternalExceptions)
				cmd.Add ("-v");
			if (!string.IsNullOrEmpty (OutputFormat)) {
				cmd.Add ("--output-format");
				cmd.Add (OutputFormat);
			}
			cmd.Add ($"-o");
			cmd.Add (GetFullPath (output.ItemSpec));
			foreach (var file  in files) {
				cmd.Add (GetFullPath (file.ItemSpec));
			}
			return cmd.ToArray ();
		}
	}
}
