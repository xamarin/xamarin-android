using System;
using System.Collections.Generic;
using System.IO;

using ELFSharp.ELF.Sections;
using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

// Must be kept in sync with `struct DSOCacheEntry` in src/monodroid/jni/xamarin-app.hh
sealed class DSOCacheEntry
{
	public readonly ulong     hash;
	public readonly bool      ignore;
	public readonly string    name;
	public readonly IntPtr    handle = IntPtr.Zero;

	public DSOCacheEntry (ILogger log, BinaryReader reader, AnELF elf, ISymbolEntry symbolEntry)
	{
		bool is64Bit = elf.Is64Bit;
		ulong sizeSoFar = 0;
		ulong entryOffset = (ulong)reader.BaseStream.Position;

		sizeSoFar += Util.ReadField (reader, ref hash, sizeSoFar, is64Bit);
		sizeSoFar += Util.ReadField (reader, ref ignore, sizeSoFar, is64Bit);

		ulong pointerOffset = Util.GetPadding<string> (sizeSoFar, is64Bit) + sizeSoFar + entryOffset;
		name = elf.GetStringFromPointerField (symbolEntry, pointerOffset) ?? Constants.UnableToLoadDataForPointer;
		sizeSoFar += Util.ReadField (reader, ref name, sizeSoFar, is64Bit);
		sizeSoFar += Util.ReadField (reader, ref handle, sizeSoFar, is64Bit);
	}
}

class DSOCache
{
	public List<DSOCacheEntry> Entries { get; } = new List<DSOCacheEntry> ();

	public DSOCache (ILogger log, byte[] data, AnELF elf, ISymbolEntry symbolEntry)
	{
		using var stream = new MemoryStream (data);
		using var reader = new BinaryReader (stream);

		while (stream.Position < stream.Length) {
			Entries.Add (new DSOCacheEntry (log, reader, elf, symbolEntry));
		}
	}
}
