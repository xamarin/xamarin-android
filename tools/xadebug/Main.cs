using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

using Microsoft.Build.Logging.StructuredLogger;
using Mono.Options;
using Xamarin.Android.Utilities;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Debug;

sealed class ParsedOptions
{
	public bool ShowHelp;
	public bool Verbose;
	public string Configuration = "Debug";
	public string? PackageName;
	public string? Activity;
	public string DotNetCommand = "dotnet";
	public string WorkDirectory = "xadebug-data";
	public string AdbPath = "adb";
	public string? TargetDevice;
	public string? NdkDirPath;
}

class XADebug
{
	const string AndroidManifestZipPath = "AndroidManifest.xml";
	const string DefaultMinSdkVersion = "21";

	static readonly string[] NdkEnvvars = {
		"ANDROID_NDK_PATH",
		"ANDROID_NDK_ROOT",
	};

	static XamarinLoggingHelper log = new XamarinLoggingHelper ();

	static int Main (string[] args)
	{
		bool haveOptionErrors = false;
		var parsedOptions = new ParsedOptions ();
		log.Verbose = parsedOptions.Verbose;

 		var opts = new OptionSet {
			"Usage: dotnet xadebug [OPTIONS] <PROJECT_DIRECTORY_PATH | APPLICATION_APK_PATH>",
			"",
			{ "p|package-name=", "name of the application package", v => parsedOptions.PackageName = EnsureNonEmptyString (log, "-p|--package-name", v, ref haveOptionErrors) },
			{ "c|configuration=", $"{{CONFIGURATION}} in which to build the application. Ignored when running in APK-only mode. Default: {parsedOptions.Configuration}", v => parsedOptions.Configuration = v },
			{ "a|activity=", "Name of the {ACTIVITY} to start the application. Default: determined from AndroidManifest.xml inside the APK", v => parsedOptions.Activity = v },
			{ "d|dotnet=", $"Name of the dotnet {{COMMAND}} to use when building a project. Defaults to {parsedOptions.DotNetCommand}", v => parsedOptions.DotNetCommand = v },
			{ "w|work-dir=", $"{{DIRECTORY}} in which xadebug will store build and debug logs, as well as shared libraries with symbols. Default: {parsedOptions.WorkDirectory}", v => parsedOptions.WorkDirectory = v },
			"",
			{ "s|adb=", "{PATH} to adb to use for this session", v => parsedOptions.AdbPath = EnsureNonEmptyString (log, "-s|--adb", v, ref haveOptionErrors) },
			{ "e|device=", "ID of {DEVICE} to target for this session", v => parsedOptions.TargetDevice = EnsureNonEmptyString (log, "-e|--device", v, ref haveOptionErrors) },
			{ "n|ndk-dir=", "{PATH} to to the Android NDK root directory", v => parsedOptions.NdkDirPath = v },
			"",
			{ "v|verbose", "Show debug messages", v => parsedOptions.Verbose = true },
			{ "h|help|?", "Show this help screen", v => parsedOptions.ShowHelp = true },
		};

		List<string> rest = opts.Parse (args);
		log.Verbose = parsedOptions.Verbose;

		DateTime now = DateTime.Now;
		log.LogFilePath = Path.Combine (Path.GetFullPath (parsedOptions.WorkDirectory), $"session-{now.Year}-{now.Month:00}-{now.Day:00}-{now.Hour:00}:{now.Minute:00}:{now.Second:00}.log");
		log.StatusLine ("Session log file", log.LogFilePath);

		if (parsedOptions.ShowHelp || rest.Count == 0) {
			int ret = 0;
			if (!parsedOptions.ShowHelp) {
				log.ErrorLine ("Path to application APK or directory with a C# project must be specified");
				log.ErrorLine ();
				ret = 1;
			}

			opts.WriteOptionDescriptions (Console.Out);
			return ret;
		}

		if (String.IsNullOrEmpty (parsedOptions.DotNetCommand)) {
			log.ErrorLine ("Empty string passed in the `-d|--dotnet` parameter.  It must be a non-empty string.");
			haveOptionErrors = true;
		}

		if (String.IsNullOrEmpty (parsedOptions.NdkDirPath)) {
			string? ndk = null;

			foreach (string envvar in NdkEnvvars) {
				log.DebugLine ($"Trying to read NDK path environment variable '{envvar}'");
				ndk = Environment.GetEnvironmentVariable (envvar);
				if (!String.IsNullOrEmpty (ndk)) {
					log.DebugLine ($"Potential NDK location: {ndk}");
					break;
				}
			}

			if (String.IsNullOrEmpty (ndk)) {
				log.ErrorLine ("Unable to locate Android NDK from environment variables");
				log.MessageLine ("Please provide path to the NDK using the '-n|--ndk' argument");
				haveOptionErrors = true;
			} else {
				parsedOptions.NdkDirPath = ndk;
			}
		}

		if (!Directory.Exists (parsedOptions.NdkDirPath)) {
			log.ErrorLine ($"NDK directory '{parsedOptions.NdkDirPath}' does not exist");
			return 1;
		}

		if (haveOptionErrors) {
			return 1;
		}

		log.StatusLine ("Using NDK", parsedOptions.NdkDirPath);

		string aPath = rest[0];
		string? apkFilePath = null;
		string? buildLogPath = null;
		ZipArchive? apk = null;

		if (Directory.Exists (aPath)) {
			(apkFilePath, buildLogPath) = BuildApp (aPath, parsedOptions, projectPathIsDirectory: true);
		} else if (File.Exists (aPath)) {
			if (String.Compare (".csproj", Path.GetExtension (aPath), StringComparison.OrdinalIgnoreCase) == 0) {
				// Let's see if we can trust the file name...
				(apkFilePath, buildLogPath) = BuildApp (aPath, parsedOptions, projectPathIsDirectory: false);
			} else if (IsAndroidPackageFile (aPath, out apk)) {
				apkFilePath = aPath;
			} else {
				log.ErrorLine ($"File '{aPath}' is not an Android APK package");
				log.ErrorLine ();
			}
		} else {
			log.ErrorLine ($"Neither directory nor file '{aPath}' exist");
			log.ErrorLine ();
		}

		if (!String.IsNullOrEmpty (buildLogPath)) {
			log.StatusLine ("Build log", buildLogPath);
		}

		if (String.IsNullOrEmpty (apkFilePath)) {
			return 1;
		}

		log.StatusLine ("Input APK", apkFilePath);

		if (apk == null) {
			apk = OpenApk (apkFilePath);
		}

		// Extract app information fromn the embedded manifest
		ApplicationInfo? appInfo = ReadManifest (apk, parsedOptions);
		if (appInfo == null) {
			return 1;
		}

		if (!appInfo.Debuggable) {
			log.ErrorLine ($"Application {apkFilePath} is not debuggable.");
			log.MessageLine ();
			log.MessageLine ("Please rebuild the aplication either in `Debug` configuration or with appropriate properties set in `Release` configuration:");
			log.MessageLine ("TODO: fill in instructions");
			log.MessageLine ();
			return 1;
		}

		var debugSession = new DebugSession (log, appInfo, apkFilePath, apk, parsedOptions);
		if (!debugSession.Prepare ()) {
			return 1;
		}

		if (!debugSession.Run ()) {
			return 1;
		}

		return 0;
	}

	static ApplicationInfo? ReadManifest (ZipArchive apk, ParsedOptions parsedOptions)
	{
		ZipEntry entry = apk.ReadEntry (AndroidManifestZipPath);

		using var manifestData = new MemoryStream ();
		entry.Extract (manifestData);
		manifestData.Seek (0, SeekOrigin.Begin);

		// TODO: make provisions for plain XML AndroidManifest.xml, perhaps? Although not sure if it's really necesary these days anymore as the APKs should all have the
		// binary version of the manifest.
		var axml = new AXMLParser (manifestData, log);
		XmlDocument? manifest = axml.Parse ();
		if (manifest == null) {
			log.ErrorLine ("Unable to parse Android manifest from the apk");
			return null;
		}

		var writerSettings = new XmlWriterSettings {
			Encoding = new UTF8Encoding (false),
			Indent = true,
			IndentChars = "\t",
			NewLineOnAttributes = false,
			OmitXmlDeclaration = false,
			WriteEndDocumentOnClose = true,
		};

		var manifestXml = new StringBuilder ();
		using var writer = XmlWriter.Create (manifestXml, writerSettings);
		manifest.WriteTo (writer);
		writer.Flush ();
		log.DebugLine ("Android manifest from the apk: START");
		log.DebugLine (manifestXml.ToString ());
		log.DebugLine ("Android manifest from the apk: END");

		string? packageName = null;
		XmlNode? node;

		node = manifest.SelectSingleNode ("//manifest");
		if (node == null) {
			log.ErrorLine ("Unable to find root element 'manifest' of AndroidManifest.xml");
			return null;
		}

		var nsManager = new XmlNamespaceManager (manifest.NameTable);
		if (node.Attributes != null) {
			const string nsPrefix = "xmlns:";

			foreach (XmlAttribute attr in node.Attributes) {
				if (!attr.Name.StartsWith (nsPrefix, StringComparison.Ordinal)) {
					continue;
				}

				nsManager.AddNamespace (attr.Name.Substring (nsPrefix.Length), attr.Value);
			}
		}

		if (String.IsNullOrEmpty (parsedOptions.PackageName)) {
			packageName = GetAttributeValue (node, "package");
		} else {
			packageName = parsedOptions.PackageName;
		}

		if (String.IsNullOrEmpty (packageName)) {
			log.ErrorLine ("Unable to determine the package name");
			return null;
		}

		node = manifest.SelectSingleNode ("//manifest/uses-sdk");
		string? minSdkVersion = GetAttributeValue (node, "android:minSdkVersion");
		if (String.IsNullOrEmpty (minSdkVersion)) {
			log.WarningLine ($"Android manifest doesn't specify the minimum SDK version supported by the application, assuming the default of {DefaultMinSdkVersion}");
			minSdkVersion = DefaultMinSdkVersion;
		}

		ApplicationInfo? ret;
		try {
			ret = new ApplicationInfo (packageName, minSdkVersion);
		} catch (Exception ex) {
			log.ErrorLine ($"Exception {ex.GetType ()} thrown while constructing application info: {ex.Message}");
			return null;
		}

		if (String.IsNullOrEmpty (parsedOptions.Activity)) {
			node = manifest.SelectSingleNode ("//manifest/application");
			string? debuggable = GetAttributeValue (node, "android:debuggable");
			if (!String.IsNullOrEmpty (debuggable)) {
				ret.Debuggable = String.Compare ("true", debuggable, StringComparison.OrdinalIgnoreCase) == 0;
			}

			node = manifest.SelectSingleNode ("//manifest/application/activity[./intent-filter/action[@android:name='android.intent.action.MAIN']]", nsManager);
			if (node != null) {
				ret.Activity = GetAttributeValue (node, "android:name");
				log.DebugLine ($"Detected main activity: {ret.Activity}");
			}
		} else {
			ret.Activity = parsedOptions.Activity;
		}

		return ret;
	}

	static string? GetAttributeValue (XmlNode? node, string prefixedAttributeName)
	{
		if (node?.Attributes == null) {
			return null;
		}

		foreach (XmlAttribute attr in node.Attributes) {
			if (String.Compare (prefixedAttributeName, attr.Name, StringComparison.Ordinal) == 0) {
				return attr.Value;
			}
		}

		return null;
	}

	static string EnsureNonEmptyString (XamarinLoggingHelper log, string paramName, string? value, ref bool haveOptionErrors)
	{
		if (String.IsNullOrEmpty (value)) {
			haveOptionErrors = true;
			log.ErrorLine ($"Parameter '{paramName}' requires a non-empty string as its value");
			return String.Empty;
		}

		return value;
	}

	static ZipArchive OpenApk (string filePath) => ZipArchive.Open (filePath, FileMode.Open);

	static bool IsAndroidPackageFile (string filePath, out ZipArchive? apk)
	{
		try {
			apk = OpenApk (filePath);
		} catch (ZipIOException ex) {
			log.DebugLine ($"Failed to open '{filePath}' as ZIP archive: {ex.Message}");
			apk = null;
			return false;
		}

		return apk.ContainsEntry (AndroidManifestZipPath);
	}

	static (string? apkPath, string? buildLogPath) BuildApp (string projectPath, ParsedOptions parsedOptions, bool projectPathIsDirectory)
	{
		log.MessageLine ();

		var dotnet = new DotNetRunner (log, parsedOptions.DotNetCommand, parsedOptions.WorkDirectory);
		string? logPath = dotnet.Build (
			projectPath,
			parsedOptions.Configuration,
			"-p:AndroidCreatePackagePerAbi=False",
			"-p:AndroidPackageFormat=apk",
			"-p:_AndroidAotStripLibraries=False",
			"-p:_AndroidEnableNativeDebugging=True",
			"-p:_AndroidStripNativeLibraries=False"
		).Result;

		if (String.IsNullOrEmpty (logPath)) {
			return FinishAndReturn (null, null);
		}

		string projectDir = projectPathIsDirectory ? projectPath : Path.GetDirectoryName (projectPath) ?? ".";
		string? apkPath = FindApkPathFromLog (projectDir, logPath);

		if (String.IsNullOrEmpty (apkPath)) {
			log.DebugLine ("Could not get APK path from build log, trying to guess");
			apkPath = TryToGuessApkPath (projectDir, parsedOptions);
		}

		if (String.IsNullOrEmpty (apkPath)) {
			log.ErrorLine ("Unable to determine path to the application APK file after build.");
			log.MessageLine ();
			log.MessageLine ("Please run `xadebug` again, passing it path to the produced APK file");
			log.MessageLine ();
			return FinishAndReturn (null, logPath);
		}

		if (!File.Exists (apkPath)) {
			log.ErrorLine ($"APK file '{apkPath}' not found after build");
			return FinishAndReturn (null, logPath);
		};

		return FinishAndReturn (apkPath, logPath);

		(string? apkPath, string? buildLogPath) FinishAndReturn (string? apkPath, string? buildLogPath)
		{
			log.MessageLine ();
			return (apkPath, buildLogPath);
		}
	}

	static string? TryToGuessApkPath (string projectDir, ParsedOptions parsedOptions)
	{
		log.DebugLine ("Trying to find application APK in {projectDir}");

		string binDir = Path.Combine (projectDir, "bin", parsedOptions.Configuration);
		if (!Directory.Exists (binDir)) {
			log.WarningLine ($"Bin output directory '{binDir}' does not exist. Unable to determine path to the produced APK");
			return null;
		}

		const string ApkSuffix = "-Signed.apk";
		string apkName;
		bool apkNameIsGlob;

		if (!String.IsNullOrEmpty (parsedOptions.PackageName)) {
			apkName = $"{parsedOptions.PackageName}{ApkSuffix}";
			apkNameIsGlob = false;
		} else {
			apkName = $"*{ApkSuffix}";
			apkNameIsGlob = true;
		}

		log.StatusLine ("Looking for APK with name", apkName);

		if (!apkNameIsGlob) {
			string apkPath = Path.Combine (binDir, apkName);
			LogPotentialPath (apkPath);

			if (File.Exists (apkPath)) {
				return LogFoundApkPathAndReturn (apkPath);
			}
		}

		// Find subdirectories named netX.Y-android and the apk files inside them
		var apkFiles = new List<string> ();
		foreach (string dir in Directory.EnumerateDirectories (binDir, "net*-android")) {
			if (apkNameIsGlob) {
				foreach (string file in Directory.EnumerateFiles (dir, apkName)) {
					// We know it exists, but the method also logs paths
					AddApkIfExists (file, apkFiles);
				}
			} else {
				AddApkIfExists (Path.Combine (dir, apkName), apkFiles);
			}
		}

		if (apkFiles.Count == 0) {
			return null;
		}

		string selectedApkPath;
		if (apkFiles.Count > 1) {
			// TODO: ask the user to select one
			throw new NotImplementedException ("Support for multiple APK files not implemented yet");
		} else {
			selectedApkPath = apkFiles[0];
		}

		return LogFoundApkPathAndReturn (selectedApkPath);

		void AddApkIfExists (string apkPath, List<string> apkFiles)
		{
			LogPotentialPath (apkPath);
			if (File.Exists (apkPath)) {
				log.DebugLine ($"Found APK: {apkPath}");
				apkFiles.Add (apkPath);
			}
		}

		void LogPotentialPath (string path)
		{
			log.DebugLine ($"Trying path: {path}");
		}
	}

	static string? FindApkPathFromLog (string projectDir, string? logPath)
	{
		if (String.IsNullOrEmpty (logPath)) {
			return null;
		}

		log.DebugLine ($"Trying to find APK file path in the build log ('{logPath}')");

		Build build = BinaryLog.ReadBuild (logPath);
		foreach (Property prop in build.FindChildrenRecursive<Property> ()) {
			if (String.Compare ("ApkFileSigned", prop.Name, StringComparison.Ordinal) != 0) {
				continue;
			}

			if (Path.IsPathRooted (prop.Value)) {
				return LogFoundApkPathAndReturn (prop.Value);
			}

			return LogFoundApkPathAndReturn (Path.Combine (projectDir, prop.Value));
		}

		return null;
	}

	static string LogFoundApkPathAndReturn (string path)
	{
		log.DebugLine ($"Returning APK path: {path}");
		return path;
	}
}
