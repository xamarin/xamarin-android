using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Xamarin.Android.Prepare
{
	partial class Step_GenerateFiles : Step
	{
		bool atBuildStart;
		bool onlyRequired;

		IEnumerable<GitSubmoduleInfo>?  gitSubmodules;
		string?                         xaCommit;


		public Step_GenerateFiles (bool atBuildStart, bool onlyRequired = false)
			: base ("Generating files required by the build")
		{
			this.atBuildStart = atBuildStart;
			this.onlyRequired = onlyRequired;
		}

		protected override async Task<bool> Execute (Context context)
		{
			var git                 = new GitRunner (context);
			xaCommit                = git.GetTopCommitHash (workingDirectory: BuildPaths.XamarinAndroidSourceRoot, shortHash: false);
			var gitSubmoduleInfo    = await git.ConfigList (new[]{"--blob", "HEAD:.gitmodules"});
			var gitSubmoduleStatus  = await git.SubmoduleStatus ();
			gitSubmodules           = GitSubmoduleInfo.GetGitSubmodules (gitSubmoduleInfo, gitSubmoduleStatus);

			List<GeneratedFile>? filesToGenerate = GetFilesToGenerate (context);
			if (filesToGenerate != null && filesToGenerate.Count > 0) {
				foreach (GeneratedFile gf in filesToGenerate) {
					if (gf == null)
						continue;

					Log.Status ("Generating ");
					Log.Status (Utilities.GetRelativePath (BuildPaths.XamarinAndroidSourceRoot, gf.OutputPath), ConsoleColor.White);
					if (!String.IsNullOrEmpty (gf.InputPath))
						Log.StatusLine ($" {context.Characters.LeftArrow} ", Utilities.GetRelativePath (BuildPaths.XamarinAndroidSourceRoot, gf.InputPath), leadColor: ConsoleColor.Cyan, tailColor: ConsoleColor.White);
					else
						Log.StatusLine ();

					gf.Generate (context);
				}
			}

			return true;
		}

		List<GeneratedFile>? GetFilesToGenerate (Context context)
		{
			if (atBuildStart) {
				if (onlyRequired) {
					return new List<GeneratedFile> {
						Get_SourceLink_Json (context),
						Get_Configuration_Generated_Props (context),
						Get_Cmake_XA_Build_Configuration (context),
						Get_Cmake_Presets (context),
					};
				} else {
					return new List <GeneratedFile> {
						Get_SourceLink_Json (context),
						Get_Configuration_OperatingSystem_props (context),
						Get_Configuration_Generated_Props (context),
						Get_Cmake_XA_Build_Configuration (context),
						Get_Cmake_Presets (context),
						Get_Ndk_projitems (context),
						Get_XABuildConfig_cs (context),
						Get_Omnisharp_Json (context),
					};
				}
			}

			if (onlyRequired)
				return null;

			var steps = new List <GeneratedFile> {
				new GeneratedMonoAndroidProjitemsFile (),
			};

			AddOSSpecificSteps (context, steps);
			AddUnixPostBuildSteps (context, steps);

			return steps;
		}

		partial void AddUnixPostBuildSteps (Context context, List<GeneratedFile> steps);
		partial void AddOSSpecificSteps (Context context, List<GeneratedFile> steps);

		GeneratedFile Get_Cmake_XA_Build_Configuration (Context context)
		{
			const string OutputFileName = "xa_build_configuration.cmake";

			var replacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@NETCORE_APP_RUNTIME_ANDROID_ARM@",    Utilities.EscapePathSeparators (Configurables.Paths.NetcoreAppRuntimeAndroidARM) },
				{ "@NETCORE_APP_RUNTIME_ANDROID_ARM64@",  Utilities.EscapePathSeparators (Configurables.Paths.NetcoreAppRuntimeAndroidARM64) },
				{ "@NETCORE_APP_RUNTIME_ANDROID_X86@",    Utilities.EscapePathSeparators (Configurables.Paths.NetcoreAppRuntimeAndroidX86) },
				{ "@NETCORE_APP_RUNTIME_ANDROID_X86_64@", Utilities.EscapePathSeparators (Configurables.Paths.NetcoreAppRuntimeAndroidX86_64) },
			};

			return new GeneratedPlaceholdersFile (
				replacements,
				Path.Combine (Configurables.Paths.BuildToolsScriptsDir, $"{OutputFileName}.in"),
				Path.Combine (Configurables.Paths.BuildBinDir, OutputFileName)
			);
		}

		GeneratedFile Get_Cmake_Presets (Context context)
		{
			const string OutputFileName = "CMakePresets.json";

			Properties props = context.Properties;
			var replacements = new Dictionary <string, string> (StringComparer.Ordinal) {
				{ "@AndroidNdkDirectory@",       Utilities.EscapePathSeparators (props.GetRequiredValue (KnownProperties.AndroidNdkDirectory)) },
				{ "@NinjaPath@",                 Utilities.EscapePathSeparators (props.GetRequiredValue (KnownProperties.NinjaPath)) },
				{ "@MicrosoftAndroidSdkOutDir@", Utilities.EscapePathSeparators (props.GetRequiredValue (KnownProperties.MicrosoftAndroidSdkOutDir)) },
				{ "@OutputPath@",                Utilities.EscapePathSeparators (Path.Combine (props.GetRequiredValue (KnownProperties.MicrosoftAndroidSdkOutDir), "lib")) },
				{ "@NDK_ARMEABI_V7_API_NET@",    BuildAndroidPlatforms.NdkMinimumAPI.ToString () },
				{ "@NDK_ARM64_V8A_API_NET@",     BuildAndroidPlatforms.NdkMinimumAPI.ToString () },
				{ "@NDK_X86_API_NET@",           BuildAndroidPlatforms.NdkMinimumAPI.ToString () },
				{ "@NDK_X86_64_API_NET@",        BuildAndroidPlatforms.NdkMinimumAPI.ToString () },
				{ "@XA_BUILD_CONFIGURATION@",    context.Configuration },
			};

			return new GeneratedPlaceholdersFile (
				replacements,
				Path.Combine (Configurables.Paths.NativeSourcesDir, $"{OutputFileName}.in"),
				Path.Combine (Configurables.Paths.NativeSourcesDir, OutputFileName)
			);
		}

		GeneratedFile Get_Configuration_Generated_Props (Context context)
		{
			const string OutputFileName = "Configuration.Generated.props";

			var replacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@XA_PACKAGES_DIR@",                    Configurables.Paths.XAPackagesDir },
			};

			return new GeneratedPlaceholdersFile (
				replacements,
				Path.Combine (Configurables.Paths.BootstrapResourcesDir, $"{OutputFileName}.in"),
				Configurables.Paths.ConfigurationPropsGeneratedPath
			);
		}

		GeneratedFile Get_Configuration_OperatingSystem_props (Context context)
		{
			const string OutputFileName = "Configuration.OperatingSystem.props";

			var replacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@OS_NAME@",              context.OS.Name ?? String.Empty },
				{ "@HOST_OS_FLAVOR@",       context.OS.Flavor ?? String.Empty },
				{ "@OS_RELEASE@",           context.OS.Release ?? String.Empty },
				{ "@HOST_CPUS@",            context.OS.CPUCount.ToString () },
				{ "@ARCHITECTURE_BITS@",    context.OS.Is64Bit ? "64" : "32" },
				{ "@JavaSdkDirectory@",     context.OS.JavaHome },
				{ "@javac@",                context.OS.JavaCPath },
				{ "@java@",                 context.OS.JavaPath },
				{ "@jar@",                  context.OS.JarPath },
				{ "@NDK_LLVM_TAG@",         $"{context.OS.Type.ToLowerInvariant ()}-x86_64" },
			};

			return new GeneratedPlaceholdersFile (
				replacements,
				Path.Combine (Configurables.Paths.BootstrapResourcesDir, $"{OutputFileName}.in"),
				Path.Combine (BuildPaths.XamarinAndroidSourceRoot, OutputFileName)
			);
		}

		GeneratedFile Get_XABuildConfig_cs (Context context)
		{
			const string OutputFileName = "XABuildConfig.cs";

			string xaVersion = context.Properties.GetRequiredValue (KnownProperties.AndroidPackVersion);
			string? xaVersionSuffix = context.Properties.GetRequiredValue (KnownProperties.AndroidPackVersionSuffix);
			if (!String.IsNullOrEmpty(xaVersionSuffix)) {
				xaVersion = $"{xaVersion}-{xaVersionSuffix}";
			}

			var replacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@NDK_REVISION@",              context.BuildInfo.NDKRevision },
				{ "@NDK_RELEASE@",               BuildAndroidPlatforms.AndroidNdkVersion },
				{ "@NDK_VERSION_MAJOR@",         context.BuildInfo.NDKVersionMajor },
				{ "@NDK_VERSION_MINOR@",         context.BuildInfo.NDKVersionMinor },
				{ "@NDK_VERSION_MICRO@",         context.BuildInfo.NDKVersionMicro },
				{ "@NDK_ARMEABI_V7_API@",        BuildAndroidPlatforms.NdkMinimumAPILegacy32.ToString () },
				{ "@NDK_ARM64_V8A_API@",         BuildAndroidPlatforms.NdkMinimumAPI.ToString () },
				{ "@NDK_X86_API@",               BuildAndroidPlatforms.NdkMinimumAPILegacy32.ToString ().ToString () },
				{ "@NDK_X86_64_API@",            BuildAndroidPlatforms.NdkMinimumAPI.ToString ().ToString () },
				{ "@XA_SUPPORTED_ABIS@",         context.Properties.GetRequiredValue (KnownProperties.AndroidSupportedTargetJitAbis).Replace (':', ';') },
				{ "@ANDROID_DEFAULT_MINIMUM_DOTNET_API_LEVEL@", context.Properties.GetRequiredValue (KnownProperties.AndroidMinimumDotNetApiLevel) },
				{ "@ANDROID_DEFAULT_TARGET_DOTNET_API_LEVEL@", context.Properties.GetRequiredValue (KnownProperties.AndroidDefaultTargetDotnetApiLevel) },
				{ "@ANDROID_LATEST_STABLE_API_LEVEL@", context.Properties.GetRequiredValue (KnownProperties.AndroidLatestStableApiLevel) },
				{ "@ANDROID_LATEST_UNSTABLE_API_LEVEL@", context.Properties.GetRequiredValue (KnownProperties.AndroidLatestUnstableApiLevel) },
				{ "@XAMARIN_ANDROID_VERSION@",   xaVersion },
				{ "@XAMARIN_ANDROID_COMMIT_HASH@", context.BuildInfo.XACommitHash },
				{ "@XAMARIN_ANDROID_BRANCH@", context.BuildInfo.XABranch },
			};

			return new GeneratedPlaceholdersFile (
				replacements,
				Path.Combine (Configurables.Paths.BuildToolsScriptsDir, $"{OutputFileName}.in"),
				Path.Combine (Configurables.Paths.BuildBinDir, OutputFileName)
			);
		}

		GeneratedFile Get_Ndk_projitems (Context context)
		{
			const string OutputFileName = "Ndk.projitems";

			var replacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@NDK_RELEASE@",               BuildAndroidPlatforms.AndroidNdkVersion },
				{ "@NDK_ARMEABI_V7_API@",        BuildAndroidPlatforms.NdkMinimumAPILegacy32.ToString () },
				{ "@NDK_ARMEABI_V7_API_NET@",    BuildAndroidPlatforms.NdkMinimumAPI.ToString () },
				{ "@NDK_ARM64_V8A_API@",         BuildAndroidPlatforms.NdkMinimumAPI.ToString ()  },
				{ "@NDK_ARM64_V8A_API_NET@",     BuildAndroidPlatforms.NdkMinimumAPI.ToString () },
				{ "@NDK_X86_API@",               BuildAndroidPlatforms.NdkMinimumAPILegacy32.ToString ()  },
				{ "@NDK_X86_API_NET@",           BuildAndroidPlatforms.NdkMinimumAPI.ToString ()  },
				{ "@NDK_X86_64_API@",            BuildAndroidPlatforms.NdkMinimumAPI.ToString ()  },
				{ "@NDK_X86_64_API_NET@",        BuildAndroidPlatforms.NdkMinimumAPI.ToString ()  },
			};

			return new GeneratedPlaceholdersFile (
				replacements,
				Path.Combine (Configurables.Paths.BuildToolsScriptsDir, $"{OutputFileName}.in"),
				Path.Combine (Configurables.Paths.BuildBinDir, OutputFileName)
			);
		}

		public GeneratedFile Get_Omnisharp_Json (Context context)
		{
			const string OutputFileName = "omnisharp.json";

			var replacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@CONFIGURATION@", context.Configuration },
				{ "@DOTNET_SDK_PATH@", Path.Combine (Configurables.Paths.DotNetPreviewPath, "sdk", context.Properties.GetRequiredValue (KnownProperties.MicrosoftDotnetSdkInternalPackageVersion)) },
			};

			return new GeneratedPlaceholdersFile (
				replacements,
				Path.Combine (Configurables.Paths.BuildToolsScriptsDir, $"{OutputFileName}.in"),
				Path.Combine (BuildPaths.XamarinAndroidSourceRoot, OutputFileName)
			);
		}

		public GeneratedFile Get_SourceLink_Json (Context context)
		{
			if (gitSubmodules == null || xaCommit == null) {
				return new SkipGeneratedFile ();
			}
			return new GeneratedSourceLinkJsonFile (
					gitSubmodules!,
					xaCommit!,
					Path.Combine (Configurables.Paths.BuildBinDir, "SourceLink.json")
			);
		}
	}
}
