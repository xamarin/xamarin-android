using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using NUnit.Framework;
using Xamarin.Android.Tasks;
using Xamarin.Android.Tools;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	class EnvironmentHelper
	{
		public sealed class EnvironmentFile
		{
			public readonly string Path;
			public readonly string ABI;

			public EnvironmentFile (string path, string abi)
			{
				if (String.IsNullOrEmpty (path)) {
					throw new ArgumentException ("must not be null or empty", nameof (path));
				}

				if (String.IsNullOrEmpty (abi)) {
					throw new ArgumentException ("must not be null or empty", nameof (abi));
				}

				Path = path;
				ABI = abi;
			}
		}

		// This must be identical to the like-named structure in src/native/xamarin-app-stub/xamarin-app.hh
		public sealed class ApplicationConfig
		{
			public bool   uses_mono_llvm;
			public bool   uses_mono_aot;
			public bool   aot_lazy_load;
			public bool   uses_assembly_preload;
			public bool   broken_exception_transitions;
			public bool   instant_run_enabled;
			public bool   jni_add_native_method_registration_attribute_present;
			public bool   have_runtime_config_blob;
			public bool   have_assemblies_blob;
			public bool   marshal_methods_enabled;
			public bool   ignore_split_configs;
			public byte   bound_stream_io_exception_type;
			public uint   package_naming_policy;
			public uint   environment_variable_count;
			public uint   system_property_count;
			public uint   number_of_assemblies_in_apk;
			public uint   bundled_assembly_name_width;
			public uint   number_of_assembly_store_files;
			public uint   number_of_dso_cache_entries;
			public uint   android_runtime_jnienv_class_token;
			public uint   jnienv_initialize_method_token;
			public uint   jnienv_registerjninatives_method_token;
			public uint   jni_remapping_replacement_type_count;
			public uint   jni_remapping_replacement_method_index_entry_count;
			public uint   mono_components_mask;
			public string android_package_name = String.Empty;
		}

		const uint ApplicationConfigFieldCount = 26;

		const string ApplicationConfigSymbolName = "application_config";
		const string AppEnvironmentVariablesSymbolName = "app_environment_variables";

		static readonly object ndkInitLock = new object ();
		static readonly char[] readElfFieldSeparator = new [] { ' ', '\t' };
		static readonly Regex assemblerLabelRegex = new Regex ("^[_.a-zA-Z0-9]+:", RegexOptions.Compiled);

		static readonly HashSet <string> expectedPointerTypes = new HashSet <string> (StringComparer.Ordinal) {
			".long",
			".quad",
			".xword",
		};

		static readonly HashSet <string> expectedUInt32Types = new HashSet <string> (StringComparer.Ordinal) {
			".word",
			".long",
		};

		static readonly string[] requiredSharedLibrarySymbols = {
			AppEnvironmentVariablesSymbolName,
			"app_system_properties",
			ApplicationConfigSymbolName,
			"map_modules",
			"map_module_count",
			"java_type_count",
			"map_java_hashes",
			"map_java",
			"mono_aot_mode_name",
		};

		static readonly string executableExtension;

		static EnvironmentHelper ()
		{
			if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
				executableExtension = ".exe";
			} else {
				executableExtension = String.Empty;
			}
		}

		static string GenerateNativeAssemblyFromLLVM (string llvmIrFilePath)
		{
			Assert.AreEqual (".ll", Path.GetExtension (llvmIrFilePath), $"Invalid file extension for '{llvmIrFilePath}'");

			// Should match the arguments passed to llc by the CompileNativeAssembly task (except for the `--filetype` argument)
			const string assemblerOptions =
				"-O2 " +
				"--debugger-tune=lldb " + // NDK uses lldb now
				"--debugify-level=location+variables " +
				"--fatal-warnings " +
				"--filetype=asm " +
				"--relocation-model=pic";

			string llc = Path.Combine (TestEnvironment.OSBinDirectory, "binutils", "bin", $"llc{executableExtension}");
			string outputFilePath = Path.ChangeExtension (llvmIrFilePath, ".s");
			RunCommand (llc, $"{assemblerOptions} -o \"{outputFilePath}\" \"{llvmIrFilePath}\"");

			Assert.IsTrue (File.Exists (outputFilePath), $"Generated native assembler file '{outputFilePath}' does not exist");

			return outputFilePath;
		}

		static NativeAssemblyParser CreateAssemblyParser (EnvironmentFile envFile)
		{
			string assemblerFilePath = GenerateNativeAssemblyFromLLVM (envFile.Path);
			return new NativeAssemblyParser (assemblerFilePath, envFile.ABI);
		}

		static NativeAssemblyParser.AssemblerSymbol GetRequiredSymbol (string symbolName, EnvironmentFile envFile, NativeAssemblyParser parser)
		{
			if (!parser.Symbols.TryGetValue (symbolName, out NativeAssemblyParser.AssemblerSymbol symbol)) {
				Assert.Fail ($"Required symbol '{symbolName}' cannot be found in the '{envFile.Path}' file.");
			}

			Assert.IsNotNull (symbol, $"Assembler symbol '{symbolName}' must not be null in environment file '{envFile.Path}'");
			Assert.IsTrue (symbol.Contents.Count > 0, $"Assembler symbol '{symbolName}' must have at least one line of contents in environment file '{envFile.Path}'");

			return symbol;
		}

		static string GetStringContents (NativeAssemblyParser.AssemblerSymbol symbol, EnvironmentFile envFile, NativeAssemblyParser parser, int minimumLength = 1)
		{
			string[] field = GetField (envFile.Path, parser.SourceFilePath, symbol.Contents[0].Contents, symbol.Contents[0].LineNumber);

			Assert.IsTrue (field.Length >= 2, $"First line of symbol '{symbol.Name}' must have at least two fields in environment file '{parser.SourceFilePath}'. File generated from '{envFile.Path}'");
			Assert.IsTrue (String.Compare (".asciz", field[0], StringComparison.Ordinal) == 0, $"First line of symbol '{symbol.Name}' must begin with the '.asciz' directive in environment file '{parser.SourceFilePath}'. Instead it was '{field[0]}'. File generated from '{envFile.Path}'");

			// the extra 2 characters account for the enclosing '"'
			Assert.IsTrue (field[1].Length > 2 + minimumLength, $"Symbol '{symbol.Name}' must not be a string at least {minimumLength} characters long in environment file '{parser.SourceFilePath}'. File generated from '{envFile.Path}'");

			string s = field[1].Trim ();
			Assert.IsTrue (s[0] == '"' && s[s.Length - 1] == '"', $"Symbol '{symbol.Name}' value must be enclosed in double quotes in environment file '{parser.SourceFilePath}'. File generated from '{envFile.Path}'");

			return field[1].Trim ('"');
		}

		// Reads all the environment files, makes sure they all have identical contents in the
		// `application_config` structure and returns the config if the condition is true
		public static ApplicationConfig ReadApplicationConfig (List<EnvironmentFile> envFilePaths)
		{
			if (envFilePaths.Count == 0)
				return null;

			ApplicationConfig app_config = ReadApplicationConfig (envFilePaths [0]);

			for (int i = 1; i < envFilePaths.Count; i++) {
				AssertApplicationConfigIsIdentical (app_config, envFilePaths [0].Path, ReadApplicationConfig (envFilePaths[i]), envFilePaths[i].Path);
			}

			return app_config;
		}

		static ApplicationConfig ReadApplicationConfig (EnvironmentFile envFile)
		{
			NativeAssemblyParser parser = CreateAssemblyParser (envFile);

			if (!parser.Symbols.TryGetValue (ApplicationConfigSymbolName, out NativeAssemblyParser.AssemblerSymbol appConfigSymbol)) {
				Assert.Fail ($"Symbol '{ApplicationConfigSymbolName}' not found in LLVM IR file '{envFile.Path}'");
			}

			Assert.IsTrue (appConfigSymbol.Size != 0, $"{ApplicationConfigSymbolName} size as specified in the '.size' directive must not be 0");

			var pointers = new List <string> ();
			var ret = new ApplicationConfig ();
			uint fieldCount = 0;
			string[] field;

			foreach (NativeAssemblyParser.AssemblerSymbolItem item in appConfigSymbol.Contents) {
				field = GetField (envFile.Path, parser.SourceFilePath, item.Contents, item.LineNumber);

				if (String.Compare (".zero", field[0], StringComparison.Ordinal) == 0) {
					continue; // padding, we can safely ignore it
				}

				switch (fieldCount) {
					case 0: // uses_mono_llvm: bool / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.uses_mono_llvm = ConvertFieldToBool ("uses_mono_llvm", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 1: // uses_mono_aot: bool / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.uses_mono_aot = ConvertFieldToBool ("uses_mono_aot", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 2:
						// aot_lazy_load: bool / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.uses_mono_aot = ConvertFieldToBool ("aot_lazy_load", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 3: // uses_assembly_preload: bool / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.uses_assembly_preload = ConvertFieldToBool ("uses_assembly_preload", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 4: // broken_exception_transitions: bool / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.broken_exception_transitions = ConvertFieldToBool ("broken_exception_transitions", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 5: // instant_run_enabled: bool / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.instant_run_enabled = ConvertFieldToBool ("instant_run_enabled", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 6: // jni_add_native_method_registration_attribute_present: bool / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.jni_add_native_method_registration_attribute_present = ConvertFieldToBool ("jni_add_native_method_registration_attribute_present", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 7: // have_runtime_config_blob: bool / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.have_runtime_config_blob = ConvertFieldToBool ("have_runtime_config_blob", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 8: // have_assemblies_blob: bool / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.have_assemblies_blob = ConvertFieldToBool ("have_assemblies_blob", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 9: // marshal_methods_enabled: bool / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.marshal_methods_enabled = ConvertFieldToBool ("marshal_methods_enabled", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 10: // ignore_split_configs: bool / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.ignore_split_configs = ConvertFieldToBool ("ignore_split_configs", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 11: // bound_stream_io_exception_type: byte / .byte
						AssertFieldType (envFile.Path, parser.SourceFilePath, ".byte", field [0], item.LineNumber);
						ret.bound_stream_io_exception_type = ConvertFieldToByte ("bound_stream_io_exception_type", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 12: // package_naming_policy: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.package_naming_policy = ConvertFieldToUInt32 ("package_naming_policy", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 13: // environment_variable_count: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.environment_variable_count = ConvertFieldToUInt32 ("environment_variable_count", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 14: // system_property_count: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.system_property_count = ConvertFieldToUInt32 ("system_property_count", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 15: // number_of_assemblies_in_apk: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.number_of_assemblies_in_apk = ConvertFieldToUInt32 ("number_of_assemblies_in_apk", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 16: // bundled_assembly_name_width: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.bundled_assembly_name_width = ConvertFieldToUInt32 ("bundled_assembly_name_width", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 17: // number_of_assembly_store_files: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.number_of_assembly_store_files = ConvertFieldToUInt32 ("number_of_assembly_store_files", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 18: // number_of_dso_cache_entries: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.number_of_dso_cache_entries = ConvertFieldToUInt32 ("number_of_dso_cache_entries", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 19: // android_runtime_jnienv_class_token: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.number_of_dso_cache_entries = ConvertFieldToUInt32 ("android_runtime_jnienv_class_token", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 20: // jnienv_initialize_method_token: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.number_of_dso_cache_entries = ConvertFieldToUInt32 ("jnienv_initialize_method_token", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 21: // jnienv_registerjninatives_method_token: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.number_of_dso_cache_entries = ConvertFieldToUInt32 ("jnienv_registerjninatives_method_token", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 22: // jni_remapping_replacement_type_count: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.jni_remapping_replacement_type_count = ConvertFieldToUInt32 ("jni_remapping_replacement_type_count", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 23: // jni_remapping_replacement_method_index_entry_count: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.jni_remapping_replacement_method_index_entry_count = ConvertFieldToUInt32 ("jni_remapping_replacement_method_index_entry_count", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 24: // mono_components_mask: uint32_t / .word | .long
						Assert.IsTrue (expectedUInt32Types.Contains (field [0]), $"Unexpected uint32_t field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						ret.mono_components_mask = ConvertFieldToUInt32 ("mono_components_mask", envFile.Path, parser.SourceFilePath, item.LineNumber, field [1]);
						break;

					case 25: // android_package_name: string / [pointer type]
						Assert.IsTrue (expectedPointerTypes.Contains (field [0]), $"Unexpected pointer field type in '{envFile.Path}:{item.LineNumber}': {field [0]}");
						pointers.Add (field [1].Trim ());
						break;
				}
				fieldCount++;
			}

			Assert.AreEqual (1, pointers.Count, $"Invalid number of string pointers in 'application_config' structure in environment file '{envFile.Path}'");

			NativeAssemblyParser.AssemblerSymbol androidPackageNameSymbol = GetRequiredSymbol (pointers[0], envFile, parser);
			ret.android_package_name = GetStringContents (androidPackageNameSymbol, envFile, parser);

			Assert.AreEqual (ApplicationConfigFieldCount, fieldCount, $"Invalid 'application_config' field count in environment file '{envFile.Path}'");
			Assert.IsFalse (String.IsNullOrEmpty (ret.android_package_name), $"Package name field in 'application_config' in environment file '{envFile.Path}' must not be null or empty");

			return ret;
		}

		// Reads all the environment files, makes sure they contain the same environment variables (both count
		// and contents) and then returns a dictionary filled with the variables.
		public static Dictionary<string, string> ReadEnvironmentVariables (List<EnvironmentFile> envFilePaths)
		{
			if (envFilePaths.Count == 0)
				return null;

			Dictionary<string, string> envvars = ReadEnvironmentVariables (envFilePaths [0]);
			if (envFilePaths.Count == 1)
				return envvars;

			for (int i = 1; i < envFilePaths.Count; i++) {
				AssertDictionariesAreEqual (envvars, envFilePaths [0].Path, ReadEnvironmentVariables (envFilePaths[i]), envFilePaths[i].Path);
			}

			return envvars;
		}

		static Dictionary<string, string> ReadEnvironmentVariables (EnvironmentFile envFile)
		{
			NativeAssemblyParser parser = CreateAssemblyParser (envFile);

			if (!parser.Symbols.TryGetValue (AppEnvironmentVariablesSymbolName, out NativeAssemblyParser.AssemblerSymbol appEnvvarsSymbol)) {
				Assert.Fail ($"Symbol '{AppEnvironmentVariablesSymbolName}' not found in LLVM IR file '{envFile.Path}'");
			}

			Assert.IsTrue (appEnvvarsSymbol.Size != 0, $"{AppEnvironmentVariablesSymbolName} size as specified in the '.size' directive must not be 0");

			string[] field;
			var pointers = new List <string> ();
			foreach (NativeAssemblyParser.AssemblerSymbolItem item in appEnvvarsSymbol.Contents) {
				field = GetField (envFile.Path, parser.SourceFilePath, item.Contents, item.LineNumber);

				Assert.IsTrue (expectedPointerTypes.Contains (field [0]), $"Unexpected pointer field type in '{parser.SourceFilePath}:{item.LineNumber}': {field [0]}. File generated from '{envFile.Path}'");
				pointers.Add (field [1].Trim ());
			}

			var ret = new Dictionary <string, string> (StringComparer.Ordinal);
			if (pointers.Count == 0)
				return ret;

			Assert.IsTrue (pointers.Count % 2 == 0, "Environment variable array must have an even number of elements");
			for (int i = 0; i < pointers.Count; i += 2) {
				NativeAssemblyParser.AssemblerSymbol symbol = GetRequiredSymbol (pointers[i], envFile, parser);
				string name = GetStringContents (symbol, envFile, parser);

				symbol = GetRequiredSymbol (pointers[i + 1], envFile, parser);
				string value = GetStringContents (symbol, envFile, parser, minimumLength: 0);

				ret [name] = value;
			}

			return ret;
		}

		static string[] GetField (string llvmAssemblerFile, string nativeAssemblerFile, string line, ulong lineNumber)
		{
			string[] ret = line?.Trim ()?.Split ('\t');
			Assert.IsTrue (ret != null && ret.Length >= 2, $"Invalid assembler field format in file '{nativeAssemblerFile}:{lineNumber}': '{line}'. File generated from '{llvmAssemblerFile}'");

			return ret;
		}

		static void AssertFieldType (string llvmAssemblerFile, string nativeAssemblerFile, string expectedType, string value, ulong lineNumber)
		{
			Assert.AreEqual (expectedType, value, $"Expected the '{expectedType}' field type in file '{nativeAssemblerFile}:{lineNumber}': {value}. File generated from '{llvmAssemblerFile}'");
		}

		static void AssertDictionariesAreEqual (Dictionary <string, string> d1, string d1FileName, Dictionary <string, string> d2, string d2FileName)
		{
			Assert.AreEqual (d1.Count, d2.Count, $"File '{d2FileName}' has a different number of environment variables than file '{d2FileName}'");

			foreach (var kvp in d1) {
				string value;

				Assert.IsTrue (d2.TryGetValue (kvp.Key, out value), $"File '{d2FileName}' does not contain environment variable '{kvp.Key}'");
				Assert.AreEqual (kvp.Value, value, $"Value of environnment variable '{kvp.Key}' is different in file '{d2FileName}' than in file '{d1FileName}'");
			}
		}

		static void AssertApplicationConfigIsIdentical (ApplicationConfig firstAppConfig, string firstEnvFile, ApplicationConfig secondAppConfig, string secondEnvFile)
		{
			Assert.AreEqual (firstAppConfig.uses_mono_llvm, secondAppConfig.uses_mono_llvm, $"Field 'uses_mono_llvm' has different value in environment file '{secondEnvFile}' than in environment file '{firstEnvFile}'");
			Assert.AreEqual (firstAppConfig.uses_mono_aot, secondAppConfig.uses_mono_aot, $"Field 'uses_mono_aot' has different value in environment file '{secondEnvFile}' than in environment file '{firstEnvFile}'");
			Assert.AreEqual (firstAppConfig.environment_variable_count, secondAppConfig.environment_variable_count, $"Field 'environment_variable_count' has different value in environment file '{secondEnvFile}' than in environment file '{firstEnvFile}'");
			Assert.AreEqual (firstAppConfig.system_property_count, secondAppConfig.system_property_count, $"Field 'system_property_count' has different value in environment file '{secondEnvFile}' than in environment file '{firstEnvFile}'");
			Assert.AreEqual (firstAppConfig.android_package_name, secondAppConfig.android_package_name, $"Field 'android_package_name' has different value in environment file '{secondEnvFile}' than in environment file '{firstEnvFile}'");
		}

		public static List<EnvironmentFile> GatherEnvironmentFiles (string outputDirectoryRoot, string supportedAbis, bool required)
		{
			var environmentFiles = new List <EnvironmentFile> ();

			foreach (string abi in supportedAbis.Split (';')) {
				string envFilePath = Path.Combine (outputDirectoryRoot, "android", $"environment.{abi}.ll");

				Assert.IsTrue (File.Exists (envFilePath), $"Environment file {envFilePath} does not exist");
				environmentFiles.Add (new EnvironmentFile (envFilePath, abi));
			}

			if (required)
				Assert.AreNotEqual (0, environmentFiles.Count, "No environment files found");

			return environmentFiles;
		}

		public static void AssertValidEnvironmentSharedLibrary (string outputDirectoryRoot, string sdkDirectory, string ndkDirectory, string supportedAbis)
		{
			NdkTools ndk = NdkTools.Create (ndkDirectory);
			MonoAndroidHelper.AndroidSdk = new AndroidSdkInfo ((arg1, arg2) => {}, sdkDirectory, ndkDirectory, AndroidSdkResolver.GetJavaSdkPath ());

			AndroidTargetArch arch;

			foreach (string abi in supportedAbis.Split (';')) {
				switch (abi) {
					case "armeabi-v7a":
						arch = AndroidTargetArch.Arm;
						break;

					case "arm64":
					case "arm64-v8a":
					case "aarch64":
						arch = AndroidTargetArch.Arm64;
						break;

					case "x86":
						arch = AndroidTargetArch.X86;
						break;

					case "x86_64":
						arch = AndroidTargetArch.X86_64;
						break;

					default:
						throw new Exception ("Unsupported Android target architecture ABI: " + abi);
				}

				string envSharedLibrary = Path.Combine (outputDirectoryRoot, "app_shared_libraries", abi, "libxamarin-app.so");
				Assert.IsTrue (File.Exists (envSharedLibrary), $"Application environment SharedLibrary '{envSharedLibrary}' must exist");

				// API level doesn't matter in this case
				var readelf = ndk.GetToolPath ("readelf", arch, 0);
				if (!File.Exists (readelf)) {
					readelf = ndk.GetToolPath ("llvm-readelf", arch, 0);
				}
				AssertSharedLibraryHasRequiredSymbols (envSharedLibrary, readelf);
			}
		}

		static void AssertSharedLibraryHasRequiredSymbols (string dsoPath, string readElfPath)
		{
			(List<string> stdout_lines, List<string> _) = RunCommand (readElfPath, $"--dyn-syms \"{dsoPath}\"");

			var symbols = new HashSet<string> (StringComparer.Ordinal);
			foreach (string line in stdout_lines) {
				string[] fields = line.Split (readElfFieldSeparator, StringSplitOptions.RemoveEmptyEntries);
				if (fields.Length < 8 || !fields [0].EndsWith (":", StringComparison.Ordinal))
					continue;
				string symbolName = fields [7].Trim ();
				if (String.IsNullOrEmpty (symbolName))
					continue;

				symbols.Add (symbolName);
			}

			foreach (string symbol in requiredSharedLibrarySymbols) {
				Assert.IsTrue (symbols.Contains (symbol), $"Symbol '{symbol}' is missing from '{dsoPath}'");
			}
		}

		static (List<string> stdout, List<string> stderr) RunCommand (string executablePath, string arguments = null)
		{
			var psi = new ProcessStartInfo {
				FileName = executablePath,
				Arguments = arguments,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
			};

			psi.StandardOutputEncoding = Encoding.UTF8;
			psi.StandardErrorEncoding = Encoding.UTF8;

			var stdout_completed = new ManualResetEventSlim (false);
			var stderr_completed = new ManualResetEventSlim (false);
			var stdout_lines = new List <string> ();
			var stderr_lines = new List <string> ();

			using (var process = new Process ()) {
				process.StartInfo = psi;
				process.OutputDataReceived += (s, e) => {
					if (e.Data != null)
						stdout_lines.Add (e.Data);
					else
						stdout_completed.Set ();
				};

				process.ErrorDataReceived += (s, e) => {
					if (e.Data != null)
						stderr_lines.Add (e.Data);
					else
						stderr_completed.Set ();
				};

				process.Start ();
				process.BeginOutputReadLine ();
				process.BeginErrorReadLine ();
				bool exited = process.WaitForExit ((int)TimeSpan.FromSeconds (60).TotalMilliseconds);
				bool stdout_done = stdout_completed.Wait (TimeSpan.FromSeconds (30));
				bool stderr_done = stderr_completed.Wait (TimeSpan.FromSeconds (30));

				if (!exited)
					TestContext.Out.WriteLine ($"{psi.FileName} {psi.Arguments} timed out");
				if (process.ExitCode != 0)
					TestContext.Out.WriteLine ($"{psi.FileName} {psi.Arguments} returned with error code {process.ExitCode}");

				if (!exited || process.ExitCode != 0) {
					DumpLines ("stdout", stdout_lines);
					DumpLines ("stderr", stderr_lines);
					Assert.Fail ($"Command '{psi.FileName} {psi.Arguments}' failed to run or exited with error code");
				}
			}

			return (stdout_lines, stderr_lines);
		}

		static void DumpLines (string streamName, List <string> lines)
		{
			if (lines == null || lines.Count == 0)
				return;

			TestContext.Out.WriteLine ($"{streamName}:");
			foreach (string line in lines) {
				TestContext.Out.WriteLine (line);
			}
		}

		static bool ConvertFieldToBool (string fieldName, string llvmAssemblerEnvFile, string nativeAssemblerEnvFile, ulong fileLine, string value)
		{
			// Allow both decimal and hexadecimal values
			Assert.IsTrue (value.Length > 0 && value.Length <= 3, $"Field '{fieldName}' in {nativeAssemblerEnvFile}:{fileLine} is not a valid boolean value (length not between 1 and 3). File generated from '{llvmAssemblerEnvFile}'");

			uint fv;
			Assert.IsTrue (TryParseInteger (value, out fv), $"Field '{fieldName}' in {nativeAssemblerEnvFile}:{fileLine} is not a valid boolean value (not a valid integer). File generated from '{llvmAssemblerEnvFile}'");
			Assert.IsTrue (fv == 0 || fv == 1, $"Field '{fieldName}' in {nativeAssemblerEnvFile}:{fileLine} is not a valid boolean value (not a valid boolean value 0 or 1). File generated from '{llvmAssemblerEnvFile}'");

			return fv == 1;
		}

		static uint ConvertFieldToUInt32 (string fieldName, string llvmAssemblerEnvFile, string nativeAssemblerEnvFile, ulong fileLine, string value)
		{
			Assert.IsTrue (value.Length > 0, $"Field '{fieldName}' in {nativeAssemblerEnvFile}:{fileLine} is not a valid uint32_t value (not long enough). File generated from '{llvmAssemblerEnvFile}'");

			uint fv;
			Assert.IsTrue (TryParseInteger (value, out fv), $"Field '{fieldName}' in {nativeAssemblerEnvFile}:{fileLine} is not a valid uint32_t value (not a valid integer). File generated from '{llvmAssemblerEnvFile}'");

			return fv;
		}

		static byte ConvertFieldToByte (string fieldName, string llvmAssemblerEnvFile, string nativeAssemblerEnvFile, ulong fileLine, string value)
		{
			Assert.IsTrue (value.Length > 0, $"Field '{fieldName}' in {nativeAssemblerEnvFile}:{fileLine} is not a valid uint8_t value (not long enough). File generated from '{llvmAssemblerEnvFile}'");

			byte fv;
			Assert.IsTrue (TryParseInteger (value, out fv), $"Field '{fieldName}' in {nativeAssemblerEnvFile}:{fileLine} is not a valid uint8_t value (not a valid integer). File generated from '{llvmAssemblerEnvFile}'");

			return fv;
		}

		static bool TryParseInteger (string value, out uint fv)
		{
			if (value.StartsWith ("0x", StringComparison.Ordinal)) {
				return UInt32.TryParse (value.Substring (2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out fv);
			}

			return UInt32.TryParse (value, out fv);
		}

		static bool TryParseInteger (string value, out byte fv)
		{
			if (value.StartsWith ("0x", StringComparison.Ordinal)) {
				return Byte.TryParse (value.Substring (2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out fv);
			}

			return Byte.TryParse (value, out fv);
		}
	}
}
