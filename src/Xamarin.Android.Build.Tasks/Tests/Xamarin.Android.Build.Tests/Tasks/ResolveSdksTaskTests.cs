using System;
using System.Collections.Generic;
using NUnit.Framework;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Xamarin.Android.Tasks;
using Xamarin.ProjectTools;
using ResolveAndroidTooling = Xamarin.Android.Tasks.Legacy.ResolveAndroidTooling;
using ValidateJavaVersion = Xamarin.Android.Tasks.Legacy.ValidateJavaVersion;

namespace Xamarin.Android.Build.Tests {

	[TestFixture]
	[Category ("Node-2")]
	[NonParallelizable] // NOTE: This test was hanging without this
	public class ResolveSdksTaskTests : BaseTest {
#pragma warning disable 414

		static ApiInfo [] apiInfoSelection = new ApiInfo [] {
			new ApiInfo () { Id = "25", Level = 25, Name = "Nougat", FrameworkVersion = "v7.1",  Stable = true },
			new ApiInfo () { Id = "26", Level = 26, Name = "Oreo", FrameworkVersion = "v8.0",  Stable = true  },
			new ApiInfo () { Id = "27", Level = 27, Name = "Oreo", FrameworkVersion = "v8.1",  Stable = true  },
			new ApiInfo () { Id = "28", Level = 28, Name = "Pie",    FrameworkVersion = "v9.0", Stable = false },
			new ApiInfo () { Id = "29", Level = 29, Name = "Android10", FrameworkVersion = "v10.0", Stable = false },
			new ApiInfo () { Id = "30", Level = 30, Name = "Android11", FrameworkVersion = "v11.0", Stable = false },
			new ApiInfo () { Id = "S",  Level = 31, Name = "S",         FrameworkVersion = "v11.0.99", Stable = false },
			new ApiInfo () { Id = "Z",  Level = 127, Name = "Z",    FrameworkVersion = "v108.1.99", Stable = false },
		};

		// via Xamarin.Android.Common.props
		const   string  MinimumSupportedJavaVersion     = "1.6.0";
		const   string  LatestSupportedJavaVersion      = "11.0.99";

		static object [] UseLatestAndroidSdkTestCases = new object [] {
			new object[] {
				/* buildtools */   "26.0.3",
				/* jdk */ "1.8.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ true,
				/* targetFrameworkVersion */ "v9.0",
				/* expectedTaskResult */ true,
				/* expectedTargetFramework */ "v9.0",
				/* expectedError */ "",
				/* expectedErrorMessage */ "",
			},
			new object[] {
				/* buildtools */   "26.0.3",
				/* jdk */ "1.8.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ true,
				/* targetFrameworkVersion */ "v8.0",
				/* expectedTaskResult */ true,
				/* expectedTargetFramework */ "v8.1",
				/* expectedError */ "",
				/* expectedErrorMessage */ "",
			},
			new object[] {
				/* buildtools */   "26.0.3",
				/* jdk */ "1.8.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ true,
				/* targetFrameworkVersion */ "v8.1",
				/* expectedTaskResult */ true,
				/* expectedTargetFramework */ "v8.1",
				/* expectedError */ "",
				/* expectedErrorMessage */ "",
			},
			new object[] {
				/* buildtools */   "26.0.3",
				/* jdk */ "1.8.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ true,
				/* targetFrameworkVersion */ "v6.0",
				/* expectedTaskResult */ true,
				/* expectedTargetFramework */ "v8.1",
				/* expectedError */ "",
				/* expectedErrorMessage */ "",
			},
			new object[] {
				/* buildtools */   "26.0.3",
				/* jdk */ "1.8.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ true,
				/* targetFrameworkVersion */ null,
				/* expectedTaskResult */ true,
				/* expectedTargetFramework */ "v8.1",
				/* expectedError */ "",
				/* expectedErrorMessage */ "",
			},
			new object[] {
				/* buildtools */   "26.0.3",
				/* jdk */ "1.8.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ true,
				/* targetFrameworkVersion */ "v7.1",
				/* expectedTaskResult */ true,
				/* expectedTargetFramework */ "v8.1",
				/* expectedError */ "",
				/* expectedErrorMessage */ "",
			},
			new object[] {
				/* buildtools */   "26.0.3",
				/* jdk */ "1.8.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ false,
				/* targetFrameworkVersion */ "v9.0",
				/* expectedTaskResult */ true,
				/* expectedTargetFramework */ "v9.0",
				/* expectedError */ "",
				/* expectedErrorMessage */ "",
			},
			new object[] {
				/* buildtools */   "26.0.3",
				/* jdk */ "1.8.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ false,
				/* targetFrameworkVersion */ "v8.1",
				/* expectedTaskResult */ true,
				/* expectedTargetFramework */ "v8.1",
				/* expectedError */ "",
				/* expectedErrorMessage */ "",
			},
			new object[] {
				/* buildtools */   "26.0.3",
				/* jdk */ "1.8.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ false,
				/* targetFrameworkVersion */ "v8.0",
				/* expectedTaskResult */ true,
				/* expectedTargetFramework */ "v8.0",
				/* expectedError */ "",
				/* expectedErrorMessage */ "",
			},
			new object[] {
				/* buildtools */   "26.0.3",
				/* jdk */ "1.8.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ false,
				/* targetFrameworkVersion */ null,
				/* expectedTaskResult */ true,
				/* expectedTargetFramework */ "v8.1",
				/* expectedError */ "",
				/* expectedErrorMessage */ "",
			},
			new object[] {
				/* buildtools */   "26.0.3",
				/* jdk */ "1.8.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ false,
				/* targetFrameworkVersion */ "v6.0",
				/* expectedTaskResult */ false,
				/* expectedTargetFramework */ "v6.0",
				/* expectedError */ "XA0001",
				/* expectedErrorMessage */ "Unsupported or invalid $(TargetFrameworkVersion) value of 'v6.0'. Please update your Project Options.",
			},
			new object[] {
				/* buildtools */   "30.0.0",
				/* jdk */ "11.0",
				/* apis*/ apiInfoSelection,
				/* useLatestAndroidSdk */ false,
				/* targetFrameworkVersion */ "v11.0.99",
				/* expectedTaskResult */ true,
				/* expectedTargetFramework */ "v11.0.99",
				/* expectedError */ "",
				/* expectedErrorMessage */ "",
			},
		};
		#pragma warning restore 414
		[Test]
		[TestCaseSource(nameof(UseLatestAndroidSdkTestCases))]
		public void UseLatestAndroidSdk (string buildtools, string jdk, ApiInfo[] apis, bool useLatestAndroidSdk, string targetFrameworkVersion, bool expectedTaskResult, string expectedTargetFramework, string expectedError = "", string expectedErrorMessage = "")
		{
			var path = Path.Combine ("temp", "UseLatestAndroidSdk_" + Guid.NewGuid ());
			var androidSdkPath = CreateFauxAndroidSdkDirectory (Path.Combine (path, "android-sdk"), buildtools, apis);
			var androidNdkPath = CreateFauxAndroidNdkDirectory (Path.Combine (path, "android-ndk"));
			string javaExe = string.Empty;
			string javacExe;
			var javaPath = CreateFauxJavaSdkDirectory (Path.Combine (path, "jdk"), jdk, out javaExe, out javacExe);
			var referencePath = CreateFauxReferencesDirectory (Path.Combine (path, "references"), apis);
			var errors = new List<BuildErrorEventArgs> ();
			IBuildEngine engine = new MockBuildEngine (TestContext.Out, errors);
			var resolveSdks = new ResolveSdks {
				BuildEngine = engine,
				AndroidSdkPath = androidSdkPath,
				AndroidNdkPath = androidNdkPath,
				JavaSdkPath = javaPath,
				MinimumSupportedJavaVersion = MinimumSupportedJavaVersion,
				LatestSupportedJavaVersion  = LatestSupportedJavaVersion,
				ReferenceAssemblyPaths = new [] {
					Path.Combine (referencePath, "MonoAndroid"),
				},
			};
			var validateJavaVersion = new ValidateJavaVersion {
				BuildEngine = engine,
				TargetFrameworkVersion = targetFrameworkVersion,
				AndroidSdkBuildToolsVersion = buildtools,
				JavaSdkPath = javaPath,
				JavaToolExe = javaExe,
				JavacToolExe = javacExe,
				LatestSupportedJavaVersion = LatestSupportedJavaVersion,
				MinimumSupportedJavaVersion = "1.7.0",
			};
			var androidTooling = new ResolveAndroidTooling {
				BuildEngine = engine,
				AndroidSdkPath = androidSdkPath,
				TargetFrameworkVersion = targetFrameworkVersion,
				AndroidSdkBuildToolsVersion = buildtools,
				UseLatestAndroidPlatformSdk = useLatestAndroidSdk,
				AotAssemblies = false,
				SequencePointsMode = "None",
				AndroidApplication = true,
			};
			Assert.AreEqual (expectedTaskResult, resolveSdks.Execute () && validateJavaVersion.Execute () && androidTooling.Execute (), $"Tasks should have {(expectedTaskResult ? "succeeded" : "failed" )}.");
			Assert.AreEqual (expectedTargetFramework, androidTooling.TargetFrameworkVersion, $"TargetFrameworkVersion should be {expectedTargetFramework} but was {androidTooling.TargetFrameworkVersion}");
			if (!string.IsNullOrWhiteSpace (expectedError)) {
				Assert.AreEqual (1, errors.Count (), "An error should have been raised.");
				Assert.AreEqual (expectedError, errors [0].Code, $"Expected error code {expectedError} but found {errors [0].Code}");
				Assert.AreEqual (expectedErrorMessage, errors [0].Message, $"Expected error code {expectedErrorMessage} but found {errors [0].Message}");
			}
			Directory.Delete (Path.Combine (Root, path), recursive: true);
		}

		[Test, NonParallelizable]
		public void ResolveSdkTiming ()
		{
			var path = Path.Combine ("temp", TestName);
			var androidSdkPath = CreateFauxAndroidSdkDirectory (Path.Combine (path, "android-sdk"), "26.0.3");
			var androidNdkPath = CreateFauxAndroidNdkDirectory (Path.Combine (path, "android-ndk"));
			string javaExe = string.Empty;
			string javacExe;
			var javaPath = CreateFauxJavaSdkDirectory (Path.Combine (path, "jdk"), "1.8.0", out javaExe, out javacExe);
			var referencePath = CreateFauxReferencesDirectory (Path.Combine (path, "references"), new ApiInfo [] {
				new ApiInfo () { Id = "26", Level = 26, Name = "Oreo", FrameworkVersion = "v8.0", Stable = true },
				new ApiInfo () { Id = "27", Level = 27, Name = "Oreo", FrameworkVersion = "v8.1", Stable = true },
			});
			IBuildEngine engine = new MockBuildEngine (TestContext.Out);
			var resolveSdks = new ResolveSdks {
				BuildEngine = engine,
				AndroidSdkPath = androidSdkPath,
				AndroidNdkPath = androidNdkPath,
				JavaSdkPath = javaPath,
				MinimumSupportedJavaVersion = MinimumSupportedJavaVersion,
				LatestSupportedJavaVersion  = LatestSupportedJavaVersion,
				ReferenceAssemblyPaths = new [] {
					Path.Combine (referencePath, "MonoAndroid"),
				},
			};
			var validateJavaVersion = new ValidateJavaVersion {
				BuildEngine = engine,
				TargetFrameworkVersion = "v8.0",
				AndroidSdkBuildToolsVersion = "26.0.3",
				JavaSdkPath = javaPath,
				JavaToolExe = javaExe,
				JavacToolExe = javacExe,
				LatestSupportedJavaVersion = "1.8.0",
				MinimumSupportedJavaVersion = "1.7.0",
			};
			var androidTooling = new ResolveAndroidTooling {
				BuildEngine = engine,
				AndroidSdkPath = androidSdkPath,
				TargetFrameworkVersion = "v8.0",
				AndroidSdkBuildToolsVersion = "26.0.3",
				UseLatestAndroidPlatformSdk = false,
				AotAssemblies = false,
				SequencePointsMode = "None",
				AndroidApplication = true,
			};
			var start = DateTime.UtcNow;
			Assert.IsTrue (resolveSdks.Execute (), "ResolveSdks should succeed!");
			Assert.IsTrue (validateJavaVersion.Execute (), "ValidateJavaVersion should succeed!");
			Assert.IsTrue (androidTooling.Execute (), "ResolveAndroidTooling should succeed!");
			var executionTime = DateTime.UtcNow - start;
			Assert.LessOrEqual (executionTime, TimeSpan.FromSeconds(2), "Task should not take more than 2 seconds to run.");
			Assert.AreEqual (androidTooling.AndroidApiLevel, "26", "AndroidApiLevel should be 26");
			Assert.AreEqual (androidTooling.TargetFrameworkVersion, "v8.0", "TargetFrameworkVersion should be v8.0");
			Assert.AreEqual (androidTooling.AndroidApiLevelName, "26", "AndroidApiLevelName should be 26");
			Assert.NotNull (resolveSdks.ReferenceAssemblyPaths, "ReferenceAssemblyPaths should not be null.");
			Assert.AreEqual (resolveSdks.ReferenceAssemblyPaths.Length, 1, "ReferenceAssemblyPaths should have 1 entry.");
			Assert.AreEqual (resolveSdks.ReferenceAssemblyPaths[0], Path.Combine (referencePath, "MonoAndroid"), $"ReferenceAssemblyPaths should be {Path.Combine (referencePath, "MonoAndroid")}.");
			var expected = Path.GetDirectoryName (GetType ().Assembly.Location);
			Assert.AreEqual (resolveSdks.MonoAndroidToolsPath, expected, $"MonoAndroidToolsPath should be {expected}");
			expected += Path.DirectorySeparatorChar;
			if (resolveSdks.MonoAndroidBinPath != expected) {
				//For non-Windows platforms, remove a directory such as "Darwin", MonoAndroidBinPath also has a trailing /
				var binPath = Path.GetDirectoryName (Path.GetDirectoryName (resolveSdks.MonoAndroidBinPath)) + Path.DirectorySeparatorChar;
				Assert.AreEqual (binPath, expected, $"MonoAndroidBinPath should be {expected}");
			}
			Assert.AreEqual (resolveSdks.AndroidSdkPath, androidSdkPath, $"AndroidSdkPath should be {androidSdkPath}");
			Assert.AreEqual (resolveSdks.AndroidNdkPath, androidNdkPath, $"AndroidNdkPath should be {androidNdkPath}");
			Assert.AreEqual (resolveSdks.JavaSdkPath, javaPath, $"JavaSdkPath should be {javaPath}");
			expected = Path.Combine (androidSdkPath, "build-tools", "26.0.3");
			Assert.AreEqual (androidTooling.AndroidSdkBuildToolsPath, expected, $"AndroidSdkBuildToolsPath should be {expected}");
			Assert.AreEqual (androidTooling.AndroidSdkBuildToolsBinPath, expected, "AndroidSdkBuildToolsBinPath should be {expected}");
			Assert.AreEqual (androidTooling.ZipAlignPath, expected, "ZipAlignPath should be {expected}");
			Assert.AreEqual (androidTooling.AndroidSequencePointsMode, "None", "AndroidSequencePointsMode should be None");
			expected = Path.Combine (androidSdkPath, "tools");
			Assert.AreEqual (androidTooling.LintToolPath, expected, $"LintToolPath should be {expected}");
			Assert.AreEqual (validateJavaVersion.JdkVersion, "1.8.0", "JdkVersion should be 1.8.0");
			Assert.AreEqual (validateJavaVersion.MinimumRequiredJdkVersion, "1.8", "MinimumRequiredJdkVersion should be 1.8");
			Directory.Delete (Path.Combine (Root, path), recursive: true);
		}

		static object [] TargetFrameworkPairingParameters = new [] {
			//We support 28, but only 27 is installed
			new object [] {
				"Older API Installed", //description
				// androidSdks
				new [] {
					new ApiInfo () { Id = "27", Level = 27, Name = "Oreo", FrameworkVersion = "v8.1", Stable = true },
				},
				// targetFrameworks
				new ApiInfo [] {
					new ApiInfo () { Id = "27", Level = 27, Name = "Oreo", FrameworkVersion = "v8.1", Stable = true },
					new ApiInfo () { Id = "28", Level = 28, Name = "P",    FrameworkVersion = "v9.0", Stable = true },
				},
				null,   //userSelected
				"27",   //androidApiLevel
				"27",   //androidApiLevelName
				"v8.1", //targetFrameworkVersion
			},
			//28 is installed but we only support 27
			new object [] {
				"Newer API Installed", //description
				// androidSdks
				new [] {
					new ApiInfo () { Id = "27", Level = 27, Name = "Oreo", FrameworkVersion = "v8.1", Stable = true },
					new ApiInfo () { Id = "28", Level = 28, Name = "P",    FrameworkVersion = "v9.0", Stable = true },
				},
				// targetFrameworks
				new ApiInfo [] {
					new ApiInfo () { Id = "27", Level = 27, Name = "Oreo", FrameworkVersion = "v8.1", Stable = true },
				},
				null,   //userSelected
				"27",   //androidApiLevel
				"27",   //androidApiLevelName
				"v8.1", //targetFrameworkVersion
			},
			//A paired downgrade to API 26
			new object [] {
				"Paired Downgrade", //description
				// androidSdks
				new [] {
					new ApiInfo () { Id = "26", Level = 26, Name = "Oreo", FrameworkVersion = "v8.0", Stable = true },
					new ApiInfo () { Id = "27", Level = 27, Name = "Oreo", FrameworkVersion = "v8.1", Stable = true },
				},
				// targetFrameworks
				new ApiInfo [] {
					new ApiInfo () { Id = "26", Level = 26, Name = "Oreo", FrameworkVersion = "v8.0", Stable = true },
					new ApiInfo () { Id = "28", Level = 28, Name = "P",    FrameworkVersion = "v9.0", Stable = true },
				},
				null,   //userSelected
				"26",   //androidApiLevel
				"26",   //androidApiLevelName
				"v8.0", //targetFrameworkVersion
			},
			//A new API level 28 is not stable yet
			new object [] {
				"New Unstable API", //description
				// androidSdks
				new [] {
					new ApiInfo () { Id = "26", Level = 26, Name = "Oreo", FrameworkVersion = "v8.0", Stable = true },
					new ApiInfo () { Id = "27", Level = 27, Name = "Oreo", FrameworkVersion = "v8.1", Stable = true },
					new ApiInfo () { Id = "28", Level = 28, Name = "P",    FrameworkVersion = "v9.0", Stable = false },
				},
				// targetFrameworks
				new ApiInfo [] {
					new ApiInfo () { Id = "26", Level = 26, Name = "Oreo", FrameworkVersion = "v8.0", Stable = true },
					new ApiInfo () { Id = "27", Level = 27, Name = "Oreo", FrameworkVersion = "v8.1", Stable = true },
					new ApiInfo () { Id = "28", Level = 28, Name = "P",    FrameworkVersion = "v9.0", Stable = false },
				},
				null,   //userSelected
				"27",   //androidApiLevel
				"27",   //androidApiLevelName
				"v8.1", //targetFrameworkVersion
			},
			//User selected a new API level 28 is not stable yet
			new object [] {
				"User Selected Unstable API", //description
				// androidSdks
				new [] {
					new ApiInfo () { Id = "26", Level = 26, Name = "Oreo", FrameworkVersion = "v8.0", Stable = true },
					new ApiInfo () { Id = "27", Level = 27, Name = "Oreo", FrameworkVersion = "v8.1", Stable = true },
					new ApiInfo () { Id = "28", Level = 28, Name = "P",    FrameworkVersion = "v9.0", Stable = false },
				},
				// targetFrameworks
				new ApiInfo [] {
					new ApiInfo () { Id = "26", Level = 26, Name = "Oreo", FrameworkVersion = "v8.0", Stable = true },
					new ApiInfo () { Id = "27", Level = 27, Name = "Oreo", FrameworkVersion = "v8.1", Stable = true },
					new ApiInfo () { Id = "28", Level = 28, Name = "P",    FrameworkVersion = "v9.0", Stable = false },
				},
				"v9.0", //userSelected
				"28",   //androidApiLevel
				"28",   //androidApiLevelName
				"v9.0", //targetFrameworkVersion
			},
		};

		[Test]
		[TestCaseSource (nameof (TargetFrameworkPairingParameters))]
		public void TargetFrameworkPairing (string description, ApiInfo[] androidSdk, ApiInfo[] targetFrameworks, string userSelected, string androidApiLevel, string androidApiLevelName, string targetFrameworkVersion)
		{
			var path = Path.Combine ("temp", $"{nameof (TargetFrameworkPairing)}_{description}");
			var androidSdkPath = CreateFauxAndroidSdkDirectory (Path.Combine (path, "android-sdk"), "26.0.3", androidSdk);
			var androidNdkPath = CreateFauxAndroidNdkDirectory (Path.Combine (path, "android-ndk"));
			string javaExe = string.Empty;
			string javacExe;
			var javaPath = CreateFauxJavaSdkDirectory (Path.Combine (path, "jdk"), "1.8.0", out javaExe, out javacExe);
			var referencePath = CreateFauxReferencesDirectory (Path.Combine (path, "references"), targetFrameworks);
			IBuildEngine engine = new MockBuildEngine (TestContext.Out);
			var resolveSdks = new ResolveSdks {
				BuildEngine = engine,
				AndroidSdkPath = androidSdkPath,
				AndroidNdkPath = androidNdkPath,
				JavaSdkPath = javaPath,
				MinimumSupportedJavaVersion = MinimumSupportedJavaVersion,
				LatestSupportedJavaVersion  = LatestSupportedJavaVersion,
				ReferenceAssemblyPaths = new [] {
					Path.Combine (referencePath, "MonoAndroid"),
				},
			};
			var androidTooling = new ResolveAndroidTooling {
				BuildEngine = engine,
				AndroidSdkPath = androidSdkPath,
				UseLatestAndroidPlatformSdk = true,
				TargetFrameworkVersion = userSelected,
				AndroidApplication = true,
			};
			Assert.IsTrue (resolveSdks.Execute (), "ResolveSdks should succeed!");
			Assert.IsTrue (androidTooling.Execute (), "ResolveAndroidTooling should succeed!");
			Assert.AreEqual (androidApiLevel, androidTooling.AndroidApiLevel, $"AndroidApiLevel should be {androidApiLevel}");
			Assert.AreEqual (androidApiLevelName, androidTooling.AndroidApiLevelName, $"AndroidApiLevelName should be {androidApiLevelName}");
			Assert.AreEqual (targetFrameworkVersion, androidTooling.TargetFrameworkVersion, $"TargetFrameworkVersion should be {targetFrameworkVersion}");
			Directory.Delete (Path.Combine (Root, path), recursive: true);
		}

		[Test]
		public void LatestTFV_OldTargetSdkVersion ()
		{
			var proj = new XamarinAndroidApplicationProject {
				UseLatestPlatformSdk = false,
			};
			proj.TargetSdkVersion = "19";
			using (var b = CreateApkBuilder ()) {
				proj.TargetFrameworkVersion = b.LatestTargetFrameworkVersion ();
				AssertTargetFrameworkVersionSupported (proj.TargetFrameworkVersion);
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}
	}
}
