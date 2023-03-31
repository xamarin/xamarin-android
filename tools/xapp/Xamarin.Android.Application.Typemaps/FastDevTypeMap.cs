using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application.Typemaps;

class FastDevTypeMap : ITypemap
{
	FastDevTypeMap_Version? fdtm;

	FastDevTypeMap_Version FDTM => fdtm ?? throw new InvalidOperationException ("Format implementation not found");
	ILogger log;

	public MapArchitecture MapArchitecture => MapArchitecture.FastDev;
	public string Description              => fdtm?.Description ?? "FastDev typemap";
	public string FormatVersion            => fdtm?.FormatVersion ?? "0";
	public Map Map                         => FDTM.Map;
	public string FullPath                 => FDTM.FullPath;

	public FastDevTypeMap (ILogger log)
	{
		this.log = log;
	}

	public bool CanLoad (Stream stream, string filePath)
	{
		FastDevTypeMap_Version? reader = new FastDevTypeMap_V2 (log, stream, filePath);
		if (!reader.CanLoad (stream, filePath)) {
			reader = null;
		}

		if (reader == null) {
			log.ErrorLine ($"{filePath} format is not supported by this version of TMT");
			return false;
		}

		fdtm = reader;
		return true;
	}

	public bool Load (string outputDirectory, bool generateFiles)
	{
		return FDTM.Load (outputDirectory, generateFiles);
	}
}

abstract class FastDevTypeMap_Version : ITypemap
{
	// Corresponds to the `MODULE_INDEX_MAGIC` constant in src/monodroid/xamarin-app.hh
	protected const UInt32 ModuleIndexMagic = 0x49544158; // 'XATI', little-endian

	// Corresponds to the `MODULE_MAGIC_NAMES` constant in src/monodroid/xamarin-app.hh
	protected const UInt32 ModuleNamesMagic = 0x53544158; // 'XATS', little-endian

	public MapArchitecture MapArchitecture => MapArchitecture.FastDev;
	public string Description              => "FastDev typemap";
	public string FormatVersion            => SupportedFormat.ToString ();
	public string FullPath                 { get; }

	protected ILogger Log                  { get; }
	protected Stream Input                 { get; }
	protected UInt32 Magic                 { get; private set; } = 0;

	protected abstract UInt32 SupportedFormat { get; }
	public abstract Map Map                   { get; }

	public abstract bool Load (string outputDirectory, bool generateFiles);

	protected FastDevTypeMap_Version (ILogger log, Stream stream, string fullPath)
	{
		Log = log;
		Input = stream;
		FullPath = fullPath;
	}

	public bool CanLoad (Stream stream, string filePath)
	{
		UInt32 expectedMagic = 0;
		if (!GetExpectedMagic (filePath, ref expectedMagic)) {
			return false;
		}

		stream.Seek (0, SeekOrigin.Begin);
		using (var br = new BinaryReader (stream, Encoding.UTF8, leaveOpen: true)) {
			if (br.ReadUInt32 () != expectedMagic) {
				return false;
			}

			if (br.ReadUInt32 () != SupportedFormat) {
				return false;
			}

			Magic = expectedMagic;
		}

		return true;
	}

	protected bool GetExpectedMagic (string filePath, ref UInt32 magic)
	{
		string ext = Path.GetExtension (filePath);

		if (String.Compare (ext, ".index", StringComparison.OrdinalIgnoreCase) == 0) {
			magic = ModuleIndexMagic;
		} else if (String.Compare (ext, ".typemap", StringComparison.OrdinalIgnoreCase) == 0) {
			magic = ModuleNamesMagic;
		} else {
			return false;
		}

		return true;
	}

	protected bool EnsureValidModule (BinaryReader br, string filePath, bool primaryFile)
	{
		UInt32 magic = br.ReadUInt32 ();
		if (magic != ModuleNamesMagic) {
			string message = $"FastDev module file '{filePath}' is not valid (wrong magic number: expected 0x{ModuleNamesMagic:x}, got 0x{magic:x})";
			if (primaryFile) {
				throw new InvalidOperationException (message); // Should "never" happen
			}
			Log.WarningLine (message);
			return false;
		}

		UInt32 formatVersion = br.ReadUInt32 ();
		if (formatVersion != SupportedFormat) {
			string message = $"FastDev module file '{filePath}' uses unsupported format version ({formatVersion})";
			if (primaryFile) {
				throw new InvalidOperationException (message); // Should "never" happen
			}
			Log.WarningLine (message);
			return false;
		}

		return true;
	}
}

class FastDevTypeMap_V2 : FastDevTypeMap_Version
{
	const uint InvalidJavaToManagedMappingIndex = UInt32.MaxValue;

	sealed class Entry
	{
		public string TypeName     = String.Empty;
		public string AssemblyName = String.Empty;
		public string SourceFile   = String.Empty;
		public UInt32 MappedTypeIndex;
	}

	Map? map;
	List<MapEntry> managedToJava;
	List<MapEntry> javaToManaged;
	HashSet<string> managedToJavaUsed;
	HashSet<string> javaToManagedUsed;

	public override Map Map                   => map ?? throw new InvalidOperationException ("Data hasn't been loaded yet");
	protected override UInt32 SupportedFormat => 2;

	public FastDevTypeMap_V2 (ILogger log, Stream stream, string fullPath)
		: base (log, stream, fullPath)
	{
		managedToJava = new List<MapEntry> ();
		javaToManaged = new List<MapEntry> ();

		managedToJavaUsed = new HashSet<string> (StringComparer.Ordinal);
		javaToManagedUsed = new HashSet<string> (StringComparer.Ordinal);
	}

	public override bool Load (string outputDirectory, bool generateFiles)
	{
		try {
			if (!DoLoad (outputDirectory, generateFiles)) {
				return false;
			}

			map = new Map (MapKind.Debug, MapArchitecture.FastDev, managedToJava, javaToManaged, FormatVersion);
			return true;
		} catch (Exception ex) {
			Log.ExceptionError ($"{Description}: failed to load maps", ex);
			return false;
		}
	}

	void AddToMap (List<Entry> javaToManagedSource, List<Entry> managedToJavaSource)
	{
		foreach (Entry entry in javaToManagedSource) {
			Entry managedEntry;

			if (entry.MappedTypeIndex == InvalidJavaToManagedMappingIndex) {
				Log.DebugLine ($"Java-to-managed: entry {entry.TypeName} marked as to be ignored");
				managedEntry = new Entry {
					AssemblyName = entry.AssemblyName,
					TypeName = "[Ignored]",
				};
			} else {
				managedEntry = managedToJavaSource[(int)entry.MappedTypeIndex];
			}

			if (AlreadyUsed (entry, managedEntry, javaToManagedUsed)) {
				Log.DebugLine ($"Java-to-managed: skipping duplicate {entry.TypeName} -> {managedEntry.TypeName}");
				continue;
			}

			CreateAndAdd (entry, managedEntry, javaToManaged);
		}

		foreach (Entry entry in managedToJavaSource) {
			Entry javaEntry = javaToManagedSource[(int)entry.MappedTypeIndex];
			if (AlreadyUsed (entry, javaEntry, managedToJavaUsed)) {
				Log.InfoLine ($"Managed-to-java: skipping duplicate {entry.TypeName} -> {javaEntry.TypeName}");
				continue;
			}

			CreateAndAdd (javaEntry, entry, managedToJava);
		}

		bool AlreadyUsed (Entry from, Entry to, HashSet<string> cache)
		{
			string key = $"{from.TypeName}/{to.TypeName}";
			if (cache.Contains (key)) {
				return true;
			}

			cache.Add (key);
			return false;
		}

		void CreateAndAdd (Entry java, Entry managed, List<MapEntry> list)
		{
			list.Add (
				new MapEntry (
					new MapManagedType (managed.TypeName, managed.SourceFile),
					new MapJavaType (java.TypeName, java.SourceFile)
				)
			);
		}
	}

	bool DoLoad (string outputDirectory, bool generateFiles)
	{
		switch (Magic) {
			case ModuleIndexMagic:
				return LoadIndex ();

			case ModuleNamesMagic:
				return LoadModule (Input, FullPath, primaryFile: true);

			default:
				Log.ErrorLine ($"File {FullPath} has an usupported magic number ({Magic})");
				return false;
		}
	}

	int FindFirstNUL (byte[] data)
	{
		for (int i = 0; i < data.Length; i++) {
			if (data [i] == 0) {
				return i;
			}
		}

		return -1;
	}

	bool GetNonEmptyString (byte[] data, string filePath, int idx, ref string result)
	{
		int nulPosition = FindFirstNUL (data);

		if (nulPosition < 0) {
			Log.ErrorLine ($"{filePath} entry at index {idx} is malformed - no terminating NUL");
			return false;
		}

		if (nulPosition == 0) {
			Log.ErrorLine ($"{filePath} entry at index {idx} is malformed - empty file name");
			return false;
		}

		result = Encoding.UTF8.GetString (data, 0, nulPosition);
		return true;
	}

	bool LoadIndex ()
	{
		string dir = Path.GetDirectoryName (FullPath) ?? String.Empty;

		Input.Seek (0, SeekOrigin.Begin);
		using (var br = new BinaryReader (Input, Encoding.UTF8, leaveOpen: true)) {
			br.ReadUInt32 (); // magic
			br.ReadUInt32 (); // format version

			UInt32 entryCount = br.ReadUInt32 ();
			if (entryCount == 0) {
				Log.ErrorLine ($"FastDev index {FullPath} has no entries");
				return false;
			}

			UInt32 filenameWidth = br.ReadUInt32 ();
			if (filenameWidth == 0) {
				Log.ErrorLine ($"FastDev index indicates file name width is 0");
				return false;
			}

			bool somethingFailed = false;
			for (UInt32 idx = 0; idx < entryCount; idx++) {
				string name = String.Empty;

				if (!GetNonEmptyString (br.ReadBytes ((int)filenameWidth), FullPath, (int)idx, ref name)) {
					somethingFailed = true;
					continue;
				}

				if (!LoadModule (Path.Combine (dir, name), primaryFile: false)) {
					somethingFailed = true;
				}
			}

			return !somethingFailed;
		}
	}

	bool LoadModule (string filePath, bool primaryFile)
	{
		if (!File.Exists (filePath)) {
			Log.ErrorLine ($"Module {filePath} not found");
			return false;
		}

		using (var fs = File.Open (filePath, FileMode.Open, FileAccess.Read)) {
			return LoadModule (fs, filePath, primaryFile);
		}
	}

	bool LoadModule (Stream stream, string filePath, bool primaryFile)
	{
		Log.DebugLine ($"Loading FastDev typemap from module: {filePath}");
		stream.Seek (0, SeekOrigin.Begin);

		string assemblyName = String.Empty;
		var javaToManaged = new List<Entry> ();
		var managedToJava = new List<Entry> ();
		using (var br = new BinaryReader (stream, Encoding.UTF8, leaveOpen: true)) {
			if (!EnsureValidModule (br, filePath, primaryFile)) {
				return false;
			}

			UInt32 entryCount = br.ReadUInt32 ();
			if (entryCount == 0) {
				Log.WarningLine ($"FastDev typemap module file {filePath} has no entries");
				return false;
			}

			UInt32 javaNameWidth = br.ReadUInt32 ();
			if (javaNameWidth == 0) {
				Log.ErrorLine ($"FastDev module {filePath} indicates Java type name width is 0");
				return false;
			}

			UInt32 managedNameWidth = br.ReadUInt32 ();
			if (managedNameWidth == 0) {
				Log.ErrorLine ($"FastDev module {filePath} indicates managed type name width is 0");
				return false;
			}

			UInt32 assemblyNameSize = br.ReadUInt32 ();
			if (assemblyNameSize == 0) {
				Log.ErrorLine ($"FastDev module {filePath} indicates assembly name length is 0");
				return false;
			}

			byte[] data = Util.BytePool.Rent ((int)assemblyNameSize);
			int read = br.Read (data, 0, (int)assemblyNameSize);
			if (read != (int)assemblyNameSize) {
				Log.ErrorLine ($"FastDev typemap module file {filePath} is too short: not enough bytes to read assembly name");
				Util.BytePool.Return (data);
				return false;
			}
			assemblyName = Encoding.UTF8.GetString (data, 0, read);
			Util.BytePool.Return (data);

			if (!LoadModuleTable (br, assemblyName, filePath, javaToManaged, entryCount, javaNameWidth)) {
				Log.ErrorLine ($"Failed to read java-to-managed table from module {filePath}");
				return false;
			}

			if (!LoadModuleTable (br, assemblyName, filePath, managedToJava, entryCount, managedNameWidth)) {
				Log.ErrorLine ($"Failed to read managed-to-java table from module {filePath}");
				return false;
			}

			AddToMap (javaToManaged, managedToJava);
		}

		return true;
	}

	bool LoadModuleTable (BinaryReader br, string assemblyName, string filePath, List<Entry> table, UInt32 entryCount, UInt32 nameWidth)
	{
		byte[] data = Util.BytePool.Rent ((int)nameWidth);
		bool somethingFailed = false;
		for (UInt32 i = 0; i < entryCount; i++) {
			int read = br.Read (data, 0, (int)nameWidth);
			if (read != (int)nameWidth) {
				Log.ErrorLine ($"Error reading module {filePath} at index {i}: missing {nameWidth - read} bytes");
				Util.BytePool.Return (data);
				return false;
			}

			string typeName = String.Empty;
			if (!GetNonEmptyString (data, filePath, (int)i, ref typeName)) {
				somethingFailed = true;
			}

			UInt32 mappedTypeIndex = br.ReadUInt32 ();
			table.Add (
				new Entry {
					AssemblyName    = assemblyName,
					TypeName        = typeName,
					MappedTypeIndex = mappedTypeIndex,
				}
			);
		}

		Util.BytePool.Return (data);
		return !somethingFailed;
	}
}
