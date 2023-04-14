using System;
using System.Collections.Generic;
using System.IO;

using Xamarin.Android.Utilities;

namespace Xamarin.Debug.Session.Prep;

class LdConfigParser
{
	XamarinLoggingHelper log;

	public LdConfigParser (XamarinLoggingHelper log)
	{
		this.log = log;
	}

	// Format: https://android.googlesource.com/platform/bionic/+/master/linker/ld.config.format.md
	//
	public (List<string> searchPaths, HashSet<string> permittedPaths) Parse (string localLdConfigPath, string deviceBinDirectory, string libDirName)
	{
		var searchPaths = new List<string> ();
		var permittedPaths = new HashSet<string> ();
		bool foundSomeSection = false;
		bool insideMatchingSection = false;
		string normalizedDeviceBinDirectory = Utilities.NormalizeDirectoryPath (deviceBinDirectory);
		string? sectionName = null;

		log.DebugLine ($"Parsing LD config file '{localLdConfigPath}'");
		int lineCounter = 0;
		var namespaces = new List<string> {
			"default"
		};

		foreach (string l in File.ReadLines (localLdConfigPath)) {
			lineCounter++;
			string line = l.Trim ();
			if (line.Length == 0 || line.StartsWith ('#')) {
				continue;
			}

			// The `dir.*` entries are before any section, don't waste time looking for them if we've parsed a section already
			if (!foundSomeSection && sectionName == null) {
				sectionName = GetMatchingDirMapping (normalizedDeviceBinDirectory, line);
				if (sectionName != null) {
					log.DebugLine ($"Found section name on line {lineCounter}: '{sectionName}'");
					continue;
				}
			}

			if (line[0] == '[') {
				foundSomeSection = true;
				insideMatchingSection = String.Compare (line, $"[{sectionName}]", StringComparison.Ordinal) == 0;
				if (insideMatchingSection) {
					log.DebugLine ($"Found section '{sectionName}' start on line {lineCounter}");
				}
			}

			if (!insideMatchingSection) {
				continue;
			}

			if (line.StartsWith ("additional.namespaces", StringComparison.Ordinal) && GetVariableAssignmentParts (line, out string? name, out string? value)) {
				foreach (string v in value!.Split (',')) {
					string nsName = v.Trim ();
					if (nsName.Length == 0) {
						continue;
					}

					log.DebugLine ($"Adding additional namespace '{nsName}'");
					namespaces.Add (nsName);
				}
				continue;
			}

			MaybeAddLibraryPath (searchPaths, permittedPaths, namespaces, line, libDirName);
		}

		return (searchPaths, permittedPaths);

	}

	void MaybeAddLibraryPath (List<string> searchPaths, HashSet<string> permittedPaths, List<string> knownNamespaces, string configLine, string libDirName)
	{
		if (!configLine.StartsWith ("namespace.", StringComparison.Ordinal)) {
			return;
		}

		// not interested in ASAN libraries
		if (configLine.IndexOf (".asan.", StringComparison.Ordinal) > 0) {
			return;
		}

		foreach (string ns in knownNamespaces) {
			if (!GetVariableAssignmentParts (configLine, out string? name, out string? value)) {
				continue;
			}

			string varName = $"namespace.{ns}.search.paths";
			if (String.Compare (varName, name, StringComparison.Ordinal) == 0) {
				AddPath (searchPaths, "search", value!);
				continue;
			}

			varName = $"namespace.{ns}.permitted.paths";
			if (String.Compare (varName, name, StringComparison.Ordinal) == 0) {
				AddPath (permittedPaths, "permitted", value!, checkIfAlreadyAdded: true);
			}
		}

		void AddPath (ICollection<string> list, string which, string value, bool checkIfAlreadyAdded = false)
		{
			string path = Utilities.NormalizeDirectoryPath (value.Replace ("${LIB}", libDirName));

			if (checkIfAlreadyAdded && list.Contains (path)) {
				return;
			}

			log.DebugLine ($"Adding library {which} path: {path}");
			list.Add (path);
		}
	}

	string? GetMatchingDirMapping (string deviceBinDirectory, string configLine)
	{
		const string LinePrefix = "dir.";

		string line = configLine.Trim ();
		if (line.Length == 0 || !line.StartsWith (LinePrefix, StringComparison.Ordinal)) {
			return null;
		}

		if (!GetVariableAssignmentParts (line, out string? name, out string? value)) {
			return null;
		}

		string dirPath = Utilities.NormalizeDirectoryPath (value!);
		if (String.Compare (dirPath, deviceBinDirectory, StringComparison.Ordinal) != 0) {
			return null;
		}

		string ns = name!.Substring (LinePrefix.Length).Trim ();
		if (String.IsNullOrEmpty (ns)) {
			return null;
		}

		return ns;
	}

	bool GetVariableAssignmentParts (string line, out string? name, out string? value)
	{
		name = value = null;

		string[] parts = line.Split ("+=", 2);
		if (parts.Length != 2) {
			parts = line.Split ('=', 2);
			if (parts.Length != 2) {
				return false;
			}
		}

		name = parts[0].Trim ();
		value = parts[1].Trim ();

		return true;
	}
}
