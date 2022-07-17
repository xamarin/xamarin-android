using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Xamarin.Android.Tools;
using Xamarin.Tools.Zip;
using Microsoft.Android.Build.Tasks;

#if MSBUILD
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
#endif

namespace Xamarin.Android.Tasks
{
	public partial class MonoAndroidHelper
	{
		static Lazy<string> uname = new Lazy<string> (GetOSBinDirName, System.Threading.LazyThreadSafetyMode.PublicationOnly);

		// Set in ResolveSdks.Execute();
		// Requires that ResolveSdks.Execute() run before anything else
		public static AndroidVersions   SupportedVersions;
		public static AndroidSdkInfo    AndroidSdk;

		public static int RunProcess (string name, string args, DataReceivedEventHandler onOutput, DataReceivedEventHandler onError, Dictionary<string, string> environmentVariables = null)
		{
			var psi = new ProcessStartInfo (name, args) {
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
			};
			if (environmentVariables != null) {
				foreach (var pair in environmentVariables) {
					psi.EnvironmentVariables [pair.Key] = pair.Value;
				}
			}
			Process p = new Process ();
			p.StartInfo = psi;

			p.OutputDataReceived += onOutput;
			p.ErrorDataReceived += onError;
			p.Start ();
			p.BeginErrorReadLine ();
			p.BeginOutputReadLine ();
			p.WaitForExit ();
			try {
				return p.ExitCode;
			} finally {
				p.Close ();
			}
		}

		static string GetOSBinDirName ()
		{
			if (OS.IsWindows)
				return "";
			string os = null;
			DataReceivedEventHandler output = (o, e) => {
				if (string.IsNullOrWhiteSpace (e.Data))
					return;
				os = e.Data.Trim ();
			};
			DataReceivedEventHandler error = (o, e) => {};

			string uname = "/usr/bin/uname";
			if (!File.Exists (uname)) {
				uname = "uname";
			}

			int r = RunProcess (uname, "-s", output, error);
			if (r == 0)
				return os;
			return null;
		}

		// Path which contains OS-specific binaries; formerly known as $prefix/bin
		internal static string GetOSBinPath ()
		{
			var toolsDir = Path.GetFullPath (Path.GetDirectoryName (typeof (MonoAndroidHelper).Assembly.Location));
			return Path.Combine (toolsDir, uname.Value);
		}

		internal static string GetOSLibPath ()
		{
			var toolsDir = Path.GetFullPath (Path.GetDirectoryName (typeof (MonoAndroidHelper).Assembly.Location));
			return Path.Combine (toolsDir, "lib", $"host-{uname.Value}");
		}

#if MSBUILD
		public static void RefreshAndroidSdk (string sdkPath, string ndkPath, string javaPath, TaskLoggingHelper logHelper = null)
		{
			Action<TraceLevel, string> logger = (level, value) => {
				var log = logHelper;
				switch (level) {
				case TraceLevel.Error:
					if (log == null)
						Console.Error.Write (value);
					else
						log.LogCodedError ("XA5300", "{0}", value);
					break;
				case TraceLevel.Warning:
					if (log == null)
						Console.WriteLine (value);
					else
						log.LogCodedWarning ("XA5300", "{0}", value);
					break;
				default:
					if (log == null)
						Console.WriteLine (value);
					else
						log.LogDebugMessage ("{0}", value);
					break;
				}
			};
			AndroidSdk  = new AndroidSdkInfo (logger, sdkPath, ndkPath, javaPath);
		}

		public static void RefreshSupportedVersions (string[] referenceAssemblyPaths)
		{
			SupportedVersions   = new AndroidVersions (referenceAssemblyPaths);
		}
#endif  // MSBUILD

		public static JdkInfo GetJdkInfo (Action<TraceLevel, string> logger, string javaSdkPath, Version minSupportedVersion, Version maxSupportedVersion)
		{
			JdkInfo info = null;
			try {
				info = new JdkInfo (javaSdkPath, logger:logger);
			} catch {
				info = JdkInfo.GetKnownSystemJdkInfos (logger)
					.Where (jdk => jdk.Version >= minSupportedVersion && jdk.Version <= maxSupportedVersion)
					.FirstOrDefault ();
			}
			return info;
		}

		class SizeAndContentFileComparer : IEqualityComparer<FileInfo>
#if MSBUILD
			, IEqualityComparer<ITaskItem>
#endif  // MSBUILD
		{
			public  static  readonly  SizeAndContentFileComparer  DefaultComparer     = new SizeAndContentFileComparer ();

			public bool Equals (FileInfo x, FileInfo y)
			{
				if (x.Exists != y.Exists || x.Length != y.Length)
					return false;
				using (var f1 = File.OpenRead (x.FullName)) {
					using (var f2 = File.OpenRead (y.FullName)) {
						var b1 = new byte [0x1000];
						var b2 = new byte [0x1000];
						int total = 0;
						while (total < x.Length) {
							int size = f1.Read (b1, 0, b1.Length);
							total += size;
							f2.Read (b2, 0, b2.Length);
							if (!b1.Take (size).SequenceEqual (b2.Take (size)))
								return false;
						}
					}
				}
				return true;
			}

			public int GetHashCode (FileInfo obj)
			{
				return (int) obj.Length;
			}

#if MSBUILD
			public bool Equals (ITaskItem x, ITaskItem y)
			{
				return Equals (new FileInfo (x.ItemSpec), new FileInfo (y.ItemSpec));
			}

			public int GetHashCode (ITaskItem obj)
			{
				return GetHashCode (new FileInfo (obj.ItemSpec));
			}
#endif  // MSBUILD
		}

		internal static bool LogInternalExceptions {
			get {
				return string.Equals (
						"icanhaz",
						Environment.GetEnvironmentVariable ("__XA_LOG_ERRORS__"),
						StringComparison.OrdinalIgnoreCase);
			}
		}

#if MSBUILD
		public static IEnumerable<string> ExpandFiles (ITaskItem[] libraryProjectJars)
		{
			libraryProjectJars  = libraryProjectJars ?? Array.Empty<ITaskItem> ();
			return (from path in libraryProjectJars
					let     dir     = Path.GetDirectoryName (path.ItemSpec)
					let     pattern = Path.GetFileName (path.ItemSpec)
					where   Directory.Exists (dir)
					select  Directory.GetFiles (dir, pattern))
				.SelectMany (paths => paths);
		}

		public static IEnumerable<ITaskItem> DistinctFilesByContent (IEnumerable<ITaskItem> filePaths)
		{
			return filePaths.Distinct (MonoAndroidHelper.SizeAndContentFileComparer.DefaultComparer);
		}
#endif

		public static IEnumerable<string> DistinctFilesByContent (IEnumerable<string> filePaths)
		{
			return filePaths.Select (p => new FileInfo (p)).ToArray ().Distinct (new MonoAndroidHelper.SizeAndContentFileComparer ()).Select (f => f.FullName).ToArray ();
		}

		public static IEnumerable<string> GetDuplicateFileNames (IEnumerable<string> fullPaths, string [] excluded)
		{
			var files = fullPaths.Select (full => Path.GetFileName (full)).Where (f => excluded == null || !excluded.Contains (f, StringComparer.OrdinalIgnoreCase)).ToArray ();
			for (int i = 0; i < files.Length; i++)
				for (int j = i + 1; j < files.Length; j++)
					if (String.Compare (files [i], files [j], StringComparison.OrdinalIgnoreCase) == 0)
						yield return files [i];
		}

		public static bool IsEmbeddedReferenceJar (string jar)
		{
			return jar.StartsWith ("__reference__", StringComparison.Ordinal);
		}

		public static void LogWarning (object log, string msg, params object [] args)
		{
#if MSBUILD
			var helper = log as TaskLoggingHelper;
			if (helper != null) {
				helper.LogWarning (msg, args);
				return;
			}
			var action = log as Action<string>;
			if (action != null) {
				action (string.Format (msg, args));
				return;
			}
#else
			Console.Error.WriteLine (msg, args);
#endif
		}

#if MSBUILD
		static readonly Dictionary<string, string> ClangAbiMap = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase) {
			{"arm64-v8a",   "aarch64"},
			{"armeabi-v7a", "arm"},
			{"x86",         "i686"},
			{"x86_64",      "x86_64"}
		};

		public static string MapAndroidAbiToClang (string androidAbi)
		{
			if (ClangAbiMap.TryGetValue (androidAbi, out string clangAbi)) {
				return clangAbi;
			}
			return null;
		}
#endif

		public static bool IsMonoAndroidAssembly (ITaskItem assembly)
		{
			var tfi = assembly.GetMetadata ("TargetFrameworkIdentifier");
			if (string.Compare (tfi, "MonoAndroid", StringComparison.OrdinalIgnoreCase) == 0)
				return true;

			var hasReference = assembly.GetMetadata ("HasMonoAndroidReference");
			return bool.TryParse (hasReference, out bool value) && value;
		}

		public static bool HasMonoAndroidReference (ITaskItem assembly)
		{
			// Check item metadata and return early
			if (IsMonoAndroidAssembly (assembly))
				return true;

			using var pe = new PEReader (File.OpenRead (assembly.ItemSpec));
			var reader = pe.GetMetadataReader ();
			return HasMonoAndroidReference (reader);
		}

		public static bool HasMonoAndroidReference (MetadataReader reader)
		{
			foreach (var handle in reader.AssemblyReferences) {
				var reference = reader.GetAssemblyReference (handle);
				var name = reader.GetString (reference.Name);
				if ("Mono.Android" == name) {
					return true;
				}
			}
			return false;
		}

		public static bool IsReferenceAssembly (string assembly)
		{
			using (var stream = File.OpenRead (assembly))
			using (var pe = new PEReader (stream)) {
				var reader = pe.GetMetadataReader ();
				var assemblyDefinition = reader.GetAssemblyDefinition ();
				foreach (var handle in assemblyDefinition.GetCustomAttributes ()) {
					var attribute = reader.GetCustomAttribute (handle);
					var attributeName = reader.GetCustomAttributeFullName (attribute);
					if (attributeName == "System.Runtime.CompilerServices.ReferenceAssemblyAttribute")
						return true;
				}
				return false;
			}
		}

		public static bool IsForceRetainedAssembly (string assembly)
		{
			switch (assembly) {
			case "Mono.Android.Export.dll": // this is totally referenced by reflection.
				return true;
			}
			return false;
		}

		public static bool CopyAssemblyAndSymbols (string source, string destination)
		{
			bool changed = Files.CopyIfChanged (source, destination);
			var mdb = source + ".mdb";
			if (File.Exists (mdb)) {
				var mdbDestination = destination + ".mdb";
				Files.CopyIfChanged (mdb, mdbDestination);
			}
			var pdb = Path.ChangeExtension (source, "pdb");
			if (File.Exists (pdb) && Files.IsPortablePdb (pdb)) {
				var pdbDestination = Path.ChangeExtension (destination, "pdb");
				Files.CopyIfChanged (pdb, pdbDestination);
			}
			return changed;
		}

		public static ZipArchive ReadZipFile (string filename)
		{
			try {
				return Files.ReadZipFile (filename);
			} catch (ZipIOException ex) {
				throw new ZipIOException ($"There was an error opening {filename}. The file is probably corrupt. Try deleting it and building again. {ex.Message}", ex);
			}
		}

#if MSBUILD
		internal static IEnumerable<ITaskItem> GetFrameworkAssembliesToTreatAsUserAssemblies (ITaskItem[] resolvedAssemblies)
		{
			var ret = new List<ITaskItem> ();
			foreach (ITaskItem item in resolvedAssemblies) {
				if (FrameworkAssembliesToTreatAsUserAssemblies.Contains (Path.GetFileName (item.ItemSpec)))
					ret.Add (item);
			}

			return ret;
		}
#endif

		public static Dictionary<string, string> LoadAcwMapFile (string acwPath)
		{
			var acw_map = new Dictionary<string, string> ();
			if (!File.Exists (acwPath))
				return acw_map;
			foreach (var s in File.ReadLines (acwPath)) {
				var items = s.Split (new char[] { ';' }, count: 2);
				if (!acw_map.ContainsKey (items [0]))
					acw_map.Add (items [0], items [1]);
			}
			return acw_map;
		}

		public static Dictionary<string, HashSet<string>> LoadCustomViewMapFile (IBuildEngine4 engine, string mapFile)
		{
			var cachedMap = engine?.GetRegisteredTaskObjectAssemblyLocal<Dictionary<string, HashSet<string>>> (mapFile, RegisteredTaskObjectLifetime.Build);
			if (cachedMap != null)
				return cachedMap;
			var map = new Dictionary<string, HashSet<string>> ();
			if (!File.Exists (mapFile))
				return map;
			foreach (var s in File.ReadLines (mapFile)) {
				var items = s.Split (new char [] { ';' }, count: 2);
				var key = items [0];
				var value = items [1];
				HashSet<string> set;
				if (!map.TryGetValue (key, out set))
					map.Add (key, set = new HashSet<string> ());
				set.Add (value);
			}
			return map;
		}

		public static bool SaveCustomViewMapFile (IBuildEngine4 engine, string mapFile, Dictionary<string, HashSet<string>> map)
		{
			engine?.RegisterTaskObjectAssemblyLocal (mapFile, map, RegisteredTaskObjectLifetime.Build);
			using (var writer = MemoryStreamPool.Shared.CreateStreamWriter ()) {
				foreach (var i in map.OrderBy (x => x.Key)) {
					foreach (var v in i.Value.OrderBy (x => x))
						writer.WriteLine ($"{i.Key};{v}");
				}
				writer.Flush ();
				return Files.CopyIfStreamChanged (writer.BaseStream, mapFile);
			}
		}

		public static string [] GetProguardEnvironmentVaribles (string proguardHome)
		{
			string proguardHomeVariable = "PROGUARD_HOME=" + proguardHome;

			return Environment.OSVersion.Platform == PlatformID.Unix ?
				new string [] { proguardHomeVariable } :
				// Windows seems to need special care, needs JAVA_TOOL_OPTIONS.
				// On the other hand, xbuild has a bug and fails to parse '=' in the value, so we skip JAVA_TOOL_OPTIONS on Mono runtime.
				new string [] { proguardHomeVariable, "JAVA_TOOL_OPTIONS=-Dfile.encoding=UTF8" };
		}

		public static string GetExecutablePath (string dir, string exe)
		{
			if (string.IsNullOrEmpty (dir))
				return exe;
			foreach (var e in Executables (exe))
				if (File.Exists (Path.Combine (dir, e)))
					return e;
			return exe;
		}

		public static IEnumerable<string> Executables (string executable)
		{
			var pathExt = Environment.GetEnvironmentVariable ("PATHEXT");
			var pathExts = pathExt?.Split (new char [] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);

			if (pathExts != null) {
				foreach (var ext in pathExts)
					yield return Path.ChangeExtension (executable, ext);
			}
			yield return executable;
		}

		public static string TryGetAndroidJarPath (TaskLoggingHelper log, string platform)
		{
			var platformPath = MonoAndroidHelper.AndroidSdk.TryGetPlatformDirectoryFromApiLevel (platform, MonoAndroidHelper.SupportedVersions);
			if (platformPath == null) {
				var expectedPath = MonoAndroidHelper.AndroidSdk.GetPlatformDirectoryFromId (platform);
				var sdkManagerMenuPath = OS.IsWindows ? Properties.Resources.XA5207_SDK_Manager_Windows : Properties.Resources.XA5207_SDK_Manager_macOS;
				log.LogCodedError ("XA5207", Properties.Resources.XA5207, platform, Path.Combine (expectedPath, "android.jar"), sdkManagerMenuPath);
				return null;
			}
			return Path.Combine (platformPath, "android.jar");
		}

		static readonly string ResourceCaseMapKey = $"{nameof (MonoAndroidHelper)}_ResourceCaseMap";

		public static void SaveResourceCaseMap (IBuildEngine4 engine, Dictionary<string, string> map) =>
			engine.RegisterTaskObjectAssemblyLocal (ResourceCaseMapKey, map, RegisteredTaskObjectLifetime.Build);

		public static Dictionary<string, string> LoadResourceCaseMap (IBuildEngine4 engine) =>
			engine.GetRegisteredTaskObjectAssemblyLocal<Dictionary<string, string>> (ResourceCaseMapKey, RegisteredTaskObjectLifetime.Build) ?? new Dictionary<string, string> (0);

		public static string FixUpAndroidResourcePath (string file, string resourceDirectory, string resourceDirectoryFullPath, Dictionary<string, string> resource_name_case_map)
		{
			string newfile = null;
			if (file.StartsWith (resourceDirectory, StringComparison.InvariantCultureIgnoreCase)) {
				newfile = file.Substring (resourceDirectory.Length).TrimStart (Path.DirectorySeparatorChar);
			}
			if (!string.IsNullOrEmpty (resourceDirectoryFullPath) && file.StartsWith (resourceDirectoryFullPath, StringComparison.InvariantCultureIgnoreCase)) {
				newfile = file.Substring (resourceDirectoryFullPath.Length).TrimStart (Path.DirectorySeparatorChar);
			}
			if (!string.IsNullOrEmpty (newfile)) {
				if (resource_name_case_map.TryGetValue (newfile, out string value))
					newfile = value;
				newfile = Path.Combine ("Resources", newfile);
				return newfile;
			}
			return string.Empty;
		}

		static readonly char [] DirectorySeparators = new [] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

		/// <summary>
		/// Returns the relative path that should be used for an @(AndroidAsset) item
		/// </summary>
		public static string GetRelativePathForAndroidAsset (string assetsDirectory, ITaskItem androidAsset)
		{
			var path = androidAsset.GetMetadata ("Link");
			path = !string.IsNullOrWhiteSpace (path) ? path : androidAsset.ItemSpec;
			var head = string.Join ("\\", path.Split (DirectorySeparators).TakeWhile (s => !s.Equals (assetsDirectory, StringComparison.OrdinalIgnoreCase)));
			path = head.Length == path.Length ? path : path.Substring ((head.Length == 0 ? 0 : head.Length + 1) + assetsDirectory.Length).TrimStart (DirectorySeparators);
			return path;
		}

		/// <summary>
		/// Returns the appropriate path for dotnet, pass in $(NetCoreRoot)
		/// </summary>
		public static string FindDotnet (string netCoreRoot)
		{
			if (Directory.Exists (netCoreRoot)) {
				var dotnetPath = Path.Combine (netCoreRoot, (RuntimeInformation.IsOSPlatform (OSPlatform.Windows) ? "dotnet.exe" : "dotnet"));
				if (File.Exists (dotnetPath))
					return dotnetPath;
			}

			var dotnetHostPath = Environment.GetEnvironmentVariable ("DOTNET_HOST_PATH");
			if (File.Exists (dotnetHostPath))
				return dotnetHostPath;

			return "dotnet";
		}
	}
}
