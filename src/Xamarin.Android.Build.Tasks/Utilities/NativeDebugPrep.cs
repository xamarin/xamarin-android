using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Utilities;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	class NativeDebugPrep
	{
		const string ConfigScriptName = "debug-app-config";
		const string LldbScriptName = "lldb.x";

		static HashSet<string> xaLibraries = new HashSet<string> (StringComparer.Ordinal) {
			"libmonodroid.so",
			"libxamarin-app.so",
			"libxamarin-debug-app-helper.so",
		};

		TaskLoggingHelper log;

		public NativeDebugPrep (TaskLoggingHelper logger)
		{
			log = logger;
		}

		[DllImport ("c", SetLastError=true, EntryPoint="chmod")]
		private static extern int chmod (string path, uint mode);

		public void Prepare (string adbPath, string ndkRootPath, string activityName,
		                     string outputDirRoot, string packageName, string[] supportedAbis,
		                     Dictionary<string, List<string>> nativeLibsPerAbi, string? targetDevice)
		{
			bool isWindows = OS.IsWindows;
			string scriptResourceName = isWindows ? "debug-app.ps1" : "debug-app.sh";
			string? script = MonoAndroidHelper.ReadManifestResource (log, scriptResourceName);

			if (String.IsNullOrEmpty (script)) {
				log.LogError ($"Failed to read script resource '{scriptResourceName}'");
				return;
			}

			string? appLibrariesRoot = CopyLibraries (outputDirRoot, nativeLibsPerAbi);
			string scriptConfigExt = isWindows ? ".ps1" : ".sh";
			string scriptOutput = Path.Combine (outputDirRoot, scriptResourceName);

			var sb = new StringBuilder (script);

			// TODO: perhaps use relative paths for APP_LIBS_DIR and OUTPUT_DIR?
			sb.Replace ("@ACTIVITY_NAME@", activityName);
			sb.Replace ("@ADB_PATH@", adbPath);
			sb.Replace ("@APP_LIBS_DIR@", appLibrariesRoot ?? String.Empty);
			sb.Replace ("@CONFIG_SCRIPT_NAME@", $"{ConfigScriptName}{scriptConfigExt}");
			sb.Replace ("@DEBUG_SESSION_PREP_PATH@", Path.Combine (Path.GetDirectoryName (typeof(NativeDebugPrep).Assembly.Location), "debug-session-prep.dll"));
			sb.Replace ("@LLDB_SCRIPT_NAME@", LldbScriptName);
			sb.Replace ("@NDK_DIR@", Path.GetFullPath (ndkRootPath));
			sb.Replace ("@OUTPUT_DIR@", Path.GetFullPath (outputDirRoot));
			sb.Replace ("@PACKAGE_NAME@", packageName);

			var abis = new StringBuilder ();
			bool first = true;
			foreach (string abi in supportedAbis) {
				if (first) {
					first = false;
				} else {
					abis.Append (isWindows ? ", " : " ");
				}
				abis.Append ($"\"{abi}\"");
			}
			sb.Replace ("@SUPPORTED_ABIS@", abis.ToString ());

			Directory.CreateDirectory (Path.GetDirectoryName (scriptOutput));

			using var fs = File.Open (scriptOutput, FileMode.Create, FileAccess.Write, FileShare.Read);
			using var sw = new StreamWriter (fs, Files.UTF8withoutBOM);

			sw.Write (sb.ToString ());
			sw.Flush ();
			sw.Close ();
			fs.Close ();

			// 493 decimal is 0755 octal - makes the file rwx for the owner and rx for everybody else
			if (!isWindows && chmod (scriptOutput, 493) != 0) {
				log.LogWarning ($"Failed to make {scriptOutput} executable");
			}

			// TODO: color?
			Console.WriteLine ();
			Console.WriteLine ("You can start the debugging session by running the following command now:");
			Console.WriteLine ($"  {scriptOutput}");
			Console.WriteLine ();
		}

		string? CopyLibraries (string outputDirRoot, Dictionary<string, List<string>> nativeLibsPerAbi)
		{
			if (nativeLibsPerAbi.Count == 0) {
				return null;
			}

			string appLibsRoot = Path.Combine (outputDirRoot, "lldb", "lib");
			log.LogDebugMessage ($"Copying application native libararies to {appLibsRoot}");

			DotnetSymbolRunner? dotnetSymbol = GetDotnetSymbolRunner ();
			bool haveLibsWithoutSymbols = false;
			foreach (var kvp in nativeLibsPerAbi) {
				string abi = kvp.Key;
				List<string> libs = kvp.Value;

				string abiDir = Path.Combine (appLibsRoot, abi);
				foreach (string library in libs) {
					log.LogDebugMessage ($"  [{abi}] {library}");

					string fileName = Path.GetFileName (library);
					if (fileName.StartsWith ("libmono-android.")) {
						fileName = "libmonodroid.so";
					}

					string destPath = Path.Combine (appLibsRoot, abi, fileName);
					Directory.CreateDirectory (Path.GetDirectoryName (destPath));
					File.Copy (library, destPath, true);

					if (!EnsureSharedLibraryHasSymboles (destPath, dotnetSymbol)) {
						haveLibsWithoutSymbols = true;
					}
				}
			}

			if (haveLibsWithoutSymbols) {
				log.LogWarning ($"One or more native libraries have no debug symbols.");
				if (dotnetSymbol == null) {
					log.LogWarning ($"The dotnet-symbol tool was not found. It can be installed using: dotnet tool install -g dotnet-symbol");
				}
			}

			return Path.GetFullPath (appLibsRoot);
		}

		bool EnsureSharedLibraryHasSymboles (string libraryPath, DotnetSymbolRunner? dotnetSymbol)
		{
			bool tryToFetchSymbols = false;
			bool hasSymbols = ELFHelper.HasDebugSymbols (log, libraryPath, out bool usesDebugLink);
			string libName = Path.GetFileName (libraryPath);

			if (!xaLibraries.Contains (libName)) {
				if (ELFHelper.IsAOTLibrary (log, libraryPath)) {
					return true; // We don't care about symbols, AOT libraries are only data
				}

				// It might be a framework shared library, we'll try to fetch symbols if necessary and possible
				tryToFetchSymbols = !hasSymbols && usesDebugLink;
			}

			if (tryToFetchSymbols && dotnetSymbol != null) {
				log.LogMessage ($"Attempting to download debug symbols from symbol server");
				if (!dotnetSymbol.Fetch (libraryPath).Result) {
					log.LogWarning ($"Failed to download debug symbols for {libraryPath}");
				}
			}

			hasSymbols = ELFHelper.HasDebugSymbols (log, libraryPath);
			return hasSymbols;
		}

		DotnetSymbolRunner? GetDotnetSymbolRunner ()
		{
			string dotnetSymbolPath = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.UserProfile), ".dotnet", "tools", "dotnet-symbol");
			if (OS.IsWindows) {
				dotnetSymbolPath = $"{dotnetSymbolPath}.exe";
			}

			if (!File.Exists (dotnetSymbolPath)) {
				return null;
			}

			return new DotnetSymbolRunner (log, dotnetSymbolPath);
		}
	}
}
