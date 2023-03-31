using System;
using System.Buffers;
using System.IO;
using System.Text;

using K4os.Compression.LZ4;

namespace Xamarin.Android.Application.Utilities;

static class Util
{
	//
	// See src/monodroid/jni/xamarin-app.hh; struct CompressedAssemblyHeader
	//
	// LZ4 compressed assembly header format (12 bytes):
	//   uint magic;                 // 0x5A4C4158; 'XALZ', little-endian
	//   uint descriptor_index;      // Index into an internal assembly descriptor table
	//   uint uncompressed_length;   // Size of assembly, uncompressed
	//
	const int CompressionHeaderLength = 12;

	public static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;
	public static ILogger? Log;

	/// <summary>
	/// <para>
	/// Checks whether assembly data in <paramref name="stream"/> is compressed.  The method checks whether there
	/// is enough data available in the stream, but it REQUIRES that the stream is positioned at the start of the
	/// potentially compressed data.  This is required since sometimes (e.g. in the assembly store reader), the
	/// stream contains more than one entry and we mustn't assume anything about positions there.
	/// </para>
	/// <para>
	/// Stream position is unmodified after this method returns, but stream must support seeking.
	/// </para>
	/// </summary>
	public static bool IsCompressedAssembly (Stream stream)
	{
		if (!EnoughDataInStreamForCompressedAssembly (stream, nameof (IsCompressedAssembly))) {
			return false;
		}

		long pos = stream.Position;
		try {
			foreach (byte b in Constants.CompressedDataMagic) {
				int rval = stream.ReadByte ();
				if (rval == -1) {
					return false;
				}

				var r = (byte)rval;
				if (r != b) {
					return false;
				}
			}
		} finally {
			stream.Seek (pos, SeekOrigin.Begin);
		}

		return true;
	}

	/// <summary>
	/// <para>
	/// Attempt to decompress assembly data from <paramref name="input"/>.  The method checks whether there
	/// is enough data available in the stream, and that the data has a valid compression signature, but it
	/// REQUIRES that the stream is positioned at the start of the potentially compressed data.  This is
	/// required since sometimes (e.g. in the assembly store reader), the stream contains more than one
	/// entry and we mustn't assume anything about positions there.
	/// </para>
	/// <para>
	/// The <paramref name="size"/> parameter may be set to a value equal to or less than zero to indicate that
	/// all the data available in the <paramref name="input"/> stream from its current position to the end is to
	/// be read.
	/// </para>
	/// <para>
	/// Data is decompressed into <paramref name="output"/>, overwriting any data that might have been previously
	/// found in it.  On return, <paramref name="output"/> is positioned at the start of decompressed data.
	/// </para>
	/// <para>
	/// Both streams must support seeking.
	/// </para>
	/// </summary>
	/// <returns><c>true</c> upon successful completion, <c>false</c> otherwise</returns>
	public static bool DecompressAssembly (Stream input, Stream output, long size = 0)
	{
		if (!EnoughDataInStreamForCompressedAssembly (input, nameof (DecompressAssembly))) {
			return false;
		}

		// Compressed header format is at the top of this class
		using var reader = new BinaryReader (input, Encoding.UTF8, leaveOpen: true);

		// magic
		uint magic = reader.ReadUInt32 ();
		if (magic != Constants.CompressedDataMagicInt) {
			Log?.DebugLine ($"Stream cannot be compressed, invalid magic number. Expected 0x{Constants.CompressedDataMagicInt:x}, got 0x{magic:x}");
			return false;
		}

		// descriptor_index, ignore
		reader.ReadUInt32 ();

		if (size <= 0) {
			size = input.Length - input.Position;
		}

		output.SetLength (0);

		int uncompressedLength = (int)reader.ReadUInt32 ();
		int dataSize = (int)size - CompressionHeaderLength; // subtract the compression header size
		byte[]  inputBytes = BytePool.Rent (dataSize);
		byte[]? outputBytes = null;

		try {
			int nread = reader.Read (inputBytes, 0, dataSize);
			if (nread < dataSize) {
				Log?.DebugLine ($"{nameof(DecompressAssembly)}: read less data from stream ({nread} than expected ({inputBytes.Length})");
				return false;
			}

			outputBytes = BytePool.Rent (uncompressedLength);
			int decoded = LZ4Codec.Decode (inputBytes, 0, dataSize, outputBytes, 0, uncompressedLength);
			if (decoded < uncompressedLength) {
				Log?.DebugLine ($"{nameof(DecompressAssembly)}: LZ4 decoded less bytes ({decoded}) than expected ({uncompressedLength})");
				return false;
			}

			output.Write (outputBytes, 0, decoded);
			output.Flush ();
			output.Seek (0, SeekOrigin.Begin);
		} finally {
			BytePool.Return (inputBytes);
			if (outputBytes != null) {
				BytePool.Return (outputBytes);
			}
		}

		return true;
	}

	// Check whether there's enough data for compression header + at least one byte of data
	static bool EnoughDataInStreamForCompressedAssembly (Stream stream, string where)
	{
		bool enough = EnoughDataInStream (stream, CompressionHeaderLength + 1);
		if (!enough) {
			Log?.DebugLine ($"{where}: stream cannot be compressed, not enough data");
		}

		return enough;
	}

	static bool EnoughDataInStream (Stream stream, long needed)
	{
		if (needed < 0) {
			return false;
		}

		if (needed == 0) {
			return true;
		}

		return stream.Length - stream.Position >= needed;
	}

	public static void CreateFileDirectory (string filePath)
	{
		string fileDir = Path.GetDirectoryName (filePath) ?? String.Empty;
		if (fileDir.Length > 0) {
			Directory.CreateDirectory (fileDir);
		}
	}

	static ulong GetPadding<S> (ulong sizeSoFar, bool is64Bit, out ulong typeSize)
	{
		typeSize = Util.GetNativeTypeSize<S> (is64Bit);
		if (typeSize == 1) {
			return 0;
		}

		ulong modulo;
		if (is64Bit) {
			modulo = typeSize < 8 ? 4u : 8u;
		} else {
			modulo = 4u;
		}

		ulong alignment = sizeSoFar % modulo;
		if (alignment == 0) {
			return 0;
		}

		return modulo - alignment;
	}

	public static ulong GetPadding<S> (ulong sizeSoFar, bool is64Bit)
	{
		return GetPadding<S> (sizeSoFar, is64Bit, out ulong _);
	}

	public static ulong GetPaddedSize<S> (ulong sizeSoFar, bool is64Bit)
	{
		ulong padding = GetPadding<S> (sizeSoFar, is64Bit, out ulong typeSize);

		if (padding == 0) {
			return typeSize;
		}

		return typeSize + padding;
	}

	public static ulong GetNativeTypeSize<S> (bool is64Bit)
	{
		Type type = typeof(S);

		if (type == typeof(string) || type == typeof(IntPtr)) {
			// We treat `string` as a generic pointer
			return is64Bit ? 8u : 4u;
		}

		if (type == typeof(byte)) {
			return 1u;
		}

		if (type == typeof(bool)) {
			return 1u;
		}

		if (type == typeof(Int32) || type == typeof(UInt32)) {
			return 4u;
		}

		if (type == typeof(Int64) || type == typeof(UInt64)) {
			return 8u;
		}

		throw new InvalidOperationException ($"Unable to map managed type {type} to native assembler type");
	}

	/// <summary>
	/// When reading binary data for C++ structures from ELF images, we need to account for field alignment.
	/// This method calculates the number of actual bytes read from the data stream, as well as properly
	/// adjusts the stream position to read the next field correctly.
	/// </summary>
	public static ulong GetSizeAndAdjustPosition <T> (BinaryReader reader, ulong sizeSoFar, bool is64Bit)
	{
		ulong typeSize = GetNativeTypeSize<T> (is64Bit);
		ulong paddedSize = GetPaddedSize<T> (sizeSoFar, is64Bit);

		if (paddedSize == 0) {
			throw new InvalidOperationException ("Padded size must not be 0");
		}

		if (paddedSize < typeSize) {
			throw new InvalidOperationException ("Padded size must not be smaller than type size");
		}

		if (paddedSize == typeSize) {
			return typeSize;
		}

		ulong seekOffset = paddedSize - typeSize;
		reader.BaseStream.Seek ((long)seekOffset, SeekOrigin.Current);

		return paddedSize;
	}

	/// <summary>
	/// Read data from a C++ structure <c>bool</c> field and adjust stream position accordingly.
	/// </summary>
	/// <seealso cref="GetSizeAndAdjustPosition"/>
	/// <returns>Number of actual bytes read (including padding)</returns>
	public static ulong ReadField (BinaryReader reader, ref bool field, ulong sizeSoFar, bool is64Bit)
	{
		ulong ret = GetSizeAndAdjustPosition<bool> (reader, sizeSoFar, is64Bit);
		field = reader.ReadBoolean ();
		return ret;
	}

	/// <summary>
	/// Read data from a C++ structure <c>byte</c> field and adjust stream position accordingly.
	/// </summary>
	/// <seealso cref="GetSizeAndAdjustPosition"/>
	/// <returns>Number of actual bytes read (including padding)</returns>
	public static ulong ReadField (BinaryReader reader, ref byte field, ulong sizeSoFar, bool is64Bit)
	{
		ulong ret = GetSizeAndAdjustPosition<byte> (reader, sizeSoFar, is64Bit);
		field = reader.ReadByte ();
		return ret;
	}

	/// <summary>
	/// Read data from a C++ structure <c>uint</c> field and adjust stream position accordingly.
	/// </summary>
	/// <seealso cref="GetSizeAndAdjustPosition"/>
	/// <returns>Number of actual bytes read (including padding)</returns>
	public static ulong ReadField (BinaryReader reader, ref uint field, ulong sizeSoFar, bool is64Bit)
	{
		ulong ret = GetSizeAndAdjustPosition<uint> (reader, sizeSoFar, is64Bit);
		field = reader.ReadUInt32 ();
		return ret;
	}

	/// <summary>
	/// Read data from a C++ structure <c>ulong</c> field and adjust stream position accordingly.
	/// </summary>
	/// <seealso cref="GetSizeAndAdjustPosition"/>
	/// <returns>Number of actual bytes read (including padding)</returns>
	public static ulong ReadField (BinaryReader reader, ref ulong field, ulong sizeSoFar, bool is64Bit)
	{
		ulong ret = GetSizeAndAdjustPosition<ulong> (reader, sizeSoFar, is64Bit);
		field = reader.ReadUInt64 ();
		return ret;
	}

	/// <summary>
	/// Read data from a C++ structure <c>pointer</c> field and adjust stream position accordingly.  Nothing is
	/// actually stored in the <param ref="field"/> parameter, as the pointer will have a value of <c>0</c>. Instead,
	/// appropriate number of bytes is skipped in the data stream.
	/// </summary>
	/// <seealso cref="GetSizeAndAdjustPosition"/>
	/// <returns>Number of actual bytes read (including padding)</returns>
	public static ulong ReadField (BinaryReader reader, ref string field, ulong sizeSoFar, bool is64Bit)
	{
		ulong ret = GetSizeAndAdjustPosition<string> (reader, sizeSoFar, is64Bit);
		var _ = is64Bit ? reader.ReadUInt64 () : reader.ReadUInt32 ();
		return ret;
	}

	/// <summary>
	/// Read data from a C++ structure <c>pointer</c> field and adjust stream position accordingly.  Nothing is
	/// actually stored in the <param ref="field"/> parameter, as the pointer will have a value of <c>0</c>. Instead,
	/// appropriate number of bytes is skipped in the data stream.
	/// </summary>
	/// <seealso cref="GetSizeAndAdjustPosition"/>
	/// <returns>Number of actual bytes read (including padding)</returns>
	public static ulong ReadField (BinaryReader reader, ref IntPtr field, ulong sizeSoFar, bool is64Bit)
	{
		ulong ret = GetSizeAndAdjustPosition<IntPtr> (reader, sizeSoFar, is64Bit);
		var _ = is64Bit ? reader.ReadUInt64 () : reader.ReadUInt32 ();
		return ret;
	}

	public static string ToStringOrNull<T> (T? reference) => reference == null ? "<NULL>" : reference.ToString () ?? "[unknown]";

	public static string YesNo (bool yes) => yes ? "yes" : "no";
	public static string AreOrNot (bool are) => are ? "are" : "are not";
}
