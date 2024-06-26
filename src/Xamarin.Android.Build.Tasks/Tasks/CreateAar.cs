using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Build.Framework;
using Xamarin.Android.Tools;
using Xamarin.Tools.Zip;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class CreateAar : AndroidTask
	{
		public override string TaskPrefix => "CAAR";

		public ITaskItem [] AndroidAssets { get; set; }

		public ITaskItem [] AndroidResources { get; set; }

		public ITaskItem [] AndroidEnvironment { get; set; }

		public ITaskItem AndroidManifest { get; set; }

		public ITaskItem [] JarFiles { get; set; }

		public ITaskItem [] NativeLibraries { get; set; }

		public ITaskItem [] ProguardConfigurationFiles { get; set; }

		[Required]
		public string AssetDirectory { get; set; }

		[Required]
		public string OutputFile { get; set; }

		public override bool RunTask ()
		{
			Directory.CreateDirectory (Path.GetDirectoryName (OutputFile));

			using (var stream = File.Create (OutputFile))
			using (var aar = ZipArchive.Open (stream)) {
				var existingEntries = new HashSet<string> (StringComparer.Ordinal);
				foreach (var entry in aar) {
					Log.LogDebugMessage ("Existing entry: " + entry.FullName);
					existingEntries.Add (entry.FullName);
				}
				if (AndroidAssets != null) {
					foreach (var asset in AndroidAssets) {
						// See: https://github.com/xamarin/xamarin-android/commit/665cb59205f8ac565b6acbda740624844bc1cbd9
						if (Directory.Exists (asset.ItemSpec)) {
							Log.LogDebugMessage ($"Skipping item, is a directory: {asset.ItemSpec}");
							continue;
						}
						var relative = MonoAndroidHelper.GetRelativePathForAndroidAsset (AssetDirectory, asset);
						var archivePath = "assets/" + relative.Replace ('\\', '/');
						aar.AddStream (File.OpenRead (asset.ItemSpec), archivePath);
						existingEntries.Remove (archivePath);
					}
				}
				if (AndroidResources != null) {
					var nameCaseMap = new StringBuilder ();
					foreach (var resource in AndroidResources) {
						// See: https://github.com/xamarin/xamarin-android/commit/665cb59205f8ac565b6acbda740624844bc1cbd9
						if (Directory.Exists (resource.ItemSpec)) {
							Log.LogDebugMessage ($"Skipping item, is a directory: {resource.ItemSpec}");
							continue;
						}
						var directory = Path.GetDirectoryName (resource.ItemSpec);
						var resourcePath = Path.GetFileName (directory) + "/" + Path.GetFileName (resource.ItemSpec);
						var archivePath = "res/" + resourcePath;
						aar.AddStream (File.OpenRead (resource.ItemSpec), archivePath);
						existingEntries.Remove (archivePath);

						nameCaseMap.Append (resource.GetMetadata ("LogicalName").Replace ('\\', '/'));
						nameCaseMap.Append (';');
						nameCaseMap.AppendLine (resourcePath);
					}
					if (nameCaseMap.Length > 0) {
						var archivePath = ".net/__res_name_case_map.txt";
						aar.AddEntry (archivePath, nameCaseMap.ToString (), Files.UTF8withoutBOM);
						existingEntries.Remove (archivePath);
					}
				}
				if (AndroidEnvironment != null) {
					foreach (var env in AndroidEnvironment) {
						var archivePath = $".net/env/{GetHashedFileName (env)}.env";
						aar.AddStream (File.OpenRead (env.ItemSpec), archivePath);
						existingEntries.Remove (archivePath);
					}
				}
				if (JarFiles != null) {
					foreach (var jar in JarFiles) {
						var archivePath = $"libs/{GetHashedFileName (jar)}.jar";
						aar.AddStream (File.OpenRead (jar.ItemSpec), archivePath);
						existingEntries.Remove (archivePath);
					}
				}
				if (NativeLibraries != null) {
					foreach (var lib in NativeLibraries) {
						var abi = AndroidRidAbiHelper.GetNativeLibraryAbi (lib);
						if (string.IsNullOrWhiteSpace (abi)) {
							Log.LogCodedError ("XA4301", lib.ItemSpec, 0, Properties.Resources.XA4301_ABI, lib.ItemSpec);
							continue;
						}
						var archivePath = "jni/" + abi + "/" + Path.GetFileName (lib.ItemSpec);
						aar.AddStream (File.OpenRead (lib.ItemSpec), archivePath);
						existingEntries.Remove (archivePath);
					}
				}
				if (ProguardConfigurationFiles != null) {
					var sb = new StringBuilder ();
					foreach (var file in ProguardConfigurationFiles) {
						sb.AppendLine (File.ReadAllText (file.ItemSpec));
					}
					aar.AddEntry ("proguard.txt", sb.ToString (), Files.UTF8withoutBOM);
				}
				if (AndroidManifest != null && File.Exists (AndroidManifest.ItemSpec)) {
					var sb = new StringBuilder ();
					sb.AppendLine (File.ReadAllText (AndroidManifest.ItemSpec));
					aar.AddEntry ("AndroidManifest.xml", sb.ToString (), Files.UTF8withoutBOM);
				}
				foreach (var entry in existingEntries) {
					Log.LogDebugMessage ($"Removing {entry} as it is not longer required.");
					aar.DeleteEntry (entry);
				}
			}

			// Delete the archive on failure
			if (Log.HasLoggedErrors && File.Exists (OutputFile)) {
				File.Delete (OutputFile);
			}

			return !Log.HasLoggedErrors;
		}

		/// <summary>
		/// Hash the path to an ITaskItem to get a unique file name.
		/// Replaces \ with /, so we get the same hash on all platforms.
		/// </summary>
		static string GetHashedFileName (ITaskItem item) =>
			Files.HashString (item.ItemSpec.Replace ('\\', '/'));
	}
}
