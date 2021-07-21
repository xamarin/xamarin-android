using System;
using System.IO;
using NUnit.Framework;
using Xamarin.ProjectTools;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Xml.Linq;
using Xamarin.Tools.Zip;
using Xamarin.Android.Tasks;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Build.Tests
{
	[Category ("Node-3")]
	[Parallelizable (ParallelScope.Children)]
	public class PackagingTest : BaseTest
	{
#pragma warning disable 414
		static object [] ManagedSymbolsArchiveSource = new object [] {
			//           isRelease, monoSymbolArchive, packageFormat,
			new object[] { false    , false              , "apk" },
			new object[] { true     , true               , "apk" },
			new object[] { true     , false              , "apk" },
			new object[] { true     , true               , "aab" },
		};
#pragma warning restore 414

		[Test]
		[Category ("MonoSymbolicate")]
		[TestCaseSource (nameof(ManagedSymbolsArchiveSource))]
		public void CheckManagedSymbolsArchive (bool isRelease, bool monoSymbolArchive, string packageFormat)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = isRelease,
			};
			proj.SetProperty (proj.ReleaseProperties, "MonoSymbolArchive", monoSymbolArchive);
			proj.SetProperty (proj.ReleaseProperties, KnownProperties.AndroidCreatePackagePerAbi, "true");
			proj.SetProperty (proj.ReleaseProperties, "AndroidPackageFormat", packageFormat);
			proj.SetAndroidSupportedAbis ("armeabi-v7a", "x86");
			using (var b = CreateApkBuilder ()) {
				b.ThrowOnBuildFailure = false;
				Assert.IsTrue (b.Build (proj), "first build failed");
				var outputPath = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath);
				var archivePath = Path.Combine (outputPath, $"{proj.PackageName}.{packageFormat}.mSYM");
				Assert.AreEqual (monoSymbolArchive, Directory.Exists (archivePath),
					string.Format ("The msym archive {0} exist.", monoSymbolArchive ? "should" : "should not"));
			}
		}

		[Test]
		public void CheckProguardMappingFileExists ()
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true,
			};
			proj.SetProperty (proj.ReleaseProperties, KnownProperties.AndroidDexTool, "d8");
			proj.SetProperty (proj.ReleaseProperties, KnownProperties.AndroidLinkTool, "r8");
			// Projects must provide a $(AndroidProguardMappingFile) value to opt in
			proj.SetProperty (proj.ReleaseProperties, "AndroidProguardMappingFile", @"$(OutputPath)\mapping.txt");

			using (var b = CreateApkBuilder ()) {
				string mappingFile = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, "mapping.txt");
				Assert.IsTrue (b.Build (proj), "build should have succeeded.");
				FileAssert.Exists (mappingFile, $"'{mappingFile}' should have been generated.");
			}
		}

		[Test]
		public void CheckIncludedAssemblies ()
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true
			};
			proj.SetAndroidSupportedAbis ("armeabi-v7a");
			if (!Builder.UseDotNet) {
				proj.PackageReferences.Add (new Package {
					Id = "System.Runtime.InteropServices.WindowsRuntime",
					Version = "4.0.1",
					TargetFramework = "monoandroid71",
				});
				proj.References.Add (new BuildItem.Reference ("Mono.Data.Sqlite.dll"));
				proj.MainActivity = proj.DefaultMainActivity.Replace ("//${AFTER_ONCREATE}", "var command = new Mono.Data.Sqlite.SqliteCommand ();");
			}
			var expectedFiles = Builder.UseDotNet ?
				new [] {
					"Java.Interop.dll",
					"Mono.Android.dll",
					"rc.bin",
					"System.Private.CoreLib.dll",
					"System.Runtime.dll",
					"System.Linq.dll",
					"UnnamedProject.dll",
				} :
				new [] {
					"Java.Interop.dll",
					"Mono.Android.dll",
					"mscorlib.dll",
					"System.Core.dll",
					"System.Data.dll",
					"System.dll",
					"UnnamedProject.dll",
					"Mono.Data.Sqlite.dll",
					"Mono.Data.Sqlite.dll.config",
				};
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "build should have succeeded.");
				var apk = Path.Combine (Root, b.ProjectDirectory,
						proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				using (var zip = ZipHelper.OpenZip (apk)) {
					var existingFiles = zip.Where (a => a.FullName.StartsWith ("assemblies/", StringComparison.InvariantCultureIgnoreCase));
					var missingFiles = expectedFiles.Where (x => !zip.ContainsEntry ("assemblies/" + Path.GetFileName (x)));
					Assert.IsFalse (missingFiles.Any (),
						string.Format ("The following Expected files are missing. {0}",
						string.Join (Environment.NewLine, missingFiles)));
					var additionalFiles = existingFiles.Where (x => !expectedFiles.Contains (Path.GetFileName (x.FullName)));
					Assert.IsTrue (!additionalFiles.Any (),
						string.Format ("Unexpected Files found! {0}",
						string.Join (Environment.NewLine, additionalFiles.Select (x => x.FullName))));
				}
			}
		}

		[Test]
		public void CheckClassesDexIsIncluded ([Values ("dx", "d8", "invalid")] string dexTool)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
				DexTool = dexTool,
			};
			using (var b = CreateApkBuilder ()) {
				b.ThrowOnBuildFailure = false;
				if (Builder.UseDotNet && dexTool == "dx") {
					Assert.IsFalse (b.Build (proj), "build failed");
					StringAssertEx.Contains ("XA1023", b.LastBuildOutput, "Output should contain XA1023 errors");
					return;
				}

				Assert.IsTrue (b.Build (proj), "build failed");
				var apk = Path.Combine (Root, b.ProjectDirectory,
						proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				using (var zip = ZipHelper.OpenZip (apk)) {
					Assert.IsTrue (zip.ContainsEntry ("classes.dex"), "Apk should contain classes.dex");
				}
			}
		}

		[Test]
		[Parallelizable (ParallelScope.Self)]
		public void CheckIncludedNativeLibraries ([Values (true, false)] bool compressNativeLibraries, [Values (true, false)] bool useAapt2)
		{
			AssertAaptSupported (useAapt2);
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.PackageReferences.Add(KnownPackages.SQLitePCLRaw_Core);
			proj.AndroidUseAapt2 = useAapt2;
			proj.SetAndroidSupportedAbis ("x86");
			proj.SetProperty (proj.ReleaseProperties, "AndroidStoreUncompressedFileExtensions", compressNativeLibraries ? "" : "so");
			using (var b = CreateApkBuilder ()) {
				b.ThrowOnBuildFailure = false;
				Assert.IsTrue (b.Build (proj), "build failed");
				var apk = Path.Combine (Root, b.ProjectDirectory,
						proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				CompressionMethod method = compressNativeLibraries ? CompressionMethod.Deflate : CompressionMethod.Store;
				using (var zip = ZipHelper.OpenZip (apk)) {
					var libFiles = zip.Where (x => x.FullName.StartsWith("lib/") && !x.FullName.Equals("lib/", StringComparison.InvariantCultureIgnoreCase));
					var abiPaths = new string[] { "lib/x86/" };
					foreach (var file in libFiles) {
						Assert.IsTrue (abiPaths.Any (x => file.FullName.Contains (x)), $"Apk contains an unnesscary lib file: {file.FullName}");
						Assert.IsTrue (file.CompressionMethod == method, $"{file.FullName} should have been CompressionMethod.{method} in the apk, but was CompressionMethod.{file.CompressionMethod}");
					}
				}
			}
		}

		[Test]
		public void EmbeddedDSOs ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.AndroidManifest = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" android:versionCode=""1"" android:versionName=""1.0"" package=""{proj.PackageName}"">
	<uses-sdk />
	<application android:label=""{proj.ProjectName}"" android:extractNativeLibs=""false"">
	</application>
</manifest>";

			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "first build should have succeeded");

				var manifest = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				AssertExtractNativeLibs (manifest, extractNativeLibs: false);

				var apk = Path.Combine (Root, b.ProjectDirectory,
						proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				AssertEmbeddedDSOs (apk);

				//Delete the apk & build again
				File.Delete (apk);
				Assert.IsTrue (b.Build (proj), "second build should have succeeded");
				AssertEmbeddedDSOs (apk);
			}
		}

		void AssertEmbeddedDSOs (string apk)
		{
			FileAssert.Exists (apk);

			using (var zip = ZipHelper.OpenZip (apk)) {
				foreach (var entry in zip) {
					if (entry.FullName.EndsWith (".so")) {
						AssertCompression (entry, compressed: false);
					}
				}
			}
		}

		void AssertCompression (ZipEntry entry, bool compressed)
		{
			if (compressed) {
				Assert.AreNotEqual (CompressionMethod.Store, entry.CompressionMethod, $"`{entry.FullName}` should be compressed!");
				Assert.AreNotEqual (entry.Size, entry.CompressedSize, $"`{entry.FullName}` should be compressed!");
			} else {
				Assert.AreEqual (CompressionMethod.Store, entry.CompressionMethod, $"`{entry.FullName}` should be uncompressed!");
				Assert.AreEqual (entry.Size, entry.CompressedSize, $"`{entry.FullName}` should be uncompressed!");
			}
		}

		[Test]
		public void IncrementalCompression ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.OtherBuildItems.Add (new AndroidItem.AndroidAsset ("foo.bar") {
				BinaryContent = () => new byte [1024],
			});

			var manifest_template = proj.AndroidManifest;
			proj.AndroidManifest = manifest_template.Replace ("<application ", "<application android:extractNativeLibs=\"true\" ");

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "first build should have succeeded");

				var apk = Path.Combine (Root, b.ProjectDirectory,
						proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				FileAssert.Exists (apk);
				using (var zip = ZipHelper.OpenZip (apk)) {
					foreach (var entry in zip) {
						if (entry.FullName.EndsWith (".so") || entry.FullName.EndsWith (".bar")) {
							AssertCompression (entry, compressed: true);
						}
					}
				}

				// Change manifest & compressed extensions
				proj.AndroidManifest = manifest_template.Replace ("<application ", "<application android:extractNativeLibs=\"false\" ");
				proj.Touch ("Properties\\AndroidManifest.xml");
				proj.SetProperty ("AndroidStoreUncompressedFileExtensions", ".bar");

				b.BuildLogFile = "build2.log";
				Assert.IsTrue (b.Build (proj), "second build should have succeeded");

				FileAssert.Exists (apk);
				using (var zip = ZipHelper.OpenZip (apk)) {
					foreach (var entry in zip) {
						if (entry.FullName.EndsWith (".so") || entry.FullName.EndsWith (".bar")) {
							AssertCompression (entry, compressed: false);
						}
					}
				}
			}
		}

		[Test]
		public void ExplicitPackageNamingPolicy ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.Sources.Add (new BuildItem.Source ("Bar.cs") {
				TextContent = () => "namespace Foo { class Bar : Java.Lang.Object { } }"
			});
			proj.SetProperty (proj.DebugProperties, "AndroidPackageNamingPolicy", "Lowercase");
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "build failed");
				var text = b.Output.GetIntermediaryAsText (b.Output.IntermediateOutputPath, Path.Combine ("android", "src", "foo", "Bar.java"));
				Assert.IsTrue (text.Contains ("package foo;"), "expected package not found in the source.");
			}
		}

		[Test]
		public void CheckMetadataSkipItemsAreProcessedCorrectly ()
		{
			var packages = new List<Package> () {
				KnownPackages.Android_Arch_Core_Common_26_1_0,
				KnownPackages.Android_Arch_Lifecycle_Common_26_1_0,
				KnownPackages.Android_Arch_Lifecycle_Runtime_26_1_0,
				KnownPackages.AndroidSupportV4_27_0_2_1,
				KnownPackages.SupportCompat_27_0_2_1,
				KnownPackages.SupportCoreUI_27_0_2_1,
				KnownPackages.SupportCoreUtils_27_0_2_1,
				KnownPackages.SupportDesign_27_0_2_1,
				KnownPackages.SupportFragment_27_0_2_1,
				KnownPackages.SupportMediaCompat_27_0_2_1,
				KnownPackages.SupportV7AppCompat_27_0_2_1,
				KnownPackages.SupportV7CardView_27_0_2_1,
				KnownPackages.SupportV7MediaRouter_27_0_2_1,
				KnownPackages.SupportV7RecyclerView_27_0_2_1,
				KnownPackages.VectorDrawable_27_0_2_1,
				new Package () { Id = "Xamarin.Android.Support.Annotations", Version = "27.0.2.1" },
				new Package () { Id = "Xamarin.Android.Support.Transition", Version = "27.0.2.1" },
				new Package () { Id = "Xamarin.Android.Support.v7.Palette", Version = "27.0.2.1" },
				new Package () { Id = "Xamarin.Android.Support.Animated.Vector.Drawable", Version = "27.0.2.1" },
			};

			string metaDataTemplate = @"<AndroidCustomMetaDataForReferences Include=""%"">
	<AndroidSkipAddToPackage>True</AndroidSkipAddToPackage>
	<AndroidSkipJavaStubGeneration>True</AndroidSkipJavaStubGeneration>
	<AndroidSkipResourceExtraction>True</AndroidSkipResourceExtraction>
</AndroidCustomMetaDataForReferences>";
			var proj = new XamarinAndroidApplicationProject () {
				Imports = {
					new Import (() => "CustomMetaData.target") {
						TextContent = () => @"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
<ItemGroup>" +
string.Join ("\n", packages.Select (x => metaDataTemplate.Replace ("%", x.Id))) +
@"</ItemGroup>
</Project>"
					},
				}
			};
			proj.SetProperty (proj.DebugProperties, "AndroidPackageNamingPolicy", "Lowercase");
			foreach (var package in packages)
				proj.PackageReferences.Add (package);
			using (var b = CreateApkBuilder ()) {
				b.ThrowOnBuildFailure = false;
				Assert.IsTrue (b.Build (proj), "build failed");
				var bin = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath);
				var obj = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				var lp = Path.Combine (obj, "lp");
				Assert.IsTrue (Directory.Exists (lp), $"{lp} should exists.");
				Assert.AreEqual (0, Directory.GetDirectories (lp).Length, $"{lp} should NOT contain any directories.");
				var support = Path.Combine (obj, "android", "src", "android", "support");
				Assert.IsFalse (Directory.Exists (support), $"{support} should NOT exists.");
				Assert.IsFalse (File.Exists (lp), $" should NOT have been generated.");
				foreach (var apk in Directory.GetFiles (bin, "*-Signed.apk")) {
					using (var zip = ZipHelper.OpenZip (apk)) {
						foreach (var package in packages) {
							Assert.IsFalse (zip.Any (e => e.FullName == $"assemblies/{package.Id}.dll"), $"APK file `{apk}` should not contain {package.Id}");
						}
					}
				}
			}
		}

		[Test]
		[Category ("SmokeTests")]
		public void CheckSignApk ([Values(true, false)] bool useApkSigner, [Values(true, false)] bool perAbiApk)
		{
			string ext = Environment.OSVersion.Platform != PlatformID.Unix ? ".bat" : "";
			var foundApkSigner = Directory.EnumerateDirectories (Path.Combine (AndroidSdkPath, "build-tools")).Any (dir => Directory.EnumerateFiles (dir, "apksigner"+ ext).Any ());
			if (useApkSigner && !foundApkSigner) {
				Assert.Ignore ("Skipping test. Required build-tools verison which contains apksigner is not installed.");
			}
			string keyfile = Path.Combine (Root, "temp", TestName, "release.keystore");
			if (File.Exists (keyfile))
				File.Delete (keyfile);
			string keyToolPath = Path.Combine (AndroidSdkResolver.GetJavaSdkPath (), "bin");
			var engine = new MockBuildEngine (Console.Out);
			string pass = "Cy(nBW~j.&@B-!R_aq7/syzFR!S$4]7R%i6)R!";
			string alias = "release store";
			var task = new AndroidCreateDebugKey {
				BuildEngine = engine,
				KeyStore = keyfile,
				StorePass = pass,
				KeyAlias = alias,
				KeyPass = pass,
				KeyAlgorithm="RSA",
				Validity=30,
				StoreType="pkcs12",
				Command="-genkeypair",
				ToolPath = keyToolPath,
			};
			Assert.IsTrue (task.Execute (), "Task should have succeeded.");
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.SetProperty (proj.ReleaseProperties, "AndroidUseApkSigner", useApkSigner);
			proj.SetProperty (proj.ReleaseProperties, "AndroidKeyStore", "True");
			proj.SetProperty (proj.ReleaseProperties, "AndroidSigningKeyStore", keyfile);
			proj.SetProperty (proj.ReleaseProperties, "AndroidSigningKeyAlias", alias);
			proj.SetProperty (proj.ReleaseProperties, "AndroidSigningKeyPass", Uri.EscapeDataString (pass));
			proj.SetProperty (proj.ReleaseProperties, "AndroidSigningStorePass", Uri.EscapeDataString (pass));
			proj.SetProperty (proj.ReleaseProperties, KnownProperties.AndroidCreatePackagePerAbi, perAbiApk);
			if (perAbiApk) {
				proj.SetAndroidSupportedAbis ("armeabi-v7a", "x86", "arm64-v8a", "x86_64");
			} else {
				proj.SetAndroidSupportedAbis ("armeabi-v7a", "x86");
			}
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestContext.CurrentContext.Test.Name))) {
				var bin = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath);
				Assert.IsTrue (b.Build (proj), "First build failed");
				b.AssertHasNoWarnings ();

				//Make sure the APKs are signed
				foreach (var apk in Directory.GetFiles (bin, "*-Signed.apk")) {
					using (var zip = ZipHelper.OpenZip (apk)) {
						Assert.IsTrue (zip.Any (e => e.FullName == "META-INF/MANIFEST.MF"), $"APK file `{apk}` is not signed! It is missing `META-INF/MANIFEST.MF`.");
					}
				}

				// Make sure the APKs have unique version codes
				if (perAbiApk) {
					int armManifestCode = GetVersionCodeFromIntermediateManifest (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "armeabi-v7a", "AndroidManifest.xml"));
					int x86ManifestCode = GetVersionCodeFromIntermediateManifest (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "x86", "AndroidManifest.xml"));
					int arm64ManifestCode = GetVersionCodeFromIntermediateManifest (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "arm64-v8a", "AndroidManifest.xml"));
					int x86_64ManifestCode = GetVersionCodeFromIntermediateManifest (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "x86_64", "AndroidManifest.xml"));
					var versionList = new List<int> { armManifestCode, x86ManifestCode, arm64ManifestCode, x86_64ManifestCode };
					Assert.True (versionList.Distinct ().Count () == versionList.Count,
						$"APK version codes were not unique - armeabi-v7a: {armManifestCode}, x86: {x86ManifestCode}, arm64-v8a: {arm64ManifestCode}, x86_64: {x86_64ManifestCode}");
				}

				var item = proj.AndroidResources.First (x => x.Include () == "Resources\\values\\Strings.xml");
				item.TextContent = () => proj.StringsXml.Replace ("${PROJECT_NAME}", "Foo");
				item.Timestamp = null;
				Assert.IsTrue (b.Build (proj), "Second build failed");
				b.AssertHasNoWarnings ();

				//Make sure the APKs are signed
				foreach (var apk in Directory.GetFiles (bin, "*-Signed.apk")) {
					using (var zip = ZipHelper.OpenZip (apk)) {
						Assert.IsTrue (zip.Any (e => e.FullName == "META-INF/MANIFEST.MF"), $"APK file `{apk}` is not signed! It is missing `META-INF/MANIFEST.MF`.");
					}
				}
			}

			int GetVersionCodeFromIntermediateManifest (string manifestFilePath)
			{
				var doc = XDocument.Load (manifestFilePath);
				var versionCode = doc.Descendants ()
					.Where (e => e.Name == "manifest")
					.Select (m => m.Attribute ("{http://schemas.android.com/apk/res/android}versionCode")).FirstOrDefault ();

				if (!int.TryParse (versionCode?.Value, out int parsedCode))
					Assert.Fail ($"Unable to parse 'versionCode' value from manifest content: {File.ReadAllText (manifestFilePath)}.");
				return parsedCode;
			}
		}

		[Test]
		[Category ("DotNetIgnore")] // Xamarin.Forms version is too old, uses net45 MSBuild tasks
		[NonParallelizable] // Commonly fails NuGet restore
		public void CheckAapt2WarningsDoNotGenerateErrors ()
		{
			//https://github.com/xamarin/xamarin-android/issues/3083
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
				TargetFrameworkVersion = Xamarin.ProjectTools.Versions.Oreo_27,
				UseLatestPlatformSdk = false,
			};
			proj.PackageReferences.Add (KnownPackages.XamarinForms_2_3_4_231);
			proj.PackageReferences.Add (KnownPackages.AndroidSupportV4_27_0_2_1);
			proj.PackageReferences.Add (KnownPackages.SupportCompat_27_0_2_1);
			proj.PackageReferences.Add (KnownPackages.SupportCoreUI_27_0_2_1);
			proj.PackageReferences.Add (KnownPackages.SupportCoreUtils_27_0_2_1);
			proj.PackageReferences.Add (KnownPackages.SupportDesign_27_0_2_1);
			proj.PackageReferences.Add (KnownPackages.SupportFragment_27_0_2_1);
			proj.PackageReferences.Add (KnownPackages.SupportMediaCompat_27_0_2_1);
			proj.PackageReferences.Add (KnownPackages.SupportV7AppCompat_27_0_2_1);
			proj.PackageReferences.Add (KnownPackages.SupportV7CardView_27_0_2_1);
			proj.PackageReferences.Add (KnownPackages.SupportV7MediaRouter_27_0_2_1);
			proj.SetProperty (proj.ReleaseProperties, KnownProperties.AndroidCreatePackagePerAbi, true);
			proj.SetAndroidSupportedAbis ("armeabi-v7a", "x86");
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				if (!b.TargetFrameworkExists (proj.TargetFrameworkVersion))
					Assert.Ignore ($"Skipped as {proj.TargetFrameworkVersion} not available.");
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");
				string intermediateDir = TestEnvironment.IsWindows
					? Path.Combine (proj.IntermediateOutputPath, proj.TargetFrameworkAbbreviated) : proj.IntermediateOutputPath;
				var packagedResource = Path.Combine (b.Root, b.ProjectDirectory, intermediateDir, "android", "bin", "packaged_resources");
				FileAssert.Exists (packagedResource, $"{packagedResource} should have been created.");
				var packagedResourcearm = packagedResource + "-armeabi-v7a";
				FileAssert.Exists (packagedResourcearm, $"{packagedResourcearm} should have been created.");
				var packagedResourcex86 = packagedResource + "-x86";
				FileAssert.Exists (packagedResourcex86, $"{packagedResourcex86} should have been created.");
			}
		}

		[Test]
		[Category ("SmokeTests")]
		public void CheckAppBundle ([Values (true, false)] bool isRelease)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = isRelease,
			};
			proj.SetProperty ("AndroidPackageFormat", "aab");
			// Disable the fast deployment because it is not currently compatible with aabs and so gives an XA0119 build error.
			proj.EmbedAssembliesIntoApk = true;

			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				var bin = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath);
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");

				// Make sure the AAB is signed
				var aab = Path.Combine (bin, $"{proj.PackageName}-Signed.aab");
				using (var zip = ZipHelper.OpenZip (aab)) {
					Assert.IsTrue (zip.Any (e => e.FullName == "META-INF/MANIFEST.MF"), $"AAB file `{aab}` is not signed! It is missing `META-INF/MANIFEST.MF`.");
				}

				// Build with no changes
				Assert.IsTrue (b.Build (proj), "second build should have succeeded.");
				foreach (var target in new [] { "_Sign", "_BuildApkEmbed" }) {
					Assert.IsTrue (b.Output.IsTargetSkipped (target), $"`{target}` should be skipped!");
				}
			}
		}

		[Test]
		[Category ("SmokeTests"), Category ("DotNetIgnore")] // Xamarin.Forms version is too old, uses net45 MSBuild tasks
		public void NetStandardReferenceTest ()
		{
			var netStandardProject = new DotNetStandard () {
				ProjectName = "XamFormsSample",
				ProjectGuid = Guid.NewGuid ().ToString (),
				Sdk = "Microsoft.NET.Sdk",
				TargetFramework = "netstandard1.4",
				IsRelease = true,
				PackageTargetFallback = "portable-net45+win8+wpa81+wp8",
				PackageReferences = {
					KnownPackages.XamarinForms_2_3_4_231,
					new Package () {
						Id = "System.IO.Packaging",
						Version = "4.4.0",
					},
					new Package () {
						Id = "Newtonsoft.Json",
						Version = "13.0.1"
					},
				},
				OtherBuildItems = {
					new BuildItem ("None") {
						Remove = () => "**\\*.xaml",
					},
					new BuildItem ("Compile") {
						Update = () => "**\\*.xaml.cs",
						DependentUpon = () => "%(Filename)"
					},
					new BuildItem ("EmbeddedResource") {
						Include = () => "**\\*.xaml",
						SubType = () => "Designer",
						Generator = () => "MSBuild:UpdateDesignTimeXaml",
					},
				},
				Sources = {
					new BuildItem.Source ("App.xaml.cs") {
						TextContent = () => @"using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.IO.Packaging;

using Xamarin.Forms;

namespace XamFormsSample
{
    public partial class App : Application
    {
        Package package;

        public App()
        {
            try {
                JsonConvert.DeserializeObject<string>(""test"");
                package = Package.Open ("""");
            } catch {
            }
            InitializeComponent();

            MainPage = new ContentPage ();
        }

        protected override void OnStart()
        {
            // Handle when your app starts
        }

        protected override void OnSleep()
        {
            // Handle when your app sleeps
        }

        protected override void OnResume()
        {
            // Handle when your app resumes
        }
    }
}",
					},
					new BuildItem.Source ("App.xaml") {
						TextContent = () => @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Application xmlns=""http://xamarin.com/schemas/2014/forms""
             xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
             x:Class=""XamFormsSample.App"">
  <Application.Resources>
    <!-- Application resource dictionary -->
  </Application.Resources>
</Application>",
					},
				},
			};

			var app = new XamarinAndroidApplicationProject () {
				ProjectName = "App1",
				IsRelease = true,
				UseLatestPlatformSdk = true,
				References = {
					new BuildItem.Reference ("Mono.Android.Export"),
					new BuildItem.ProjectReference ($"..\\{netStandardProject.ProjectName}\\{netStandardProject.ProjectName}.csproj",
						netStandardProject.ProjectName, netStandardProject.ProjectGuid),
				},
				PackageReferences = {
					KnownPackages.SupportDesign_27_0_2_1,
					KnownPackages.SupportV7CardView_27_0_2_1,
					KnownPackages.AndroidSupportV4_27_0_2_1,
					KnownPackages.SupportCoreUtils_27_0_2_1,
					KnownPackages.SupportMediaCompat_27_0_2_1,
					KnownPackages.SupportFragment_27_0_2_1,
					KnownPackages.SupportCoreUI_27_0_2_1,
					KnownPackages.SupportCompat_27_0_2_1,
					KnownPackages.SupportV7AppCompat_27_0_2_1,
					KnownPackages.SupportV7MediaRouter_27_0_2_1,
					KnownPackages.XamarinForms_2_3_4_231,
					new Package () {
						Id = "System.Runtime.Loader",
						Version = "4.3.0",
					},
				}
			};
			app.MainActivity = @"using System;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using XamFormsSample;

namespace App1
{
	[Activity (Label = ""App1"", MainLauncher = true, Icon = ""@drawable/icon"")]
	public class MainActivity : global::Xamarin.Forms.Platform.Android.FormsAppCompatActivity {
			protected override void OnCreate (Bundle bundle)
			{
				base.OnCreate (bundle);

				global::Xamarin.Forms.Forms.Init (this, bundle);

				LoadApplication (new App ());
			}
		}
	}";
			app.SetAndroidSupportedAbis ("x86", "armeabi-v7a");
			var expectedFiles = new string [] {
				"Java.Interop.dll",
				"Mono.Android.dll",
				"mscorlib.dll",
				"System.Core.dll",
				"System.Data.dll",
				"System.dll",
				"System.Runtime.Serialization.dll",
				"System.IO.Packaging.dll",
				"System.IO.Compression.dll",
				"Mono.Android.Export.dll",
				"App1.dll",
				"FormsViewGroup.dll",
				"Xamarin.Android.Arch.Lifecycle.Common.dll",
				"Xamarin.Android.Support.Compat.dll",
				"Xamarin.Android.Support.Core.UI.dll",
				"Xamarin.Android.Support.Core.Utils.dll",
				"Xamarin.Android.Support.Design.dll",
				"Xamarin.Android.Support.Fragment.dll",
				"Xamarin.Android.Support.v7.AppCompat.dll",
				"Xamarin.Android.Support.v7.CardView.dll",
				"Xamarin.Forms.Core.dll",
				"Xamarin.Forms.Platform.Android.dll",
				"Xamarin.Forms.Platform.dll",
				"Xamarin.Forms.Xaml.dll",
				"XamFormsSample.dll",
				"Mono.Security.dll",
				"System.Xml.dll",
				"System.Net.Http.dll",
				"System.ServiceModel.Internals.dll",
				"Newtonsoft.Json.dll",
				"System.Numerics.dll",
				"System.Xml.Linq.dll",
			};
			var path = Path.Combine ("temp", TestContext.CurrentContext.Test.Name);
			using (var builder = CreateDllBuilder (Path.Combine (path, netStandardProject.ProjectName), cleanupOnDispose: false)) {
				using (var ab = CreateApkBuilder (Path.Combine (path, app.ProjectName), cleanupOnDispose: false)) {
					Assert.IsTrue (builder.Build (netStandardProject), "XamFormsSample should have built.");
					Assert.IsTrue (ab.Build (app), "App should have built.");
					var apk = Path.Combine (Root, ab.ProjectDirectory,
						app.OutputPath, $"{app.PackageName}-Signed.apk");
					using (var zip = ZipHelper.OpenZip (apk)) {
						var existingFiles = zip.Where (a => a.FullName.StartsWith ("assemblies/", StringComparison.InvariantCultureIgnoreCase));
						var missingFiles = expectedFiles.Where (x => !zip.ContainsEntry ("assemblies/" + Path.GetFileName (x)));
						Assert.IsFalse (missingFiles.Any (),
						string.Format ("The following Expected files are missing. {0}",
							string.Join (Environment.NewLine, missingFiles)));
						var additionalFiles = existingFiles.Where (x => !expectedFiles.Contains (Path.GetFileName (x.FullName)));
						Assert.IsTrue (!additionalFiles.Any (),
							string.Format ("Unexpected Files found! {0}",
							string.Join (Environment.NewLine, additionalFiles.Select (x => x.FullName))));
					}
				}
			}
		}

		[Test]
		[Category ("DotNetIgnore")] // Uses MSBuild.Sdk.Extras
		public void CheckTheCorrectRuntimeAssemblyIsUsedFromNuget ()
		{
			string monoandroidFramework = "monoandroid10.0";
			string path = Path.Combine (Root, "temp", TestName);
			var ns = new DotNetStandard () {
				ProjectName = "Dummy",
				Sdk = "MSBuild.Sdk.Extras/2.0.54",
				Sources = {
					new BuildItem.Source ("Class1.cs") {
						TextContent = () => @"public class Class1 {
#if __ANDROID__
	public static string Library => ""Android"";
#else
	public static string Library => "".NET Standard"";
#endif
}",
					},
				},
				OtherBuildItems = {
					new BuildItem.NoActionResource ("$(OutputPath)netstandard2.0\\$(AssemblyName).dll") {
						TextContent = null,
						BinaryContent = null,
						Metadata = {
							{ "PackagePath", "ref\\netstandard2.0" },
							{ "Pack", "True" }
						},
					},
					new BuildItem.NoActionResource ($"$(OutputPath){monoandroidFramework}\\$(AssemblyName).dll") {
						TextContent = null,
						BinaryContent = null,
						Metadata = {
							{ "PackagePath", $"lib\\{monoandroidFramework}" },
							{ "Pack", "True" }
						},
					},
				},
			};
			ns.SetProperty ("TargetFrameworks", $"netstandard2.0;{monoandroidFramework}");
			ns.SetProperty ("PackageId", "dummy.package.foo");
			ns.SetProperty ("PackageVersion", "1.0.0");
			ns.SetProperty ("GeneratePackageOnBuild", "True");
			ns.SetProperty ("IncludeBuildOutput", "False");
			ns.SetProperty ("Summary", "Test");
			ns.SetProperty ("Description", "Test");
			ns.SetProperty ("PackageOutputPath", path);


			var xa = new XamarinAndroidApplicationProject () {
				ProjectName = "App",
				PackageReferences = {
					new Package () {
						Id = "dummy.package.foo",
						Version = "1.0.0",
					},
				},
				OtherBuildItems = {
					new BuildItem.NoActionResource ("NuGet.config") {
					},
				},
			};
			xa.SetProperty ("RestoreNoCache", "true");
			xa.SetProperty ("RestorePackagesPath", "$(MSBuildThisFileDirectory)packages");
			using (var nsb = CreateDllBuilder (Path.Combine (path, ns.ProjectName), cleanupAfterSuccessfulBuild: false, cleanupOnDispose: false))
			using (var xab = CreateApkBuilder (Path.Combine (path, xa.ProjectName), cleanupAfterSuccessfulBuild: false, cleanupOnDispose: false)) {
				nsb.ThrowOnBuildFailure = xab.ThrowOnBuildFailure = false;
				Assert.IsTrue (nsb.Build (ns), "Build of NetStandard Library should have succeeded.");
				Assert.IsFalse (xab.Build (xa, doNotCleanupOnUpdate: true), "Build of App Library should have failed.");
				File.WriteAllText (Path.Combine (Root, xab.ProjectDirectory, "NuGet.config"), @"<?xml version='1.0' encoding='utf-8'?>
<configuration>
  <packageSources>
    <add key='nuget.org' value='https://api.nuget.org/v3/index.json' protocolVersion='3' />
    <add key='bug-testing' value='..' />
  </packageSources>
</configuration>");
				Assert.IsTrue (xab.Build (xa, doNotCleanupOnUpdate: true), "Build of App Library should have succeeded.");
				string expected = Path.Combine ("dummy.package.foo", "1.0.0", "lib", monoandroidFramework, "Dummy.dll");
				Assert.IsTrue (xab.LastBuildOutput.ContainsText (expected), $"Build should be using {expected}");
			}
		}

		[Test]
		public void MissingDexFile ()
		{
			//NOTE: The trailing / was breaking <CompileToDalvik /> in 16.5
			var parameters = new [] { "_AndroidIntermediateJavaClassDirectory=obj/Debug/android/bin/classes/" };
			var proj = new XamarinAndroidApplicationProject {
				DexTool = "dx"
			};
			AssertDexToolSupported (proj.DexTool);
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj, parameters: parameters), "Build should have succeeded.");
				var apk = Path.Combine (Root, b.ProjectDirectory,
					proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				using (var zip = ZipHelper.OpenZip (apk)) {
					Assert.IsTrue (zip.ContainsEntry ("classes.dex"), "Apk should contain classes.dex");
				}
			}
		}

		[Test]
		public void MissingSatelliteAssemblyInLibrary ()
		{
			var path = Path.Combine ("temp", TestName);
			var lib = new XamarinAndroidLibraryProject {
				ProjectName = "Localization",
				OtherBuildItems = {
					new BuildItem ("EmbeddedResource", "Foo.resx") {
						TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancel</value></data>")
					},
					new BuildItem ("EmbeddedResource", "Foo.es.resx") {
						TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancelar</value></data>")
					}
				}
			};

			var app = new XamarinAndroidApplicationProject {
				IsRelease = true,
			};
			app.References.Add (new BuildItem.ProjectReference ($"..\\{lib.ProjectName}\\{lib.ProjectName}.csproj", lib.ProjectName, lib.ProjectGuid));

			using (var libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName)))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
				Assert.IsTrue (libBuilder.Build (lib), "Library Build should have succeeded.");
				appBuilder.Target = "Build";
				Assert.IsTrue (appBuilder.Build (app), "App Build should have succeeded.");
				appBuilder.Target = "SignAndroidPackage";
				Assert.IsTrue (appBuilder.Build (app), "App SignAndroidPackage should have succeeded.");

				var apk = Path.Combine (Root, appBuilder.ProjectDirectory,
					app.OutputPath, $"{app.PackageName}-Signed.apk");
				using (var zip = ZipHelper.OpenZip (apk)) {
					Assert.IsTrue (zip.ContainsEntry ($"assemblies/es/{lib.ProjectName}.resources.dll"), "Apk should contain satellite assemblies!");
				}
			}
		}

		[Test]
		public void MissingSatelliteAssemblyInApp ()
		{
			var proj = new XamarinAndroidApplicationProject {
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

			using (var b = CreateApkBuilder ()) {
				b.Target = "Build";
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				b.Target = "SignAndroidPackage";
				Assert.IsTrue (b.Build (proj), "SignAndroidPackage should have succeeded.");

				var apk = Path.Combine (Root, b.ProjectDirectory,
					proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				using (var zip = ZipHelper.OpenZip (apk)) {
					Assert.IsTrue (zip.ContainsEntry ($"assemblies/es/{proj.ProjectName}.resources.dll"), "Apk should contain satellite assemblies!");
				}
			}
		}

		[Test]
		public void IgnoreManifestFromJar ()
		{
			string java = @"
package com.xamarin.testing;

public class Test
{
}
";
			var path = Path.Combine (Root, "temp", TestName);
			var javaDir = Path.Combine (path, "java", "com", "xamarin", "testing");
			if (Directory.Exists (javaDir))
				Directory.Delete (javaDir, true);
			Directory.CreateDirectory (javaDir);
			File.WriteAllText (Path.Combine (javaDir, "..", "..", "..", "AndroidManifest.xml"), @"<?xml version='1.0' ?><maniest />");
			var lib = new XamarinAndroidBindingProject () {
				AndroidClassParser = "class-parse",
				ProjectName = "Binding1",
			};
			lib.MetadataXml = "<metadata></metadata>";
			lib.Jars.Add (new AndroidItem.EmbeddedJar (Path.Combine ("java", "test.jar")) {
				BinaryContent = new JarContentBuilder () {
					BaseDirectory = Path.Combine (path, "java"),
					JarFileName = "test.jar",
					JavaSourceFileName = Path.Combine ("com", "xamarin", "testing", "Test.java"),
					JavaSourceText = java,
					AdditionalFileExtensions = "*.xml",
				}.Build
			});
			var app = new XamarinAndroidApplicationProject {
				IsRelease = true,
			};
			app.References.Add (new BuildItem.ProjectReference ($"..\\{lib.ProjectName}\\{lib.ProjectName}.csproj", lib.ProjectName, lib.ProjectGuid));

			using (var builder = CreateDllBuilder (Path.Combine (path, lib.ProjectName))) {
				Assert.IsTrue (builder.Build (lib), "Build of jar should have succeeded.");
				using (var zip = ZipHelper.OpenZip (Path.Combine (path, "java", "test.jar"))) {
					Assert.IsTrue (zip.ContainsEntry ($"AndroidManifest.xml"), "Jar should contain AndroidManifest.xml");
				}
				using (var b = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
					Assert.IsTrue (b.Build (app), "Build of jar should have succeeded.");
					var jar = Builder.UseDotNet ? "2965D0C9A2D5DB1E.jar" : "test.jar";
					string expected = $"Failed to add jar entry AndroidManifest.xml from {jar}: the same file already exists in the apk";
					Assert.IsTrue (b.LastBuildOutput.ContainsText (expected), $"AndroidManifest.xml for test.jar should have been ignored.");
				}
			}
		}

		[Test]
		public void ExtractNativeLibsTrue ()
		{
			var proj = new XamarinAndroidApplicationProject {
				// This combination produces android:extractNativeLibs="false" by default
				MinSdkVersion = "23",
				ManifestMerger = "manifestmerger.jar",
			};
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				// We should find extractNativeLibs="true"
				var manifest = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				AssertExtractNativeLibs (manifest, extractNativeLibs: true);

				// All .so files should be compressed
				var apk = Path.Combine (Root, b.ProjectDirectory,
					proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				using (var zip = ZipHelper.OpenZip (apk)) {
					foreach (var entry in zip) {
						if (entry.FullName.EndsWith (".so")) {
							AssertCompression (entry, compressed: true);
						}
					}
				}
			}
		}
	}
}
