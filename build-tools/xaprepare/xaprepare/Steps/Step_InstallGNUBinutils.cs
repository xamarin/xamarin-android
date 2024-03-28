using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Xamarin.Android.Prepare
{
	partial class Step_InstallGNUBinutils : StepWithDownloadProgress
	{
		static readonly string[]? WindowsExtensions = {".exe", ".cmd"};
		static readonly string ProductName = $"Xamarin.Android Toolchain {Configurables.Defaults.BinutilsVersion}";

		public Step_InstallGNUBinutils ()
			: base ("Install Xamarin.Android Toolchain")
		{}

		protected override async Task<bool> Execute (Context context)
		{
			string hostDestinationDirectory = Configurables.Paths.HostBinutilsInstallDir;
			string windowsDestinationDirectory = Configurables.Paths.WindowsBinutilsInstallDir;

			bool hostHaveAll = HaveAllBinutils (hostDestinationDirectory);
			bool windowsHaveAll = HaveAllBinutils (windowsDestinationDirectory, WindowsExtensions);

			if (hostHaveAll && windowsHaveAll) {
				Log.StatusLine ("All Binutils are already installed");
				return true;
			}

			string packageName = Path.GetFileName (Configurables.Urls.BinutilsArchive.LocalPath);
			string localArchivePath = Path.Combine (Configurables.Paths.BinutilsCacheDir, packageName);

			if (!await DownloadBinutils (context, localArchivePath, Configurables.Urls.BinutilsArchive)) {
				return false;
			}

			string tempDir = Path.Combine (Path.GetTempPath (), "xaprepare-binutils");
			Utilities.DeleteDirectorySilent (tempDir);
			Utilities.CreateDirectory (tempDir);

			Log.DebugLine ($"Unpacking {ProductName} archive {localArchivePath} into {tempDir}");
			if (!await Utilities.Unpack (localArchivePath, tempDir, cleanDestinatioBeforeUnpacking: true)) {
				return false;
			}

			// HACK START: replace distribution as.{exe,pdb} with the "bundled" one. To be removed before PR can be merged
			string asDestDir = Path.Combine (tempDir, "windows", "bin");
			File.Copy (Path.Combine (Configurables.Paths.BootstrapResourcesDir, "as.exe"), Path.Combine (asDestDir, "as.exe"), true);
			File.Copy (Path.Combine (Configurables.Paths.BootstrapResourcesDir, "as.pdb"), Path.Combine (asDestDir, "as.pdb"), true);
			// HACK END

			if (!hostHaveAll) {
				CopyToDestination (context, "Host", tempDir, hostDestinationDirectory, executableExtensions: ExecutableExtensions);
			}

			if (!windowsHaveAll) {
				CopyToDestination (context, "Windows", tempDir, windowsDestinationDirectory, "windows", WindowsExtensions);
			}

			return true;
		}



		bool CopyToDestination (Context context, string label, string sourceDir, string destinationDir, string osName = HostName, string[]? executableExtensions = null)
		{
			bool isWindows = osName == "windows";

			Log.StatusLine ();
			Log.StatusLine ($"Installing for {label}:");

			string osSourcePath = Path.Combine (sourceDir, osName);
			string sourcePath = Path.Combine (osSourcePath, "bin");
			string symbolArchiveDir = Path.Combine (destinationDir, "windows-toolchain-pdb");
			foreach (var kvp in Configurables.Defaults.AndroidToolchainPrefixes) {
				string prefix = kvp.Value;
				CopyTools (prefix);
			}
			CopyTools (String.Empty);
			CopyLibraries ();

			return true;

			void CopyLibraries ()
			{
				if (isWindows) {
					return;
				}

				string libSourcePath = Path.Combine (osSourcePath, "lib");
				string libDestPath = Path.Combine (destinationDir, "lib");
				foreach (string file in Directory.EnumerateFiles (libSourcePath)) {
					Utilities.CopyFileToDir (file, libDestPath);
				}
			}

			void CopyTools (string prefix)
			{
				bool copyPrefixed = !String.IsNullOrEmpty (prefix);
				foreach (NDKTool tool in Configurables.Defaults.NDKTools) {
					if (tool.Prefixed != copyPrefixed) {
						continue;
					}

					string toolSourcePath = GetToolPath (sourcePath, prefix, tool, executableExtensions, throwOnMissing: true);
					string toolName = Path.GetFileName (toolSourcePath);
					string toolDestinationPath = Path.Combine (destinationDir, "bin", toolName);
					string versionMarkerPath = GetVersionMarker (toolDestinationPath);

					Log.StatusLine ($"  {context.Characters.Bullet} Installing ", toolName, tailColor: ConsoleColor.White);
					Utilities.CopyFile (toolSourcePath, toolDestinationPath);
					File.WriteAllText (versionMarkerPath, DateTime.UtcNow.ToString ());

					if (!isWindows) {
						continue;
					}

					// Copy PDBs and corresponding EXEs to a folder to be zipped up for symbol archiving
					string toolSourcePdbPath = Path.ChangeExtension (toolSourcePath, ".pdb");
					if (!File.Exists (toolSourcePdbPath)) {
						continue;
					}

					toolDestinationPath = Path.Combine (symbolArchiveDir, toolName);
					string toolDestinationPdbPath = Path.ChangeExtension (toolDestinationPath, ".pdb");

					Log.StatusLine ($"  {context.Characters.Bullet} Copying symbols for ", toolName, tailColor: ConsoleColor.White);
					Utilities.CopyFile (toolSourcePath, toolDestinationPath);
					Utilities.CopyFile (toolSourcePdbPath, toolDestinationPdbPath);
				}
			}
		}

		async Task<bool> DownloadBinutils (Context context, string localPackagePath, Uri url)
		{
			if (Utilities.FileExists (localPackagePath)) {
				Log.StatusLine ($"{ProductName} archive already downloaded");
				return true;
			}

			Log.StatusLine ($"Downloading {ProductName} from ", url.ToString (), tailColor: ConsoleColor.White);
			(bool success, ulong size, HttpStatusCode status) = await Utilities.GetDownloadSizeWithStatus (url);
			if (!success) {
				if (status == HttpStatusCode.NotFound) {
					Log.ErrorLine ($"{ProductName} archive URL not found");
					return false;
				}
				Log.WarningLine ($"Failed to obtain {ProductName} size. HTTP status code: {status} ({(int)status})");
			}

			DownloadStatus downloadStatus = Utilities.SetupDownloadStatus (context, size, context.InteractiveSession);
			Log.StatusLine ($"  {context.Characters.Link} {url}", ConsoleColor.White);
			await Download (context, url, localPackagePath, ProductName, Path.GetFileName (localPackagePath), downloadStatus);

			if (!File.Exists (localPackagePath)) {
				Log.ErrorLine ($"Download of {ProductName} from {url} failed.");
				return false;
			}

			return true;
		}

		bool HaveAllBinutils (string dir, string[]? executableExtensions = null)
		{
			Log.DebugLine ("Checking if all binutils are installed in {dir}");
			foreach (var kvp in Configurables.Defaults.AndroidToolchainPrefixes) {
				string prefix = kvp.Value;
				if (!CheckToolsExist (prefix)) {
					return false;
				}
			}

			return CheckToolsExist (String.Empty);

			bool CheckToolsExist (string prefix)
			{
				bool checkPrefixed = !String.IsNullOrEmpty (prefix);
				foreach (NDKTool tool in Configurables.Defaults.NDKTools) {
					if (tool.Prefixed != checkPrefixed) {
						continue;
					}
					string toolPath = GetToolPath (dir, prefix, tool, executableExtensions);
					string toolName = Path.GetFileName (toolPath);
					string versionMarkerPath = GetVersionMarker (toolPath);

					Log.DebugLine ($"Checking {toolName}");
					if (!Utilities.FileExists (toolPath)) {
						Log.DebugLine ($"Binutils tool {toolPath} does not exist");
						return false;
					}

					if (!Utilities.FileExists (versionMarkerPath)) {
						Log.DebugLine ($"Binutils tool {toolPath} exists, but its version is incorrect");
						return false;
					}
				}

				return true;
			}
		}

		string GetToolPath (string sourcePath, string prefix, NDKTool tool, string[]? executableExtensions = null, bool throwOnMissing = false)
		{
			string baseName = $"{(String.IsNullOrEmpty (tool.DestinationName) ? tool.Name : tool.DestinationName)}";

			if (!String.IsNullOrEmpty (prefix)) {
				baseName = $"{prefix}-{baseName}";
			}

			if (executableExtensions == null || executableExtensions.Length == 0) {
				return Path.Combine (sourcePath, baseName);
			}

			foreach (string executableExtension in executableExtensions) {
				string binary = Path.Combine (sourcePath, $"{baseName}{executableExtension}");
				Console.WriteLine ($"Checking: {binary}");
				if (!Utilities.FileExists (binary)) {
					continue;
				}

				return binary;
			}

			if (throwOnMissing) {
				string extensions = String.Join (",", executableExtensions);
				throw new InvalidOperationException ($"Failed to find binary file '{baseName}{{{extensions}}}'");
			}

			return baseName;
		}

		string GetVersionMarker (string toolPath)
		{
			return $"{toolPath}.{Configurables.Defaults.BinutilsVersion}";
		}
	}
}
