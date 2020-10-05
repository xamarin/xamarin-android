using System;
using System.IO;
using System.Collections.Generic;
using System.Xml;

namespace Xamarin.Android.Prepare
{
	enum ApkTestPhase
	{
		Plain,
		AOT,
		ProfiledAOT,
		MonoBundle,
	}

	sealed class ApkTestPhaseProperties
	{
		public List<string> Exec { get; } = new List<string> ();
		public List<string> MSBuild { get; } = new List<string> ();
	}

	// NOTE: ALL the file/directory paths must be either absolute (unlikely, unless referring to some system location)
	// or relative to Xamarin.Android source root. They will be converted to correct paths during file generation.
	class Tests
	{
		readonly string testsFlavor;
		readonly string configuration;
		readonly string outputPath;
		readonly string timingDefinitionsPath;
		readonly Dictionary<ApkTestPhase, ApkTestPhaseProperties> apkTestPhaseProperties;

		public IDictionary<string, XATest> AllTests { get; } = new SortedDictionary<string, XATest> (StringComparer.OrdinalIgnoreCase);

		public Tests ()
		{
			testsFlavor = Context.Instance.Properties.GetRequiredValue (KnownProperties.TestsFlavor);
			configuration = Context.Instance.Configuration;
			outputPath = Configurables.Paths.TestBinDir;
			timingDefinitionsPath = Path.Combine ("build-tools", "scripts", "TimingDefinitions.txt");
			apkTestPhaseProperties = new Dictionary<ApkTestPhase, ApkTestPhaseProperties> ();

			DefineApkTestPhaseProperties ();
			AddApkTests ();
			AddHostUnitTests ();
		}

		void AddHostUnitTests ()
		{
			AddTest (
				new TestHostUnit (
					"Build Tests",
					Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "bin", $"Test{configuration}", "net472", "Xamarin.Android.Build.Tests.dll")
				)
			);
		}

		//
		// Properties used when generating the RunApkTests target
		//
		void DefineApkTestPhaseProperties ()
		{
			var phaseProperties = new ApkTestPhaseProperties ();
			apkTestPhaseProperties [ApkTestPhase.AOT] = phaseProperties;
			phaseProperties.Exec.Add ("AotAssemblies=true");
			phaseProperties.MSBuild.AddRange (phaseProperties.Exec);

			phaseProperties = new ApkTestPhaseProperties ();
			apkTestPhaseProperties [ApkTestPhase.ProfiledAOT] = phaseProperties;
			phaseProperties.Exec.Add ("AndroidEnableProfiledAot=True");
			phaseProperties.MSBuild.AddRange (phaseProperties.Exec);
			phaseProperties.MSBuild.Add ("AotAssemblies=True");

			phaseProperties = new ApkTestPhaseProperties ();
			apkTestPhaseProperties [ApkTestPhase.MonoBundle] = phaseProperties;
			phaseProperties.Exec.Add ("BundleAssemblies=True");
			phaseProperties.Exec.Add ("EmbedAssembliesIntoApk=True");
			phaseProperties.MSBuild.Add ("BundleAssemblies=True");
		}

		void AddApkTests ()
		{
			CreateTestAPK (
				androidPackageName: "Mono.Android_Tests",
				name: "Mono.Android",
				projectFilePath: Path.Combine ("src", "Mono.Android", "Test", "Mono.Android-Tests.csproj"),
				(TestAPK t) => SetCommonMonoAndroidProperties (t)
			);

			CreateTestAPK (
				androidPackageName: "Mono.Android_Tests",
				name: "AOT: Mono.Android",
				projectFilePath: Path.Combine ("src", "Mono.Android", "Test", "Mono.Android-Tests.csproj"),
				testFlavor: APKTestFlavor.AOT,
				(TestAPK t) => {
					SetCommonMonoAndroidProperties (t);
					t.SkipProjitemsGeneration = true;
				}
			);

			CreateTestAPK (
				androidPackageName: "Mono.Android_Tests",
				name: "MonoBundle: Mono.Android",
				projectFilePath: Path.Combine ("src", "Mono.Android", "Test", "Mono.Android-Tests.csproj"),
				testFlavor: APKTestFlavor.MonoBundle,
				(TestAPK t) => {
					SetCommonMonoAndroidProperties (t);
					t.SkipProjitemsGeneration = true;
				}
			);

			CreateTestAPK (
				androidPackageName: "Mono.Android_TestsMultiDex",
				name: "MultiDex: Mono.Android",
				projectFilePath: Path.Combine ("tests", "Runtime-MultiDex", "Mono.Android-TestsMultiDex.csproj"),
				(TestAPK t) => SetCommonMonoAndroidProperties (t)
			);

			CreateTestAPK (
				androidPackageName: "Mono.Android_TestsAppBundle",
				name: "AppBundle: Mono.Android",
				projectFilePath: Path.Combine ("tests", "Runtime-AppBundle", "Mono.Android-TestsAppBundle.csproj"),
				packageExtension: "aab",
				testFlavor: APKTestFlavor.AndroidApplicationBundle,
				(TestAPK t) => SetCommonMonoAndroidProperties (t, addApkSizes: false)
			);

			CreateTestAPK (
				androidPackageName: "Xamarin.Android.Locale_Tests",
				name: "Xamarin.Android Locale",
				projectFilePath: Path.Combine ("tests", "locales", "Xamarin.Android.Locale-Tests", "Xamarin.Android.Locale-Tests.csproj"),
				SetCommonLocaleTestProperties
			);

			CreateTestAPK (
				androidPackageName: "Xamarin.Android.Locale_Tests",
				name: "AOT: Xamarin.Android Locale",
				projectFilePath: Path.Combine ("tests", "locales", "Xamarin.Android.Locale-Tests", "Xamarin.Android.Locale-Tests.csproj"),
				testFlavor: APKTestFlavor.AOT,
				(TestAPK t) => {
					SetCommonLocaleTestProperties (t);
					t.SkipProjitemsGeneration = true;
				}
			);

			CreateTestAPK (
				androidPackageName: "Xamarin.Forms_Performance_Integration",
				name: "Xamarin.Forms Performance Integration",
				projectFilePath: Path.Combine ("tests", "Xamarin.Forms-Performance-Integration", "Droid", "Xamarin.Forms.Performance.Integration.Droid.csproj"),
				SetCommonXFIntegrationTestProperties
			);

			CreateTestAPK (
				androidPackageName: "Xamarin.Forms_Performance_Integration",
				name: "AOT: Xamarin.Forms Performance Integration",
				projectFilePath: Path.Combine ("tests", "Xamarin.Forms-Performance-Integration", "Droid", "Xamarin.Forms.Performance.Integration.Droid.csproj"),
				testFlavor: APKTestFlavor.AOT,
				(TestAPK t) => {
					SetCommonXFIntegrationTestProperties (t);
					t.SkipProjitemsGeneration = true;
				}
			);

			CreateTestAPK (
				androidPackageName: "Xamarin.Forms_Performance_Integration",
				name: "MonoBundle: Xamarin.Forms Performance Integration",
				projectFilePath: Path.Combine ("tests", "Xamarin.Forms-Performance-Integration", "Droid", "Xamarin.Forms.Performance.Integration.Droid.csproj"),
				testFlavor: APKTestFlavor.MonoBundle,
				(TestAPK t) => {
					SetCommonXFIntegrationTestProperties (t);
					t.SkipProjitemsGeneration = true;
				}
			);

			CreateTestAPK (
				androidPackageName: "Xamarin.Android.EmbeddedDSO_Test",
				name: "Embedded DSO",
				projectFilePath: Path.Combine ("tests", "EmbeddedDSOs", "EmbeddedDSO", "EmbeddedDSO.csproj"),
				(TestAPK t) => {
					SetStandardTimingDefinitionProperties (t);
					SetStandardStoragePermissions (t);
					t.AndroidPermissions.Add ("READ_PHONE_STATE");

					t.Instrumentations.Add (new TestAndroidInstrumentation ("xamarin.android.embeddeddso_test.NUnitInstrumentation") {
							ResultsPath = Path.Combine (outputPath, $"TestResult-{t.AndroidPackageName}.nunit.xml"),
						}
					);
				}
			);

			CreateTestAPK (
				androidPackageName: "Xamarin.Android.Bcl_Tests",
				name: "BCL",
				projectFilePath: Path.Combine ("tests", "BCL-Tests", "Xamarin.Android.Bcl-Tests", "Xamarin.Android.Bcl-Tests.csproj"),
				(TestAPK t) => {
					SetStandardStoragePermissions (t);
					t.AndroidPermissions.Add ("READ_PHONE_STATE");

					t.Instrumentations.Add (new TestAndroidInstrumentation ("xamarin.android.bcltests.XUnitInstrumentation") {
							ResultsPath = Path.Combine (outputPath, $"TestResult-{t.AndroidPackageName}.xunit.xml"),
							LogcatFilenameDistincion = ".xunit",
							TimeoutInMS = 4500000,
						}
					);

					t.Instrumentations.Add (new TestAndroidInstrumentation ("xamarin.android.bcltests.NUnitInstrumentation") {
							ResultsPath = Path.Combine (outputPath, $"TestResult-{t.AndroidPackageName}.nunit.xml"),
							LogcatFilenameDistincion = ".nunit",
							TimeoutInMS = 3600000,
						}
					);

					t.AddExtraXMLWriter (APKTestMSBuildContext.RunApkTestsImportProjitems, (XmlWriter xw, TestAPK test) => {
						xw.WriteAttributeString ("Condition", " '$(AotAssemblies)' != 'True' ");
					});
				}
			);

			CreateTestAPK (
				androidPackageName: "Xamarin.Android.JcwGen_Tests",
				name: "JcwGen",
				projectFilePath: Path.Combine ("tests", "CodeGen-Binding", "Xamarin.Android.JcwGen-Tests", "Xamarin.Android.JcwGen-Tests.csproj"),
				(TestAPK t) => {
					SetStandardTimingDefinitionProperties (t);
					SetStandardStoragePermissions (t);

					t.Instrumentations.Add (new TestAndroidInstrumentation ("xamarin.android.jcwgentests.TestInstrumentation") {
							ResultsPath = Path.Combine (outputPath, $"TestResult-{t.AndroidPackageName}.xml"),
						}
					);

					t.AddExtraXMLWriter (APKTestMSBuildContext.RunApkTestsImportProjitems, (XmlWriter xw, TestAPK test) => {
						xw.WriteAttributeString ("Condition", " '$(AotAssemblies)' != 'True' ");
					});
				}
			);

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

				test.Instrumentations.Add (new TestAndroidInstrumentation ("xamarin.android.runtimetests.TestInstrumentation") {
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

		void CreateTestAPK (string androidPackageName, string name, string projectFilePath, Action<TestAPK>? configurator = null)
		{
			CreateTestAPK (androidPackageName, name, projectFilePath, "apk", APKTestFlavor.Plain, configurator);
		}

		void CreateTestAPK (string androidPackageName, string name, string projectFilePath, APKTestFlavor testFlavor, Action<TestAPK>? configurator = null)
		{
			CreateTestAPK (androidPackageName, name, projectFilePath, "apk", testFlavor, configurator);
		}

		void CreateTestAPK (string androidPackageName, string name, string projectFilePath, string packageExtension, APKTestFlavor testFlavor = APKTestFlavor.Plain, Action<TestAPK>? configurator = null)
		{
			if (packageExtension.Length == 0) {
				throw new ArgumentException ("must not be empty", nameof (packageExtension));
			}

			var test = new TestAPK (
				Path.Combine (Configurables.Paths.TestBinDir, $"{androidPackageName}-Signed.{packageExtension}"),
				androidPackageName,
				name,
				projectFilePath,
				testFlavor
			);

			if (configurator != null) {
				configurator (test);
			}

			AddTest (test);
		}

		void AddTest (XATest test)
		{
			if (AllTests.ContainsKey (test.Name)) {
				throw new InvalidOperationException ($"Duplicate test name '{test.Name}'");
			}

			AllTests.Add (test.Name, test);
		}

		public List<string> GetApkTestPhasePropertiesExec (ApkTestPhase phase)
		{
			if (!apkTestPhaseProperties.TryGetValue (phase, out ApkTestPhaseProperties properties)) {
				return new List<string> ();
			}

			return properties.Exec;
		}

		public List<string> GetApkTestPhasePropertiesMSBuild (ApkTestPhase phase)
		{
			if (!apkTestPhaseProperties.TryGetValue (phase, out ApkTestPhaseProperties properties)) {
				return new List<string> ();
			}

			return properties.MSBuild;
		}
	}
}
