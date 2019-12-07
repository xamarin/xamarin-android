using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
		};

		static readonly string [] assemblies =
		{
			"Mono.Android.dll",
		};

		// Path where Microsoft.DotNet.ApiCompat nuget package is located
		[Required]
		public string ApiCompatPath { get; set; }

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

		// This Build tasks validates that changes are not breaking Api
		public override bool Execute ()
		{
			Log.LogMessage (MessageImportance.High, $"CheckApiCompatibility for ApiLevel: {ApiLevel}");

			// Check to see if Api has a previous Api defined.
			if (!api_versions.TryGetValue (ApiLevel, out string previousApiLevel)) {
				Log.LogError ($"Please add ApiLevel:{ApiLevel} to the list of supported apis.");
				return !Log.HasLoggedErrors;
			}

			// Get the previous api implementation path by replacing the current api string with the previous one.
			var previousTargetImplementationPath = TargetImplementationPath.Replace (ApiLevel, previousApiLevel);

			// In case previous api is not defined or directory does not exist we can skip the check.
			var validateAgainstPreviousApi = !(string.IsNullOrWhiteSpace (previousApiLevel) || !Directory.Exists (previousTargetImplementationPath));
			if (validateAgainstPreviousApi) {

				// First we check the Api level assembly against the previous api level assembly
				// i.e.: check api breakages using "the just built V2.dll" against "the just built V1.dll"
				ValidateApiCompat (previousTargetImplementationPath, false);

				if (Log.HasLoggedErrors) {
					return !Log.HasLoggedErrors;
				}
			}

			// If Api level is the latest we should also compare it against the reference assembly
			// located on the external folder. (xamarin-android-api-compatibility)
			// i.e.: check apicompat using "the just built V2.dll" against V2.dll located on xamarin-android-api-compatibility repo
			if (ApiLevel == LastStableApiLevel) {

				// Check xamarin-android-api-compatibility reference directory exists
				var referenceContractPath = Path.Combine (ApiCompatibilityPath, "reference");
				if (!Directory.Exists (referenceContractPath)) {
					Log.LogMessage (MessageImportance.High, $"CheckApiCompatibility Warning: Skipping reference contract check.\n{referenceContractPath} does not exist.");
					return !Log.HasLoggedErrors;
				}

				// Before validate, check that zip files were decompressed.
				var zipFiles = Directory.GetFiles (referenceContractPath, "*.zip");
				foreach (var zipFile in zipFiles) {
					using (var zip = ZipArchive.Open (zipFile, FileMode.Open)) {
						zip.ExtractAll (referenceContractPath);
					}
				}

				ValidateApiCompat (referenceContractPath, true);
			}

			return !Log.HasLoggedErrors;
		}

		// Validates Api compatibility between contract (previous version) and implementation (current version)
		// We do that by using Microsoft.DotNet.ApiCompat.dll
		void ValidateApiCompat (string contractPath, bool validateAgainstReference)
		{
			const string ApiCompatTemp = "ApiCompatTemp";

			var apiCompat = Path.Combine (ApiCompatPath, "Microsoft.DotNet.ApiCompat.exe");
			var contractPathDirectory = Path.Combine (contractPath, ApiCompatTemp);
			var targetImplementationPathDirectory = Path.Combine (TargetImplementationPath, ApiCompatTemp);

			try {
				// Copy interesting assemblies to a temp folder.
				// This is done to avoids the Microsoft.DotNet.ApiCompat.exe to analyze unwanted assemblies
				// We need to validate assembly exist in both contract and implementation folders.
				Directory.CreateDirectory (contractPathDirectory);
				Directory.CreateDirectory (targetImplementationPathDirectory);

				foreach (var assemblyToValidate in assemblies) {
					var contractAssembly = Path.Combine (contractPath, assemblyToValidate);
					if (!File.Exists (contractAssembly)) {
						Log.LogMessage ($"Contract assembly {assemblyToValidate} does not exists in the contract path.");
						continue;
					}

					var implementationAssembly = Path.Combine (TargetImplementationPath, assemblyToValidate);
					if (!File.Exists (implementationAssembly)) {
						Log.LogError ($"Implementation assembly {assemblyToValidate} exists in the contract path but not on the implementation folder.");
						return;
					}

					File.Copy (contractAssembly, Path.Combine (contractPathDirectory, assemblyToValidate), true);
					File.Copy (implementationAssembly, Path.Combine (targetImplementationPathDirectory, assemblyToValidate), true);
				}

				using (var genApiProcess = new Process ()) {

					genApiProcess.StartInfo.FileName = apiCompat;
					genApiProcess.StartInfo.Arguments = $"\"{contractPathDirectory}\" -i \"{targetImplementationPathDirectory}\" ";

					// Verify if there is an exclusion list
					var excludeAttributes = Path.Combine (ApiCompatibilityPath, $"api-compat-exclude-attributes.txt");
					if (File.Exists (excludeAttributes)) {
						genApiProcess.StartInfo.Arguments += $"--exclude-attributes {excludeAttributes} ";
					}

					genApiProcess.StartInfo.UseShellExecute = false;
					genApiProcess.StartInfo.CreateNoWindow = true;
					genApiProcess.StartInfo.RedirectStandardOutput = true;

					Log.LogMessage (MessageImportance.High, $"CompatApi command: {genApiProcess.StartInfo.FileName} {genApiProcess.StartInfo.Arguments}");

					// Get api definition for previous Api
					genApiProcess.Start ();
					ValidateIssues (genApiProcess.StandardOutput, validateAgainstReference);
					genApiProcess.WaitForExit ();
				}
			} finally {
				if (Directory.Exists (contractPathDirectory)) {
					Directory.Delete (contractPathDirectory, true);
				}

				if (Directory.Exists (targetImplementationPathDirectory)) {
					Directory.Delete (targetImplementationPathDirectory, true);
				}
			}
		}

		// Validates there is no issue or issues found are acceptable
		void ValidateIssues (StreamReader content, bool validateAgainstReference)
		{
			// Load issues into a dictionary
			var issuesFound = LoadIssues (content);

			Dictionary<string, HashSet<string>> acceptableIssues = null;

			// Verify if there is a file with acceptable issues.
			var acceptableIssuesFile = Path.Combine (ApiCompatibilityPath, $"acceptable-breakages-{ (validateAgainstReference ? "vReference" : ApiLevel) }.txt");
			if (!File.Exists (acceptableIssuesFile)) {

				// If file does not exist but no issues were reported we can return here.
				if (issuesFound == null || issuesFound.Count == 0) {
					return;
				}
			} else {

				// Read and Convert the acceptable issues into a dictionary
				using (var streamReader = new StreamReader (acceptableIssuesFile)) {
					acceptableIssues = LoadIssues (streamReader);
				}
			}

			// Now remove all acceptable issues form the dictionary of issues found.
			var count = 0;
			if (acceptableIssues != null) {
				foreach (var item in acceptableIssues) {
					if (!issuesFound.TryGetValue (item.Key, out HashSet<string> issues)) {
						// we should always be able to find the assembly that is reporting the issues
						Log.LogMessage (MessageImportance.High, $"There is an invalid assembly listed on the acceptable breakages file: {item.Key}");
						count++;
						continue;
					}

					foreach (var issue in item.Value) {
						// we should always be able to remove the issue, if we try to remove an issue that does not exist,
						// it means the acceptable list is incorrect and should be reported.
						if (!issues.Remove (issue)) {
							Log.LogMessage (MessageImportance.High, $"There is an invalid issue listed on the acceptable breakages file: {issue}");
							count++;
						}
					}
				}
			}

			// Any issue that still exist on issues found means it is a new issue and we should report
			foreach (var item in issuesFound) {
				if (item.Value.Count == 0) {
					continue;
				}

				Log.LogMessage (MessageImportance.High, item.Key);
				foreach (var issue in item.Value) {
					Log.LogMessage (MessageImportance.High, issue);
					count++;
				}
			}

			if (count > 0) {
				Log.LogMessage (MessageImportance.High, $"Total Issues: {count}");
				Log.LogError ($"CheckApiCompatibility found nonacceptable Api breakages for ApiLevel: {ApiLevel}.");
			}
		}

		// Converts list of issue into a dictionary
		Dictionary<string, HashSet<string>> LoadIssues (StreamReader content)
		{
			var issues = new Dictionary<string, HashSet<string>> ();
			HashSet<string> currentSet = null;

			while (!content.EndOfStream) {
				var line = content.ReadLine ();

				if (string.IsNullOrWhiteSpace (line) || line.StartsWith ("#")) {
					continue;
				}

				line = line.Trim ();

				// Create hashset per assembly
				if (line.StartsWith ("Compat issues with assembly", StringComparison.InvariantCultureIgnoreCase)) {
					currentSet = new HashSet<string> ();
					issues.Add (line, currentSet);
					continue;
				}

				// end of file
				if (line.StartsWith ("Total Issues:", StringComparison.InvariantCultureIgnoreCase)) {
					break;
				}

				if (currentSet == null) {
					// Hashset should never be null, unless exception file is not defining assembly line.
					Log.LogError ($"Exception report/file should start with: Compat issues with assembly");
					return null;
				}

				// Add rule to hashset
				currentSet.Add (line);
			}

			return issues;
		}
	}
}
