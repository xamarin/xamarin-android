using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class GetImportedLibraries : AndroidTask
	{
		public override string TaskPrefix => "GIL";

		static readonly string [] IgnoredManifestDirectories = new [] {
			"bin",
			"manifest",
			"aapt",
		};

		[Required]
		public string TargetDirectory { get; set; }

		public string CacheFile { get; set;}

		[Output]
		public ITaskItem [] Jars { get; set; }

		[Output]
		public ITaskItem [] NativeLibraries { get; set; }

		[Output]
		public ITaskItem [] ManifestDocuments { get; set; }

		[Output]
		public string LibraryMinSdk { get; set; } = string.Empty;

		public override bool RunTask ()
		{
			if (!Directory.Exists (TargetDirectory)) {
				Log.LogDebugMessage ("Target directory was not found");
				return true;
			}

			var manifestDocuments = new List<ITaskItem> ();
			var nativeLibraries   = new List<ITaskItem> ();
			var jarFiles          = new List<ITaskItem> ();
			int libraryMinSdk  = -1;
			foreach (var file in Directory.EnumerateFiles (TargetDirectory, "*", SearchOption.AllDirectories)) {
				if (file.EndsWith (".so", StringComparison.OrdinalIgnoreCase)) {
					if (AndroidRidAbiHelper.GetNativeLibraryAbi (file) != null)
						nativeLibraries.Add (new TaskItem (file));
				} else if (file.EndsWith (".jar", StringComparison.OrdinalIgnoreCase)) {
					jarFiles.Add (new TaskItem (file));
				} else if (file.EndsWith (".xml", StringComparison.OrdinalIgnoreCase)) {
					if (Path.GetFileName (file) == "AndroidManifest.xml") {
						// there could be ./AndroidManifest.xml and bin/AndroidManifest.xml, which will be the same. So, ignore "bin" ones.
						var directory = Path.GetFileName (Path.GetDirectoryName (file));
						if (IgnoredManifestDirectories.Contains (directory))
							continue;
						var item = new TaskItem (file);
						XDocument doc = XDocument.Load (file);
						var minAttr = doc.Root.Element ("uses-sdk")?.Attribute (ManifestDocument.AndroidXmlNamespace + "minSdkVersion");
						if (minAttr != null) {
							if (int.TryParse (minAttr.Value, out int minSDK))
								libraryMinSdk = Math.Max (libraryMinSdk, minSDK);
						}
						manifestDocuments.Add (item);
					}
				}
			}

			XAttribute minSdk = null;
			if (libraryMinSdk != -1) {
				LibraryMinSdk = libraryMinSdk.ToString ();
				minSdk = new XAttribute ("libraryMinSdk", LibraryMinSdk);
			}
			ManifestDocuments = manifestDocuments.ToArray ();
			NativeLibraries = nativeLibraries.ToArray ();
			Jars = jarFiles.ToArray ();

			if (!string.IsNullOrEmpty (CacheFile)) {
				var document = new XDocument (
							new XDeclaration ("1.0", "UTF-8", null),
							new XElement ("Paths",
									new XElement ("ManifestDocuments", minSdk, ManifestDocuments.Select(e => new XElement ("ManifestDocument", e.ItemSpec))),
									new XElement ("NativeLibraries", NativeLibraries.Select(e => new XElement ("NativeLibrary", e.ItemSpec))),
									new XElement ("Jars", Jars.Select(e => new XElement ("Jar", e.ItemSpec)))
						));
				document.SaveIfChanged (CacheFile);
			}

			return true;
		}
	}


}
