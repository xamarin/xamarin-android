using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using NUnit.Framework;
using Xamarin.Android.Tasks;
using Xamarin.ProjectTools;
using Xamarin.Tools.Zip;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Build.Tests
{
	[TestFixture]
	[NonParallelizable] // On MacOS, parallel /restore causes issues
	[Category ("Node-2"), Category ("DotNetIgnore")] // These don't need to run under `dotnet test`
	public class XASdkTests : BaseTest
	{
		/// <summary>
		/// The full path to the project directory
		/// </summary>
		public string FullProjectDirectory { get; set; }

		static readonly object [] DotNetBuildLibrarySource = new object [] {
			new object [] {
				/* isRelease */    false,
				/* duplicateAar */ false,
			},
			new object [] {
				/* isRelease */    false,
				/* duplicateAar */ true,
			},
			new object [] {
				/* isRelease */    true,
				/* duplicateAar */ false,
			},
		};

		[Test]
		[Category ("SmokeTests")]
		[TestCaseSource (nameof (DotNetBuildLibrarySource))]
		public void DotNetBuildLibrary (bool isRelease, bool duplicateAar)
		{
			var path = Path.Combine ("temp", TestName);
			var env_var = "MY_ENVIRONMENT_VAR";
			var env_val = "MY_VALUE";

			// Setup dependencies App A -> Lib B -> Lib C

			var libC = new XASdkProject (outputType: "Library") {
				ProjectName = "LibraryC",
				IsRelease = isRelease,
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar { }",
					},
					new AndroidItem.AndroidResource (() => "Resources\\drawable\\IMALLCAPS.png") {
						BinaryContent = () => XamarinAndroidApplicationProject.icon_binary_mdpi,
					},
				}
			};
			libC.OtherBuildItems.Add (new AndroidItem.AndroidAsset ("Assets\\bar\\bar.txt") {
				BinaryContent = () => Array.Empty<byte> (),
			});
			var activity = libC.Sources.FirstOrDefault (s => s.Include () == "MainActivity.cs");
			if (activity != null)
				libC.Sources.Remove (activity);
			var libCBuilder = CreateDotNetBuilder (libC, Path.Combine (path, libC.ProjectName));
			Assert.IsTrue (libCBuilder.Build (), $"{libC.ProjectName} should succeed");

			var libB = new XASdkProject (outputType: "Library") {
				ProjectName = "LibraryB",
				IsRelease = isRelease,
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () =>
@"public class Foo : Bar
{
	public Foo ()
	{
		int x = LibraryB.Resource.Drawable.IMALLCAPS;
	}
}",
					},
					new AndroidItem.AndroidResource ("Resources\\layout\\test.axml") {
						TextContent = () => {
							return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ImageView xmlns:android=\"http://schemas.android.com/apk/res/android\" android:src=\"@drawable/IMALLCAPS\" />";
						}
					}
				}
			};
			libB.OtherBuildItems.Add (new AndroidItem.AndroidAsset ("Assets\\foo\\foo.txt") {
				BinaryContent = () => Array.Empty<byte> (),
			});
			libB.OtherBuildItems.Add (new AndroidItem.AndroidResource ("Resources\\layout\\MyLayout.axml") {
				TextContent = () => "<?xml version=\"1.0\" encoding=\"utf-8\" ?><LinearLayout xmlns:android=\"http://schemas.android.com/apk/res/android\" />"
			});
			libB.OtherBuildItems.Add (new AndroidItem.AndroidResource ("Resources\\raw\\bar.txt") {
				BinaryContent = () => Array.Empty<byte> (),
			});
			libB.OtherBuildItems.Add (new AndroidItem.AndroidEnvironment ("env.txt") {
				TextContent = () => $"{env_var}={env_val}",
			});
			libB.OtherBuildItems.Add (new AndroidItem.AndroidEnvironment ("sub\\directory\\env.txt") {
				TextContent = () => $"{env_var}={env_val}",
			});
			libB.OtherBuildItems.Add (new AndroidItem.AndroidLibrary ("sub\\directory\\foo.jar") {
				BinaryContent = () => ResourceData.JavaSourceJarTestJar,
			});
			libB.OtherBuildItems.Add (new AndroidItem.AndroidLibrary ("sub\\directory\\arm64-v8a\\libfoo.so") {
				BinaryContent = () => Array.Empty<byte> (),
			});
			libB.OtherBuildItems.Add (new AndroidItem.AndroidNativeLibrary (default (Func<string>)) {
				Update = () => "libfoo.so",
				MetadataValues = "Link=x86\\libfoo.so",
				BinaryContent = () => Array.Empty<byte> (),
			});
			libB.AddReference (libC);

			activity = libB.Sources.FirstOrDefault (s => s.Include () == "MainActivity.cs");
			if (activity != null)
				libB.Sources.Remove (activity);
			var libBBuilder = CreateDotNetBuilder (libB, Path.Combine (path, libB.ProjectName));
			Assert.IsTrue (libBBuilder.Build (), $"{libB.ProjectName} should succeed");

			// Check .aar file for class library
			var aarPath = Path.Combine (FullProjectDirectory, libB.OutputPath, $"{libB.ProjectName}.aar");
			FileAssert.Exists (aarPath);
			using (var aar = ZipHelper.OpenZip (aarPath)) {
				aar.AssertContainsEntry (aarPath, "assets/foo/foo.txt");
				aar.AssertContainsEntry (aarPath, "res/layout/mylayout.xml");
				aar.AssertContainsEntry (aarPath, "res/raw/bar.txt");
				aar.AssertContainsEntry (aarPath, ".net/__res_name_case_map.txt");
				aar.AssertContainsEntry (aarPath, ".net/env/190E30B3D205731E.env");
				aar.AssertContainsEntry (aarPath, ".net/env/2CBDAB7FEEA94B19.env");
				aar.AssertContainsEntry (aarPath, "libs/A1AFA985571E728E.jar");
				aar.AssertContainsEntry (aarPath, "jni/arm64-v8a/libfoo.so");
				aar.AssertContainsEntry (aarPath, "jni/x86/libfoo.so");
			}

			// Check EmbeddedResource files do not exist
			var assemblyPath = Path.Combine (FullProjectDirectory, libB.OutputPath, $"{libB.ProjectName}.dll");
			FileAssert.Exists (assemblyPath);
			using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
				Assert.AreEqual (0, assembly.MainModule.Resources.Count);
			}

			var appA = new XASdkProject {
				ProjectName = "AppA",
				IsRelease = isRelease,
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar : Foo { }",
					}
				}
			};
			appA.AddReference (libB);
			if (duplicateAar) {
				// Test a duplicate @(AndroidLibrary) item with the same path of LibraryB.aar
				appA.OtherBuildItems.Add (new AndroidItem.AndroidLibrary (aarPath));
			}
			var appBuilder = CreateDotNetBuilder (appA, Path.Combine (path, appA.ProjectName));
			Assert.IsTrue (appBuilder.Build (), $"{appA.ProjectName} should succeed");

			// Check .apk for assets, res, and native libraries
			var apkPath = Path.Combine (FullProjectDirectory, appA.OutputPath, $"{appA.PackageName}.apk");
			FileAssert.Exists (apkPath);
			using (var apk = ZipHelper.OpenZip (apkPath)) {
				apk.AssertContainsEntry (apkPath, "assets/foo/foo.txt");
				apk.AssertContainsEntry (apkPath, "assets/bar/bar.txt");
				apk.AssertContainsEntry (aarPath, "res/layout/mylayout.xml");
				apk.AssertContainsEntry (apkPath, "res/raw/bar.txt");
				apk.AssertContainsEntry (apkPath, "lib/arm64-v8a/libfoo.so");
				apk.AssertContainsEntry (apkPath, "lib/x86/libfoo.so");
			}

			// Check classes.dex contains foo.jar
			var intermediate = Path.Combine (FullProjectDirectory, appA.IntermediateOutputPath);
			var dexFile = Path.Combine (intermediate, "android", "bin", "classes.dex");
			FileAssert.Exists (dexFile);
			string className = "Lcom/xamarin/android/test/msbuildtest/JavaSourceJarTest;";
			Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");

			// Check environment variable
			var environmentFiles = EnvironmentHelper.GatherEnvironmentFiles (intermediate, "x86", required: true);
			var environmentVariables = EnvironmentHelper.ReadEnvironmentVariables (environmentFiles);
			Assert.IsTrue (environmentVariables.TryGetValue (env_var, out string actual), $"Environment should contain {env_var}");
			Assert.AreEqual (env_val, actual, $"{env_var} should be {env_val}");

			// Check Resource.designer.cs
			var resource_designer_cs = Path.Combine (intermediate, "Resource.designer.cs");
			FileAssert.Exists (resource_designer_cs);
			var resource_designer_text = File.ReadAllText (resource_designer_cs);
			StringAssert.Contains ("public const int MyLayout", resource_designer_text);
			StringAssert.Contains ("global::LibraryB.Resource.Drawable.IMALLCAPS = global::AppA.Resource.Drawable.IMALLCAPS", resource_designer_text);
		}

		[Test]
		public void DotNetNew ([Values ("android", "androidlib", "android-bindinglib")] string template)
		{
			var dotnet = CreateDotNetBuilder ();
			Assert.IsTrue (dotnet.New (template), $"`dotnet new {template}` should succeed");
			File.WriteAllBytes (Path.Combine (dotnet.ProjectDirectory, "foo.jar"), ResourceData.JavaSourceJarTestJar);
			Assert.IsTrue (dotnet.New ("android-activity"), "`dotnet new android-activity` should succeed");
			Assert.IsTrue (dotnet.New ("android-layout", Path.Combine (dotnet.ProjectDirectory, "Resources", "layout")), "`dotnet new android-layout` should succeed");
			Assert.IsTrue (dotnet.Build (), "`dotnet build` should succeed");
		}

		[Test]
		public void DotNetPack ([Values ("net6.0-android", "net6.0-android30")] string targetFramework)
		{
			var proj = new XASdkProject (outputType: "Library") {
				TargetFramework = targetFramework,
				IsRelease = true,
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () => "public class Foo { }",
					}
				}
			};
			proj.OtherBuildItems.Add (new AndroidItem.AndroidResource ("Resources\\raw\\bar.txt") {
				BinaryContent = () => Array.Empty<byte> (),
			});
			proj.OtherBuildItems.Add (new AndroidItem.AndroidLibrary ("sub\\directory\\foo.jar") {
				BinaryContent = () => ResourceData.JavaSourceJarTestJar,
			});
			proj.OtherBuildItems.Add (new AndroidItem.AndroidLibrary ("sub\\directory\\arm64-v8a\\libfoo.so") {
				BinaryContent = () => Array.Empty<byte> (),
			});
			proj.OtherBuildItems.Add (new AndroidItem.AndroidNativeLibrary (default (Func<string>)) {
				Update = () => "libfoo.so",
				MetadataValues = "Link=x86\\libfoo.so",
				BinaryContent = () => Array.Empty<byte> (),
			});

			var dotnet = CreateDotNetBuilder (proj);
			Assert.IsTrue (dotnet.Pack (), "`dotnet pack` should succeed");

			var nupkgPath = Path.Combine (FullProjectDirectory, proj.OutputPath, "..", $"{proj.ProjectName}.1.0.0.nupkg");
			FileAssert.Exists (nupkgPath);
			using (var nupkg = ZipHelper.OpenZip (nupkgPath)) {
				nupkg.AssertContainsEntry (nupkgPath, $"lib/net6.0-android30.0/{proj.ProjectName}.dll");
				nupkg.AssertContainsEntry (nupkgPath, $"lib/net6.0-android30.0/{proj.ProjectName}.aar");
			}
		}

		[Test]
		public void DotNetLibraryAarChanges ()
		{
			var proj = new XASdkProject (outputType: "Library");
			proj.Sources.Add (new AndroidItem.AndroidResource ("Resources\\raw\\foo.txt") {
				TextContent = () => "foo",
			});
			proj.Sources.Add (new AndroidItem.AndroidResource ("Resources\\raw\\bar.txt") {
				TextContent = () => "bar",
			});

			var dotnet = CreateDotNetBuilder (proj);
			Assert.IsTrue (dotnet.Build (), "first build should succeed");
			var aarPath = Path.Combine (FullProjectDirectory, proj.OutputPath, $"{proj.ProjectName}.aar");
			FileAssert.Exists (aarPath);
			using (var aar = ZipHelper.OpenZip (aarPath)) {
				aar.AssertEntryContents (aarPath, "res/raw/foo.txt", contents: "foo");
				aar.AssertEntryContents (aarPath, "res/raw/bar.txt", contents: "bar");
			}

			// Change res/raw/bar.txt contents
			WaitFor (1000);
			var bar_txt = Path.Combine (FullProjectDirectory, "Resources", "raw", "bar.txt");
			File.WriteAllText (bar_txt, contents: "baz");
			Assert.IsTrue (dotnet.Build (), "second build should succeed");
			FileAssert.Exists (aarPath);
			using (var aar = ZipHelper.OpenZip (aarPath)) {
				aar.AssertEntryContents (aarPath, "res/raw/foo.txt", contents: "foo");
				aar.AssertEntryContents (aarPath, "res/raw/bar.txt", contents: "baz");
			}

			// Delete res/raw/bar.txt
			File.Delete (bar_txt);
			Assert.IsTrue (dotnet.Build (), "third build should succeed");
			FileAssert.Exists (aarPath);
			using (var aar = ZipHelper.OpenZip (aarPath)) {
				aar.AssertEntryContents (aarPath, "res/raw/foo.txt", contents: "foo");
				aar.AssertDoesNotContainEntry (aarPath, "res/raw/bar.txt");
			}
		}

		[Test]
		[Category ("SmokeTests")]
		public void DotNetBuildBinding ()
		{
			var proj = new XASdkProject (outputType: "Library");
			// Both transform files should be applied
			proj.Sources.Add (new AndroidItem.TransformFile ("Transforms.xml") {
				TextContent = () =>
@"<metadata>
  <attr path=""/api/package[@name='com.xamarin.android.test.msbuildtest']"" name=""managedName"">FooBar</attr>
</metadata>",
			});
			proj.Sources.Add (new AndroidItem.TransformFile ("Transforms\\Metadata.xml") {
				TextContent = () =>
@"<metadata>
  <attr path=""/api/package[@managedName='FooBar']"" name=""managedName"">MSBuildTest</attr>
</metadata>",
			});
			proj.Sources.Add (new AndroidItem.AndroidLibrary ("javaclasses.jar") {
				BinaryContent = () => ResourceData.JavaSourceJarTestJar,
			});
			proj.OtherBuildItems.Add (new BuildItem ("JavaSourceJar", "javaclasses-sources.jar") {
				BinaryContent = () => ResourceData.JavaSourceJarTestSourcesJar,
			});
			var dotnet = CreateDotNetBuilder (proj);
			Assert.IsTrue (dotnet.Build (), "`dotnet build` should succeed");

			var assemblyPath = Path.Combine (FullProjectDirectory, proj.OutputPath, "UnnamedProject.dll");
			FileAssert.Exists (assemblyPath);
			using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
				var typeName = "MSBuildTest.JavaSourceJarTest";
				var type = assembly.MainModule.GetType (typeName);
				Assert.IsNotNull (type, $"{assemblyPath} should contain {typeName}");
			}
		}

		static readonly object [] DotNetBuildSource = new object [] {
			new object [] {
				/* runtimeIdentifiers */ "android-arm",
				/* isRelease */          false,
				/* aot */                false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm64",
				/* isRelease */          false,
				/* aot */                false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-x86",
				/* isRelease */          false,
				/* aot */                false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-x64",
				/* isRelease */          false,
				/* aot */                false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm",
				/* isRelease */          true,
				/* aot */                false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm",
				/* isRelease */          true,
				/* aot */                true,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm64",
				/* isRelease */          true,
				/* aot */                false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm;android-arm64;android-x86;android-x64",
				/* isRelease */          false,
				/* aot */                false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm;android-arm64;android-x86",
				/* isRelease */          true,
				/* aot */                false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm;android-arm64;android-x86;android-x64",
				/* isRelease */          true,
				/* aot */                false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm;android-arm64;android-x86;android-x64",
				/* isRelease */          true,
				/* aot */                true,
			},
		};

		[Test]
		[Category ("SmokeTests")]
		[TestCaseSource (nameof (DotNetBuildSource))]
		public void DotNetBuild (string runtimeIdentifiers, bool isRelease, bool aot)
		{
			var proj = new XASdkProject {
				IsRelease = isRelease,
				ExtraNuGetConfigSources = {
					"https://pkgs.dev.azure.com/azure-public/vside/_packaging/xamarin-impl/nuget/v3/index.json"
				},
				PackageReferences = {
					new Package { Id = "Xamarin.AndroidX.AppCompat", Version = "1.2.0.7-net6preview01" },
					new Package { Id = "Microsoft.AspNetCore.Components.WebView", Version = "6.0.0-preview.5.21301.17" },
					new Package { Id = "Microsoft.Extensions.FileProviders.Embedded", Version = "6.0.0-preview.6.21306.3" },
					new Package { Id = "Microsoft.JSInterop", Version = "6.0.0-preview.6.21306.3" },
					new Package { Id = "System.Text.Json", Version = "6.0.0-preview.7.21323.3" },
				},
				Sources = {
					new BuildItem ("EmbeddedResource", "Foo.resx") {
						TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancel</value></data>")
					},
					new BuildItem ("EmbeddedResource", "Foo.es.resx") {
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
			proj.MainActivity = proj.DefaultMainActivity.Replace (": Activity", ": AndroidX.AppCompat.App.AppCompatActivity");
			if (aot) {
				proj.SetProperty ("RunAOTCompilation", "true");
			}
			proj.OtherBuildItems.Add (new AndroidItem.InputJar ("javaclasses.jar") {
				BinaryContent = () => ResourceData.JavaSourceJarTestJar,
			});
			proj.OtherBuildItems.Add (new BuildItem ("JavaSourceJar", "javaclasses-sources.jar") {
				BinaryContent = () => ResourceData.JavaSourceJarTestSourcesJar,
			});
			if (!runtimeIdentifiers.Contains (";")) {
				proj.SetProperty (KnownProperties.RuntimeIdentifier, runtimeIdentifiers);
			} else {
				proj.SetProperty (KnownProperties.RuntimeIdentifiers, runtimeIdentifiers);
			}

			var dotnet = CreateDotNetBuilder (proj);
			Assert.IsTrue (dotnet.Build (), "`dotnet build` should succeed");
			dotnet.AssertHasNoWarnings ();

			var outputPath = Path.Combine (FullProjectDirectory, proj.OutputPath);
			var intermediateOutputPath = Path.Combine (FullProjectDirectory, proj.IntermediateOutputPath);
			if (!runtimeIdentifiers.Contains (";")) {
				outputPath = Path.Combine (outputPath, runtimeIdentifiers);
				intermediateOutputPath = Path.Combine (intermediateOutputPath, runtimeIdentifiers);
			}

			var files = Directory.EnumerateFileSystemEntries (outputPath)
				.Select (Path.GetFileName)
				.OrderBy (f => f)
				.ToArray ();
			var expectedFiles = new[]{
				$"{proj.PackageName}.apk",
				$"{proj.PackageName}-Signed.apk",
				"es",
				$"{proj.ProjectName}.dll",
				$"{proj.ProjectName}.pdb",
				$"{proj.ProjectName}.runtimeconfig.json",
				$"{proj.ProjectName}.xml",
			};
			CollectionAssert.AreEqual (expectedFiles, files, $"Expected: {string.Join (";", expectedFiles)}\n   Found: {string.Join (";", files)}");

			var assemblyPath = Path.Combine (outputPath, $"{proj.ProjectName}.dll");
			FileAssert.Exists (assemblyPath);
			using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
				var typeName = "Com.Xamarin.Android.Test.Msbuildtest.JavaSourceJarTest";
				Assert.IsNotNull (assembly.MainModule.GetType (typeName), $"{assemblyPath} should contain {typeName}");
				typeName = "Com.Balysv.Material.Drawable.Menu.MaterialMenuView";
				Assert.IsNotNull (assembly.MainModule.GetType (typeName), $"{assemblyPath} should contain {typeName}");
			}

			var rids = runtimeIdentifiers.Split (';');
			if (isRelease) {
				// Check for stripped native libraries
				foreach (var rid in rids) {
					FileAssert.Exists (Path.Combine (intermediateOutputPath, "native", rid, "libmono-android.release.so"));
					FileAssert.Exists (Path.Combine (intermediateOutputPath, "native", rid, "libmonosgen-2.0.so"));
				}
			}

			bool expectEmbeddedAssembies = !(CommercialBuildAvailable && !isRelease);
			var apkPath = Path.Combine (outputPath, $"{proj.PackageName}.apk");
			FileAssert.Exists (apkPath);
			using (var apk = ZipHelper.OpenZip (apkPath)) {
				apk.AssertContainsEntry (apkPath, $"assemblies/{proj.ProjectName}.dll", shouldContainEntry: expectEmbeddedAssembies);
				apk.AssertContainsEntry (apkPath, $"assemblies/{proj.ProjectName}.pdb", shouldContainEntry: !CommercialBuildAvailable && !isRelease);
				apk.AssertContainsEntry (apkPath, $"assemblies/System.Linq.dll",        shouldContainEntry: expectEmbeddedAssembies);
				apk.AssertContainsEntry (apkPath, $"assemblies/es/{proj.ProjectName}.resources.dll", shouldContainEntry: expectEmbeddedAssembies);
				foreach (var abi in rids.Select (AndroidRidAbiHelper.RuntimeIdentifierToAbi)) {
					apk.AssertContainsEntry (apkPath, $"lib/{abi}/libmonodroid.so");
					apk.AssertContainsEntry (apkPath, $"lib/{abi}/libmonosgen-2.0.so");
					if (rids.Length > 1) {
						apk.AssertContainsEntry (apkPath, $"assemblies/{abi}/System.Private.CoreLib.dll",        shouldContainEntry: expectEmbeddedAssembies);
					} else {
						apk.AssertContainsEntry (apkPath, "assemblies/System.Private.CoreLib.dll",        shouldContainEntry: expectEmbeddedAssembies);
					}
				}
			}
		}

		[Test]
		[Category ("SmokeTests")]
		public void DotNetBuildXamarinForms ([Values (true, false)] bool useInterpreter)
		{
			var proj = new XamarinFormsXASdkProject ();
			proj.SetProperty ("UseInterpreter", useInterpreter.ToString ());
			var dotnet = CreateDotNetBuilder (proj);
			Assert.IsTrue (dotnet.Build (), "`dotnet build` should succeed");
			dotnet.AssertHasNoWarnings ();
		}

		[Test]
		public void DotNetPublish ([Values (false, true)] bool isRelease)
		{
			const string runtimeIdentifier = "android-arm";
			var proj = new XASdkProject {
				IsRelease = isRelease
			};
			proj.SetProperty (KnownProperties.RuntimeIdentifier, runtimeIdentifier);
			var dotnet = CreateDotNetBuilder (proj);
			Assert.IsTrue (dotnet.Publish (), "first `dotnet publish` should succeed");

			var publishDirectory = Path.Combine (FullProjectDirectory, proj.OutputPath, runtimeIdentifier, "publish");
			var apk = Path.Combine (publishDirectory, $"{proj.PackageName}.apk");
			var apkSigned = Path.Combine (publishDirectory, $"{proj.PackageName}-Signed.apk");
			FileAssert.Exists (apk);
			FileAssert.Exists (apkSigned);

			Assert.IsTrue (dotnet.Publish (parameters: new [] { "AndroidPackageFormat=aab" }), $"second `dotnet publish` should succeed");
			var aab = Path.Combine (publishDirectory, $"{proj.PackageName}.aab");
			var aabSigned = Path.Combine (publishDirectory, $"{proj.PackageName}-Signed.aab");
			FileAssert.DoesNotExist (apk);
			FileAssert.DoesNotExist (apkSigned);
			FileAssert.Exists (aab);
			FileAssert.Exists (aabSigned);
		}

		[Test]
		public void DefaultItems ()
		{
			void CreateEmptyFile (params string [] paths)
			{
				var path = Path.Combine (FullProjectDirectory, Path.Combine (paths));
				Directory.CreateDirectory (Path.GetDirectoryName (path));
				File.WriteAllText (path, contents: "");
			}

			var proj = new XASdkProject ();
			var dotnet = CreateDotNetBuilder (proj);

			// Build error -> no nested sub-directories in Resources
			CreateEmptyFile ("Resources", "drawable", "foo", "bar.png");
			CreateEmptyFile ("Resources", "raw", "foo", "bar.png");

			// Build error -> no files/directories that start with .
			CreateEmptyFile ("Resources", "raw", ".DS_Store");
			CreateEmptyFile ("Assets", ".DS_Store");
			CreateEmptyFile ("Assets", ".svn", "foo.txt");

			// Files that should work
			CreateEmptyFile ("Resources", "raw", "foo.txt");
			CreateEmptyFile ("Assets", "foo", "bar.txt");

			Assert.IsTrue (dotnet.Build (), "`dotnet build` should succeed");

			var apkPath = Path.Combine (FullProjectDirectory, proj.OutputPath, $"{proj.PackageName}.apk");
			FileAssert.Exists (apkPath);
			using (var apk = ZipHelper.OpenZip (apkPath)) {
				apk.AssertContainsEntry (apkPath, "res/raw/foo.txt");
				apk.AssertContainsEntry (apkPath, "assets/foo/bar.txt");
			}
		}

		[Test]
		public void XamarinLegacySdk ()
		{
			var proj = new XASdkProject (outputType: "Library") {
				Sdk = "Xamarin.Legacy.Sdk/0.1.0-alpha2",
				Sources = {
					new AndroidItem.AndroidLibrary ("javaclasses.jar") {
						BinaryContent = () => ResourceData.JavaSourceJarTestJar,
					}
				}
			};

			using var b = new Builder ();
			var dotnetTargetFramework = "net6.0-android30.0";
			var legacyTargetFrameworkVersion = b.LatestTargetFrameworkVersion ().TrimStart ('v');
			var legacyTargetFramework = $"monoandroid{legacyTargetFrameworkVersion}";
			proj.SetProperty ("TargetFramework",  value: "");
			proj.SetProperty ("TargetFrameworks", value: $"{dotnetTargetFramework};{legacyTargetFramework}");

			var dotnet = CreateDotNetBuilder (proj);
			Assert.IsTrue (dotnet.Pack (), "`dotnet pack` should succeed");

			var nupkgPath = Path.Combine (FullProjectDirectory, proj.OutputPath, $"{proj.ProjectName}.1.0.0.nupkg");
			FileAssert.Exists (nupkgPath);
			using var nupkg = ZipHelper.OpenZip (nupkgPath);
			nupkg.AssertContainsEntry (nupkgPath, $"lib/{dotnetTargetFramework}/{proj.ProjectName}.dll");
			nupkg.AssertContainsEntry (nupkgPath, $"lib/{legacyTargetFramework}/{proj.ProjectName}.dll");
		}

		[Test]
		public void MauiTargetFramework ([Values ("net6.0-android", "net6.0-android30", "net6.0-android30.0")] string targetFramework)
		{
			var library = new XASdkProject (outputType: "Library") {
				TargetFramework = targetFramework,
			};
			library.ExtraNuGetConfigSources.Add ("https://pkgs.dev.azure.com/azure-public/vside/_packaging/xamarin-impl/nuget/v3/index.json");
			library.Sources.Clear ();
			library.Sources.Add (new BuildItem.Source ("Foo.cs") {
				TextContent = () =>
@"using Microsoft.Maui;
using Microsoft.Maui.Handlers;

public abstract class Foo<TVirtualView, TNativeView> : AbstractViewHandler<TVirtualView, TNativeView>
	where TVirtualView : class, IView
#if ANDROID
	where TNativeView : Android.Views.View
#else
	where TNativeView : class
#endif
{
		protected Foo (PropertyMapper mapper) : base(mapper)
		{
#if ANDROID
			var t = this.Context;
#endif
		}
}",
			});

			library.PackageReferences.Add (new Package { Id = "Microsoft.Maui.Core", Version = "6.0.100-preview.3.269" });

			var dotnet = CreateDotNetBuilder (library);
			Assert.IsTrue (dotnet.Build (), $"{library.ProjectName} should succeed");
			dotnet.AssertHasNoWarnings ();
		}

		[Test]
		public void DotNetIncremental ()
		{
			// Setup dependencies App A -> Lib B
			var path = Path.Combine ("temp", TestName);

			var libB = new XASdkProject (outputType: "Library") {
				ProjectName = "LibraryB"
			};
			libB.Sources.Clear ();
			libB.Sources.Add (new BuildItem.Source ("Foo.cs") {
				TextContent = () => "public class Foo { }",
			});

			// Will save the project, does not need to build it
			CreateDotNetBuilder (libB, Path.Combine (path, libB.ProjectName));

			var appA = new XASdkProject {
				ProjectName = "AppA",
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar : Foo { }",
					}
				}
			};
			appA.AddReference (libB);
			var appBuilder = CreateDotNetBuilder (appA, Path.Combine (path, appA.ProjectName));
			Assert.IsTrue (appBuilder.Build (), $"{appA.ProjectName} should succeed");
			appBuilder.AssertTargetIsNotSkipped ("CoreCompile");

			// Build again, no changes
			Assert.IsTrue (appBuilder.Build (), $"{appA.ProjectName} should succeed");
			appBuilder.AssertTargetIsSkipped ("CoreCompile");
		}

		[Test]
		public void SignAndroidPackage ()
		{
			var proj = new XASdkProject ();
			var builder = CreateDotNetBuilder (proj);
			var parameters = new [] { "BuildingInsideVisualStudio=true" };
			Assert.IsTrue (builder.Build ("SignAndroidPackage", parameters), $"{proj.ProjectName} should succeed");
		}

		DotNetCLI CreateDotNetBuilder (string relativeProjectDir = null)
		{
			if (string.IsNullOrEmpty (relativeProjectDir)) {
				relativeProjectDir = Path.Combine ("temp", TestName);
			}
			TestOutputDirectories [TestContext.CurrentContext.Test.ID] =
				FullProjectDirectory = Path.Combine (Root, relativeProjectDir);
			new XASdkProject ().CopyNuGetConfig (relativeProjectDir);
			return new DotNetCLI (Path.Combine (FullProjectDirectory, $"{TestName}.csproj"));
		}

		DotNetCLI CreateDotNetBuilder (XASdkProject project, string relativeProjectDir = null)
		{
			if (string.IsNullOrEmpty (relativeProjectDir)) {
				relativeProjectDir = Path.Combine ("temp", TestName);
			}
			TestOutputDirectories [TestContext.CurrentContext.Test.ID] =
				FullProjectDirectory = Path.Combine (Root, relativeProjectDir);
			var files = project.Save ();
			project.Populate (relativeProjectDir, files);
			project.CopyNuGetConfig (relativeProjectDir);
			return new DotNetCLI (project, Path.Combine (FullProjectDirectory, project.ProjectFilePath));
		}
	}
}
