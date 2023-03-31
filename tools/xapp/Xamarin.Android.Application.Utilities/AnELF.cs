using System;
using System.IO;
using System.Linq;
using System.Text;

using ELFSharp;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;

namespace Xamarin.Android.Application.Utilities;

abstract class AnELF
{
	protected static readonly byte[] EmptyArray = Array.Empty<byte> ();

	const string DynsymSectionName  = ".dynsym";
	const string SymtabSectionName  = ".symtab";
	const string RodataSectionName  = ".rodata";

	ISymbolTable dynamicSymbolsSection;
	ISection rodataSection;
	ISymbolTable? symbolsSection;
	string filePath;
	IELF elf;
	Stream elfStream;

	protected ISymbolTable DynSymSection => dynamicSymbolsSection;
	protected ISymbolTable? SymSection => symbolsSection;
	protected ISection RodataSection => rodataSection;
	public IELF AnyELF => elf;
	protected Stream ELFStream => elfStream;
	protected ILogger Log { get; }

	public string FilePath => filePath;
	public int PointerSize => Is64Bit ? 8 : 4;

	public abstract bool Is64Bit { get; }
	public abstract string Bitness { get; }

	protected AnELF (ILogger log, Stream stream, string filePath, IELF elf, ISymbolTable dynsymSection, ISection rodataSection, ISymbolTable? symSection)
	{
		Log = log;
		this.filePath = filePath;
		this.elf = elf;
		elfStream = stream;
		dynamicSymbolsSection = dynsymSection;
		this.rodataSection = rodataSection;
		symbolsSection = symSection;
	}

	public ISymbolEntry? GetSymbol (string symbolName)
	{
		ISymbolEntry? symbol = null;

		if (symbolsSection != null) {
			symbol = GetSymbol (symbolsSection, symbolName);
		}

		if (symbol == null) {
			symbol = GetSymbol (dynamicSymbolsSection, symbolName);
		}

		return symbol;
	}

	protected static ISymbolEntry? GetSymbol (ISymbolTable symtab, string symbolName)
	{
		return symtab.Entries.Where (entry => String.Compare (entry.Name, symbolName, StringComparison.Ordinal) == 0).FirstOrDefault ();
	}

	protected static SymbolEntry<T>? GetSymbol<T> (SymbolTable<T> symtab, T symbolValue) where T: struct
	{
		return symtab.Entries.Where (entry => entry.Value.Equals (symbolValue)).FirstOrDefault ();
	}

	public bool HasSymbol (string symbolName)
	{
		return GetSymbol (symbolName) != null;
	}

	public byte[] GetData (string symbolName)
	{
		return GetData (symbolName, out ISymbolEntry? _);
	}

	public byte[] GetData (string symbolName, out ISymbolEntry? symbolEntry)
	{
		Log.DebugLine ($"Looking for symbol: {symbolName}");
		symbolEntry = GetSymbol (symbolName);
		if (symbolEntry == null)
			return EmptyArray;

		if (Is64Bit) {
			var symbol64 = symbolEntry as SymbolEntry<ulong>;
			if (symbol64 == null)
				throw new InvalidOperationException ($"Symbol '{symbolName}' is not a valid 64-bit symbol");
			return GetData (symbol64);
		}

		var symbol32 = symbolEntry as SymbolEntry<uint>;
		if (symbol32 == null)
			throw new InvalidOperationException ($"Symbol '{symbolName}' is not a valid 32-bit symbol");

		return GetData (symbol32);
	}

	public string? GetStringFromPointer (ISymbolEntry symbolEntry)
	{
		return GetStringFromPointerField (symbolEntry, 0);
	}

	public abstract string? GetStringFromPointerField (ISymbolEntry symbolEntry, ulong pointerFieldOffset);
	public abstract byte[] GetData (ulong symbolValue, ulong size);

	public string? GetASCIIZ (ulong symbolValue)
	{
		return GetASCIIZ (GetData (symbolValue, 0), 0);
	}

	public string? GetASCIIZ (byte[] data, ulong offset)
	{
		if (offset >= (ulong)data.LongLength) {
			Log.DebugLine ("Not enough data to retrieve an ASCIIZ string");
			return null;
		}

		int count = data.Length;

		for (ulong i = offset; i < (ulong)data.LongLength; i++) {
			if (data[i] == 0) {
				count = (int)(i - offset);
				break;
			}
		}

		return Encoding.ASCII.GetString (data, (int)offset, count);
	}

	public ulong GetPaddedSize<S> (ulong sizeSoFar) => Util.GetPaddedSize<S> (sizeSoFar, Is64Bit);

	public ulong GetPaddedSize<S> (ulong sizeSoFar, S _)
	{
		return GetPaddedSize<S> (sizeSoFar);
	}

	protected virtual byte[] GetData (SymbolEntry<ulong> symbol)
	{
		throw new NotSupportedException ();
	}

	protected virtual byte[] GetData (SymbolEntry<uint> symbol)
	{
		throw new NotSupportedException ();
	}

	protected byte[] GetData (ISymbolEntry symbol, ulong size, ulong offset)
	{
		return GetData (symbol.PointedSection, size, offset);
	}

	protected byte[] GetData (ISection section, ulong size, ulong offset)
	{
		ulong sectionOffset = (elf.Class == Class.Bit64 ? ((Section<ulong>)section).Offset : ((Section<uint>)section).Offset);
		Log.VerboseLine ($"AnELF.GetData: section == {section.Name}; type == {section.Type}; flags == {section.Flags}; offset into binary == {sectionOffset}; size == {size}");
		byte[] data = section.GetContents ();

		Log.VerboseLine ($"  section data length: {data.Length} (long: {data.LongLength})");
		Log.VerboseLine ($"  offset into section: {offset}; symbol data length: {size}");
		if ((ulong)data.LongLength < (offset + size)) {
			return EmptyArray;
		}

		if (size == 0)
			size = (ulong)data.Length - offset;

		var ret = new byte[size];
		checked {
			Array.Copy (data, (int)offset, ret, 0, (int)size);
		}

		return ret;
	}

	public uint GetUInt32 (string symbolName)
	{
		return GetUInt32 (GetData (symbolName), 0, symbolName);
	}

	public uint GetUInt32 (ulong symbolValue)
	{
		return GetUInt32 (GetData (symbolValue, 4), 0, symbolValue.ToString ());
	}

	protected uint GetUInt32 (byte[] data, ulong offset, string symbolName)
	{
		if (data.Length < 4) {
			throw new InvalidOperationException ($"Data not big enough to retrieve a 32-bit integer from it (need 4, got {data.Length})");
		}

		return BitConverter.ToUInt32 (GetIntegerData (4, data, offset, symbolName), 0);
	}

	public ulong GetUInt64 (string symbolName)
	{
		return GetUInt64 (GetData (symbolName), 0, symbolName);
	}

	public ulong GetUInt64 (ulong symbolValue)
	{
		return GetUInt64 (GetData (symbolValue, 8), 0, symbolValue.ToString ());
	}

	protected ulong GetUInt64 (byte[] data, ulong offset, string symbolName)
	{
		if (data.Length < 8)
			throw new InvalidOperationException ("Data not big enough to retrieve a 64-bit integer from it");
		return BitConverter.ToUInt64 (GetIntegerData (8, data, offset, symbolName), 0);
	}

	byte[] GetIntegerData (uint size, byte[] data, ulong offset, string symbolName)
	{
		if ((ulong)data.LongLength < (offset + size)) {
			string bits = size == 4 ? "32" : "64";
			throw new InvalidOperationException ($"Unable to read UInt{bits} value for symbol '{symbolName}': data not long enough");
		}

		byte[] ret = new byte[size];
		Array.Copy (data, (int)offset, ret, 0, ret.Length);
		Endianess myEndianness = BitConverter.IsLittleEndian ? Endianess.LittleEndian : Endianess.BigEndian;
		if (AnyELF.Endianess != myEndianness) {
			Array.Reverse (ret);
		}

		return ret;
	}

	public static bool TryLoad (ILogger log, string filePath, out AnELF? anElf)
	{
		using var fs = File.OpenRead (filePath);
		return TryLoad (log, fs, filePath, out anElf);
	}

	public static bool TryLoad (ILogger log, Stream stream, string filePath, out AnELF? anElf)
	{
		anElf = null;
		Class elfClass = ELFReader.CheckELFType (stream);
		if (elfClass == Class.NotELF) {
			log.WarningLine ($"AnELF.TryLoad: {filePath} is not an ELF binary");
			return false;
		}

		IELF elf = ELFReader.Load (stream, shouldOwnStream: false);

		if (elf.Type != FileType.SharedObject) {
			log.WarningLine ($"AnELF.TryLoad: {filePath} is not a shared library");
			return false;
		}

		if (elf.Endianess != Endianess.LittleEndian) {
			log.WarningLine ($"AnELF.TryLoad: {filePath} is not a little-endian binary");
			return false;
		}

		bool is64;
		switch (elf.Machine) {
			case Machine.ARM:
			case Machine.Intel386:
				is64 = false;

				break;

			case Machine.AArch64:
			case Machine.AMD64:
				is64 = true;

				break;

			default:
				log.WarningLine ($"{filePath} is for an unsupported machine type {elf.Machine}");
				return false;
		}

		ISymbolTable? symtab = GetSymbolTable (elf, DynsymSectionName);
		if (symtab == null) {
			log.WarningLine ($"{filePath} does not contain dynamic symbol section '{DynsymSectionName}'");
			return false;
		}
		ISymbolTable dynsym = symtab;

		ISection? sec = GetSection (elf, RodataSectionName);
		if (sec == null) {
			log.WarningLine ("${filePath} does not contain read-only data section ('{RodataSectionName}')");
			return false;
		}
		ISection rodata = sec;

		ISymbolTable? sym = GetSymbolTable (elf, SymtabSectionName);

		if (is64) {
			anElf = new ELF64 (log, stream, filePath, elf, dynsym, rodata, sym);
		} else {
			anElf = new ELF32 (log, stream, filePath, elf, dynsym, rodata, sym);
		}

		log.DebugLine ($"AnELF.TryLoad: {filePath} is a {anElf.Bitness}-bit ELF binary ({elf.Machine})");
		return true;
	}

	protected static ISymbolTable? GetSymbolTable (IELF elf, string sectionName)
	{
		ISection? section = GetSection (elf, sectionName);
		if (section == null) {
			return null;
		}

		var symtab = section as ISymbolTable;
		if (symtab == null) {
			return null;
		}

		return symtab;
	}

	protected static ISection? GetSection (IELF elf, string sectionName)
	{
		if (!elf.TryGetSection (sectionName, out ISection section)) {
			return null;
		}

		return section;
	}
}
