using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Build.Framework;
using Mono.Cecil;
using NUnit.Framework;
using Xamarin.Android.Tasks;
using Xamarin.Android.Tools;
using Xamarin.ProjectTools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Build.Tests
{
	[Parallelizable (ParallelScope.Children)]
	public partial class BuildTest : BaseTest
	{
		[Test]
		[Category ("SmokeTests")]
		[TestCaseSource (nameof (DotNetBuildSource))]
		[NonParallelizable] // On MacOS, parallel /restore causes issues
		public void DotNetBuild (string runtimeIdentifiers, bool isRelease, bool aot, bool usesAssemblyStore)
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = isRelease,
				EnableDefaultItems = true,
				ExtraNuGetConfigSources = {
					// Microsoft.AspNetCore.Components.WebView is not in dotnet-public
					"https://api.nuget.org/v3/index.json",
				},
				PackageReferences = {
					new Package { Id = "Xamarin.AndroidX.AppCompat", Version = "1.3.1.1" },
					// Using * here, so we explicitly get newer packages
					new Package { Id = "Microsoft.AspNetCore.Components.WebView", Version = "8.0.*" },
					new Package { Id = "Microsoft.Extensions.FileProviders.Embedded", Version = "8.0.*" },
					new Package { Id = "Microsoft.JSInterop", Version = "8.0.*" },
					new Package { Id = "System.Text.Json", Version = "8.0.*" },
				},
				Sources = {
					new BuildItem ("EmbeddedResource", "Resource.resx") {
						TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancel</value></data>"),
					},
					new BuildItem ("EmbeddedResource", "Resource.es.resx") {
						TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancelar</value></data>")
					},
					new AndroidItem.TransformFile ("Transforms.xml") {
						// Remove two methods that introduced warnings:
						// Com.Balysv.Material.Drawable.Menu.MaterialMenuView.cs(214,30): warning CS0114: 'MaterialMenuView.OnRestoreInstanceState(IParcelable)' hides inherited member 'View.OnRestoreInstanceState(IParcelable?)'. To make the current member override that implementation, add the override keyword. Otherwise add the new keyword.
						// Com.Balysv.Material.Drawable.Menu.MaterialMenuView.cs(244,56): warning CS0114: 'MaterialMenuView.OnSaveInstanceState()' hides inherited member 'View.OnSaveInstanceState()'. To make the current member override that implementation, add the override keyword. Otherwise add the new keyword.
						TextContent = () => "<metadata><remove-node path=\"/api/package[@name='com.balysv.material.drawable.menu']/class[@name='MaterialMenuView']/method[@name='onRestoreInstanceState']\" /><remove-node path=\"/api/package[@name='com.balysv.material.drawable.menu']/class[@name='MaterialMenuView']/method[@name='onSaveInstanceState']\" /></metadata>",
					},
					new AndroidItem.AndroidLibrary ("material-menu-1.1.0.aar") {
						WebContent = "https://repo1.maven.org/maven2/com/balysv/material-menu/1.1.0/material-menu-1.1.0.aar"
					},
				}
			};
			proj.MainActivity = proj.DefaultMainActivity.Replace (": Activity", ": AndroidX.AppCompat.App.AppCompatActivity")
				.Replace ("//${AFTER_ONCREATE}", @"button.Text = Resource.CancelButton;");
			proj.SetProperty ("AndroidUseAssemblyStore", usesAssemblyStore.ToString ());
			proj.SetProperty ("RunAOTCompilation", aot.ToString ());
			proj.OtherBuildItems.Add (new AndroidItem.InputJar ("javaclasses.jar") {
				BinaryContent = () => ResourceData.JavaSourceJarTestJar,
			});
			proj.OtherBuildItems.Add (new BuildItem ("JavaSourceJar", "javaclasses-sources.jar") {
				BinaryContent = () => ResourceData.JavaSourceJarTestSourcesJar,
			});
			proj.OtherBuildItems.Add (new BuildItem ("EmbeddedResource", default (Func<string>)) {
						Update = () =>  "Resource.resx",
						TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancel</value></data>"),
						Metadata = {
							{ "Generator", "ResXFileCodeGenerator" },
							{ "LastGenOutput", "Resource.designer.cs" }
						},
			});
			proj.OtherBuildItems.Add (new BuildItem ("Compile", default (Func<string>)) {
						TextContent = () => InlineData.DesignerWithContents (proj.ProjectName, "Resource", "internal partial", new string[] {"CancelButton"}),
						Update = () =>  "Resource.designer.cs",
						Metadata = {
							{ "DependentUpon", "Resource.resx" },
						},
					});
			proj.AndroidJavaSources.Add (new AndroidItem.AndroidJavaSource ("JavaSourceTestExtension.java") {
				Encoding = Encoding.ASCII,
				TextContent = () => ResourceData.JavaSourceTestExtension,
				Metadata = { { "Bind", "True"} },
			});
			if (!runtimeIdentifiers.Contains (";")) {
				proj.SetProperty (KnownProperties.RuntimeIdentifier, runtimeIdentifiers);
			} else {
				proj.SetProperty (KnownProperties.RuntimeIdentifiers, runtimeIdentifiers);
			}

			var builder = CreateApkBuilder ();
			Assert.IsTrue (builder.Build (proj), "`dotnet build` should succeed");
			builder.AssertHasNoWarnings ();

			var outputPath = Path.Combine (Root, builder.ProjectDirectory, proj.OutputPath);
			var intermediateOutputPath = Path.Combine (Root, builder.ProjectDirectory, proj.IntermediateOutputPath);
			if (!runtimeIdentifiers.Contains (";")) {
				outputPath = Path.Combine (outputPath, runtimeIdentifiers);
				intermediateOutputPath = Path.Combine (intermediateOutputPath, runtimeIdentifiers);
			}

			var files = Directory.EnumerateFileSystemEntries (outputPath)
				.Select (Path.GetFileName)
				.OrderBy (f => f, StringComparer.OrdinalIgnoreCase)
				.ToArray ();
			var expectedFiles = new List<string> {
				$"{proj.PackageName}-Signed.apk",
				"es",
				$"{proj.ProjectName}.dll",
				$"{proj.ProjectName}.pdb",
				$"{proj.ProjectName}.runtimeconfig.json",
				$"{proj.ProjectName}.xml",
			};
			if (isRelease) {
				expectedFiles.Add ($"{proj.PackageName}.aab");
				expectedFiles.Add ($"{proj.PackageName}-Signed.aab");
			} else {
				expectedFiles.Add ($"{proj.PackageName}.apk");
				expectedFiles.Add ($"{proj.PackageName}-Signed.apk.idsig");
			}

			expectedFiles.Sort(StringComparer.OrdinalIgnoreCase);

			CollectionAssert.AreEquivalent (expectedFiles, files, $"Expected: {string.Join (";", expectedFiles)}\n   Found: {string.Join (";", files)}");

			var assemblyPath = Path.Combine (outputPath, $"{proj.ProjectName}.dll");
			FileAssert.Exists (assemblyPath);
			using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
				var typeName = "Com.Xamarin.Android.Test.Msbuildtest.JavaSourceJarTest";
				Assert.IsNotNull (assembly.MainModule.GetType (typeName), $"{assemblyPath} should contain {typeName}");
				typeName = "Com.Balysv.Material.Drawable.Menu.MaterialMenuView";
				Assert.IsNotNull (assembly.MainModule.GetType (typeName), $"{assemblyPath} should contain {typeName}");
				typeName = "Com.Xamarin.Android.Test.Msbuildtest.JavaSourceTestExtension";
				Assert.IsNotNull (assembly.MainModule.GetType (typeName), $"{assemblyPath} should contain {typeName}");
			}

			var rids = runtimeIdentifiers.Split (';');

			// Check AndroidManifest.xml
			var manifestPath = Path.Combine (intermediateOutputPath, "android", "AndroidManifest.xml");
			FileAssert.Exists (manifestPath);
			var manifest = XDocument.Load (manifestPath);
			XNamespace ns = "http://schemas.android.com/apk/res/android";
			var uses_sdk = manifest.Root.Element ("uses-sdk");
			Assert.AreEqual ("21", uses_sdk.Attribute (ns + "minSdkVersion").Value);
			Assert.AreEqual (XABuildConfig.AndroidDefaultTargetDotnetApiLevel.ToString(),
				uses_sdk.Attribute (ns + "targetSdkVersion").Value);

			bool expectEmbeddedAssembies = !(TestEnvironment.CommercialBuildAvailable && !isRelease);
			var apkPath = Path.Combine (outputPath, $"{proj.PackageName}-Signed.apk");
			FileAssert.Exists (apkPath);
			var helper = new ArchiveAssemblyHelper (apkPath, usesAssemblyStore, rids);
			helper.AssertContainsEntry ($"assemblies/{proj.ProjectName}.dll", shouldContainEntry: expectEmbeddedAssembies);
			helper.AssertContainsEntry ($"assemblies/{proj.ProjectName}.pdb", shouldContainEntry: !TestEnvironment.CommercialBuildAvailable && !isRelease);
			helper.AssertContainsEntry ($"assemblies/Mono.Android.dll",        shouldContainEntry: expectEmbeddedAssembies);
			helper.AssertContainsEntry ($"assemblies/es/{proj.ProjectName}.resources.dll", shouldContainEntry: expectEmbeddedAssembies);
			foreach (var abi in rids.Select (AndroidRidAbiHelper.RuntimeIdentifierToAbi)) {
				helper.AssertContainsEntry ($"lib/{abi}/libmonodroid.so");
				helper.AssertContainsEntry ($"lib/{abi}/libmonosgen-2.0.so");
				if (rids.Length > 1) {
					helper.AssertContainsEntry ($"assemblies/{abi}/System.Private.CoreLib.dll",        shouldContainEntry: expectEmbeddedAssembies);
				} else {
					helper.AssertContainsEntry ("assemblies/System.Private.CoreLib.dll",        shouldContainEntry: expectEmbeddedAssembies);
				}
				if (aot) {
					helper.AssertContainsEntry ($"lib/{abi}/libaot-{proj.ProjectName}.dll.so");
					helper.AssertContainsEntry ($"lib/{abi}/libaot-Mono.Android.dll.so");
				}
			}
		}

		static object [] MonoComponentMaskChecks () => new object [] {
			new object[] {
				true,  // enableProfiler
				true,  // useInterpreter
				true,  // debugBuild
				0x07U, // expectedMask
			},

			new object[] {
				true,  // enableProfiler
				false, // useInterpreter
				true,  // debugBuild
				0x05U, // expectedMask
			},

			new object[] {
				false, // enableProfiler
				false, // useInterpreter
				true,  // debugBuild
				0x01U, // expectedMask
			},

			new object[] {
				true,  // enableProfiler
				false, // useInterpreter
				false, // debugBuild
				0x04U, // expectedMask
			},
		};

		[Test]
		[TestCaseSource (nameof (MonoComponentMaskChecks))]
		public void CheckMonoComponentsMask (bool enableProfiler, bool useInterpreter, bool debugBuild, uint expectedMask)
		{
			var proj = new XamarinFormsAndroidApplicationProject () {
				IsRelease = !debugBuild,
			};

			proj.SetProperty (proj.ActiveConfigurationProperties, "AndroidEnableProfiler", enableProfiler.ToString ());
			proj.SetProperty (proj.ActiveConfigurationProperties, "UseInterpreter", useInterpreter.ToString ());

			var abis = new [] { "armeabi-v7a", "x86" };
			proj.SetAndroidSupportedAbis (abis);

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				b.AssertHasNoWarnings ();
				string objPath = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);

				List<EnvironmentHelper.EnvironmentFile> envFiles = EnvironmentHelper.GatherEnvironmentFiles (objPath, String.Join (";", abis), true);
				EnvironmentHelper.ApplicationConfig app_config = EnvironmentHelper.ReadApplicationConfig (envFiles);
				Assert.That (app_config, Is.Not.Null, "application_config must be present in the environment files");
				Assert.IsTrue (app_config.mono_components_mask == expectedMask, "Expected Mono Components mask 0x{expectedMask:x}, got 0x{app_config.mono_components_mask:x}");
			}
		}

		static object [] CheckAssemblyCountsSource = new object [] {
			new object[] {
				/*isRelease*/ false,
				/*aot*/       false,
			},
			new object[] {
				/*isRelease*/ true,
				/*aot*/       false,
			},
			new object[] {
				/*isRelease*/ true,
				/*aot*/       true,
			},
		};

		[Test]
		[TestCaseSource (nameof (CheckAssemblyCountsSource))]
		[NonParallelizable]
		public void CheckAssemblyCounts (bool isRelease, bool aot)
		{
			var proj = new XamarinFormsAndroidApplicationProject {
				IsRelease = isRelease,
				EmbedAssembliesIntoApk = true,
				AotAssemblies = aot,
			};

			var abis = new [] { "armeabi-v7a", "x86" };
			proj.SetRuntimeIdentifiers (abis);
			proj.SetProperty (proj.ActiveConfigurationProperties, "AndroidUseAssemblyStore", "True");

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				string objPath = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);

				List<EnvironmentHelper.EnvironmentFile> envFiles = EnvironmentHelper.GatherEnvironmentFiles (objPath, String.Join (";", abis), true);
				EnvironmentHelper.ApplicationConfig app_config = EnvironmentHelper.ReadApplicationConfig (envFiles);
				Assert.That (app_config, Is.Not.Null, "application_config must be present in the environment files");

				if (aot) {
					foreach (var env in envFiles) {
						StringAssert.Contains ("libaot-Mono.Android.dll.so", File.ReadAllText (env.Path));
					}
				}

				string apk = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				var helper = new ArchiveAssemblyHelper (apk, useAssemblyStores: true);

				foreach (string abi in abis) {
					AndroidTargetArch arch = MonoAndroidHelper.AbiToTargetArch (abi);
					Assert.AreEqual (
						app_config.number_of_assemblies_in_apk,
						helper.GetNumberOfAssemblies (arch: arch),
						$"Assembly count must be equal between ApplicationConfig and the archive contents for architecture {arch} (ABI: {abi})"
					);
				}
			}
		}

		// DotNet fails, see https://github.com/dotnet/runtime/issues/65484
		// Enable the commented out signature (and AOT) once the above is fixed
		[Test]
		public void SmokeTestBuildWithSpecialCharacters ([Values (false, true)] bool forms, [Values (false /*, true*/)] bool aot)
		{
			var testName = "テスト";

			var rootPath = Path.Combine (Root, "temp", TestName);
			var proj = forms ?
				new XamarinFormsAndroidApplicationProject () :
				new XamarinAndroidApplicationProject ();
			proj.ProjectName = testName;
			proj.IsRelease = true;
			proj.AotAssemblies = aot;

			using (var builder = CreateApkBuilder (Path.Combine (rootPath, proj.ProjectName))){
				Assert.IsTrue (builder.Build (proj), "Build should have succeeded.");
			}
		}

		public static string GetLinkedPath (ProjectBuilder builder, bool isRelease, string filename)
		{
			return isRelease ?
				builder.Output.GetIntermediaryPath (Path.Combine ("android-arm64", "linked", filename)) :
				builder.Output.GetIntermediaryPath (Path.Combine ("android", "assets", filename));
		}

		[Test]
		[TestCaseSource (nameof (RuntimeChecks))]
		public void CheckWhichRuntimeIsIncluded (string supportedAbi, bool debugSymbols, bool? optimize, bool? embedAssemblies, string expectedRuntime) {
			var proj = new XamarinAndroidApplicationProject ();
			proj.SetAndroidSupportedAbis (supportedAbi);
			proj.SetProperty (proj.ActiveConfigurationProperties, "DebugSymbols", debugSymbols);
			if (optimize.HasValue)
				proj.SetProperty (proj.ActiveConfigurationProperties, "Optimize", optimize.Value);
			else
				proj.RemoveProperty (proj.ActiveConfigurationProperties, "Optimize");
			if (embedAssemblies.HasValue)
				proj.SetProperty (proj.ActiveConfigurationProperties, KnownProperties.EmbedAssembliesIntoApk, embedAssemblies.Value);
			else
				proj.RemoveProperty (proj.ActiveConfigurationProperties, KnownProperties.EmbedAssembliesIntoApk);
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				var runtimeInfo = b.GetSupportedRuntimes ();
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var apkPath = Path.Combine (Root, b.ProjectDirectory,
					proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				using (var apk = ZipHelper.OpenZip (apkPath)) {
					var runtime = runtimeInfo.FirstOrDefault (x => x.Abi == supportedAbi && x.Runtime == expectedRuntime);
					Assert.IsNotNull (runtime, "Could not find the expected runtime.");
					var inApk = ZipHelper.ReadFileFromZip (apk, $"lib/{supportedAbi}/{runtime.Name}");
					var inApkRuntime = runtimeInfo.FirstOrDefault (x => x.Abi == supportedAbi && x.Size == inApk.Length);
					Assert.IsNotNull (inApkRuntime, "Could not find the actual runtime used.");
				}
			}
		}

		[Test]
		public void CheckItemMetadata ([Values (true, false)] bool isRelease)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = isRelease,
				Imports = {
					new Import (() => "My.Test.target") {
						TextContent = () => @"<Project xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
	<Target Name=""CustomTarget"" AfterTargets=""UpdateAndroidAssets"" BeforeTargets=""UpdateAndroidInterfaceProxies"" >
		<Message Text=""Foo""/>
		<Message Text=""@(_AndroidAssetsDest->'%(CustomData)')"" />
	</Target>
	<Target Name=""CustomTarget2"" AfterTargets=""UpdateAndroidResources"" >
		<Message Text=""@(_AndroidResourceDest->'%(CustomData)')"" />
	</Target>
</Project>
						"
					},
				},
				OtherBuildItems = {
					new AndroidItem.AndroidAsset (() => "Assets\\foo.txt") {
						TextContent = () => "Foo",
						MetadataValues = "CustomData=AssetMetaDataOK"
					},
				}
			};

			var mainAxml = proj.AndroidResources.First (x => x.Include () == "Resources\\layout\\Main.axml");
			mainAxml.MetadataValues = "CustomData=ResourceMetaDataOK";

			using (var builder = CreateApkBuilder (string.Format ("temp/CheckItemMetadata_{0}", isRelease))) {
				builder.Verbosity = LoggerVerbosity.Detailed;
				builder.Build (proj);
				StringAssertEx.Contains ("AssetMetaDataOK", builder.LastBuildOutput, "Metadata was not copied for AndroidAsset");
				StringAssertEx.Contains ("ResourceMetaDataOK", builder.LastBuildOutput, "Metadata was not copied for AndroidResource");
			}
		}

		// Context https://bugzilla.xamarin.com/show_bug.cgi?id=29706
		[Test]
		public void CheckLogicalNamePathSeperators ([Values (false, true)] bool isRelease, [Values (false, true)] bool useDesignerAssembly)
		{
			var illegalSeperator = IsWindows ? "/" : @"\";
			var dll = new XamarinAndroidLibraryProject () {
				ProjectName = "Library1",
				IsRelease = isRelease,
				AndroidResources = {
					new AndroidItem.AndroidResource (() => "Resources\\Test\\Test2.png") {
						BinaryContent = () => XamarinAndroidApplicationProject.icon_binary_mdpi,
						MetadataValues = string.Format ("LogicalName=drawable{0}foo2.png", illegalSeperator)
					},
				},
			};
			var proj = new XamarinAndroidApplicationProject () {
				ProjectName = "Application1",
				IsRelease = isRelease,
				AndroidResources = {
					new AndroidItem.AndroidResource (() => "Resources\\Test\\Test.png") {
						BinaryContent = () => XamarinAndroidApplicationProject.icon_binary_mdpi,
						MetadataValues = string.Format ("LogicalName=drawable{0}foo.png", illegalSeperator)
					},
				},
				References = {
					new BuildItem ("ProjectReference","..\\Library1\\Library1.csproj"),
				},
			};
			if (!useDesignerAssembly)
				dll.SetProperty ("AndroidUseDesignerAssembly", useDesignerAssembly.ToString ());
			proj.SetProperty ("AndroidUseDesignerAssembly", useDesignerAssembly.ToString ());
			var path = Path.Combine ("temp", TestName);
			using (var b = CreateDllBuilder (Path.Combine (path, dll.ProjectName))) {
				Assert.IsTrue (b.Build (dll), "Build should have succeeded.");
				using (var builder = CreateApkBuilder (Path.Combine (path, proj.ProjectName), isRelease)) {
					Assert.IsTrue (builder.Build (proj), "Build should have succeeded");
					string resource_designer_cs = GetResourceDesignerPath (builder, proj);
					var contents = GetResourceDesignerText (proj, resource_designer_cs);
					StringAssert.Contains ("public const int foo =", contents);
					StringAssert.Contains ("public const int foo2 =", contents);
				}
			}
		}

		[Test]
		public void ApplicationJavaClassProperties ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.SetProperty ("AndroidApplicationJavaClass", "android.test.mock.MockApplication");
			var builder = CreateApkBuilder ("temp/ApplicationJavaClassProperties");
			builder.Build (proj);
			var appsrc = File.ReadAllText (Path.Combine (Root, builder.ProjectDirectory, "obj", "Debug", "android", "AndroidManifest.xml"));
			Assert.IsTrue (appsrc.Contains ("android.test.mock.MockApplication"), "app class");
			builder.Dispose ();
		}

		[Test]
		public void ApplicationIdPlaceholder ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.AndroidManifest = proj.AndroidManifest.Replace ("</application>", "<provider android:name='${applicationId}' android:authorities='example' /></application>");
			using (var builder = CreateApkBuilder ("temp/ApplicationIdPlaceholder")) {
				builder.Build (proj);
				var manifest = XDocument.Load (Path.Combine (Root, builder.ProjectDirectory, "obj", "Debug", "android", "AndroidManifest.xml"));
				var namespaceResolver = new XmlNamespaceManager (new NameTable ());
				namespaceResolver.AddNamespace ("android", "http://schemas.android.com/apk/res/android");
				var element = manifest.XPathSelectElement ($"/manifest/application/provider[@android:name='{proj.PackageName}']", namespaceResolver);
				Assert.IsNotNull (element, "placeholder not replaced");
			}
		}

		[Test]
		public void AarContentExtraction ()
		{
			var aar = new AndroidItem.AndroidAarLibrary ("Jars\\android-crop-1.0.1.aar") {
				// https://mvnrepository.com/artifact/com.soundcloud.android/android-crop/1.0.1
				WebContent = "https://repo1.maven.org/maven2/com/soundcloud/android/android-crop/1.0.1/android-crop-1.0.1.aar"
			};
			var proj = new XamarinAndroidApplicationProject () {
				OtherBuildItems = {
					aar,
					new AndroidItem.AndroidAarLibrary ("fragment-1.2.2.aar") {
						WebContent = "https://maven.google.com/androidx/fragment/fragment/1.2.2/fragment-1.2.2.aar"
					}
				},
			};
			using (var builder = CreateApkBuilder ()) {
				Assert.IsTrue (builder.Build (proj), "Build should have succeeded");
				var cache = builder.Output.GetIntermediaryPath ("libraryprojectimports.cache");
				Assert.IsTrue (File.Exists (cache), $"{cache} should exist.");
				var assemblyIdentityMap = builder.Output.GetAssemblyMapCache ();
				var libraryProjects = Path.Combine (Root, builder.ProjectDirectory, proj.IntermediateOutputPath, "lp");
				FileAssert.Exists (Path.Combine (libraryProjects, assemblyIdentityMap.IndexOf ("android-crop-1.0.1.aar").ToString (), "jl", "classes.jar"),
					"classes.jar was not extracted from the aar.");
				Assert.IsTrue (builder.Build (proj), "Build should have succeeded");
				Assert.IsTrue (builder.Output.IsTargetSkipped ("_ResolveLibraryProjectImports"),
					"_ResolveLibraryProjectImports should not have run.");

				var doc = XDocument.Load (cache);
				var expectedCount = doc.Elements ("Paths").Elements ("ResolvedResourceDirectories").Count ();

				aar.Timestamp = DateTimeOffset.UtcNow.Add (TimeSpan.FromMinutes (2));
				Assert.IsTrue (builder.Build (proj), "Build should have succeeded");
				Assert.IsFalse (builder.Output.IsTargetSkipped ("_ResolveLibraryProjectImports"),
					"_ResolveLibraryProjectImports should have run.");

				doc = XDocument.Load (cache);
				var count = doc.Elements ("Paths").Elements ("ResolvedResourceDirectories").Count ();
				Assert.AreEqual (expectedCount, count, "The same number of resource directories should have been resolved.");

				//NOTE: the designer requires the paths to be full paths
				foreach (var paths in doc.Elements ("Paths")) {
					foreach (var element in paths.Elements ("Path")) {
						var path = element.Value;
						if (!string.IsNullOrEmpty (path)) {
							Assert.IsTrue (path == Path.GetFullPath (path), $"`{path}` is not a full path!");
						}
					}
				}
				Assert.IsFalse (Directory.EnumerateFiles (libraryProjects, "lint.jar", SearchOption.AllDirectories).Any (),
					"`lint.jar` should not be extracted!");
			}
		}

#pragma warning disable 414
		public static object [] GeneratorValidateEventNameArgs = new object [] {
			new object [] { false, true, string.Empty, string.Empty },
			new object [] { false, false, "<attr path=\"/api/package/class[@name='Test']/method[@name='setOn123Listener']\" name='eventName'>OneTwoThree</attr>", string.Empty },
			new object [] { true, true, string.Empty, "String s" },
		};
#pragma warning restore 414

		[Test]
		[TestCaseSource (nameof (GeneratorValidateEventNameArgs))]
		public void GeneratorValidateEventName (bool failureExpected, bool warningExpected, string metadataFixup, string methodArgs)
		{
			string java = @"
package com.xamarin.testing;

public class Test
{
	public void setOnAbcListener (OnAbcListener listener)
	{
	}

	public void setOn123Listener (On123Listener listener)
	{
	}

	public interface OnAbcListener
	{
		public void onAbc ();
	}

	public interface On123Listener
	{
		public void onAbc (%%ARGS%%);
	}
}
".Replace ("%%ARGS%%", methodArgs);
			var path = Path.Combine (Root, "temp", $"GeneratorValidateEventName{failureExpected}{warningExpected}");
			var javaDir = Path.Combine (path, "java", "com", "xamarin", "testing");
			if (Directory.Exists (javaDir))
				Directory.Delete (javaDir, true);
			Directory.CreateDirectory (javaDir);
			var proj = new XamarinAndroidBindingProject () {
				AndroidClassParser = "class-parse",
			};
			proj.MetadataXml = "<metadata>" + metadataFixup + "</metadata>";
			proj.Jars.Add (new AndroidItem.EmbeddedJar (Path.Combine ("java", "test.jar")) {
				BinaryContent = new JarContentBuilder () {
					BaseDirectory = Path.Combine (path, "java"),
					JarFileName = "test.jar",
					JavaSourceFileName = Path.Combine ("com", "xamarin", "testing", "Test.java"),
					JavaSourceText = java
				}.Build
			});
			using (var builder = CreateDllBuilder (path, false, false)) {
				bool result = false;
				try {
					result = builder.Build (proj);
					Assert.AreEqual (warningExpected, builder.LastBuildOutput.ContainsText ("warning BG8504"), "warning BG8504 is expected: " + warningExpected);
				} catch (FailedBuildException) {
					if (!failureExpected)
						throw;
				}
				Assert.AreEqual (failureExpected, !result, "Should build fail?");
			}
		}

#pragma warning disable 414
		public static object [] GeneratorValidateMultiMethodEventNameArgs = new object [] {
			new object [] { false, "BG8505", string.Empty, string.Empty },
			new object [] { false, null, "<attr path=\"/api/package/interface[@name='Test.OnFooListener']/method[@name='on123']\" name='eventName'>One23</attr>", string.Empty },
			new object [] { false, null, @"
					<attr path=""/api/package/interface[@name='Test.OnFooListener']/method[@name='on123']"" name='eventName'>One23</attr>
					<attr path=""/api/package/interface[@name='Test.OnFooListener']/method[@name='on123']"" name='argsType'>OneTwoThreeEventArgs</attr>
				", "String s" },
			new object [] { true, "BG8504", string.Empty, "String s" },
		};
#pragma warning restore 414

		[Test]
		[TestCaseSource (nameof (GeneratorValidateMultiMethodEventNameArgs))]
		public void GeneratorValidateMultiMethodEventName (bool failureExpected, string expectedWarning, string metadataFixup, string methodArgs)
		{
			string java = @"
package com.xamarin.testing;

public class Test
{
	public void setOnFooListener (OnFooListener listener)
	{
	}

	public interface OnFooListener
	{
		public void onAbc ();
		public void on123 (%%ARGS%%);
	}
}
".Replace ("%%ARGS%%", methodArgs);
			var path = Path.Combine (Root, "temp", $"GeneratorValidateMultiMethodEventName{failureExpected}{expectedWarning}{methodArgs}");
			var javaDir = Path.Combine (path, "java", "com", "xamarin", "testing");
			if (Directory.Exists (javaDir))
				Directory.Delete (javaDir, true);
			Directory.CreateDirectory (javaDir);
			var proj = new XamarinAndroidBindingProject () {
				AndroidClassParser = "class-parse",
			};
			proj.MetadataXml = "<metadata>" + metadataFixup + "</metadata>";
			proj.Jars.Add (new AndroidItem.EmbeddedJar (Path.Combine ("java", "test.jar")) {
				BinaryContent = new JarContentBuilder () {
					BaseDirectory = Path.Combine (path, "java"),
					JarFileName = "test.jar",
					JavaSourceFileName = Path.Combine ("com", "xamarin", "testing", "Test.java"),
					JavaSourceText = java
				}.Build
			});
			using (var builder = CreateDllBuilder (path, false, false)) {
				try {
					builder.Build (proj);
					if (failureExpected)
						Assert.Fail ("Build should fail.");
					if (expectedWarning == null)
						Assert.IsFalse (builder.LastBuildOutput.ContainsText ("warning BG850"), "warning BG850* is NOT expected");
					else
						Assert.IsTrue (builder.LastBuildOutput.ContainsText ("warning " + expectedWarning), "warning " + expectedWarning + " is expected.");
				} catch (FailedBuildException) {
					if (!failureExpected)
						throw;
				}
			}
		}

		[Test]
		[Category ("AOT")]
		[NonParallelizable]
		public void BuildApplicationWithSpacesInPath ([Values (true, false)] bool enableMultiDex, [Values ("", "r8")] string linkTool)
		{
			var folderName = $"BuildReleaseApp AndÜmläüts({enableMultiDex}{linkTool})";
			var lib = new XamarinAndroidLibraryProject {
				IsRelease = true,
				ProjectName = "Library1"
			};
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
				AotAssemblies = true,
				LinkTool = linkTool,
				References = { new BuildItem ("ProjectReference", $"..\\{folderName}Library1\\Library1.csproj") }
			};
			proj.OtherBuildItems.Add (new BuildItem ("AndroidJavaLibrary", "Hello (World).jar") { BinaryContent = () => Convert.FromBase64String (@"
UEsDBBQACAgIAMl8lUsAAAAAAAAAAAAAAAAJAAQATUVUQS1JTkYv/soAAAMAUEsHCAAAAAACAAAAA
AAAAFBLAwQUAAgICADJfJVLAAAAAAAAAAAAAAAAFAAAAE1FVEEtSU5GL01BTklGRVNULk1G803My0
xLLS7RDUstKs7Mz7NSMNQz4OVyLkpNLElN0XWqBAlY6BnEG5oaKWj4FyUm56QqOOcXFeQXJZYA1Wv
ycvFyAQBQSwcIbrokAkQAAABFAAAAUEsDBBQACAgIAIJ8lUsAAAAAAAAAAAAAAAASAAAAc2FtcGxl
L0hlbGxvLmNsYXNzO/Vv1z4GBgYTBkEuBhYGXg4GPnYGfnYGAUYGNpvMvMwSO0YGZg3NMEYGFuf8l
FRGBn6fzLxUv9LcpNSikMSkHKAIa3l+UU4KI4OIhqZPVmJZon5OYl66fnBJUWZeujUjA1dwfmlRcq
pbJkgtl0dqTk6+HkgZDwMrAxvQFrCIIiMDT3FibkFOqj6Yz8gggDDKPykrNbmEQZGBGehCEGBiYAR
pBpLsQJ4skGYE0qxa2xkYNwIZjAwcQJINIggkORm4oEqloUqZhZg2oClkB5LcYLN5AFBLBwjQMrpO
0wAAABMBAABQSwECFAAUAAgICADJfJVLAAAAAAIAAAAAAAAACQAEAAAAAAAAAAAAAAAAAAAATUVUQ
S1JTkYv/soAAFBLAQIUABQACAgIAMl8lUtuuiQCRAAAAEUAAAAUAAAAAAAAAAAAAAAAAD0AAABNRV
RBLUlORi9NQU5JRkVTVC5NRlBLAQIUABQACAgIAIJ8lUvQMrpO0wAAABMBAAASAAAAAAAAAAAAAAA
AAMMAAABzYW1wbGUvSGVsbG8uY2xhc3NQSwUGAAAAAAMAAwC9AAAA1gEAAAAA") });
			if (enableMultiDex)
				proj.SetProperty ("AndroidEnableMultiDex", "True");

			proj.Imports.Add (new Import ("foo.targets") {
				TextContent = () => @"<?xml version=""1.0"" encoding=""utf-16""?>
<Project ToolsVersion=""4.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
<Target Name=""_Foo"" AfterTargets=""_SetLatestTargetFrameworkVersion"">
	<PropertyGroup>
		<AotAssemblies Condition=""!Exists('$(MonoAndroidBinDirectory)" + Path.DirectorySeparatorChar + @"cross-arm')"">False</AotAssemblies>
	</PropertyGroup>
	<Message Text=""$(AotAssemblies)"" />
</Target>
</Project>
",
			});
			using (var libb = CreateDllBuilder (Path.Combine ("temp", $"{folderName}Library1")))
			using (var b = CreateApkBuilder (Path.Combine ("temp", folderName))) {
				libb.Build (lib);
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				Assert.IsFalse (b.LastBuildOutput.ContainsText ("Duplicate zip entry"), "Should not get warning about [META-INF/MANIFEST.MF]");

				var className = "Lmono/MonoRuntimeProvider;";
				var dexFile = b.Output.GetIntermediaryPath (Path.Combine ("android", "bin", "classes.dex"));
				FileAssert.Exists (dexFile);
				Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
			}
		}

		//This test validates the _CleanIntermediateIfNeeded target
		[Test]
		[NonParallelizable]
		public void BuildAfterUpgradingNuget ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.MainActivity = proj.DefaultMainActivity.Replace ("public class MainActivity : Activity", "public class MainActivity : AndroidX.AppCompat.App.AppCompatActivity");

			proj.PackageReferences.Add (new Package {
				Id = "Xamarin.AndroidX.AppCompat",
				Version = "1.6.1.5",
			});

			using (var b = CreateApkBuilder (Path.Combine ("temp", TestContext.CurrentContext.Test.Name))) {
				//[TearDown] will still delete if test outcome successful, I need logs if assertions fail but build passes
				b.CleanupAfterSuccessfulBuild =
					b.CleanupOnDispose = false;
				b.Verbosity = LoggerVerbosity.Detailed;
				var projectDir = Path.Combine (Root, b.ProjectDirectory);
				if (Directory.Exists (projectDir))
					Directory.Delete (projectDir, true);
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");
				Assert.IsFalse (b.Output.IsTargetSkipped ("_CleanIntermediateIfNeeded"), "`_CleanIntermediateIfNeeded` should have run for the first build!");

				var nugetStamp = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "stamp", "_CleanIntermediateIfNeeded.stamp");
				FileAssert.Exists (nugetStamp, "`_CleanIntermediateIfNeeded` did not create stamp file!");
				string build_props = b.Output.GetIntermediaryPath ("build.props");
				FileAssert.Exists (build_props, "build.props should exist after first build.");

				proj.PackageReferences.Clear ();
				//NOTE: this should be newer than specified above
				proj.PackageReferences.Add (KnownPackages.AndroidXAppCompat);
				b.Save (proj, doNotCleanupOnUpdate: true);
				Assert.IsTrue (b.Build (proj), "second build should have succeeded.");
				Assert.IsFalse (b.Output.IsTargetSkipped ("_CleanIntermediateIfNeeded"), "`_CleanIntermediateIfNeeded` should have run for the second build!");
				FileAssert.Exists (nugetStamp, "`_CleanIntermediateIfNeeded` did not create stamp file!");
				Assert.IsTrue (StringAssertEx.ContainsText (b.LastBuildOutput, "Refreshing Xamarin.AndroidX.AppCompat.dll"), "`ResolveLibraryProjectImports` should not skip `Xamarin.AndroidX.AppCompat.dll`!");
				FileAssert.Exists (build_props, "build.props should exist after second build.");

				proj.MainActivity = proj.MainActivity.Replace ("clicks", "CLICKS");
				proj.Touch ("MainActivity.cs");
				Assert.IsTrue (b.Build (proj), "third build should have succeeded.");
				Assert.IsTrue (b.Output.IsTargetSkipped ("_CleanIntermediateIfNeeded"), "A build with no changes to NuGets should *not* trigger `_CleanIntermediateIfNeeded`!");
				FileAssert.Exists (build_props, "build.props should exist after third build.");
			}
		}

		[Test]
		public void BuildInDesignTimeMode ([Values(false, true)] bool useManagedParser)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.SetProperty ("AndroidUseManagedDesignTimeResourceGenerator", useManagedParser.ToString ());
			using (var builder = CreateApkBuilder ()) {
				builder.Target = "UpdateAndroidResources";
				builder.Build (proj, parameters: new string[] { "DesignTimeBuild=true" });
				Assert.IsFalse (builder.Output.IsTargetSkipped ("_CreatePropertiesCache"), "target \"_CreatePropertiesCache\" should have been run.");
				Assert.IsFalse (builder.Output.IsTargetSkipped ("_ResolveLibraryProjectImports"), "target \"_ResolveLibraryProjectImports\' should have been run.");
				var intermediate = Path.Combine (Root, builder.ProjectDirectory, proj.IntermediateOutputPath);
				var librarycache = Path.Combine (intermediate, "designtime", "libraryprojectimports.cache");
				Assert.IsTrue (File.Exists (librarycache), $"'{librarycache}' should exist.");
				librarycache = Path.Combine (intermediate, "libraryprojectimports.cache");
				Assert.IsFalse (File.Exists (librarycache), $"'{librarycache}' should not exist.");
				builder.Build (proj, parameters: new string[] { "DesignTimeBuild=true" });
				Assert.IsFalse (builder.Output.IsTargetSkipped ("_CreatePropertiesCache"), "target \"_CreatePropertiesCache\" should have been run.");
				Assert.IsTrue (builder.Output.IsTargetSkipped ("_ResolveLibraryProjectImports"), "target \"_ResolveLibraryProjectImports\' should have been skipped.");
				Assert.IsTrue (builder.Clean (proj), "Clean Should have succeeded");
				builder.Target = "_CleanDesignTimeIntermediateDir";
				Assert.IsTrue (builder.Build (proj), "_CleanDesignTimeIntermediateDir should have succeeded");
				librarycache = Path.Combine (intermediate, "designtime", "libraryprojectimports.cache");
				Assert.IsFalse (File.Exists (librarycache), $"'{librarycache}' should not exist.");
			}
		}

		[Test]
		public void IfAndroidJarDoesNotExistThrowXA5207 ([Values(true, false)] bool buildingInsideVisualStudio)
		{
			var path = Path.Combine ("temp", TestName);
			var AndroidSdkDirectory = CreateFauxAndroidSdkDirectory (Path.Combine (path, "android-sdk"), "24.0.1", new ApiInfo [] { new ApiInfo { Id = "30" } });
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};

			using (var builder = CreateApkBuilder (Path.Combine (path, proj.ProjectName), false, false)) {
				builder.ThrowOnBuildFailure = false;
				builder.BuildingInsideVisualStudio = buildingInsideVisualStudio;
				Assert.IsTrue (builder.DesignTimeBuild (proj), "DesignTime build should succeed.");
				Assert.IsFalse (builder.LastBuildOutput.ContainsText ("error XA5207:"), "XA5207 should not have been raised.");
				builder.Target = "AndroidPrepareForBuild";
				Assert.IsFalse (builder.Build (proj, parameters: new string [] {
					$"AndroidSdkBuildToolsVersion=24.0.1",
					$"AndroidSdkDirectory={AndroidSdkDirectory}",
				}), "Build should have failed");
				Assert.IsTrue (builder.LastBuildOutput.ContainsText ("error XA5207:"), "XA5207 should have been raised.");
				Assert.IsTrue (builder.LastBuildOutput.ContainsText ($"Could not find android.jar for API level {proj.TargetSdkVersion}"), "XA5207 should have had a good error message.");
				if (buildingInsideVisualStudio)
					Assert.IsTrue (builder.LastBuildOutput.ContainsText ($"Either install it in the Android SDK Manager"), "XA5207 should have an error message for Visual Studio.");
				else
				    Assert.IsTrue (builder.LastBuildOutput.ContainsText ($"You can install the missing API level by running"), "XA5207 should have an error message for the command line.");
			}
			Directory.Delete (AndroidSdkDirectory, recursive: true);
		}

		[Test]
		public void InvalidTargetPlatformVersion ([Values ("android33", "android99.0")] string platformVersion)
		{
			const string targetFramework = "net9.0";
			var project = new XamarinAndroidApplicationProject {
				TargetFramework = $"{targetFramework}-{platformVersion}",
			};
			using var builder = CreateApkBuilder ();
			builder.ThrowOnBuildFailure = false;
			Assert.IsFalse (builder.Build (project), "build should fail");

			Assert.IsTrue (builder.LastBuildOutput.ContainsText ("error NETSDK1140:"), "NETSDK1140 should have been raised.");
		}

		[Test]
		public void XA4212 ()
		{
			var proj = new XamarinAndroidApplicationProject () {
			};
			proj.Sources.Add (new BuildItem ("Compile", "MyBadJavaObject.cs") { TextContent = () => @"
using System;
using Android.Runtime;
namespace UnnamedProject {
    public class MyBadJavaObject : IJavaObject
    {
        public IntPtr Handle {
			get {return IntPtr.Zero;}
        }

        public void Dispose ()
        {
        }
    }
}" });
			using (var builder = CreateApkBuilder (Path.Combine ("temp", TestContext.CurrentContext.Test.Name))) {
				builder.ThrowOnBuildFailure = false;
				Assert.IsFalse (builder.Build (proj), "Build should have failed with XA4212.");
				StringAssertEx.Contains ($"error : XA4", builder.LastBuildOutput, "Error should be XA4212");
				StringAssertEx.Contains ($"Type `UnnamedProject.MyBadJavaObject` implements `Android.Runtime.IJavaObject`", builder.LastBuildOutput, "Error should mention MyBadJavaObject");
				Assert.IsTrue (builder.Build (proj, parameters: new [] { "AndroidErrorOnCustomJavaObject=False" }), "Build should have succeeded.");
				StringAssertEx.Contains ($"warning : XA4", builder.LastBuildOutput, "warning XA4212");
			}
		}

		[Test]
		public void Desugar ([Values (true, false)] bool isRelease, [Values ("", "r8")] string linkTool)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = isRelease,
				// desugar is on by default with r8
				LinkTool = linkTool,
			};

			//Add a BroadcastReceiver
			proj.Sources.Add (new BuildItem.Source ("MyReceiver.cs") {
				TextContent = () => @"
using Android.Content;

[BroadcastReceiver(Process = "":remote"", Name = ""foo.MyReceiver"")]
public class MyReceiver : BroadcastReceiver
{
    public override void OnReceive(Context context, Intent intent) { }
}",
			});

			//Okhttp and Okio
			//https://github.com/square/okhttp
			//https://github.com/square/okio
			if (!string.IsNullOrEmpty (linkTool)) {
				//NOTE: these are just enough rules to get it to build, not optimal
				var rules = new List<string> {
					"-dontwarn com.google.devtools.build.android.desugar.**",
					"-dontwarn javax.annotation.**",
					"-dontwarn org.codehaus.mojo.animal_sniffer.*",
				};
				//FIXME: We aren't de-BOM'ing proguard files?
				var bytes = Files.UTF8withoutBOM.GetBytes (string.Join (Environment.NewLine, rules));
				proj.OtherBuildItems.Add (new BuildItem ("ProguardConfiguration", "okhttp3.pro") {
					BinaryContent = () => bytes,
				});
			}
			proj.OtherBuildItems.Add (new BuildItem ("AndroidJavaLibrary", "okio-1.13.0.jar") {
				WebContent = "https://repo1.maven.org/maven2/com/squareup/okio/okio/1.13.0/okio-1.13.0.jar"
			});
			proj.OtherBuildItems.Add (new BuildItem ("AndroidJavaLibrary", "okhttp-3.8.0.jar") {
				WebContent = "https://repo1.maven.org/maven2/com/squareup/okhttp3/okhttp/3.8.0/okhttp-3.8.0.jar"
			});
			proj.OtherBuildItems.Add (new BuildItem ("AndroidJavaLibrary", "retrofit-2.3.0.jar") {
				WebContent = "https://repo1.maven.org/maven2/com/squareup/retrofit2/retrofit/2.3.0/retrofit-2.3.0.jar"
			});
			proj.OtherBuildItems.Add (new BuildItem ("AndroidJavaLibrary", "converter-gson-2.3.0.jar") {
				WebContent = "https://repo1.maven.org/maven2/com/squareup/retrofit2/converter-gson/2.3.0/converter-gson-2.3.0.jar"
			});
			proj.OtherBuildItems.Add (new BuildItem ("AndroidJavaLibrary", "gson-2.7.jar") {
				WebContent = "https://repo1.maven.org/maven2/com/google/code/gson/gson/2.7/gson-2.7.jar"
			});
			/* The source is simple:
			 *
				public class Lambda
				{
				    public void foo()
				    {
				        Runnable r = () -> System.out.println("whee");
					r.run();
				    }
				}
			 *
			 * We wanted to use AndroidJavaSource to simply compile it, but with
			 * android.jar as bootclasspath, it is impossible to compile lambdas.
			 * Therefore we compiled it without android.jar (javac Lambda.java)
			 * and then manually archived it (jar cvf Lambda.jar Lambda.class).
			 */

			proj.OtherBuildItems.Add (new BuildItem ("AndroidJavaLibrary", "Lambda.jar") { BinaryContent = () => Convert.FromBase64String (@"
UEsDBBQACAgIAECRZ0sAAAAAAAAAAAAAAAAJAAQATUVUQS1JTkYv/soAAAMAUEsHCAAAAAACAAAA
AAAAAFBLAwQUAAgICABBkWdLAAAAAAAAAAAAAAAAFAAAAE1FVEEtSU5GL01BTklGRVNULk1G803M
y0xLLS7RDUstKs7Mz7NSMNQz4OVyLkpNLElN0XWqBAlY6BnEGxobKmj4FyUm56QqOOcXFeQXJZYA
1WvycvFyAQBQSwcIUQoqTEQAAABFAAAAUEsDBBQACAgIACWRZ0sAAAAAAAAAAAAAAAAMAAAATGFt
YmRhLmNsYXNznVNdTxNBFD1DaafdLrQWip+gxaJdEIrfDzU+2MRIUoVYwvu0HWBhO9PszmL6s/RB
Iw/+AONvMt7pEotWiXEf5sz9OOfeuTvz9fvpFwCP8NRBFpdKtF/I4zKu5HAV17K47uAGFjmWOG4y
ZJ75yjfPGVI1b49huql7kqHQ8pV8E/c7MtwVnYA8qX2tGdxA9Ds9USWjusngtHUcduVL32bkW6PY
xpE4ES5ycBiKL7Q2kQnF4LU0h7oXFTK4VYRDUHGxjNscVYsOx4qLO7hLDbw7lJKj5sLDKrWXiJKU
la0HQh3UtztHsmscrOGeA451ai6MFcNCzWuNs97GStnWGwylSe8vgu1hZGSfZHRsGMqJiK/rO6Gv
TNuEUvRJZe4PbgY+sFZA5cu1c9Up7KuDhrfHseGijocuZu1ElscpvjrRx7KeHJDmI/ZF1+hwSJPs
jy2Ox3YKWh/HA5r/llIybAYiimTE8O18yTO9ZNKvhOoFMqomxMZkZ38j7g4H8v+CScmLud5ktCmC
oO0b2eB4wrDyT+dhWLo4DxW6GFnYLwVmLyOtebIWCRlhevUT2Hva0ExpzYycNjTzM3WdcIpw5tRC
a+0zUgxjyiwpkw5RM2TzomN/8Bm1MiICuQ+YLqU/IvN7pTSRC4RTKOIBoSVu0pO9T0/UPliX7DnK
mUcZ8z8AUEsHCLuHtAn+AQAA0QMAAFBLAQIUABQACAgIAECRZ0sAAAAAAgAAAAAAAAAJAAQAAAAA
AAAAAAAAAAAAAABNRVRBLUlORi/+ygAAUEsBAhQAFAAICAgAQZFnS1EKKkxEAAAARQAAABQAAAAA
AAAAAAAAAAAAPQAAAE1FVEEtSU5GL01BTklGRVNULk1GUEsBAhQAFAAICAgAJZFnS7uHtAn+AQAA
0QMAAAwAAAAAAAAAAAAAAAAAwwAAAExhbWJkYS5jbGFzc1BLBQYAAAAAAwADALcAAAD7AgAAAAA=
				") });
			using (var builder = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (builder.Build (proj), "Build should have succeeded");
				Assert.IsFalse (builder.LastBuildOutput.ContainsText ("Duplicate zip entry"), "Should not get warning about [META-INF/MANIFEST.MF]");

				var className = "Lmono/MonoRuntimeProvider;";
				var dexFile = builder.Output.GetIntermediaryPath (Path.Combine ("android", "bin", "classes.dex"));
				FileAssert.Exists (dexFile);
				Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
				className = "Lmono/MonoRuntimeProvider_1;";
				Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
			}
		}

		//See: https://developer.android.com/about/versions/marshmallow/android-6.0-changes#behavior-apache-http-client
		[Test]
		public void MissingOrgApacheHttpClient ()
		{
			var proj = new XamarinAndroidApplicationProject {
				Sources = {
					new BuildItem.Source ("ApacheHttpClient.cs") {
						BinaryContent = () => ResourceData.ApacheHttpClient_cs,
					},
				},
			};
			proj.AndroidManifest = proj.AndroidManifest.Replace ("</application>",
				"<uses-library android:name=\"org.apache.http.legacy\" android:required=\"false\" /></application>");
			proj.SetProperty ("AndroidEnableMultiDex", "True");

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded");
			}
		}

		//NOTE: tests type forwarders in Mono.Android.dll to System.Drawing.Common.dll
		[Test]
		public void SystemDrawingCommon ()
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true,
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () => "class Foo { System.Drawing.Color bar; }"
					}
				},
				PackageReferences = {
					KnownPackages.Acr_UserDialogs,
				}
			};
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		//NOTE: Referencing only Microsoft.Extensions.Http, surfaced a bug in <ResolveAssemblies/>
		[Test]
		public void MicrosoftExtensionsHttp ()
		{
			// The goal is to create a project with only this <PackageReference/>
			var proj = new XamarinAndroidApplicationProject {
				PackageReferences = {
					KnownPackages.Microsoft_Extensions_Http,
				}
			};
			proj.References.Clear ();
			proj.Sources.Clear ();
			// We have to add a custom Target to remove Java.Interop and System.Runtime
			proj.Imports.Add (new Import ("foo.targets") {
				TextContent = () => @"<?xml version=""1.0"" encoding=""utf-16""?>
<Project ToolsVersion=""4.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
	<Target Name=""_Foo"" BeforeTargets=""_ResolveAssemblies"">
		<ItemGroup>
			<_Remove Include=""@(_ReferencePath)"" Condition=""'%(FileName)' == 'Java.Interop' Or '%(FileName)' == 'System.Runtime'"" />
			<_ReferencePath Remove=""@(_Remove)"" />
		</ItemGroup>
	</Target>
</Project>"
			});
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void FastDeploymentDoesNotAddContentProvider ()
		{
			AssertCommercialBuild ();

			var proj = new XamarinAndroidApplicationProject {
				EmbedAssembliesIntoApk = false,
			};
			proj.SetProperty ("_XASupportsFastDev", "True");
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				//NOTE: build will fail, due to $(_XASupportsFastDev)
				b.ThrowOnBuildFailure = false;
				b.Build (proj);

				var manifest = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				FileAssert.Exists (manifest);
				var content = File.ReadAllLines (manifest);
				var type = "mono.android.ResourcePatcher";

				//NOTE: only $(AndroidFastDeploymentType) containing "dexes" should add this to the manifest
				Assert.IsFalse (StringAssertEx.ContainsText (content, type), $"`{type}` should not exist in `AndroidManifest.xml`!");
			}
		}

		[Test]
		public void BuildOutsideVisualStudio ()
		{
			var path = Path.Combine ("temp", TestName);
			var lib = new XamarinAndroidLibraryProject {
				ProjectName = "Library1",
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () => "public class Foo { }",
					}
				},
			};
			var proj = new XamarinFormsAndroidApplicationProject {
				ProjectName = "App1",
				References = { new BuildItem ("ProjectReference", "..\\Library1\\Library1.csproj") },
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar : Foo { }",
					}
				},
			};
			using (var libb = CreateDllBuilder (Path.Combine (path, lib.ProjectName)))
			using (var appb = CreateApkBuilder (Path.Combine (path, proj.ProjectName))) {
				libb.BuildingInsideVisualStudio =
					appb.BuildingInsideVisualStudio = false;
				appb.Target = "SignAndroidPackage";
				//Save, but don't build
				libb.Save (lib);
				Assert.IsTrue (appb.Build (proj), "build should have succeeded.");
			}
		}

		[Test]
		public void RemoveOldMonoPackageManager ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				b.ThrowOnBuildFailure = false;
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var targets = new [] {
					"_CleanIntermediateIfNeeded",
					"_GeneratePackageManagerJava",
					"_CompileJava",
				};
				var intermediate = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				var oldMonoPackageManager = Path.Combine (intermediate, "android", "src", "mono", "MonoPackageManager.java");
				var notifyTimeZoneChanges = Path.Combine (intermediate, "android", "src", "mono", "android", "app", "NotifyTimeZoneChanges.java");
				Directory.CreateDirectory (Path.GetDirectoryName (notifyTimeZoneChanges));
				File.WriteAllText (oldMonoPackageManager, @"package mono;
public class MonoPackageManager { }
class MonoPackageManager_Resources { }");
				File.WriteAllText (notifyTimeZoneChanges, @"package mono.android.app;
public class ApplicationRegistration { }");
				var oldMonoPackageManagerClass = Path.Combine (intermediate, "android", "bin", "classes" , "mono", "MonoPackageManager.class");
				File.WriteAllText (oldMonoPackageManagerClass, "");
				// Change $(AndroidNETSdkVersion) to trigger _CleanIntermediateIfNeeded
				Assert.IsTrue (b.Build (proj, parameters: new [] { "AndroidNETSdkVersion=99.99" }, doNotCleanupOnUpdate: true), "Build should have succeeded.");
				foreach (var target in targets) {
					Assert.IsFalse (b.Output.IsTargetSkipped (target), $"`{target}` should *not* be skipped.");
				}
				// Old files that should *not* exist
				FileAssert.DoesNotExist (oldMonoPackageManager);
				FileAssert.DoesNotExist (oldMonoPackageManagerClass);
				FileAssert.DoesNotExist (notifyTimeZoneChanges);
				// New files that should exist
				var monoPackageManager_Resources = Path.Combine (intermediate, "android", "src", "mono", "MonoPackageManager_Resources.java");
				var monoPackageManager_ResourcesClass = Path.Combine (intermediate, "android", "bin", "classes", "mono", "MonoPackageManager_Resources.class");
				FileAssert.Exists (monoPackageManager_Resources);
				FileAssert.Exists (monoPackageManager_ResourcesClass);
			}
		}

		[Test]
		public void CompilerErrorShouldNotRunLinkAssemblies ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.Sources.Add (new BuildItem.Source ("SyntaxError.cs") {
				TextContent = () => "class SyntaxError {"
			});
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				b.ThrowOnBuildFailure = false;
				Assert.IsFalse (b.Build (proj), "Build should have failed.");
				Assert.IsFalse (StringAssertEx.ContainsText (b.LastBuildOutput, "The \"LinkAssemblies\" task failed unexpectedly"), "The LinkAssemblies MSBuild task should not run!");
			}
		}

		/// <summary>
		/// This assembly weirdly has no [assembly: System.Runtime.Versioning.TargetFrameworkAttribute()], at all...
		/// </summary>
		[Test]
		public void AssemblyWithMissingTargetFramework ()
		{
			var proj = new XamarinFormsAndroidApplicationProject {
				AndroidResources = {
					new AndroidItem.AndroidResource ("Resources\\layout\\test.axml") {
						TextContent = () =>
@"<?xml version=""1.0"" encoding=""utf-8""?>
<ScrollView
    xmlns:android=""http://schemas.android.com/apk/res/android""
    xmlns:local=""http://schemas.android.com/apk/res-auto"">
    <refractored.controls.CircleImageView local:civ_border_width=""0dp"" />
</ScrollView>"
					}
				}
			};
			proj.PackageReferences.Add (KnownPackages.CircleImageView);
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "build should have succeeded.");

				// We should have a java stub
				var javaStubDir = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "src");
				var files = Directory.GetFiles (javaStubDir, "CircleImageView.java", SearchOption.AllDirectories);
				CollectionAssert.IsNotEmpty (files, $"{javaStubDir} should contain CircleImageView.java!");
			}
		}

		[Test]
		public void WorkManager ()
		{
			var proj = new XamarinFormsAndroidApplicationProject ();
			proj.Sources.Add (new BuildItem.Source ("MyWorker.cs") {
				TextContent = () =>
@"using System;
using Android.Content;
using AndroidX.Work;

public class MyWorker : Worker
{
	public MyWorker (Context c, WorkerParameters p) : base (c, p) { }

	public override Result DoWork () => Result.InvokeSuccess ();
}
"
			});
			proj.PackageReferences.Add (KnownPackages.AndroidXWorkRuntime);
			proj.PackageReferences.Add (KnownPackages.AndroidXLifecycleLiveData);
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void NuGetizer3000 ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.PackageReferences.Add (KnownPackages.NuGet_Build_Packaging);
			using (var b = CreateApkBuilder (Path.Combine ("temp", nameof (NuGetizer3000)))) {
				b.Target = "GetPackageContents";
				Assert.IsTrue (b.Build (proj), $"{b.Target} should have succeeded.");
			}
		}

		[Test]
		public void NetworkSecurityConfig ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.Sources.Add (new BuildItem ("Compile", "CustomApp.cs") { TextContent = () => @"
using System;
using Android.App;
using Android.Runtime;

namespace UnnamedProject
{
	[Application(Name = ""com.xamarin.android.CustomApp"", NetworkSecurityConfig = ""@xml/network_security_config"")]
	public class CustomApp : Application
	{
		public CustomApp(IntPtr handle, JniHandleOwnership ownerShip) : base(handle, ownerShip) { }
	}
}" });
			proj.AndroidResources.Add (new AndroidItem.AndroidResource (@"Resources\xml\network_security_config.xml") {
				TextContent = () =>
@"<?xml version=""1.0"" encoding=""utf-8""?>
<network-security-config>
    <domain-config>
        <domain includeSubdomains=""true"">example.com</domain>
        <trust-anchors>
            <certificates src=""@raw/my_ca""/>
        </trust-anchors>
    </domain-config>
</network-security-config>"
			});
			proj.AndroidResources.Add (new AndroidItem.AndroidResource (@"Resources\raw\my_ca") {
				BinaryContent = () => Array.Empty<byte> (), // doesn't have to be real, just *exist*
			});

			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				var manifest = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				FileAssert.Exists (manifest);
				var contents = File.ReadAllText (manifest);
				StringAssert.Contains ("android:networkSecurityConfig=\"@xml/network_security_config\"", contents);
			}
		}

		[Test]
		public void AbiNameInIntermediateOutputPath ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.PackageReferences.Add (KnownPackages.Akavache);
			proj.OutputPath = Path.Combine ("bin", "x86", "Debug");
			proj.IntermediateOutputPath = Path.Combine ("obj", "x86", "Debug");
			proj.MainActivity = proj.DefaultMainActivity.Replace ("//${AFTER_ONCREATE}", "var task = Akavache.BlobCache.LocalMachine.GetAllKeys();");
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				Assert.IsFalse (StringAssertEx.ContainsText (b.LastBuildOutput, Path.Combine ("armeabi", "libe_sqlite3.so")), "Build should not use `armeabi`.");
			}
		}

		[Test]
		public void PackageNamingPolicy ([Values ("LowercaseMD5", "LowercaseCrc64")] string packageNamingPolicy)
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.SetProperty ("UseInterpreter", "true");
			proj.SetProperty ("AndroidPackageNamingPolicy", packageNamingPolicy);
			proj.SetAndroidSupportedAbis ("armeabi-v7a", "x86");
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "build should have succeeded.");
				var environment = b.Output.GetIntermediaryPath (Path.Combine ("__environment__.txt"));
				FileAssert.Exists (environment);
				var values = new List<string> {
					$"__XA_PACKAGE_NAMING_POLICY__={packageNamingPolicy}"
				};
				values.Add ("mono.enable_assembly_preload=0");
				values.Add ("DOTNET_MODIFIABLE_ASSEMBLIES=Debug");
				Assert.AreEqual (string.Join (Environment.NewLine, values), File.ReadAllText (environment).Trim ());
			}
		}

		[Test]
		public void KotlinServiceLoader ([Values ("apk", "aab")] string packageFormat)
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.SetProperty ("AndroidPackageFormat", packageFormat);
			if (packageFormat == "aab")
				// Disable fast deployment for aabs because it is not currently compatible and so gives an XA0119 build error.
				proj.EmbedAssembliesIntoApk = true;
			proj.OtherBuildItems.Add (new BuildItem ("AndroidJavaLibrary", "kotlinx-coroutines-android-1.3.2.jar") {
				WebContent = "https://repo1.maven.org/maven2/org/jetbrains/kotlinx/kotlinx-coroutines-android/1.3.2/kotlinx-coroutines-android-1.3.2.jar"
			});
			proj.OtherBuildItems.Add (new BuildItem ("AndroidJavaLibrary", "gson-2.7.jar") {
				WebContent = "https://repo1.maven.org/maven2/com/google/code/gson/gson/2.7/gson-2.7.jar"
			});
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "build should have succeeded.");
				var archive = Path.Combine (Root, b.ProjectDirectory,
					proj.OutputPath, $"{proj.PackageName}.{packageFormat}");
				var prefix = packageFormat == "apk" ? "" : "base/root/";
				var expectedFiles = new [] {
					prefix + "META-INF/maven/com.google.code.gson/gson/pom.xml",
					prefix + "META-INF/services/kotlinx.coroutines.internal.MainDispatcherFactory",
					prefix + "META-INF/services/kotlinx.coroutines.CoroutineExceptionHandler",
				};
				var manifest = prefix + "META-INF/MANIFEST.MF";
				using (var zip = ZipHelper.OpenZip (archive)) {
					Assert.IsFalse (zip.ContainsEntry (manifest, caseSensitive: true), $"{manifest} should *not* exist in {archive}");
					foreach (var expected in expectedFiles) {
						Assert.IsTrue (zip.ContainsEntry (expected, caseSensitive: true), $"{expected} should exist in {archive}");
					}
				}
			}
		}

		[Test]
		public void XA1018 ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.SetProperty ("AndroidManifest", "DoesNotExist");
			using (var builder = CreateApkBuilder ()) {
				builder.ThrowOnBuildFailure = false;
				Assert.IsFalse (builder.Build (proj), "Build should have failed.");
				string error = builder.LastBuildOutput
						.SkipWhile (x => !x.StartsWith ("Build FAILED.", StringComparison.Ordinal))
						.FirstOrDefault (x => x.Contains ("error XA1018:"));
				Assert.IsNotNull (error, "Build should have failed with XA1018.");
				StringAssert.Contains ("DoesNotExist", error, "Error should include the name of the nonexistent file");
			}
		}

		[Test]
		public void XA4313 ([Values ("OpenTK-1.0", "Xamarin.Android.NUnitLite")] string reference)
		{
			var proj = new XamarinAndroidApplicationProject () {
				References = {
					new BuildItem.Reference (reference)
				},
			};
			using (var builder = CreateApkBuilder ()) {
				builder.ThrowOnBuildFailure = false;
				Assert.IsFalse (builder.Build (proj), $"Build should have failed.");
				string error = builder.LastBuildOutput
						.SkipWhile (x => !x.StartsWith ($"Build FAILED.", StringComparison.Ordinal))
						.FirstOrDefault (x => x.Contains ("error XA4313"));
				Assert.IsNotNull (error, $"Build should have failed with XA4313.");
			}
		}

		[Test]
		public void OpenTKNugetWorks ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.PackageReferences.Add (KnownPackages.Xamarin_Legacy_OpenTK);
			using (var builder = CreateApkBuilder ()) {
				builder.ThrowOnBuildFailure = false;
				Assert.IsTrue (builder.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void NUnitLiteNugetWorks ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.PackageReferences.Add (KnownPackages.Xamarin_Legacy_NUnitLite);
			using (var builder = CreateApkBuilder ()) {
				builder.ThrowOnBuildFailure = false;
				Assert.IsTrue (builder.Build (proj), "Build should have succeeded.");
			}
		}

		static readonly object [] XA1027XA1028Source = new object [] {
			new object [] {
				/* linkTool */                   "r8",
				/* enableProguard */             null,
				/* androidEnableProguard */      "true",
				/* expectedBuildResult */        true,
				/* expectedWarning */            "0 Warning(s)",
			},
			new object [] {
				/* linkTool */                   "proguard",
				/* enableProguard */             null,
				/* androidEnableProguard */      "true",
				/* expectedBuildResult */        false,
				/* expectedWarning */            "0 Warning(s)",
			},
			new object [] {
				/* linkTool */                   null,
				/* enableProguard */             null,
				/* androidEnableProguard */      null,
				/* expectedBuildResult */        true,
				/* expectedWarning */            "0 Warning(s)",
			},
			new object [] {
				/* linkTool */                   null,
				/* enableProguard */             "true",
				/* androidEnableProguard */      null,
				/* expectedBuildResult */        false,
				/* expectedWarning */            "warning XA1027:",
			},
			new object [] {
				/* linkTool */                   null,
				/* enableProguard */             null,
				/* androidEnableProguard */      "true",
				/* expectedBuildResult */        false,
				/* expectedWarning */            "warning XA1028:",
			}
		};

		[Test]
		[TestCaseSource (nameof (XA1027XA1028Source))]
		public void XA1027XA1028 (string linkTool, string enableProguard, string androidEnableProguard, bool expectedBuildResult, string expectedWarning)
		{
			var proj = new XamarinAndroidApplicationProject {
				LinkTool = linkTool,
				IsRelease = true
			};
			proj.SetProperty ("EnableProguard", enableProguard);
			proj.SetProperty ("AndroidEnableProguard", androidEnableProguard);
			using (var builder = CreateApkBuilder ()) {
				builder.Target = "_CheckNonIdealConfigurations";
				builder.ThrowOnBuildFailure = expectedBuildResult;
				builder.Build (proj);
				Assert.IsNotNull(
					builder.LastBuildOutput
						.SkipWhile (x => !x.StartsWith (expectedBuildResult ? "Build succeeded." : "Build FAILED.", StringComparison.Ordinal))
						.FirstOrDefault (x => x.Contains (expectedWarning)),
					$"Build output should contain '{expectedWarning}'.");
			}
		}

		[Test]
		public void XA4310 ([Values ("apk", "aab")] string packageFormat)
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true,
			};
			proj.SetProperty ("AndroidKeyStore", "true");
			proj.SetProperty ("AndroidSigningKeyStore", "DoesNotExist");
			proj.SetProperty ("AndroidSigningStorePass", "android");
			proj.SetProperty ("AndroidSigningKeyAlias", "mykey");
			proj.SetProperty ("AndroidSigningKeyPass", "android");
			proj.SetProperty ("AndroidPackageFormat", packageFormat);
			using (var builder = CreateApkBuilder ()) {
				builder.ThrowOnBuildFailure = false;
				Assert.IsFalse (builder.Build (proj), "Build should have failed with XA4310.");

				StringAssertEx.Contains ("error XA4310", builder.LastBuildOutput, "Error should be XA4310");
				StringAssertEx.Contains ("`DoesNotExist`", builder.LastBuildOutput, "Error should include the name of the nonexistent file");
				builder.AssertHasNoWarnings ();
			}
		}

		[Test]
		[NonParallelizable]
		public void CheckLintErrorsAndWarnings ()
		{
			string disabledIssues = "StaticFieldLeak,ObsoleteSdkInt,AllowBackup,ExportedReceiver,RedundantLabel";

			var proj = new XamarinAndroidApplicationProject ();
			proj.SetProperty ("AndroidLintEnabled", true.ToString ());
			proj.SetProperty ("AndroidLintDisabledIssues", disabledIssues);
			proj.SetProperty ("AndroidLintEnabledIssues", "");
			proj.SetProperty ("AndroidLintCheckIssues", "");
			proj.MainActivity = proj.DefaultMainActivity.Replace ("public class MainActivity : Activity", @"
		[IntentFilter (new[] { Android.Content.Intent.ActionView },
			Categories = new [] { Android.Content.Intent.CategoryDefault, Android.Content.Intent.CategoryBrowsable },
			DataHost = ""mydomain.com"",
			DataScheme = ""http""
		)]
		public class MainActivity : Activity
			");
			proj.AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\layout\\test.axml") {
				TextContent = () => {
					return @"<?xml version=""1.0"" encoding=""utf-8""?>
<ConstraintLayout xmlns:android=""http://schemas.android.com/apk/res/android""
	android:orientation=""vertical""
	android:layout_width=""fill_parent""
	android:layout_height=""fill_parent"">
	<TextView android:id=""@+id/foo""
		android:layout_width=""150dp""
		android:layout_height=""wrap_content""
	/>
</ConstraintLayout>";
				}
			});
			using (var b = CreateApkBuilder ("temp/CheckLintErrorsAndWarnings", cleanupOnDispose: false)) {
				int maxApiLevel = AndroidSdkResolver.GetMaxInstalledPlatform ();
				b.LatestTargetFrameworkVersion (out string apiLevel);
				if (int.TryParse (apiLevel, out int a) && a < maxApiLevel)
					disabledIssues += ",OldTargetApi";
				proj.SetProperty ("AndroidLintDisabledIssues", disabledIssues);
				proj.SupportedOSPlatformVersion = "24";
				proj.TargetSdkVersion = apiLevel;
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				StringAssertEx.DoesNotContain ("XA0102", b.LastBuildOutput, "Output should not contain any XA0102 warnings");
				StringAssertEx.DoesNotContain ("XA0103", b.LastBuildOutput, "Output should not contain any XA0103 errors");
				Assert.IsTrue (b.Clean (proj), "Clean should have succeeded.");
			}
		}

		[Test]
		public void CheckLintConfigMerging ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.SetProperty ("AndroidLintEnabled", true.ToString ());
			proj.OtherBuildItems.Add (new AndroidItem.AndroidLintConfig ("lint1.xml") {
				TextContent = () => @"<?xml version=""1.0"" encoding=""UTF-8""?>
<lint>
	<issue id=""NewApi"" severity=""warning"" />
</lint>"
			});
			proj.OtherBuildItems.Add (new AndroidItem.AndroidLintConfig ("lint2.xml") {
				TextContent = () => @"<?xml version=""1.0"" encoding=""UTF-8""?>
<lint>
	<issue id=""MissingApplicationIcon"" severity=""ignore"" />
</lint>"
			});
			using (var b = CreateApkBuilder ("temp/CheckLintConfigMerging", false, false)) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var lintFile = Path.Combine (Root, "temp", "CheckLintConfigMerging", proj.IntermediateOutputPath, "lint.xml");
				Assert.IsTrue (File.Exists (lintFile), "{0} should have been created.", lintFile);
				var doc = XDocument.Load (lintFile);
				Assert.IsNotNull (doc, "Document should have loaded successfully.");
				Assert.IsNotNull (doc.Element ("lint"), "The xml file should have a lint element.");
				Assert.IsNotNull (doc.Element ("lint")
					.Elements ()
					.Any (x => x.Name == "Issue" && x.Attribute ("id").Value == "MissingApplicationIcon"), "Element is missing");
				Assert.IsNotNull (doc.Element ("lint")
					.Elements ()
					.Any (x => x.Name == "Issue" && x.Attribute ("id").Value == "NewApi"), "Element is missing");
				Assert.IsTrue (b.Clean (proj), "Clean should have succeeded.");
				Assert.IsFalse (File.Exists (lintFile), "{0} should have been deleted on clean.", lintFile);
			}
		}

		[Test]
		public void BuildApplicationWithJavaSourceUsingAndroidX ([Values(true, false)] bool isRelease)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = isRelease,
				AndroidJavaSources = {
					new BuildItem (AndroidBuildActions.AndroidJavaSource, "ToolbarEx.java") {
						TextContent = () => @"package com.unnamedproject.unnamedproject;
import android.content.Context;
import androidx.appcompat.widget.Toolbar;
public class ToolbarEx {
	public static Toolbar GetToolbar (Context context) {
		return new Toolbar (context);
	}
}
",
						Encoding = Encoding.ASCII
					},
				}
			};
			proj.PackageReferences.Add (KnownPackages.AndroidXAppCompat);
			using (var b = CreateApkBuilder ()) {
				b.ThrowOnBuildFailure = false;
				Assert.IsTrue (b.Build (proj), "Build should have succeeded");

				Assert.IsTrue (b.Clean (proj), "Clean should have succeeded.");
			}
		}

		[Test]
		public void BuildApplicationCheckThatAddStaticResourcesTargetDoesNotRerun ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			using (var b = CreateApkBuilder ()) {
				b.ThrowOnBuildFailure = false;
				Assert.IsTrue (b.Build (proj), "Build should not have failed");
				Assert.IsFalse (
					b.Output.IsTargetSkipped ("_AddStaticResources"),
					"The _AddStaticResources should have been run");
				Assert.IsTrue (b.Build (proj), "Build should not have failed");
				Assert.IsTrue (
					b.Output.IsTargetSkipped ("_AddStaticResources"),
					"The _AddStaticResources should NOT have been run");
			}
		}

		[Test]
		public void CheckJavaError ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.AndroidJavaSources.Add (new BuildItem (AndroidBuildActions.AndroidJavaSource, "TestMe.java") {
				TextContent = () => "public classo TestMe { }",
				Encoding = Encoding.ASCII
			});
			proj.AndroidJavaSources.Add (new BuildItem (AndroidBuildActions.AndroidJavaSource, "TestMe2.java") {
				TextContent = () => "public class TestMe2 {" +
					"public vod Test ()" +
					"}",
				Encoding = Encoding.ASCII
			});
			using (var b = CreateApkBuilder ("temp/CheckJavaError")) {
				b.ThrowOnBuildFailure = false;
				Assert.IsFalse (b.Build (proj), "Build should have failed.");
				var ext = b.IsUnix ? "" : ".exe";
				var text = $"TestMe.java(1,8): javac{ext} error JAVAC0000:  error: class, interface, or enum expected";
				Assert.IsTrue (StringAssertEx.ContainsText (b.LastBuildOutput, text), "TestMe.java(1,8) expected");
				text = $"TestMe2.java(1,41): javac{ext} error JAVAC0000:  error: ';' expected";
				Assert.IsTrue (StringAssertEx.ContainsText (b.LastBuildOutput, text), "TestMe2.java(1,41) expected");
				Assert.IsTrue (b.Clean (proj), "Clean should have succeeded.");
			}
		}

		[Test]
		/// <summary>
		/// Based on issue raised in
		/// https://bugzilla.xamarin.com/show_bug.cgi?id=28721
		/// </summary>
		public void DuplicateValuesInResourceCaseMap ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\layout\\test.axml") {
				TextContent = () => {
					return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<LinearLayout xmlns:android=\"http://schemas.android.com/apk/res/android\"\n    android:orientation=\"vertical\"\n    android:layout_width=\"fill_parent\"\n    android:layout_height=\"fill_parent\"\n    />";
				}
			});
			proj.AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\layout\\test.axml") {
				MetadataValues = "Link=Resources\\layout-xhdpi\\test.axml"
			});
			proj.AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\layout\\test.axml") {
				MetadataValues = "Link=Resources\\layout-xhdpi\\Test.axml"
			});
			using (var b = CreateApkBuilder ()) {
				b.ThrowOnBuildFailure = false;
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				Assert.IsTrue (b.Clean (proj), "Clean should have succeeded.");
			}
		}

		[Test]
		public void CheckLintResourceFileReferencesAreFixed ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.SetProperty ("AndroidLintEnabled", true.ToString ());
			proj.AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\layout\\test.axml") {
				TextContent = () => {
					return @"<?xml version=""1.0"" encoding=""utf-8""?>
<ConstraintLayout xmlns:android=""http://schemas.android.com/apk/res/android""
	android:orientation=""vertical""
	android:layout_width=""fill_parent""
	android:layout_height=""fill_parent"">
	<TextView android:id=""@+id/foo""
		android:layout_width=""150dp""
		android:layout_height=""wrap_content""
	/>
	<EditText
		android:id=""@+id/phone""
		android:layout_width=""fill_parent""
		android:layout_height=""wrap_content""
		android:hint=""Hint me up.""
	/>
 </ConstraintLayout>";
				}
			});
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Project should have built.");
				StringAssertEx.Contains ("XA0102", b.LastBuildOutput, "Output should contain XA0102 warnings");
				var errorFilePath = Path.Combine (proj.IntermediateOutputPath, "android", proj.IntermediateOutputPath, "res", "layout", "test.xml");
				StringAssertEx.DoesNotContain (errorFilePath, b.LastBuildOutput, $"Path {errorFilePath} should have been replaced.");
			}
		}

		[Test]
		public void SimilarAndroidXAssemblyNames ([Values(true, false)] bool publishTrimmed)
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true,
				AotAssemblies = publishTrimmed,
				PackageReferences = {
					new Package { Id = "Xamarin.AndroidX.CustomView", Version = "1.1.0.17" },
					new Package { Id = "Xamarin.AndroidX.CustomView.PoolingContainer", Version = "1.0.0.4" },
				}
			};
			proj.SetProperty (KnownProperties.PublishTrimmed, publishTrimmed.ToString());
			proj.MainActivity = proj.DefaultMainActivity.Replace ("//${AFTER_ONCREATE}", "AndroidX.CustomView.PoolingContainer.PoolingContainer.IsPoolingContainer (null);");
			using var builder = CreateApkBuilder ();
			Assert.IsTrue (builder.Build (proj), "Build should have succeeded.");
		}
	}
}
