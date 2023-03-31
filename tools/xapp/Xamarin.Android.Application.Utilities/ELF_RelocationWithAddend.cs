using System;
using System.IO;

namespace Xamarin.Android.Application.Utilities;

abstract class ELF_Rela<TUnsigned, TSigned>
{
	protected abstract long StructureSize { get; }

	public readonly TUnsigned r_offset;
	public readonly TUnsigned r_info;
	public readonly TSigned   r_addend;

	protected ELF_Rela (BinaryReader reader, ulong offsetIntoData)
	{
		if (reader.BaseStream.Length < (long)offsetIntoData + StructureSize) {
			throw new ArgumentOutOfRangeException ("Data array too short");
		}
		ReadData (reader, out r_offset, out r_info, out r_addend);
	}

	protected abstract void ReadData (BinaryReader reader, out TUnsigned offset, out TUnsigned info, out TSigned addend);

	public override string ToString()
	{
		return $"{GetType ()}: r_offset == 0x{r_offset:x} ({r_offset}); r_info == 0x{r_info:x} ({r_info}); r_addend == 0x{r_addend:x} ({r_addend})";
	}
}

// Corresponds to Elf64_Rela structure from ELF documentation:
//
//  typedef struct {
//     Elf64_Addr r_offset;
//     uint64_t   r_info;
//     int64_t    r_addend;
//  } Elf64_Rela;
//
sealed class ELF64_Rela : ELF_Rela<ulong, long>
{
	protected override long StructureSize => 3 * sizeof (ulong);

	public ELF64_Rela (BinaryReader data, ulong offsetIntoData)
		: base (data, offsetIntoData)
	{}

	protected override void ReadData (BinaryReader reader, out ulong offset, out ulong info, out long addend)
	{
		offset = reader.ReadUInt64 ();
		info = reader.ReadUInt64 ();
		addend = reader.ReadInt64 ();
	}
}
