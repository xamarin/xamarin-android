using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Utilities;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks;

//
// Assembly store format
//
// Each target ABI/architecture has a single assembly store file, composed of the following parts:
//
// [HEADER]
// [INDEX]
// [ASSEMBLY_DESCRIPTORS]
// [ASSEMBLY DATA]
//
// Formats of the sections above are as follows:
//
// HEADER (fixed size)
//  [MAGIC]              uint; value: 0x41424158
//  [FORMAT_VERSION]     uint; store format version number
//  [ENTRY_COUNT]        uint; number of entries in the store
//  [INDEX_ENTRY_COUNT]  uint; number of entries in the index
//  [INDEX_SIZE]         uint; index size in bytes
//
// INDEX (variable size, HEADER.ENTRY_COUNT*2 entries, for assembly names with and without the extension)
//  [NAME_HASH]          uint on 32-bit platforms, ulong on 64-bit platforms; xxhash of the assembly name
//  [DESCRIPTOR_INDEX]   uint; index into in-store assembly descriptor array
//
// ASSEMBLY_DESCRIPTORS (variable size, HEADER.ENTRY_COUNT entries), each entry formatted as follows:
//  [MAPPING_INDEX]      uint; index into a runtime array where assembly data pointers are stored
//  [DATA_OFFSET]        uint; offset from the beginning of the store to the start of assembly data
//  [DATA_SIZE]          uint; size of the stored assembly data
//  [DEBUG_DATA_OFFSET]  uint; offset from the beginning of the store to the start of assembly PDB data, 0 if absent
//  [DEBUG_DATA_SIZE]    uint; size of the stored assembly PDB data, 0 if absent
//  [CONFIG_DATA_OFFSET] uint; offset from the beginning of the store to the start of assembly .config contents, 0 if absent
//  [CONFIG_DATA_SIZE]   uint; size of the stored assembly .config contents, 0 if absent
//
// ASSEMBLY_NAMES (variable size, HEADER.ENTRY_COUNT entries), each entry formatted as follows:
//  [NAME_LENGTH]        uint: length of assembly name
//  [NAME]               byte: UTF-8 bytes of assembly name, without the NUL terminator
//
partial class AssemblyStoreGenerator
{
	// The two constants below must match their counterparts in src/monodroid/jni/xamarin-app.hh
	const uint ASSEMBLY_STORE_MAGIC = 0x41424158; // 'XABA', little-endian, must match the BUNDLED_ASSEMBLIES_BLOB_MAGIC native constant

	// Bit 31 is set for 64-bit platforms, cleared for the 32-bit ones
	const uint ASSEMBLY_STORE_FORMAT_VERSION_64BIT = 0x80000002; // Must match the ASSEMBLY_STORE_FORMAT_VERSION native constant
	const uint ASSEMBLY_STORE_FORMAT_VERSION_32BIT = 0x00000002;

	const uint ASSEMBLY_STORE_ABI_AARCH64 = 0x00010000;
	const uint ASSEMBLY_STORE_ABI_ARM = 0x00020000;
	const uint ASSEMBLY_STORE_ABI_X64 = 0x00030000;
	const uint ASSEMBLY_STORE_ABI_X86 = 0x00040000;

	readonly TaskLoggingHelper log;
	readonly Dictionary<AndroidTargetArch, List<AssemblyStoreAssemblyInfo>> assemblies;

	public AssemblyStoreGenerator (TaskLoggingHelper log)
	{
		this.log = log;
		assemblies = new Dictionary<AndroidTargetArch, List<AssemblyStoreAssemblyInfo>> ();
	}

	public void Add (AssemblyStoreAssemblyInfo asmInfo)
	{
		if (!assemblies.TryGetValue (asmInfo.Arch, out List<AssemblyStoreAssemblyInfo> infos)) {
			infos = new List<AssemblyStoreAssemblyInfo> ();
			assemblies.Add (asmInfo.Arch, infos);
		}

		infos.Add (asmInfo);
	}

	public Dictionary<AndroidTargetArch, string> Generate (string baseOutputDirectory)
	{
		var ret = new Dictionary<AndroidTargetArch, string> ();

		foreach (var kvp in assemblies) {
			string storePath = Generate (baseOutputDirectory, kvp.Key, kvp.Value);
			ret.Add (kvp.Key, storePath);
		}

		return ret;
	}

	string Generate (string baseOutputDirectory, AndroidTargetArch arch, List<AssemblyStoreAssemblyInfo> infos)
	{
		(bool is64Bit, uint abiFlag) = arch switch {
			AndroidTargetArch.Arm    => (false, ASSEMBLY_STORE_ABI_ARM),
			AndroidTargetArch.X86    => (false, ASSEMBLY_STORE_ABI_X86),
			AndroidTargetArch.Arm64  => (true, ASSEMBLY_STORE_ABI_AARCH64),
			AndroidTargetArch.X86_64 => (true, ASSEMBLY_STORE_ABI_X64),
			_ => throw new NotSupportedException ($"Internal error: arch {arch} not supported")
		};

		string androidAbi = MonoAndroidHelper.ArchToAbi (arch);
		string outputDir = Path.Combine (baseOutputDirectory, androidAbi);
		Directory.CreateDirectory (outputDir);

		uint infoCount = (uint)infos.Count;
		string storePath = Path.Combine (outputDir, $"assemblies.{androidAbi}.blob.so");
		var index = new List<AssemblyStoreIndexEntry> ();
		var descriptors = new List<AssemblyStoreEntryDescriptor> ();
		ulong namesSize = 0;

		foreach (AssemblyStoreAssemblyInfo info in infos) {
			namesSize += (ulong)info.AssemblyNameBytes.Length;
			namesSize += sizeof (uint);
		}

		ulong assemblyDataStart = (infoCount * IndexEntrySize () * 2) + (AssemblyStoreEntryDescriptor.NativeSize * infoCount) + AssemblyStoreHeader.NativeSize + namesSize;
		// We'll start writing to the stream after we seek to the position just after the header, index, descriptors and name data.
		ulong curPos = assemblyDataStart;

		using var fs = File.Open (storePath, FileMode.Create, FileAccess.Write, FileShare.Read);
		fs.Seek ((long)curPos, SeekOrigin.Begin);

		foreach (AssemblyStoreAssemblyInfo info in infos) {
			(AssemblyStoreEntryDescriptor desc, curPos) = MakeDescriptor (info, curPos);
			desc.mapping_index = (uint)descriptors.Count;
			descriptors.Add (desc);

			if ((uint)fs.Position != desc.data_offset) {
				throw new InvalidOperationException ($"Internal error: corrupted store '{storePath}' stream");
			}

			ulong name_with_ext_hash = MonoAndroidHelper.GetXxHash (info.AssemblyNameBytes, is64Bit);
			ulong name_no_ext_hash = MonoAndroidHelper.GetXxHash (info.AssemblyNameNoExtBytes, is64Bit);
			index.Add (new AssemblyStoreIndexEntry (info.AssemblyName, name_with_ext_hash, desc.mapping_index));
			index.Add (new AssemblyStoreIndexEntry (info.AssemblyNameNoExt, name_no_ext_hash, desc.mapping_index));

			CopyData (info.SourceFile, fs, storePath);
			CopyData (info.SymbolsFile, fs, storePath);
			CopyData (info.ConfigFile, fs, storePath);
		}
		fs.Flush ();
		fs.Seek (0, SeekOrigin.Begin);

		uint storeVersion = is64Bit ? ASSEMBLY_STORE_FORMAT_VERSION_64BIT : ASSEMBLY_STORE_FORMAT_VERSION_32BIT;
		var header = new AssemblyStoreHeader (storeVersion | abiFlag, infoCount, (uint)index.Count, (uint)(index.Count * IndexEntrySize ()));
		using var writer = new BinaryWriter (fs);
		WriteHeader (writer, header);

		using var manifestFs = File.Open ($"{storePath}.manifest", FileMode.Create, FileAccess.Write, FileShare.Read);
		using var mw = new StreamWriter (manifestFs, new System.Text.UTF8Encoding (false));
		WriteIndex (writer, mw, index, descriptors, is64Bit);
		mw.Flush ();

		Console.WriteLine ($"Number of descriptors: {descriptors.Count}; index entries: {index.Count}");
		Console.WriteLine ($"Header size: {AssemblyStoreHeader.NativeSize}; index entry size: {IndexEntrySize ()}; descriptor size: {AssemblyStoreEntryDescriptor.NativeSize}");

		WriteDescriptors (writer, descriptors);
		WriteNames (writer, infos);
		writer.Flush ();

		if (fs.Position != (long)assemblyDataStart) {
			Console.WriteLine ($"fs.Position == {fs.Position}; assemblyDataStart == {assemblyDataStart}");
			throw new InvalidOperationException ($"Internal error: store '{storePath}' position is different than metadata size after header write");
		}

		return storePath;

		uint IndexEntrySize () => is64Bit ? AssemblyStoreIndexEntry.NativeSize64 : AssemblyStoreIndexEntry.NativeSize32;
	}

	void CopyData (FileInfo? src, Stream dest, string storePath)
	{
		if (src == null) {
			return;
		}

		log.LogDebugMessage ($"Adding file '{src.Name}' to assembly store '{storePath}'");
		using var fs = src.Open (FileMode.Open, FileAccess.Read, FileShare.Read);
		fs.CopyTo (dest);
	}

	static (AssemblyStoreEntryDescriptor desc, ulong newPos) MakeDescriptor (AssemblyStoreAssemblyInfo info, ulong curPos)
	{
		var ret = new AssemblyStoreEntryDescriptor {
			data_offset = (uint)curPos,
			data_size = GetDataLength (info.SourceFile),
		};
		if (info.SymbolsFile != null) {
			ret.debug_data_offset = ret.data_offset + ret.data_size;
			ret.debug_data_size = GetDataLength (info.SymbolsFile);
		}

		if (info.ConfigFile != null) {
			ret.config_data_offset = ret.data_offset + ret.data_size + ret.debug_data_size;
			ret.config_data_size = GetDataLength (info.ConfigFile);
		}

		curPos += ret.data_size + ret.debug_data_size + ret.config_data_size;
		if (curPos > UInt32.MaxValue) {
			throw new NotSupportedException ("Assembly store size exceeds the maximum supported value");
		}

		return (ret, curPos);

		uint GetDataLength (FileInfo? info) {
			if (info == null) {
				return 0;
			}

			if (info.Length > UInt32.MaxValue) {
				throw new NotSupportedException ($"File '{info.Name}' exceeds the maximum supported size");
			}

			return (uint)info.Length;
		}
	}

	void WriteHeader (BinaryWriter writer, AssemblyStoreHeader header)
	{
		writer.Write (header.magic);
		writer.Write (header.version);
		writer.Write (header.entry_count);
		writer.Write (header.index_entry_count);
		writer.Write (header.index_size);
	}
#if XABT_TESTS
	AssemblyStoreHeader ReadHeader (BinaryReader reader)
	{
		reader.BaseStream.Seek (0, SeekOrigin.Begin);
		uint magic             = reader.ReadUInt32 ();
		uint version           = reader.ReadUInt32 ();
		uint entry_count       = reader.ReadUInt32 ();
		uint index_entry_count = reader.ReadUInt32 ();
		uint index_size        = reader.ReadUInt32 ();

		return new AssemblyStoreHeader (magic, version, entry_count, index_entry_count, index_size);
	}
#endif

	void WriteIndex (BinaryWriter writer, StreamWriter manifestWriter, List<AssemblyStoreIndexEntry> index, List<AssemblyStoreEntryDescriptor> descriptors, bool is64Bit)
	{
		index.Sort ((AssemblyStoreIndexEntry a, AssemblyStoreIndexEntry b) => a.name_hash.CompareTo (b.name_hash));

		foreach (AssemblyStoreIndexEntry entry in index) {
			if (is64Bit) {
				writer.Write (entry.name_hash);
				manifestWriter.Write ($"0x{entry.name_hash:x}");
			} else {
				writer.Write ((uint)entry.name_hash);
				manifestWriter.Write ($"0x{(uint)entry.name_hash:x}");
			}
			writer.Write (entry.descriptor_index);
			manifestWriter.Write ($" di:{entry.descriptor_index}");

			AssemblyStoreEntryDescriptor desc = descriptors[(int)entry.descriptor_index];
			manifestWriter.Write ($" mi:{desc.mapping_index}");
			manifestWriter.Write ($" do:{desc.data_offset}");
			manifestWriter.Write ($" ds:{desc.data_size}");
			manifestWriter.Write ($" ddo:{desc.debug_data_offset}");
			manifestWriter.Write ($" dds:{desc.debug_data_size}");
			manifestWriter.Write ($" cdo:{desc.config_data_offset}");
			manifestWriter.Write ($" cds:{desc.config_data_size}");
			manifestWriter.WriteLine ($" {entry.name}");
		}
	}

	List<AssemblyStoreIndexEntry> ReadIndex (BinaryReader reader, AssemblyStoreHeader header)
	{
		if (header.index_entry_count > Int32.MaxValue) {
			throw new InvalidOperationException ("Assembly store index is too big");
		}

		var index = new List<AssemblyStoreIndexEntry> ((int)header.index_entry_count);
		reader.BaseStream.Seek (AssemblyStoreHeader.NativeSize, SeekOrigin.Begin);

		bool is64Bit = (header.version & ASSEMBLY_STORE_FORMAT_VERSION_64BIT) == ASSEMBLY_STORE_FORMAT_VERSION_64BIT;
		for (int i = 0; i < (int)header.index_entry_count; i++) {
			ulong name_hash;
			if (is64Bit) {
				name_hash = reader.ReadUInt64 ();
			} else {
				name_hash = reader.ReadUInt32 ();
			}

			uint descriptor_index = reader.ReadUInt32 ();
			index.Add (new AssemblyStoreIndexEntry (String.Empty, name_hash, descriptor_index));
		}

		return index;
	}

	void WriteDescriptors (BinaryWriter writer, List<AssemblyStoreEntryDescriptor> descriptors)
	{
		foreach (AssemblyStoreEntryDescriptor desc in descriptors) {
			writer.Write (desc.mapping_index);
			writer.Write (desc.data_offset);
			writer.Write (desc.data_size);
			writer.Write (desc.debug_data_offset);
			writer.Write (desc.debug_data_size);
			writer.Write (desc.config_data_offset);
			writer.Write (desc.config_data_size);
		}
	}

	List<AssemblyStoreEntryDescriptor> ReadDescriptors (BinaryReader reader, AssemblyStoreHeader header)
	{
		if (header.entry_count > Int32.MaxValue) {
			throw new InvalidOperationException ("Assembly store descriptor table is too big");
		}

		var descriptors = new List<AssemblyStoreEntryDescriptor> ();
		reader.BaseStream.Seek (AssemblyStoreHeader.NativeSize + header.index_size, SeekOrigin.Begin);

		for (int i = 0; i < (int)header.entry_count; i++) {
			uint mapping_index      = reader.ReadUInt32 ();
			uint data_offset        = reader.ReadUInt32 ();
			uint data_size          = reader.ReadUInt32 ();
			uint debug_data_offset  = reader.ReadUInt32 ();
			uint debug_data_size    = reader.ReadUInt32 ();
			uint config_data_offset = reader.ReadUInt32 ();
			uint config_data_size   = reader.ReadUInt32 ();

			var desc = new AssemblyStoreEntryDescriptor {
				mapping_index      = mapping_index,
				data_offset        = data_offset,
				data_size          = data_size,
				debug_data_offset  = debug_data_offset,
				debug_data_size    = debug_data_size,
				config_data_offset = config_data_offset,
				config_data_size   = config_data_size,
			};
			descriptors.Add (desc);
		}

		return descriptors;
	}

	void WriteNames (BinaryWriter writer, List<AssemblyStoreAssemblyInfo> infos)
	{
		foreach (AssemblyStoreAssemblyInfo info in infos) {
			writer.Write ((uint)info.AssemblyNameBytes.Length);
			writer.Write (info.AssemblyNameBytes);
		}
	}
}
