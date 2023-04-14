using System;
using System.Collections.Generic;

using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using Xamarin.Android.Utilities;

namespace Xamarin.Android.Debug;

class NoLddLldbModuleCache : LldbModuleCache
{
	public NoLddLldbModuleCache (XamarinLoggingHelper log, AndroidDevice device, List<string> deviceSharedLibraries)
		: base (log, device, deviceSharedLibraries)
	{}

	protected override void FetchDependencies (HashSet<string> alreadyDownloaded, string localPath)
	{
		using IELF? elf = ReadElfFile (localPath);
		FetchDependencies (elf, alreadyDownloaded, localPath);
	}

	void FetchDependencies (IELF? elf, HashSet<string> alreadyDownloaded, string localPath)
	{
		if (elf == null) {
			Log.DebugLine ($"Failed to open '{localPath}' as an ELF file. Ignoring.");
			return;
		}

		var dynstr = GetSection (elf, ".dynstr") as IStringTable;
		if (dynstr == null) {
			Log.DebugLine ($"ELF binary {localPath} has no .dynstr section, unable to read referenced shared library names");
			return;
		}

		var needed = new HashSet<string> (StringComparer.Ordinal);
		foreach (IDynamicSection section in elf.GetSections<IDynamicSection> ()) {
			foreach (IDynamicEntry entry in section.Entries) {
				if (entry.Tag != DynamicTag.Needed) {
					continue;
				}

				AddNeeded (dynstr, entry);
			}
		}

		Log.DebugLine ($"Binary {localPath} references the following libraries:");
		foreach (string lib in needed) {
			string? localLibraryPath = FetchLibrary (lib, alreadyDownloaded);
			if (String.IsNullOrEmpty (localLibraryPath)) {
				continue;
			}

			using IELF? libElf = ReadElfFile (localLibraryPath);
			FetchDependencies (libElf, alreadyDownloaded, localLibraryPath);
		}

		void AddNeeded (IStringTable stringTable, IDynamicEntry entry)
		{
			ulong index;
			if (entry is DynamicEntry<ulong> entry64) {
				index = entry64.Value;
			} else if (entry is DynamicEntry<uint> entry32) {
				index = (ulong)entry32.Value;
			} else {
				Log.WarningLine ($"DynamicEntry neither 32 nor 64 bit? Weird");
				return;
			}

			string name = stringTable[(long)index];
			if (needed.Contains (name)) {
				return;
			}

			needed.Add (name);
		}
	}

	IELF? ReadElfFile (string path)
	{
		try {
			if (ELFReader.TryLoad (path, out IELF ret)) {
				return ret;
			}
		} catch (Exception ex) {
			Log.WarningLine ($"{path} may not be a valid ELF binary.");
			Log.WarningLine (ex.ToString ());
		}

		return null;
	}

	ISection? GetSection (IELF elf, string sectionName)
	{
		if (!elf.TryGetSection (sectionName, out ISection section)) {
			return null;
		}

		return section;
	}
}
