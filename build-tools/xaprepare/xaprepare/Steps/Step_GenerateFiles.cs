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
						Get_MonoGitHash_props (context),
						Get_Configuration_Generated_Props (context),
						Get_Cmake_XA_Build_Configuration (context),
						new GeneratedMonodroidCmakeFiles (Configurables.Paths.BuildBinDir),
					};
				} else {
					return new List <GeneratedFile> {
						Get_SourceLink_Json (context),
						Get_Configuration_OperatingSystem_props (context),
						Get_Configuration_Generated_Props (context),
						Get_Cmake_XA_Build_Configuration (context),
						new GeneratedMonodroidCmakeFiles (Configurables.Paths.BuildBinDir),
						Get_Ndk_projitems (context),
						Get_XABuildConfig_cs (context),
						Get_mingw_32_cmake (context),
						Get_mingw_64_cmake (context),
						Get_MonoGitHash_props (context),
						Get_Omnisharp_Json (context),
					};
				}
			}

			if (onlyRequired)
				return null;

			var steps = new List <GeneratedFile> {
				new GeneratedProfileAssembliesProjitemsFile (Configurables.Paths.ProfileAssembliesProjitemsPath),
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
				{ "@HOST_TRIPLE@",          context.OS.Triple ?? String.Empty },
				{ "@HOST_TRIPLE32@",        context.OS.Triple32 ?? String.Empty },
				{ "@HOST_TRIPLE64@",        context.OS.Triple64 ?? String.Empty },
				{ "@HOST_CPUS@",            context.OS.CPUCount.ToString () },
				{ "@ARCHITECTURE_BITS@",    context.OS.Is64Bit ? "64" : "32" },
				{ "@HOST_CC@",              context.OS.CC ?? String.Empty },
				{ "@HOST_CXX@",             context.OS.CXX ?? String.Empty },
				{ "@HOST_CC32@",            context.OS.CC32 ?? String.Empty },
				{ "@HOST_CC64@",            context.OS.CC64 ?? String.Empty },
				{ "@HOST_CXX32@",           context.OS.CXX32 ?? String.Empty },
				{ "@HOST_CXX64@",           context.OS.CXX64 ?? String.Empty },
				{ "@HOST_HOMEBREW_PREFIX@", context.OS.HomebrewPrefix ?? String.Empty },
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

			var replacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@NDK_REVISION@",              context.BuildInfo.NDKRevision },
				{ "@NDK_RELEASE@",               BuildAndroidPlatforms.AndroidNdkVersion },
				{ "@NDK_MINIMUM_API_AVAILABLE@", context.BuildInfo.NDKMinimumApiAvailable },
				{ "@NDK_VERSION_MAJOR@",         context.BuildInfo.NDKVersionMajor },
				{ "@NDK_VERSION_MINOR@",         context.BuildInfo.NDKVersionMinor },
				{ "@NDK_VERSION_MICRO@",         context.BuildInfo.NDKVersionMicro },
				{ "@NDK_ARMEABI_V7_API@",        BuildAndroidPlatforms.NdkMinimumAPI.ToString () },
				{ "@NDK_ARM64_V8A_API@",         BuildAndroidPlatforms.NdkMinimumAPI.ToString () },
				{ "@NDK_X86_API@",               BuildAndroidPlatforms.NdkMinimumAPI.ToString ().ToString () },
				{ "@NDK_X86_64_API@",            BuildAndroidPlatforms.NdkMinimumAPI.ToString ().ToString () },
				{ "@XA_SUPPORTED_ABIS@",         context.Properties.GetRequiredValue (KnownProperties.AndroidSupportedTargetJitAbis).Replace (':', ';') },
				{ "@ANDROID_DEFAULT_TARGET_DOTNET_API_LEVEL@", context.Properties.GetRequiredValue (KnownProperties.AndroidDefaultTargetDotnetApiLevel) },
				{ "@ANDROID_LATEST_STABLE_API_LEVEL@", context.Properties.GetRequiredValue (KnownProperties.AndroidLatestStableApiLevel) },
				{ "@ANDROID_LATEST_UNSTABLE_API_LEVEL@", context.Properties.GetRequiredValue (KnownProperties.AndroidLatestUnstableApiLevel) },
				{ "@XAMARIN_ANDROID_VERSION@",   context.Properties.GetRequiredValue (KnownProperties.ProductVersion) },
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
				{ "@NDK_ARMEABI_V7_API_NET@",    BuildAndroidPlatforms.NdkMinimumAPI.ToString () },
				{ "@NDK_ARM64_V8A_API_NET@",     BuildAndroidPlatforms.NdkMinimumAPI.ToString () },
				{ "@NDK_X86_API_NET@",           BuildAndroidPlatforms.NdkMinimumAPI.ToString ()  },
				{ "@NDK_X86_64_API_NET@",        BuildAndroidPlatforms.NdkMinimumAPI.ToString ()  },
			};

			return new GeneratedPlaceholdersFile (
				replacements,
				Path.Combine (Configurables.Paths.BuildToolsScriptsDir, $"{OutputFileName}.in"),
				Path.Combine (Configurables.Paths.BuildBinDir, OutputFileName)
			);
		}

		GeneratedFile Get_mingw_32_cmake (Context context)
		{
			return Get_mingw_cmake (context, Configurables.Paths.Mingw32CmakeTemplatePath, Configurables.Paths.Mingw32CmakePath);
		}

		GeneratedFile Get_mingw_64_cmake (Context context)
		{
			return Get_mingw_cmake (context, Configurables.Paths.Mingw64CmakeTemplatePath, Configurables.Paths.Mingw64CmakePath);
		}

		GeneratedFile Get_mingw_cmake (Context context, string input, string output)
		{
			var replacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@HOMEBREW_PREFIX@", context.OS.HomebrewPrefix ?? String.Empty },
			};

			return new GeneratedPlaceholdersFile (replacements, Path.Combine (input), Path.Combine (output));
		}

		public GeneratedFile Get_MonoGitHash_props (Context context)
		{
			const string OutputFileName = "MonoGitHash.props";

			var replacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@MONO_COMMIT_HASH@", context.BuildInfo.MonoHash }
			};

			return new GeneratedPlaceholdersFile (
				replacements,
				Path.Combine (Configurables.Paths.BootstrapResourcesDir, $"{OutputFileName}.in"),
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
