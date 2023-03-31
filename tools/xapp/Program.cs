using System;
using System.Collections.Generic;

using Mono.Options;

using Xamarin.Android.Application;
using Xamarin.Android.Application.Utilities;
using Xamarin.Android.AssemblyStore;

namespace Xamarin.XApp;

sealed class ParsedOptions
{
	public bool TypeMaps;
	public bool AssemblyStore;
	public bool ExtractAssemblies;
}

class App
{
	static XamarinLoggingHelper log = new XamarinLoggingHelper ();

	static int Main (string[] args)
	{
		Util.Log = log;
		log.Level = LogLevel.Debug;

		var parsedOptions = new ParsedOptions ();

		var opts = new OptionSet {
			"Usage: xapp [OPTIONS] <path/to/file> [path/to/file ..]",
			"",
			"OPTIONS are:",
			"",
			{ "t|typemaps", "Show detailed typemap information", v => parsedOptions.TypeMaps = true },
			{ "s|assembly-store", "Show detailed assembly store information", v => parsedOptions.AssemblyStore = true },
			{ "e|extract-assemblies", "Extract assemblies from the APK/AAB archive or assembly store blob", v => parsedOptions.ExtractAssemblies = true },
		};

		List<string> rest= opts.Parse (args);

		foreach (string inputFile in rest) {
			InputReader? reader = InputType.Detect (inputFile, log);
			if (reader == null) {
				// TODO: proper logging
				log.WarningLine ($"Input file '{inputFile}' is not of supported type.");
				continue;
			}

			PrintInfo (reader, inputFile);
		}

		return 0;
	}

	static void PrintInfo (InputReader reader, string inputFile)
	{
		log.DebugLine ($"Got reader '{reader}' for file '{inputFile}'");
		if (reader.SupportsAssemblyStore) {
			PrintAssemblyStoreInfo (reader.GetAssemblyStore ());
		}

		if (reader.SupportsAppInfo) {
			PrintAppInfo (reader.GetAppInfo ());
		}

		if (reader.SupportsXamarinApp) {
			PrintXamarinAppInfo (reader.GetXamarinApp ());
		}
	}

	static void PrintXamarinAppInfo (DataProviderXamarinApp? xamarinAppInfo)
	{
		if (xamarinAppInfo == null) {
			return;
		}

		log.InfoLine ("== libxamarin-app.so information ==");
		log.InfoLine ("-----------------------------------");
		log.InfoLine ("Miscellaneous");
		log.StatusLine ("  Machine architecture", xamarinAppInfo.MachineArchitecture);
		log.StatusLine ("  Mono AOT mode name", xamarinAppInfo.GetAOTMode () ?? "[information unavailable]");
		log.InfoLine ();

		log.InfoLine ("Application configuration structure");
		ApplicationConfigShim? applicationConfig = xamarinAppInfo.GetApplicationConfig ();
		if (applicationConfig == null) {
			log.InfoLine ("  not available");
		} else {
			PrintAppConfig (applicationConfig);
		}

		log.InfoLine ();
		log.InfoLine ("Application environment variables");
		IDictionary<string, string>? dict = xamarinAppInfo.GetEnvironmentVariables ();
		if (dict == null || dict.Count == 0) {
			log.InfoLine ("  none defined");
		} else {
			PrintKeyValuePairs (dict);
		}

		log.InfoLine ();
		log.InfoLine ("Application system properties");
		dict = xamarinAppInfo.GetSystemProperties ();;
		if (dict == null || dict.Count == 0) {
			log.InfoLine ("  none defined");
		} else {
			PrintKeyValuePairs (dict);
		}

		log.InfoLine ();
		log.InfoLine ("DSO (shared library) cache");
		DSOCache? dsoCache = xamarinAppInfo.GetDSOCache ();
		if (dsoCache == null) {
			log.InfoLine ("  not available");
		} else if (dsoCache.Entries.Count == 0) {
			log.InfoLine ("  empty");
		} else {
			PrintDSOCache (dsoCache);
		}

		log.InfoLine ();
		log.InfoLine ("Marshal methods");
		MarshalMethods? marshalMethods = xamarinAppInfo.GetMarshalMethods ();
		if (marshalMethods == null) {
			log.InfoLine ("  not available");
		} else {
			PrintMarshalMethods (marshalMethods);
		}
	}

	static void PrintMarshalMethods (MarshalMethods marshalMethods)
	{
		if (marshalMethods is MarshalMethods_V2 v2) {
			PrintMarshalMethods (v2);
		} else {
			log.WarningLine ($"Marshal methods format {marshalMethods.FormatName} not supported");
		}
	}

	static void PrintMarshalMethods (MarshalMethods_V2 marshalMethods)
	{
		log.StatusLine ("  Format version", marshalMethods.FormatName);
		log.StatusLine ("  Init function", marshalMethods.XamarinAppInitFuncValid ? "valid" : "invalid");
		log.StatusLine ("  Number of classes with marshal methods", marshalMethods.NumberOfClasses);

		const string classCacheLabel = "  Class cache (managed tokens)";
		if (marshalMethods.ClassCache.Count == 0) {
			log.StatusLine (classCacheLabel, "empty");
		} else {
			log.LogLine (LogLevel.Info, $"{classCacheLabel}:", XamarinLoggingHelper.StatusLabel);
			for (int i = 0; i < marshalMethods.ClassCache.Count; i++) {
				MarshalMethodsManagedClass_V2 entry = marshalMethods.ClassCache[i];
				log.StatusLine ($"    {i:0000}", $"0x{entry.token:x}");
			}
		}

		const string classNamesLabel = "  Class names";
		if (marshalMethods.ClassNames.Count == 0) {
			log.StatusLine (classNamesLabel, "none");
		} else {
			log.LogLine (LogLevel.Info, $"{classNamesLabel}:", XamarinLoggingHelper.StatusLabel);
			for (int i = 0; i < marshalMethods.ClassNames.Count; i++) {
				log.StatusLine ($"    {i:0000}", marshalMethods.ClassNames[i]);
			}
		}

		const string methodNamesLabel = "  Method names";
		if (marshalMethods.MethodNames.Count == 0) {
			log.StatusLine (methodNamesLabel, "none");
		} else {
			log.LogLine (LogLevel.Info, $"{methodNamesLabel}:", XamarinLoggingHelper.StatusLabel);
			for (int i = 0; i < marshalMethods.MethodNames.Count; i++) {
				MarshalMethodName_V2 methodName = marshalMethods.MethodNames[i];
				log.StatusLine ($"    {i:0000}", $"{methodName.name}; id: 0x{methodName.id:x} (assembly index: {methodName.AssemblyIndex}; method token: 0x{methodName.MethodToken:X})");
			}
		}
	}

	static void PrintDSOCache (DSOCache cache)
	{
		for (int i = 0; i < cache.Entries.Count; i++) {
			DSOCacheEntry entry = cache.Entries[i];
			log.StatusLine ($"  {i}", entry.name);
			log.StatusLine ("    Hash", $"0x{entry.hash:x08}");
			log.StatusYesNo ($"    Ignored", entry.ignore);
			log.InfoLine ();
		}
	}

	static void PrintKeyValuePairs (IDictionary<string, string> envvars)
	{
		foreach (var kvp in envvars) {
			log.StatusLine ($"  {kvp.Key}", kvp.Value);
		}
	}

	static void PrintAppConfig (ApplicationConfigShim applicationConfig)
	{
		log.StatusLine ("  Uses Mono LLVM", applicationConfig.UsesMonoLlvm);
		log.StatusLine ("  Uses Mono AOT", applicationConfig.UsesMonoAot);
		log.StatusLine ("  AOT lazy load", applicationConfig.AotLazyLoad);
		log.StatusLine ("  Uses assembly preload", applicationConfig.UsesAssemblyPreload);
		log.StatusLine ("  Broken exception transitions", applicationConfig.BrokenExceptionTransitions);
		log.StatusLine ("  Instant run enabled", applicationConfig.InstantRunEnabled);
		log.StatusLine ("  JNI add native method registration attribute present", applicationConfig.JniAddNativeMethodRegistrationAttributePresent);
		log.StatusLine ("  Have runtime config blob", applicationConfig.HaveRuntimeConfigBlob);
		log.StatusLine ("  Have assemblies blob", applicationConfig.HaveAssembliesBlob);
		log.StatusLine ("  Marshal methods enabled", applicationConfig.MarshalMethodsEnabled);
		log.StatusLine ("  Bound stream IO exception type", applicationConfig.BoundStreamIoExceptionType);
		log.StatusLine ("  Package naming policy", applicationConfig.PackageNamingPolicy);
		log.StatusLine ("  Environment variables count", applicationConfig.EnvironmentVariableCount);
		log.StatusLine ("  System property count", applicationConfig.SystemPropertyCount);
		log.StatusLine ("  Number of assemblies in APK", applicationConfig.NumberOfAssembliesInApk);
		log.StatusLine ("  Bundled assembly name width", applicationConfig.BundledAssemblyNameWidth);
		log.StatusLine ("  Number of assembly store files", applicationConfig.NumberOfAssemblyStoreFiles);
		log.StatusLine ("  Number of DSO cache entries", applicationConfig.NumberOfDsoCacheEntries);
		log.StatusLine ("  Android.Runtime.JNIEnv class token", applicationConfig.AndroidRuntimeJnienvClassToken);
		log.StatusLine ("  Android.Runtime.JNIEnv.Initialize method token", applicationConfig.JnienvInitializeMethodToken);
		log.StatusLine ("  Android.Runtime.JNIEnv.RegisterJniNatives method token", applicationConfig.JnienvRegisterjninativesMethodToken);
		log.StatusLine ("  JNI remapping replacement type count", applicationConfig.JniRemappingReplacementTypeCount);
		log.StatusLine ("  JNI remapping replacement method index entry count", applicationConfig.JniRemappingReplacementMethodIndexEntryCount);
		log.StatusLine ("  Mono components mask", applicationConfig.MonoComponentsMask);
		log.StatusLine ("  Android package name", applicationConfig.AndroidPackageName);
	}

	static void PrintAppInfo (DataProviderAppInfo? appInfo)
	{
		if (appInfo == null) {
			return;
		}

		// TODO: Validation
		//  - Assembly stores: number of blobs must be number of supported ABIs (if abi-specific blobs count is != 0)
		//  - Assembly stores: must have a manifest
		//  - Assembly stores: total number of stores must be at least 1
		//  - Assembly stores: validate number of assemblies - must match the manifest, all ABI-specific stores must have the same number of assemblies
		//  - Supported ABIs must be at least 1

		log.InfoLine ();
		log.InfoLine ("== Application info ==");
		log.InfoLine ("----------------------");
		log.StatusLine ("  Archive type", appInfo.ArchiveType);
		log.StatusYesNoLine ($"  Signed", appInfo.IsSigned);
		log.StatusLine ("  Package name", appInfo.PackageName);
		log.StatusLine ("  Main activity name", appInfo.MainActivityName);
		log.StatusLine ("  Application name", appInfo.ApplicationName);
		log.StatusLine ("  Launcher label", appInfo.ApplicationLabel);
		log.StatusLine ("  Minimum SDK version", appInfo.MinSdkVersion);
		log.StatusLine ("  Target SDK version", appInfo.TargetSdkVersion);
		log.StatusYesNoLine ("  Extracts native libs", appInfo.ExtractsNativeLibs);
		log.StatusYesNoLine ("  Uses permissions", appInfo.UsesPermissions.Count != 0);
		if (appInfo.UsesPermissions.Count != 0) {
			foreach (string permission in appInfo.UsesPermissions) {
				log.MessageLine ($"    {permission}");
			}
		}

		string supportedAbis;
		if (appInfo.SupportedAbis.Length == 0) {
			supportedAbis = "none";
		} else {
			supportedAbis = String.Join (", ", appInfo.SupportedAbis);
		}
		log.StatusLine ($"  Supported ABIs", supportedAbis);

		log.StatusYesNoLine ($"  Uses assembly stores", appInfo.UsesAssemblyStores);
		if (appInfo.UsesAssemblyStores) {
			log.StatusYesNoLine ($"  Assembly stores manifest present", appInfo.HasAssemblyStoresManifest);
			log.StatusLine ($"  Total number of assembly store blobs", appInfo.TotalNumberOfAssemblyStores);
			log.StatusLine ($"  Number of ABI-specific assembly store blobs", appInfo.NumberOfAbiAssemblyStores);
		}

		log.StatusYesNoLine ($"  Is a classic XA app", appInfo.IsClassicXA);
		log.StatusYesNoLine ($"  Contains runtime config blob", appInfo.HasRuntimeConfigBlob);
		log.StatusYesNoLine ($"  Debuggable", appInfo.IsDebug);
		log.StatusYesNoLine ($"  Profileable", appInfo.IsProfileable);
		log.StatusYesNoLine ($"  Test-only build", appInfo.IsTestOnly);
		log.StatusYesNoLine ($"  Uses AOT", appInfo.UsesAOT);
	}

	static void PrintAssemblyStoreInfo (DataProviderAssemblyStore? assemblyStore)
	{
		if (assemblyStore == null) {
			return;
		}

		assemblyStore.EnsureFullAssemblyInformation ();

		log.MessageLine ("Assembly store info:");
		log.StatusLine ("  Number of read blobs", assemblyStore.Blobs.Count);
		log.StatusYesNoLine ("  Manifest present", assemblyStore.Manifest != null);
		log.MessageLine ();
		log.MessageLine ("Individual blob information:");

		foreach (AssemblyStoreReader blob in assemblyStore.Blobs) {
			log.StatusLine ("       ID", blob.StoreID);
			log.StatusLine ("  Version", blob.Version);
			if (blob.IsArchSpecific) {
				log.StatusLine ($"  Specific to architecture", ArchitectureName (blob));
			}
			log.StatusLine ("  Local entry count", blob.LocalEntryCount);
			log.StatusLine ("  Global entry count", blob.GlobalEntryCount);
			log.MessageLine ("  Assemblies in this blob:");
			foreach (AssemblyStoreAssembly asm in blob.Assemblies) {
				string not = asm.IsCompressed ? String.Empty : "not ";
				log.StatusLine ($"    {asm.Name}", $"{not}compressed");
			}
			log.MessageLine ();
		}
	}

	static string ArchitectureName (AssemblyStoreReader blob)
	{
		return String.IsNullOrEmpty (blob.Arch) ? "yes, undetermined" : blob.Arch;
	}
}
