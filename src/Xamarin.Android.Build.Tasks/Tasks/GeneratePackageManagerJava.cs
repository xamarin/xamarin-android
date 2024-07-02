// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Java.Interop.Tools.TypeNameMappings;
using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	using PackageNamingPolicyEnum   = PackageNamingPolicy;

	public class GeneratePackageManagerJava : AndroidTask
	{
		public override string TaskPrefix => "GPM";

		Guid buildId = Guid.NewGuid ();

		[Required]
		public ITaskItem[] ResolvedAssemblies { get; set; }

		[Required]
		public ITaskItem[] ResolvedUserAssemblies { get; set; }

		public ITaskItem[] NativeLibraries { get; set; }

		public ITaskItem[] MonoComponents { get; set; }

		public ITaskItem[] SatelliteAssemblies { get; set; }

		public bool UseAssemblyStore { get; set; }

		[Required]
		public string OutputDirectory { get; set; }

		[Required]
		public string EnvironmentOutputDirectory { get; set; }

		[Required]
		public string MainAssembly { get; set; }

		[Required]
		public string TargetFrameworkVersion { get; set; }

		[Required]
		public string Manifest { get; set; }

		[Required]
		public string [] SupportedAbis { get; set; }

		[Required]
		public string AndroidPackageName { get; set; }

		[Required]
		public bool EnablePreloadAssembliesDefault { get; set; }

		[Required]
		public bool InstantRunEnabled { get; set; }

		public bool EnableMarshalMethods { get; set; }
		public string RuntimeConfigBinFilePath { get; set; }
		public string BoundExceptionType { get; set; }

		public string PackageNamingPolicy { get; set; }
		public string Debug { get; set; }
		public ITaskItem[] Environments { get; set; }
		public string AndroidAotMode { get; set; }
		public bool AndroidAotEnableLazyLoad { get; set; }
		public bool EnableLLVM { get; set; }
		public string HttpClientHandlerType { get; set; }
		public string TlsProvider { get; set; }
		public string AndroidSequencePointsMode { get; set; }
		public bool EnableSGenConcurrent { get; set; }
		public string? CustomBundleConfigFile { get; set; }

		[Output]
		public string BuildId { get; set; }

		bool _Debug {
			get {
				return string.Equals (Debug, "true", StringComparison.OrdinalIgnoreCase);
			}
		}

		public override bool RunTask ()
		{
			BuildId = buildId.ToString ();
			Log.LogDebugMessage ("  [Output] BuildId: {0}", BuildId);

			var doc = AndroidAppManifest.Load (Manifest, MonoAndroidHelper.SupportedVersions);
			int minApiVersion = doc.MinSdkVersion == null ? 4 : (int) doc.MinSdkVersion;
			// We need to include any special assemblies in the Assemblies list
			var mainFileName = Path.GetFileName (MainAssembly);

			using (var pkgmgr = MemoryStreamPool.Shared.CreateStreamWriter ()) {
				pkgmgr.WriteLine ("package mono;");

				// Write all the user assemblies
				pkgmgr.WriteLine ("public class MonoPackageManager_Resources {");
				pkgmgr.WriteLine ("\tpublic static String[] Assemblies = new String[]{");

				pkgmgr.WriteLine ("\t\t/* We need to ensure that \"{0}\" comes first in this list. */", mainFileName);
				pkgmgr.WriteLine ("\t\t\"" + mainFileName + "\",");
				foreach (var assembly in ResolvedUserAssemblies) {
					if (string.Compare (Path.GetFileName (assembly.ItemSpec), mainFileName, StringComparison.OrdinalIgnoreCase) == 0)
						continue;
					pkgmgr.WriteLine ("\t\t\"" + Path.GetFileName (assembly.ItemSpec) + "\",");
				}
				foreach (var assembly in MonoAndroidHelper.GetFrameworkAssembliesToTreatAsUserAssemblies (ResolvedAssemblies)) {
					if (string.Compare (Path.GetFileName (assembly.ItemSpec), mainFileName, StringComparison.OrdinalIgnoreCase) == 0)
						continue;
					pkgmgr.WriteLine ("\t\t\"" + Path.GetFileName (assembly.ItemSpec) + "\",");
				}

				// Write the assembly dependencies
				pkgmgr.WriteLine ("\t};");
				pkgmgr.WriteLine ("\tpublic static String[] Dependencies = new String[]{");

				//foreach (var assembly in assemblies.Except (args.Assemblies)) {
				//        if (args.SharedRuntime && !Toolbox.IsInSharedRuntime (assembly))
				//                pkgmgr.WriteLine ("\t\t\"" + Path.GetFileName (assembly) + "\",");
				//}

				pkgmgr.WriteLine ("\t};");

				pkgmgr.WriteLine ("}");
				pkgmgr.Flush ();

				// Only copy to the real location if the contents actually changed
				var dest = Path.GetFullPath (Path.Combine (OutputDirectory, "MonoPackageManager_Resources.java"));

				Files.CopyIfStreamChanged (pkgmgr.BaseStream, dest);
			}

			AddEnvironment ();

			return !Log.HasLoggedErrors;
		}

		static internal AndroidTargetArch GetAndroidTargetArchForAbi (string abi) => MonoAndroidHelper.AbiToTargetArch (abi);

		static readonly string[] defaultLogLevel = {"MONO_LOG_LEVEL", "info"};
		static readonly string[] defaultMonoDebug = {"MONO_DEBUG", "gen-compact-seq-points"};
		static readonly string[] defaultHttpMessageHandler = {"XA_HTTP_CLIENT_HANDLER_TYPE", "System.Net.Http.HttpClientHandler, System.Net.Http"};
		static readonly string[] defaultTlsProvider = {"XA_TLS_PROVIDER", "btls"};

		void AddEnvironment ()
		{
			bool usesMonoAOT = false;
			var environmentVariables = new Dictionary<string, string> (StringComparer.Ordinal);
			var systemProperties = new Dictionary<string, string> (StringComparer.Ordinal);

			if (!Enum.TryParse (PackageNamingPolicy, out PackageNamingPolicy pnp)) {
				pnp = PackageNamingPolicyEnum.LowercaseCrc64;
			}

			AotMode aotMode = AotMode.None;
			if (!string.IsNullOrEmpty (AndroidAotMode) && Aot.GetAndroidAotMode (AndroidAotMode, out aotMode) && aotMode != AotMode.None) {
				usesMonoAOT = true;
			}

			SequencePointsMode sequencePointsMode;
			if (!Aot.TryGetSequencePointsMode (AndroidSequencePointsMode, out sequencePointsMode))
				sequencePointsMode = SequencePointsMode.None;

			// Even though environment files were potentially parsed in GenerateJavaStubs, we need to do it here again because we might have additional environment
			// files (generated by us) which weren't present by the time GeneratJavaStubs ran.
			var environmentParser = new EnvironmentFilesParser {
				BrokenExceptionTransitions = false,
				UsesAssemblyPreload = EnablePreloadAssembliesDefault,
			};
			environmentParser.Parse (Environments, sequencePointsMode, Log);

			foreach (string line in environmentParser.EnvironmentVariableLines) {
				AddEnvironmentVariableLine (line);
			}

			if (_Debug && !environmentParser.HaveLogLevel) {
				AddEnvironmentVariable (defaultLogLevel[0], defaultLogLevel[1]);
			}

			if (sequencePointsMode != SequencePointsMode.None && !environmentParser.HaveMonoDebug) {
				AddEnvironmentVariable (defaultMonoDebug[0], defaultMonoDebug[1]);
			}

			if (!environmentParser.HavebuildId)
				AddEnvironmentVariable ("XAMARIN_BUILD_ID", BuildId);

			if (!environmentParser.HaveHttpMessageHandler) {
				if (HttpClientHandlerType == null)
					AddEnvironmentVariable (defaultHttpMessageHandler[0], defaultHttpMessageHandler[1]);
				else
					AddEnvironmentVariable ("XA_HTTP_CLIENT_HANDLER_TYPE", HttpClientHandlerType.Trim ());
			}

			if (!environmentParser.HaveMonoGCParams) {
				if (EnableSGenConcurrent)
					AddEnvironmentVariable ("MONO_GC_PARAMS", "major=marksweep-conc");
				else
					AddEnvironmentVariable ("MONO_GC_PARAMS", "major=marksweep");
			}

			global::Android.Runtime.BoundExceptionType boundExceptionType;
			if (String.IsNullOrEmpty (BoundExceptionType) || String.Compare (BoundExceptionType, "System", StringComparison.OrdinalIgnoreCase) == 0) {
				boundExceptionType = global::Android.Runtime.BoundExceptionType.System;
			} else if (String.Compare (BoundExceptionType, "Java", StringComparison.OrdinalIgnoreCase) == 0) {
				boundExceptionType = global::Android.Runtime.BoundExceptionType.Java;
			} else {
				throw new InvalidOperationException ($"Unsupported BoundExceptionType value '{BoundExceptionType}'");
			}

			int assemblyNameWidth = 0;
			Encoding assemblyNameEncoding = Encoding.UTF8;

			Action<ITaskItem> updateNameWidth = (ITaskItem assembly) => {
				if (UseAssemblyStore) {
					return;
				}

				string assemblyName = Path.GetFileName (assembly.ItemSpec);
				int nameBytes = assemblyNameEncoding.GetBytes (assemblyName).Length;
				if (nameBytes > assemblyNameWidth) {
					assemblyNameWidth = nameBytes;
				}
			};

			int assemblyCount = 0;
			bool enableMarshalMethods = EnableMarshalMethods;
			HashSet<string> archAssemblyNames = null;
			HashSet<string> uniqueAssemblyNames = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
			Action<ITaskItem> updateAssemblyCount = (ITaskItem assembly) => {
				string? culture = MonoAndroidHelper.GetAssemblyCulture (assembly);
				string fileName = Path.GetFileName (assembly.ItemSpec);
				string assemblyName;

				if (String.IsNullOrEmpty (culture)) {
					assemblyName = fileName;
				} else {
					assemblyName = $"{culture}/{fileName}";
				}

				if (!uniqueAssemblyNames.Contains (assemblyName)) {
					uniqueAssemblyNames.Add (assemblyName);
				}

				string abi = MonoAndroidHelper.GetAssemblyAbi (assembly);
				archAssemblyNames ??= new HashSet<string> (StringComparer.OrdinalIgnoreCase);

				if (!archAssemblyNames.Contains (assemblyName)) {
					assemblyCount++;
					archAssemblyNames.Add (assemblyName);
				}
			};

			if (SatelliteAssemblies != null) {
				foreach (ITaskItem assembly in SatelliteAssemblies) {
					updateNameWidth (assembly);
					updateAssemblyCount (assembly);
				}
			}

			int android_runtime_jnienv_class_token = -1;
			int jnienv_initialize_method_token = -1;
			int jnienv_registerjninatives_method_token = -1;
			foreach (var assembly in ResolvedAssemblies) {
				updateNameWidth (assembly);
				updateAssemblyCount (assembly);

				if (android_runtime_jnienv_class_token != -1) {
					continue;
				}

				if (!assembly.ItemSpec.EndsWith ("Mono.Android.dll", StringComparison.OrdinalIgnoreCase)) {
					continue;
				}

				GetRequiredTokens (assembly.ItemSpec, out android_runtime_jnienv_class_token, out jnienv_initialize_method_token, out jnienv_registerjninatives_method_token);
			}

			if (!UseAssemblyStore) {
				int abiNameLength = 0;
				foreach (string abi in SupportedAbis) {
					if (abi.Length <= abiNameLength) {
						continue;
					}
					abiNameLength = abi.Length;
				}
				assemblyNameWidth += abiNameLength + 2; // room for '/' and the terminating NUL
			}

			MonoComponent monoComponents = MonoComponent.None;
			if (MonoComponents != null && MonoComponents.Length > 0) {
				foreach (ITaskItem item in MonoComponents) {
					if (String.Compare ("diagnostics_tracing", item.ItemSpec, StringComparison.OrdinalIgnoreCase) == 0) {
						monoComponents |= MonoComponent.Tracing;
					} else if (String.Compare ("hot_reload", item.ItemSpec, StringComparison.OrdinalIgnoreCase) == 0) {
						monoComponents |= MonoComponent.HotReload;
					} else if (String.Compare ("debugger", item.ItemSpec, StringComparison.OrdinalIgnoreCase) == 0) {
						monoComponents |= MonoComponent.Debugger;
					}
				}
			}

			var uniqueNativeLibraries = new List<ITaskItem> ();
			var seenNativeLibraryNames = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
			if (NativeLibraries != null) {
				foreach (ITaskItem item in NativeLibraries) {
					// We don't care about different ABIs here, just the file name
					string name = Path.GetFileName (item.ItemSpec);
					if (seenNativeLibraryNames.Contains (name)) {
						continue;
					}

					seenNativeLibraryNames.Add (name);
					uniqueNativeLibraries.Add (item);
				}
			}

			Dictionary<AndroidTargetArch, NativeCodeGenState>? nativeCodeGenStates = null;
			if (enableMarshalMethods) {
				nativeCodeGenStates = BuildEngine4.GetRegisteredTaskObjectAssemblyLocal<Dictionary<AndroidTargetArch, NativeCodeGenState>> (
					ProjectSpecificTaskObjectKey (GenerateJavaStubs.NativeCodeGenStateRegisterTaskKey),
					RegisteredTaskObjectLifetime.Build
				);
			}

			bool haveRuntimeConfigBlob = !String.IsNullOrEmpty (RuntimeConfigBinFilePath) && File.Exists (RuntimeConfigBinFilePath);
			var jniRemappingNativeCodeInfo = BuildEngine4.GetRegisteredTaskObjectAssemblyLocal<GenerateJniRemappingNativeCode.JniRemappingNativeCodeInfo> (ProjectSpecificTaskObjectKey (GenerateJniRemappingNativeCode.JniRemappingNativeCodeInfoKey), RegisteredTaskObjectLifetime.Build);
			var appConfigAsmGen = new ApplicationConfigNativeAssemblyGenerator (environmentVariables, systemProperties, Log) {
				UsesMonoAOT = usesMonoAOT,
				UsesMonoLLVM = EnableLLVM,
				UsesAssemblyPreload = environmentParser.UsesAssemblyPreload,
				MonoAOTMode = aotMode.ToString ().ToLowerInvariant (),
				AotEnableLazyLoad = AndroidAotEnableLazyLoad,
				AndroidPackageName = AndroidPackageName,
				BrokenExceptionTransitions = environmentParser.BrokenExceptionTransitions,
				PackageNamingPolicy = pnp,
				BoundExceptionType = boundExceptionType,
				InstantRunEnabled = InstantRunEnabled,
				JniAddNativeMethodRegistrationAttributePresent = NativeCodeGenState.Template != null ? NativeCodeGenState.Template.JniAddNativeMethodRegistrationAttributePresent : false,
				HaveRuntimeConfigBlob = haveRuntimeConfigBlob,
				NumberOfAssembliesInApk = assemblyCount,
				BundledAssemblyNameWidth = assemblyNameWidth,
				MonoComponents = (MonoComponent)monoComponents,
				NativeLibraries = uniqueNativeLibraries,
				HaveAssemblyStore = UseAssemblyStore,
				AndroidRuntimeJNIEnvToken = android_runtime_jnienv_class_token,
				JNIEnvInitializeToken = jnienv_initialize_method_token,
				JNIEnvRegisterJniNativesToken = jnienv_registerjninatives_method_token,
				JniRemappingReplacementTypeCount = jniRemappingNativeCodeInfo == null ? 0 : jniRemappingNativeCodeInfo.ReplacementTypeCount,
				JniRemappingReplacementMethodIndexEntryCount = jniRemappingNativeCodeInfo == null ? 0 : jniRemappingNativeCodeInfo.ReplacementMethodIndexEntryCount,
				MarshalMethodsEnabled = EnableMarshalMethods,
				IgnoreSplitConfigs = ShouldIgnoreSplitConfigs (),
			};
			LLVMIR.LlvmIrModule appConfigModule = appConfigAsmGen.Construct ();

			foreach (string abi in SupportedAbis) {
				string targetAbi = abi.ToLowerInvariant ();
				string environmentBaseAsmFilePath = Path.Combine (EnvironmentOutputDirectory, $"environment.{targetAbi}");
				string marshalMethodsBaseAsmFilePath = Path.Combine (EnvironmentOutputDirectory, $"marshal_methods.{targetAbi}");
				string environmentLlFilePath  = $"{environmentBaseAsmFilePath}.ll";
				string marshalMethodsLlFilePath = $"{marshalMethodsBaseAsmFilePath}.ll";
				AndroidTargetArch targetArch = GetAndroidTargetArchForAbi (abi);

				using var appConfigWriter = MemoryStreamPool.Shared.CreateStreamWriter ();
				try {
					appConfigAsmGen.Generate (appConfigModule, targetArch, appConfigWriter, environmentLlFilePath);
				} catch {
					throw;
				} finally {
					appConfigWriter.Flush ();
					Files.CopyIfStreamChanged (appConfigWriter.BaseStream, environmentLlFilePath);
				}

				MarshalMethodsNativeAssemblyGenerator marshalMethodsAsmGen;
				if (enableMarshalMethods) {
					marshalMethodsAsmGen = new MarshalMethodsNativeAssemblyGenerator (
						Log,
						assemblyCount,
						uniqueAssemblyNames,
						EnsureCodeGenState (targetArch)
					);
				} else {
					marshalMethodsAsmGen = new MarshalMethodsNativeAssemblyGenerator (
						Log,
						targetArch,
						assemblyCount,
						uniqueAssemblyNames
					);
				}

				LLVMIR.LlvmIrModule marshalMethodsModule = marshalMethodsAsmGen.Construct ();
				using var marshalMethodsWriter = MemoryStreamPool.Shared.CreateStreamWriter ();
				try {
					marshalMethodsAsmGen.Generate (marshalMethodsModule, targetArch, marshalMethodsWriter, marshalMethodsLlFilePath);
				} catch {
					throw;
				} finally {
					marshalMethodsWriter.Flush ();
					Files.CopyIfStreamChanged (marshalMethodsWriter.BaseStream, marshalMethodsLlFilePath);
				}
			}

			NativeCodeGenState EnsureCodeGenState (AndroidTargetArch targetArch)
			{
				if (nativeCodeGenStates == null || !nativeCodeGenStates.TryGetValue (targetArch, out NativeCodeGenState? state)) {
					throw new InvalidOperationException ($"Internal error: missing native code generation state for architecture '{targetArch}'");
				}

				return state;
			}

			void AddEnvironmentVariable (string name, string value)
			{
				if (Char.IsUpper(name [0]) || !Char.IsLetter(name [0]))
					environmentVariables [ValidAssemblerString (name)] = ValidAssemblerString (value);
				else
					systemProperties [ValidAssemblerString (name)] = ValidAssemblerString (value);
			}

			void AddEnvironmentVariableLine (string l)
			{
				string line = l?.Trim ();
				if (String.IsNullOrEmpty (line) || line [0] == '#')
					return;

				string[] nv = line.Split (new char[]{'='}, 2);
				AddEnvironmentVariable (nv[0].Trim (), nv.Length < 2 ? String.Empty : nv[1].Trim ());
			}

			string ValidAssemblerString (string s)
			{
				return s.Replace ("\"", "\\\"");
			}
		}

		bool ShouldIgnoreSplitConfigs ()
		{
			if (String.IsNullOrEmpty (CustomBundleConfigFile)) {
				return false;
			}

			return BundleConfigSplitConfigsChecker.ShouldIgnoreSplitConfigs (Log, CustomBundleConfigFile);
		}

		void GetRequiredTokens (string assemblyFilePath, out int android_runtime_jnienv_class_token, out int jnienv_initialize_method_token, out int jnienv_registerjninatives_method_token)
		{
			using (var pe = new PEReader (File.OpenRead (assemblyFilePath))) {
				GetRequiredTokens (pe.GetMetadataReader (), out android_runtime_jnienv_class_token, out jnienv_initialize_method_token, out jnienv_registerjninatives_method_token);
			}

			if (android_runtime_jnienv_class_token == -1 || jnienv_initialize_method_token == -1 || jnienv_registerjninatives_method_token == -1) {
				throw new InvalidOperationException ($"Unable to find the required Android.Runtime.JNIEnvInit method tokens for {assemblyFilePath}");
			}
		}

		void GetRequiredTokens (MetadataReader reader, out int android_runtime_jnienv_class_token, out int jnienv_initialize_method_token, out int jnienv_registerjninatives_method_token)
		{
			android_runtime_jnienv_class_token = -1;
			jnienv_initialize_method_token = -1;
			jnienv_registerjninatives_method_token = -1;

			TypeDefinition? typeDefinition = null;

			foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions) {
				TypeDefinition td = reader.GetTypeDefinition (typeHandle);
				if (!TypeMatches (td)) {
					continue;
				}

				typeDefinition = td;
				android_runtime_jnienv_class_token = MetadataTokens.GetToken (reader, typeHandle);
				break;
			}

			if (typeDefinition == null) {
				return;
			}

			foreach (MethodDefinitionHandle methodHandle in typeDefinition.Value.GetMethods ()) {
				MethodDefinition md = reader.GetMethodDefinition (methodHandle);
				string name = reader.GetString (md.Name);

				if (jnienv_initialize_method_token == -1 && String.Compare (name, "Initialize", StringComparison.Ordinal) == 0) {
					jnienv_initialize_method_token = MetadataTokens.GetToken (reader, methodHandle);
				} else if (jnienv_registerjninatives_method_token == -1 && String.Compare (name, "RegisterJniNatives", StringComparison.Ordinal) == 0) {
					jnienv_registerjninatives_method_token = MetadataTokens.GetToken (reader, methodHandle);
				}

				if (jnienv_initialize_method_token != -1 && jnienv_registerjninatives_method_token != -1) {
					break;
				}
			}


			bool TypeMatches (TypeDefinition td)
			{
				string ns = reader.GetString (td.Namespace);
				if (String.Compare (ns, "Android.Runtime", StringComparison.Ordinal) != 0) {
					return false;
				}

				string name = reader.GetString (td.Name);
				if (String.Compare (name, "JNIEnvInit", StringComparison.Ordinal) != 0) {
					return false;
				}

				return true;
			}
		}
	}
}
