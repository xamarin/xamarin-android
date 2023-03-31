using System;
using System.Collections.Generic;
using System.IO;

using ELFSharp.ELF.Sections;
using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

sealed class MarshalMethodsManagedClass_V2
{
	public readonly uint token;
	public readonly IntPtr klass = IntPtr.Zero;

	public MarshalMethodsManagedClass_V2 (ILogger log, BinaryReader reader, AnELF elf, ISymbolEntry symbolEntry)
	{
		bool is64Bit = elf.Is64Bit;
		ulong sizeSoFar = 0;

		// Each entry is:
		//   - 32-bit managed token
		//   - pointer to class instance
		sizeSoFar += Util.ReadField (reader, ref token, sizeSoFar, is64Bit);
		sizeSoFar += Util.ReadField (reader, ref klass, sizeSoFar, is64Bit);
	}
}

sealed class MarshalMethodName_V2
{
	public readonly ulong id;
	public readonly string name;

	public uint AssemblyIndex => (uint)((id & 0xFFFFFFFF00000000) >> 32);
	public uint MethodToken   => (uint)((id & 0x00000000FFFFFFFF));

	public MarshalMethodName_V2 (ILogger log, BinaryReader reader, AnELF elf, ISymbolEntry symbolEntry)
	{
		bool is64Bit = elf.Is64Bit;
		ulong sizeSoFar = 0;
		ulong entryOffset = (ulong)reader.BaseStream.Position;

		// Each entry is:
		//   - 64-bit internal method ID
		//   - pointer to name
		sizeSoFar += Util.ReadField (reader, ref id, sizeSoFar, is64Bit);
		ulong pointerOffset = Util.GetPadding<string> (sizeSoFar, is64Bit) + sizeSoFar + entryOffset;
		name = elf.GetStringFromPointerField (symbolEntry, pointerOffset) ?? Constants.UnableToLoadDataForPointer;
		sizeSoFar += Util.ReadField (reader, ref name, sizeSoFar, is64Bit);
	}
}

abstract class MarshalMethods
{
	protected const string LogTag = "Marshal methods:";

	public abstract string FormatName { get; }
	public abstract ulong FormatTag   { get; }

	protected ILogger Log { get; }

	protected MarshalMethods (ILogger log)
	{
		Log = log;
	}

	public static MarshalMethods? Create (ILogger log, AnELF elf, ulong format_tag)
	{
		switch (format_tag) {
			case Constants.FormatTag_V1:
			case Constants.FormatTag_V2:
				return new MarshalMethods_V2 (log, elf, format_tag);

			default:
				WarnNotSupported (elf, format_tag);
				return null;
		}
	}

	public static bool Supported (AnELF elf, ulong format_tag)
	{
		Util.Log?.DebugLine ($"{LogTag} checking whether {elf.FilePath} contains supported marshal methods structures");
		// V1 is the same format as V2
		switch (format_tag) {
			case Constants.FormatTag_V1:
			case Constants.FormatTag_V2:
				return LogAndReturn (MarshalMethods_V2.CompatibleBinary (elf, format_tag));

			default:
				WarnNotSupported (elf, format_tag);
				break;
		}

		return LogAndReturn (false);

		bool LogAndReturn (bool retval)
		{
			Util.Log?.DebugLine ($"Marshal methods {Util.AreOrNot (retval)} supported in {elf.FilePath}");
			return retval;
		}
	}

	protected static bool HasRequiredSymbols (AnELF elf, List<string> symbolNames, string formatName)
	{
		Util.Log?.DebugLine ($"{LogTag} checking for required {formatName} symbols in {elf.FilePath}");
		bool allGood = true;
		foreach (string symbol in symbolNames) {
			Util.Log?.Debug ($"  '{symbol}'");
			if (!elf.HasSymbol (symbol)) {
				Util.Log?.DebugLine (" [missing]");
				allGood = false;
			} else {
				Util.Log?.DebugLine (" [found]");
			}
		}

		return allGood;
	}

	protected ISymbolEntry GetRequiredSymbol (AnELF elf, string symbolName)
	{
		ISymbolEntry? symbolEntry = elf.GetSymbol (symbolName);
		if (symbolEntry == null) {
			throw new InvalidOperationException ($"Internal error: symbol not found '{symbolName}', use CompatibleBinary to check validity before instantiating the class");
		}

		return symbolEntry;
	}

	static void WarnNotSupported (AnELF elf, ulong format_tag)
	{
		Util.Log?.WarningLine ($"{LogTag} reader does not support libxamarin-app.so format version 0x{format_tag:x}");
	}
}

class MarshalMethods_V2 : MarshalMethods
{
	const string formatName = "V2";

	static readonly List<string> RequiredSymbols_V2 = new List<string> {
		Constants.SymbolNames.MarshalMethodsClassCache,
		Constants.SymbolNames.MarshalMethodsClassNames,
		Constants.SymbolNames.MarshalMethodsMethodNames,
		Constants.SymbolNames.MarshalMethodsNumberOfClasses,
		Constants.SymbolNames.MarshalMethodsXamarinAppInit,
	};

	public override string FormatName => formatName;
	public override ulong FormatTag   => Constants.FormatTag_V2;

	public ulong NumberOfClasses                          { get; protected set; }
	public bool XamarinAppInitFuncValid                   { get; protected set; }
	public List<MarshalMethodsManagedClass_V2> ClassCache { get; protected set; }
	public List<string> ClassNames                        { get; protected set; }
	public List<MarshalMethodName_V2> MethodNames         { get; protected set; }

	public MarshalMethods_V2 (ILogger log, AnELF elf, ulong format_tag)
		: base (log)
	{
		NumberOfClasses = elf.GetUInt32 (Constants.SymbolNames.MarshalMethodsNumberOfClasses);

		ISymbolEntry? xamarinAppInit = elf.GetSymbol (Constants.SymbolNames.MarshalMethodsXamarinAppInit);
		if (xamarinAppInit != null) {
			XamarinAppInitFuncValid = true;
			if (xamarinAppInit.Type != SymbolType.Function) {
				Log.WarningLine ($"{LogTag} symbol {Constants.SymbolNames.MarshalMethodsXamarinAppInit} is not a function");
				XamarinAppInitFuncValid = false;
			}

			if (xamarinAppInit.Visibility != SymbolVisibility.Default) {
				Log.WarningLine ($"{LogTag} symbol {Constants.SymbolNames.MarshalMethodsXamarinAppInit} is not exported");
				XamarinAppInitFuncValid = false;
			}
		}

		ClassCache = new List<MarshalMethodsManagedClass_V2> ();
		ReadClassCache (elf, ClassCache);

		ClassNames = new List<string> ();
		ReadClassNames (elf, ClassNames);

		MethodNames = new List<MarshalMethodName_V2> ();
		ReadMethodNames (elf, MethodNames);
	}

	public static bool CompatibleBinary (AnELF elf, ulong format_tag)
	{
		return HasRequiredSymbols (elf, RequiredSymbols_V2, formatName);
	}

	void ReadMethodNames (AnELF elf, List<MarshalMethodName_V2> names)
	{
		ISymbolEntry symbolEntry = GetRequiredSymbol (elf, Constants.SymbolNames.MarshalMethodsMethodNames);
		byte[] data = elf.GetData (Constants.SymbolNames.MarshalMethodsMethodNames);
		if (data.Length == 0) {
			Log.DebugLine ($"{LogTag} {elf.FilePath} class names symbol {Constants.SymbolNames.MarshalMethodsMethodNames} is empty");
			return;
		}

		using var stream = new MemoryStream (data);
		using var reader = new BinaryReader (stream);

		bool seenLast = false;
		while (stream.Position < stream.Length) {
			var methodName = new MarshalMethodName_V2 (Log, reader, elf, symbolEntry);
			if (methodName.id == 0 && String.IsNullOrEmpty (methodName.name)) {
				// terminating entry, ignore
				seenLast = true;
				continue;
			}

			if (seenLast) {
				Log.WarningLine ($"{LogTag} method names array may be broken, contains termination entry in the middle of array");
			}

			names.Add (methodName);
		}
	}

	void ReadClassNames (AnELF elf, List<string> names)
	{
		ISymbolEntry symbolEntry = GetRequiredSymbol (elf, Constants.SymbolNames.MarshalMethodsClassNames);
		byte[] data = elf.GetData (Constants.SymbolNames.MarshalMethodsClassNames);
		if (data.Length == 0) {
			Log.DebugLine ($"{LogTag} {elf.FilePath} class names symbol {Constants.SymbolNames.MarshalMethodsClassNames} is empty");
			return;
		}

		using var stream = new MemoryStream (data);
		using var reader = new BinaryReader (stream);

		bool is64Bit = elf.Is64Bit;
		ulong sizeSoFar = 0;
		while (stream.Position < stream.Length) {
			ulong entryOffset = (ulong)reader.BaseStream.Position;

			ulong pointerOffset = Util.GetPadding<string> (sizeSoFar, is64Bit) + entryOffset;
			string name = elf.GetStringFromPointerField (symbolEntry, pointerOffset) ?? Constants.UnableToLoadDataForPointer;
			sizeSoFar += Util.ReadField (reader, ref name, sizeSoFar, is64Bit);

			names.Add (name);
		}
	}

	void ReadClassCache (AnELF elf, List<MarshalMethodsManagedClass_V2> cache)
	{
		ISymbolEntry symbolEntry = GetRequiredSymbol (elf, Constants.SymbolNames.MarshalMethodsClassCache);
		byte[] data = elf.GetData (Constants.SymbolNames.MarshalMethodsClassCache);
		if (data.Length == 0) {
			Log.DebugLine ($"{LogTag} {elf.FilePath} class cache symbol {Constants.SymbolNames.MarshalMethodsClassCache} is empty");
			return;
		}

		using var stream = new MemoryStream (data);
		using var reader = new BinaryReader (stream);

		while (stream.Position < stream.Length) {
			cache.Add (new MarshalMethodsManagedClass_V2 (Log, reader, elf, symbolEntry));
		}
	}
}
