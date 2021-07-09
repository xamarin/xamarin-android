using System.IO;
using System.Xml;
using System.Collections.Generic;
using Microsoft.Build.Framework;
using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Utilities;

namespace Xamarin.Android.Tasks
{
	public class GetManifestVersions : AndroidTask
	{
		readonly HashSet<string> validPaths = new HashSet<string> {
			"manifest",
			"x86",
			"x86_64",
			"armeabi-v7a",
			"arm64-v8a",
		};
		public override string TaskPrefix => "GAP";

		public string ManifestPath { get; set; }

		public string PackageName { get; set; }

		[Output]
		public ITaskItem[] ManifestVersions { get; set; }

		public override bool RunTask ()
		{
			var versionInfo = new List<ITaskItem> ();
			foreach (string manifestFile in Directory.GetFiles (ManifestPath, "AndroidManifest.xml", SearchOption.AllDirectories))
			{
				string folder = Path.GetFileName (Path.GetDirectoryName (manifestFile));
				if (!validPaths.Contains (folder)) {
					continue;
				}
				string versionCode = string.Empty;
				string versionName = string.Empty;
				using var stream = File.OpenRead (manifestFile);
				using var reader = XmlReader.Create (stream);
				if (reader.MoveToContent () == XmlNodeType.Element) {
					versionCode = reader.GetAttribute ("android:versionCode");
					versionName = reader.GetAttribute ("android:versionName");
				}
				if (!string.IsNullOrEmpty (versionCode) && !string.IsNullOrEmpty (versionName)) {
					string suffix = folder == "manifest" ? "" : $"-{folder}";
					var data = new TaskItem ($"{PackageName}{suffix}");
					data.SetMetadata ("VersionCode", versionCode);
					data.SetMetadata ("VersionName", versionName);
					data.SetMetadata ("Abi", folder.Replace ("manifest", "all"));
					versionInfo.Add (data);
				}
			}
			ManifestVersions = versionInfo.ToArray ();
			return !Log.HasLoggedErrors;
		}
	}
}
