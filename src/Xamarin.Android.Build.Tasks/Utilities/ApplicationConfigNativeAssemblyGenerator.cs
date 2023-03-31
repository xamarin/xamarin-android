using System;
using System.Collections.Generic;
using System.IO;

using Java.Interop.Tools.TypeNameMappings;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Xamarin.Android.Tasks.LLVMIR;

namespace Xamarin.Android.Tasks
{
	// Must match the MonoComponent enum in src/monodroid/jni/xamarin-app.hh
	[Flags]
	enum MonoComponent
	{
		None      = 0x00,
		Debugger  = 0x01,
		HotReload = 0x02,
		Tracing   = 0x04,
	}

	class ApplicationConfigNativeAssemblyGenerator : LlvmIrComposer
	{
		sealed class DSOCacheEntryContextDataProvider : NativeAssemblerStructContextDataProvider
		{
			public override string GetComment (object data, string fieldName)
			{
				var dso_entry = data as DSOCacheEntry;
				if (dso_entry == null) {
					throw new InvalidOperationException ("Invalid data type, expected an instance of DSOCacheEntry");
				}

				if (String.Compare ("hash", fieldName, StringComparison.Ordinal) == 0) {
					return $"hash 0x{dso_entry.hash:x}, from name: {dso_entry.HashedName}";
				}

				if (String.Compare ("name", fieldName, StringComparison.Ordinal) == 0) {
					return $"name: {dso_entry.name}";
				}

				return String.Empty;
			}
		}

		// Order of fields and their type must correspond *exactly* (with exception of the
		// ignored managed members) to that in
		// src/monodroid/jni/xamarin-app.hh DSOCacheEntry structure
		[NativeAssemblerStructContextDataProvider (typeof (DSOCacheEntryContextDataProvider))]
		sealed class DSOCacheEntry
		{
			[NativeAssembler (Ignore = true)]
			public string HashedName;

			[NativeAssembler (UsesDataProvider = true)]
			public ulong hash;
			public bool ignore;

			public string name;
			public IntPtr handle = IntPtr.Zero;
		}

		// Order of fields and their type must correspond *exactly* to that in
		// src/monodroid/jni/xamarin-app.hh AssemblyStoreAssemblyDescriptor structure
		sealed class AssemblyStoreAssemblyDescriptor
		{
			public uint data_offset;
			public uint data_size;

			public uint debug_data_offset;
			public uint debug_data_size;

			public uint config_data_offset;
			public uint config_data_size;
		}

		// Order of fields and their type must correspond *exactly* to that in
		// src/monodroid/jni/xamarin-app.hh AssemblyStoreSingleAssemblyRuntimeData structure
		sealed class AssemblyStoreSingleAssemblyRuntimeData
		{
			[NativePointer]
			public byte image_data;

			[NativePointer]
			public byte debug_info_data;

			[NativePointer]
			public byte config_data;

			[NativePointer]
			public AssemblyStoreAssemblyDescriptor descriptor;
		}

		// Order of fields and their type must correspond *exactly* to that in
		// src/monodroid/jni/xamarin-app.hh AssemblyStoreRuntimeData structure
		sealed class AssemblyStoreRuntimeData
		{
			[NativePointer]
			public byte data_start;
			public uint assembly_count;

			[NativePointer]
			public AssemblyStoreAssemblyDescriptor assemblies;
		}

		sealed class XamarinAndroidBundledAssemblyContextDataProvider : NativeAssemblerStructContextDataProvider
		{
			public override ulong GetBufferSize (object data, string fieldName)
			{
				if (String.Compare ("name", fieldName, StringComparison.Ordinal) != 0) {
					return 0;
				}

				var xaba = EnsureType<XamarinAndroidBundledAssembly> (data);
				return xaba.name_length;
			}
		}

		// Order of fields and their type must correspond *exactly* to that in
		// src/monodroid/jni/xamarin-app.hh XamarinAndroidBundledAssembly structure
		[NativeAssemblerStructContextDataProvider (typeof (XamarinAndroidBundledAssemblyContextDataProvider))]
		sealed class XamarinAndroidBundledAssembly
		{
			public int  apk_fd;
			public uint data_offset;
			public uint data_size;

			[NativePointer]
			public byte data;
			public uint name_length;

			[NativeAssembler (UsesDataProvider = true), NativePointer (PointsToPreAllocatedBuffer = true)]
			public char name;
		}

		// Keep in sync with FORMAT_TAG in src/monodroid/jni/xamarin-app.hh
		const ulong FORMAT_TAG = 0x00026E69726D6158;

		SortedDictionary <string, string> environmentVariables;
		SortedDictionary <string, string> systemProperties;
		TaskLoggingHelper log;
		StructureInstance<ApplicationConfig>? application_config;
		List<StructureInstance<DSOCacheEntry>>? dsoCache;
		List<StructureInstance<XamarinAndroidBundledAssembly>>? xamarinAndroidBundledAssemblies;

		StructureInfo<ApplicationConfig>? applicationConfigStructureInfo;
		StructureInfo<DSOCacheEntry>? dsoCacheEntryStructureInfo;
		StructureInfo<XamarinAndroidBundledAssembly>? xamarinAndroidBundledAssemblyStructureInfo;
		StructureInfo<AssemblyStoreSingleAssemblyRuntimeData> assemblyStoreSingleAssemblyRuntimeDataStructureinfo;
		StructureInfo<AssemblyStoreRuntimeData> assemblyStoreRuntimeDataStructureInfo;

		public bool UsesMonoAOT { get; set; }
		public bool UsesMonoLLVM { get; set; }
		public bool UsesAssemblyPreload { get; set; }
		public string MonoAOTMode { get; set; }
		public bool AotEnableLazyLoad { get; set; }
		public string AndroidPackageName { get; set; }
		public bool BrokenExceptionTransitions { get; set; }
		public global::Android.Runtime.BoundExceptionType BoundExceptionType { get; set; }
		public bool InstantRunEnabled { get; set; }
		public bool JniAddNativeMethodRegistrationAttributePresent { get; set; }
		public bool HaveRuntimeConfigBlob { get; set; }
		public bool HaveAssemblyStore { get; set; }
		public int NumberOfAssembliesInApk { get; set; }
		public int NumberOfAssemblyStoresInApks { get; set; }
		public int BundledAssemblyNameWidth { get; set; } // including the trailing NUL
		public int AndroidRuntimeJNIEnvToken { get; set; }
		public int JNIEnvInitializeToken { get; set; }
		public int JNIEnvRegisterJniNativesToken { get; set; }
		public int JniRemappingReplacementTypeCount { get; set; }
		public int JniRemappingReplacementMethodIndexEntryCount { get; set; }
		public MonoComponent MonoComponents { get; set; }
		public PackageNamingPolicy PackageNamingPolicy { get; set; }
		public List<ITaskItem> NativeLibraries { get; set; }
		public bool MarshalMethodsEnabled { get; set; }

		public ApplicationConfigNativeAssemblyGenerator (IDictionary<string, string> environmentVariables, IDictionary<string, string> systemProperties, TaskLoggingHelper log)
		{
			if (environmentVariables != null) {
				this.environmentVariables = new SortedDictionary<string, string> (environmentVariables, StringComparer.Ordinal);
			}

			if (systemProperties != null) {
				this.systemProperties = new SortedDictionary<string, string> (systemProperties, StringComparer.Ordinal);
			}

			this.log = log;
		}

		public override void Init ()
		{
			dsoCache = InitDSOCache ();
			var app_cfg = new ApplicationConfig {
				uses_mono_llvm = UsesMonoLLVM,
				uses_mono_aot = UsesMonoAOT,
				aot_lazy_load = AotEnableLazyLoad,
				uses_assembly_preload = UsesAssemblyPreload,
				broken_exception_transitions = BrokenExceptionTransitions,
				instant_run_enabled = InstantRunEnabled,
				jni_add_native_method_registration_attribute_present = JniAddNativeMethodRegistrationAttributePresent,
				have_runtime_config_blob = HaveRuntimeConfigBlob,
				have_assemblies_blob = HaveAssemblyStore,
				marshal_methods_enabled = MarshalMethodsEnabled,
				bound_stream_io_exception_type = (byte)BoundExceptionType,
				package_naming_policy = (uint)PackageNamingPolicy,
				environment_variable_count = (uint)(environmentVariables == null ? 0 : environmentVariables.Count * 2),
				system_property_count = (uint)(systemProperties == null ? 0 : systemProperties.Count * 2),
				number_of_assemblies_in_apk = (uint)NumberOfAssembliesInApk,
				bundled_assembly_name_width = (uint)BundledAssemblyNameWidth,
				number_of_assembly_store_files = (uint)NumberOfAssemblyStoresInApks,
				number_of_dso_cache_entries = (uint)dsoCache.Count,
				android_runtime_jnienv_class_token = (uint)AndroidRuntimeJNIEnvToken,
				jnienv_initialize_method_token = (uint)JNIEnvInitializeToken,
				jnienv_registerjninatives_method_token = (uint)JNIEnvRegisterJniNativesToken,
				jni_remapping_replacement_type_count = (uint)JniRemappingReplacementTypeCount,
				jni_remapping_replacement_method_index_entry_count = (uint)JniRemappingReplacementMethodIndexEntryCount,
				mono_components_mask = (uint)MonoComponents,
				android_package_name = AndroidPackageName,
			};
			application_config = new StructureInstance<ApplicationConfig> (app_cfg);

			if (!HaveAssemblyStore) {
				xamarinAndroidBundledAssemblies = new List<StructureInstance<XamarinAndroidBundledAssembly>> (NumberOfAssembliesInApk);

				var emptyBundledAssemblyData = new XamarinAndroidBundledAssembly {
					apk_fd = -1,
					data_offset = 0,
					data_size = 0,
					data = 0,
					name_length = (uint)BundledAssemblyNameWidth,
					name = '\0',
				};

				for (int i = 0; i < NumberOfAssembliesInApk; i++) {
					xamarinAndroidBundledAssemblies.Add (new StructureInstance<XamarinAndroidBundledAssembly> (emptyBundledAssemblyData));
				}
			}
		}

		List<StructureInstance<DSOCacheEntry>> InitDSOCache ()
		{
			var dsos = new List<(string name, string nameLabel, bool ignore)> ();
			var nameCache = new HashSet<string> (StringComparer.OrdinalIgnoreCase);

			foreach (ITaskItem item in NativeLibraries) {
				string? name = item.GetMetadata ("ArchiveFileName");
				if (String.IsNullOrEmpty (name)) {
					name = item.ItemSpec;
				}
				name = Path.GetFileName (name);

				if (nameCache.Contains (name)) {
					continue;
				}

				dsos.Add ((name, $"dsoName{dsos.Count}", ELFHelper.IsEmptyAOTLibrary (log, item.ItemSpec)));
			}

			var dsoCache = new List<StructureInstance<DSOCacheEntry>> ();
			var nameMutations = new List<string> ();

			for (int i = 0; i < dsos.Count; i++) {
				string name = dsos[i].name;
				nameMutations.Clear();
				AddNameMutations (name);
				// All mutations point to the actual library name, but have hash of the mutated one
				foreach (string entryName in nameMutations) {
					var entry = new DSOCacheEntry {
						HashedName = entryName,
						hash = 0, // Hash is arch-specific, we compute it before writing
						ignore = dsos[i].ignore,
						name = name,
					};

					dsoCache.Add (new StructureInstance<DSOCacheEntry> (entry));
				}
			}

			return dsoCache;

			void AddNameMutations (string name)
			{
				nameMutations.Add (name);
				if (name.EndsWith (".dll.so", StringComparison.OrdinalIgnoreCase)) {
					nameMutations.Add (Path.GetFileNameWithoutExtension (Path.GetFileNameWithoutExtension (name))!);
				} else {
					nameMutations.Add (Path.GetFileNameWithoutExtension (name)!);
				}

				const string aotPrefix = "libaot-";
				if (name.StartsWith (aotPrefix, StringComparison.OrdinalIgnoreCase)) {
					AddNameMutations (name.Substring (aotPrefix.Length));
				}

				const string libPrefix = "lib";
				if (name.StartsWith (libPrefix, StringComparison.OrdinalIgnoreCase)) {
					AddNameMutations (name.Substring (libPrefix.Length));
				}
			}
		}

		protected override void MapStructures (LlvmIrGenerator generator)
		{
			applicationConfigStructureInfo = generator.MapStructure<ApplicationConfig> ();
			generator.MapStructure<AssemblyStoreAssemblyDescriptor> ();
			assemblyStoreSingleAssemblyRuntimeDataStructureinfo = generator.MapStructure<AssemblyStoreSingleAssemblyRuntimeData> ();
			assemblyStoreRuntimeDataStructureInfo = generator.MapStructure<AssemblyStoreRuntimeData> ();
			xamarinAndroidBundledAssemblyStructureInfo = generator.MapStructure<XamarinAndroidBundledAssembly> ();
			dsoCacheEntryStructureInfo = generator.MapStructure<DSOCacheEntry> ();
		}

		protected override void Write (LlvmIrGenerator generator)
		{
			generator.WriteVariable ("format_tag", FORMAT_TAG);
			generator.WriteString ("mono_aot_mode_name", MonoAOTMode);

			generator.WriteNameValueArray ("app_environment_variables", environmentVariables);
			generator.WriteNameValueArray ("app_system_properties", systemProperties);

			generator.WriteStructure (applicationConfigStructureInfo, application_config, LlvmIrVariableOptions.GlobalConstant, "application_config");

			WriteDSOCache (generator);
			WriteBundledAssemblies (generator);
			WriteAssemblyStoreAssemblies (generator);
		}

		void WriteAssemblyStoreAssemblies (LlvmIrGenerator generator)
		{
			ulong count = (ulong)(HaveAssemblyStore ? NumberOfAssembliesInApk : 0);
			generator.WriteStructureArray<AssemblyStoreSingleAssemblyRuntimeData> (assemblyStoreSingleAssemblyRuntimeDataStructureinfo, count, "assembly_store_bundled_assemblies", initialComment: "Assembly store individual assembly data");

			count = (ulong)(HaveAssemblyStore ? NumberOfAssemblyStoresInApks : 0);
			generator.WriteStructureArray<AssemblyStoreRuntimeData> (assemblyStoreRuntimeDataStructureInfo, count, "assembly_stores", initialComment: "Assembly store data");
		}

		void WriteBundledAssemblies (LlvmIrGenerator generator)
		{
			generator.WriteStructureArray (xamarinAndroidBundledAssemblyStructureInfo, xamarinAndroidBundledAssemblies, "bundled_assemblies", initialComment: $"Bundled assembly name buffers, all {BundledAssemblyNameWidth} bytes long");
		}

		void WriteDSOCache (LlvmIrGenerator generator)
		{
			bool is64Bit = generator.Is64Bit;

			// We need to hash here, because the hash is architecture-specific
			foreach (StructureInstance<DSOCacheEntry> entry in dsoCache) {
				entry.Obj.hash = HashName (entry.Obj.HashedName, is64Bit);
			}
			dsoCache.Sort ((StructureInstance<DSOCacheEntry> a, StructureInstance<DSOCacheEntry> b) => a.Obj.hash.CompareTo (b.Obj.hash));

			generator.WriteStructureArray (dsoCacheEntryStructureInfo, dsoCache, "dso_cache");
		}
	}
}
