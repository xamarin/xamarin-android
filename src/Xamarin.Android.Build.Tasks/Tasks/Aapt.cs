// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Xamarin.Android.Tools;
using Xamarin.Build;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class Aapt : AndroidAsyncTask
	{
		public override string TaskPrefix => "APT";

		public ITaskItem[] AdditionalAndroidResourcePaths { get; set; }

		public string AndroidComponentResgenFlagFile { get; set; }

		public ITaskItem AndroidManifestFile { get; set;}

		public bool NonConstantId { get; set; }

		public string AssetDirectory { get; set; }

		[Required]
		public ITaskItem[] ManifestFiles { get; set; }

		[Required]
		public string ResourceDirectory { get; set; }

		public string ResourceOutputFile { get; set; }

		[Required]
		public string JavaDesignerOutputDirectory { get; set; }

		[Required]
		public string JavaPlatformJarPath { get; set; }

		public string UncompressedFileExtensions { get; set; }
		public string PackageName { get; set; }

		public string ExtraPackages { get; set; }

		public ITaskItem [] AdditionalResourceDirectories { get; set; }

		public ITaskItem [] LibraryProjectJars { get; set; }

		public string ExtraArgs { get; set; }

		protected string ToolName { get { return OS.IsWindows ? "aapt.exe" : "aapt"; } }

		public string ToolPath { get; set; }

		public string ToolExe { get; set; }

		public string ApiLevel { get; set; }

		public bool AndroidUseLatestPlatformSdk { get; set; }

		public string [] SupportedAbis { get; set; }

		public bool CreatePackagePerAbi { get; set; }

		public string ImportsDirectory { get; set; }
		public string OutputImportDirectory { get; set; }
		public string AssemblyIdentityMapFile { get; set; }

		// pattern to use for the version code. Used in CreatePackagePerAbi
		// eg. {abi:00}{dd}{version}
		// known keyworks
		//  {abi} the value for the current abi
		//  {version} the version code from the manifest.
		public string VersionCodePattern { get; set; }

		// Name=Value pair seperated by ';'
		// e.g screen=21;abi=11
		public string VersionCodeProperties { get; set; }

		public string AndroidSdkPlatform { get; set; }

		public string ResourceSymbolsTextFileDirectory { get; set; }

		Dictionary<string,string> _resource_name_case_map;
		AssemblyIdentityMap assemblyMap = new AssemblyIdentityMap ();
		string resourceDirectory;

		Dictionary<string, string> resource_name_case_map => _resource_name_case_map ??= MonoAndroidHelper.LoadResourceCaseMap (BuildEngine4);

		bool ManifestIsUpToDate (string manifestFile)
		{
			return !String.IsNullOrEmpty (AndroidComponentResgenFlagFile) &&
				File.Exists (AndroidComponentResgenFlagFile) && File.Exists (manifestFile) &&
				File.GetLastWriteTime (AndroidComponentResgenFlagFile) > File.GetLastWriteTime (manifestFile);
		}

		bool RunAapt (string commandLine, IList<OutputLine> output)
		{
			var stdout_completed = new ManualResetEvent (false);
			var stderr_completed = new ManualResetEvent (false);
			var psi = new ProcessStartInfo () {
				FileName = GenerateFullPathToTool (),
				Arguments = commandLine,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				WorkingDirectory = WorkingDirectory,
			};
			object lockObject = new object ();
			using (var proc = new Process ()) {
				proc.OutputDataReceived += (sender, e) => {
					if (e.Data != null)
						lock (lockObject)
							output.Add (new OutputLine (e.Data, stdError: false));
					else
						stdout_completed.Set ();
				};
				proc.ErrorDataReceived += (sender, e) => {
					if (e.Data != null)
						lock (lockObject)
							output.Add (new OutputLine (e.Data, stdError: true));
					else
						stderr_completed.Set ();
				};
				proc.StartInfo = psi;
				LogDebugMessage ("Executing {0}", commandLine);
				proc.Start ();
				proc.BeginOutputReadLine ();
				proc.BeginErrorReadLine ();
				CancellationToken.Register (() => {
					try {
						proc.Kill ();
					} catch (Exception) {
					}
				});
				proc.WaitForExit ();
				if (psi.RedirectStandardError)
					stderr_completed.WaitOne (TimeSpan.FromSeconds (30));
				if (psi.RedirectStandardOutput)
					stdout_completed.WaitOne (TimeSpan.FromSeconds (30));
				return proc.ExitCode == 0;
			}
		}

		bool ExecuteForAbi (string cmd, string currentResourceOutputFile)
		{
			var output = new List<OutputLine> ();
			var ret = RunAapt (cmd, output);
			var success = !string.IsNullOrEmpty (currentResourceOutputFile)
				? File.Exists (Path.Combine (currentResourceOutputFile + ".bk"))
				: ret;
			foreach (var line in output) {
				if (line.StdError) {
					LogEventsFromTextOutput (line.Line, MessageImportance.Normal, success);
				} else {
					LogMessage (line.Line, MessageImportance.Normal);
				}
			}
			if (ret && !string.IsNullOrEmpty (currentResourceOutputFile)) {
				var tmpfile = currentResourceOutputFile + ".bk";
				Files.CopyIfZipChanged (tmpfile, currentResourceOutputFile);
				File.Delete (tmpfile);
			}
			return ret;
		}

		void ProcessManifest (ITaskItem manifestFile)
		{
			var manifest = Path.IsPathRooted (manifestFile.ItemSpec) ? manifestFile.ItemSpec : Path.Combine (WorkingDirectory, manifestFile.ItemSpec);
			if (!File.Exists (manifest)) {
				LogDebugMessage ("{0} does not exists. Skipping", manifest);
				return;
			}

			bool upToDate = ManifestIsUpToDate (manifest);

			if (AdditionalAndroidResourcePaths != null)
				foreach (var dir in AdditionalAndroidResourcePaths)
					if (!string.IsNullOrEmpty (dir.ItemSpec))
						upToDate = upToDate && ManifestIsUpToDate (string.Format ("{0}{1}{2}{3}{4}", dir, Path.DirectorySeparatorChar, "manifest", Path.DirectorySeparatorChar, "AndroidManifest.xml"));

			if (upToDate) {
				LogMessage ("  Additional Android Resources manifsets files are unchanged. Skipping.");
				return;
			}

			var defaultAbi = new string [] { null };
			var abis = CreatePackagePerAbi && SupportedAbis?.Length > 1 ? defaultAbi.Concat (SupportedAbis) : defaultAbi;
			foreach (var abi in abis) {
				var currentResourceOutputFile = abi != null ? string.Format ("{0}-{1}", ResourceOutputFile, abi) : ResourceOutputFile;
				if (!string.IsNullOrEmpty (currentResourceOutputFile) && !Path.IsPathRooted (currentResourceOutputFile))
					currentResourceOutputFile = Path.Combine (WorkingDirectory, currentResourceOutputFile);
				string cmd = GenerateCommandLineCommands (manifest, abi, currentResourceOutputFile);
				if (string.IsNullOrWhiteSpace (cmd) || !ExecuteForAbi (cmd, currentResourceOutputFile)) {
					Cancel ();
				}
			}

			return;
		}

		public override System.Threading.Tasks.Task RunTaskAsync ()
		{
			resourceDirectory = ResourceDirectory.TrimEnd ('\\');
			if (!Path.IsPathRooted (resourceDirectory))
				resourceDirectory = Path.Combine (WorkingDirectory, resourceDirectory);

			assemblyMap.Load (Path.Combine (WorkingDirectory, AssemblyIdentityMapFile));

			return this.WhenAll (ManifestFiles, ProcessManifest);
		}

		protected string GenerateCommandLineCommands (string ManifestFile, string currentAbi, string currentResourceOutputFile)
		{
			// For creating Resource.designer.cs:
			//   Running command: C:\Program Files (x86)\Android\android-sdk-windows\platform-tools\aapt
			//     "package"
			//     "-M" "C:\Users\Jonathan\AppData\Local\Temp\ryob4gaw.way\AndroidManifest.xml"
			//     "-J" "C:\Users\Jonathan\AppData\Local\Temp\ryob4gaw.way"
			//     "-F" "C:\Users\Jonathan\AppData\Local\Temp\ryob4gaw.way\resources.apk"
			//     "-S" "c:\users\jonathan\documents\visual studio 2010\Projects\MonoAndroidApplication4\MonoAndroidApplication4\obj\Debug\res"
			//     "-I" "C:\Program Files (x86)\Android\android-sdk-windows\platforms\android-8\android.jar"
			//     "--max-res-version" "10"

			// For packaging:
			//   Running command: C:\Program Files (x86)\Android\android-sdk-windows\platform-tools\aapt
			//     "package"
			//     "-f"
			//     "-m"
			//     "-M" "AndroidManifest.xml"
			//     "-J" "src"
			//     "--custom-package" "androidmsbuildtest.androidmsbuildtest"
			//     "-F" "bin\packaged_resources"
			//     "-S" "C:\Users\Jonathan\Documents\Visual Studio 2010\Projects\AndroidMSBuildTest\AndroidMSBuildTest\obj\Debug\res"
			//     "-I" "C:\Program Files (x86)\Android\android-sdk-windows\platforms\android-8\android.jar"
			//     "--extra-packages" "com.facebook.android:my.another.library"

			var cmd = new CommandLineBuilder ();

			cmd.AppendSwitch ("package");

			if (MonoAndroidHelper.LogInternalExceptions)
				cmd.AppendSwitch ("-v");
			if (NonConstantId)
				cmd.AppendSwitch ("--non-constant-id");
			cmd.AppendSwitch ("-f");
			cmd.AppendSwitch ("-m");
			string manifestFile;
			string manifestDir = Path.Combine (Path.GetDirectoryName (ManifestFile), currentAbi != null ? currentAbi : "manifest");

			Directory.CreateDirectory (manifestDir);
			manifestFile = Path.Combine (manifestDir, Path.GetFileName (ManifestFile));
			ManifestDocument manifest = new ManifestDocument (ManifestFile);
			manifest.TargetSdkVersion = AndroidSdkPlatform;
			if (!string.IsNullOrEmpty (VersionCodePattern)) {
				try {
					manifest.CalculateVersionCode (currentAbi, VersionCodePattern, VersionCodeProperties);
				} catch (ArgumentOutOfRangeException ex) {
					LogCodedError ("XA0003", ManifestFile, 0, ex.Message);
					return string.Empty;
				}
			}
			if (currentAbi != null && string.IsNullOrEmpty (VersionCodePattern)) {
				manifest.SetAbi (currentAbi);
			}
			if (!manifest.ValidateVersionCode (out string error, out string errorCode)) {
				LogCodedError (errorCode, ManifestFile, 0, error);
				return string.Empty;
			}
			manifest.Save (LogCodedWarning, manifestFile);

			cmd.AppendSwitchIfNotNull ("-M ", manifestFile);
			var designerDirectory = Path.IsPathRooted (JavaDesignerOutputDirectory) ? JavaDesignerOutputDirectory : Path.Combine (WorkingDirectory, JavaDesignerOutputDirectory);
			Directory.CreateDirectory (designerDirectory);
			cmd.AppendSwitchIfNotNull ("-J ", JavaDesignerOutputDirectory);

			if (PackageName != null)
				cmd.AppendSwitchIfNotNull ("--custom-package ", PackageName.ToLowerInvariant ());

			if (!string.IsNullOrEmpty (currentResourceOutputFile))
				cmd.AppendSwitchIfNotNull ("-F ", currentResourceOutputFile + ".bk");
			// The order of -S arguments is *important*, always make sure this one comes FIRST
			cmd.AppendSwitchIfNotNull ("-S ", resourceDirectory.TrimEnd ('\\'));
			if (AdditionalResourceDirectories != null) {
				foreach (var dir in AdditionalResourceDirectories) {
					var resdir = dir.ItemSpec.TrimEnd ('\\');
					if (Directory.Exists (resdir)) {
						cmd.AppendSwitchIfNotNull ("-S ", resdir);
					}
				}
			}
			if (AdditionalAndroidResourcePaths != null) {
				foreach (var dir in AdditionalAndroidResourcePaths) {
					var resdir = Path.Combine (dir.ItemSpec, "res");
					if (Directory.Exists (resdir)) {
						cmd.AppendSwitchIfNotNull ("-S ", resdir);
					}
				}
			}

			if (LibraryProjectJars != null)
				foreach (var jar in LibraryProjectJars)
					cmd.AppendSwitchIfNotNull ("-j ", jar);

			cmd.AppendSwitchIfNotNull ("-I ", JavaPlatformJarPath);

			// Add asset directory if it exists
			if (!string.IsNullOrWhiteSpace (AssetDirectory)) {
				var assetDir = AssetDirectory.TrimEnd ('\\');
				if (!Path.IsPathRooted (assetDir))
					assetDir = Path.Combine (WorkingDirectory, assetDir);
				if (!string.IsNullOrWhiteSpace (assetDir) && Directory.Exists (assetDir))
					cmd.AppendSwitchIfNotNull ("-A ", assetDir);
			}
			if (!string.IsNullOrWhiteSpace (UncompressedFileExtensions))
				foreach (var ext in UncompressedFileExtensions.Split (new char[] { ';', ','}, StringSplitOptions.RemoveEmptyEntries))
					cmd.AppendSwitchIfNotNull ("-0 ", ext.StartsWith (".", StringComparison.OrdinalIgnoreCase) ? ext : $".{ext}");

			if (!string.IsNullOrEmpty (ExtraPackages))
				cmd.AppendSwitchIfNotNull ("--extra-packages ", ExtraPackages);

			cmd.AppendSwitch ("--auto-add-overlay");

			if (!string.IsNullOrEmpty (ResourceSymbolsTextFileDirectory))
				cmd.AppendSwitchIfNotNull ("--output-text-symbols ", ResourceSymbolsTextFileDirectory);

			if (!string.IsNullOrWhiteSpace (ExtraArgs))
				cmd.AppendSwitch (ExtraArgs);

			if (!AndroidUseLatestPlatformSdk)
				cmd.AppendSwitchIfNotNull ("--max-res-version ", ApiLevel);

			return cmd.ToString ();
		}

		protected string GenerateFullPathToTool ()
		{
			return Path.Combine (ToolPath, string.IsNullOrEmpty (ToolExe) ? ToolName : ToolExe);
		}

		protected void LogEventsFromTextOutput (string singleLine, MessageImportance messageImportance, bool apptResult)
		{
			if (string.IsNullOrEmpty (singleLine))
				return;

			var match = AndroidRunToolTask.AndroidErrorRegex.Match (singleLine.Trim ());

			if (match.Success) {
				var file = match.Groups["file"].Value;
				int line = 0;
				if (!string.IsNullOrEmpty (match.Groups["line"]?.Value))
					line = int.Parse (match.Groups["line"].Value.Trim ()) + 1;
				var level = match.Groups["level"].Value.ToLowerInvariant ();
				var message = match.Groups ["message"].Value;
				if (message.Contains ("fakeLogOpen")) {
					LogMessage (singleLine, MessageImportance.Normal);
					return;
				}
				if (level.Contains ("warning")) {
					LogCodedWarning (GetErrorCode (singleLine), singleLine);
					return;
				}

				// Try to map back to the original resource file, so when the user
				// double clicks the error, it won't take them to the obj/Debug copy
				string newfile = MonoAndroidHelper.FixUpAndroidResourcePath (file, resourceDirectory, string.Empty, resource_name_case_map);
				if (!string.IsNullOrEmpty (newfile)) {
					file = newfile;
				}

				bool manifestError = false;
				if (AndroidManifestFile != null && string.Compare (Path.GetFileName (file), Path.GetFileName (AndroidManifestFile.ItemSpec), StringComparison.OrdinalIgnoreCase) == 0) {
					manifestError = true;
				}

				// Strip any "Error:" text from aapt's output
				if (message.StartsWith ("error: ", StringComparison.InvariantCultureIgnoreCase))
					message = message.Substring ("error: ".Length);

				if (level.Contains ("error") || (line != 0 && !string.IsNullOrEmpty (file))) {
					if (manifestError)
						LogCodedError (GetErrorCode (message), string.Format (Xamarin.Android.Tasks.Properties.Resources.AAPTManifestError, message.TrimEnd('.')), AndroidManifestFile.ItemSpec, 0);
					else
						LogCodedError (GetErrorCode (message), message, file, line);
					return;
				}
			}

			if (!apptResult) {
				var message = string.Format ("{0} \"{1}\".", singleLine.Trim (), singleLine.Substring (singleLine.LastIndexOfAny (new char [] { '\\', '/' }) + 1));
				LogCodedError (GetErrorCode (message), message, ToolName);
			} else {
				LogCodedWarning (GetErrorCode (singleLine), singleLine);
			}
		}

		static string GetErrorCode (string message)
		{
			foreach (var tuple in error_codes)
				if (message.IndexOf (tuple.Item2, StringComparison.OrdinalIgnoreCase) >= 0)
					return tuple.Item1;

			return "APT1000";
		}

		static readonly List<Tuple<string, string>> error_codes = new List<Tuple<string, string>> () {
			Tuple.Create ("APT1001", "can't use '-u' with add"),
			Tuple.Create ("APT1002", "dump failed because assets could not be loaded"),
			Tuple.Create ("APT1003", "dump failed because no AndroidManifest.xml found"),
			Tuple.Create ("APT1004", "dump failed because the resource table is invalid/corrupt"),
			Tuple.Create ("APT1005", "during crunch - archive is toast"),
			Tuple.Create ("APT1006", "failed to get platform version code"),
			Tuple.Create ("APT1007", "failed to get platform version name"),
			Tuple.Create ("APT1008", "failed to get XML element name (bad string pool)"),
			Tuple.Create ("APT1009", "failed to write library table"),
			Tuple.Create ("APT1010", "getting resolved resource attribute"),
			Tuple.Create ("APT1011", "Key string data is corrupt"),
			Tuple.Create ("APT1012", "list -a failed because assets could not be loaded"),
			Tuple.Create ("APT1013", "manifest does not start with <manifest> tag"),
			Tuple.Create ("APT1014", "missing 'android:name' for permission"),
			Tuple.Create ("APT1015", "missing 'android:name' for uses-permission"),
			Tuple.Create ("APT1016", "missing 'android:name' for uses-permission-sdk-23"),
			Tuple.Create ("APT1017", "Missing entries, quit"),
			Tuple.Create ("APT1018", "must specify zip file name"),
			Tuple.Create ("APT1019", "No AndroidManifest.xml file found"),
			Tuple.Create ("APT1020", "No argument supplied for '-A' option"),
			Tuple.Create ("APT1021", "No argument supplied for '-c' option"),
			Tuple.Create ("APT1022", "No argument supplied for '--custom-package' option"),
			Tuple.Create ("APT1023", "No argument supplied for '-D' option"),
			Tuple.Create ("APT1024", "No argument supplied for '-e' option"),
			Tuple.Create ("APT1025", "No argument supplied for '--extra-packages' option"),
			Tuple.Create ("APT1026", "No argument supplied for '--feature-after' option"),
			Tuple.Create ("APT1027", "No argument supplied for '--feature-of' option"),
			Tuple.Create ("APT1028", "No argument supplied for '-F' option"),
			Tuple.Create ("APT1029", "No argument supplied for '-g' option"),
			Tuple.Create ("APT1030", "No argument supplied for '--ignore-assets' option"),
			Tuple.Create ("APT1031", "No argument supplied for '-I' option"),
			Tuple.Create ("APT1032", "No argument supplied for '-j' option"),
			Tuple.Create ("APT1033", "No argument supplied for '--max-res-version' option"),
			Tuple.Create ("APT1034", "No argument supplied for '--max-sdk-version' option"),
			Tuple.Create ("APT1035", "No argument supplied for '--min-sdk-version' option"),
			Tuple.Create ("APT1036", "No argument supplied for '-M' option"),
			Tuple.Create ("APT1037", "No argument supplied for '-o' option"),
			Tuple.Create ("APT1038", "No argument supplied for '-output-text-symbols' option"),
			Tuple.Create ("APT1039", "No argument supplied for '-P' option"),
			Tuple.Create ("APT1040", "No argument supplied for '--preferred-density' option"),
			Tuple.Create ("APT1041", "No argument supplied for '--private-symbols' option"),
			Tuple.Create ("APT1042", "No argument supplied for '--product' option"),
			Tuple.Create ("APT1043", "No argument supplied for '--rename-instrumentation-target-package' option"),
			Tuple.Create ("APT1044", "No argument supplied for '--rename-manifest-package' option"),
			Tuple.Create ("APT1045", "No argument supplied for '-S' option"),
			Tuple.Create ("APT1046", "No argument supplied for '--split' option"),
			Tuple.Create ("APT1047", "No argument supplied for '--target-sdk-version' option"),
			Tuple.Create ("APT1048", "No argument supplied for '--version-code' option"),
			Tuple.Create ("APT1049", "No argument supplied for '--version-name' option"),
			Tuple.Create ("APT1050", "no dump file specified"),
			Tuple.Create ("APT1051", "no dump option specified"),
			Tuple.Create ("APT1052", "no dump xmltree resource file specified"),
			Tuple.Create ("APT1053", "no input files"),
			Tuple.Create ("APT1054", "no <manifest> tag found in platform AndroidManifest.xml"),
			Tuple.Create ("APT1055", "out of memory creating package chunk for ResTable_header"),
			Tuple.Create ("APT1056", "out of memory creating ResTable_entry"),
			Tuple.Create ("APT1057", "out of memory creating ResTable_header"),
			Tuple.Create ("APT1058", "out of memory creating ResTable_package"),
			Tuple.Create ("APT1059", "out of memory creating ResTable_type"),
			Tuple.Create ("APT1060", "out of memory creating ResTable_typeSpec"),
			Tuple.Create ("APT1061", "out of memory creating Res_value"),
			Tuple.Create ("APT1062", "Out of memory for string pool"),
			Tuple.Create ("APT1063", "Out of memory padding string pool"),
			Tuple.Create ("APT1064", "parsing XML"),
			Tuple.Create ("APT1065", "Platform AndroidManifest.xml is corrupt"),
			Tuple.Create ("APT1066", "Platform AndroidManifest.xml not found"),
			Tuple.Create ("APT1067", "print resolved resource attribute"),
			Tuple.Create ("APT1068", "retrieving parent for item:"),
			Tuple.Create ("APT1069", "specify zip file name (only)"),
			Tuple.Create ("APT1070", "Type string data is corrupt"),
			Tuple.Create ("APT1071", "Unable to parse generated resources, aborting"),
			Tuple.Create ("APT1072", "Invalid BCP 47 tag in directory name"),	// ERROR: Invalid BCP 47 tag in directory name: %s
			Tuple.Create ("APT1073", "parsing preferred density"),			// Error parsing preferred density: %s
			Tuple.Create ("APT1074", "Asset package include"),			// ERROR: Asset package include '%s' not found
			Tuple.Create ("APT1075", "base feature package"),			// ERROR: base feature package '%s' not found
			Tuple.Create ("APT1076", "Split configuration"),			// ERROR: Split configuration '%s' is already defined in another split
			Tuple.Create ("APT1077", "failed opening/creating"),			// ERROR: failed opening/creating '%s' as Zip file
			Tuple.Create ("APT1078", "as Zip file for writing"),			// ERROR: unable to open '%s' as Zip file for writing
			Tuple.Create ("APT1079", "as Zip file"),				// ERROR: failed opening '%s' as Zip file
			Tuple.Create ("APT1080", "included asset path"),			// ERROR: included asset path %s could not be loaded
			Tuple.Create ("APT1081", "getting 'android:name' attribute"),
			Tuple.Create ("APT1082", "getting 'android:name'"),
			Tuple.Create ("APT1083", "getting 'android:versionCode' attribute"),
			Tuple.Create ("APT1084", "getting 'android:versionName' attribute"),
			Tuple.Create ("APT1085", "getting 'android:compileSdkVersion' attribute"),
			Tuple.Create ("APT1086", "getting 'android:installLocation' attribute"),
			Tuple.Create ("APT1087", "getting 'android:icon' attribute"),
			Tuple.Create ("APT1088", "getting 'android:testOnly' attribute"),
			Tuple.Create ("APT1089", "getting 'android:banner' attribute"),
			Tuple.Create ("APT1090", "getting 'android:isGame' attribute"),
			Tuple.Create ("APT1091", "getting 'android:debuggable' attribute"),
			Tuple.Create ("APT1092", "getting 'android:minSdkVersion' attribute"),
			Tuple.Create ("APT1093", "getting 'android:targetSdkVersion' attribute"),
			Tuple.Create ("APT1094", "getting 'android:label' attribute"),
			Tuple.Create ("APT1095", "getting compatible screens"),
			Tuple.Create ("APT1096", "getting 'android:name' attribute for uses-library"),
			Tuple.Create ("APT1097", "getting 'android:name' attribute for receiver"),
			Tuple.Create ("APT1098", "getting 'android:permission' attribute for receiver"),
			Tuple.Create ("APT1099", "getting 'android:name' attribute for service"),
			Tuple.Create ("APT1100", "getting 'android:name' attribute for meta-data tag in service"),
			Tuple.Create ("APT1101", "getting 'android:name' attribute for meta-data"),
			Tuple.Create ("APT1102", "getting 'android:permission' attribute for service"),
			Tuple.Create ("APT1103", "getting 'android:permission' attribute for provider"),
			Tuple.Create ("APT1104", "getting 'android:exported' attribute for provider"),
			Tuple.Create ("APT1105", "getting 'android:grantUriPermissions' attribute for provider"),
			Tuple.Create ("APT1106", "getting 'android:value' or 'android:resource' attribute for meta-data"),
			Tuple.Create ("APT1107", "getting 'android:resource' attribute for meta-data tag in service"),
			Tuple.Create ("APT1108", "getting AID category for service"),
			Tuple.Create ("APT1109", "getting 'name' attribute"),
			Tuple.Create ("APT1110", "unknown dump option"),
			Tuple.Create ("APT1111", "failed opening Zip archive"),
			Tuple.Create ("APT1112", "exists but is not regular file"),		// ERROR: output file '%s' exists but is not regular file
			Tuple.Create ("APT1113", "failed to parse split configuration"),
			Tuple.Create ("APT1114", "packaging of"),				// ERROR: packaging of '%s' failed
			Tuple.Create ("APT1115", "9-patch image"),				// ERROR: 9-patch image %s malformed
			Tuple.Create ("APT1116", "Failure processing PNG image"),
			Tuple.Create ("APT1117", "Unknown command"),
			Tuple.Create ("APT1118", "exists (use '-f' to force overwrite)"),
			Tuple.Create ("APT1119", "exists and is not a regular file"),
			Tuple.Create ("APT1120", "unable to process assets while packaging"),
			Tuple.Create ("APT1121", "unable to process jar files while packaging"),
			Tuple.Create ("APT1122", "Unknown option"),
			Tuple.Create ("APT1123", "Unknown flag"),
			Tuple.Create ("APT1124", "Zip flush failed, archive may be hosed"),
			Tuple.Create ("APT1125", "exists twice (check for with"),		// ERROR: '%s' exists twice (check for with & w/o '.gz'?)
			Tuple.Create ("APT1126", "unable to uncompress entry"),
			Tuple.Create ("APT1127", "as a zip file"),				// ERROR: unable to open '%s' as a zip file: %d
			Tuple.Create ("APT1128", "unable to process"),				// ERROR: unable to process '%s'
			Tuple.Create ("APT1129", "malformed resource filename"),
			Tuple.Create ("APT1130", "AndroidManifest.xml already defines"),	// Error: AndroidManifest.xml already defines %s (in %s); cannot insert new value %s
			Tuple.Create ("APT1131", "In <declare-styleable>"),			// ERROR: In <declare-styleable> %s, unable to find attribute %s
			Tuple.Create ("APT1132", "Feature package"),				// ERROR: Feature package '%s' not found
			Tuple.Create ("APT1133", "declaring public resource"),			// Error declaring public resource %s/%s for included package %s
			Tuple.Create ("APT1134", "with value"),					// Error: %s (at '%s' with value '%s')
			Tuple.Create ("APT1135", "is not a single item or a bag"),		// Error: entry %s is not a single item or a bag
			Tuple.Create ("APT1136", "adding span for style tag"),
			Tuple.Create ("APT1137", "parsing XML"),
			Tuple.Create ("APT1138", "access denied"),				// ERROR: '%s' access denied
			Tuple.Create ("APT1139", "included asset path"),			// ERROR: included asset path %s could not be loaded
			Tuple.Create ("APT1140", "is corrupt"),					// ERROR: Resource %s is corrupt
			Tuple.Create ("APT1141", "dump failed because resource"),		// ERROR: dump failed because resource %s [not] found
			Tuple.Create ("APT1142", "not found"),					// ERROR: '%s' not found
			Tuple.Create ("APT1043", "asset directory"),				// ERROR: asset directory '%s' does not exist
			Tuple.Create ("APT1044", "input directory"),				// ERROR: input directory '%s' does not exist
			Tuple.Create ("APT1045", "resource directory"),				// ERROR: resource directory '%s' does not exist
			Tuple.Create ("APT1046", "is not a directory"),				// ERROR: '%s' is not a directory
			Tuple.Create ("APT1047", "opening zip file"),				// error opening zip file %s
			Tuple.Create ("APT1143", "AndroidManifest.xml is corrupt"),
			Tuple.Create ("APT1144", "Invalid file name: must contain only"),
			Tuple.Create ("APT1145", "has no default translation"),
			Tuple.Create ("APT1146", "max res"),
		};
	}
}
