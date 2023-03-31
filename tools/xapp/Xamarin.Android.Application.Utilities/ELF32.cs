using System;
using System.IO;

using ELFSharp;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;

namespace Xamarin.Android.Application.Utilities;

class ELF32 : AnELF
{
	public override bool Is64Bit => false;
	public override string Bitness => "32";

	SymbolTable<uint> DynamicSymbols => (SymbolTable<uint>)DynSymSection;
	SymbolTable<uint>? Symbols => (SymbolTable<uint>?)SymSection;
	Section<uint> Rodata => (Section<uint>)RodataSection;
	ELF<uint> ELF => (ELF<uint>)AnyELF;

	public ELF32 (ILogger log, Stream stream, string filePath, IELF elf, ISymbolTable dynsymSection, ISection rodataSection, ISymbolTable? symSection)
		: base (log, stream, filePath, elf, dynsymSection, rodataSection, symSection)
	{}

	public override string GetStringFromPointerField(ISymbolEntry symbolEntry, ulong pointerFieldOffset)
	{
		throw new NotImplementedException();
	}

	public override byte[] GetData (ulong symbolValue, ulong size = 0)
	{
		checked {
			return GetData ((uint)symbolValue, size);
		}
	}

	byte[] GetData (uint symbolValue, ulong size)
	{
		Log.Debug ($"ELF64.GetData: Looking for symbol value {symbolValue:X08}");

		SymbolEntry<uint>? symbol = GetSymbol (DynamicSymbols, symbolValue);
		if (symbol == null && Symbols != null) {
			symbol = GetSymbol (Symbols, symbolValue);
		}

		if (symbol != null) {
			Log.Debug ($"ELF64.GetData: found in section {symbol.PointedSection.Name}");
			return GetData (symbol);
		}

		Section<uint> section = FindSectionForValue (symbolValue);

		Log.Debug ($"ELF64.GetData: found in section {section} {section.Name}");
		return GetData (section, size, OffsetInSection (section, symbolValue));
	}

	protected override byte[] GetData (SymbolEntry<uint> symbol)
	{
		ulong offset = symbol.Value - symbol.PointedSection.LoadAddress;
		return GetData (symbol, symbol.Size, offset);
	}

	Section<uint> FindSectionForValue (uint symbolValue)
	{
		Log.Debug ($"FindSectionForValue ({symbolValue:X08})");
		int nsections = ELF.Sections.Count;

		for (int i = nsections - 1; i >= 0; i--) {
			Section<uint> section = ELF.GetSection (i);
			if (section.Type != SectionType.ProgBits)
				continue;

			if (SectionInRange (section, symbolValue))
				return section;
		}

		throw new InvalidOperationException ($"Section matching symbol value {symbolValue:X08} cannot be found");
	}

	bool SectionInRange (Section<uint> section, uint symbolValue)
	{
		Log.Debug ($"SectionInRange ({section.Name}, {symbolValue:X08})");
		Log.Debug ($"  address == {section.LoadAddress:X08}; size == {section.Size}; last address = {section.LoadAddress + section.Size:X08}");
		Log.Debug ($"  symbolValue >= section.LoadAddress? {symbolValue >= section.LoadAddress}");
		Log.Debug ($"  (section.LoadAddress + section.Size) >= symbolValue? {(section.LoadAddress + section.Size) >= symbolValue}");
		return symbolValue >= section.LoadAddress && (section.LoadAddress + section.Size) >= symbolValue;
	}

	ulong OffsetInSection (Section<uint> section, uint symbolValue)
	{
		return symbolValue - section.LoadAddress;
	}
}
