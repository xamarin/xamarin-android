using System;
using System.Collections.Generic;
using System.IO;

using ELFSharp.ELF;
using ELFSharp.ELF.Sections;

using Xamarin.Android.Tasks;
using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

class DataProviderXamarinApp : DataProvider
{
	readonly AnELF elf;
	readonly ulong format_tag;

	public string MachineArchitecture { get; }

	public DataProviderXamarinApp (Stream inputStream, string? inputPath, ILogger log)
		: base (inputStream, inputPath, log)
	{
		string filePath = inputPath ?? "[memory]";
		if (!AnELF.TryLoad (Log, inputStream, filePath, out AnELF? maybeELF) || maybeELF == null) {
			throw new InvalidOperationException ($"Failed to load ELF image from '{filePath}'");
		}

		elf = maybeELF;
		format_tag = GetFormatTag (elf);

		MachineArchitecture = elf.AnyELF.Machine switch {
			Machine.AArch64  => "ARM64 (AArch64)",
			Machine.AMD64    => "X86_64",
			Machine.Intel386 => "X86 (i386)",
			Machine.ARM      => "ARM32 (ARM)",
			_                => $"[unsupported] {elf.AnyELF.Machine}"
		};
	}

	public MarshalMethods? GetMarshalMethods ()
	{
		if (!MarshalMethods.Supported (elf, format_tag)) {
			return null;
		}

		return MarshalMethods.Create (Log, elf, format_tag);
	}

	public string? GetAOTMode ()
	{
		if (!elf.HasSymbol (Constants.SymbolNames.MonoAotModeName)) {
			return null;
		}

		byte[]? data = GetSymbolData (Constants.SymbolNames.MonoAotModeName, out ISymbolEntry? symbolEntry);
		if (data == null || symbolEntry == null) {
			return null;
		}

		return elf.GetStringFromPointer (symbolEntry) ?? Constants.UnableToLoadDataForPointer;
	}

	public DSOCache? GetDSOCache ()
	{
		if (!elf.HasSymbol (Constants.SymbolNames.DSOCache)) {
			return null;
		}

		byte[]? data = GetSymbolData (Constants.SymbolNames.DSOCache, out ISymbolEntry? symbolEntry);
		if (data == null || data.Length == 0 || symbolEntry == null) {
			return null;
		}

		return new DSOCache (Log, data, elf, symbolEntry);
	}

	public IDictionary<string, string>? GetSystemProperties ()
	{
		return GetKeyValuePairs (Constants.SymbolNames.SystemProperties, "System properties");
	}

	public IDictionary<string, string>? GetEnvironmentVariables ()
	{
		return GetKeyValuePairs (Constants.SymbolNames.EnvironmentVariables, "Environment variables");
	}

	IDictionary<string, string>? GetKeyValuePairs (string symbolName, string description)
	{
		if (!elf.HasSymbol (symbolName)) {
			return null;
		}

		byte[]? data = GetSymbolData (symbolName, out ISymbolEntry? symbolEntry);
		if (data == null || data.Length == 0 || symbolEntry == null) {
			return null;
		}

		ulong pointerSize = (ulong)elf.PointerSize;
		ulong nEntries = (ulong)data.Length / pointerSize;
		bool oddNumberOfEntries = nEntries % 2 != 0;

		if (oddNumberOfEntries) {
			Log.WarningLine ($"  {description} array doesn't have an even number of elements");
		}

		ulong currentOffset = 0;
		string? name;
		string? value;
		var dict = new SortedDictionary<string, string> ();

		while (nEntries > 0) {
			name = GetNextEntry (symbolEntry);
			value = GetNextEntry (symbolEntry);

			if (dict.ContainsKey (name)) {
				Log.WarningLine ($"Duplicate array entry '{name}' (value: '{value}')");
				continue;
			}
			dict.Add (name, value);

			string GetNextEntry (ISymbolEntry symbol)
			{
				string ret = elf.GetStringFromPointerField (symbol, currentOffset) ?? Constants.UnableToLoadDataForPointer;
				currentOffset += pointerSize;
				nEntries--;

				return ret;
			}
		}

		return dict;
	}

	public ApplicationConfigShim? GetApplicationConfig ()
	{
		if (!elf.HasSymbol (Constants.SymbolNames.ApplicationConfig)) {
			return null;
		}

		var applicationConfig = new ApplicationConfig ();
		ulong size = 0;

		size += elf.GetPaddedSize (size, applicationConfig.uses_mono_llvm);
		size += elf.GetPaddedSize (size, applicationConfig.uses_mono_aot);
		size += elf.GetPaddedSize (size, applicationConfig.aot_lazy_load);
		size += elf.GetPaddedSize (size, applicationConfig.uses_assembly_preload);
		size += elf.GetPaddedSize (size, applicationConfig.broken_exception_transitions);
		size += elf.GetPaddedSize (size, applicationConfig.instant_run_enabled);
		size += elf.GetPaddedSize (size, applicationConfig.jni_add_native_method_registration_attribute_present);
		size += elf.GetPaddedSize (size, applicationConfig.have_runtime_config_blob);
		size += elf.GetPaddedSize (size, applicationConfig.have_assemblies_blob);
		size += elf.GetPaddedSize (size, applicationConfig.marshal_methods_enabled);
		size += elf.GetPaddedSize (size, applicationConfig.bound_stream_io_exception_type);
		size += elf.GetPaddedSize (size, applicationConfig.package_naming_policy);
		size += elf.GetPaddedSize (size, applicationConfig.environment_variable_count);
		size += elf.GetPaddedSize (size, applicationConfig.system_property_count);
		size += elf.GetPaddedSize (size, applicationConfig.number_of_assemblies_in_apk);
		size += elf.GetPaddedSize (size, applicationConfig.bundled_assembly_name_width);
		size += elf.GetPaddedSize (size, applicationConfig.number_of_assembly_store_files);
		size += elf.GetPaddedSize (size, applicationConfig.number_of_dso_cache_entries);
		size += elf.GetPaddedSize (size, applicationConfig.android_runtime_jnienv_class_token);
		size += elf.GetPaddedSize (size, applicationConfig.jnienv_initialize_method_token);
		size += elf.GetPaddedSize (size, applicationConfig.jnienv_registerjninatives_method_token);
		size += elf.GetPaddedSize (size, applicationConfig.jni_remapping_replacement_type_count);
		size += elf.GetPaddedSize (size, applicationConfig.jni_remapping_replacement_method_index_entry_count);
		size += elf.GetPaddedSize (size, applicationConfig.mono_components_mask);
		size += elf.GetPaddedSize (size, applicationConfig.android_package_name);

		byte[]? data = GetSymbolData (Constants.SymbolNames.ApplicationConfig, out ISymbolEntry? symbolEntry);
		if (data == null || symbolEntry == null) {
			return null;
		}

		switch (format_tag) {
			case Constants.FormatTag_V1:
				return GetApplicationConfig_V1 (size, data, symbolEntry);

			case Constants.FormatTag_V2:
				return GetApplicationConfig_V2 (size, data, symbolEntry);

			default:
				Log.WarningLine ($"libxamarin-app.so format 0x{format_tag:x} is not supported");
				return null;
		}
	}

	ApplicationConfigShim? GetApplicationConfig_V1 (ulong currentApplicationConfigSize, byte[] data, ISymbolEntry symbolEntry)
	{
		// Due to lack of consistent versioning, the latest "v1" binaries since commit 8bc7a3e84f95e70fe12790ac31ecd97957771cb2 are the same
		// as the first V2 binaries.  Earlier versions had different structure sizes, so if we find these sizes below, we can instead use
		// the V2 loader safely.
		const int ExpectedSize32_V2 = 68;
		const int ExpectedSize64_V2 = 72;

		if (data.Length == ExpectedSize32_V2 || data.Length == ExpectedSize64_V2) {
			Log.DebugLine ("Application config V1 with V2 structure size, forwarding to the V2 reader");
			return GetApplicationConfig_V2 (currentApplicationConfigSize, data, symbolEntry);
		}

		Log.DebugLine ("Reading application config V1");
		var appConfig = new ApplicationConfig_V1 (data, elf, symbolEntry);

		return new ApplicationConfigShim (appConfig);
	}

	ApplicationConfigShim? GetApplicationConfig_V2 (ulong currentApplicationConfigSize, byte[] data, ISymbolEntry symbolEntry)
	{
		const int ExpectedSize32 = 68;
		const int ExpectedSize64 = 72;

		Log.DebugLine ("Reading application config V2");
		var appConfig = new ApplicationConfig_V2 (data, elf, symbolEntry);

		int expectedSize = elf.Is64Bit ? ExpectedSize64 : ExpectedSize32;
		if (data.Length != expectedSize) {
			Log.WarningLine ($"Failed to read '{Constants.SymbolNames.ApplicationConfig}' data from {InputPath} (expected {expectedSize}, got {data.Length})");
			return null;
		}

		return new ApplicationConfigShim (appConfig);
	}

	byte[]? GetSymbolData (string symbolName, out ISymbolEntry? symbolEntry)
	{
		byte[] data = elf.GetData (symbolName, out symbolEntry);
		if (data.Length == 0 || symbolEntry == null) {
			string reason = symbolEntry == null ? "not found" : "is empty";
			Log.DebugLine ($"Application config symbol '{symbolName}' {reason} in {InputPath}");
			return null;
		}

		return data;
	}

	ulong GetFormatTag (AnELF elfBinary)
	{
		if (!elfBinary.HasSymbol (Constants.SymbolNames.FormatTag)) {
			return 0;
		}

		return elf.GetUInt64 (Constants.SymbolNames.FormatTag);
	}
}
