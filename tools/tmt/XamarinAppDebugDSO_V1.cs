using System;
using System.Collections.Generic;

namespace tmt;

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

	protected override string LogTag => "DebugDSO_V1";

	Map? map;
	SortedDictionary<string, MappedType> javaToManaged = new SortedDictionary<string, MappedType> (StringComparer.Ordinal);
	SortedDictionary<string, MappedType> managedToJava = new SortedDictionary<string, MappedType> (StringComparer.Ordinal);

	public override string FormatVersion => "1";
	public override Map Map => map ?? throw new InvalidOperationException ("Data hasn't been loaded yet");

	public XamarinAppDebugDSO_V1 (ManagedTypeResolver managedResolver, AnELF elf)
	: base (managedResolver, elf)
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
		size += GetPaddedSize<uint> (size);   // entry_count
		size += GetPaddedSize<string> (size); // assembly_name (pointer)
		size += GetPaddedSize<string> (size); // data (pointer)
		size += GetPaddedSize<string> (size); // java_to_managed (pointer)
		size += GetPaddedSize<string> (size); // managed_to_java (pointer)

		string filePath = ELF.FilePath;
		(byte[] mapData, _) = ELF.GetData (TypeMapSymbolName);
		if (mapData.Length == 0) {
			Log.Error ($"{filePath} doesn't have a valid '{TypeMapSymbolName}' symbol");
			return false;
		}

		if ((ulong)mapData.Length != size) {
			Log.Error ($"Symbol '{TypeMapSymbolName}' in {filePath} has invalid size. Expected {size}, got {mapData.Length}");
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
		Log.Info ($"  Loading {name} map: {entry_count} {entries}, please wait...");

		ulong size = 0;
		size += GetPaddedSize<string> (size); // from
		size += GetPaddedSize<string> (size); // to

		ulong mapSize = entry_count * size;
		byte[] data = ELF.GetData (pointer, mapSize);

		ulong offset = 0;
		string mapFrom;
		string mapTo;
		for (uint i = 0; i < entry_count; i++) {
			pointer = ReadPointer (data, ref offset);
			if (pointer != 0) {
				mapFrom = ELF.GetASCIIZ (pointer);
			} else {
				mapFrom = $"#{i}";
			}

			pointer = ReadPointer (data, ref offset);
			if (pointer != 0) {
				mapTo = ELF.GetASCIIZ (pointer);
			} else {
				mapTo = $"#{i}";
			}

			addToMap (mapFrom, mapTo);
		}
	}
}
