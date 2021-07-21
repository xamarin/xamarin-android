using System;
using System.Collections.Generic;
using System.IO;

namespace Xamarin.Android.Prepare
{
	// This class exists so that nobody has to jump around the source in order to find an URL to modify, a setting to
	// tweak etc. Code that uses those configurables is all over the place, but the source of the data is here and only
	// here it has to be changed if need be. The entries should be something that might have to be changed by somebody
	// not familiar with the entirety of Xamarin.Android or a developer not usually involved in build system
	// maintenance/development.
	//
	// When adding entries here, try to make their names clear and unambiguous. Don't hesitate using comments (doc
	// comments work fine).
	//
	partial class Configurables
	{
		const string BinutilsVersion                = "2.37-XA.1";

		const string MicrosoftOpenJDK11Version      = "11.0.10";
		const string MicrosoftOpenJDK11Release      = "9.1";
		const string MicrosoftOpenJDK11RootDirName  = "jdk-11.0.10+9";
		const string MicrosoftOpenJDK11DirVersion   = MicrosoftOpenJDK11RootDirName + ".1";
		const string MicrosoftOpenJDK11FileVersion  = "11.0.10.9";

		const string AdoptOpenJDKUpdate = "282";
		const string AdoptOpenJDKBuild = "b08";
		const string AdoptOpenJDKRelease = "8.0"; // build_number.0
		static readonly string AdoptOpenJDKUrlVersion = $"8u{AdoptOpenJDKUpdate}{AdoptOpenJDKBuild}";
		static readonly string AdoptOpenJDKTag = $"jdk8u{AdoptOpenJDKUpdate}-{AdoptOpenJDKBuild}";
		static readonly string AdoptOpenJDKVersion = $"1.8.0.{AdoptOpenJDKUpdate}";

		static Context ctx => Context.Instance;

		public static partial class Urls
		{
			// https://github.com/AdoptOpenJDK/openjdk8-binaries/releases/download/jdk8u282-b08/OpenJDK8U-jdk_x64_linux_hotspot_8u282b08.tar.gz
			// https://github.com/AdoptOpenJDK/openjdk8-binaries/releases/download/jdk8u282-b08/OpenJDK8U-jdk_x64_mac_hotspot_8u282b08.tar.gz
			// https://github.com/AdoptOpenJDK/openjdk8-binaries/releases/download/jdk8u282-b08/OpenJDK8U-jdk_x64_windows_hotspot_8u282b08.zip
			public static readonly Uri AdoptOpenJDK8 = new Uri ($"https://github.com/AdoptOpenJDK/openjdk8-binaries/releases/download/{AdoptOpenJDKTag}/OpenJDK8U-jdk_{AdoptOpenJDKOperatingSystem}_hotspot_{AdoptOpenJDKUrlVersion}.{AdoptOpenJDKArchiveExtension}");

			// https://msopenjdk.azureedge.net/jdk11u/jdk-11.0.10+9.1/linux/x64/microsoft-jdk-11.0.10.9-linux-x64.tar.gz
			// https://msopenjdk.azureedge.net/jdk11u/jdk-11.0.10+9.1/macOS/x64/microsoft-jdk-11.0.10.9-macOS-x64.tar.gz
			// https://msopenjdk.azureedge.net/jdk11u/jdk-11.0.10+9.1/windows/x64/microsoft-jdk-11.0.10.9-windows-x64.zip
			public static readonly Uri MicrosoftOpenJDK11 = new Uri ($"https://msopenjdk.azureedge.net/jdk11u/{MicrosoftOpenJDK11DirVersion}/{MicrosoftOpenJDKOperatingSystem}/microsoft-jdk-{MicrosoftOpenJDK11FileVersion}-{MicrosoftOpenJDKOperatingSystem.Replace ('/', '-')}.{MicrosoftOpenJDKFileExtension}");

			/// <summary>
			///   Base URL for all Android SDK and NDK downloads. Used in <see cref="AndroidToolchain"/>
			/// </summary>
			public static readonly Uri AndroidToolchain_AndroidUri = new Uri ("https://dl.google.com/android/repository/");

			public static readonly Uri NugetUri = new Uri ("https://dist.nuget.org/win-x86-commandline/v4.9.4/nuget.exe");

			public static Uri MonoArchive_BaseUri = new Uri ("https://xamjenkinsartifact.azureedge.net/mono-sdks/");

			public static Uri BinutilsArchive = new Uri ($"https://github.com/xamarin/xamarin-android-binutils/releases/download/{BinutilsVersion}/xamarin-android-binutils-{BinutilsVersion}.7z");
		}

		public static partial class Defaults
		{
			public static readonly string BinutilsVersion            = Configurables.BinutilsVersion;
			public static readonly char[] PropertyListSeparator            = new [] { ':' };

			public static readonly string JdkFolder                        = "jdk-11";

			public static readonly Version MicrosoftOpenJDK11Version = new Version (Configurables.MicrosoftOpenJDK11Version);
			public static readonly Version MicrosoftOpenJDK11Release = new Version (Configurables.MicrosoftOpenJDK11Release);
			public static readonly string  MicrosoftOpenJDK11RootDirName   = Configurables.MicrosoftOpenJDK11RootDirName;

			public static readonly Version AdoptOpenJDK8Version     = new Version (Configurables.AdoptOpenJDKVersion);
			public static readonly Version AdoptOpenJDK8Release     = new Version (Configurables.AdoptOpenJDKRelease);
			public static readonly string  AdoptOpenJDK8RootDirName = Configurables.AdoptOpenJDKTag;

			public const string DotNetTestRuntimeVersion                   = "3.1.11";

			// Mono runtimes
			public const string DebugFileExtension                         = ".pdb";
			public const string MonoHostMingwRuntimeNativeLibraryExtension = WindowsDLLSuffix;
			public const string MonoJitRuntimeNativeLibraryExtension       = ".so";
			public const string MonoRuntimeOutputMonoBtlsFilename          = "libmono-btls-shared";
			public const string MonoRuntimeOutputMonoPosixHelperFilename   = "libMonoPosixHelper";
			public const string MonoRuntimeOutputAotProfilerFilename       = "libmono-profiler-aot";
			public const string MonoRuntimeOutputFileName                  = "libmonosgen-2.0";
			public const string MonoRuntimeOutputProfilerFilename          = "libmono-profiler-log";

			public const string WindowsExecutableSuffix                    = ".exe";
			public const string WindowsDLLSuffix                           = ".dll";
			public const string DebugBinaryInfix                           = ".d";

			public const bool UseEmoji                                     = true;
			public const bool DullMode                                     = false;

			public static string MonoSdksConfiguration                     => Context.Instance.Configuration.ToLowerInvariant ();

			public const string ZipCompressionFormatName = "zip";
			public const string SevenZipCompressionFormatName = "7z";

			public static readonly Dictionary<string, CompressionFormat> CompressionFormats = new Dictionary<string, CompressionFormat> (StringComparer.OrdinalIgnoreCase) {
				{ZipCompressionFormatName,      new CompressionFormat (ZipCompressionFormatName,      "ZIP",  "zip")},
				{SevenZipCompressionFormatName, new CompressionFormat (SevenZipCompressionFormatName, "7Zip", "7z")},
			};

			public static CompressionFormat DefaultCompressionFormat => CompressionFormats [SevenZipCompressionFormatName];

			/// <summary>
			///   Set of .external "submodules" to check out when the <see
			///   cref="KnownConditions.IncludeCommercial" /> condition is set.
			/// </summary>
			public static HashSet<string> CommercialExternalDependencies = new HashSet<string> (StringComparer.OrdinalIgnoreCase) {
				"xamarin/monodroid"
			};

			/// <summary>
			///   Default execution mode. One of:
			///
			///     * CI: continuous integration (a.k.a. bot, a.k.a. dull, a.k.a. sad) mode in which no color, no fancy
			///       progress indicators are used. At the end of the run the application exits.
			///
			///     * Standard: default mode if running with a terminal attached (i.e. when we're not redirected) and
			///       when the console supports colors, cursor movement etc. If no such capabilities are detected this
			///       mode degrades to CI. At the end of the run the application exits.
			///
			///     * Interactive: like Standard but at the end of the run the application does not exit. This mode
			///       presents the user with a full TUI app. Degrades in the same manner as Standard.
			///
			/// </summary>
			public static readonly  ExecutionMode ExecutionMode = ExecutionMode.Standard;

			/// <summary>
			///   Default make/ninja/etc concurrency. If set to <c>0</c> then the actual concurrency level is determined
			///   by looking at the number of CPUs and equals that value + 1.
			/// </summary>
			public const uint MakeConcurrency = 0;

			/// <summary>
			///   Default maximum number of parallel tasks to start, for instance when downloading.
			/// </summary>
			public const int DefaultMaximumParallelTasks = 5;

			/// <summary>
			///   The maximum JDK version we support.
			/// </summary>
			public static readonly Version MaxJDKVersion = new Version (11, 99, 0);

			/// <summary>
			///   Prefix for all the log files created by the bootstrapper.
			/// </summary>
			public const string LogFilePrefix = "prepare";

			/// <summary>
			///   Default logging verbosity for the entire program. <see cref="Prepare.LoggingVerbosity" />
			/// </summary>
			public static readonly LoggingVerbosity LoggingVerbosity = LoggingVerbosity.Normal;

			/// <summary>
			///   Length to truncate the git commit hash to.
			/// </summary>
			public const uint AbbreviatedHashLength = 7;

			/// <summary>
			///   Default hash algorithm to compute file hashes
			/// </summary>
			public const string HashAlgorithm = "SHA1";

			public static readonly Dictionary<string, string> AndroidToolchainPrefixes = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ AbiNames.TargetJit.AndroidArmV7a, "arm-linux-androideabi" },
				{ AbiNames.TargetJit.AndroidArmV8a, "aarch64-linux-android" },
				{ AbiNames.TargetJit.AndroidX86,    "i686-linux-android" },
				{ AbiNames.TargetJit.AndroidX86_64, "x86_64-linux-android" },
			};

			const string CrossArmV7aName = "cross-arm";
			const string CrossArmV8aName = "cross-arm64";
			const string CrossX86Name    = "cross-x86";
			const string CrossX86_64Name = "cross-x86_64";

			public static readonly Dictionary<string, string> CrossRuntimeNames = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ AbiNames.CrossAot.ArmV7a,    CrossArmV7aName },
				{ AbiNames.CrossAot.ArmV8a,    CrossArmV8aName },
				{ AbiNames.CrossAot.X86,       CrossX86Name },
				{ AbiNames.CrossAot.X86_64,    CrossX86_64Name },
				{ AbiNames.CrossAot.WinArmV7a, CrossArmV7aName },
				{ AbiNames.CrossAot.WinArmV8a, CrossArmV8aName },
				{ AbiNames.CrossAot.WinX86,    CrossX86Name },
				{ AbiNames.CrossAot.WinX86_64, CrossX86_64Name },
			};

			const string ArmV7aPrefix = "armv7-linux-android-";
			const string ArmV8aPrefix = "aarch64-v8a-linux-android-";
			const string X86Prefix    = "i686-linux-android-";
			const string X86_64Prefix = "x86_64-linux-android-";

			public static readonly Dictionary<string, string> CrossRuntimeExePrefixes = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ AbiNames.CrossAot.ArmV7a,    ArmV7aPrefix},
				{ AbiNames.CrossAot.ArmV8a,    ArmV8aPrefix },
				{ AbiNames.CrossAot.X86,       X86Prefix },
				{ AbiNames.CrossAot.X86_64,    X86_64Prefix },
				{ AbiNames.CrossAot.WinArmV7a, ArmV7aPrefix },
				{ AbiNames.CrossAot.WinArmV8a, ArmV8aPrefix },
				{ AbiNames.CrossAot.WinX86,    X86Prefix },
				{ AbiNames.CrossAot.WinX86_64, X86_64Prefix },
			};

			/// <summary>
			///   Used in rules.mk generator. Files to include in the XA bundle archives.
			/// </summary>
			public static readonly List <string> BundleZipsInclude = new List <string> {
				"$(ZIP_OUTPUT_BASENAME)/ThirdPartyNotices.txt",
				"$(ZIP_OUTPUT_BASENAME)/bin/Debug",
				"$(ZIP_OUTPUT_BASENAME)/bin/Release",
			};

			/// <summary>
			///   Used in rules.mk generator. Files to exclude from the XA bundle archives. Must be syntactically
			///   correct for GNU Make.
			/// </summary>
			public static readonly List <string> BundleZipsExclude = new List <string> {
				"$(ZIP_OUTPUT_BASENAME)/bin/*/bundle-*.zip"
			};

			/// <summary>
			///   Used in rules.mk generator. Files to include in test results bundle. Must be syntactically
			///   correct for GNU Make.
			/// </summary>
			public static readonly List <string> TestResultsBundleInclude = new List <string> {
				"$(wildcard TestResult-*.xml)",
				"$(wildcard bin/Test$(CONFIGURATION)/compatibility)",
				"$(wildcard bin/Test$(CONFIGURATION)/logcat*)",
				"$(wildcard bin/Test$(CONFIGURATION)/msbuild*.binlog*)",
				"$(wildcard bin/Test$(CONFIGURATION)/temp)",
				"$(wildcard bin/Test$(CONFIGURATION)/EmbeddedDSO)",
				"$(wildcard bin/Test$(CONFIGURATION)/CodeBehind)",
				"$(wildcard bin/Test$(CONFIGURATION)/TestOutput-*.txt)",
				"$(wildcard bin/Test$(CONFIGURATION)/Timing_*)",
				"$(wildcard *.csv)",
			};

			/// <summary>
			///   Used in rules.mk generator. Files to exclude from the test results bundle. Must be syntactically
			///   correct for GNU Make.
			/// </summary>
			public static readonly List <string> TestResultsBundleExclude = new List <string> {
			};

			/// <summary>
			///   Used in rules.mk generator. Files to include in build status bundle archive. Must be syntactically
			///   correct for GNU Make.
			/// </summary>
			public static readonly List <string> BuildStatusBundleInclude = new List <string> {
				"Configuration.OperatingSystem.props",
				"$(wildcard bin/Build$(CONFIGURATION)/msbuild*.binlog)",
				"$(shell find . -name 'config.log')",
				"$(shell find . -name 'config.status')",
				"$(shell find . -name 'config.h')",
				"$(shell find . -name 'CMakeCache.txt')",
				"$(shell find . -name 'config.h')",
				"$(shell find . -name '.ninja_log')",
				"$(shell find . -name 'android-*.config.cache')",
				"bin/Build$(CONFIGURATION)/XABuildConfig.cs",
			};

			/// <summary>
			///   Used in rules.mk generator. Optional files to include in the build status bundle (included only if
			///   they exist). Must be syntactically correct for GNU Make.
			/// </summary>
			public static readonly List <string> BuildStatusBundleIncludeConditional = new List <string> {
				"Configuration.Override.props",
			};

			/// <summary>
			///   Used in rules.mk generator. Files to exclude from the build status bundle. Must be syntactically
			///   correct for GNU Make.
			/// </summary>
			public static readonly List <string> BuildStatusBundleExclude = new List <string> {
			};

			public static readonly List <NDKTool> NDKTools = new List<NDKTool> {
				new NDKTool (name: "as"),
				new NDKTool (name: "ld"),
				new NDKTool (name: "strip"),
			};
		}

		public static partial class Paths
		{
			// Global, compile-time locations
			public static readonly string HomeDir                          = Environment.GetFolderPath (Environment.SpecialFolder.Personal);
			public static readonly string BootstrapResourcesDir            = Path.Combine (BuildPaths.XAPrepareSourceDir, "Resources");
			public static readonly string BuildToolsDir                    = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "build-tools");
			public static readonly string BuildToolsScriptsDir             = Path.Combine (BuildToolsDir, "scripts");
			public static readonly string BinDirRoot                       = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "bin");
			public static readonly string ExternalDir                      = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "external");
			public static readonly string LocalNugetPath                   = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, ".nuget", "NuGet.exe");
			public static readonly string ExternalGitDepsFilePath          = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, ".external");
			public static readonly string ExternalGitDepsDestDir           = ExternalDir;
			public static readonly string ExternalXamarinAndroidToolsSln   = Path.Combine (ExternalDir, "xamarin-android-tools", "Xamarin.Android.Tools.sln");
			public static readonly string MxeSourceDir                     = Path.Combine (ExternalDir, "mxe");
			public static readonly string MonoSDKSRelativeOutputDir        = Path.Combine ("sdks", "out");
			public static readonly string MonoSDKRelativeIncludeSourceDir  = Path.Combine ("include", "mono-2.0", "mono");
			public static readonly string RuntimeInstallRelativeLibDir     = "lib";
			public static readonly string PackageImageDependenciesTemplate = Path.Combine (BuildToolsScriptsDir, "prepare-image-dependencies.sh.in");
			public static readonly string PackageImageDependenciesOutput   = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "prepare-image-dependencies.sh");

			// Dynamic locations used throughout the code
			public static string ExternalJavaInteropDir              => GetCachedPath (ref externalJavaInteropDir, ()              => ctx.Properties.GetRequiredValue (KnownProperties.JavaInteropFullPath));
			public static string MonoSDKSOutputDir                   => GetCachedPath (ref monoSDKsOutputDir, ()                   => Path.Combine (MonoSourceFullPath, MonoSDKSRelativeOutputDir));
			public static string MonoProfileDir                      => GetCachedPath (ref monoProfileDir, ()                      => Path.Combine (MonoSDKSOutputDir, "android-bcl", "monodroid"));
			public static string MonoProfileToolsDir                 => GetCachedPath (ref monoProfileToolsDir, ()                 => Path.Combine (MonoSDKSOutputDir, "android-bcl", "monodroid_tools"));
			public static string MonoSDKIncludeDestinationDir        => GetCachedPath (ref monoSDKSIncludeDestDir, ()              => Path.Combine (OutputIncludeDir, "mono-2.0", "mono"));

			public static string BCLFacadeAssembliesSourceDir        => GetCachedPath (ref bclFacadeAssembliesSourceDir, ()        => Path.Combine (BCLAssembliesSourceDir, "Facades"));
			public static string BCLHostAssembliesSourceDir          => BCLAssembliesSourceDir;
			public static string BCLHostFacadeAssembliesSourceDir    => BCLFacadeAssembliesSourceDir;

			public static string BCLWindowsOutputDir                 => GetCachedPath (ref bclWindowsOutputDir, ()                 => Path.Combine (BuildBinDir, "windows-bcl"));
			public static string BCLWindowsAssembliesSourceDir       => GetCachedPath (ref bclWindowsAssembliesSourceDir, ()       => Path.Combine (BCLWindowsOutputDir, "android-bcl", "monodroid"));
			public static string BCLWindowsFacadeAssembliesSourceDir => GetCachedPath (ref bclWindowsFacadeAssembliesSourceDir, () => Path.Combine (BCLWindowsAssembliesSourceDir, "Facades"));

			public static string BCLAssembliesSourceDir              => MonoProfileDir;

			public static string BCLTestsSourceDir                   => GetCachedPath (ref bclTestsSourceDir, ()                   => Path.Combine (MonoProfileDir, "tests"));

			public static string BCLTestsDestDir                     => GetCachedPath (ref bclTestsDestDir, ()                     => Path.Combine (XAInstallPrefix, "..", "..", "bcl-tests"));
			public static string BCLTestsArchivePath                 => GetCachedPath (ref bclTestsArchivePath, ()                 => Path.Combine (BCLTestsDestDir, BCLTestsArchiveName));

			public static string TestBinDir                          => GetCachedPath (ref testBinDir, ()                          => Path.Combine (Configurables.Paths.BinDirRoot, $"Test{ctx.Configuration}"));
			public static string BinDir                              => GetCachedPath (ref binDir, ()                              => Path.Combine (Configurables.Paths.BinDirRoot, ctx.Configuration));
			public static string NetCoreBinDir                       => GetCachedPath (ref netCoreBinDir, ()                       => Path.Combine (Configurables.Paths.BinDirRoot, $"{ctx.Configuration}-net6.0"));
			public static string BuildBinDir                         => GetCachedPath (ref buildBinDir, ()                         => Path.Combine (Configurables.Paths.BinDirRoot, $"Build{ctx.Configuration}"));
			public static string MingwBinDir                         => GetCachedPath (ref mingwBinDir, ()                         => Path.Combine (ctx.Properties.GetRequiredValue (KnownProperties.AndroidMxeFullPath), "bin"));
			public static string ProfileAssembliesProjitemsPath      => GetCachedPath (ref profileAssembliesProjitemsPath, ()      => Path.Combine (BuildBinDir, "ProfileAssemblies.projitems"));
			public static string ConfigurationPropsGeneratedPath     => GetCachedPath (ref configurationPropsGeneratedPath, ()     => Path.Combine (BuildBinDir, "Configuration.Generated.props"));

			// Mono Runtimes
			public static string MonoAndroidFrameworksSubDir         = Path.Combine ("xbuild-frameworks", "MonoAndroid");
			public static string MonoAndroidFrameworksRootDir        => GetCachedPath (ref monoAndroidFrameworksRootDir, ()        => Path.Combine (XAInstallPrefix, MonoAndroidFrameworksSubDir));
			public static string InstallBCLFrameworkDir              => GetCachedPath (ref installBCLFrameworkDir, ()              => Path.Combine (MonoAndroidFrameworksRootDir, "v1.0"));
			public static string InstallBCLFrameworkFacadesDir       => GetCachedPath (ref installBCLFrameworkFacadesDir, ()       => Path.Combine (InstallBCLFrameworkDir, "Facades"));
			public static string InstallBCLFrameworkRedistListDir    => GetCachedPath (ref installBCLFrameworkRedistListDir, ()    => Path.Combine (InstallBCLFrameworkDir, "RedistList"));

			public static string InstallBCLDesignerDir               => GetCachedPath (ref installBCLDesignerDir, ()               => Path.Combine (XAInstallPrefix, "xbuild", "Xamarin", "Android"));
			public static string InstallHostBCLDir                   => GetCachedPath (ref installHostBCLDir, ()                   => Path.Combine (InstallBCLDesignerDir, ctx.OS.Type, "bcl"));
			public static string InstallHostBCLFacadesDir            => GetCachedPath (ref installHostBCLFacadesDir, ()            => Path.Combine (InstallBCLDesignerDir, ctx.OS.Type, "bcl", "Facades"));
			public static string InstallWindowsBCLDir                => GetCachedPath (ref installWindowsBCLDir, ()                => Path.Combine (InstallBCLDesignerDir, "bcl"));
			public static string InstallWindowsBCLFacadesDir         => GetCachedPath (ref installWindowsBCLFacadesDir, ()         => Path.Combine (InstallBCLDesignerDir, "bcl", "Facades"));

			public static string InstallMSBuildDir                   => GetCachedPath (ref installMSBuildDir, ()                   => Path.Combine (XAInstallPrefix, "xbuild", "Xamarin", "Android"));
			public static string OutputIncludeDir                    => GetCachedPath (ref outputIncludeDir, ()                    => Path.Combine (BinDirRoot, ctx.Configuration, "include"));
			public static string MonoRuntimesEnabledAbisCachePath    => GetCachedPath (ref monoRuntimesEnabledAbisCachePath, ()    => Path.Combine (BuildBinDir, "mono-runtimes-abi.cache"));
			public static string FrameworkListInstallPath            => GetCachedPath (ref frameworkListInstallPath, ()            => Path.Combine (InstallBCLFrameworkRedistListDir, "FrameworkList.xml"));

			// Cmake + MinGW
			public static string Mingw32CmakeTemplatePath            => GetCachedPath (ref mingw32CmakeTemplatePath, ()            => Path.Combine (BuildToolsScriptsDir, "mingw-32.cmake.in"));
			public static string Mingw64CmakeTemplatePath            => GetCachedPath (ref mingw64CmakeTemplatePath, ()            => Path.Combine (BuildToolsScriptsDir, "mingw-64.cmake.in"));
			public static string Mingw32CmakePath                    => GetCachedPath (ref mingw32CmakePath, ()                    => Path.Combine (BuildBinDir, "mingw-32.cmake"));
			public static string Mingw64CmakePath                    => GetCachedPath (ref mingw64CmakePath, ()                    => Path.Combine (BuildBinDir, "mingw-64.cmake"));

			// AdoptOpenJDK
			public static string OldOpenJDKInstallDir                => GetCachedPath (ref oldOpenJDKInstallDir, ()                => Path.Combine (ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainDirectory), "jdk"));
			public static string OpenJDK8InstallDir                  => GetCachedPath (ref openJDK8InstallDir, ()                   => Path.Combine (ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainDirectory), "jdk-1.8"));
			public static string OpenJDK8CacheDir                    => GetCachedPath (ref openJDK8CacheDir, ()                     => ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainCacheDirectory));

			public static string OpenJDK11InstallDir                 => GetCachedPath (ref openJDK11InstallDir, ()                   => Path.Combine (ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainDirectory), "jdk-11"));
			public static string OpenJDK11CacheDir                   => GetCachedPath (ref openJDK11CacheDir, ()                     => ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainCacheDirectory));
			// bundle
			public static string BCLTestsArchiveName                 = "bcl-tests.zip";

			// Mono Archive
			public static string MonoArchiveMonoHash                 => ctx.BuildInfo.FullMonoHash;
			public static string MonoArchiveBaseFileName             => $"android-{Defaults.MonoSdksConfiguration}-{ArchiveOSType}-{MonoArchiveMonoHash}";
			public static string MonoArchiveWindowsBaseFileName      => $"android-release-Windows-{MonoArchiveMonoHash}";
			public static string MonoArchiveFileName                 => $"{MonoArchiveBaseFileName}.7z";
			public static string MonoArchiveWindowsFileName          => $"{MonoArchiveWindowsBaseFileName}.7z";
			public static string MonoArchiveLocalPath                => Path.Combine (ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainCacheDirectory), MonoArchiveFileName);
			public static string MonoArchiveWindowsLocalPath         => Path.Combine (ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainCacheDirectory), MonoArchiveWindowsFileName);

			// .NET 6
			public static string NetcoreAppRuntimeAndroidARM         => GetCachedPath (ref netcoreAppRuntimeAndroidARM, () => GetNetcoreAppRuntimePath (ctx, "arm"));
			public static string NetcoreAppRuntimeAndroidARM64       => GetCachedPath (ref netcoreAppRuntimeAndroidARM64, () => GetNetcoreAppRuntimePath (ctx, "arm64"));
			public static string NetcoreAppRuntimeAndroidX86         => GetCachedPath (ref netcoreAppRuntimeAndroidX86, () => GetNetcoreAppRuntimePath (ctx, "x86"));
			public static string NetcoreAppRuntimeAndroidX86_64      => GetCachedPath (ref netcoreAppRuntimeAndroidX86_64, () => GetNetcoreAppRuntimePath (ctx, "x64"));

			public static string MicrosoftNETWorkloadMonoToolChainDir => Path.Combine (
				XAPackagesDir,
				$"microsoft.net.workload.mono.toolchain.manifest-{ctx.Properties.GetRequiredValue (KnownProperties.DotNetPreviewVersionBand)}",
				ctx.Properties.GetRequiredValue (KnownProperties.MicrosoftNETCoreAppRefPackageVersion),
				"data"
			);

			// CMake
			public static string CmakeMSBuildPropsName               = "cmake-config.props";
			public static string CmakeShellScriptsPropsName          = "cmake-config.sh";
			public static string CmakeMonodroidTargets               = "cmake-monodroid.targets";
			public static string MonodroidSourceDir                  => GetCachedPath (ref monodroidSourceDir, () => Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "src", "monodroid"));

			// Other
			public static string AndroidNdkDirectory                 => ctx.Properties.GetRequiredValue (KnownProperties.AndroidNdkDirectory);
			public static string AndroidToolchainRootDirectory       => GetCachedPath (ref androidToolchainRootDirectory,       () => Path.Combine (AndroidNdkDirectory, "toolchains", "llvm", "prebuilt", NdkToolchainOSTag));
			public static string AndroidToolchainBinDirectory        => GetCachedPath (ref androidToolchainBinDirectory,        () => Path.Combine (AndroidToolchainRootDirectory, "bin"));
			public static string AndroidToolchainSysrootLibDirectory => GetCachedPath (ref androidToolchainSysrootLibDirectory, () => Path.Combine (AndroidToolchainRootDirectory, "sysroot", "usr", "lib"));
			public static string WindowsBinutilsInstallDir           => GetCachedPath (ref windowsBinutilsInstallDir,           () => Path.Combine (InstallMSBuildDir, "binutils"));
			public static string HostBinutilsInstallDir              => GetCachedPath (ref hostBinutilsInstallDir,              () => Path.Combine (InstallMSBuildDir, ctx.Properties.GetRequiredValue (KnownProperties.HostOS), "binutils"));
			public static string BinutilsCacheDir                    => ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainCacheDirectory);
			public static string AndroidBuildToolsCacheDir           => ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainCacheDirectory);

			// not really configurables, merely convenience aliases for more frequently used paths that come from properties
			public static string XAInstallPrefix                => ctx.Properties.GetRequiredValue (KnownProperties.XAInstallPrefix);
			public static string XAPackagesDir                  = DetermineNugetPackagesDir (ctx);
			public static string MonoSourceFullPath             => ctx.Properties.GetRequiredValue (KnownProperties.MonoSourceFullPath);
			public static string MonoSdksTpnPath                => GetCachedPath (ref monoSdksTpnPath, ()         => Path.Combine (MonoSDKSOutputDir, "android-tpn"));
			public static string MonoSdksTpnExternalPath        => GetCachedPath (ref monoSdksTpnExternalPath, () => Path.Combine (MonoSdksTpnPath, "external"));
			public static string MonoLlvmTpnPath                => GetCachedPath (ref monoLlvmTpnPath, () => {
				var path = Path.Combine (MonoSdksTpnExternalPath, "llvm-project", "llvm");
				return Directory.Exists (path) ? path : Path.Combine (MonoSdksTpnExternalPath, "llvm");
			});

			static string GetNetcoreAppRuntimePath (Context ctx, string androidTarget)
			{
				return Path.Combine (
					XAPackagesDir,
					$"microsoft.netcore.app.runtime.mono.android-{androidTarget}",
					ctx.Properties.GetRequiredValue (KnownProperties.MicrosoftNETCoreAppRefPackageVersion),
					"runtimes",
					$"android-{androidTarget}"
				);
			}

			static string DetermineNugetPackagesDir (Context ctx)
			{
				return Path.GetFullPath (
					Path.Combine (
						ctx.Properties.GetRequiredValue (KnownProperties.PkgXamarin_LibZipSharp),
						"..",
						".."
					)
				);
			}

			static string EnsureAndroidToolchainBinDirectories ()
			{
				if (androidToolchainBinDirectory != null)
					return androidToolchainBinDirectory;

				androidToolchainBinDirectory = Path.Combine (ctx.Properties.GetRequiredValue (KnownProperties.AndroidNdkDirectory), "toolchains", "llvm", "prebuilt", NdkToolchainOSTag, "bin");
				return androidToolchainBinDirectory;
			}

			static string GetCachedPath (ref string? variable, Func<string> creator)
			{
				if (!String.IsNullOrEmpty (variable))
					return variable!;

				variable = Path.GetFullPath (creator ());
				return variable;
			}

			static string? testBinDir;
			static string? buildBinDir;
			static string? mingwBinDir;
			static string? binDir;
			static string? netCoreBinDir;
			static string? monoSDKsOutputDir;
			static string? androidToolchainRootDirectory;
			static string? androidToolchainBinDirectory;
			static string? androidToolchainSysrootLibDirectory;
			static string? monoProfileDir;
			static string? monoProfileToolsDir;
			static string? bclTestsDestDir;
			static string? bclTestsArchivePath;
			static string? bclFacadeAssembliesSourceDir;
			static string? bclWindowsOutputDir;
			static string? bclWindowsAssembliesSourceDir;
			static string? bclWindowsFacadeAssembliesSourceDir;
			static string? installBCLFrameworkDir;
			static string? installBCLFrameworkFacadesDir;
			static string? installBCLFrameworkRedistListDir;
			static string? installMSBuildDir;
			static string? outputIncludeDir;
			static string? mingw32CmakePath;
			static string? mingw64CmakePath;
			static string? mingw32CmakeTemplatePath;
			static string? mingw64CmakeTemplatePath;
			static string? monoRuntimesEnabledAbisCachePath;
			static string? frameworkListInstallPath;
			static string? correttoCacheDir;
			static string? correttoInstallDir;
			static string? profileAssembliesProjitemsPath;
			static string? bclTestsSourceDir;
			static string? installHostBCLDir;
			static string? installHostBCLFacadesDir;
			static string? installWindowsBCLDir;
			static string? installWindowsBCLFacadesDir;
			static string? installBCLDesignerDir;
			static string? monoAndroidFrameworksRootDir;
			static string? externalJavaInteropDir;
			static string? monoSdksTpnPath;
			static string? monoSdksTpnExternalPath;
			static string? monoSDKSIncludeDestDir;
			static string? monoLlvmTpnPath;
			static string? openJDK8InstallDir,  openJDK11InstallDir;
			static string? openJDK8CacheDir,    openJDK11CacheDir;
			static string? oldOpenJDKInstallDir;
			static string? configurationPropsGeneratedPath;
			static string? windowsBinutilsInstallDir;
			static string? hostBinutilsInstallDir;
			static string? netcoreAppRuntimeAndroidARM;
			static string? netcoreAppRuntimeAndroidARM64;
			static string? netcoreAppRuntimeAndroidX86;
			static string? netcoreAppRuntimeAndroidX86_64;
			static string? monodroidSourceDir;
		}
	}
}
