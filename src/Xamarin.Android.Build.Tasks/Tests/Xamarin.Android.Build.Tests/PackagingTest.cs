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
	[Parallelizable (ParallelScope.Children)]
	public class PackagingTest : BaseTest
	{
		[Test]
		public void CheckProguardMappingFileExists ()
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true,
			};
			proj.SetProperty (proj.ReleaseProperties, KnownProperties.AndroidLinkTool, "r8");
			// Projects must set $(AndroidCreateProguardMappingFile) to true to opt in
			proj.SetProperty (proj.ReleaseProperties, "AndroidCreateProguardMappingFile", true);

			using (var b = CreateApkBuilder ()) {
				string mappingFile = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, "mapping.txt");
				Assert.IsTrue (b.Build (proj), "build should have succeeded.");
				FileAssert.Exists (mappingFile, $"'{mappingFile}' should have been generated.");
			}
		}

		[Test]
		[NonParallelizable] // Commonly fails NuGet restore
		public void CheckIncludedAssemblies ([Values (false, true)] bool usesAssemblyStores)
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true
			};
			proj.SetProperty ("AndroidUseAssemblyStore", usesAssemblyStores.ToString ());
			proj.SetAndroidSupportedAbis ("armeabi-v7a");
			proj.PackageReferences.Add (new Package {
				Id = "Humanizer.Core",
				Version = "2.14.1",
			});
			proj.PackageReferences.Add (new Package {
				Id = "Humanizer.Core.es",
				Version = "2.14.1",
			});
			proj.MainActivity = proj.DefaultMainActivity
				.Replace ("//${USINGS}", @"using System;
using Humanizer;
using System.Globalization;")
				.Replace ("//${AFTER_ONCREATE}", @"var c = new CultureInfo (""es-ES"");
Console.WriteLine ($""{DateTime.UtcNow.AddHours(-30).Humanize(culture:c)}"");
//${AFTER_ONCREATE}");
			proj.OtherBuildItems.Add (new BuildItem ("Using", "System.Globalization"));
			proj.OtherBuildItems.Add (new BuildItem ("Using", "Humanizer"));

			var expectedFiles = new [] {
					"Java.Interop.dll",
					"Mono.Android.dll",
					"Mono.Android.Runtime.dll",
					"rc.bin",
					"System.Console.dll",
					"System.Private.CoreLib.dll",
					"System.Runtime.dll",
					"System.Runtime.InteropServices.dll",
					"System.Linq.dll",
					"UnnamedProject.dll",
					"_Microsoft.Android.Resource.Designer.dll",
					"Humanizer.dll",
					"es/Humanizer.resources.dll",
					"System.Collections.dll",
					"System.Collections.Concurrent.dll",
					"System.Text.RegularExpressions.dll",
			};

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "build should have succeeded.");
				var apk = Path.Combine (Root, b.ProjectDirectory,
						proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				var helper = new ArchiveAssemblyHelper (apk, usesAssemblyStores);
				List<string> existingFiles;
				List<string> missingFiles;
				List<string> additionalFiles;

				helper.Contains (expectedFiles, out existingFiles, out missingFiles, out additionalFiles);

				Assert.IsTrue (missingFiles == null || missingFiles.Count == 0,
				       string.Format ("The following Expected files are missing. {0}",
				       string.Join (Environment.NewLine, missingFiles)));

				Assert.IsTrue (additionalFiles == null || additionalFiles.Count == 0,
					string.Format ("Unexpected Files found! {0}",
					string.Join (Environment.NewLine, additionalFiles)));
			}
		}

		[Test]
		[Category ("SmokeTests")]
		[TestCase ("Test Me")]
		// testing characters as per https://www.compart.com/en/unicode/category/Zs
		[TestCase ("TestUnicodeSpace0020\u0020Me")]
		// TODO these break with AOT error on windows
		//[TestCase ("TestUnicodeSpace2000\u2000Me")]
		//[TestCase ("TestUnicodeSpace2009\u2009Me")]
		//[TestCase ("TestUnicodeSpace2002\u2002Me")]
		//[TestCase ("TestUnicodeSpace2007\u2007Me")]
		public void CheckProjectWithSpaceInNameWorks (string projectName)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
				ProjectName = projectName,
				RootNamespace = "Test.Me",
			};
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "build failed");
			}
		}

		[Test]
		public void CheckClassesDexIsIncluded ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			using (var b = CreateApkBuilder ()) {
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
		public void CheckIncludedNativeLibraries ([Values (true, false)] bool compressNativeLibraries)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.PackageReferences.Add(KnownPackages.SQLitePCLRaw_Core);
			proj.SetAndroidSupportedAbis ("x86");
			proj.SetProperty (proj.ReleaseProperties, "AndroidStoreUncompressedFileExtensions", compressNativeLibraries ? "" : "so");
			using (var b = CreateApkBuilder ()) {
				b.ThrowOnBuildFailure = false;
				Assert.IsTrue (b.Build (proj), "build failed");
				var apk = Path.Combine (Root, b.ProjectDirectory,
						proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				CompressionMethod method = compressNativeLibraries ? CompressionMethod.Deflate : CompressionMethod.Store;
				using (var zip = ZipHelper.OpenZip (apk)) {
					var libFiles = zip.Where (x => x.FullName.StartsWith("lib/", StringComparison.Ordinal) && !x.FullName.Equals("lib/", StringComparison.InvariantCultureIgnoreCase));
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

			var zipAlignPath = Path.Combine (GetPathToZipAlign (), IsWindows ? "zipalign.exe" : "zipalign");
			Assert.That (new FileInfo (zipAlignPath), Does.Exist, $"ZipAlign not found at {zipAlignPath}");
			Assert.That (RunCommand (zipAlignPath, $"-c -v -p 4 {apk}"), Is.True, $"{apk} does not contain page-aligned .so files");

			using (var zip = ZipHelper.OpenZip (apk)) {
				foreach (var entry in zip) {
					if (entry.FullName.EndsWith (".so", StringComparison.Ordinal)) {
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
						if (entry.FullName.EndsWith (".so", StringComparison.Ordinal) || entry.FullName.EndsWith (".bar", StringComparison.Ordinal)) {
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
						if (entry.FullName.EndsWith (".so", StringComparison.Ordinal) || entry.FullName.EndsWith (".bar", StringComparison.Ordinal)) {
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
				KnownPackages.Xamarin_Jetbrains_Annotations,
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
		public void MissingSatelliteAssemblyInLibrary ()
		{
			var path = Path.Combine ("temp", TestName);
			var lib = new XamarinAndroidLibraryProject {
				ProjectName = "Localization",
				OtherBuildItems = {
					new BuildItem ("EmbeddedResource", "Foo.resx") {
						TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancel</value></data>")
					},
				}
			};

			var languages = new string[] {"es", "de", "fr", "he", "it", "pl", "pt", "ru", "sl" };
			foreach (string lang in languages) {
				lib.OtherBuildItems.Add (
					new BuildItem ("EmbeddedResource", $"Foo.{lang}.resx") {
						TextContent = () => InlineData.ResxWithContents ($"<data name=\"CancelButton\"><value>{lang}</value></data>")
					}
				);
			}

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
				var helper = new ArchiveAssemblyHelper (apk);

				foreach (string lang in languages) {
					Assert.IsTrue (helper.Exists ($"assemblies/{lang}/{lib.ProjectName}.resources.dll"), $"Apk should contain satellite assembly for language '{lang}'!");
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
				var helper = new ArchiveAssemblyHelper (apk);
				Assert.IsTrue (helper.Exists ($"assemblies/es/{proj.ProjectName}.resources.dll"), "Apk should contain satellite assemblies!");
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
					var jar = "2965D0C9A2D5DB1E.jar";
					string expected = $"Ignoring jar entry AndroidManifest.xml from {jar}: the same file already exists in the apk";
					Assert.IsTrue (b.LastBuildOutput.ContainsText (expected), $"AndroidManifest.xml for {jar} should have been ignored.");
				}
			}
		}

		[Test]
		public void CheckExcludedFilesAreMissing ()
		{

			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.PackageReferences.Add (KnownPackages.Xamarin_Kotlin_StdLib_Common);
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var apk = Path.Combine (Root, b.ProjectDirectory,
					proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				string expected = $"Ignoring jar entry 'kotlin/Error.kotlin_metadata'";
				Assert.IsTrue (b.LastBuildOutput.ContainsText (expected), $"Error.kotlin_metadata should have been ignored.");
				using (var zip = ZipHelper.OpenZip (apk)) {
					Assert.IsFalse (zip.ContainsEntry ("kotlin/Error.kotlin_metadata"), "Error.kotlin_metadata should have been ignored.");
				}
			}
		}

		[Test]
		public void CheckExcludedFilesCanBeModified ()
		{

			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.PackageReferences.Add (KnownPackages.Xamarin_Kotlin_StdLib_Common);
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var apk = Path.Combine (Root, b.ProjectDirectory,
					proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				string expected = $"Ignoring jar entry 'kotlin/Error.kotlin_metadata'";
				Assert.IsTrue (b.LastBuildOutput.ContainsText (expected), $"Error.kotlin_metadata should have been ignored.");
				using (var zip = ZipHelper.OpenZip (apk)) {
					Assert.IsFalse (zip.ContainsEntry ("kotlin/Error.kotlin_metadata"), "Error.kotlin_metadata should have been ignored.");
				}
				proj.OtherBuildItems.Add (new BuildItem ("AndroidPackagingOptionsExclude") {
					Remove = () => "$([MSBuild]::Escape('*.kotlin*'))",
				});
				Assert.IsTrue (b.Clean (proj), "Clean should have succeeded.");
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				using (var zip = ZipHelper.OpenZip (apk)) {
					Assert.IsTrue (zip.ContainsEntry ("kotlin/Error.kotlin_metadata"), "Error.kotlin_metadata should have been included.");
				}
			}
		}

		[Test]
		public void CheckIncludedFilesArePresent ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.PackageReferences.Add (KnownPackages.Xamarin_Kotlin_Reflect);
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var apk = Path.Combine (Root, b.ProjectDirectory,
					proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				using (var zip = ZipHelper.OpenZip (apk)) {
					Assert.IsTrue (zip.ContainsEntry ("kotlin/reflect/reflect.kotlin_builtins"), "reflect.kotlin_builtins should have been included.");
				}
			}
		}

		[Test]
		[TestCase (1, -1)]
		[TestCase (5, -1)]
		[TestCase (50, -1)]
		[TestCase (100, -1)]
		[TestCase (512, -1)]
		[TestCase (1024, -1)]
		[TestCase (-1, 1)]
		[TestCase (-1, 5)]
		[TestCase (-1, 10)]
		[TestCase (-1, 100)]
		[TestCase (-1, 200)]
		public void BuildApkWithZipFlushLimits (int filesLimit, int sizeLimit)
		{
			var proj = new XamarinFormsAndroidApplicationProject {
				IsRelease = false,
			};
			proj.SetProperty ("EmbedAssembliesIntoApk", "true");
			if (filesLimit > 0)
				proj.SetProperty ("_ZipFlushFilesLimit", filesLimit.ToString ());
			if (sizeLimit > 0)
				proj.SetProperty ("_ZipFlushSizeLimit", (sizeLimit * 1024 * 1024).ToString ());
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

			}
		}

		[Test]
		public void ExtractNativeLibsTrue ()
		{
			var proj = new XamarinAndroidApplicationProject {
				// This combination produces android:extractNativeLibs="false" by default
				SupportedOSPlatformVersion = "23",
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
						if (entry.FullName.EndsWith (".so", StringComparison.Ordinal)) {
							AssertCompression (entry, compressed: true);
						}
					}
				}
			}
		}

		[Test]
		public void DefaultItems ()
		{
			void CreateEmptyFile (string path)
			{
				Directory.CreateDirectory (Path.GetDirectoryName (path));
				File.WriteAllText (path, contents: "");
			}

			var proj = new XamarinAndroidApplicationProject () {
				EnableDefaultItems = true,
			};

			var builder = CreateApkBuilder ();
			builder.Save (proj);
			proj.ShouldPopulate = false;

			// Build error -> no nested sub-directories in Resources
			CreateEmptyFile (Path.Combine (Root, builder.ProjectDirectory, "Resources", "drawable", "foo", "bar.png"));
			CreateEmptyFile (Path.Combine (Root, builder.ProjectDirectory, "Resources", "raw", "foo", "bar.png"));

			// Build error -> no files/directories that start with .
			CreateEmptyFile (Path.Combine (Root, builder.ProjectDirectory, "Resources", "raw", ".DS_Store"));
			CreateEmptyFile (Path.Combine (Root, builder.ProjectDirectory, "Assets", ".DS_Store"));
			CreateEmptyFile (Path.Combine (Root, builder.ProjectDirectory, "Assets", ".svn", "foo.txt"));

			// Files that should work
			CreateEmptyFile (Path.Combine (Root, builder.ProjectDirectory, "Resources", "raw", "foo.txt"));
			CreateEmptyFile (Path.Combine (Root, builder.ProjectDirectory, "Assets", "foo", "bar.txt"));

			Assert.IsTrue (builder.Build (proj), "`dotnet build` should succeed");

			var apkPath = Path.Combine (Root, builder.ProjectDirectory, proj.OutputPath, $"{proj.PackageName}-Signed.apk");
			FileAssert.Exists (apkPath);
			using (var apk = ZipHelper.OpenZip (apkPath)) {
				apk.AssertContainsEntry (apkPath, "res/raw/foo.txt");
				apk.AssertContainsEntry (apkPath, "assets/foo/bar.txt");
			}
		}

	}
}
