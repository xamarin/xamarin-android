using System;
using System.IO;
using System.Collections.Generic;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	// NOTE: ALL the file/directory paths must be either absolute (unlikely, unless referring to some system location or
	// taken from Configurables.Paths) or relative to Xamarin.Android source root. They will be converted to correct
	// paths during execution.
	partial class TestCollection
	{
		const int NumberOfTestNodes = 3;

		readonly string testsFlavor;
		readonly string configuration;
		readonly string outputPath;
		readonly string timingDefinitionsPath;

		public IDictionary<string, XATest> AllSuitesByName { get; } = new SortedDictionary<string, XATest> (StringComparer.OrdinalIgnoreCase);
		public IDictionary<string, XATest> AllSuitesByID   { get; } = new SortedDictionary<string, XATest> (StringComparer.OrdinalIgnoreCase);
		public IDictionary<string, TestGroup> GroupsByName { get; } = new SortedDictionary<string, TestGroup> (StringComparer.OrdinalIgnoreCase);
		public IDictionary<string, TestGroup> GroupsByID   { get; } = new SortedDictionary<string, TestGroup> (StringComparer.OrdinalIgnoreCase);

		public TestCollection ()
		{
			testsFlavor = Context.Instance.Properties.GetRequiredValue (KnownProperties.TestsFlavor);
			configuration = Context.Instance.Configuration;
			outputPath = Configurables.Paths.TestBinDir;
			timingDefinitionsPath = Path.Combine ("build-tools", "scripts", "TimingDefinitions.txt");

			CreateTestGroups ();
			AddApkTests ();
			AddHostUnitTests ();
			AddMSBuildTimingTests ();

			// TODO: dotnet runner
			// TODO: dotnet tests
			// TODO: check boot times (build-tools/automation/yaml-templates/run-msbuild-device-tests.yaml)
			// TODO: add commercial tests
		}

		void AddMSBuildTimingTests ()
		{
			if (Utilities.FileExists (Configurables.Paths.XABuildReleasePath)) {
				MSBuildTiming.MSBuildTimingTestCommand.XABuild = Configurables.Paths.XABuildReleasePath;
			} else {
				MSBuildTiming.MSBuildTimingTestCommand.XABuild = Configurables.Paths.XABuildConfigurationPath;
			}

			MSBuildTiming.MSBuildTimingTestCommand.TimingLogger = $"Xamarin.Android.Tools.BootstrapTasks.TimingLogger,{Context.Instance.Properties.GetRequiredValue(KnownProperties.BootstrapTasksAssembly)}";

			MSBuildTiming.MSBuildTimingTestCommand.OutputFilePathCreator = (TestMSBuildTiming test, MSBuildTiming.MSBuildTimingTestCommand command) => {
				// Use test.ID instead of test.Name since `Name` may contain spaces as well as invalid path characters
				return Utilities.EnsureFullPath (Path.Combine (test.OutputDirPath, $"Timing_{test.ShortName}-{command.ID}_{Context.Instance.Configuration}_{test.ID}"));
			};

			MSBuildTiming.MSBuildTimingTestCommand.DefaultAndroidSupportedAbis = "x86";

			string outputDir = Configurables.Paths.TestBinDir;

			var standardBuildCommands = new Shared.SharedCommandContainer {
				Commands = {
					new Shared.BuildWithMSBuild (),
				},
			};

			TestMSBuildTiming.GlobalInitCommands = new List<TestCommand> {
				new MSBuildTiming.MSBuildPrep (Path.Combine (outputDir, "Timing_*")),
				new MSBuildTiming.AcquireAndroidTarget (),
			};

			var standardRunCommands = new MSBuildTiming.MSBuildTimingCommandContainer {
				Commands = {
					new MSBuildTiming.FreshBuild (),
					new MSBuildTiming.FreshInstall (),
					new MSBuildTiming.SecondBuild (),
					new MSBuildTiming.SecondInstall (),
					new MSBuildTiming.TouchCSharpBuild (),
					new MSBuildTiming.TouchCSharpInstall (),
					new MSBuildTiming.TouchAndroidResourceBuild (),
					new MSBuildTiming.TouchAndroidResourceInstall (),
					new MSBuildTiming.ProcessMSBuildTiming (
						(TestMSBuildTiming t) => Path.Combine (BuildPaths.XamarinAndroidSourceRoot, $"TestResult-Timing-{t.ShortName}.csv"),
						$"-{Context.Instance.Configuration}"
					),
				},
			};

			TestMSBuildTiming.GlobalShutdownCommands = new List<TestCommand> {
				new MSBuildTiming.ReleaseAndroidTarget (),
			};

			string directoryPath = Path.Combine ("samples", "HelloWorld");
			var test = new TestMSBuildTiming ("HelloWorld", "HelloWorld", Path.Combine (directoryPath, "HelloWorld.csproj")) {
				AndroidResourceFile = Path.Combine (directoryPath, "Resources", "values", "Strings.xml"),
				CSharpFile = Path.Combine (directoryPath, "MainActivity.cs"),
				DirectoryPath = directoryPath,
				OutputDirPath = outputDir,

				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			AddTest (test);

			directoryPath = Path.Combine ("tests", "Xamarin.Forms-Performance-Integration");
			string droidPath = Path.Combine (directoryPath, "Droid");
			test = new TestMSBuildTiming ("Xamarin.Forms-Integration", "XF", Path.Combine (droidPath, "Xamarin.Forms.Performance.Integration.Droid.csproj")) {
				AndroidResourceFile = Path.Combine (droidPath, "Resources", "values", "styles.xml"),
				CSharpFile = Path.Combine (droidPath, "MainActivity.cs"),
				DirectoryPath = directoryPath,
				OutputDirPath = outputDir,
				Restore = Path.Combine (directoryPath, "Xamarin.Forms.Performance.Integration.csproj"),

				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			AddTest (test);
		}

		void AddHostUnitTests ()
		{
			var standardBuildCommands = new Shared.SharedCommandContainer {
				Commands = {
					new Shared.BuildWithMSBuild (),
				},
			};

			var standardRunCommands = new Host.HostCommandContainer {
				Commands = {
					new Host.RunUnitTests (),
					new Host.RenameTestCases (),
				},
			};

			var test = new TestHostUnit (
				familyName: "Build Tests",
				name: "Build Tests",
				testAssemblyPath: Path.Combine (Configurables.Paths.TestBinDir, "net472", "Xamarin.Android.Build.Tests.dll"),
				testProjectFilePath: Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "src", "Xamarin.Android.Build.Tasks", "Tests", "Xamarin.Android.Build.Tests", "Xamarin.Android.Build.Tests.csproj")
			) {
				Timeout = TimeSpan.FromMinutes (90),

				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands
				},
			};
			AddTest (test);
			AddToGroup (test, GroupNick.UnitSmokeTests);
			AddToGroup (test, GroupNick.UnitMSBuildLegacyMacOSOnNode);
			AddToGroup (test, GroupNick.UnitMSBuildExtraNoNode);

			// We could introduce a factory for Java.Interop tests which would enumerate the files using a glob pattern on the
			// runtime, but then we'd not be able to `xat list` them until they were built - discoverability would be
			// diminished. The set of tests changes so infrequently that it shouldn't be a problem to just list them
			// manually here.  Please keep the list sorted alphabetically.
			//
			// project path is relative to external/Java.Interop/tests
			//
			var javaInteropTests = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase) {
				{ "generator-Tests.dll",                               Path.Combine ("generator-Tests",                               "generator-Tests.csproj") },
				{ "Java.Interop.Dynamic-Tests.dll",                    Path.Combine ("Java.Interop.Dynamic-Tests",                    "Java.Interop.Dynamic-Tests.csproj") },
				{ "Java.Interop.Export-Tests.dll",                     Path.Combine ("Java.Interop.Export-Tests",                     "Java.Interop.Export-Tests.csproj") },
				{ "Java.Interop-PerformanceTests.dll",                 Path.Combine ("Java.Interop-PerformanceTests",                 "Java.Interop-PerformanceTests.csproj")},
				{ "Java.Interop-Tests.dll",                            Path.Combine ("Java.Interop-Tests",                            "Java.Interop-Tests.csproj") },
				{ "Java.Interop.Tools.Generator-Tests.dll",            Path.Combine ("Java.Interop.Tools.Generator-Tests",            "Java.Interop.Tools.Generator-Tests.csproj") },
				{ "Java.Interop.Tools.JavaCallableWrappers-Tests.dll", Path.Combine ("Java.Interop.Tools.JavaCallableWrappers-Tests", "Java.Interop.Tools.JavaCallableWrappers-Tests.csproj") },
				{ "Java.Interop.Tools.JavaSource-Tests.dll",           Path.Combine ("Java.Interop.Tools.JavaSource-Tests",           "Java.Interop.Tools.JavaSource-Tests.csproj") },
				{ "logcat-parse-Tests.dll",                            Path.Combine ("logcat-parse-Tests",                            "logcat-parse-Tests.csproj") },
				{ "Xamarin.Android.Tools.ApiXmlAdjuster-Tests.dll",    Path.Combine ("Xamarin.Android.Tools.ApiXmlAdjuster-Tests",    "Xamarin.Android.Tools.ApiXmlAdjuster-Tests.csproj") },
				{ "Xamarin.Android.Tools.Bytecode-Tests.dll",          Path.Combine ("Xamarin.Android.Tools.Bytecode-Tests",          "Xamarin.Android.Tools.Bytecode-Tests.csproj") },
				{ "Xamarin.SourceWriter-Tests.dll",                    Path.Combine ("Xamarin.SourceWriter-Tests",                    "Xamarin.SourceWriter-Tests.csproj") },
			};

			var prepareEnvironment = new Host.PrepareEnvironmentVariables ((TestHostUnit t) => {
				string assemblyFileName = Path.GetFileName (t.TestAssemblyPath);

				t.EnvironmentVariables ["ANDROID_SDK_PATH"] = Context.Instance.Properties.GetRequiredValue (KnownProperties.AndroidSdkDirectory);
				t.EnvironmentVariables ["MONO_TRACE_LISTENER"] = "Console.Out";
				t.EnvironmentVariables ["JAVA_INTEROP_GREF_LOG"] = Path.Combine (Configurables.Paths.JavaInteropTestDir, $"g-{assemblyFileName}.txt");
				t.EnvironmentVariables ["JAVA_INTEROP_LREF_LOG"] = Path.Combine (Configurables.Paths.JavaInteropTestDir, $"l-{assemblyFileName}.txt");
				t.EnvironmentVariables ["JI_JVM_PATH"] = Context.Instance.Properties.GetRequiredValue (KnownProperties.JavaSdkDirectory);
			});

			var buildDSO = new Shared.BuildWithMSBuild {
				ProjectFilePath = Path.Combine (Configurables.Paths.JavaInteropDir, "src", "java-interop", "java-interop.csproj")
			};
			var copyDSO = new Shared.CopyFiles (Path.Combine (Configurables.Paths.JavaInteropBinDir, "libjava-interop.*"), Configurables.Paths.JavaInteropTestDir, required: true);

			TimeSpan jiTimeout = TimeSpan.FromMinutes (15);
			foreach (var kvp in javaInteropTests) {
				string jiTestAssembly = kvp.Key;
				string jiTestProject = Path.Combine (Configurables.Paths.JavaInteropDir, "tests", kvp.Value);

				test = new TestHostUnit (
					familyName: "JavaInterop",
					name: $"JavaInterop: {Path.GetFileNameWithoutExtension(jiTestAssembly)}",
					testAssemblyPath: Path.Combine (Configurables.Paths.JavaInteropTestDir, jiTestAssembly),
					testProjectFilePath: jiTestProject
				) {
					Timeout = jiTimeout,

					GlobalInitCommands = {
						buildDSO,
						copyDSO,
					},

					BuildCommands = {
						standardBuildCommands,
					},

					RunCommands = {
						prepareEnvironment,
						standardRunCommands,
					},

					GlobalShutdownCommands = {
						new Host.JavaInteropReportMissingAssemblies (Path.Combine (Configurables.Paths.JavaInteropTestDir, "*-*Tests.dll"), javaInteropTests.Keys),
					},
				};

				AddTest (test);
				AddToGroup (test, GroupNick.UnitJavaInterop);
			}

			test = new TestHostUnit (
				familyName: "MSBuild Device Integration",
				name: "MSBuild Device Integration",
				testAssemblyPath: Path.Combine (Configurables.Paths.TestBinDir, "net472", "MSBuildDeviceIntegration.dll"),
				testProjectFilePath: Path.Combine ("tests", "MSBuildDeviceIntegration", "MSBuildDeviceIntegration.csproj")
			) {
				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					new Host.AcquireAndroidTarget (),
					standardRunCommands,
					new Host.ReleaseAndroidTarget (),
				},
			};
			AddTest (test);
			AddToGroup (test, GroupNick.UnitSmokeTests, $"TestResult-MSBuildDeviceIntegrationSmoke-{Context.Instance.Configuration}.xml");
		}

		//
		// All permission name must not include the `android.permission.` prefix, just the permission name.
		// The prefix is added by the RunTestApks command automatically
		//
		void AddApkTests ()
		{
			//
			// Commands which are executed only once - before the first test is ran
			//
			TestAPK.GlobalInitCommands = new List<TestCommand> {
				new APK.AcquireAndroidTarget (),
			};

			var standardBuildCommands = new Shared.SharedCommandContainer {
				Commands = {
					new Shared.BuildWithMSBuild {
						Target = "SignAndroidPackage"
					},
				},
			};

			var standardRunCommands = new APK.APKCommandContainer {
				Commands = {
					new APK.Deploy {
						FailureMode = CommandFailureMode.Error
					},
					new APK.CheckAndRecordApkSizes (),
					new APK.RunTestApks (),
					new APK.RenameApkTestCases (),
				},
			};

			//
			// Commands which are executed only once - after the last test ran
			//
			TestAPK.GlobalShutdownCommands = new List<TestCommand> {
				new APK.ReleaseAndroidTarget (),
			};

			// Mono.Android tests
			string androidPackageName = "Mono.Android_Tests";
			string projectFilePath = Path.Combine ("tests", "Mono.Android-Tests", "Mono.Android-Tests.csproj");
			string testName = "Mono.Android";
			TestAPK test = new TestAPK (androidPackageName, testName, projectFilePath) {
				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetCommonMonoAndroidProperties (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			testName = "AOT: Mono.Android";
			test = new TestAPK (androidPackageName, testName, projectFilePath) {
				TestFlavor = APKTestFlavor.AOT
			};
			SetCommonMonoAndroidProperties (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			testName = "MonoBundle: Mono.Android";
			test = new TestAPK (androidPackageName, testName, projectFilePath) {
				TestFlavor = APKTestFlavor.MonoBundle,

				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetCommonMonoAndroidProperties (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			androidPackageName = "Mono.Android_TestsMultiDex";
			projectFilePath = Path.Combine ("tests", "Mono.Android-Tests", "Runtime-MultiDex", "Mono.Android-TestsMultiDex.csproj");
			testName = "MultiDex: Mono.Android";
			test = new TestAPK (androidPackageName, testName, projectFilePath) {
				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetCommonMonoAndroidProperties (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			androidPackageName = "Mono.Android_TestsAppBundle";
			projectFilePath = Path.Combine ("tests", "Mono.Android-Tests", "Runtime-AppBundle", "Mono.Android-TestsAppBundle.csproj");
			testName = "AppBundle: Mono.Android";
			test = new TestAPK (androidPackageName, testName, projectFilePath, isAndroidAppBundle: true) {
				TestFlavor = APKTestFlavor.AndroidApplicationBundle, // Redundant, as it's set by the constructor, but
																	 // let's be explicit
				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetCommonMonoAndroidProperties (test, addApkSizes: false);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			// TODO: add .NET6 Mono.Android-Tests (see commit 9fdf6c46fc4f01e55b74bd6a0bf73b9ff731e766)

			// Xamarin.Android.Locale tests
			androidPackageName = "Xamarin.Android.Locale_Tests";
			projectFilePath = Path.Combine ("tests", "locales", "Xamarin.Android.Locale-Tests", "Xamarin.Android.Locale-Tests.csproj");
			testName = "Xamarin.Android Locale";
			test = new TestAPK (androidPackageName, testName, projectFilePath) {
				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetCommonLocaleTestProperties (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			testName = "AOT: Xamarin.Android Locale";
			test = new TestAPK (androidPackageName, testName, projectFilePath) {
				TestFlavor = APKTestFlavor.AOT,

				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetCommonLocaleTestProperties (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			// Xamarin.Forms.Performance.Integration tests
			androidPackageName = "Xamarin.Forms_Performance_Integration";
			projectFilePath = Path.Combine ("tests", "Xamarin.Forms-Performance-Integration", "Droid", "Xamarin.Forms.Performance.Integration.Droid.csproj");
			testName = "Xamarin.Forms Performance Integration";
			test = new TestAPK (androidPackageName, testName, projectFilePath) {
				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetCommonXFIntegrationTestProperties (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			testName = "AOT: Xamarin.Forms Performance Integration";
			test = new TestAPK (androidPackageName, testName, projectFilePath) {
				TestFlavor = APKTestFlavor.AOT,

				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetCommonXFIntegrationTestProperties (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			testName = "MonoBundle: Xamarin.Forms Performance Integration";
			test = new TestAPK (androidPackageName, testName, projectFilePath) {
				TestFlavor = APKTestFlavor.MonoBundle,

				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetCommonXFIntegrationTestProperties (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			// Xamarin.Android.EmbeddedDSO tests
			androidPackageName = "Xamarin.Android.EmbeddedDSO_Test";
			projectFilePath = Path.Combine ("tests", "EmbeddedDSOs", "EmbeddedDSO", "EmbeddedDSO.csproj");
			testName = "Embedded DSO";
			test = new TestAPK (androidPackageName, testName, projectFilePath) {
				AndroidPermissions = { "READ_PHONE_STATE" },

				Instrumentations = {
					new TestAndroidInstrumentation ("xamarin.android.embeddeddso_test.NUnitInstrumentation") {
						ResultsPath = Path.Combine (outputPath, $"TestResult-{androidPackageName}.nunit.xml"),
					}
				},

				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetStandardTimingDefinitionProperties (test);
			SetStandardStoragePermissions (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			// Xamarin.Android.Bcl tests
			androidPackageName = "Xamarin.Android.Bcl_Tests";
			projectFilePath = Path.Combine ("tests", "BCL-Tests", "Xamarin.Android.Bcl-Tests", "Xamarin.Android.Bcl-Tests.csproj");
			testName = "BCL";
			test = new TestAPK (androidPackageName, testName, projectFilePath) {
				AndroidPermissions = { "READ_PHONE_STATE" },

				Instrumentations = {
					new TestAndroidInstrumentation ("xamarin.android.bcltests.XUnitInstrumentation") {
						ResultsPath = Path.Combine (outputPath, $"TestResult-{androidPackageName}.xunit.xml"),
						LogcatFilenameDistincion = ".xunit",
						TimeoutInMS = 4500000,
					},

					new TestAndroidInstrumentation ("xamarin.android.bcltests.NUnitInstrumentation") {
						ResultsPath = Path.Combine (outputPath, $"TestResult-{androidPackageName}.nunit.xml"),
						LogcatFilenameDistincion = ".nunit",
						TimeoutInMS = 3600000,
					}
				},

				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetStandardStoragePermissions (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			// Xamarin.Android.JcwGen tests
			androidPackageName = "Xamarin.Android.JcwGen_Tests";
			projectFilePath = Path.Combine ("tests", "CodeGen-Binding", "Xamarin.Android.JcwGen-Tests", "Xamarin.Android.JcwGen-Tests.csproj");
			testName = "JcwGen";
			test = new TestAPK (androidPackageName, testName, projectFilePath) {
				Instrumentations = {
					new TestAndroidInstrumentation ("xamarin.android.jcwgentests.TestInstrumentation") {
						ResultsPath = Path.Combine (outputPath, $"TestResult-{androidPackageName}.xml"),
					}
				},

				BuildCommands = {
					standardBuildCommands,
				},

				RunCommands = {
					standardRunCommands,
				},
			};
			SetStandardTimingDefinitionProperties (test);
			SetStandardStoragePermissions (test);
			AddTest (test);
			AddToGroup (test, GroupNick.APK);

			void SetCommonXFIntegrationTestProperties (TestAPK test)
			{
				test.Activity = $"{test.AndroidPackageName}/xamarin.forms.performance.integration.MainActivity";

				test.TimingDefinitionsFilename = Path.Combine ("tests", "Xamarin.Forms-Performance-Integration", "Droid", "timing-definitions.txt");
				test.TimingResultsFilename = $"TestResult-{test.AndroidPackageName}-times.csv";

				SetStandardApkSizesProperties (test);
			}

			void SetCommonLocaleTestProperties (TestAPK test)
			{
				SetStandardTimingDefinitionProperties (test);
				SetStandardStoragePermissions (test);

				test.Instrumentations.Add (new TestAndroidInstrumentation ("xamarin.android.localetests.TestInstrumentation") {
						ResultsPath = Path.Combine (outputPath, $"TestResult-{test.AndroidPackageName}.xml"),
					}
				);
			}

			void SetCommonMonoAndroidProperties (TestAPK test, bool addApkSizes = true)
			{
				SetStandardTimingDefinitionProperties (test);
				SetStandardStoragePermissions (test);

				test.Instrumentations.Add (new TestAndroidInstrumentation ("xamarin.android.runtimetests.NUnitInstrumentation") {
						ResultsPath = Path.Combine (outputPath, $"TestResult-{test.AndroidPackageName}.xml"),
					}
				);

				if (addApkSizes)
					SetStandardApkSizesProperties (test);
			};

			void SetStandardTimingDefinitionProperties (TestAPK test)
			{
				test.TimingDefinitionsFilename = timingDefinitionsPath;
				test.TimingResultsFilename = $"TestResult-{test.AndroidPackageName}-times.csv";
			}

			void SetStandardApkSizesProperties (TestAPK test)
			{
				test.ApkSizesInputFilename = $"apk-sizes-{test.AndroidPackageName}-{configuration}{testsFlavor}.txt";
				test.ApkSizesDefinitionFilename = Path.Combine ("build-tools", "scripts", "ApkSizesDefinitions.txt");
				test.ApkSizesResultsFilename = $"TestResult-{test.AndroidPackageName}-values-{configuration}.csv";
			}

			void SetStandardStoragePermissions (TestAPK test)
			{
				test.AndroidPermissions.Add ("READ_EXTERNAL_STORAGE");
				test.AndroidPermissions.Add ("WRITE_EXTERNAL_STORAGE");
			}
		}

		void AddTest (XATest test)
		{
			if (AllSuitesByName.ContainsKey (test.Name)) {
				throw new InvalidOperationException ($"Duplicate test name '{test.Name}'");
			}

			if (AllSuitesByID.ContainsKey (test.ID)) {
				throw new InvalidOperationException ($"Duplicate test ID '{test.ID}'");
			}

			AllSuitesByName.Add (test.Name, test);
			AllSuitesByID.Add (test.ID, test);
		}
	}
}
