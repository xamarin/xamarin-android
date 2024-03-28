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
		const string BinutilsVersion                = "L_17.0.6-7.1.0";

		const string MicrosoftOpenJDK17Version      = "17.0.8";
		const string MicrosoftOpenJDK17Release      = "17.0.8.7";
		const string MicrosoftOpenJDK17RootDirName  = "jdk-17.0.8+7";

		const string AdoptOpenJDKRelease = "8.0"; // build_number.0
		static readonly string AdoptOpenJDKUrlVersion = $"8u{AdoptOpenJDKUpdate}{AdoptOpenJDKBuild}";
		static readonly string AdoptOpenJDKTag = $"jdk8u{AdoptOpenJDKUpdate}-{AdoptOpenJDKBuild}";
		static readonly string AdoptOpenJDKVersion = $"1.8.0.{AdoptOpenJDKUpdate}";

		static Context ctx => Context.Instance;

		public static partial class Urls
		{
			// https://github.com/adoptium/temurin8-binaries/releases/download/jdk8u345-b01/OpenJDK8U-jdk_x64_linux_hotspot_8u345b01.tar.gz
			// https://github.com/adoptium/temurin8-binaries/releases/download/jdk8u332-b09/OpenJDK8U-jdk_x64_mac_hotspot_8u332b09.tar.gz
			// https://github.com/adoptium/temurin8-binaries/releases/download/jdk8u345-b01/OpenJDK8U-jdk_x64_windows_hotspot_8u345b01.zip
			public static readonly Uri AdoptOpenJDK8 = new Uri ($"https://github.com/adoptium/temurin8-binaries/releases/download/{AdoptOpenJDKTag}/OpenJDK8U-jdk_{AdoptOpenJDKOperatingSystem}_hotspot_{AdoptOpenJDKUrlVersion}.{AdoptOpenJDKArchiveExtension}");

			// https://aka.ms/download-jdk/microsoft-jdk-17.0.8-linux-x64.tar.gz
			// https://aka.ms/download-jdk/microsoft-jdk-17.0.8-macOS-x64.tar.gz
			// https://aka.ms/download-jdk/microsoft-jdk-17.0.8-windows-x64.zip
			public static readonly Uri MicrosoftOpenJDK17 = new Uri ($"https://aka.ms/download-jdk/microsoft-jdk-{MicrosoftOpenJDK17Version}-{MicrosoftOpenJDKOperatingSystem}.{MicrosoftOpenJDKFileExtension}");

			/// <summary>
			///   Base URL for all Android SDK and NDK downloads. Used in <see cref="AndroidToolchain"/>
			/// </summary>
			public static readonly Uri AndroidToolchain_AndroidUri = new Uri ("https://dl.google.com/android/repository/");

			public static Uri MonoArchive_BaseUri = new Uri ("https://xamjenkinsartifact.blob.core.windows.net/mono-sdks/");

			public static Uri BinutilsArchive = new Uri ($"https://github.com/xamarin/xamarin-android-binutils/releases/download/{BinutilsVersion}/xamarin-android-toolchain-{BinutilsVersion}.7z");
		}

		public static partial class Defaults
		{
			public static readonly string BinutilsVersion            = Configurables.BinutilsVersion;
			public static readonly char[] PropertyListSeparator            = new [] { ':' };

			public static readonly string JdkFolder                        = "jdk-17";

			public static readonly Version MicrosoftOpenJDK17Version       = new Version (Configurables.MicrosoftOpenJDK17Version);
			public static readonly Version MicrosoftOpenJDK17Release       = new Version (Configurables.MicrosoftOpenJDK17Release);
			public static readonly string  MicrosoftOpenJDK17RootDirName   = Configurables.MicrosoftOpenJDK17RootDirName;

			public static readonly Version AdoptOpenJDK8Version     = new Version (Configurables.AdoptOpenJDKVersion);
			public static readonly Version AdoptOpenJDK8Release     = new Version (Configurables.AdoptOpenJDKRelease);
			public static readonly string  AdoptOpenJDK8RootDirName = Configurables.AdoptOpenJDKTag;

			public const string DotNetTestRuntimeVersion                   = "3.1.11";

			// Mono runtimes
			public const string DebugFileExtension                         = ".pdb";
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
				{ "armeabi-v7a",    "arm-linux-androideabi" },
				{ "arm64-v8a",      "aarch64-linux-android" },
				{ "x86",            "i686-linux-android" },
				{ "x86_64",         "x86_64-linux-android" },
			};

			/// <summary>
			///   Used in rules.mk generator. Files to include in the XA bundle archives.
			/// </summary>
			public static readonly List <string> BundleZipsInclude = new List <string> {
				"$(ZIP_OUTPUT_BASENAME)/THIRD-PARTY-NOTICES.TXT",
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

			public static readonly List <NDKTool> NDKTools = new List<NDKTool> {
				// Tools prefixed with architecture triple
				new NDKTool (name: "as", prefixed: true),
				new NDKTool (name: "ld", prefixed: true),
				new NDKTool (name: "strip", prefixed: true),

				// Unprefixed tools
				new NDKTool (name: "as"),
				new NDKTool (name: "ld"),
				new NDKTool (name: "llc"),
				new NDKTool (name: "llvm-mc"),
				new NDKTool (name: "llvm-strip"),
			};
		}

		public static partial class Paths
		{
			// Global, compile-time locations
			public static readonly string HomeDir                          = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);
			public static readonly string BootstrapResourcesDir            = Path.Combine (BuildPaths.XAPrepareSourceDir, "Resources");
			public static readonly string BuildToolsDir                    = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "build-tools");
			public static readonly string BuildToolsScriptsDir             = Path.Combine (BuildToolsDir, "scripts");
			public static readonly string BinDirRoot                       = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "bin");
			public static readonly string ExternalDir                      = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "external");
			public static readonly string ExternalGitDepsFilePath          = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, ".external");
			public static readonly string ExternalGitDepsDestDir           = ExternalDir;
			public static readonly string ExternalXamarinAndroidToolsSln   = Path.Combine (ExternalDir, "xamarin-android-tools", "Xamarin.Android.Tools.sln");

			// Dynamic locations used throughout the code
			public static string ExternalJavaInteropDir              => GetCachedPath (ref externalJavaInteropDir, ()              => ctx.Properties.GetRequiredValue (KnownProperties.JavaInteropFullPath));
			public static string TestBinDir                          => GetCachedPath (ref testBinDir, ()                          => Path.Combine (Configurables.Paths.BinDirRoot, $"Test{ctx.Configuration}"));
			public static string BinDir                              => GetCachedPath (ref binDir, ()                              => Path.Combine (Configurables.Paths.BinDirRoot, ctx.Configuration));
			public static string BuildBinDir                         => GetCachedPath (ref buildBinDir, ()                         => Path.Combine (Configurables.Paths.BinDirRoot, $"Build{ctx.Configuration}"));
			public static string ConfigurationPropsGeneratedPath     => GetCachedPath (ref configurationPropsGeneratedPath, ()     => Path.Combine (BuildBinDir, "Configuration.Generated.props"));
			public static string MonoAndroidFrameworksSubDir         = Path.Combine ("xbuild-frameworks", "MonoAndroid");
			public static string MonoAndroidFrameworksRootDir        => GetCachedPath (ref monoAndroidFrameworksRootDir, ()        => Path.Combine (XAInstallPrefix, MonoAndroidFrameworksSubDir));
			public static string InstallMSBuildDir                   => GetCachedPath (ref installMSBuildDir, ()                   => ctx.Properties.GetRequiredValue (KnownProperties.MicrosoftAndroidSdkOutDir));

			// AdoptOpenJDK
			public static string OldOpenJDKInstallDir                => GetCachedPath (ref oldOpenJDKInstallDir, ()                => Path.Combine (ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainDirectory), "jdk"));
			public static string OpenJDK8InstallDir                  => GetCachedPath (ref openJDK8InstallDir, ()                   => Path.Combine (ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainDirectory), "jdk-1.8"));
			public static string OpenJDK8CacheDir                    => GetCachedPath (ref openJDK8CacheDir, ()                     => ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainCacheDirectory));

			public static string OpenJDK17InstallDir                 => GetCachedPath (ref openJDK17InstallDir, ()                   => Path.Combine (ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainDirectory), "jdk-17"));
			public static string OpenJDK17CacheDir                   => GetCachedPath (ref openJDK17CacheDir, ()                     => ctx.Properties.GetRequiredValue (KnownProperties.AndroidToolchainCacheDirectory));

			// .NET 6
			public static string NetcoreAppRuntimeAndroidARM         => GetCachedPath (ref netcoreAppRuntimeAndroidARM, () => GetNetcoreAppRuntimePath (ctx, "arm"));
			public static string NetcoreAppRuntimeAndroidARM64       => GetCachedPath (ref netcoreAppRuntimeAndroidARM64, () => GetNetcoreAppRuntimePath (ctx, "arm64"));
			public static string NetcoreAppRuntimeAndroidX86         => GetCachedPath (ref netcoreAppRuntimeAndroidX86, () => GetNetcoreAppRuntimePath (ctx, "x86"));
			public static string NetcoreAppRuntimeAndroidX86_64      => GetCachedPath (ref netcoreAppRuntimeAndroidX86_64, () => GetNetcoreAppRuntimePath (ctx, "x64"));

			public static string MicrosoftNETWorkloadMonoPackageDir => Path.Combine (
				XAPackagesDir,
				$"microsoft.net.workload.mono.toolchain.{{0}}.manifest-{ctx.Properties.GetRequiredValue (KnownProperties.DotNetMonoManifestVersionBand)}",
				ctx.Properties.GetRequiredValue (KnownProperties.MicrosoftNETCoreAppRefPackageVersion)
			);

			public static string MicrosoftNETWorkloadMonoToolChainDir => Path.Combine (MicrosoftNETWorkloadMonoPackageDir, "data");

			public static string MicrosoftNETWorkloadEmscriptenPackageDir => Path.Combine (
				XAPackagesDir,
				$"microsoft.net.workload.emscripten.{{0}}.manifest-{ctx.Properties.GetRequiredValue (KnownProperties.DotNetEmscriptenManifestVersionBand)}",
				ctx.Properties.GetRequiredValue (KnownProperties.MicrosoftNETWorkloadEmscriptenPackageVersion)
			);

			public static string MicrosoftNETWorkloadEmscriptenDir => Path.Combine (MicrosoftNETWorkloadEmscriptenPackageDir, "data");

			public static string DotNetPreviewPath => ctx.Properties.GetRequiredValue (KnownProperties.DotNetPreviewPath);

			public static string DotNetPreviewTool => Path.Combine (DotNetPreviewPath, "dotnet");

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
			public static string XAPackagesDir                  => ctx.Properties.GetRequiredValue (KnownProperties.XAPackagesDir);

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
			static string? binDir;
			static string? androidToolchainRootDirectory;
			static string? androidToolchainBinDirectory;
			static string? androidToolchainSysrootLibDirectory;
			static string? installMSBuildDir;
			static string? monoAndroidFrameworksRootDir;
			static string? externalJavaInteropDir;
			static string? openJDK8InstallDir,  openJDK17InstallDir;
			static string? openJDK8CacheDir,    openJDK17CacheDir;
			static string? oldOpenJDKInstallDir;
			static string? configurationPropsGeneratedPath;
			static string? windowsBinutilsInstallDir;
			static string? hostBinutilsInstallDir;
			static string? netcoreAppRuntimeAndroidARM;
			static string? netcoreAppRuntimeAndroidARM64;
			static string? netcoreAppRuntimeAndroidX86;
			static string? netcoreAppRuntimeAndroidX86_64;
		}
	}
}
