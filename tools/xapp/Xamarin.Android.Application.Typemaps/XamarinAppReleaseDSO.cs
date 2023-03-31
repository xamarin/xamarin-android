using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application.Typemaps;

class XamarinAppReleaseDSO : XamarinAppDSO
{
	XamarinAppReleaseDSO_Version? xapp;

	public override string FormatVersion => xapp?.FormatVersion ?? "0";
	public override string Description => xapp?.Description ?? "Xamarin App Release DSO Forwarder";
	public override Map Map => XAPP.Map;

	XamarinAppReleaseDSO_Version XAPP => xapp ?? throw new InvalidOperationException ("Format implementation not found");

	public XamarinAppReleaseDSO (ILogger log, ManagedTypeResolver managedResolver, string fullPath)
		: base (log, managedResolver, fullPath)
	{}

	protected XamarinAppReleaseDSO (ILogger log, ManagedTypeResolver managedResolver, AnELF elf)
		: base (log, managedResolver, elf)
	{}

	public override bool CanLoad (AnELF elf)
	{
		Log.DebugLine ($"Checking if {elf.FilePath} is a Release DSO");

		xapp = null;
		ulong format_tag = 0;
		if (elf.HasSymbol (Constants.SymbolNames.FormatTag))
			format_tag = elf.GetUInt64 (Constants.SymbolNames.FormatTag);

		XamarinAppReleaseDSO_Version? reader = null;
		switch (format_tag) {
			case 0:
			case Constants.FormatTag_V1:
				format_tag = 1;
				reader = new XamarinAppReleaseDSO_V1 (Log, ManagedResolver, elf);
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

abstract class XamarinAppReleaseDSO_Version : XamarinAppDSO
{
	public override string Description => "Xamarin App Release DSO";

	protected XamarinAppReleaseDSO_Version (ILogger log, ManagedTypeResolver managedResolver, AnELF elf)
		: base (log, managedResolver, elf)
	{}

	protected Map MakeMap (List<MapEntry> managedToJava, List<MapEntry> javaToManaged)
	{
		return new Map (MapKind.Release, MapArchitecture, managedToJava, javaToManaged, FormatVersion);
	}

	public override bool Load (string outputDirectory, bool generateFiles)
	{
		if (!LoadMaps ()) {
			return false;
		}

		if (generateFiles) {
			string rawOutputFile = TypemapUtilities.GetOutputFileBaseName (outputDirectory, FormatVersion, MapKind.Release, MapArchitecture);
			SaveRaw (rawOutputFile, "raw");
		}

		return Convert ();
	}

	protected abstract bool LoadMaps ();
	protected abstract bool Convert ();
	protected abstract void SaveRaw (string baseOutputFilePath, string extension);
}

class XamarinAppReleaseDSO_V1 : XamarinAppReleaseDSO_Version
{
	// Field names correspond to: src/monodroid/jni/xamarin-app.hh (struct TypeMapModuleEntry)
	sealed class TypeMapModuleEntry
	{
		public uint type_token_id;
		public uint java_map_index;
	}

	// Field names correspond to: src/monodroid/jni/xamarin-app.hh (struct TypeMapModule)
	sealed class TypeMapModule
	{
		public Guid module_uuid;
		public uint entry_count;
		public uint duplicate_count;
		public List<TypeMapModuleEntry>? map;
		public List<TypeMapModuleEntry>? duplicate_map;
		public string assembly_name = String.Empty;

		// These three aren't used, listed for completeness
		public readonly object? image = null;
		public readonly uint java_name_width = 0;
		public readonly byte[]? java_map = null;
	}

	// Field names correspond to: src/monodroid/jni/xamarin-app.hh (struct TypeMapJava)
	sealed class TypeMapJava
	{
		public uint module_index;
		public uint type_token_id;
		public string java_name = String.Empty;
	}

	const string MapModulesSymbolName = "map_modules";
	const string ModuleCountSymbolName = "map_module_count";
	const string JavaTypeCountSymbolName = "java_type_count";
	const string JavaNameWidthSymbolName = "java_name_width";
	const string MapJavaSymbolName = "map_java";

	Map? map;
	List<TypeMapModule>? modules;
	List<TypeMapJava>? javaTypes;

	public override string FormatVersion => "1";
	public override Map Map => map ?? throw new InvalidOperationException ("Data hasn't been loaded yet");

	public XamarinAppReleaseDSO_V1 (ILogger log, ManagedTypeResolver managedResolver, AnELF elf)
		: base (log, managedResolver, elf)
	{}

	public override bool CanLoad (AnELF elf)
	{
		return
			HasSymbol (elf, MapModulesSymbolName) &&
			HasSymbol (elf, ModuleCountSymbolName) &&
			HasSymbol (elf, JavaTypeCountSymbolName) &&
			HasSymbol (elf, JavaNameWidthSymbolName) &&
			HasSymbol (elf, MapJavaSymbolName);
	}

	protected override bool LoadMaps ()
	{
		try {
			string filePath = ELF.FilePath;
			modules = LoadMapModules (filePath);
			javaTypes = LoadJavaTypes (filePath);
			return true;
		} catch (Exception ex) {
			Log.ExceptionError ($"{Description}: failed to load maps", ex);
			return false;
		}
	}

	protected override void SaveRaw (string baseOutputFilePath, string extension)
	{
		const string indent = "\t";

		if (modules == null || javaTypes == null) {
			Log.WarningLine ($"{Description}: cannot save raw report, no data");
			return;
		}

		string outputFilePath = TypemapUtilities.GetManagedOutputFileName (baseOutputFilePath, extension);
		Util.CreateFileDirectory (outputFilePath);
		using (var sw = new StreamWriter (outputFilePath, false, new UTF8Encoding (false))) {
			sw.WriteLine ("TYPE_TOKEN_DECIMAL (TYPE_TOKEN_HEXADECIMAL)\tJAVA_MAP_INDEX");
			uint index = 0;
			foreach (TypeMapModule module in modules) {
				sw.WriteLine ();
				sw.WriteLine ($"Module {index++:D04}: {module.assembly_name} (MVID: {module.module_uuid}; entries: {module.entry_count}; duplicates: {module.duplicate_count})");
				if (module.map == null) {
					sw.WriteLine ($"{indent}no map");
				} else {
					WriteManagedMap ("map", module.map, sw);
				}

				if (module.duplicate_map == null) {
					if (module.duplicate_count > 0)
						sw.WriteLine ($"{indent}no duplicate map, but there should be {module.duplicate_count} entries");
					else
						sw.WriteLine ($"{indent}no duplicates");
					continue;
				}

				WriteManagedMap ("duplicate map", module.duplicate_map, sw);
			}
			sw.Flush ();
		}

		outputFilePath = TypemapUtilities.GetJavaOutputFileName (baseOutputFilePath, extension);
		using (var sw = new StreamWriter (outputFilePath, false, new UTF8Encoding (false))) {
			sw.WriteLine ("MANAGED_MODULE_INDEX\tTYPE_TOKEN_DECIMAL (TYPE_TOKEN_HEXADECIMAL)\tJAVA_TYPE_NAME");

			foreach (TypeMapJava tmj in javaTypes) {
				sw.WriteLine ($"{indent}{tmj.module_index}\t{tmj.type_token_id:D08} ({tmj.type_token_id:X08})\t{tmj.java_name}");
			}
			sw.Flush ();
		}

		void WriteManagedMap (string name, List<TypeMapModuleEntry> map, StreamWriter sw)
		{
			sw.WriteLine ($"{indent}{name}:");
			foreach (TypeMapModuleEntry entry in map) {
				sw.WriteLine ($"{indent}{indent}{entry.type_token_id} ({entry.type_token_id:X08})\t{entry.java_map_index}");
			}
		}
	}

	MapManagedType MakeManagedType (Guid mvid, uint tokenID, string assemblyName, string filePath, bool isGeneric, bool isDuplicate)
	{
		return new MapManagedType (mvid, tokenID, assemblyName, filePath) {
			IsGeneric = isGeneric,
				IsDuplicate = isDuplicate,
				TypeName = ManagedResolver.Lookup (assemblyName, mvid, tokenID)
				};
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

	bool DoConvert ()
	{
		if (modules == null || javaTypes == null) {
			Log.WarningLine ($"{Description}: cannot convert maps, no data");
			return false;
		}

		string filePath = ELF.FilePath;
		var managedToJava = new List<MapEntry> ();
		uint index = 0;

		bool somethingFailed = false;
		foreach (TypeMapModule m in modules) {
			ConvertManagedMap (m, EnsureMap (m), isDuplicate: false);
			if (m.duplicate_map != null) {
				if (!ConvertManagedMap (m, m.duplicate_map, isDuplicate: true)) {
					somethingFailed = true;
				}
			}

			index++;
		}

		if (somethingFailed) {
			return false;
		}

		index = 0;
		var javaToManaged = new List<MapEntry> ();
		foreach (TypeMapJava tmj in javaTypes) {
			(bool success, TypeMapModule? module, bool isGeneric, bool isDuplicate) = FindManagedType (tmj.module_index, tmj.type_token_id);
			if (!success) {
				somethingFailed = true;
				continue;
			}

			if (module == null) {
				throw new InvalidOperationException ("module must not be null here");
			}

			javaToManaged.Add (
				new MapEntry (
					MakeManagedType (module.module_uuid, tmj.type_token_id, module.assembly_name, filePath, isGeneric, isDuplicate),
					new MapJavaType (tmj.java_name, filePath)
				)
			);
			index++;
		}

		map = MakeMap (managedToJava, javaToManaged);
		return true;

		bool ConvertManagedMap (TypeMapModule module, List<TypeMapModuleEntry> map, bool isDuplicate)
		{
			foreach (TypeMapModuleEntry entry in map) {
				TypeMapJava java;

				if ((uint)javaTypes.Count <= entry.java_map_index) {
					Log.ErrorLine ($"Managed type {entry.type_token_id} in module {module.assembly_name} ({module.module_uuid}) has invalid Java map index {entry.java_map_index}");
					return false;
				}
				java = javaTypes[(int)entry.java_map_index];
				managedToJava.Add (
					new MapEntry (
						MakeManagedType (module.module_uuid, entry.type_token_id, module.assembly_name, filePath, isGeneric: false, isDuplicate: isDuplicate),
						new MapJavaType (java.java_name, filePath)
					)
				);
			}
			return true;
		}

		(bool success, TypeMapModule? module, bool isGeneric, bool isDuplicate) FindManagedType (uint moduleIndex, uint tokenID)
		{
			if (moduleIndex >= (uint)modules.Count) {
				Log.ErrorLine ($"Invalid module index {moduleIndex} for type token ID {tokenID} at Java map index {index}");
				return (false, null, false, false);
			}

			TypeMapModule m = modules[(int)moduleIndex];
			if (tokenID == 0) {
				return (true, m, true, false);
			}

			foreach (TypeMapModuleEntry entry in EnsureMap (m)) {
				if (entry.type_token_id == tokenID) {
					return (true, m, false, false);
				}
			}

			if (m.duplicate_map != null) {
				foreach (TypeMapModuleEntry entry in m.duplicate_map) {
					if (entry.type_token_id == tokenID) {
						return (true, m, false, true);
					}
				}
			}

			Log.ErrorLine ($"Module {m.assembly_name} ({m.module_uuid}) at index {moduleIndex} doesn't contain an entry for managed type with token ID {tokenID}");
			return (false, null, false, false);
		}

		List<TypeMapModuleEntry> EnsureMap (TypeMapModule m)
		{
			if (m.map == null)
				throw new InvalidOperationException ($"Module {m.module_uuid} ({m.assembly_name}) has no map?");
			return m.map;
		}
	}

	List<TypeMapJava> LoadJavaTypes (string filePath)
	{
		ulong javaTypeCount = (ulong)ELF.GetUInt32 (JavaTypeCountSymbolName);
		ulong javaNameWidth = (ulong)ELF.GetUInt32 (JavaNameWidthSymbolName);

		// MUST be kept in sync with: src/monodroid/jni/xamarin-app.hh (struct TypeMapModule)
		ulong size = 0;
		size += ELF.GetPaddedSize<uint> (size); // module_index
		size += ELF.GetPaddedSize<uint> (size); // type_token_id
		size += javaNameWidth;

		byte[] data = ELF.GetData (MapJavaSymbolName);
		if (data.Length == 0)
			throw new InvalidOperationException ($"{filePath} doesn't have a valid '{MapJavaSymbolName}' symbol");

		ulong calculatedJavaTypeCount = (ulong)data.LongLength / size;
		if (calculatedJavaTypeCount != javaTypeCount)
			throw new InvalidOperationException ($"{filePath} has invalid '{JavaTypeCountSymbolName}' symbol value ({javaTypeCount}), '{JavaTypeCountSymbolName}' size indicates there are {calculatedJavaTypeCount} managedToJava instead");

		var ret = new List<TypeMapJava> ();
		ulong offset = 0;
		for (ulong i = 0; i < javaTypeCount; i++) {
			var javaEntry = new TypeMapJava {
				module_index = ReadUInt32 (data, ref offset, packed: true),
				type_token_id = ReadUInt32 (data, ref offset, packed: true),
			};

			javaEntry.java_name = ELF.GetASCIIZ (data, offset) ?? Constants.UnableToLoadDataForPointer;
			offset += javaNameWidth;
			ret.Add (javaEntry);
		}

		return ret;
	}

	List<TypeMapModule> LoadMapModules (string filePath)
	{
		ulong moduleCount = (ulong)ELF.GetUInt32 (ModuleCountSymbolName);

		// MUST be kept in sync with: src/monodroid/jni/xamarin-app.hh (struct TypeMapModule)
		ulong size;

		size  = 16;                           // module_uuid
		size += ELF.GetPaddedSize<uint> (size);   // entry_count
		size += ELF.GetPaddedSize<uint> (size);   // duplicate_count
		size += ELF.GetPaddedSize<string> (size); // map (pointer)
		size += ELF.GetPaddedSize<string> (size); // duplicate_map (pointer)
		size += ELF.GetPaddedSize<string> (size); // assembly_name (pointer)
		size += ELF.GetPaddedSize<string> (size); // image (pointer)
		size += ELF.GetPaddedSize<uint> (size);   // java_name_width
		size += ELF.GetPaddedSize<string> (size); // java_map (pointer)

		byte[] moduleData = ELF.GetData (MapModulesSymbolName);
		if (moduleData.Length == 0)
			throw new InvalidOperationException ($"{filePath} doesn't have a valid '{MapModulesSymbolName}' symbol");

		ulong calculatedModuleCount = (ulong)moduleData.Length / size;
		if (calculatedModuleCount != moduleCount)
			throw new InvalidOperationException ($"{filePath} has invalid '{ModuleCountSymbolName}' symbol value ({moduleCount}), '{MapModulesSymbolName}' size indicates there are {calculatedModuleCount} managedToJava instead");

		var ret = new List<TypeMapModule> ();
		ulong offset = 0;
		for (ulong i = 0; i < moduleCount; i++) {
			Log.DebugLine ($"Module {i + 1}");
			var module = new TypeMapModule ();

			byte[] mvid = new byte[16];
			Array.Copy (moduleData, (int)offset, mvid, 0, mvid.Length);
			module.module_uuid = new Guid (mvid);
			offset += (ulong)mvid.Length;
			Log.DebugLine ($"  module_uuid == {module.module_uuid}");

			module.entry_count = ReadUInt32 (moduleData, ref offset);
			Log.DebugLine ($"  entry_count == {module.entry_count}");

			module.duplicate_count = ReadUInt32 (moduleData, ref offset);
			Log.DebugLine ($"  duplicate_count == {module.duplicate_count}");

			// MUST be kept in sync with: src/monodroid/jni/xamarin-app.hh (struct TypeMapModuleEntry)
			ulong pointer = ReadPointer (moduleData, ref offset);
			size  = 0;
			size += ELF.GetPaddedSize<uint> (size); // type_token_id
			size += ELF.GetPaddedSize<uint> (size); // java_map_index

			ulong mapSize = size * module.entry_count;
			byte[] data = ELF.GetData (pointer, mapSize);

			module.map = new List<TypeMapModuleEntry> ();
			ReadMapEntries (module.map, data, module.entry_count);

			// MUST be kept in sync with: src/monodroid/jni/xamarin-app.hh (struct TypeMapModuleEntry)
			pointer = ReadPointer (moduleData, ref offset);
			if (pointer != 0) {
				mapSize = size *  module.duplicate_count;
				data = ELF.GetData (pointer, mapSize);
				module.duplicate_map = new List<TypeMapModuleEntry> ();
				ReadMapEntries (module.duplicate_map, data, module.duplicate_count);
			}

			pointer = ReadPointer (moduleData, ref offset);
			module.assembly_name = ELF.GetASCIIZ (pointer) ?? Constants.UnableToLoadDataForPointer;
			Log.DebugLine ($"  assembly_name == {module.assembly_name}");
			Log.DebugLine ("");

			// Read the values to properly adjust the offset taking padding into account
			ReadPointer (moduleData, ref offset);
			ReadUInt32 (moduleData, ref offset);
			ReadPointer (moduleData, ref offset);

			ret.Add (module);
		}

		return ret;

		void ReadMapEntries (List<TypeMapModuleEntry> map, byte[] inputData, uint entryCount)
		{
			ulong mapOffset = 0;
			for (uint i = 0; i < entryCount; i++) {
				var entry = new TypeMapModuleEntry {
					type_token_id = ReadUInt32 (inputData, ref mapOffset),
					java_map_index = ReadUInt32 (inputData, ref mapOffset)
				};

				map.Add (entry);
			}
		}
	}
}
