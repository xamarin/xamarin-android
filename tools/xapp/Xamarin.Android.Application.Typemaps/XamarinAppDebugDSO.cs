using System;
using System.Collections.Generic;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application.Typemaps;

class XamarinAppDebugDSO : XamarinAppDSO
{
	XamarinAppDebugDSO_Version? xapp;

	public override string FormatVersion => xapp?.FormatVersion ?? "0";
	public override string Description => xapp?.Description ?? "Xamarin App Debug DSO Forwarder";
	public override Map Map => XAPP.Map;

	XamarinAppDebugDSO_Version XAPP => xapp ?? throw new InvalidOperationException ("Format implementation not found");

	public XamarinAppDebugDSO (ILogger log, ManagedTypeResolver managedResolver, string fullPath)
		: base (log, managedResolver, fullPath)
	{}

	public override bool CanLoad (AnELF elf)
	{
		xapp = null;
		ulong format_tag = 0;
		if (elf.HasSymbol (Constants.SymbolNames.FormatTag))
			format_tag = elf.GetUInt64 (Constants.SymbolNames.FormatTag);

		XamarinAppDebugDSO_Version? reader = null;
		switch (format_tag) {
			case 0:
			case Constants.FormatTag_V1:
				format_tag = 1;
				reader = new XamarinAppDebugDSO_V1 (Log, ManagedResolver, elf);
				break;

			default:
				Log.ErrorLine ($"{elf.FilePath} format (0x{format_tag:X}) is not supported by this version of xapp");
				return false;
		}

		if (reader == null || !reader.CanLoad (elf)) {
			return false;
		}

		xapp = reader;
		return true;
	}

	public override bool Load (string outputDirectory, bool generateFiles)
	{
		return XAPP.Load (outputDirectory, generateFiles);
	}
}

abstract class XamarinAppDebugDSO_Version : XamarinAppDSO
{
	public override string Description => "Xamarin App Debug DSO";

	protected XamarinAppDebugDSO_Version (ILogger log, ManagedTypeResolver managedResolver, AnELF elf)
		: base (log, managedResolver, elf)
	{}

	protected Map MakeMap (List<MapEntry> managedToJava, List<MapEntry> javaToManaged)
	{
		return new Map (MapKind.Debug, MapArchitecture, managedToJava, javaToManaged, FormatVersion);
	}

	public override bool Load (string outputDirectory, bool generateFiles)
	{
		if (!LoadMaps ()) {
			return false;
		}

		return Convert ();
	}

	protected abstract bool LoadMaps ();
	protected abstract bool Convert ();
}

class XamarinAppDebugDSO_V1 : XamarinAppDebugDSO_Version
{
	sealed class MappedType
	{
		public string TargetType;
		public ulong  DuplicateCount = 0;

		public MappedType (string targetType)
		{
			TargetType = targetType;
		}
	}

	const string TypeMapSymbolName = "type_map";

	Map? map;
	SortedDictionary<string, MappedType> javaToManaged = new SortedDictionary<string, MappedType> (StringComparer.Ordinal);
	SortedDictionary<string, MappedType> managedToJava = new SortedDictionary<string, MappedType> (StringComparer.Ordinal);

	public override string FormatVersion => "1";
	public override Map Map => map ?? throw new InvalidOperationException ("Data hasn't been loaded yet");

	public XamarinAppDebugDSO_V1 (ILogger log, ManagedTypeResolver managedResolver, AnELF elf)
		: base (log, managedResolver, elf)
	{}


	public override bool CanLoad (AnELF elf)
	{
		return HasSymbol (elf, TypeMapSymbolName);
	}

	protected override bool Convert ()
	{
		try {
			DoConvert ();
		} catch (Exception ex) {
			Log.ExceptionError ($"{Description}: failed to convert loaded maps to common format", ex);
			return false;
		}
		return true;
	}

	void DoConvert ()
	{
		var managed = new List<MapEntry> ();
		foreach (var kvp in managedToJava) {
			string managedName = kvp.Key;
			MappedType javaType = kvp.Value;

			managed.Add (
				new MapEntry (
					new MapManagedType (managedName) { IsDuplicate = javaType.DuplicateCount > 0 },
					new MapJavaType (javaType.TargetType)
				)
			);
		}

		var java = new List<MapEntry> ();
		foreach (var kvp in javaToManaged) {
			string javaName = kvp.Key;
			MappedType managedType = kvp.Value;

			java.Add (
				new MapEntry (
					new MapManagedType (managedType.TargetType) { IsDuplicate = managedType.DuplicateCount > 0 },
					new MapJavaType (javaName)
				)
			);
		}

		map = MakeMap (managed, java);
	}

	protected override bool LoadMaps ()
	{
		try {
			return DoLoadMaps ();
		} catch (Exception ex) {
			Log.ExceptionError ($"{Description}: failed to load maps", ex);
			return false;
		}
	}

	bool DoLoadMaps ()
	{
		// MUST be kept in sync with: src/monodroid/jni/xamarin-app.hh (struct TypeMap)
		ulong size = 0;
		size += ELF.GetPaddedSize<uint> (size);   // entry_count
		size += ELF.GetPaddedSize<string> (size); // assembly_name (pointer)
		size += ELF.GetPaddedSize<string> (size); // data (pointer)
		size += ELF.GetPaddedSize<string> (size); // java_to_managed (pointer)
		size += ELF.GetPaddedSize<string> (size); // managed_to_java (pointer)

		string filePath = ELF.FilePath;
		byte[] mapData = ELF.GetData (TypeMapSymbolName);
		if (mapData.Length == 0) {
			Log.ErrorLine ($"{filePath} doesn't have a valid '{TypeMapSymbolName}' symbol");
			return false;
		}

		if ((ulong)mapData.Length != size) {
			Log.ErrorLine ($"Symbol '{TypeMapSymbolName}' in {filePath} has invalid size. Expected {size}, got {mapData.Length}");
			return false;
		}

		ulong offset = 0;
		uint entry_count = ReadUInt32 (mapData, ref offset);

		ReadPointer (mapData, ref offset); // assembly_name, unused in Debug mode
		ReadPointer (mapData, ref offset); // data, unused in Debug mode

		ulong pointer = ReadPointer (mapData, ref offset); // java_to_managed
		LoadMap ("Java to Managed", pointer, entry_count, AddJavaToManaged);

		pointer = ReadPointer (mapData, ref offset); // managed_to_java
		LoadMap ("Managed to Java", pointer, entry_count, AddManagedToJava);

		return true;
	}

	void AddManagedToJava (string mapFrom, string mapTo)
	{
		if (managedToJava.TryGetValue (mapFrom, out MappedType? entry)) {
			entry.DuplicateCount++;
			return;
		}

		managedToJava.Add (mapFrom, new MappedType (mapTo));
	}

	void AddJavaToManaged (string mapFrom, string mapTo)
	{
		if (javaToManaged.TryGetValue (mapFrom, out MappedType? entry)) {
			entry.DuplicateCount++;
			return;
		}

		javaToManaged.Add (mapFrom, new MappedType (mapTo));
	}

	void LoadMap (string name, ulong pointer, uint entry_count, Action<string, string> addToMap)
	{
		string entries = entry_count == 1 ? "entry" : "entries";
		Log.InfoLine ($"  Loading {name} map: {entry_count} {entries}, please wait...");

		ulong size = 0;
		size += ELF.GetPaddedSize<string> (size); // from
		size += ELF.GetPaddedSize<string> (size); // to

		ulong mapSize = entry_count * size;
		byte[] data = ELF.GetData (pointer, mapSize);

		ulong offset = 0;
		string mapFrom;
		string mapTo;
		for (uint i = 0; i < entry_count; i++) {
			pointer = ReadPointer (data, ref offset);
			if (pointer != 0) {
				mapFrom = ELF.GetASCIIZ (pointer) ?? Constants.UnableToLoadDataForPointer;
			} else {
				mapFrom = $"#{i}";
			}

			pointer = ReadPointer (data, ref offset);
			if (pointer != 0) {
				mapTo = ELF.GetASCIIZ (pointer) ?? Constants.UnableToLoadDataForPointer;
			} else {
				mapTo = $"#{i}";
			}

			addToMap (mapFrom, mapTo);
		}
	}
}
