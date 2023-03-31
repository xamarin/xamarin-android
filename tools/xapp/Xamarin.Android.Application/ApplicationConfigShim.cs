using System;
using System.Collections.Generic;
using System.Globalization;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

sealed class ApplicationConfigShim
{
	// Must be kept in sync with eponymous structure in src/monodroid/jni/xamarin-app.hh
	[Flags]
	enum MonoComponent
	{
		None      = 0x00,
		Debugger  = 0x01,
		HotReload = 0x02,
		Tracing   = 0x04,
	};

	const string NotSupportedInThisVersion = "Not supported in this version";

	public string UsesMonoLlvm                                   { get; } = NotSupportedInThisVersion;
	public string UsesMonoAot                                    { get; } = NotSupportedInThisVersion;
	public string AotLazyLoad                                    { get; } = NotSupportedInThisVersion;
	public string UsesAssemblyPreload                            { get; } = NotSupportedInThisVersion;
	public string BrokenExceptionTransitions                     { get; } = NotSupportedInThisVersion;
	public string InstantRunEnabled                              { get; } = NotSupportedInThisVersion;
	public string JniAddNativeMethodRegistrationAttributePresent { get; } = NotSupportedInThisVersion;
	public string HaveRuntimeConfigBlob                          { get; } = NotSupportedInThisVersion;
	public string HaveAssembliesBlob                             { get; } = NotSupportedInThisVersion;
	public string MarshalMethodsEnabled                          { get; } = NotSupportedInThisVersion;
	public string BoundStreamIoExceptionType                     { get; } = NotSupportedInThisVersion;
	public string PackageNamingPolicy                            { get; } = NotSupportedInThisVersion;
	public string EnvironmentVariableCount                       { get; } = NotSupportedInThisVersion;
	public string SystemPropertyCount                            { get; } = NotSupportedInThisVersion;
	public string NumberOfAssembliesInApk                        { get; } = NotSupportedInThisVersion;
	public string BundledAssemblyNameWidth                       { get; } = NotSupportedInThisVersion;
	public string NumberOfAssemblyStoreFiles                     { get; } = NotSupportedInThisVersion;
	public string NumberOfDsoCacheEntries                        { get; } = NotSupportedInThisVersion;
	public string AndroidRuntimeJnienvClassToken                 { get; } = NotSupportedInThisVersion;
	public string JnienvInitializeMethodToken                    { get; } = NotSupportedInThisVersion;
	public string JnienvRegisterjninativesMethodToken            { get; } = NotSupportedInThisVersion;
	public string JniRemappingReplacementTypeCount               { get; } = NotSupportedInThisVersion;
	public string JniRemappingReplacementMethodIndexEntryCount   { get; } = NotSupportedInThisVersion;
	public string MonoComponentsMask                             { get; } = NotSupportedInThisVersion;
	public string AndroidPackageName                             { get; } = NotSupportedInThisVersion;

	public ApplicationConfigCommon NativeAppConfig               { get; }
	public ulong AppConfigFormatTag                              { get; }

	public ApplicationConfigShim (ApplicationConfig_V1 appConfig)
	{
		NativeAppConfig = appConfig;
		AppConfigFormatTag = Constants.FormatTag_V1;
	}

	public ApplicationConfigShim (ApplicationConfig_V2 appConfig)
	{
		NativeAppConfig = appConfig;
		AppConfigFormatTag = Constants.FormatTag_V2;

		UsesMonoLlvm                                   = Util.YesNo (appConfig.uses_mono_llvm);
		UsesMonoAot                                    = Util.YesNo (appConfig.uses_mono_aot);
		AotLazyLoad                                    = Util.YesNo (appConfig.aot_lazy_load);
		UsesAssemblyPreload                            = Util.YesNo (appConfig.uses_assembly_preload);
		BrokenExceptionTransitions                     = Util.YesNo (appConfig.broken_exception_transitions);
		InstantRunEnabled                              = Util.YesNo (appConfig.instant_run_enabled );
		JniAddNativeMethodRegistrationAttributePresent = Util.YesNo (appConfig.jni_add_native_method_registration_attribute_present);
		HaveRuntimeConfigBlob                          = Util.YesNo (appConfig.have_runtime_config_blob);
		HaveAssembliesBlob                             = Util.YesNo (appConfig.have_assemblies_blob);
		MarshalMethodsEnabled                          = Util.YesNo (appConfig.marshal_methods_enabled);
		BoundStreamIoExceptionType                     = FormatInt (appConfig.bound_stream_io_exception_type);
		PackageNamingPolicy                            = FormatInt (appConfig.package_naming_policy);
		EnvironmentVariableCount                       = FormatInt (appConfig.environment_variable_count);
		SystemPropertyCount                            = FormatInt (appConfig.system_property_count);
		NumberOfAssembliesInApk                        = FormatInt (appConfig.number_of_assemblies_in_apk);
		BundledAssemblyNameWidth                       = FormatInt (appConfig.bundled_assembly_name_width);
		NumberOfAssemblyStoreFiles                     = FormatInt (appConfig.number_of_assembly_store_files);
		NumberOfDsoCacheEntries                        = FormatInt (appConfig.number_of_dso_cache_entries);
		AndroidRuntimeJnienvClassToken                 = FormatToken (appConfig.android_runtime_jnienv_class_token);
		JnienvInitializeMethodToken                    = FormatToken (appConfig.jnienv_initialize_method_token);
		JnienvRegisterjninativesMethodToken            = FormatToken (appConfig.jnienv_registerjninatives_method_token);
		JniRemappingReplacementTypeCount               = FormatInt (appConfig.jni_remapping_replacement_type_count);
		JniRemappingReplacementMethodIndexEntryCount   = FormatInt (appConfig.jni_remapping_replacement_method_index_entry_count);
		MonoComponentsMask                             = $"{FormatMonoComponentMask((MonoComponent)appConfig.mono_components_mask)} 0x{appConfig.mono_components_mask:x}";
		AndroidPackageName                             = appConfig.android_package_name;
	}

	static string FormatInt (uint v) => v.ToString (CultureInfo.InvariantCulture);

	static string FormatToken (uint token) => $"0x{token:x}";

	static string FormatMonoComponentMask (MonoComponent mask)
	{
		var items = new List<string> ();

		if (mask.HasFlag (MonoComponent.Debugger)) {
			items.Add ("Debugger");
		}

		if (mask.HasFlag (MonoComponent.HotReload)) {
			items.Add ("HotReload");
		}

		if (mask.HasFlag (MonoComponent.Tracing)) {
			items.Add ("Tracing");
		}

		return String.Join (", ", items);
	}
}
