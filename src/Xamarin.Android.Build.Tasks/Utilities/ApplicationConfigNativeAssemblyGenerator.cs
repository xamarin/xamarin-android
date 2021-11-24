using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Java.Interop.Tools.TypeNameMappings;
using K4os.Hash.xxHash;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

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

	class DSOCacheEntry
	{
		public ulong Hash;
		public bool Ignore;
		public string Name;
		public int NameIndex;
	}

	class ApplicationConfigNativeAssemblyGenerator : NativeAssemblyGenerator
	{
		SortedDictionary <string, string> environmentVariables;
		SortedDictionary <string, string> systemProperties;
		uint stringCounter = 0;
		uint bufferCounter = 0;

		public bool IsBundledApp { get; set; }
		public bool UsesMonoAOT { get; set; }
		public bool UsesMonoLLVM { get; set; }
		public bool UsesAssemblyPreload { get; set; }
		public string MonoAOTMode { get; set; }
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
		public MonoComponent MonoComponents { get; set; }
		public List<ITaskItem> NativeLibraries { get; set; }

		public PackageNamingPolicy PackageNamingPolicy { get; set; }

		TaskLoggingHelper log;

		public ApplicationConfigNativeAssemblyGenerator (NativeAssemblerTargetProvider targetProvider, string baseFileName, IDictionary<string, string> environmentVariables, IDictionary<string, string> systemProperties, TaskLoggingHelper log)
			: base (targetProvider, baseFileName)
		{
			if (environmentVariables != null)
				this.environmentVariables = new SortedDictionary<string, string> (environmentVariables, StringComparer.Ordinal);
			if (systemProperties != null)
			this.systemProperties = new SortedDictionary<string, string> (systemProperties, StringComparer.Ordinal);

			this.log = log;
		}

		protected override void WriteSymbols (StreamWriter output)
		{
			if (String.IsNullOrEmpty (AndroidPackageName))
				throw new InvalidOperationException ("Android package name must be set");

			if (UsesMonoAOT && String.IsNullOrEmpty (MonoAOTMode))
				throw new InvalidOperationException ("Mono AOT enabled but no AOT mode specified");

			string stringLabel = GetStringLabel ();
			WriteData (output, MonoAOTMode ?? String.Empty, stringLabel);
			WriteDataSection (output, "mono_aot_mode_name");
			WritePointer (output, MakeLocalLabel (stringLabel), "mono_aot_mode_name", isGlobal: true);

			WriteNameValueStringArray (output, "app_environment_variables", environmentVariables);
			WriteNameValueStringArray (output, "app_system_properties", systemProperties);

			WriteBundledAssemblies (output);
			WriteAssemblyStoreAssemblies (output);

			uint dsoCacheEntries = WriteDSOCache (output);

			stringLabel = GetStringLabel ();
			WriteData (output, AndroidPackageName, stringLabel);

			WriteDataSection (output, "application_config");
			WriteSymbol (output, "application_config", TargetProvider.GetStructureAlignment (true), packed: false, isGlobal: true, alwaysWriteSize: true, structureWriter: () => {
				// Order of fields and their type must correspond *exactly* to that in
				// src/monodroid/jni/xamarin-app.hh ApplicationConfig structure
				WriteCommentLine (output, "uses_mono_llvm");
				uint size = WriteData (output, UsesMonoLLVM);

				WriteCommentLine (output, "uses_mono_aot");
				size += WriteData (output, UsesMonoAOT);

				WriteCommentLine (output, "uses_assembly_preload");
				size += WriteData (output, UsesAssemblyPreload);

				WriteCommentLine (output, "is_a_bundled_app");
				size += WriteData (output, IsBundledApp);

				WriteCommentLine (output, "broken_exception_transitions");
				size += WriteData (output, BrokenExceptionTransitions);

				WriteCommentLine (output, "instant_run_enabled");
				size += WriteData (output, InstantRunEnabled);

				WriteCommentLine (output, "jni_add_native_method_registration_attribute_present");
				size += WriteData (output, JniAddNativeMethodRegistrationAttributePresent);

				WriteCommentLine (output, "have_runtime_config_blob");
				size += WriteData (output, HaveRuntimeConfigBlob);

				WriteCommentLine (output, "have_assembly_store");
				size += WriteData (output, HaveAssemblyStore);

				WriteCommentLine (output, "bound_exception_type");
				size += WriteData (output, (byte)BoundExceptionType);

				WriteCommentLine (output, "package_naming_policy");
				size += WriteData (output, (uint)PackageNamingPolicy);

				WriteCommentLine (output, "environment_variable_count");
				size += WriteData (output, environmentVariables == null ? 0 : environmentVariables.Count * 2);

				WriteCommentLine (output, "system_property_count");
				size += WriteData (output, systemProperties == null ? 0 : systemProperties.Count * 2);

				WriteCommentLine (output, "number_of_assemblies_in_apk");
				size += WriteData (output, NumberOfAssembliesInApk);

				WriteCommentLine (output, "bundled_assembly_name_width");
				size += WriteData (output, BundledAssemblyNameWidth);

				WriteCommentLine (output, "number_of_assembly_store_files");
				size += WriteData (output, NumberOfAssemblyStoresInApks);

				WriteCommentLine (output, "number_of_dso_cache_entries");
				size += WriteData (output, dsoCacheEntries);

				WriteCommentLine (output, "mono_components_mask");
				size += WriteData (output, (uint)MonoComponents);

				WriteCommentLine (output, "android_package_name");
				size += WritePointer (output, MakeLocalLabel (stringLabel));

				return size;
			});
		}

		uint WriteDSOCache (StreamWriter output)
		{
			output.WriteLine ();

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

			var dsoCache = new List<DSOCacheEntry> ();
			var nameMutations = new List<string> ();

			for (int i = 0; i < dsos.Count; i++) {
				string name = dsos[i].name;
				nameMutations.Clear();
				AddNameMutations (name);
				foreach (string entryName in nameMutations) {
					dsoCache.Add (
						new DSOCacheEntry {
							Hash = HashName (entryName),
							Ignore = dsos[i].ignore,
							Name = entryName,
							NameIndex = i,
						}
					);
				}
			}

			dsoCache.Sort ((DSOCacheEntry a, DSOCacheEntry b) => a.Hash.CompareTo (b.Hash));
			WriteCommentLine (output, "DSO names");
			foreach (var dso in dsos) {
				WriteData (output, dso.name, dso.nameLabel, isGlobal: false);
				output.WriteLine ();
			}

			string label = "dso_cache";
			WriteCommentLine (output, "DSO name hash table/cache");
			WriteDataSection (output, label);
			WriteStructureSymbol (output, label, alignBits: TargetProvider.MapModulesAlignBits, isGlobal: true);

			uint size = 0;
			foreach (DSOCacheEntry entry in dsoCache) {
				size += WriteStructure (output, packed: false, structureWriter: () => WriteDSOCacheEntry (output, entry, dsos[entry.NameIndex].nameLabel));
			}
			WriteStructureSize (output, label, size);
			output.WriteLine ();

			return (uint)dsoCache.Count;

			ulong HashName (string name)
			{
				byte[] nameBytes = Encoding.UTF8.GetBytes (name);
				if (TargetProvider.Is64Bit) {
					return XXH64.DigestOf (nameBytes, 0, nameBytes.Length);
				}

				return (ulong)XXH32.DigestOf (nameBytes, 0, nameBytes.Length);
			}

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
			}
		}

		uint WriteDSOCacheEntry (StreamWriter output, DSOCacheEntry entry, string nameLabel)
		{
			// Each entry must be identical to src/monodroid/jni/xamarin-app.hh DSOCacheEntry structure
			uint size = 0;

			WriteCommentLine (output, $"hash: 0x{entry.Hash:x} ('{entry.Name}')");
			size += WriteData (output, entry.Hash);

			WriteCommentLine (output, "ignore");
			size += WriteData (output, entry.Ignore);

			WriteCommentLine (output, "name");
			size += WritePointer (output, MakeLocalLabel (nameLabel));

			WriteCommentLine (output, "handle");
			size += WritePointer (output);
			return size;
		}

		void WriteAssemblyStoreAssemblies (StreamWriter output)
		{
			output.WriteLine ();

			string label = "assembly_store_bundled_assemblies";
			WriteCommentLine (output, "Assembly store individual assembly data");
			WriteDataSection (output, label);
			WriteStructureSymbol (output, label, alignBits: TargetProvider.MapModulesAlignBits, isGlobal: true);

			uint size = 0;
			if (HaveAssemblyStore) {
				for (int i = 0; i < NumberOfAssembliesInApk; i++) {
					size += WriteStructure (output, packed: false, structureWriter: () => WriteAssemblyStoreAssembly (output));
				}
			}
			WriteStructureSize (output, label, size);

			output.WriteLine ();

			label = "assembly_stores";
			WriteCommentLine (output, "Assembly store data");
			WriteDataSection (output, label);
			WriteStructureSymbol (output, label, alignBits: TargetProvider.MapModulesAlignBits, isGlobal: true);

			size = 0;
			if (HaveAssemblyStore) {
				for (int i = 0; i < NumberOfAssemblyStoresInApks; i++) {
					size += WriteStructure (output, packed: false, structureWriter: () => WriteAssemblyStore (output));
				}
			}
			WriteStructureSize (output, label, size);
		}

		uint WriteAssemblyStoreAssembly (StreamWriter output)
		{
			// Order of fields and their type must correspond *exactly* to that in
			// src/monodroid/jni/xamarin-app.hh AssemblyStoreSingleAssemblyRuntimeData structure
			WriteCommentLine (output, "image_data");
			uint size = WritePointer (output);

			WriteCommentLine (output, "debug_info_data");
			size += WritePointer (output);

			WriteCommentLine (output, "config_data");
			size += WritePointer (output);

			WriteCommentLine (output, "descriptor");
			size += WritePointer (output);

			output.WriteLine ();

			return size;
		}

		uint WriteAssemblyStore (StreamWriter output)
		{
			// Order of fields and their type must correspond *exactly* to that in
			// src/monodroid/jni/xamarin-app.hh AssemblyStoreRuntimeData structure
			WriteCommentLine (output, "data_start");
			uint size = WritePointer (output);

			WriteCommentLine (output, "assembly_count");
			size += WriteData (output, (uint)0);

			WriteCommentLine (output, "assemblies");
			size += WritePointer (output);

			output.WriteLine ();

			return size;
		}

		void WriteBundledAssemblies (StreamWriter output)
		{
			output.WriteLine ();

			WriteCommentLine (output, $"Bundled assembly name buffers, all {BundledAssemblyNameWidth} bytes long");
			WriteSection (output, ".bss.bundled_assembly_names", hasStrings: false, writable: true, nobits: true);

			List<string> name_labels = null;
			if (!HaveAssemblyStore) {
				name_labels = new List<string> ();
				for (int i = 0; i < NumberOfAssembliesInApk; i++) {
					string bufferLabel = GetBufferLabel ();
					WriteBufferAllocation (output, bufferLabel, (uint)BundledAssemblyNameWidth);
					name_labels.Add (bufferLabel);
				}
			}

			output.WriteLine ();

			string label = "bundled_assemblies";
			WriteCommentLine (output, "Bundled assemblies data");
			WriteDataSection (output, label);
			WriteStructureSymbol (output, label, alignBits: TargetProvider.MapModulesAlignBits, isGlobal: true);

			uint size = 0;
			if (!HaveAssemblyStore) {
				for (int i = 0; i < NumberOfAssembliesInApk; i++) {
					size += WriteStructure (output, packed: false, structureWriter: () => WriteBundledAssembly (output, MakeLocalLabel (name_labels[i])));
				}
			}
			WriteStructureSize (output, label, size);

			output.WriteLine ();
		}


		uint WriteBundledAssembly (StreamWriter output, string nameLabel)
		{
			// Order of fields and their type must correspond *exactly* to that in
			// src/monodroid/jni/xamarin-app.hh XamarinAndroidBundledAssembly structure

			WriteCommentLine (output, "apk_fd");
			uint size = WriteData (output, (int)-1);

			WriteCommentLine (output, "data_offset");
			size += WriteData (output, (uint)0);

			WriteCommentLine (output, "data_size");
			size += WriteData (output, (uint)0);

			WriteCommentLine (output, "data");
			size += WritePointer (output);

			WriteCommentLine (output, "name_length");
			size += WriteData (output, (uint)0);

			WriteCommentLine (output, "name");
			size += WritePointer (output, nameLabel);

			output.WriteLine ();

			return size;
		}

		void WriteNameValueStringArray (StreamWriter output, string label, SortedDictionary<string, string> entries)
		{
			if (entries == null || entries.Count == 0) {
				WriteDataSection (output, label);
				WriteSymbol (output, label, TargetProvider.GetStructureAlignment (true), packed: false, isGlobal: true, alwaysWriteSize: true, structureWriter: null);
				return;
			}

			var entry_labels = new List <string> ();
			foreach (var kvp in entries) {
				string name = kvp.Key;
				string value = kvp.Value ?? String.Empty;
				string stringLabel = GetStringLabel ();
				WriteData (output, name, stringLabel);
				entry_labels.Add (stringLabel);

				stringLabel = GetStringLabel ();
				WriteData (output, value, stringLabel);
				entry_labels.Add (stringLabel);

			}

			WriteDataSection (output, label);
			WriteSymbol (output, label, TargetProvider.GetStructureAlignment (true), packed: false, isGlobal: true, alwaysWriteSize: true, structureWriter: () => {
				uint size = 0;

				foreach (string l in entry_labels) {
					size += WritePointer (output, MakeLocalLabel (l));
				}

				return size;
			});
		}

		void WriteDataSection (StreamWriter output, string tag)
		{
			WriteSection (output, $".data.{tag}", hasStrings: false, writable: true);
		}

		string GetStringLabel ()
		{
			stringCounter++;
			return $"env.str.{stringCounter}";
		}

		string GetBufferLabel ()
		{
			bufferCounter++;
			return $"env.buf.{bufferCounter}";
		}
	};
}
