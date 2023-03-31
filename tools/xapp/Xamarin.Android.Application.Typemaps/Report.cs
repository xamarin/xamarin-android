using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application.Typemaps;

class Report
{
	const string FileFieldSeparator      = "\t";
	const string ManagedTypeColumnHeader = "Managed-Type-Name";
	const string JavaTypeColumnHeader    = "Java-Type-Name";
	const string DuplicateColumnHeader   = "Is-Duplicate-Type-Entry?";
	const string GenericColumnHeader     = "Is-Generic-Type?";
	const string MVIDColumnHeader        = "MVID";
	const string TokenIDColumnHeader     = "Token-ID";

	string outputDirectory;
	Regex? filterRegex;
	bool full;
	bool onlyJava;
	bool onlyManaged;
	bool generateFiles;
	ILogger log;

	public Report (ILogger log, string outputDirectory, string filterRegex, bool full, bool onlyJava, bool onlyManaged, bool generateFiles)
	{
		this.log = log;
		this.outputDirectory = outputDirectory;
		this.full = full;
		this.onlyJava = onlyJava;
		this.onlyManaged = onlyManaged;
		this.generateFiles = generateFiles;

		if (filterRegex.Length > 0) {
			this.filterRegex = new Regex (filterRegex, RegexOptions.Compiled);
		}
	}

	public void Generate (ITypemap typemap)
	{
		string baseOutputFile = generateFiles ? TypemapUtilities.GetOutputFileBaseName (outputDirectory, typemap.FormatVersion, typemap.Map.Kind, typemap.Map.Architecture) : String.Empty;
		Action<StreamWriter, MapEntry, bool, bool> fileGenerator;
		Action<MapEntry, bool> consoleGenerator;
		bool filtering = filterRegex != null;

		if (!onlyManaged) {
			typemap.Map.JavaToManaged.Sort ((MapEntry left, MapEntry right) => String.Compare (left.JavaType.Name, right.JavaType.Name, StringComparison.Ordinal));
			if (typemap.Map.Kind == MapKind.Release) {
				fileGenerator = FileGenerateJavaToManagedRelease;
				consoleGenerator = ConsoleGenerateJavaToManagedRelease;
			} else {
				fileGenerator = FileGenerateJavaToManagedDebug;
				consoleGenerator = ConsoleGenerateJavaToManagedDebug;
			}

			Generate (
				filtering ? "Java to Managed" : "Java to Managed output file",
				TypemapUtilities.GetJavaOutputFileName (baseOutputFile, "txt"),
				typemap.Map.JavaToManaged,
				full,
				fileGenerator,
				consoleGenerator
			);
		}

		if (!onlyJava) {
			typemap.Map.ManagedToJava.Sort ((MapEntry left, MapEntry right) => {
				int result = String.Compare (left.ManagedType.AssemblyName, right.ManagedType.AssemblyName, StringComparison.OrdinalIgnoreCase);
				if (result != 0)
					return result;

				return String.Compare (left.ManagedType.TypeName, right.ManagedType.TypeName, StringComparison.Ordinal);
			});

			if (typemap.Map.Kind == MapKind.Release) {
				fileGenerator = FileGenerateManagedToJavaRelease;
				consoleGenerator = ConsoleGenerateManagedToJavaRelease;
			} else {
				fileGenerator = FileGenerateManagedToJavaDebug;
				consoleGenerator = ConsoleGenerateManagedToJavaDebug;
			}

			Generate (
				filtering ? "Managed to Java" : "Managed to Java output file",
				TypemapUtilities.GetManagedOutputFileName (baseOutputFile, "txt"),
				typemap.Map.ManagedToJava,
				full,
				fileGenerator,
				consoleGenerator
			);
		}
	}

	void Generate (string name, string outputFile, List<MapEntry> typemap, bool full, Action<StreamWriter, MapEntry, bool, bool> fileGenerator, Action<MapEntry, bool> consoleGenerator)
	{
		if (generateFiles) {
			log.InfoLine ($"  {name}: {outputFile}");
			Util.CreateFileDirectory (outputFile);
		}

		StreamWriter? sw = null;
		if (generateFiles) {
			sw = new StreamWriter (outputFile, false, new UTF8Encoding (false));
		}

		bool firstMatch = true;
		bool first = true;
		try {
			foreach (MapEntry entry in typemap) {
				if (generateFiles) {
					fileGenerator (sw!, entry, full, first);
					if (first) {
						first = false;
					}
				}

				if (filterRegex == null || !EntryMatches (entry, filterRegex)) {
					continue;
				}

				if (firstMatch) {
					log.InfoLine ();
					log.InfoLine ($"  Matching entries ({name}):");
					firstMatch = false;
				}

				consoleGenerator (entry, full);
			}
		} finally {
			if (sw != null) {
				sw.Flush ();
				sw.Close ();
				sw.Dispose ();
			}
		}
	}

	string GetTokenID (MapEntry entry)
	{
		return $"{entry.ManagedType.TokenID} (0x{entry.ManagedType.TokenID:X08})";
	}

	string GetManagedTypeNameRelease (MapEntry entry)
	{
		return $"{entry.ManagedType.TypeName}, {entry.ManagedType.AssemblyName}";
	}

	string GetManagedTypeNameDebug (MapEntry entry)
	{
		return entry.ManagedType.TypeName;
	}

	const string IgnoredGeneric = "generic, ignored";
	const string Duplicate = "duplicate entry";

	void FileGenerateJavaToManagedDebug (StreamWriter sw, MapEntry entry, bool full, bool firstEntry)
	{
		if (firstEntry) {
			string sep = FileFieldSeparator;
			sw.WriteLine ($"{JavaTypeColumnHeader}{sep}{ManagedTypeColumnHeader}{sep}{DuplicateColumnHeader}");
		}

		WriteLineToFile (sw,
		                 entry.JavaType.Name,
		                 GetManagedTypeNameDebug (entry),
		                 entry.ManagedType.IsDuplicate ? Duplicate : String.Empty
		);
	}

	void ConsoleGenerateJavaToManagedDebug (MapEntry entry, bool full)
	{
		log.InfoLine ($"    {entry.JavaType.Name} -> {entry.ManagedType.TypeName}");
	}

	void FileGenerateJavaToManagedRelease (StreamWriter sw, MapEntry entry, bool full, bool firstEntry)
	{
		string managedTypeName = GetManagedTypeNameRelease (entry);
		string generic = entry.ManagedType.IsGeneric ? IgnoredGeneric : String.Empty;

		if (!full) {
			if (firstEntry) {
				string sep = FileFieldSeparator;
				sw.WriteLine ($"{JavaTypeColumnHeader}{sep}{ManagedTypeColumnHeader}{sep}{GenericColumnHeader}");
			}

			WriteLineToFile (
				sw,
				entry.JavaType.Name,
				managedTypeName,
				generic
			);
			return;
		}

		if (firstEntry) {
			string sep = FileFieldSeparator;
			sw.WriteLine ($"{JavaTypeColumnHeader}{sep}{ManagedTypeColumnHeader}{sep}{GenericColumnHeader}{sep}{MVIDColumnHeader}{sep}{TokenIDColumnHeader}");
		}
		WriteLineToFile (
			sw,
			entry.JavaType.Name,
			managedTypeName,
			generic,
			entry.ManagedType.MVID.ToString (),
			TokenIdToString (entry)
		);
	}

	void ConsoleGenerateJavaToManagedRelease (MapEntry entry, bool full)
	{
		string managedTypeName = GetManagedTypeNameRelease (entry);
		if (!full) {
			string generic;

			if (entry.ManagedType.IsGeneric) {
				generic = $" ({IgnoredGeneric})";
			} else {
				generic = String.Empty;
			}

			log.InfoLine ($"    {entry.JavaType.Name} -> {managedTypeName}{generic}");
			return;
		}

		log.InfoLine ($"    {entry.JavaType.Name} -> {managedTypeName}; MVID: {entry.ManagedType.MVID}; Token ID: {TokenIdToString (entry)}");
	}

	string TokenIdToString (MapEntry entry)
	{
		if (entry.ManagedType.IsGeneric) {
			return "0 (0x00000000)";
		} else {
			return GetTokenID (entry);
		}
	}

	void FileGenerateManagedToJavaDebug (StreamWriter sw, MapEntry entry, bool full, bool firstEntry)
	{
		if (firstEntry) {
			string sep = FileFieldSeparator;
			sw.WriteLine ($"{ManagedTypeColumnHeader}{sep}{JavaTypeColumnHeader}{sep}{DuplicateColumnHeader}");
		}

		WriteLineToFile (sw,
		                 GetManagedTypeNameDebug (entry),
		                 entry.JavaType.Name,
		                 entry.ManagedType.IsDuplicate ? Duplicate : String.Empty
		);
	}

	void ConsoleGenerateManagedToJavaDebug (MapEntry entry, bool full)
	{
		log.InfoLine ($"    {GetManagedTypeNameDebug (entry)} -> {entry.JavaType.Name}{GetAdditionalInfo (entry)}");
	}

	void FileGenerateManagedToJavaRelease (StreamWriter sw, MapEntry entry, bool full, bool firstEntry)
	{
		string managedTypeName = GetManagedTypeNameRelease (entry);
		string duplicate = entry.ManagedType.IsDuplicate ? Duplicate : "         ";
		string generic = entry.ManagedType.IsGeneric ? IgnoredGeneric : "         ";

		if (full) {
			if (firstEntry) {
				string sep = FileFieldSeparator;
				sw.WriteLine ($"{ManagedTypeColumnHeader}{sep}{JavaTypeColumnHeader}{sep}{GenericColumnHeader}{sep}{DuplicateColumnHeader}{sep}{MVIDColumnHeader}{sep}{TokenIDColumnHeader}");
			}
			WriteLineToFile (
				sw,
				managedTypeName,
				entry.JavaType.Name,
				generic,
				duplicate,
				entry.ManagedType.MVID.ToString (),
				GetTokenID (entry)
			);
		} else {
			if (firstEntry) {
				string sep = FileFieldSeparator;
				sw.WriteLine ($"{ManagedTypeColumnHeader}{sep}{JavaTypeColumnHeader}{sep}{GenericColumnHeader}{sep}{DuplicateColumnHeader}");
			}
			WriteLineToFile (
				sw,
				managedTypeName,
				entry.JavaType.Name,
				generic,
				duplicate
			);
		}
	}

	void ConsoleGenerateManagedToJavaRelease (MapEntry entry, bool full)
	{
		if (!full) {
			log.InfoLine ($"    {GetManagedTypeNameDebug (entry)} -> {entry.JavaType.Name}{GetAdditionalInfo (entry)}");
		} else {
			log.InfoLine ($"    {GetManagedTypeNameDebug (entry)}; MVID: {entry.ManagedType.MVID}; Token ID: {TokenIdToString (entry)} -> {entry.JavaType.Name}{GetAdditionalInfo (entry)}");
		}
	}

	string GetAdditionalInfo (MapEntry entry)
	{
		var status = new List<string> ();
		if (entry.ManagedType.IsGeneric) {
			status.Add (IgnoredGeneric);
		}

		if (entry.ManagedType.IsDuplicate) {
			status.Add (Duplicate);
		}

		if (status.Count > 0) {
			return " (" + String.Join ("; ", status) + ")";
		}

		return String.Empty;
	}

	bool EntryMatches (MapEntry entry, Regex regex)
	{
		Match match = regex.Match (entry.JavaType.Name);
		if (match.Success) {
			return true;
		}

		string managedName;
		if (entry.ManagedType.AssemblyName.Length > 0) {
			managedName = $"{entry.ManagedType.TypeName}, {entry.ManagedType.AssemblyName}";
		} else {
			managedName = entry.ManagedType.TypeName;
		}
		match = regex.Match (managedName);

		return match.Success;
	}

	void WriteLineToFile (StreamWriter sw, params string[] fields)
	{
		if (fields.Length == 0) {
			sw.WriteLine ();
			return;
		}

		for (int i = 0; i < fields.Length; i++) {
			if (i > 0)
				sw.Write (FileFieldSeparator);
			sw.Write (fields [i]);
		}
		sw.WriteLine ();
	}
}
