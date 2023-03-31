using System;
using System.IO;

using ELFSharp.ELF;
using ELFSharp.ELF.Sections;

namespace Xamarin.Android.Application.Utilities;

class ELF64 : AnELF
{
	public override bool Is64Bit => true;
	public override string Bitness => "64";

	SymbolTable<ulong> DynamicSymbols => (SymbolTable<ulong>)DynSymSection;
	SymbolTable<ulong>? Symbols => (SymbolTable<ulong>?)SymSection;
	Section<ulong> Rodata => (Section<ulong>)RodataSection;
	ELF<ulong> ELF => (ELF<ulong>)AnyELF;

	public ELF64 (ILogger log, Stream stream, string filePath, IELF elf, ISymbolTable dynsymSection, ISection rodataSection, ISymbolTable? symSection)
		: base (log, stream, filePath, elf, dynsymSection, rodataSection, symSection)
	{}

	public override string? GetStringFromPointerField (ISymbolEntry symbolEntry, ulong pointerFieldOffset)
	{
		var symbol = symbolEntry as SymbolEntry<ulong>;
		if (symbol == null) {
			throw new InvalidOperationException ($"Expected a 64-bit symbol entry, got {symbolEntry}");
		}

		switch (ELF.Machine) {
			case Machine.AArch64:
				return GetStringFromPointerField_ARM64 (symbol, pointerFieldOffset);

			case Machine.AMD64:
				return GetStringFromPointerField_X64 (symbol, pointerFieldOffset);

			default:
				throw new InvalidOperationException ($"Unsupported ELF machine type '{ELF.Machine}'");
		}
	}

	string? GetStringFromPointerField_ARM64 (SymbolEntry<ulong> symbolEntry, ulong pointerFieldOffset)
	{
		return GetStringFromPointerField_Common (
			symbolEntry,
			pointerFieldOffset,
			(ELF64_Rela rela) => {
				// We only support R_AARCH64_RELATIVE right now
				return (RelocationTypeARM64)rela.r_info == RelocationTypeARM64.R_AARCH64_RELATIVE;
			}
		);
	}

	string? GetStringFromPointerField_X64 (SymbolEntry<ulong> symbolEntry, ulong pointerFieldOffset)
	{
		return GetStringFromPointerField_Common (
			symbolEntry,
			pointerFieldOffset,
			(ELF64_Rela rela) => {
				// We only support R_X86_64_RELATIVE right now
				return (RelocationTypeX64)rela.r_info == RelocationTypeX64.R_X86_64_RELATIVE;
			}
		);
	}

	string? GetStringFromPointerField_Common (SymbolEntry<ulong> symbolEntry, ulong pointerFieldOffset, Func<ELF64_Rela, bool> validRelocation)
	{
		Log.VerboseLine ($"[ARM64] Getting string from a pointer field in symbol '{symbolEntry.Name}', at offset {pointerFieldOffset} into the structure");

		if (symbolEntry.PointedSection.Type != SectionType.ProgBits || !symbolEntry.PointedSection.Flags.HasFlag (SectionFlags.Writable)) {
			Log.VerboseLine ("  Symbol section isn't a writable data one, pointers require a writable section to apply relocations");
			Log.VerboseLine ($"  Section info: {symbolEntry.PointedSection}");
			return null;
		}

		// Steps:
		//
		//  1. Calculate address of the field in the symbol data: [symbol section virtual address] + [symbol offset into section] + pointerFieldOffset
		//     ELFSharp does part of the job for us - symbol's value is its virtual address
		ulong pointerVA = symbolEntry.Value + pointerFieldOffset;
		Log.VerboseLine ($"  Section address == 0x{symbolEntry.PointedSection.LoadAddress:x}; offset == 0x{symbolEntry.PointedSection.Offset:x}");
		Log.VerboseLine ($"  Symbol entry value == 0x{symbolEntry.Value:x}");
		Log.VerboseLine ($"  Virtual address of the pointer: 0x{pointerVA:x} ({pointerVA})");

		//  2. Find the .rela.dyn section
		const string RelaDynSectionName = ".rela.dyn";
		Section<ulong>? relaDynSection = ELF.GetSection (RelaDynSectionName);
		Log.VerboseLine ($"  Relocation section: {Util.ToStringOrNull (relaDynSection)}");
		if (relaDynSection == null) {
			Log.VerboseLine ($"  Section '{RelaDynSectionName}' not found");
			return null;
		}

		// Make sure section type is what we need and expect
		if (relaDynSection.Type != SectionType.RelocationAddends) {
			Log.VerboseLine ($"  Section '{RelaDynSectionName}' has invalid type. Expected {SectionType.RelocationAddends}, got {relaDynSection.Type}");
			return null;
		}
		var relocationReader = new RelocationSectionAddend64 (relaDynSection);

		//  3. Find relocation entry with offset matching the address calculated in 1. Relocation entry should have code 0x403 (1027) - R_AARCH64_RELATIVE
		if (!relocationReader.Entries.TryGetValue (pointerVA, out ELF64_Rela? relocation) || relocation == null) {
			Log.VerboseLine ($"  Relocation for pointer address 0x{pointerVA:x} not found");
			return null;
		}
		Log.VerboseLine ($"  Found relocation: {relocation}");

		if (!validRelocation (relocation)) {
			// Yell, so that we can fix it
			throw new NotSupportedException ($"AArch64 relocation type {relocation.r_info} not supported. Please report at https://github.com/xamarin/xamarin.android/issues/");
		}

		//  4. Read relocation entry (see elf(5) for Elf32_Rela and Elf64_Rela structures) and get the addend value
		ulong addend = (ulong)relocation.r_addend;

		//  5. Find section the addend from 4. falls within
		Section<ulong>? pointeeSection = FindSectionForValue (addend);
		if (pointeeSection == null) {
			Log.DebugLine ($"  Unable to find section in which pointee 0x{addend:x} resides");
			return null;
		}
		Log.VerboseLine ($"  Pointee 0x{addend:x} falls within section {pointeeSection}");

		//  6. Read that section data
		byte[] data = pointeeSection.GetContents ();

		//  7. Subtract section address from the addend, this will give offset into the section
		ulong addendSectionOffset = addend - pointeeSection.LoadAddress;
		Log.VerboseLine ($"  Pointee offset into section data == 0x{addendSectionOffset:x} ({addendSectionOffset})");

		//  8. Read ASCIIZ data from the offset obtained in 7.
		return GetASCIIZ (data, addendSectionOffset);
	}

	public override byte[] GetData (ulong symbolValue, ulong size = 0)
	{
		Log.VerboseLine ($"ELF64.GetData: Looking for symbol value {symbolValue:X08}");

		SymbolEntry<ulong>? symbol = GetSymbol (DynamicSymbols, symbolValue);
		if (symbol == null && Symbols != null) {
			symbol = GetSymbol (Symbols, symbolValue);
		}

		if (symbol != null) {
			Log.VerboseLine ($"ELF64.GetData: found in section {symbol.PointedSection.Name}");
			if (symbol.Size == 0) {
				return EmptyArray;
			}

			return GetData (symbol);
		}

		Section<ulong> section = FindProgBitsSectionForValue (symbolValue);

		Log.VerboseLine ($"ELF64.GetData: found in section {section} {section.Name}");
		return GetData (section, size, OffsetInSection (section, symbolValue));
	}

	protected override byte[] GetData (SymbolEntry<ulong> symbol)
	{
		if (symbol.Size == 0) {
			return EmptyArray;
		}

		return GetData (symbol, symbol.Size, OffsetInSection (symbol.PointedSection, symbol.Value));
	}

	Section<ulong> FindProgBitsSectionForValue (ulong symbolValue)
	{
		return FindSectionForValue (symbolValue, SectionType.ProgBits) ?? throw new InvalidOperationException ($"Section matching symbol value {symbolValue:X08} cannot be found");
	}

	Section<ulong>? FindSectionForValue (ulong symbolValue, SectionType requiredType = SectionType.Null)
	{
		Log.VerboseLine ($"FindSectionForValue ({symbolValue:X08}, {requiredType})");
		int nsections = ELF.Sections.Count;

		for (int i = nsections - 1; i >= 0; i--) {
			Section<ulong> section = ELF.GetSection (i);
			if (requiredType != SectionType.Null && section.Type != requiredType) {
				continue;
			}

			if (SectionInRange (section, symbolValue)) {
				return section;
			}
		}

		Log.DebugLine ($"Section matching symbol value {symbolValue:X08} cannot be found");
		return null;
	}

	bool SectionInRange (Section<ulong> section, ulong symbolValue)
	{
		Log.VerboseLine ($"SectionInRange ({section.Name}, {symbolValue:X08})");
		Log.VerboseLine ($"  address == {section.LoadAddress:X08}; size == {section.Size}; last address = {section.LoadAddress + section.Size:X08}");
		Log.VerboseLine ($"  symbolValue >= section.LoadAddress? {symbolValue >= section.LoadAddress}");
		Log.VerboseLine ($"  (section.LoadAddress + section.Size) >= symbolValue? {(section.LoadAddress + section.Size) >= symbolValue}");
		return symbolValue >= section.LoadAddress && (section.LoadAddress + section.Size) >= symbolValue;
	}

	ulong OffsetInSection (Section<ulong> section, ulong symbolValue)
	{
		return symbolValue - section.LoadAddress;
	}
}
