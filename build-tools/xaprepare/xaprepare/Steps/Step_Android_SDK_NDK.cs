using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kajabity.Tools.Java;

namespace Xamarin.Android.Prepare
{
	class Step_Android_SDK_NDK : StepWithDownloadProgress
	{
#nullable disable
		sealed class AndroidPackage
		{
			public AndroidToolchainComponent Component;
			public string PackageName;
			public Uri Url;
			public string LocalPackagePath;
			public string DestinationDir;
		}
#nullable enable

		bool RefreshSdk = false;
		bool RefreshNdk = false;
		AndroidToolchainComponentType DependencyTypeToInstall = AndroidToolchainComponentType.All;

		public Step_Android_SDK_NDK (AndroidToolchainComponentType dependencyTypeToInstall = AndroidToolchainComponentType.All)
			: base ("Preparing Android SDK and NDK")
		{
			DependencyTypeToInstall = dependencyTypeToInstall;
		}

		protected override async Task<bool> Execute (Context context)
		{
			string sdkRoot = context.Properties.GetRequiredValue (KnownProperties.AndroidSdkDirectory);
			string ndkRoot = context.Properties.GetRequiredValue (KnownProperties.AndroidNdkDirectory);
			string packageCacheDir = context.Properties.GetRequiredValue (KnownProperties.AndroidToolchainCacheDirectory);

			RefreshSdk = context.ComponentsToRefresh.HasFlag (RefreshableComponent.AndroidSDK);
			RefreshNdk = context.ComponentsToRefresh.HasFlag (RefreshableComponent.AndroidNDK);

			Log.StatusLine ("Android SDK location: ", sdkRoot, tailColor: Log.DestinationColor);
			Log.StatusLine ("Android NDK location: ", ndkRoot, tailColor: Log.DestinationColor);
			Log.DebugLine ($"Toolchain cache directory: {packageCacheDir}");

			var toolchain = new AndroidToolchain ();
			var toInstall = new List <AndroidPackage> ();

			toolchain.Components.ForEach (c => Check (context, packageCacheDir, sdkRoot, c, toInstall, 4));
			if (toInstall.Count == 0) {
				if (!AcceptLicenses (context, sdkRoot)) {
					Log.ErrorLine ("Failed to accept Android SDK licenses");
					return false;
				}
				WritePackageXmls (sdkRoot);
				return GatherNDKInfo (context);
			}

			Log.MessageLine ();
			toInstall.ForEach (p => Log.DebugLine ($"Missing Android component: {p.Component.Name}"));

			string tempDir = Path.Combine (context.Properties.GetRequiredValue (KnownProperties.AndroidToolchainDirectory), "temp");
			Log.DebugLine ($"Toolchain temporary directory: {tempDir}");

			if (Directory.Exists (tempDir)) {
				Log.DebugLine ("Temporary directory exists, cleaning it up");
				Utilities.DeleteDirectorySilent (tempDir);
			}
			Directory.CreateDirectory (tempDir);

			Log.MessageLine ("Installing missing components");
			var toDownload = new List <AndroidPackage> ();
			toInstall.ForEach (c => CheckPackageStatus (context, packageCacheDir, c, toDownload));

			if (toDownload.Count > 0) {
				ulong totalDownloadSize = 0;
				foreach (AndroidPackage pkg in toDownload) {
					Log.DebugLine ($"Android component '{pkg.Component.Name}' will be downloaded from {pkg.Url}");
					(bool success, ulong size) = await Utilities.GetDownloadSize (pkg.Url);
					if (!success)
						continue;
					totalDownloadSize += size;
				}

				toDownload.ForEach (p => Log.StatusLine ($"  {context.Characters.Link} {p.Url}", ConsoleColor.White));

				DownloadStatus downloadStatus = Utilities.SetupDownloadStatus (context, totalDownloadSize, context.InteractiveSession);
				await Task.WhenAll (toDownload.Select (p => Download (context, p.Url, p.LocalPackagePath, p.Component.Name, p.PackageName, downloadStatus)));
			}

			foreach (AndroidPackage p in toInstall) {
				await Unpack (context, tempDir, p);
			}

			if (!AcceptLicenses (context, sdkRoot)) {
				Log.ErrorLine ("Failed to accept Android SDK licenses");
				return false;
			}

			WritePackageXmls (sdkRoot);

			return GatherNDKInfo (context);
		}

		bool AcceptLicenses (Context context, string sdkRoot)
		{
			string[] sdkManagerPaths = new[]{
				Path.Combine (sdkRoot, "cmdline-tools", context.Properties [KnownProperties.CommandLineToolsFolder] ?? String.Empty, "bin", "sdkmanager"),
				Path.Combine (sdkRoot, "cmdline-tools", "latest", "bin", "sdkmanager"),
			};
			string sdkManager = "";
			foreach (var sdkManagerPath in sdkManagerPaths) {
				sdkManager = context.OS.Which (sdkManagerPath, required: false);
				if (!string.IsNullOrEmpty (sdkManager))
					break;
			}
			if (sdkManager.Length == 0)
				throw new InvalidOperationException ("sdkmanager not found");
			string jdkDir = context.OS.JavaHome;

			Log.Todo ("Modify ProcessRunner to allow standard input writing and switch to it here");
			// var runner = new ProcessRunner (sdkManager, "--licenses");
			// runner.StartInfoCallback = (ProcessStartInfo psi) => {
			// 	if (!String.IsNullOrEmpty (jdkDir))
			// 		psi.EnvironmentVariables.Add ("JAVA_HOME", jdkDir);
			// 	psi.RedirectStandardInput = true;
			// };

			var psi = new ProcessStartInfo (sdkManager, "--licenses") {
				UseShellExecute = false,
				RedirectStandardInput = true
			};
			if (!String.IsNullOrEmpty (jdkDir) && !psi.EnvironmentVariables.ContainsKey ("JAVA_HOME"))
				psi.EnvironmentVariables.Add ("JAVA_HOME", jdkDir);

			Log.DebugLine ($"Starting {psi.FileName} {psi.Arguments}");
			Process? proc = Process.Start (psi);
			if (proc != null) {
				for (int i = 0; i < 10; i++)
					proc.StandardInput.WriteLine ('y');

				proc.WaitForExit ();
			} else {
				Log.DebugLine ("Failed to start process");
			}

			return true;
		}

		bool GatherNDKInfo (Context context)
		{
			// Ignore NDK property setting if not installing the NDK
			if (!DependencyTypeToInstall.HasFlag (AndroidToolchainComponentType.BuildDependency))
				return true;
			else
				return context.BuildInfo.GatherNDKInfo (context);
		}

		void CheckPackageStatus (Context context, string packageCacheDir, AndroidPackage pkg, List <AndroidPackage> toDownload)
		{
			Log.StatusLine ($"  {context.Characters.Bullet} Installing ", pkg.Component.Name, tailColor: ConsoleColor.White);

			if (File.Exists (pkg.LocalPackagePath)) {
				if ((RefreshSdk && !IsNdk (pkg.Component)) || (RefreshNdk && IsNdk (pkg.Component))) {
					LogStatus ("Reinstall requested, deleting cache", 4, ConsoleColor.Magenta);
					Utilities.DeleteFile (pkg.LocalPackagePath);
					toDownload.Add (pkg);
				} else {
					Log.DebugLine ($"Component '{pkg.Component.Name}' package exists: {pkg.LocalPackagePath}");
					LogStatus ("already downloaded", 4, Log.InfoColor);
				}
			} else {
				Log.DebugLine ($"Component '{pkg.Component.Name}' package not downloaded yet: {pkg.LocalPackagePath}");
				LogStatus ("not downloaded yet", 4, ConsoleColor.Magenta);
				toDownload.Add (pkg);
			}
		}

		async Task Unpack (Context context, string tempDirRoot, AndroidPackage pkg)
		{
			Log.StatusLine (PadStatus ($"unpacking {pkg.PackageName} to ", 4), pkg.DestinationDir, tailColor: Log.DestinationColor);

			string sevenZip = context.Tools.SevenZipPath;
			Log.DebugLine ($"7z binary path: {sevenZip}");

			string tempDir = Path.Combine (tempDirRoot, Path.GetRandomFileName ());

			if (!await Utilities.Unpack (pkg.LocalPackagePath, tempDir)) {
				Utilities.DeleteFileSilent (pkg.LocalPackagePath);
				throw new InvalidOperationException ($"Failed to unpack {pkg.LocalPackagePath}");
			}

			// Clean up zip after extraction if running on a hosted azure pipelines agent.
			if (context.IsRunningOnHostedAzureAgent)
				Utilities.DeleteFileSilent (pkg.LocalPackagePath);

			if (pkg.Component.NoSubdirectory) {
				Utilities.MoveDirectoryContentsRecursively (tempDir, pkg.DestinationDir);
				return;
			}

			// There should be just a single subdirectory
			List<string> subdirs = Directory.EnumerateDirectories (tempDir).ToList ();
			if (subdirs.Count > 1)
				throw new InvalidOperationException ($"Unexpected contents layout of Android component '{pkg.Component.Name}' - expected a single subdirectory, instead found {subdirs.Count}");

			Utilities.MoveDirectoryContentsRecursively (subdirs [0], pkg.DestinationDir);
		}

		Uri GetPackageUrl (AndroidToolchainComponent component, string packageName)
		{
			Uri packageUrl;

			if (component.RelativeUrl != null)
				packageUrl = new Uri (AndroidToolchain.AndroidUri, component.RelativeUrl);
			else
				packageUrl = AndroidToolchain.AndroidUri;

			return new Uri (packageUrl, packageName);
		}

		string GetDestinationDir (AndroidToolchainComponent component, string sdkRoot)
		{
			string path = component.DestDir;
			if (!Path.IsPathRooted (path))
				return Path.Combine (sdkRoot, path);
			return path;
		}

		void Check (Context context, string packageCacheDir, string sdkRoot, AndroidToolchainComponent component, List <AndroidPackage> toInstall, int padLeft)
		{
			Log.StatusLine ($"  {context.Characters.Bullet} Checking ", component.Name, tailColor: ConsoleColor.White);

			if (!DependencyTypeToInstall.HasFlag (component.DependencyType)) {
				LogStatus ($"skipping, did not match dependency type: {Enum.GetName(typeof(AndroidToolchainComponentType), DependencyTypeToInstall)}", padLeft, Log.InfoColor);
				return;
			}

			component.AddToInventory ();

			const string statusMissing = "missing";
			const string statusOutdated = "outdated";
			const string statusInstalled = "installed";

			string path = GetDestinationDir (component, sdkRoot);

			Log.DebugLine ($"Checking if {component.Name} exists in {path}");
			bool missing;
			if (IsInstalled (component, path, out missing)) {
				LogStatus (statusInstalled, padLeft, Log.InfoColor);
				return;
			}

			// If only specific Android SDK platforms were requested, ignore ones that were not requested
			if (component is AndroidPlatformComponent apc && !ShouldInstall (apc, context)) {
				LogStatus ($"skipping, not requested", padLeft, Log.InfoColor);
				return;
			}

			if (missing)
				LogStatus (statusMissing, padLeft, ConsoleColor.Magenta);
			else
				LogStatus (statusOutdated, padLeft, ConsoleColor.DarkYellow);

			string packageName = $"{component.Name}.zip";
			var pkg = new AndroidPackage {
				Component = component,
				PackageName = packageName,
				Url = GetPackageUrl (component, packageName),
				LocalPackagePath = Path.Combine (packageCacheDir, component.RelativeUrl.ToString () ?? string.Empty, packageName),
				DestinationDir = GetDestinationDir (component, sdkRoot),
			};

			toInstall.Add (pkg);
		}

		bool ShouldInstall (AndroidPlatformComponent component, Context context)
		{
			var platforms = context.AndroidSdkPlatforms;

			// If no specific platforms were requested, install everything
			if (!platforms.Any () || platforms.Contains ("all"))
				return true;

			// If "latest" was requested, install the highest available stable version and any preview versions
			if (platforms.Contains ("latest") && (component.IsLatestStable || component.IsPreview))
				return true;

			// Check if this is a user-requested platform
			return context.AndroidSdkPlatforms.Contains (component.ApiLevel);
		}

		bool IsInstalled (AndroidToolchainComponent component, string path, out bool missing)
		{
			missing = true;
			if (!Directory.Exists (path)) {
				Log.DebugLine ($"Component '{component.Name}' directory does not exist: {path}");
				return false;
			}

			// This is just a cursory check, we might want to check versions
			string propsFile = Path.Combine (path, "source.properties");
			if (!File.Exists (propsFile)) {
				Log.DebugLine ($"Component '{component.Name}' properties file does not exist: {propsFile}");
				return false;
			}

			missing = false;
			if ((RefreshSdk && !IsNdk (component)) || (RefreshNdk && IsNdk (component))) {
				Log.DebugLine ($"A reinstall has been requested for component '{component.Name}'");
				return false;
			}

			if (String.IsNullOrEmpty (component.PkgRevision)) {
				Log.DebugLine ($"Component '{component.Name}' does not specify required Pkg.Revision, assuming it's valid");
				return true;
			}

			Log.DebugLine ($"Component '{component.Name}' requires Pkg.Revision to be '{component.PkgRevision}', verifying");
			var props = new JavaProperties ();
			try {
				using (var fs = File.OpenRead (propsFile)) {
					props.Load (fs);
				}
			} catch (Exception ex) {
				Log.DebugLine ($"Failed to read '{component.Name}' source.properties. Assuming invalid version, component will be reinstalled.");
				Log.DebugLine (ex.ToString ());
				return false;
			}

			string pkgRevision = props.GetProperty ("Pkg.Revision", String.Empty);
			if (String.IsNullOrEmpty (pkgRevision)) {
				Log.DebugLine ($"Component '{component.Name}' does not have Pkg.Revision in its source.properties file, it will be reinstalled.");
				return false;
			}

			if (!Utilities.ParseAndroidPkgRevision (pkgRevision, out Version? pkgVer, out string? pkgTag) || pkgVer == null) {
				Log.DebugLine ($"Failed to parse a valid version from Pkg.Revision ({pkgRevision}) for component '{component.Name}'. Component will be reinstalled.");
				return false;
			}

			if (!Utilities.ParseAndroidPkgRevision (component.PkgRevision, out Version? expectedPkgVer, out string? expectedTag) || expectedPkgVer == null)
				throw new InvalidOperationException ($"Invalid expected package version for component '{component.Name}': {component.PkgRevision}");

			bool equal = (pkgVer == expectedPkgVer) && (pkgTag == expectedTag);
			if (!equal)
				Log.DebugLine ($"Installed version of '{component.Name}' ({pkgRevision}) is different than the required one ({component.PkgRevision})");

			return equal;
		}

		bool IsNdk (AndroidToolchainComponent component)
		{
			return component.Name.StartsWith ("android-ndk", StringComparison.OrdinalIgnoreCase);
		}

		static  readonly    XNamespace  AndroidRepositoryCommon     = "http://schemas.android.com/repository/android/common/01";
		static  readonly    XNamespace  AndroidRepositoryGeneric    = "http://schemas.android.com/repository/android/generic/01";

		void WritePackageXmls (string sdkRoot)
		{
			string[] packageXmlDirs = new[]{
				Path.Combine (sdkRoot, "emulator"),
			};
			foreach (var path in packageXmlDirs) {
				var properties = ReadSourceProperties (path);
				if (properties == null)
					continue;
				string packageXml = Path.Combine (path, "package.xml");
				Log.DebugLine ($"Writing '{packageXml}'");
				var doc = new XDocument(
						new XElement (AndroidRepositoryCommon + "repository",
							new XAttribute (XNamespace.Xmlns + "ns2", AndroidRepositoryCommon.NamespaceName),
							new XAttribute (XNamespace.Xmlns + "ns3", AndroidRepositoryGeneric.NamespaceName),
							new XElement ("localPackage",
								new XAttribute ("path", properties ["Pkg.Path"]),
								new XAttribute ("obsolete", "false"),
								new XElement ("revision", GetRevision (properties ["Pkg.Revision"])),
								new XElement ("display-name", properties ["Pkg.Desc"]))));
				doc.Save (packageXml, SaveOptions.None);
			}
		}

		Dictionary<string, string>? ReadSourceProperties (string dir)
		{
			var path = Path.Combine (dir, "source.properties");
			if (!File.Exists (path))
				return null;
			var dict = new Dictionary<string, string> ();
			foreach (var line in File.ReadLines (path)) {
				if (line.Length == 0)
					continue;
				var entry = line.Split (new[]{'='}, 2, StringSplitOptions.None);
				if (entry.Length != 2)
					continue;
				dict.Add (entry [0], entry [1]);
			}
			return dict;
		}

		IEnumerable<XElement> GetRevision (string revision)
		{
			var parts = revision.Split ('.');
			if (parts.Length > 0)
				yield return new XElement ("major", parts [0]);
			if (parts.Length > 1)
				yield return new XElement ("minor", parts [1]);
			if (parts.Length > 2)
				yield return new XElement ("micro", parts [2]);
		}
	}
}
