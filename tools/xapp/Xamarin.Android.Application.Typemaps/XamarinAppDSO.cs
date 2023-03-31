using System;
using System.IO;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application.Typemaps;

abstract class XamarinAppDSO : ITypemap
{
	AnELF? elf;

	protected AnELF ELF                           => elf ?? throw new InvalidOperationException ("ELF image not loaded");
	protected bool Is64Bit                        => ELF.Is64Bit;
	protected ManagedTypeResolver ManagedResolver { get; }
	protected ILogger Log                         { get; }

	public MapArchitecture MapArchitecture        => TypemapUtilities.GetMapArchitecture (ELF.AnyELF);
	public string FullPath                        { get; } = String.Empty;
	public abstract string Description            { get; }
	public abstract string FormatVersion          { get; }
	public abstract Map Map                       { get; }

	protected XamarinAppDSO (ILogger log, ManagedTypeResolver managedResolver, string fullPath)
	{
		Log = log;
		ManagedResolver = managedResolver;
		FullPath = fullPath;
	}

	protected XamarinAppDSO (ILogger log, ManagedTypeResolver managedResolver, AnELF elf)
		: this (log, managedResolver, Path.GetFullPath (elf.FilePath))
	{
		this.elf = elf;
	}

	public bool CanLoad (Stream stream, string filePath)
	{
		stream.Seek (0, SeekOrigin.Begin);
		if (!AnELF.TryLoad (Log, stream, filePath, out AnELF? elf) || elf == null) {
			Log.Debug ($"AnELF.TryLoad failed (elf == {elf})");
			return false;
		}

		if (!CanLoad (elf)) {
			Log.Debug ($"Cannot load {elf} in {this}");
			return false;
		}

		this.elf = elf;
		return true;
	}

	public abstract bool Load (string outputDirectory, bool generateFiles);
	public abstract bool CanLoad (AnELF elf);

	protected bool HasSymbol (AnELF elf, string symbolName)
	{
		bool ret = elf.HasSymbol (symbolName);
		if (!ret)
			Log.Debug ($"{elf.FilePath} is missing symbol '{symbolName}'");
		return ret;
	}

	protected uint ReadUInt32 (byte[] data, ref ulong offset, bool packed = false)
	{
		const ulong DataSize = 4;

		if ((ulong)data.Length < (offset + DataSize))
			throw new InvalidOperationException ("Not enough data to read a 32-bit integer");

		uint ret = BitConverter.ToUInt32 (data, (int)offset);
		offset += packed ? DataSize : ELF.GetPaddedSize<uint> (offset);

		return ret;
	}

	protected ulong ReadUInt64 (byte[] data, ref ulong offset, bool packed = false)
	{
		const ulong DataSize = 8;

		if ((ulong)data.Length < (offset + DataSize))
			throw new InvalidOperationException ("Not enough data to read a 64-bit integer");

		ulong ret = BitConverter.ToUInt64 (data, (int)offset);
		offset += packed ? DataSize : ELF.GetPaddedSize<ulong> (offset);

		return ret;
	}

	protected ulong ReadPointer (byte[] data, ref ulong offset, bool packed = false)
	{
		ulong ret;

		if (Is64Bit) {
			ret = ReadUInt64 (data, ref offset, packed);
		} else {
			ret = (ulong)ReadUInt32 (data, ref offset, packed);
		}

		return ret;
	}
}
