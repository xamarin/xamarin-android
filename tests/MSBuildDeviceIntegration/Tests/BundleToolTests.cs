using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xamarin.Android.Tasks;
using Xamarin.ProjectTools;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Build.Tests
{
	[TestFixture]
	[TestFixtureSource(nameof(FixtureArgs))]
	[Category ("XamarinBuildDownload")]
	public class BundleToolTests : DeviceTest
	{
		static readonly object[] FixtureArgs = {
			new object[] { false },
			new object[] { true },
		};

		static readonly string [] Abis = new [] { "armeabi-v7a", "arm64-v8a", "x86", "x86_64" };
		XamarinAndroidLibraryProject lib;
		XamarinAndroidApplicationProject app;
		ProjectBuilder libBuilder, appBuilder;
		string intermediate;
		string bin;
		bool usesAssemblyBlobs;

		// Disable split by language
		const string BuildConfig = @"{
	""compression"": {
		""uncompressedGlob"": [
			""assets/*.data""
		]
	},
	""optimizations"": {
		""splits_config"": {
			""split_dimension"": [
				{
					""value"": ""LANGUAGE"",
					""negate"": true
				}
			]
		}
	}
}";

		public BundleToolTests (bool usesAssemblyBlobs)
		{
			this.usesAssemblyBlobs = usesAssemblyBlobs;
		}

		[OneTimeSetUp]
		public void OneTimeSetUp ()
		{
			var path = Path.Combine ("temp", TestName);
			lib = new XamarinAndroidLibraryProject {
				ProjectName = "Localization",
				IsRelease = true,
				OtherBuildItems = {
					new BuildItem ("EmbeddedResource", "Foo.resx") {
						TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancel</value></data>")
					},
					new BuildItem ("EmbeddedResource", "Foo.es.resx") {
						TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancelar</value></data>")
					}
				}
			};

			lib.SetProperty ("AndroidUseAssemblyStore", usesAssemblyBlobs.ToString ());

			var bytes = new byte [1024];
			app = new XamarinFormsMapsApplicationProject {
				IsRelease = true,
				AotAssemblies = false, // Release defaults to Profiled AOT for .NET 6
				PackageName = "com.xamarin.bundletooltests",
			};
			app.OtherBuildItems.Add (new AndroidItem.AndroidAsset ("foo.bar") {
				BinaryContent = () => bytes,
			});
			app.OtherBuildItems.Add (new AndroidItem.AndroidAsset ("foo.wav") {
				BinaryContent = () => bytes,
			});
			app.OtherBuildItems.Add (new AndroidItem.AndroidAsset ("foo.data") {
				BinaryContent = () => bytes,
			});
			app.OtherBuildItems.Add (new BuildItem ("None", "buildConfig.json") {
				TextContent = () => BuildConfig,
			});
			app.SetProperty ("AndroidStoreUncompressedFileExtensions", ".bar");
			app.References.Add (new BuildItem.ProjectReference ($"..\\{lib.ProjectName}\\{lib.ProjectName}.csproj", lib.ProjectName, lib.ProjectGuid));

			//NOTE: this is here to enable adb shell run-as
			app.AndroidManifest = app.AndroidManifest.Replace ("<application ", "<application android:debuggable=\"true\" ");
			app.SetProperty (app.ReleaseProperties, "AndroidPackageFormat", "aab");
			app.SetAndroidSupportedAbis (Abis);
			app.SetProperty ("AndroidBundleConfigurationFile", "buildConfig.json");
			app.SetProperty ("AndroidUseAssemblyStore", usesAssemblyBlobs.ToString ());

			libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName), cleanupOnDispose: true);
			Assert.IsTrue (libBuilder.Build (lib), "Library build should have succeeded.");
			appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName), cleanupOnDispose: true);
			Assert.IsTrue (appBuilder.Build (app), "App build should have succeeded.");

			var projectDir = Path.Combine (Root, appBuilder.ProjectDirectory);
			intermediate = Path.Combine (projectDir, app.IntermediateOutputPath);
			bin = Path.Combine (projectDir, app.OutputPath);
		}

		[TearDown]
		public void TearDown ()
		{
			var status = TestContext.CurrentContext.Result.Outcome.Status;
			if (status == NUnit.Framework.Interfaces.TestStatus.Failed) {
				if (libBuilder != null)
					libBuilder.CleanupOnDispose = false;
				if (appBuilder != null)
					appBuilder.CleanupOnDispose = false;
			}
		}

		[OneTimeTearDown]
		public void OneTimeTearDown ()
		{
			libBuilder?.Dispose ();
			appBuilder?.Dispose ();
		}

		string [] ListArchiveContents (string archive, bool usesAssembliesBlob)
		{
			var helper = new ArchiveAssemblyHelper (archive, usesAssembliesBlob);
			List<string> entries = helper.ListArchiveContents ();
			entries.Sort ();
			return entries.ToArray ();
		}

		[Test]
		public void BaseZip ()
		{
			var baseZip = Path.Combine (intermediate, "android", "bin", "base.zip");
			var contents = ListArchiveContents (baseZip, usesAssemblyBlobs);
			var expectedFiles = new List<string> {
				"dex/classes.dex",
				"manifest/AndroidManifest.xml",
				"res/drawable-hdpi-v4/icon.png",
				"res/drawable-mdpi-v4/icon.png",
				"res/drawable-xhdpi-v4/icon.png",
				"res/drawable-xxhdpi-v4/icon.png",
				"res/drawable-xxxhdpi-v4/icon.png",
				"res/layout/main.xml",
				"resources.pb",
			};

			string blobEntryPrefix = ArchiveAssemblyHelper.DefaultAssemblyStoreEntryPrefix;
			if (usesAssemblyBlobs) {
				expectedFiles.Add ($"{blobEntryPrefix}Java.Interop.dll");
				expectedFiles.Add ($"{blobEntryPrefix}Mono.Android.dll");
				expectedFiles.Add ($"{blobEntryPrefix}Localization.dll");
				expectedFiles.Add ($"{blobEntryPrefix}es/Localization.resources.dll");
				expectedFiles.Add ($"{blobEntryPrefix}UnnamedProject.dll");
			} else {
				expectedFiles.Add ("root/assemblies/Java.Interop.dll");
				expectedFiles.Add ("root/assemblies/Mono.Android.dll");
				expectedFiles.Add ("root/assemblies/Localization.dll");
				expectedFiles.Add ("root/assemblies/es/Localization.resources.dll");
				expectedFiles.Add ("root/assemblies/UnnamedProject.dll");
			}

			//These are random files from Google Play Services .aar files
			expectedFiles.Add ("root/play-services-base.properties");
			expectedFiles.Add ("root/play-services-basement.properties");
			expectedFiles.Add ("root/play-services-maps.properties");
			expectedFiles.Add ("root/play-services-tasks.properties");

			foreach (var abi in Abis) {
				expectedFiles.Add ($"lib/{abi}/libmonodroid.so");
				expectedFiles.Add ($"lib/{abi}/libmonosgen-2.0.so");
				expectedFiles.Add ($"lib/{abi}/libxamarin-app.so");
				if (usesAssemblyBlobs) {
					expectedFiles.Add ($"{blobEntryPrefix}System.Private.CoreLib.dll");
				} else {
					expectedFiles.Add ($"root/assemblies/{abi}/System.Private.CoreLib.dll");
				}
				expectedFiles.Add ($"lib/{abi}/libSystem.IO.Compression.Native.so");
				expectedFiles.Add ($"lib/{abi}/libSystem.Native.so");
			}
			foreach (var expected in expectedFiles) {
				CollectionAssert.Contains (contents, expected, $"`{baseZip}` did not contain `{expected}`");
			}
		}

		[Test]
		public void AppBundle ()
		{
			var aab = Path.Combine (intermediate, "android", "bin", $"{app.PackageName}.aab");
			FileAssert.Exists (aab);
			var contents = ListArchiveContents (aab, usesAssemblyBlobs);
			var expectedFiles = new List<string> {
				"base/dex/classes.dex",
				"base/manifest/AndroidManifest.xml",
				"base/native.pb",
				"base/res/drawable-hdpi-v4/icon.png",
				"base/res/drawable-mdpi-v4/icon.png",
				"base/res/drawable-xhdpi-v4/icon.png",
				"base/res/drawable-xxhdpi-v4/icon.png",
				"base/res/drawable-xxxhdpi-v4/icon.png",
				"base/res/layout/main.xml",
				"base/resources.pb",
				"BundleConfig.pb",
			};

			string blobEntryPrefix = ArchiveAssemblyHelper.DefaultAssemblyStoreEntryPrefix;
			if (usesAssemblyBlobs) {
				expectedFiles.Add ($"{blobEntryPrefix}Java.Interop.dll");
				expectedFiles.Add ($"{blobEntryPrefix}Mono.Android.dll");
				expectedFiles.Add ($"{blobEntryPrefix}Localization.dll");
				expectedFiles.Add ($"{blobEntryPrefix}es/Localization.resources.dll");
				expectedFiles.Add ($"{blobEntryPrefix}UnnamedProject.dll");
			} else {
				expectedFiles.Add ("base/root/assemblies/Java.Interop.dll");
				expectedFiles.Add ("base/root/assemblies/Mono.Android.dll");
				expectedFiles.Add ("base/root/assemblies/Localization.dll");
				expectedFiles.Add ("base/root/assemblies/es/Localization.resources.dll");
				expectedFiles.Add ("base/root/assemblies/UnnamedProject.dll");
			}

			//These are random files from Google Play Services .aar files
			expectedFiles.Add ("base/root/play-services-base.properties");
			expectedFiles.Add ("base/root/play-services-basement.properties");
			expectedFiles.Add ("base/root/play-services-maps.properties");
			expectedFiles.Add ("base/root/play-services-tasks.properties");

			foreach (var abi in Abis) {
				expectedFiles.Add ($"base/lib/{abi}/libmonodroid.so");
				expectedFiles.Add ($"base/lib/{abi}/libmonosgen-2.0.so");
				expectedFiles.Add ($"base/lib/{abi}/libxamarin-app.so");
				if (usesAssemblyBlobs) {
					expectedFiles.Add ($"{blobEntryPrefix}System.Private.CoreLib.dll");
				} else {
					expectedFiles.Add ($"base/root/assemblies/{abi}/System.Private.CoreLib.dll");
				}
				expectedFiles.Add ($"base/lib/{abi}/libSystem.IO.Compression.Native.so");
				expectedFiles.Add ($"base/lib/{abi}/libSystem.Native.so");
			}
			foreach (var expected in expectedFiles) {
				CollectionAssert.Contains (contents, expected, $"`{aab}` did not contain `{expected}`");
			}
		}

		[Test]
		public void AppBundleSigned ()
		{
			var aab = Path.Combine (bin, $"{app.PackageName}-Signed.aab");
			FileAssert.Exists (aab);
			var contents = ListArchiveContents (aab, usesAssembliesBlob: false);
			Assert.IsTrue (StringAssertEx.ContainsText (contents, "META-INF/MANIFEST.MF"), $"{aab} is not signed!");
		}

		[Test, Category ("UsesDevice")]
		public void ApkSet ()
		{
			appBuilder.BuildLogFile = "install.log";
			Assert.IsTrue (appBuilder.RunTarget (app, "Install"), "App should have installed.");

			var aab = Path.Combine (intermediate, "android", "bin", $"{app.PackageName}.apks");
			FileAssert.Exists (aab);
			// Expecting: splits/base-arm64_v8a.apk, splits/base-master.apk, splits/base-xxxhdpi.apk
			// This are split up based on: abi, base, and dpi
			var contents = ListArchiveContents (aab, usesAssembliesBlob: false).Where (a => a.EndsWith (".apk", StringComparison.OrdinalIgnoreCase)).ToArray ();
			Assert.AreEqual (3, contents.Length, "Expecting three APKs!");

			// Language split has been removed by the bundle configuration file, and therefore shouldn't be present
			var languageSplitContent = ListArchiveContents (aab, usesAssemblyBlobs).Where (a => a.EndsWith ("-en.apk", StringComparison.OrdinalIgnoreCase)).ToArray ();
			Assert.AreEqual (0, languageSplitContent.Length, "Found language split apk in bundle, but disabled by bundle configuration file!");

			using (var stream = new MemoryStream ())
			using (var apkSet = ZipArchive.Open (aab, FileMode.Open)) {
				// We have a zip inside a zip
				var baseMaster = apkSet.ReadEntry ("splits/base-master.apk");
				baseMaster.Extract (stream);

				stream.Position = 0;
				var uncompressed = new List<string> {
					".bar",
					".wav",
					".data",
				};

				if (usesAssemblyBlobs) {
					uncompressed.Add (".blob");
				} else {
					uncompressed.Add (".dll");
				}
				using (var baseApk = ZipArchive.Open (stream)) {
					foreach (var file in baseApk) {
						foreach (var ext in uncompressed) {
							if (file.FullName.EndsWith (ext, StringComparison.OrdinalIgnoreCase)) {
								Assert.AreEqual (CompressionMethod.Store, file.CompressionMethod, $"{file.FullName} should be uncompressed!");
							}
						}
					}
				}
			}
		}

		[Test]
		public void BuildAppBundleCommand ()
		{
			var task = new BuildAppBundle {
				BaseZip = "base.zip",
				Output = "foo.aab",
			};
			string cmd = task.GetCommandLineBuilder ().ToString ();
			Assert.AreEqual ($"build-bundle --modules base.zip --output foo.aab", cmd);
		}

		[Test]
		public void BuildApkSetCommand ()
		{
			var task = new BuildApkSet {
				AppBundle = "foo.aab",
				Output = "foo.apks",
				KeyStore = "foo.keystore",
				KeyAlias = "alias",
				KeyPass = "keypass",
				StorePass = "storepass",
				Aapt2ToolPath = Path.Combine ("aapt", "with spaces"),
				Aapt2ToolExe = "aapt2",
				AdbToolPath = Path.Combine ("adb", "with spaces"),
				AdbToolExe = "adb",
				AdbTarget = "-s emulator-5554"
			};
			string aapt2 = Path.Combine (task.Aapt2ToolPath, task.Aapt2ToolExe);
			string adb = Path.Combine (task.AdbToolPath, task.AdbToolExe);
			string cmd = task.GetCommandLineBuilder ().ToString ();
			Assert.AreEqual ($"build-apks --connected-device --mode default --adb \"{adb}\" --device-id emulator-5554 --bundle foo.aab --output foo.apks --aapt2 \"{aapt2}\" --ks foo.keystore --ks-key-alias alias --key-pass pass:keypass --ks-pass pass:storepass", cmd);
		}

		[Test]
		public void InstallApkSetCommand ()
		{
			var task = new InstallApkSet {
				ApkSet = "foo.apks",
				AdbToolPath = Path.Combine ("path", "with spaces"),
				AdbToolExe = "adb",
				AdbTarget = "-s emulator-5554"
			};
			string adb = Path.Combine (task.AdbToolPath, task.AdbToolExe);
			string cmd = task.GetCommandLineBuilder ().ToString ();
			Assert.AreEqual ($"install-apks --apks foo.apks --adb \"{adb}\" --device-id emulator-5554 --allow-downgrade", cmd);
		}
	}
}
