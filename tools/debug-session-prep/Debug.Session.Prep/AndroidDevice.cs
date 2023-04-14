using System;
using System.Collections.Generic;
using System.IO;

using Xamarin.Android.Tasks;
using Xamarin.Android.Utilities;

namespace Xamarin.Debug.Session.Prep;

class AndroidDevice
{
	const string ServerLauncherScriptName = "xa_start_lldb_server.sh";

	static readonly string[] abiProperties = {
		// new properties
		"ro.product.cpu.abilist",

		// old properties
		"ro.product.cpu.abi",
		"ro.product.cpu.abi2",
	};

	static readonly string[] serialNumberProperties = {
		"ro.serialno",
		"ro.boot.serialno",
	};

	string packageName;
	string adbPath;
	string[] supportedAbis;
	int apiLevel = -1;
	string? appDataDir;
	string? appLldbBaseDir;
	string? appLldbBinDir;
	string? appLldbLogDir;
	string? appLldbTmpDir;
	string? mainAbi;
	string? mainArch;
	string[]? availableAbis;
	string[]? availableArches;
	string? serialNumber;
	bool appIs64Bit;
	string? deviceLdd;
	string? deviceDebugServerPath;
	string? deviceDebugServerScriptPath;
	string outputDir;

	XamarinLoggingHelper log;
	AdbRunner adb;
	AndroidNdk ndk;

	public int ApiLevel => apiLevel;
	public string[] AvailableAbis => availableAbis ?? new string[] {};
	public string[] AvailableArches => availableArches ?? new string[] {};
	public string MainArch => mainArch ?? String.Empty;
	public string MainAbi => mainAbi ?? String.Empty;
	public string SerialNumber => serialNumber ?? String.Empty;
	public string DebugServerLauncherScriptPath => deviceDebugServerScriptPath;
	public string LldbBaseDir => appLldbBaseDir;
	public AdbRunner AdbRunner => adb;

	public AndroidDevice (XamarinLoggingHelper log, AndroidNdk ndk, string outputDir, string adbPath, string packageName, string[] supportedAbis, string? adbTargetDevice = null)
	{
		this.adbPath = adbPath;
		this.log = log;
		this.packageName = packageName;
		this.supportedAbis = supportedAbis;
		this.ndk = ndk;
		this.outputDir = outputDir;

		adb = new AdbRunner (log, adbPath, adbTargetDevice);
	}

	// TODO: implement manual error checking on API 21, since `adb` won't ever return any error code other than 0 - we need to look at the output of any command to determine
	// whether or not it was successful. Ugh.
	public bool GatherInfo ()
	{
		(bool success, string output) = adb.GetPropertyValue ("ro.build.version.sdk").Result;
		if (!success || String.IsNullOrEmpty (output) || !Int32.TryParse (output, out apiLevel)) {
			log.ErrorLine ("Unable to determine connected device's API level");
			return false;
		}

		// Warn on old Pixel C firmware (b/29381985). Newer devices may have Yama
		// enabled but still work with ndk-gdb (b/19277529).
		(success, output) = adb.Shell ("cat", "/proc/sys/kernel/yama/ptrace_scope", "2>/dev/null").Result;
		if (success &&
		    YamaOK (output.Trim ()) &&
		    PropertyIsEqualTo (adb.GetPropertyValue ("ro.build.product").Result, "dragon") &&
		    PropertyIsEqualTo (adb.GetPropertyValue ("ro.product.name").Result, "ryu")
		) {
			log.WarningLine ("WARNING: The device uses Yama ptrace_scope to restrict debugging. ndk-gdb will");
			log.WarningLine ("    likely be unable to attach to a process. With root access, the restriction");
			log.WarningLine ("    can be lifted by writing 0 to /proc/sys/kernel/yama/ptrace_scope. Consider");
			log.WarningLine ("    upgrading your Pixel C to MXC89L or newer, where Yama is disabled.");
			log.WarningLine ();
		}

		if (!DetermineArchitectureAndABI ()) {
			return false;
		}

		if (!DetermineAppDataDirectory ()) {
			return false;
		}

		serialNumber = GetFirstFoundPropertyValue (serialNumberProperties);
		if (String.IsNullOrEmpty (serialNumber)) {
			log.WarningLine ("Unable to determine device serial number");
		} else {
			log.StatusLine ($"Device serial number", serialNumber);
		}

		return true;

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

	public bool Prepare (out string? mainProcessPath)
	{
		mainProcessPath = null;
		if (!DetectTools ()) {
			return false;
		}

		if (!PushDebugServer ()) {
			return false;
		}

		if (!PullLibraries (out mainProcessPath)) {
			return false;
		}

		return true;
	}

	bool PullLibraries (out string? mainProcessPath)
	{
		mainProcessPath = null;
		DeviceLibraryCopier copier;

		if (String.IsNullOrEmpty (deviceLdd)) {
			copier = new NoLddDeviceLibraryCopier (
				log,
				adb,
				appIs64Bit,
				outputDir,
				this
			);
		} else {
			copier = new LddDeviceLibraryCopier (
				log,
				adb,
				appIs64Bit,
				outputDir,
				this
			);
		}

		return copier.Copy (out mainProcessPath);
	}

	bool PushDebugServer ()
	{
		string? debugServerPath = ndk.GetDebugServerPath (mainAbi!);
		if (String.IsNullOrEmpty (debugServerPath)) {
			return false;
		}

		if (!CreateLldbDir (appLldbBinDir!) || !CreateLldbDir (appLldbLogDir) || !CreateLldbDir (appLldbTmpDir)) {
			return false;
		}

		//string serverName = $"xa-{context.arch}-{Path.GetFileName (debugServerPath)}";
		string serverName = Path.GetFileName (debugServerPath);
		deviceDebugServerPath = $"{appLldbBinDir}/{serverName}";

		KillDebugServer (deviceDebugServerPath);

		// Always push the server binary, as we don't know what version might already be there
		if (!PushServerExecutable (debugServerPath, deviceDebugServerPath)) {
			return false;
		}
		log.StatusLine ("Debug server path on device", deviceDebugServerPath);

		string? launcherScript = Utilities.ReadManifestResource (log, ServerLauncherScriptName);
		if (String.IsNullOrEmpty (launcherScript)) {
			return false;
		}

		string launcherScriptPath = Path.Combine (outputDir, ServerLauncherScriptName);
		Directory.CreateDirectory (Path.GetDirectoryName (launcherScriptPath)!);
		File.WriteAllText (launcherScriptPath, launcherScript, Utilities.UTF8NoBOM);

		deviceDebugServerScriptPath = $"{appLldbBinDir}/{Path.GetFileName (launcherScriptPath)}";
		if (!PushServerExecutable (launcherScriptPath, deviceDebugServerScriptPath)) {
			return false;
		}
		log.StatusLine ("Debug server launcher script path on device", deviceDebugServerScriptPath);
		log.MessageLine ();

		return true;

		bool CreateLldbDir (string dir)
		{
			if (!adb.CreateDirectoryAs (packageName, dir).Result.success) {
				log.ErrorLine ($"Failed to create debug server destination directory on device, {dir}");
				return false;
			}

			return true;
		}
	}

	bool PushServerExecutable (string hostSource, string deviceDestination)
	{
		string executableName = Path.GetFileName (deviceDestination);

		// Always push the executable, as we don't know what version might already be there
		log.DebugLine ($"Uploading {hostSource} to device");

		// First upload to temporary path, as it's writable for everyone
		string remotePath = $"/data/local/tmp/{executableName}";
		if (!adb.Push (hostSource, remotePath).Result) {
			log.ErrorLine ($"Failed to upload debug server {hostSource} to device path {remotePath}");
			return false;
		}

		// Next, copy it to the app dir, with run-as
		(bool success, string output) = adb.Shell (
			"cat", remotePath, "|",
			"run-as", packageName,
			"sh", "-c", $"'cat > {deviceDestination}'"
		).Result;

		if (!success) {
			log.ErrorLine ($"Failed to copy debug executable to device, from {hostSource} to {deviceDestination}");
			return false;
		}

		(success, output) = adb.RunAs (packageName, "chmod", "700", deviceDestination).Result;
		if (!success) {
			log.ErrorLine ($"Failed to make debug server executable on device, at {deviceDestination}");
			return false;
		}

		return true;
	}

	// TODO: handle multiple pids
	bool KillDebugServer (string debugServerPath)
	{
		long serverPID = GetDeviceProcessID (debugServerPath, quiet: false);
		if (serverPID <= 0) {
			return true;
		}

		log.DebugLine ("Killing previous instance of the debug server");
		(bool success, string _) = adb.RunAs (packageName, "kill", "-9", $"{serverPID}").Result;
		return success;
	}

	long GetDeviceProcessID (string processName, bool quiet = false)
	{
		(bool success, string output) = adb.Shell ("pidof", Path.GetFileName (processName)).Result;
		if (!success) {
			if (!quiet) {
				log.ErrorLine ($"Failed to obtain PID of process '{processName}'");
				log.ErrorLine (output);
			}
			return -1;
		}

		output = output.Trim ();
		if (!UInt32.TryParse (output, out uint pid)) {
			if (!quiet) {
				log.ErrorLine ($"Unable to parse string '{output}' as the package's PID");
			}
			return -1;
		}

		return pid;
	}

	bool DetectTools ()
	{
		// Not all versions of Android have the `which` utility, all of them have `whence`
		// Also, API 21 adbd will not return an error code to us... But since we know that 21
		// doesn't have LDD, we'll cheat
		deviceLdd = null;
		if (apiLevel > 21) {
			(bool success, string output) = adb.Shell ("whence", "ldd").Result;
			if (success) {
				log.DebugLine ($"Found `ldd` on device at '{output}'");
				deviceLdd = output;
			}
		}

		if (String.IsNullOrEmpty (deviceLdd)) {
			log.DebugLine ("`ldd` not found on device");
		}

		return true;
	}

	bool DetermineAppDataDirectory ()
	{
		(bool success, string output) = adb.GetAppDataDirectory (packageName).Result;
		if (!AppDataDirFound (success, output)) {
			log.ErrorLine ($"Unable to determine data directory for package '{packageName}'");
			return false;
		}

		appDataDir = output.Trim ();
		log.StatusLine ($"Application data directory on device", appDataDir);

		appLldbBaseDir = $"{appDataDir}/lldb";
		appLldbBinDir = $"{appLldbBaseDir}/bin";
		appLldbLogDir = $"{appLldbBaseDir}/log";
		appLldbTmpDir = $"{appLldbBaseDir}/tmp";

		// Applications with minSdkVersion >= 24 will have their data directories
		// created with rwx------ permissions, preventing adbd from forwarding to
		// the gdbserver socket. To be safe, if we're on a device >= 24, always
		// chmod the directory.
		if (apiLevel >= 24) {
			(success, output) = adb.RunAs (packageName, "/system/bin/chmod", "a+x", appDataDir).Result;
			if (!success) {
				log.ErrorLine ("Failed to make application data directory world executable");
				return false;
			}
		}

		return true;

		bool AppDataDirFound (bool success, string output)
		{
			if (apiLevel > 21) {
				return success;
			}

			if (output.IndexOf ("run-as: Package", StringComparison.OrdinalIgnoreCase) >= 0 &&
			    output.IndexOf ("is unknown", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return false;
			}

			return true;
		}
	}

	bool DetermineArchitectureAndABI ()
	{
		string? propValue = GetFirstFoundPropertyValue (abiProperties);
		string[]? deviceABIs = propValue?.Split (',');

		if (deviceABIs == null || deviceABIs.Length == 0) {
			log.ErrorLine ("Unable to determine device ABI");
			return false;
		}

		LogABIs ("Application", supportedAbis);
		LogABIs ("     Device", deviceABIs);

		bool gotValidAbi = false;
		var possibleAbis = new List<string> ();
		var possibleArches = new List<string> ();

		foreach (string deviceABI in deviceABIs) {
			foreach (string appABI in supportedAbis) {
				if (String.Compare (appABI, deviceABI, StringComparison.OrdinalIgnoreCase) == 0) {
					string arch = AbiToArch (deviceABI);

					if (!gotValidAbi) {
						mainAbi = deviceABI;
						mainArch = arch;

						log.StatusLine ($"    Selected ABI", $"{mainAbi} (architecture: {mainArch})");

						appIs64Bit = mainAbi.IndexOf ("64", StringComparison.Ordinal) >= 0;
						gotValidAbi = true;
					}

					possibleAbis.Add (deviceABI);
					possibleArches.Add (arch);
				}
			}
		}

		if (!gotValidAbi) {
			log.ErrorLine ($"Application cannot run on the selected device: no matching ABI found");
		}

		availableAbis = possibleAbis.ToArray ();
		availableArches = possibleArches.ToArray ();
		return gotValidAbi;

		void LogABIs (string which, string[] abis)
		{
			log.StatusLine ($"{which} ABIs", String.Join (", ", abis));
		}

		string AbiToArch (string abi) => abi switch {
			"armeabi" => "arm",
			"armeabi-v7a" => "arm",
			"arm64-v8a" => "arm64",
			_ => abi,
		};
	}

	string? GetFirstFoundPropertyValue (string[] propertyNames)
	{
		foreach (string prop in propertyNames) {
			(bool success, string value) = adb.GetPropertyValue (prop).Result;
			if (!success) {
				continue;
			}

			return value;
		}

		return null;
	}
}
