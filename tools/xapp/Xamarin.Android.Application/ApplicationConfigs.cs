using System;
using System.IO;

using ELFSharp.ELF.Sections;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

#pragma warning disable 0649

abstract class ApplicationConfigCommon
{
	protected bool Is64Bit { get; }

	protected Stream GetStream (byte[] data) => new MemoryStream (data);

	protected ApplicationConfigCommon (bool is64Bit)
	{
		Is64Bit = is64Bit;
	}
}

sealed class ApplicationConfig_V1 : ApplicationConfigCommon
{
	public ApplicationConfig_V1 (byte[] data, AnELF elf, ISymbolEntry symbolEntry)
		: base (elf.Is64Bit)
	{}
}

sealed class ApplicationConfig_V2 : ApplicationConfigCommon
{
	public readonly bool   uses_mono_llvm;
	public readonly bool   uses_mono_aot;
	public readonly bool   aot_lazy_load;
	public readonly bool   uses_assembly_preload;
	public readonly bool   broken_exception_transitions;
	public readonly bool   instant_run_enabled ;
	public readonly bool   jni_add_native_method_registration_attribute_present;
	public readonly bool   have_runtime_config_blob;
	public readonly bool   have_assemblies_blob;
	public readonly bool   marshal_methods_enabled;
	public readonly byte   bound_stream_io_exception_type;
	public readonly uint   package_naming_policy;
	public readonly uint   environment_variable_count;
	public readonly uint   system_property_count;
	public readonly uint   number_of_assemblies_in_apk;
	public readonly uint   bundled_assembly_name_width;
	public readonly uint   number_of_assembly_store_files;
	public readonly uint   number_of_dso_cache_entries;
	public readonly uint   android_runtime_jnienv_class_token;
	public readonly uint   jnienv_initialize_method_token;
	public readonly uint   jnienv_registerjninatives_method_token;
	public readonly uint   jni_remapping_replacement_type_count;
	public readonly uint   jni_remapping_replacement_method_index_entry_count;
	public readonly uint   mono_components_mask;
	public readonly string android_package_name = String.Empty;

	public ApplicationConfig_V2 (byte[] data, AnELF elf, ISymbolEntry symbolEntry)
		: base (elf.Is64Bit)
	{
		using Stream stream = GetStream (data);
		using var reader = new BinaryReader (stream);

		ulong sizeSoFar = 0;

		sizeSoFar += Util.ReadField (reader, ref uses_mono_llvm, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref uses_mono_aot, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref aot_lazy_load, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref uses_assembly_preload, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref broken_exception_transitions, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref instant_run_enabled, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref jni_add_native_method_registration_attribute_present, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref have_runtime_config_blob, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref have_assemblies_blob, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref marshal_methods_enabled, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref bound_stream_io_exception_type, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref package_naming_policy, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref environment_variable_count, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref system_property_count, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref number_of_assemblies_in_apk, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref bundled_assembly_name_width, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref number_of_assembly_store_files, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref number_of_dso_cache_entries, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref android_runtime_jnienv_class_token, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref jnienv_initialize_method_token, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref jnienv_registerjninatives_method_token, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref jni_remapping_replacement_type_count, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref jni_remapping_replacement_method_index_entry_count, sizeSoFar, Is64Bit);
		sizeSoFar += Util.ReadField (reader, ref mono_components_mask, sizeSoFar, Is64Bit);

		android_package_name = elf.GetStringFromPointerField (symbolEntry, sizeSoFar) ?? "FAILED TO READ STRING FROM BINARY";
	}
}

#pragma warning restore 0649
