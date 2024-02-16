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

	public class Aapt2LinkAssetPack : Aapt2 {
		public override string TaskPrefix => "A2LAP";

        [Required]
		public ITaskItem Manifest { get; set; }

		[Required]
		public ITaskItem AssetDirectory { get; set; }

        [Required]
        public string PackageName { get; set; }

        [Required]
        public ITaskItem OutputArchive { get; set; }

        public string OutputFormat { get; set; } = "proto";

		protected override int GetRequiredDaemonInstances ()
		{
			return Math.Min (1, DaemonMaxInstanceCount);
		}

		public async override System.Threading.Tasks.Task RunTaskAsync ()
		{
			RunAapt (GenerateCommandLineCommands (Manifest, AssetDirectory, OutputArchive), OutputArchive.ItemSpec);
			ProcessOutput ();
            if (OutputFormat == "proto" && File.Exists (OutputArchive.ItemSpec)) {
				// move the manifest to the right place.
				using (var zip = new ZipArchiveEx (OutputArchive.ItemSpec, File.Exists (OutputArchive.ItemSpec) ? FileMode.Open : FileMode.Create)) {
					zip.MoveEntry ("AndroidManifest.xml", "manifest/AndroidManifest.xml");
                    zip.Archive.DeleteEntry ("resources.pb");
				}
			}
		}

		protected string[] GenerateCommandLineCommands (ITaskItem manifest, ITaskItem assetDirectory, ITaskItem output)
		{
            //link --manifest AndroidManifest.xml --proto-format --custom-package $(Package) -A $(AssetsDirectory) -o $(_TempOutputFile)
			List<string> cmd = new List<string> ();
			cmd.Add ("link");
			if (MonoAndroidHelper.LogInternalExceptions)
				cmd.Add ("-v");
            cmd.Add ("--manifest");
            cmd.Add (GetFullPath (manifest.ItemSpec));
			if (OutputFormat == "proto") {
				cmd.Add ("--proto-format");
			}
            cmd.Add ("--custom-package");
            cmd.Add (PackageName);
            cmd.Add ("-A");
            cmd.Add (GetFullPath (assetDirectory.ItemSpec));
			cmd.Add ($"-o");
			cmd.Add (GetFullPath (output.ItemSpec));
			return cmd.ToArray ();
		}
	}
}