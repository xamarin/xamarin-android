using System.Collections.Generic;
using System.IO;

using ELFSharp.ELF.Sections;

namespace Xamarin.Android.Application.Utilities;

abstract class RelocationSectionAddend<TUnsigned, TSigned, TRela>
	where TUnsigned: notnull
	where TRela: notnull, ELF_Rela<TUnsigned, TSigned>
{
	public abstract Dictionary<TUnsigned, TRela> Entries { get; }
}

class RelocationSectionAddend64 : RelocationSectionAddend<ulong, long, ELF64_Rela>
{
	public override Dictionary<ulong, ELF64_Rela> Entries { get; } = new Dictionary<ulong, ELF64_Rela> ();

	public RelocationSectionAddend64 (Section<ulong> relaDynSection)
	{
		byte[] data = relaDynSection.GetContents ();
		using var stream = new MemoryStream (data);
		using var reader = new BinaryReader (stream);

		while (stream.Position < stream.Length) {
			var entry = new ELF64_Rela (reader, (ulong)stream.Position);
			Entries.Add (entry.r_offset, entry);
		}
	}
}
