using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Tools.BootstrapTasks
{
	public sealed class CheckApiCompatibility : Task
	{
		// This dictionary holds Api versions
		// key is the Api version
		// value is the previous Api version in relation to the key
		static readonly Dictionary<string, string> api_versions = new Dictionary<string, string> ()
		{
			{ "v4.4", "" },
			{ "v4.4.87", "v4.4" },
			{ "v5.0", "v4.4.87" },
			{ "v5.1", "v5.0" },
			{ "v6.0", "v5.1" },
			{ "v7.0", "v6.0" },
			{ "v7.1", "v7.0" },
			{ "v8.0", "v7.1" },
			{ "v8.1", "v8.0" },
			{ "v9.0", "v8.1" },
			{ "v10.0", "v9.0" },
			{ "v11.0", "v10.0" },
			{ "v12.0", "v11.0" },
			{ "v12.1", "v12.0" },
			{ "v13.0", "v12.1" },
			{ "v14.0", "v13.0" },
			{ "v15.0", "v14.0" },
		};

		static readonly string assemblyToValidate = "Mono.Android.dll";

		static string compatApiCommand = null;

		// Path where Microsoft.DotNet.ApiCompat nuget package is located
		[Required]
		public string ApiCompatPath { get; set; }

		// Path where Microsoft.DotNet.CodeGen nuget package is located
		[Required]
		public string CodeGenPath { get; set; }

		// Api level just built
		[Required]
		public string ApiLevel { get; set; }

		// The last stable api level.
		[Required]
		public string LastStableApiLevel { get; set; }

		// Output Path where the assembly was just built
		[Required]
		public string TargetImplementationPath { get; set; }

		// Path to xamarin-android-api-compatibility folder
		[Required]
		public string ApiCompatibilityPath { get; set; }

		// In case API diffs vary between e.g. Classic MonoAndroid & .NET 6+
		public string TargetFramework { get; set; }

		// This Build tasks validates that changes are not breaking Api
		public override bool Execute ()
		{
			Log.LogMessage (MessageImportance.High, $"CheckApiCompatibility for ApiLevel: {ApiLevel}");
			Log.LogWarning ($"# jonp: TargetFramework={TargetFramework}");

			// Check to see if Api has a previous Api defined.
			if (!api_versions.TryGetValue (ApiLevel, out string previousApiLevel)) {
				LogError ($"Please add ApiLevel:{ApiLevel} to the list of supported apis.");
				return !Log.HasLoggedErrors;
			}

			var implementationPath = new DirectoryInfo (TargetImplementationPath);
			if (!implementationPath.Exists) {
				LogError ($"Implementation path does not exists:'{TargetImplementationPath}'");
				return !Log.HasLoggedErrors;
			}

			TargetImplementationPath = implementationPath.FullName;
			if (TargetImplementationPath.EndsWith ("\\", StringComparison.Ordinal) || TargetImplementationPath.EndsWith ("/", StringComparison.Ordinal)) {
				TargetImplementationPath = TargetImplementationPath.Substring (0, TargetImplementationPath.Length - 1);
			}

			// For non netcoreapp assemblies we should compare against previous version.
			if (TargetImplementationPath.IndexOf ("MonoAndroid", StringComparison.OrdinalIgnoreCase) != -1) {

				// Get the previous api implementation path by replacing the current api string with the previous one.
				var previousTargetImplementationPath = new DirectoryInfo (TargetImplementationPath.Replace (ApiLevel, previousApiLevel));

				// In case previous api is not defined or directory does not exist we can skip the check.
				var validateAgainstPreviousApi = !(string.IsNullOrWhiteSpace (previousApiLevel) || !previousTargetImplementationPath.Exists);
				if (validateAgainstPreviousApi) {

					// First we check the Api level assembly against the previous api level assembly
					// i.e.: check api breakages using "the just built V2.dll" against "the just built V1.dll"
					ValidateApiCompat (previousTargetImplementationPath.FullName, false);

					if (Log.HasLoggedErrors) {
						return !Log.HasLoggedErrors;
					}
				}
			}

			// If Api level is the latest we should also compare it against the reference assembly
			// located on the external folder. (xamarin-android-api-compatibility)
			// i.e.: check apicompat using "the just built V2.dll" against V2.dll located on xamarin-android-api-compatibility repo
			// This condition is also valid for netcoreapp
			if (ApiLevel == LastStableApiLevel) {

				// Check xamarin-android-api-compatibility reference directory exists
				var referenceContractPath = new DirectoryInfo (Path.Combine (ApiCompatibilityPath, "reference", TargetFramework));
				if (!referenceContractPath.Parent.Exists) {
					Log.LogWarning ($"CheckApiCompatibility Warning: Skipping reference contract check.\n{referenceContractPath.Parent.FullName} does not exist.");
					return !Log.HasLoggedErrors;
				}

				// Before validate, check that zip files were decompressed.
				referenceContractPath.Create ();
				var zipFiles = Directory.GetFiles (referenceContractPath.Parent.FullName, "*.zip");
				foreach (var zipFile in zipFiles) {
					var zipDateTime = File.GetLastWriteTimeUtc (zipFile);
					using (var zip = ZipArchive.Open (zipFile, FileMode.Open)) {
						foreach (var entry in zip) {
							var path = Path.Combine (referenceContractPath.FullName, entry.NativeFullName);
							if (!File.Exists (path) || File.GetLastWriteTimeUtc (path) < zipDateTime) {
								Log.LogMessage ($"Extracting: {path}");
								using (var fileStream = File.Create (path)) {
									entry.Extract (fileStream);
								}
							} else {
								Log.LogMessage ($"Skipping, up to date: {path}");
							}
						}
					}
				}

				ValidateApiCompat (referenceContractPath.FullName, true);
			}

			return !Log.HasLoggedErrors;
		}

		// Validates Api compatibility between contract (previous version) and implementation (current version)
		// We do that by using Microsoft.DotNet.ApiCompat.dll
		void ValidateApiCompat (string contractPath, bool validateAgainstReference)
		{
			var contractAssembly = new FileInfo (Path.Combine (contractPath, assemblyToValidate));
			if (!contractAssembly.Exists) {
				LogError ($"Contract assembly {assemblyToValidate} does not exists in the contract path. {contractPath} - {validateAgainstReference}");
				return;
			}

			var implementationAssembly = new FileInfo (Path.Combine (TargetImplementationPath, assemblyToValidate));
			if (!implementationAssembly.Exists) {
				LogError ($"Implementation assembly {assemblyToValidate} exists in the contract path but not on the implementation folder.");
				return;
			}

			for (int i = 0; i < 3; i++) {
				using (var genApiProcess = new Process ()) {

					if (Environment.Version.Major >= 5) {
						var apiCompat = new FileInfo (Path.Combine (ApiCompatPath, "..", "netcoreapp3.1", "Microsoft.DotNet.ApiCompat.dll"));
						genApiProcess.StartInfo.FileName = "dotnet";
						genApiProcess.StartInfo.Arguments = $"\"{apiCompat}\" ";
					} else {
						var apiCompat = new FileInfo (Path.Combine (ApiCompatPath, "Microsoft.DotNet.ApiCompat.exe"));
						genApiProcess.StartInfo.FileName = apiCompat.FullName;
					}

					genApiProcess.StartInfo.Arguments += $"\"{contractAssembly.FullName}\" -i \"{TargetImplementationPath}\" --allow-default-interface-methods ";


					// Verify if there is a file with acceptable issues.
					var acceptableIssuesFiles = new[]{
						Path.Combine (ApiCompatibilityPath, $"acceptable-breakages-{ (validateAgainstReference ? "vReference" : ApiLevel) }-{TargetFramework}.txt"),
						Path.Combine (ApiCompatibilityPath, $"acceptable-breakages-{ (validateAgainstReference ? "vReference" : ApiLevel) }.txt"),
					};
					var acceptableIssuesFile = acceptableIssuesFiles.Select (p => new FileInfo (p))
						.Where (v => v.Exists)
						.FirstOrDefault ();
					if (acceptableIssuesFile != null) {
						genApiProcess.StartInfo.Arguments += $"--baseline \"{acceptableIssuesFile.FullName}\" --validate-baseline ";
					}

					// Verify if there is an exclusion list
					var excludeAttributes = new FileInfo (Path.Combine (ApiCompatibilityPath, $"api-compat-exclude-attributes.txt"));
					if (excludeAttributes.Exists) {
						genApiProcess.StartInfo.Arguments += $"--exclude-attributes \"{excludeAttributes.FullName}\" ";
					}

					genApiProcess.StartInfo.UseShellExecute = false;
					genApiProcess.StartInfo.CreateNoWindow = true;
					genApiProcess.StartInfo.RedirectStandardOutput = true;
					genApiProcess.StartInfo.RedirectStandardError = true;
					genApiProcess.EnableRaisingEvents = true;

					var lines = new List<string> ();
					var processHasCrashed = false;
					void dataReceived (object sender, DataReceivedEventArgs args)
					{
						if (!string.IsNullOrWhiteSpace (args.Data)) {
							lines.Add (args.Data.Trim ());

							if (args.Data.IndexOf ("Native Crash Reporting", StringComparison.Ordinal) != -1) {
								processHasCrashed = true;
							}
						}
					}

					genApiProcess.OutputDataReceived += dataReceived;
					genApiProcess.ErrorDataReceived += dataReceived;

					// Get api definition for previous Api
					compatApiCommand = $"CompatApi command: {genApiProcess.StartInfo.FileName} {genApiProcess.StartInfo.Arguments}";
					Log.LogMessage (MessageImportance.High, compatApiCommand);

					genApiProcess.Start ();
					genApiProcess.BeginOutputReadLine ();
					genApiProcess.BeginErrorReadLine ();

					genApiProcess.WaitForExit ();

					genApiProcess.CancelOutputRead ();
					genApiProcess.CancelErrorRead ();

					if (lines.Count == 0) {
						return;
					}

					if (processHasCrashed) {
						if (i + 1 < 3) {
							Log.LogWarning ($"Process has crashed.");
							Log.LogMessage (MessageImportance.High, string.Join (Environment.NewLine, lines));
							Log.LogWarning ($"We will retry.");
							continue;
						} else {
							LogError ($"Unable to get a valid report. Process has crashed.'{Environment.NewLine}Crash report:{Environment.NewLine}{string.Join (Environment.NewLine, lines)}");
							return;
						}
					}

					// It is expected to have at least one line of output form ApiCompat, if we don't have it, somethign wrong happened.
					if (!lines.Any ()) {
						LogError ($"Unable to run ApiCompat correctly. Argument values may be incorrectly.{Environment.NewLine}{compatApiCommand}");
						return;
					}

					if (lines [0].Equals ("Total issues: 0", StringComparison.OrdinalIgnoreCase)) {
						Log.LogMessage (MessageImportance.High, lines [0]);
						return;
					}

					LogError ($"CheckApiCompatibility found nonacceptable Api breakages for ApiLevel: {ApiLevel}.{Environment.NewLine}{string.Join (Environment.NewLine, lines)}");

					var missingItems = CodeGenDiff.GenerateMissingItems (CodeGenPath, contractAssembly.FullName, implementationAssembly.FullName);
					if (missingItems.Any ()) {
						Log.LogMessage (MessageImportance.High, $"{Environment.NewLine}*** CodeGen missing items***{Environment.NewLine}");
						var indent = 0;
						foreach (var item in missingItems) {
							if (item.StartsWith ("}", StringComparison.Ordinal)) {
								indent--;
							}

							Log.LogMessage (MessageImportance.High, $"{(item.StartsWith ("namespace ", StringComparison.Ordinal) ? Environment.NewLine : string.Empty)}{new string (' ', indent * 2)}{item}");

							if (item.StartsWith ("{", StringComparison.Ordinal)) {
								indent++;
							}
						}

						Log.LogMessage (MessageImportance.High, string.Empty);
					}

					return;
				}
			}
		}

		void LogError (string errorMessage)
		{
			if (!string.IsNullOrWhiteSpace (compatApiCommand)) {
				errorMessage = $"{compatApiCommand}{Environment.NewLine}{errorMessage}";
			}

			Log.LogMessage (MessageImportance.High, errorMessage);
			Log.LogError (errorMessage);
		}
	}
}
