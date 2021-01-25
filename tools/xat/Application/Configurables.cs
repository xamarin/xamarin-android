using System;
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
		static Context ctx => Context.Instance;

		public const string MSBuildProgName = "msbuild";
		public const string XABuildProgName = "xabuild";
		public const string AndroidManifestFileName = "AndroidManifest.xml";
		public const string AVDName = "XamarinAndroidTestRunner64";
		public const string TestKeyStoreAlias = "mykey";
		public const string TestKeyStorePassword = "android";
		public const string TestStorePassword = "android";

		public const int ApkdiffApkSizeThreshold = 50*1024;
		public const int ApkdiffAssemblySizeThreshold = 50*1024;

		public static partial class Defaults
		{
			public const string DefaultConfiguration = "Debug";
			public const string DefaultMSBuildProg = MSBuildProgName;

			public static readonly LoggingVerbosity LoggingVerbosity = LoggingVerbosity.Normal;

			/// <summary>
			///   Prefix for all the log files created by the bootstrapper.
			/// </summary>
			public const string LogFilePrefix = "tests";
			
			/// <summary>
			///   Default make/ninja/etc concurrency. If set to <c>0</c> then the actual concurrency level is determined
			///   by looking at the number of CPUs and equals that value + 1.
			/// </summary>
			public const uint MakeConcurrency = 0;

			public const ushort AdbEmulatorPort = 5570;
			public const string AndroidSdkVersion = "29";

			/// <summary>
			///   Length to truncate the git commit hash to.
			/// </summary>
			public const uint AbbreviatedHashLength = 7;
		}

		public static partial class Paths
		{
			public static readonly string LocalNugetPath  = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, ".nuget", "NuGet.exe");
			public static readonly string BinDirRoot      = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "bin");

			public static string BuildBinDir              => GetCachedPath (ref buildBinDir, ()              => Path.Combine (BinDirRoot, $"Build{ctx.Configuration}"));
			public static string TestBinDir               => GetCachedPath (ref testBinDir, ()               => Path.Combine (BinDirRoot, $"Test{ctx.Configuration}"));
			public static string LogcatFileBase           => GetCachedPath (ref logcatFileBase, ()           => Path.Combine (TestBinDir, $"logcat-{ctx.Configuration}{ctx.Properties.GetRequiredValue (KnownProperties.TestsFlavor)}"));
			public static string TestKeyStore             => GetCachedPath (ref testKeyStore, ()             => Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "src", "Xamarin.Android.Build.Tasks", "Tests", "Xamarin.ProjectTools", "Resources", "Base", "test.keystore"));
			public static string JavaInteropDir           => GetCachedPath (ref javaInteropDir, ()           => ctx.Properties.GetRequiredValue (KnownProperties.JavaInteropFullPath));
			public static string JavaInteropBinDir        => GetCachedPath (ref javaInteropBinDir, ()        => Path.Combine (JavaInteropDir, "bin", ctx.Configuration));
			public static string JavaInteropTestDir       => GetCachedPath (ref javaInteropTestDir, ()       => Path.Combine (JavaInteropDir, "bin", $"Test{ctx.Configuration}"));
			public static string XABuildReleasePath       => GetCachedPath (ref xabuildReleasePath, ()       => Path.Combine (BinDirRoot, "Release", "bin", "xabuild"));
			public static string XABuildConfigurationPath => GetCachedPath (ref xabuildConfigurationPath, () => Path.Combine (BinDirRoot, ctx.Configuration, "bin", "xabuild"));

			// Path where to save stdout/stderr output of the NUnit runner
			public static string NUnitOutputDir           => GetCachedPath (ref nunitOutputDir, () => TestBinDir);

			// Path where to save NUnit XML result files
			public static string NUnitResultDir           => GetCachedPath (ref nunitResultDir, () => BuildPaths.XamarinAndroidSourceRoot);

			// Support for running AAB tests against a system installation of XA (see also `Context.Init ()`)
			public static string DefaultBundleToolJarPath => ctx.Properties.GetValue (KnownProperties.AndroidBundleToolJarPath) ?? String.Empty;
			public static string DefaultJavaPath          => Path.Combine (ctx.Properties.GetRequiredValue (KnownProperties.JavaSdkDirectory), "bin", "java");

			public static string ApkDescDirectory    => TestBinDir;

			static string GetCachedPath (ref string? variable, Func<string> creator)
			{
				if (!String.IsNullOrEmpty (variable))
					return variable!;

				variable = Path.GetFullPath (creator ());
				return variable;
			}

			static string? buildBinDir;
			static string? testBinDir;
			static string? logcatFileBase;
			static string? testKeyStore;
			static string? nunitOutputDir;
			static string? nunitResultDir;
			static string? javaInteropDir;
			static string? javaInteropBinDir;
			static string? javaInteropTestDir;
			static string? xabuildReleasePath;
			static string? xabuildConfigurationPath;
		}
	}
}
