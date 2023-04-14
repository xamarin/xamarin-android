using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Utilities;
using Xamarin.Android.Tools;

using TPL = System.Threading.Tasks;

namespace Xamarin.Android.Tasks
{
	// Starting LLDB server: /data/data/net.twistedcode.myapplication/lldb/bin/start_lldb_server.sh /data/data/net.twistedcode.myapplication/lldb unix-abstract /net.twistedcode.myapplication-0 platform-1668626161269.sock "lldb process:gdb-remote packets"

	/// <summary>
	/// Interface to lldb, the NDK native code debugger.
	/// </summary>
	class NativeDebugger
	{
		const ConsoleColor ErrorColor   = ConsoleColor.Red;
        const ConsoleColor DebugColor   = ConsoleColor.DarkGray;
        const ConsoleColor InfoColor    = ConsoleColor.Green;
        const ConsoleColor MessageColor = ConsoleColor.Gray;
        const ConsoleColor WarningColor = ConsoleColor.Yellow;
		const ConsoleColor StatusLabel  = ConsoleColor.Cyan;
		const ConsoleColor StatusText   = ConsoleColor.White;

		const string ServerLauncherScriptName = "xa_start_lldb_server.sh";

		enum LogLevel
		{
			Error,
			Warning,
			Info,
			Message,
			Debug
		}

		sealed class Context
		{
			public AdbRunner adb;
			public int apiLevel;
			public string abi;
			public string arch;
			public bool appIs64Bit;
			public string appDataDir;
			public string debugServerPath;
			public string debugServerScriptPath;
			public string debugSocketPath;
			public string outputDir;
			public string appLibrariesDir;
			public string appLldbBaseDir;
			public string appLldbBinDir;
			public string appLldbLogDir;
			public uint applicationPID;
			public string lldbScriptPath;
			public string zygotePath;
			public int debugServerPort;
			public string domainSocketDir;
			public string platformSocketName;
			public List<string>? nativeLibraries;
			public List<string> deviceBinaryDirs;
		}

		// We want the shell/batch scripts first, since they set up Python environment for the debugger
		static readonly string[] lldbNames = {
			"lldb.sh",
			"lldb",
			"lldb.cmd",
			"lldb.exe",
		};

		static readonly string[] abiProperties = {
			// new properties
			"ro.product.cpu.abilist",

			// old properties
			"ro.product.cpu.abi",
			"ro.product.cpu.abi2",
		};

		static readonly string[] deviceLibraries = {
			"libc.so",
			"libm.so",
			"libdl.so",
		};

		static HashSet<string> xaLibraries = new HashSet<string> (StringComparer.Ordinal) {
			"libmonodroid.so",
			"libxamarin-app.so",
			"libxamarin-debug-app-helper.so",
		};

		static readonly object consoleLock = new object ();
		static readonly UTF8Encoding UTF8NoBOM = new UTF8Encoding (false);

		TaskLoggingHelper log;
		string packageName;
		string lldbPath;
		string adbPath;
		string outputDir;
		Dictionary<string, string> hostLldbServerPaths;
		string[] supportedAbis;

		public string? AdbDeviceTarget { get; set; }
		public bool UseLldbGUI { get; set; } = true;
		public string? CustomLldbCommandsFilePath { get; set; }

		public IDictionary<string, List<string>>? NativeLibrariesPerABI { get; set; }

		public NativeDebugger (TaskLoggingHelper logger, string adbPath, string ndkRootPath, string outputDirRoot, string packageName, string[] supportedAbis)
		{
			this.log = logger;
			this.packageName = packageName;
			this.adbPath = adbPath;
			this.supportedAbis = supportedAbis;
			outputDir = Path.Combine (outputDirRoot, "native-debug");

			if (!FindTools (ndkRootPath, supportedAbis)) {
				throw new InvalidOperationException ("Failed to find all the required tools and utilities");
			}
		}

		/// <summary>
		/// Detect PID of the running application and attach the debugger to it
		/// </summary>
		public bool Attach ()
		{
			Context? context = Init ();

			return context != null;
		}

		/// <summary>
		/// Launch the application under control of the debugger.
		/// </summary>
		public bool Launch (string activityName)
		{
			if (String.IsNullOrEmpty (activityName)) {
				throw new ArgumentException ("must not be null or empty", nameof (activityName));
			}

			Context? context = Init ();
			if (context == null) {
				return false;
			}

			// Start the app, tell it to wait for debugger to attach and to kill any running instance
			// We tell `am` to wait ('-W') for the app to start, so that `pidof` then can find the process
			string launchName = $"{packageName}/{activityName}";
			LogLine ();
			LogStatusLine ("Launching activity", launchName);
			LogLine ("Waiting for the activity to start...");
			(bool success, string output) = context.adb.Shell ("am", "start", "-S", "-W", launchName).Result;
			if (!success) {
				LogErrorLine ("Failed to launch the activity");
				return false;
			}

			long appPID = GetDeviceProcessID (context, packageName);
			if (appPID <= 0) {
				LogErrorLine ("Failed to obtain PID of the running application");
				LogErrorLine (output);
				return false;
			}
			context.applicationPID = (uint)appPID;

			LogStatusLine ("Application PID", $"{context.applicationPID}");

			StartDebugServer (context);

			// GenerateLldbScript (context);

			// LogDebugLine ($"Starting LLDB: {lldbPath}");
			// var lldb = new LldbRunner (log, lldbPath, context.lldbScriptPath);
			// if (lldb.Run ()) {
			// 	LogWarning ("LLDB failed?");
			// }

			// KillDebugServer (context);
			// LogDebugLine ("Waiting on the debug server process to quit");
			// (success, output) = debugServerTask.Result;

			return true;
		}

		void GenerateLldbScript (Context context)
		{
			context.lldbScriptPath = Path.Combine (context.outputDir, $"{context.arch}-lldb-script.txt");

			using (var f = File.OpenWrite (context.lldbScriptPath)) {
				using (var sw = new StreamWriter (f, UTF8NoBOM)) {
					string systemPaths = String.Join (" ", context.deviceBinaryDirs.Select (d => $"'{Path.GetFullPath(d)}'" ));
					sw.WriteLine ($"settings append target.exec-search-paths '{Path.GetFullPath (context.appLibrariesDir)}' {systemPaths}");
					sw.WriteLine ($"target create '{Path.GetFullPath (context.zygotePath)}'");
					sw.WriteLine ($"target modules search-paths add / '{Path.GetFullPath (outputDir)}/'");
					sw.WriteLine ($"gdb-remote {context.debugServerPort}");

					if (UseLldbGUI) {
						sw.WriteLine ($"gui");
					}

					if (!String.IsNullOrEmpty (CustomLldbCommandsFilePath)) {
						sw.WriteLine ();
						sw.Write (File.ReadAllText (CustomLldbCommandsFilePath));
						sw.WriteLine ();
					}

					sw.Flush ();
				}
			}
		}

		bool KillDebugServer (Context context)
		{
			long serverPID = GetDeviceProcessID (context, context.debugServerPath, quiet: true);
			if (serverPID <= 0) {
				return true;
			}

			LogDebugLine ("Killing previous instance of the debug server");
			(bool success, string _) = context.adb.RunAs (packageName, "kill", "-9", $"{serverPID}").Result;
			return success;
		}

		(AdbRunner? runner, TPL.Task<(bool success, string output)>? task) StartDebugServer (Context context)
		{
			LogDebugLine ($"Starting debug server on device: {context.debugServerScriptPath}");

			if (!KillDebugServer (context)) {
				LogWarningLine ("Failed to kill previous instance of the debug server");
			}

			context.domainSocketDir = $"/xa-{packageName}-0";

			var rnd = new Random ();
			context.platformSocketName = $"xa-platform-{rnd.Next ()}.sock";

			var args = new List<string> {
				"shell",
				"run-as",
				packageName,
				context.debugServerScriptPath,
				context.appLldbBaseDir, // LLDB directory
				"unix-abstract", // Listener socket scheme (unix-abstract: virtual, not on the filesystem)
				context.domainSocketDir, // Directory where listener socket will be created
				context.platformSocketName, // name of the socket to create
				"'\"lldb process:gdb-remote packets\"'", // LLDB log channels
				context.arch
			};

			string command = String.Join (" ", args);
			LogDebugLine ($"Launch command: adb {command}");

			var runner = CreateAdbRunner ();
			runner.ProcessTimeout = TimeSpan.MaxValue;

			// TPL.Task<(bool success, string output)> task = runner.RunAs (
			// 	packageName,
			// 	context.debugServerScriptPath,
			// 	context.appLldbBaseDir, // LLDB directory
			// 	"unix-abstract", // Listener socket scheme (unix-abstract: virtual, not on the filesystem)
			// 	context.domainSocketDir, // Directory where listener socket will be created
			// 	context.platformSocketName, // name of the socket to create
			// 	"'\"lldb process:gdb-remote packets\"'", // LLDB log channels
			// 	context.arch // LLDB architecture
			// );

			return (runner, null);
		}

		long GetDeviceProcessID (Context context, string processName, bool quiet = false)
		{
			(bool success, string output) = context.adb.Shell ("pidof", processName).Result;
			if (!success) {
				if (!quiet) {
					LogErrorLine ($"Failed to obtain PID of process '{processName}'");
					LogErrorLine (output);
				}
				return -1;
			}

			output = output.Trim ();
			if (!UInt32.TryParse (output, out uint pid)) {
				if (!quiet) {
					LogErrorLine ($"Unable to parse string '{output}' as the package's PID");
				}
				return -1;
			}

			return pid;
		}

		AdbRunner CreateAdbRunner () => new AdbRunner (log, adbPath, AdbDeviceTarget);

		Context? Init ()
		{
			LogLine ();

			var context = new Context {
				adb = CreateAdbRunner ()
			};

			(bool success, string output) = context.adb.GetPropertyValue ("ro.build.version.sdk").Result;
			if (!success || String.IsNullOrEmpty (output) || !Int32.TryParse (output, out int apiLevel)) {
				LogErrorLine ("Unable to determine connected device's API level");
				return null;
			}
			context.apiLevel = apiLevel;

			// Warn on old Pixel C firmware (b/29381985). Newer devices may have Yama
			// enabled but still work with ndk-gdb (b/19277529).
			(success, output) = context.adb.Shell ("cat", "/proc/sys/kernel/yama/ptrace_scope", "2>/dev/null").Result;
			if (success &&
			    YamaOK (output.Trim ()) &&
			    PropertyIsEqualTo (context.adb.GetPropertyValue ("ro.build.product").Result, "dragon") &&
			    PropertyIsEqualTo (context.adb.GetPropertyValue ("ro.product.name").Result, "ryu")
			) {
				LogWarningLine ("WARNING: The device uses Yama ptrace_scope to restrict debugging. ndk-gdb will");
				LogWarningLine ("    likely be unable to attach to a process. With root access, the restriction");
				LogWarningLine ("    can be lifted by writing 0 to /proc/sys/kernel/yama/ptrace_scope. Consider");
				LogWarningLine ("    upgrading your Pixel C to MXC89L or newer, where Yama is disabled.");
				LogLine ();
			}

			if (!DetermineArchitectureAndABI (context)) {
				return null;
			}

			if (!DetermineAppDataDirectory (context)) {
				return null;
			}

			if (!PushDebugServer (context)) {
				return null;
			}

			if (!CopyLibraries (context)) {
				return null;
			}

			context.debugSocketPath = $"{context.appDataDir}/debug_socket";

			return context;

			bool YamaOK (string output)
			{
				return !String.IsNullOrEmpty (output) && String.Compare ("0", output, StringComparison.Ordinal) != 0;
			}

			bool PropertyIsEqualTo ((bool haveProperty, string value) result, string expected)
			{
				return
					result.haveProperty &&
					!String.IsNullOrEmpty (result.value) &&
					String.Compare (result.value, expected, StringComparison.Ordinal) == 0;
			}
		}

		bool EnsureSharedLibraryHasSymboles (string libraryPath, DotnetSymbolRunner? dotnetSymbol)
		{
			bool tryToFetchSymbols = false;
			bool hasSymbols = ELFHelper.HasDebugSymbols (log, libraryPath, out bool usesDebugLink);
			string libName = Path.GetFileName (libraryPath);

			if (!xaLibraries.Contains (libName)) {
				if (ELFHelper.IsAOTLibrary (log, libraryPath)) {
					return true; // We don't are about symbols, AOT libraries are only data
				}

				// It might be a framework shared library, we'll try to fetch symbols if necessary and possible
				tryToFetchSymbols = !hasSymbols && usesDebugLink;
			}

			if (tryToFetchSymbols && dotnetSymbol != null) {
				LogInfoLine ($"      Attempting to download debug symbols from symbol server");
				if (!dotnetSymbol.Fetch (libraryPath).Result) {
					LogWarningLine ($"      Warning: failed to download debug symbols for {libraryPath}");
				} else {
					LogLine ();
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

		bool CopyLibraries (Context context)
		{
			LogInfoLine ("Populating local native library cache");
			context.appLibrariesDir = Path.Combine (context.outputDir, "app", "lib");
			if (!Directory.Exists (context.appLibrariesDir)) {
				Directory.CreateDirectory (context.appLibrariesDir);
			}

			if (context.nativeLibraries != null) {
				LogInfoLine ("  Copying application native libraries");
				bool haveLibsWithoutSymbols = false;
				DotnetSymbolRunner? dotnetSymbol = GetDotnetSymbolRunner ();

				foreach (string library in context.nativeLibraries) {
					LogLine ($"    {library}");

					string fileName = Path.GetFileName (library);
					if (fileName.StartsWith ("libmono-android.")) {
						fileName = "libmonodroid.so";
					}
					string destPath = Path.Combine (context.appLibrariesDir, fileName);
					File.Copy (library, destPath, true);

					if (!EnsureSharedLibraryHasSymboles (destPath, dotnetSymbol)) {
						haveLibsWithoutSymbols = true;
					}
				}

				if (haveLibsWithoutSymbols) {
					LogWarningLine ($"One or more native libraries have no debug symbols.");
					if (dotnetSymbol == null) {
						LogWarningLine ($"The dotnet-symbol tool was not found. It can be installed using: dotnet tool install -g dotnet-symbol");
					}
				}
			}

			var requiredFiles = new List<string> ();
			var libraries = new List<string> ();
			string libraryPath;

			if (context.appIs64Bit) {
				libraryPath = "/system/lib64";

				string zygotePath = "/system/bin/app_process64";
				requiredFiles.Add (zygotePath);
				context.zygotePath = $"{context.outputDir}{ToLocalPathFormat (zygotePath)}";
				requiredFiles.Add ("/system/bin/linker64");
			} else {
				libraryPath = "/system/lib";
				requiredFiles.Add ("/system/bin/linker");
			}

			foreach (string lib in deviceLibraries) {
				requiredFiles.Add ($"{libraryPath}/{lib}");
			}

			LogLine ();
			LogInfoLine ("  Copying binaries from device");
			var dirs = new HashSet<string> (StringComparer.Ordinal);

			foreach (string file in requiredFiles) {
				string filePath = ToLocalPathFormat (file);
				string localPath = $"{context.outputDir}{filePath}";
				string localDir = Path.GetDirectoryName (localPath);

				if (!Directory.Exists (localDir)) {
					Directory.CreateDirectory (localDir);
					if (!dirs.Contains (localDir)) {
						dirs.Add (localDir);
					}
				}

				Log ($"    From '{file}' to '{localPath}' ");
				if (!context.adb.Pull (file, localPath).Result) {
					LogLine ("[FAILED]");
				} else {
					LogLine ("[SUCCESS]");
				}
			}

			context.deviceBinaryDirs = new List<string> (dirs);
			if (context.appIs64Bit) {
				return true;
			}

			// /system/bin/app_process is 32-bit on 32-bit devices, but a symlink to
			// # app_process64 on 64-bit. If we need the 32-bit version, try to pull
			// # app_process32, and if that fails, pull app_process.
			string destination = $"{context.outputDir}{ToLocalPathFormat ("/system/bin/app_process")}";
			string? source = "/system/bin/app_process32";

			if (!context.adb.Pull (source, destination).Result) {
				source = "/system/bin/app_process";
				if (!context.adb.Pull (source, destination).Result) {
					source = null;
				}
			}

			if (String.IsNullOrEmpty (source)) {
				LogErrorLine ("Failed to copy 32-bit app_process");
				return false;
			}
			LogLine ($"    From '{source}' to '{destination}' ");
			context.zygotePath = destination;

			return true;

			string ToLocalPathFormat (string path) => OS.IsWindows ? path.Replace ("/", "\\") : path;
		}

		bool PushDebugServer (Context context)
		{
			if (!hostLldbServerPaths.TryGetValue (context.abi, out string debugServerPath)) {
				LogErrorLine ($"Debug server for abi '{context.abi}' not found.");
				return false;
			}

			if (!context.adb.CreateDirectoryAs (packageName, context.appLldbBinDir).Result.success) {
				LogErrorLine ($"Failed to create debug server destination directory on device, {context.appLldbBinDir}");
				return false;
			}

			//string serverName = $"xa-{context.arch}-{Path.GetFileName (debugServerPath)}";
			string serverName = Path.GetFileName (debugServerPath);
			context.debugServerPath = $"{context.appLldbBinDir}/{serverName}";

			KillDebugServer (context);

			// Always push the server binary, as we don't know what version might already be there
			if (!PushServerExecutable (context, debugServerPath, context.debugServerPath)) {
				return false;
			}
			LogStatusLine ("Debug server path on device", context.debugServerPath);

			string? launcherScript = MonoAndroidHelper.ReadManifestResource (log, ServerLauncherScriptName);
			if (String.IsNullOrEmpty (launcherScript)) {
				return false;
			}

			string launcherScriptPath = Path.Combine (context.outputDir, ServerLauncherScriptName);
			Directory.CreateDirectory (Path.GetDirectoryName (launcherScriptPath));
			File.WriteAllText (launcherScriptPath, launcherScript, UTF8NoBOM);

			context.debugServerScriptPath = $"{context.appLldbBinDir}/{Path.GetFileName (launcherScriptPath)}";
			if (!PushServerExecutable (context, launcherScriptPath, context.debugServerScriptPath)) {
				return false;
			}
			LogStatusLine ("Debug server launcher script path on device", context.debugServerScriptPath);
			LogLine ();

			return true;
		}

		bool PushServerExecutable (Context context, string hostSource, string deviceDestination)
		{
			string executableName = Path.GetFileName (deviceDestination);

			// Always push the executable, as we don't know what version might already be there
			LogDebugLine ($"Uploading {hostSource} to device");

			// First upload to temporary path, as it's writable for everyone
			string remotePath = $"/data/local/tmp/{executableName}";
			if (!context.adb.Push (hostSource, remotePath).Result) {
				LogErrorLine ($"Failed to upload debug server {hostSource} to device path {remotePath}");
				return false;
			}

			// Next, copy it to the app dir, with run-as
			(bool success, string output) = context.adb.Shell (
				"cat", remotePath, "|",
				"run-as", packageName,
				"sh", "-c", $"'cat > {deviceDestination}'"
			).Result;

			if (!success) {
				LogErrorLine ($"Failed to copy debug executable to device, from {hostSource} to {deviceDestination}");
				return false;
			}

			(success, output) = context.adb.RunAs (packageName, "chmod", "700", deviceDestination).Result;
			if (!success) {
				LogErrorLine ($"Failed to make debug server executable on device, at {deviceDestination}");
				return false;
			}

			return true;
		}

		bool DetermineAppDataDirectory (Context context)
		{
			(bool success, string output) = context.adb.GetAppDataDirectory (packageName).Result;
			if (!success) {
				LogErrorLine ($"Unable to determine data directory for package '{packageName}'");
				return false;
			}

			context.appDataDir = output.Trim ();
			LogStatusLine ($"Application data directory on device", context.appDataDir);
			LogLine ();

			context.appLldbBaseDir = $"{context.appDataDir}/lldb";
			context.appLldbBinDir = $"{context.appLldbBaseDir}/bin";
			context.appLldbLogDir = $"{context.appLldbBaseDir}/log";

			// Applications with minSdkVersion >= 24 will have their data directories
			// created with rwx------ permissions, preventing adbd from forwarding to
			// the gdbserver socket. To be safe, if we're on a device >= 24, always
			// chmod the directory.
			if (context.apiLevel >= 24) {
				(success, output) = context.adb.RunAs (packageName, "/system/bin/chmod", "a+x", context.appDataDir).Result;
				if (!success) {
					LogErrorLine ("Failed to make application data directory world executable");
					return false;
				}
			}

			return true;
		}

		bool DetermineArchitectureAndABI (Context context)
		{
			string[]? deviceABIs = null;

			foreach (string prop in abiProperties) {
				(bool success, string value) = context.adb.GetPropertyValue (prop).Result;
				if (!success) {
					continue;
				}

				deviceABIs = value.Split (',');
				break;
			}

			if (deviceABIs == null || deviceABIs.Length == 0) {
				LogErrorLine ("Unable to determine device ABI");
				return false;
			}

			LogABIs ("Application", supportedAbis);
			LogABIs ("     Device", deviceABIs);

			foreach (string deviceABI in deviceABIs) {
				foreach (string appABI in supportedAbis) {
					if (String.Compare (appABI, deviceABI, StringComparison.OrdinalIgnoreCase) == 0) {
						context.abi = deviceABI;
						context.arch = context.abi switch {
							"armeabi" => "arm",
							"armeabi-v7a" => "arm",
							"arm64-v8a" => "arm64",
							_ => context.abi,
						};

						LogStatusLine ($"    Selected ABI", $"{context.abi} (architecture: {context.arch})");

						context.appIs64Bit = context.abi.IndexOf ("64", StringComparison.Ordinal) >= 0;
						context.outputDir = Path.Combine (outputDir, context.abi);
						if (NativeLibrariesPerABI != null && NativeLibrariesPerABI.TryGetValue (context.abi, out List<string> abiLibraries)) {
							context.nativeLibraries = abiLibraries;
						}
						return true;
					}
				}
			}

			LogErrorLine ($"Application cannot run on the selected device: no matching ABI found");
			return false;

			void LogABIs (string which, string[] abis)
			{
				LogStatusLine ($"{which} ABIs", String.Join (", ", abis));
			}
		}

		string GetLlvmVersion (string toolchainDir)
		{
			string path = Path.Combine (toolchainDir, "AndroidVersion.txt");
			if (!File.Exists (path)) {
				throw new InvalidOperationException ($"LLVM version file not found at '{path}'");
			}

			string[] lines = File.ReadAllLines (path);
			string? line = lines.Length >= 1 ? lines[0].Trim () : null;
			if (String.IsNullOrEmpty (line)) {
				throw new InvalidOperationException ($"Unable to read LLVM version from '{path}'");
			}

			return line;
		}

		bool FindTools (string ndkRootPath, string[] supportedAbis)
		{
			string toolchainDir = Path.Combine (ndkRootPath, NdkHelper.RelativeToolchainDir);
			string toolchainBinDir = Path.Combine (toolchainDir, "bin");
			string? path = null;

			foreach (string lldb in lldbNames) {
				path = Path.Combine (toolchainBinDir, lldb);
				if (File.Exists (path)) {
					break;
				}
			}

			if (String.IsNullOrEmpty (path)) {
				LogErrorLine ($"Unable to locate lldb executable in '{toolchainBinDir}'");
				return false;
			}
			lldbPath = path;

			hostLldbServerPaths = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);
			string llvmVersion = GetLlvmVersion (toolchainDir);
			foreach (string abi in supportedAbis) {
				string llvmAbi = NdkHelper.TranslateAbiToLLVM (abi);
				path = Path.Combine (ndkRootPath, NdkHelper.RelativeToolchainDir, "lib64", "clang", llvmVersion, "lib", "linux", llvmAbi, "lldb-server");
				if (!File.Exists (path)) {
					LogErrorLine ($"LLVM lldb server component for ABI '{abi}' not found at '{path}'");
					return false;
				}

				hostLldbServerPaths.Add (abi, path);
			}

			if (hostLldbServerPaths.Count == 0) {
				LogErrorLine ("Unable to find any lldb-server executables, debugging not possible");
				return false;
			}

			return true;
		}

		void Log (string? message)
		{
			Log (LogLevel.Message, message);
		}

		void LogLine (string? message = null)
		{
			Log ($"{message}{Environment.NewLine}");
		}

		void LogWarning (string? message)
		{
			Log (LogLevel.Warning, message);
		}

		void LogWarningLine (string? message)
		{
			LogWarning ($"{message}{Environment.NewLine}");
		}

		void LogError (string? message)
		{
			Log (LogLevel.Error, message);
		}

		void LogErrorLine (string? message)
		{
			LogError ($"{message}{Environment.NewLine}");
		}

		void LogInfo (string? message)
		{
			Log (LogLevel.Info, message);
		}

		void LogInfoLine (string? message)
		{
			LogInfo ($"{message}{Environment.NewLine}");
		}

		void LogDebug (string? message)
		{
			Log (LogLevel.Debug, message);
		}

		void LogDebugLine (string? message)
		{
			LogDebug ($"{message}{Environment.NewLine}");
		}

		void LogStatusLine (string label, string text)
		{
			Log (LogLevel.Info, $"{label}: ", StatusLabel);
			Log (LogLevel.Info, $"{text}{Environment.NewLine}", StatusText);
		}

		void Log (LogLevel level, string? message)
		{
			Log (level, message, ForegroundColor (level));
		}

		void Log (LogLevel level, string? message, ConsoleColor color)
		{
			TextWriter writer = level == LogLevel.Error ? Console.Error : Console.Out;
			message = message ?? String.Empty;

			ConsoleColor fg = ConsoleColor.Gray;
			try {
				lock (consoleLock) {
					fg = Console.ForegroundColor;
					Console.ForegroundColor = color;
				}

				writer.Write (message);
			} finally {
				Console.ForegroundColor = fg;
			}

			if (!String.IsNullOrEmpty (message)) {
				switch (level) {
					case LogLevel.Error:
						log.LogError (message);
						break;

					case LogLevel.Warning:
						log.LogWarning (message);
						break;

					default:
					case LogLevel.Message:
					case LogLevel.Info:
						log.LogMessage (message);
						break;

					case LogLevel.Debug:
						log.LogDebugMessage (message);
						break;
				}
			}
		}

		ConsoleColor ForegroundColor (LogLevel level) => level switch {
			LogLevel.Error => ErrorColor,
			LogLevel.Warning => WarningColor,
			LogLevel.Info => InfoColor,
			LogLevel.Debug => DebugColor,
			LogLevel.Message => MessageColor,
			_ => MessageColor,
		};
	}
}
