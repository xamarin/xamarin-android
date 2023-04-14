using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

using ELFSharp;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Utilities;

using ELFSymbolType = global::ELFSharp.ELF.Sections.SymbolType;
using ELFSectionType = global::ELFSharp.ELF.Sections.SectionType;

namespace Xamarin.Android.Tasks
{
	static class ELFHelper
	{
		public static bool IsAOTLibrary (TaskLoggingHelper log, string path)
		{
			IELF? elf = ReadElfFile (log, path, "Unable to check if file is an AOT shared library.");
			if (elf == null) {
				return false;
			}

			return IsAOTLibrary (elf);
		}

		static bool IsAOTLibrary (IELF elf)
		{
			ISymbolTable? symtab = GetSymbolTable (elf, ".dynsym");
			if (symtab == null) {
				// We can't be sure what the DSO is, play safe
				return false;
			}

			foreach (var entry in symtab.Entries) {
				if (String.Compare ("mono_aot_file_info", entry.Name, StringComparison.Ordinal) == 0 && entry.Type == ELFSymbolType.Object) {
					return true;
				}
			}

			return false;
		}

		public static bool HasDebugSymbols (TaskLoggingHelper log, string path)
		{
			return HasDebugSymbols (log, path, out bool _);
		}

		public static bool HasDebugSymbols (TaskLoggingHelper log, string path, out bool usesDebugLink)
		{
			usesDebugLink = false;
			IELF? elf = ReadElfFile (log, path, "Skipping debug symbols presence check.");
			if (elf == null) {
				return false;
			}

			if (HasDebugSymbols (elf)) {
				return true;
			}

			ISection? gnuDebugLink = GetSection (elf, ".gnu_debuglink");
			if (gnuDebugLink == null) {
				return false;
			}
			usesDebugLink = true;

			byte[] contents = gnuDebugLink.GetContents ();
			if (contents == null || contents.Length == 0) {
				return false;
			}

			// .gnu_debuglink section format: https://sourceware.org/gdb/current/onlinedocs/gdb/Separate-Debug-Files.html#index-_002egnu_005fdebuglink-sections
			int nameEnd = -1;
			for (int i = 0; i < contents.Length; i++) {
				if (contents[i] == 0) {
					nameEnd = i;
					break;
				}
			}

			if (nameEnd < 2) {
				// Name is terminated with a 0 byte, so we need at least 2 bytes
				return false;
			}

			string debugInfoFileName = Encoding.UTF8.GetString (contents, 0, nameEnd);
			if (String.IsNullOrEmpty (debugInfoFileName)) {
				return false;
			}

			string debugFilePath = Path.Combine (Path.GetDirectoryName (path), debugInfoFileName);
			return File.Exists (debugFilePath);
		}

		static bool HasDebugSymbols (IELF elf)
		{
			return GetSymbolTable (elf, ".symtab") != null;
		}

		public static bool IsEmptyAOTLibrary (TaskLoggingHelper log, string path)
		{
			if (String.IsNullOrEmpty (path) || !File.Exists (path)) {
				return false;
			}

			try {
				return IsEmptyAOTLibrary (log, path, ELFReader.Load (path));
			} catch (Exception ex) {
				log.LogWarning ($"Attempt to check whether '{path}' is a valid ELF file failed with exception, ignoring AOT check for the file.");
				log.LogWarningFromException (ex, showStackTrace: true);
				return false;
			}
		}

		public static bool ReferencesLibrary (string libraryPath, string referencedLibraryName)
		{
			if (String.IsNullOrEmpty (libraryPath) || !File.Exists (libraryPath)) {
				return false;
			}

			IELF elf = ELFReader.Load (libraryPath);
			var dynstr = GetSection (elf, ".dynstr") as IStringTable;
			if (dynstr == null) {
				return false;
			}

			foreach (IDynamicSection section in elf.GetSections<IDynamicSection> ()) {
				foreach (IDynamicEntry entry in section.Entries) {
					if (IsLibraryReference (dynstr, entry, referencedLibraryName)) {
						return true;
					}
				}
			}

			return false;
		}

		static bool IsLibraryReference (IStringTable stringTable, IDynamicEntry dynEntry, string referencedLibraryName)
		{
			if (dynEntry.Tag != DynamicTag.Needed) {
				return false;
			}

			ulong index;
			if (dynEntry is DynamicEntry<ulong> entry64) {
				index = entry64.Value;
			} else if (dynEntry is DynamicEntry<uint> entry32) {
				index = (ulong)entry32.Value;
			} else {
				return false;
			}

			return String.Compare (referencedLibraryName, stringTable[(long)index], StringComparison.Ordinal) == 0;
		}

		static bool IsEmptyAOTLibrary (TaskLoggingHelper log, string path, IELF elf)
		{
			if (!IsAOTLibrary (elf)) {
				// Not a MonoVM AOT shared library
				return false;
			}

			ISymbolTable? symtab = GetSymbolTable (elf, ".symtab");
			if (symtab == null) {
				// The DSO is stripped, we can't tell if there are any functions defined (.text will be present anyway)
				// We perhaps **can** take a look at the .text section size, but it's not a solid check...
				log.LogDebugMessage ($"{path} is an AOT assembly but without symbol table (stripped?). Including it in the archive.");
				return false;
			}

			bool isElf64 = elf.Class == Class.Bit64;
			foreach (var entry in symtab.Entries) {
				if (entry.Type == ELFSymbolType.Function) {
					return false;
				}

				if (!(isElf64 ? IsNonEmptyCodeSymbol (entry as SymbolEntry<ulong>) : IsNonEmptyCodeSymbol (entry as SymbolEntry<uint>))) {
					continue;
				}

				// We have an entry that's in (some) executable section and has some code in it.
				// Mono creates symbols which are essentially jump tables into executable code
				// inside the DSO that is not accessible via any other symbol, merely a blob of
				// executable code. The jump table symbols are named with the `_plt` prefix.
				if (entry.Name.EndsWith ("_plt")) {
					return false;
				}
			}
			return true;

			bool IsNonEmptyCodeSymbol<T> (SymbolEntry<T>? symbolEntry) where T : struct
			{
				if (symbolEntry == null) {
					return true; // Err on the side of caution
				}

				Type t = typeof(T);
				ulong size = 0;
				if (t == typeof(System.UInt64)) {
					size = (ulong)(object)symbolEntry.Size;
				} else if (t == typeof(System.UInt32)) {
					size = (uint)(object)symbolEntry.Size;
				}

				return size != 0 && symbolEntry.PointedSection.Type == ELFSectionType.ProgBits;
			}
		}

		static ISymbolTable? GetSymbolTable (IELF elf, string sectionName)
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

		static ISection? GetSection (IELF elf, string sectionName)
		{
			if (!elf.TryGetSection (sectionName, out ISection section)) {
				return null;
			}

			return section;
		}

		static IELF? ReadElfFile (TaskLoggingHelper log, string path, string customErrorMessage)
		{
			try {
				return ELFReader.Load (path);
			} catch (Exception ex) {
				log.LogWarning ($"{path} may not be a valid ELF binary. ${customErrorMessage}");
				log.LogWarningFromException (ex, showStackTrace: false);
				return null;
			}
		}
	}
}
