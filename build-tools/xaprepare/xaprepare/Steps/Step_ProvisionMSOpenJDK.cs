using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Xamarin.Android.Prepare
{
	partial class Step_ProvisionMSOpenJDK : StepWithDownloadProgress
	{
		// Paths relative to JDK installation root, just for a cursory check whether we have a sane JDK instance
		// NOTE: file extensions are not necessary here
		static readonly List<string> jdkFiles = new List<string> {
			Path.Combine ("bin", "java"),
			Path.Combine ("bin", "javac"),
		};

		Uri archiveURL;
		string destinationDir;

		public Step_ProvisionMSOpenJDK ()
			: base ("Provisioning MS OpenJDK")
		{
			InitOS ();
		}

		protected override async Task<bool> Execute (Context context)
		{
			if (archiveURL == null) {
				Log.Warning ("MS OpenJDK is not supported on this platform");
				return true;
			}

			if (String.IsNullOrEmpty (destinationDir)) {
				Log.ErrorLine ("MS OpenJDK destination directory not specified.");
				return false;
			}

			string installDir = Path.Combine (destinationDir, $"microsoft_dist_openjdk_{Configurables.Defaults.MSOpenJDKVersion}");
			if (JDKExistsAndIsValid (installDir)) {
				Log.Status ("MS OpenJDK version ");
				Log.Status (Configurables.Defaults.MSOpenJDKVersion, ConsoleColor.Yellow);
				Log.StatusLine (" already installed in: ", installDir, tailColor: ConsoleColor.Cyan);
				return true;
			}

			string packageName = Path.GetFileName (archiveURL.LocalPath);
			string localPackagePath = Path.Combine (Configurables.Paths.MSOpenJDKCacheDir, packageName);
			if (!await DownloadMSOpenJDK (context, localPackagePath, archiveURL))
				return false;

			string tempDir = $"{installDir}.temp";
			try {
				if (!await Utilities.Unpack (localPackagePath, tempDir, cleanDestinatioBeforeUnpacking: true)) {
					Log.ErrorLine ("Failed to install MS OpenJDK");
					return false;
				}

				Utilities.MoveDirectoryContentsRecursively (tempDir, installDir);
			} finally {
				Utilities.DeleteDirectorySilent (tempDir);
				// Clean up zip after extraction if running on a hosted azure pipelines agent.
				if (context.IsRunningOnHostedAzureAgent)
					Utilities.DeleteFileSilent (localPackagePath);
			}

			return true;
		}

		async Task<bool> DownloadMSOpenJDK (Context context, string localPackagePath, Uri url)
		{
			if (File.Exists (localPackagePath)) {
				Log.StatusLine ("MS OpenJDK archive already downloaded");
				return true;
			}

			Log.StatusLine ("Downloading MS OpenJDK from ", url.ToString (), tailColor: ConsoleColor.White);
			(bool success, ulong size, HttpStatusCode status) = await Utilities.GetDownloadSizeWithStatus (url);
			if (!success) {
				if (status == HttpStatusCode.NotFound)
					Log.ErrorLine ("MS OpenJDK archive URL not found");
				else
					Log.ErrorLine ($"Failed to obtain MS OpenJDK size. HTTP status code: {status} ({(int)status})");
				return false;
			}

			DownloadStatus downloadStatus = Utilities.SetupDownloadStatus (context, size, context.InteractiveSession);
			Log.StatusLine ($"  {context.Characters.Link} {url}", ConsoleColor.White);
			await Download (context, url, localPackagePath, "MS OpenJDK", Path.GetFileName (localPackagePath), downloadStatus);

			if (!File.Exists (localPackagePath)) {
				Log.ErrorLine ($"Download of MS OpenJDK from {url} failed.");
				return false;
			}

			return true;
		}

		// The MS OpenJDK packages don't have exact version information, so we can't check whether the installed version
		// is the same as the one we need. We basically rely on the directory name here (since this is the only bit that
		// uses the exact version)
		bool JDKExistsAndIsValid (string installDir)
		{
			if (!Directory.Exists (installDir)) {
				Log.DebugLine ($"MS OpenJDK directory {installDir} does not exist");
				return false;
			}

			foreach (string f in jdkFiles) {
				string file = Path.Combine (installDir, f);
				if (!File.Exists (file)) {
					bool foundExe = false;
					foreach (string exe in Utilities.FindExecutable (f)) {
						file = Path.Combine (installDir, exe);
						if (File.Exists (file)) {
							foundExe = true;
							break;
						}
					}

					if (!foundExe) {
						Log.DebugLine ($"JDK file {file} missing from MS OpenJDK");
						return false;
					}
				}
			}

			return true;
		}

		partial void InitOS ();
	}
}
