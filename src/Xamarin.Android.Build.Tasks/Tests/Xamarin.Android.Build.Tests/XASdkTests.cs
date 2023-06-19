using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Reflection;
using System.Text;
using Mono.Cecil;
using NUnit.Framework;
using Xamarin.Android.Tasks;
using Xamarin.Android.Tools;
using Xamarin.ProjectTools;
using Xamarin.Tools.Zip;
using Microsoft.Android.Build.Tasks;

#if !NET472
namespace Xamarin.Android.Build.Tests
{
	[Obsolete ("De-dupe and migrate these tests to Build, Package, or other test fixtures.")]
	[TestFixture]
	[NonParallelizable] // On MacOS, parallel /restore causes issues
	public class XASdkTests : BaseTest
	{
		static readonly object [] DotNetBuildLibrarySource = new object [] {
			new object [] {
				/* isRelease */           false,
				/* duplicateAar */        false,
				/* useDesignerAssembly */ false,
			},
			new object [] {
				/* isRelease */           false,
				/* duplicateAar */        true,
				/* useDesignerAssembly */ false,
			},
			new object [] {
				/* isRelease */           true,
				/* duplicateAar */        false,
				/* useDesignerAssembly */ false,
			},
			new object [] {
				/* isRelease */           false,
				/* duplicateAar */        false,
				/* useDesignerAssembly */ true,
			},
			new object [] {
				/* isRelease */           false,
				/* duplicateAar */        true,
				/* useDesignerAssembly */ true,
			},
			new object [] {
				/* isRelease */           true,
				/* duplicateAar */        false,
				/* useDesignerAssembly */ true,
			},
		};

		[Test]
		[TestCaseSource (nameof (DotNetBuildLibrarySource))]
		public void DotNetBuildLibrary (bool isRelease, bool duplicateAar, bool useDesignerAssembly)
		{
			var path = Path.Combine ("temp", TestName);
			var env_var = "MY_ENVIRONMENT_VAR";
			var env_val = "MY_VALUE";

			// Setup dependencies App A -> Lib B -> Lib C

			var libC = new XamarinAndroidLibraryProject {
				ProjectName = "LibraryC",
				IsRelease = isRelease,
				EnableDefaultItems = true,
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar { }",
					},
					new AndroidItem.AndroidResource (() => "Resources\\drawable\\IMALLCAPS.png") {
						BinaryContent = () => XamarinAndroidApplicationProject.icon_binary_mdpi,
					},
					new AndroidItem.ProguardConfiguration ("proguard.txt") {
						TextContent = () => "# LibraryC",
					},
				}
			};
			libC.OtherBuildItems.Add (new AndroidItem.AndroidAsset ("Assets\\bar\\bar.txt") {
				BinaryContent = () => Array.Empty<byte> (),
			});
			libC.SetProperty ("AndroidUseDesignerAssembly", useDesignerAssembly.ToString ());
			var activity = libC.Sources.FirstOrDefault (s => s.Include () == "MainActivity.cs");
			if (activity != null)
				libC.Sources.Remove (activity);
			var libCBuilder = CreateDllBuilder (Path.Combine ("temp", libC.ProjectName));
			Assert.IsTrue (libCBuilder.Build (libC), $"{libC.ProjectName} should succeed");

			var aarPath = Path.Combine (Root, libCBuilder.ProjectDirectory, libC.OutputPath, $"{libC.ProjectName}.aar");
			FileAssert.Exists (aarPath);
			using (var aar = ZipHelper.OpenZip (aarPath)) {
				aar.AssertContainsEntry (aarPath, "assets/bar/bar.txt");
				aar.AssertEntryEquals (aarPath, "proguard.txt", "# LibraryC");
			}

			var libB = new XamarinAndroidLibraryProject {
				ProjectName = "LibraryB",
				IsRelease = isRelease,
				EnableDefaultItems = true,
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
					},
					new AndroidItem.AndroidAsset ("Assets\\foo\\foo.txt") {
						BinaryContent = () => Array.Empty<byte> (),
					},
					new AndroidItem.AndroidResource ("Resources\\layout\\MyLayout.axml") {
						TextContent = () => "<?xml version=\"1.0\" encoding=\"utf-8\" ?><LinearLayout xmlns:android=\"http://schemas.android.com/apk/res/android\" />"
					},
					new AndroidItem.AndroidResource ("Resources\\raw\\bar.txt") {
						BinaryContent = () => Array.Empty<byte> (),
					},
					new AndroidItem.AndroidLibrary ("sub\\directory\\foo.jar") {
						BinaryContent = () => ResourceData.JavaSourceJarTestJar,
					},
					new AndroidItem.AndroidLibrary ("sub\\directory\\bar.aar") {
						WebContent = "https://repo1.maven.org/maven2/com/balysv/material-menu/1.1.0/material-menu-1.1.0.aar",
					},
					new AndroidItem.AndroidJavaSource ("JavaSourceTestExtension.java") {
						Encoding = Encoding.ASCII,
						TextContent = () => ResourceData.JavaSourceTestExtension,
					},
					new AndroidItem.ProguardConfiguration ("proguard.txt") {
						TextContent = () => "# LibraryB",
					},
				}
			};
			libB.OtherBuildItems.Add (new AndroidItem.AndroidEnvironment ("env.txt") {
				TextContent = () => $"{env_var}={env_val}",
			});
			libB.OtherBuildItems.Add (new AndroidItem.AndroidEnvironment ("sub\\directory\\env.txt") {
				TextContent = () => $"{env_var}={env_val}",
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
			libB.SetProperty ("AndroidUseDesignerAssembly", useDesignerAssembly.ToString ());

			activity = libB.Sources.FirstOrDefault (s => s.Include () == "MainActivity.cs");
			if (activity != null)
				libB.Sources.Remove (activity);
			var libBBuilder = CreateDllBuilder (Path.Combine ("temp", libB.ProjectName));
			Assert.IsTrue (libBBuilder.Build (libB), $"{libB.ProjectName} should succeed");

			var projectJarHash = Files.HashString (Path.Combine (libB.IntermediateOutputPath,
					"binding", "bin", $"{libB.ProjectName}.jar").Replace ("\\", "/"));

			// Check .aar file for class library
			var libBOutputPath = Path.Combine (Root, libBBuilder.ProjectDirectory, libB.OutputPath);
			aarPath = Path.Combine (libBOutputPath, $"{libB.ProjectName}.aar");
			FileAssert.Exists (aarPath);
			FileAssert.Exists (Path.Combine (libBOutputPath, "bar.aar"));
			using (var aar = ZipHelper.OpenZip (aarPath)) {
				aar.AssertContainsEntry (aarPath, "assets/foo/foo.txt");
				aar.AssertContainsEntry (aarPath, "res/layout/mylayout.xml");
				aar.AssertContainsEntry (aarPath, "res/raw/bar.txt");
				aar.AssertContainsEntry (aarPath, ".net/__res_name_case_map.txt");
				aar.AssertContainsEntry (aarPath, ".net/env/190E30B3D205731E.env");
				aar.AssertContainsEntry (aarPath, ".net/env/2CBDAB7FEEA94B19.env");
				aar.AssertContainsEntry (aarPath, "libs/A1AFA985571E728E.jar");
				aar.AssertContainsEntry (aarPath, $"libs/{projectJarHash}.jar");
				aar.AssertContainsEntry (aarPath, "jni/arm64-v8a/libfoo.so");
				aar.AssertContainsEntry (aarPath, "jni/x86/libfoo.so");
				// proguard.txt from Library C should not flow to Library B and "double"
				aar.AssertEntryEquals (aarPath, "proguard.txt", "# LibraryB");
			}

			// Check EmbeddedResource files do not exist
			var assemblyPath = Path.Combine (Root, libBBuilder.ProjectDirectory, libB.OutputPath, $"{libB.ProjectName}.dll");
			FileAssert.Exists (assemblyPath);
			using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
				Assert.AreEqual (0, assembly.MainModule.Resources.Count);
			}

			var appA = new XamarinAndroidApplicationProject {
				ProjectName = "AppA",
				IsRelease = isRelease,
				EnableDefaultItems = true,
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
			appA.SetProperty ("AndroidUseDesignerAssembly", useDesignerAssembly.ToString ());
			var appBuilder = CreateApkBuilder (Path.Combine ("temp", appA.ProjectName));
			Assert.IsTrue (appBuilder.Build (appA), $"{appA.ProjectName} should succeed");

			// Check .apk/.aab for assets, res, and native libraries
			var apkPath = Path.Combine (Root, appBuilder.ProjectDirectory, appA.OutputPath, $"{appA.PackageName}-Signed.apk");
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
			var intermediate = Path.Combine (Root, appBuilder.ProjectDirectory, appA.IntermediateOutputPath);
			var dexFile = Path.Combine (intermediate, "android", "bin", "classes.dex");
			FileAssert.Exists (dexFile);
			var proguardFiles = Directory.GetFiles (Path.Combine (intermediate, "lp"), "proguard.txt", SearchOption.AllDirectories);
			Assert.AreEqual (2, proguardFiles.Length, "There should be only two proguard.txt files.");
			string className = "Lcom/xamarin/android/test/msbuildtest/JavaSourceJarTest;";
			Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
			className = "Lcom/xamarin/android/test/msbuildtest/JavaSourceTestExtension;";
			Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");

			// Check environment variable
			var environmentFiles = EnvironmentHelper.GatherEnvironmentFiles (intermediate, "x86", required: true);
			var environmentVariables = EnvironmentHelper.ReadEnvironmentVariables (environmentFiles);
			Assert.IsTrue (environmentVariables.TryGetValue (env_var, out string actual), $"Environment should contain {env_var}");
			Assert.AreEqual (env_val, actual, $"{env_var} should be {env_val}");

			// Check Resource.designer.cs
			if (!useDesignerAssembly) {
				var resource_designer_cs = Path.Combine (intermediate, "Resource.designer.cs");
				FileAssert.Exists (resource_designer_cs);
				var resource_designer_text = File.ReadAllText (resource_designer_cs);
				StringAssert.Contains ("public const int MyLayout", resource_designer_text);
				StringAssert.Contains ("global::LibraryB.Resource.Drawable.IMALLCAPS = global::AppA.Resource.Drawable.IMALLCAPS", resource_designer_text);
			}
		}

		[Test]
		public void DotNetNew ([Values ("android", "androidlib", "android-bindinglib", "androidwear")] string template)
		{
			var templateName = TestName.Replace ("-", "");
			var templatePath = Path.Combine (Root, "temp", templateName);
			if (Directory.Exists (templatePath))
				Directory.Delete (templatePath, true);

			TestOutputDirectories [TestContext.CurrentContext.Test.ID] = templatePath;
			var dotnet = new DotNetCLI (Path.Combine (templatePath, $"{templateName}.csproj"));
			Assert.IsTrue (dotnet.New (template), $"`dotnet new {template}` should succeed");
			File.WriteAllBytes (Path.Combine (dotnet.ProjectDirectory, "foo.jar"), ResourceData.JavaSourceJarTestJar);
			Assert.IsTrue (dotnet.New ("android-activity"), "`dotnet new android-activity` should succeed");
			Assert.IsTrue (dotnet.New ("android-layout", Path.Combine (dotnet.ProjectDirectory, "Resources", "layout")), "`dotnet new android-layout` should succeed");

			// Debug build
			Assert.IsTrue (dotnet.Build (parameters: new [] { "Configuration=Debug" }), "`dotnet build` should succeed");
			dotnet.AssertHasNoWarnings ();

			// Release build
			Assert.IsTrue (dotnet.Build (parameters: new [] { "Configuration=Release" }), "`dotnet build` should succeed");
			dotnet.AssertHasNoWarnings ();
		}

		static readonly object[] DotNetPackTargetFrameworks = new object[] {
			new object[] {
				"net7.0",
				"android",
				33, // TODO: use XABuildConfig.AndroidDefaultTargetDotnetApiLevel when we add 34 to .NET 7
			},
			new object[] {
				"net7.0",
				"android33",
				33, // TODO: use XABuildConfig.AndroidDefaultTargetDotnetApiLevel when we add 34 to .NET 7
			},
			new object[] {
				"net8.0",
				"android",
				XABuildConfig.AndroidDefaultTargetDotnetApiLevel,
			},
			new object[] {
				"net8.0",
				$"android{XABuildConfig.AndroidDefaultTargetDotnetApiLevel}",
				XABuildConfig.AndroidDefaultTargetDotnetApiLevel,
			},
		};

		[Test]
		[TestCaseSource (nameof (DotNetPackTargetFrameworks))]
		public void DotNetPack (string dotnetVersion, string platform, int apiLevel)
		{
			var targetFramework = $"{dotnetVersion}-{platform}";
			var proj = new XamarinAndroidLibraryProject {
				TargetFramework = targetFramework,
				IsRelease = true,
				EnableDefaultItems = true,
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () => "public class Foo { }",
					},
					new AndroidItem.AndroidResource ("Resources\\raw\\bar.txt") {
						BinaryContent = () => Array.Empty<byte> (),
					},
					new AndroidItem.AndroidLibrary ("sub\\directory\\foo.jar") {
						BinaryContent = () => ResourceData.JavaSourceJarTestJar,
					},
					new AndroidItem.AndroidLibrary ("sub\\directory\\bar.aar") {
						WebContent = "https://repo1.maven.org/maven2/com/balysv/material-menu/1.1.0/material-menu-1.1.0.aar",
					},
					new AndroidItem.AndroidJavaSource ("JavaSourceTest.java") {
						Encoding = Encoding.ASCII,
						TextContent = () =>
@"package com.xamarin.android.test.msbuildtest;
public class JavaSourceTest {
	public String Say (String quote) {
		return quote;
	}
}",
					},
				},
			};
			if (IsPreviewFrameworkVersion (targetFramework)) {
				proj.SetProperty ("EnablePreviewFeatures", "true");
			}
			proj.OtherBuildItems.Add (new AndroidItem.AndroidLibrary ("sub\\directory\\arm64-v8a\\libfoo.so") {
				BinaryContent = () => Array.Empty<byte> (),
			});
			proj.OtherBuildItems.Add (new AndroidItem.AndroidNativeLibrary (default (Func<string>)) {
				Update = () => "libfoo.so",
				MetadataValues = "Link=x86\\libfoo.so",
				BinaryContent = () => Array.Empty<byte> (),
			});
			proj.OtherBuildItems.Add (new AndroidItem.LibraryProjectZip ("..\\baz.aar") {
				WebContent = "https://repo1.maven.org/maven2/com/balysv/material-menu/1.1.0/material-menu-1.1.0.aar",
				MetadataValues = "Bind=false",
			});
			proj.OtherBuildItems.Add (new AndroidItem.AndroidLibrary (default (Func<string>)) {
				Update = () => "nopack.aar",
				WebContent = "https://repo1.maven.org/maven2/com/balysv/material-menu/1.1.0/material-menu-1.1.0.aar",
				MetadataValues = "Pack=false;Bind=false",
			});

			var projBuilder = CreateDllBuilder ();
			projBuilder.Save (proj);
			var dotnet = new DotNetCLI (Path.Combine (Root, projBuilder.ProjectDirectory, proj.ProjectFilePath));
			Assert.IsTrue (dotnet.Pack (parameters: new [] { "Configuration=Release" }), "`dotnet pack` should succeed");

			var nupkgPath = Path.Combine (Root, projBuilder.ProjectDirectory, proj.OutputPath, $"{proj.ProjectName}.1.0.0.nupkg");
			FileAssert.Exists (nupkgPath);
			using var nupkg = ZipHelper.OpenZip (nupkgPath);
			nupkg.AssertContainsEntry (nupkgPath, $"lib/{dotnetVersion}-android{apiLevel}.0/{proj.ProjectName}.dll");
			nupkg.AssertContainsEntry (nupkgPath, $"lib/{dotnetVersion}-android{apiLevel}.0/{proj.ProjectName}.aar");
			nupkg.AssertContainsEntry (nupkgPath, $"lib/{dotnetVersion}-android{apiLevel}.0/bar.aar");
			nupkg.AssertDoesNotContainEntry (nupkgPath, "content/bar.aar");
			nupkg.AssertDoesNotContainEntry (nupkgPath, "content/sub/directory/bar.aar");
			nupkg.AssertDoesNotContainEntry (nupkgPath, $"contentFiles/any/{dotnetVersion}-android{apiLevel}.0/sub/directory/bar.aar");
			nupkg.AssertDoesNotContainEntry (nupkgPath, $"lib/{dotnetVersion}-android{apiLevel}.0/nopack.aar");
			nupkg.AssertDoesNotContainEntry (nupkgPath, "content/nopack.aar");
			nupkg.AssertDoesNotContainEntry (nupkgPath, $"contentFiles/any/{dotnetVersion}-android{apiLevel}.0/nopack.aar");
			nupkg.AssertContainsEntry (nupkgPath, $"lib/{dotnetVersion}-android{apiLevel}.0/baz.aar");
		}

		[Test]
		public void DotNetLibraryAarChanges ()
		{
			var proj = new XamarinAndroidLibraryProject () {
				EnableDefaultItems = true,
			};
			proj.Sources.Add (new AndroidItem.AndroidResource ("Resources\\raw\\foo.txt") {
				TextContent = () => "foo",
			});
			proj.Sources.Add (new AndroidItem.AndroidResource ("Resources\\raw\\bar.txt") {
				TextContent = () => "bar",
			});

			var builder = CreateDllBuilder ();
			Assert.IsTrue (builder.Build (proj, doNotCleanupOnUpdate: true), "first build should succeed");
			var aarPath = Path.Combine (Root, builder.ProjectDirectory, proj.OutputPath, $"{proj.ProjectName}.aar");
			FileAssert.Exists (aarPath);
			using (var aar = ZipHelper.OpenZip (aarPath)) {
				aar.AssertEntryContents (aarPath, "res/raw/foo.txt", contents: "foo");
				aar.AssertEntryContents (aarPath, "res/raw/bar.txt", contents: "bar");
			}

			// Change res/raw/bar.txt contents
			WaitFor (1000);
			var bar_txt = Path.Combine (Root, builder.ProjectDirectory, "Resources", "raw", "bar.txt");
			File.WriteAllText (bar_txt, contents: "baz");
			Assert.IsTrue (builder.Build (proj, doNotCleanupOnUpdate: true), "second build should succeed");
			FileAssert.Exists (aarPath);
			using (var aar = ZipHelper.OpenZip (aarPath)) {
				aar.AssertEntryContents (aarPath, "res/raw/foo.txt", contents: "foo");
				aar.AssertEntryContents (aarPath, "res/raw/bar.txt", contents: "baz");
			}

			// Delete res/raw/bar.txt
			File.Delete (bar_txt);
			proj.Sources.Remove (proj.Sources.Last ());
			Assert.IsTrue (builder.Build (proj), "third build should succeed");
			FileAssert.Exists (aarPath);
			using (var aar = ZipHelper.OpenZip (aarPath)) {
				aar.AssertEntryContents (aarPath, "res/raw/foo.txt", contents: "foo");
				aar.AssertDoesNotContainEntry (aarPath, "res/raw/bar.txt");
			}
		}

		[Test]
		public void AppWithSingleJar ()
		{
			var proj = new XamarinAndroidApplicationProject {
				EnableDefaultItems = true,
				Sources = {
					new AndroidItem.AndroidLibrary ("Jars\\javaclasses.jar") {
						BinaryContent = () => ResourceData.JavaSourceJarTestJar,
					}
				}
			};

			var builder = CreateApkBuilder ();
			Assert.IsTrue (builder.Build (proj), "first build should succeed");

			var assemblyPath = Path.Combine (Root, builder.ProjectDirectory, proj.OutputPath, $"{proj.ProjectName}.dll");
			var typeName = "Com.Xamarin.Android.Test.Msbuildtest.JavaSourceJarTest";
			FileAssert.Exists (assemblyPath);
			using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
				Assert.IsNotNull (assembly.MainModule.GetType (typeName), $"{assemblyPath} should contain {typeName}");
			}

			// Remove the @(AndroidLibrary) & build again
			proj.Sources.RemoveAt (proj.Sources.Count - 1);
			Directory.Delete (Path.Combine (Root, builder.ProjectDirectory, "Jars"), recursive: true);
			Assert.IsTrue (builder.Build (proj), "second build should succeed");

			FileAssert.Exists (assemblyPath);
			using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
				Assert.IsNull (assembly.MainModule.GetType (typeName), $"{assemblyPath} should *not* contain {typeName}");
			}
		}

		[Test]
		public void GenerateResourceDesigner([Values (false, true)] bool generateResourceDesigner, [Values (false, true)] bool useDesignerAssembly)
		{
			var path = Path.Combine ("temp", TestName);
			var libraryB = new XamarinAndroidLibraryProject {
				ProjectName = "LibraryB",
				EnableDefaultItems = true,
			};
			libraryB.Sources.Clear ();
			libraryB.Sources.Add (new BuildItem.Source ("Foo.cs") {
				TextContent = () => @"namespace LibraryB;
public class Foo {
	public static int foo => Resource.Drawable.foo;
}",
			});
			libraryB.Sources.Add (new AndroidItem.AndroidResource (() => "Resources\\drawable\\foo.png") {
				BinaryContent = () => XamarinAndroidCommonProject.icon_binary_mdpi,
			});
			libraryB.SetProperty ("AndroidUseDesignerAssembly", useDesignerAssembly.ToString ());
			var libraryA = new XamarinAndroidLibraryProject {
				ProjectName = "LibraryA",
				EnableDefaultItems = true,
			};
			libraryA.Sources.Clear ();
			libraryA.Sources.Add (new BuildItem.Source ("FooA.cs") {
				TextContent = () => @"namespace LibraryA;
public class FooA {
	public int foo => 0;
	public int foo2 => LibraryB.Foo.foo;
	public int foo3 => LibraryB.Resource.Drawable.foo;
}",
			});
			libraryA.AddReference (libraryB);
			libraryA.SetProperty ("AndroidGenerateResourceDesigner", generateResourceDesigner.ToString ());
			if (!useDesignerAssembly)
				libraryA.SetProperty ("AndroidUseDesignerAssembly", "False");
			var libraryBBuilder = CreateDllBuilder (Path.Combine (path, libraryB.ProjectName));
			Assert.IsTrue (libraryBBuilder.Build (libraryB), "Build of LibraryB should succeed.");
			var libraryABuilder = CreateDllBuilder (Path.Combine (path, libraryA.ProjectName));
			Assert.IsTrue (libraryABuilder.Build (libraryA), "Build of LibraryA should succeed.");
			var proj = new XamarinAndroidApplicationProject {
				ProjectName = "App1",
				EnableDefaultItems = true,
			};
			proj.SetProperty ("AndroidUseDesignerAssembly", useDesignerAssembly.ToString ());
			proj.AddReference (libraryA);
			var builder = CreateApkBuilder ();
			Assert.IsTrue (builder.Build (proj), "Build of Proj should succeed.");
		}

		[Test]
		public void GenerateResourceDesigner_false([Values (false, true)] bool useDesignerAssembly)
		{
			var proj = new XamarinAndroidApplicationProject {
				EnableDefaultItems = true,
				Sources = {
					new AndroidItem.AndroidResource (() => "Resources\\drawable\\foo.png") {
						BinaryContent = () => XamarinAndroidCommonProject.icon_binary_mdpi,
					},
				}
			};
			proj.SetProperty (KnownProperties.OutputType, "Library");

			// Turn off Resource.designer.cs and remove usage of it
			proj.SetProperty ("AndroidGenerateResourceDesigner", "false");
			if (!useDesignerAssembly)
				proj.SetProperty ("AndroidUseDesignerAssembly", "false");
			proj.MainActivity = proj.DefaultMainActivity
				.Replace ("Resource.Layout.Main", "0")
				.Replace ("Resource.Id.myButton", "0");

			var builder = CreateDllBuilder ();
			Assert.IsTrue (builder.RunTarget(proj, "CoreCompile", parameters: new string[] { "BuildingInsideVisualStudio=true" }), "Designtime build should succeed.");
			var intermediate = Path.Combine (Root, builder.ProjectDirectory, proj.IntermediateOutputPath);
			var resource_designer_cs = Path.Combine (intermediate, "designtime",  "Resource.designer.cs");
			if (useDesignerAssembly)
				resource_designer_cs = Path.Combine (intermediate, "__Microsoft.Android.Resource.Designer.cs");
			FileAssert.DoesNotExist (resource_designer_cs);

			Assert.IsTrue (builder.Build (proj), "build should succeed");

			resource_designer_cs =  Path.Combine (intermediate, "Resource.designer.cs");
			if (useDesignerAssembly)
				resource_designer_cs = Path.Combine (intermediate, "__Microsoft.Android.Resource.Designer.cs");
			FileAssert.DoesNotExist (resource_designer_cs);

			var assemblyPath = Path.Combine (Root, builder.ProjectDirectory, proj.OutputPath, $"{proj.ProjectName}.dll");
			FileAssert.Exists (assemblyPath);
			using var assembly = AssemblyDefinition.ReadAssembly (assemblyPath);
			var typeName = $"{proj.ProjectName}.Resource";
			var type = assembly.MainModule.GetType (typeName);
			Assert.IsNull (type, $"{assemblyPath} should *not* contain {typeName}");
		}

		[Test]
		public void DotNetBuildBinding ()
		{
			var proj = new XamarinAndroidLibraryProject () {
				EnableDefaultItems = true,
			};
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
			proj.OtherBuildItems.Add (new AndroidItem.AndroidJavaSource ("JavaSourceTestExtension.java") {
				Encoding = Encoding.ASCII,
				TextContent = () => ResourceData.JavaSourceTestExtension,
				Metadata = { { "Bind", "True"} },
			});
			var builder = CreateDllBuilder ();
			Assert.IsTrue (builder.Build (proj), "`dotnet build` should succeed");

			var assemblyPath = Path.Combine (Root, builder.ProjectDirectory, proj.OutputPath, "UnnamedProject.dll");
			FileAssert.Exists (assemblyPath);
			using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
				var typeName = "MSBuildTest.JavaSourceJarTest";
				var type = assembly.MainModule.GetType (typeName);
				Assert.IsNotNull (type, $"{assemblyPath} should contain {typeName}");
				typeName = "MSBuildTest.JavaSourceTestExtension";
				type = assembly.MainModule.GetType (typeName);
				Assert.IsNotNull (type, $"{assemblyPath} should contain {typeName}");
			}
		}

		static readonly object [] DotNetBuildSource = new object [] {
			new object [] {
				/* runtimeIdentifiers */ "android-arm",
				/* isRelease */          false,
				/* aot */                false,
				/* usesAssemblyStore */  false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm",
				/* isRelease */          false,
				/* aot */                false,
				/* usesAssemblyStore */  true,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm64",
				/* isRelease */          false,
				/* aot */                false,
				/* usesAssemblyStore */  false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-x86",
				/* isRelease */          false,
				/* aot */                false,
				/* usesAssemblyStore */  false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-x64",
				/* isRelease */          false,
				/* aot */                false,
				/* usesAssemblyStore */  false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm",
				/* isRelease */          true,
				/* aot */                false,
				/* usesAssemblyStore */  false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm",
				/* isRelease */          true,
				/* aot */                false,
				/* usesAssemblyStore */  true,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm",
				/* isRelease */          true,
				/* aot */                true,
				/* usesAssemblyStore */  false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm",
				/* isRelease */          true,
				/* aot */                true,
				/* usesAssemblyStore */  true,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm64",
				/* isRelease */          true,
				/* aot */                false,
				/* usesAssemblyStore */  false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm;android-arm64;android-x86;android-x64",
				/* isRelease */          false,
				/* aot */                false,
				/* usesAssemblyStore */  false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm;android-arm64;android-x86;android-x64",
				/* isRelease */          false,
				/* aot */                false,
				/* usesAssemblyStore */  true,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm;android-arm64;android-x86",
				/* isRelease */          true,
				/* aot */                false,
				/* usesAssemblyStore */  false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm;android-arm64;android-x86;android-x64",
				/* isRelease */          true,
				/* aot */                false,
				/* usesAssemblyStore */  false,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm;android-arm64;android-x86;android-x64",
				/* isRelease */          true,
				/* aot */                false,
				/* usesAssemblyStore */  true,
			},
			new object [] {
				/* runtimeIdentifiers */ "android-arm;android-arm64;android-x86;android-x64",
				/* isRelease */          true,
				/* aot */                true,
				/* usesAssemblyStore */  false,
			},
		};

		[Test]
		[Category ("SmokeTests")]
		[TestCaseSource (nameof (DotNetBuildSource))]
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
					new Package { Id = "Microsoft.AspNetCore.Components.WebView", Version = "6.0.0-*" },
					new Package { Id = "Microsoft.Extensions.FileProviders.Embedded", Version = "6.0.0-*" },
					new Package { Id = "Microsoft.JSInterop", Version = "6.0.0-*" },
					new Package { Id = "System.Text.Json", Version = "6.0.0-*" },
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
			proj.SetProperty ("AndroidUseAssemblyStore", usesAssemblyStore.ToString ());
			proj.SetProperty ("RunAOTCompilation", aot.ToString ());
			proj.OtherBuildItems.Add (new AndroidItem.InputJar ("javaclasses.jar") {
				BinaryContent = () => ResourceData.JavaSourceJarTestJar,
			});
			proj.OtherBuildItems.Add (new BuildItem ("JavaSourceJar", "javaclasses-sources.jar") {
				BinaryContent = () => ResourceData.JavaSourceJarTestSourcesJar,
			});
			proj.OtherBuildItems.Add (new AndroidItem.AndroidJavaSource ("JavaSourceTestExtension.java") {
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

			bool expectEmbeddedAssembies = !(CommercialBuildAvailable && !isRelease);
			var apkPath = Path.Combine (outputPath, $"{proj.PackageName}-Signed.apk");
			FileAssert.Exists (apkPath);
			var helper = new ArchiveAssemblyHelper (apkPath, usesAssemblyStore, rids);
			helper.AssertContainsEntry ($"assemblies/{proj.ProjectName}.dll", shouldContainEntry: expectEmbeddedAssembies);
			helper.AssertContainsEntry ($"assemblies/{proj.ProjectName}.pdb", shouldContainEntry: !CommercialBuildAvailable && !isRelease);
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

		[Test]
		public void DotNetBuildXamarinForms ([Values (true, false)] bool useInterpreter)
		{
			var proj = new XamarinFormsAndroidApplicationProject ();
			proj.SetProperty ("UseInterpreter", useInterpreter.ToString ());
			var builder = CreateApkBuilder ();
			Assert.IsTrue (builder.Build (proj), "`dotnet build` should succeed");
			builder.AssertHasNoWarnings ();
		}

		static readonly object[] DotNetTargetFrameworks = new object[] {
			new object[] {
				"net7.0",
				"android",
				33, // TODO: use XABuildConfig.AndroidDefaultTargetDotnetApiLevel when we add 34 to .NET 7
			},
			new object[] {
				"net8.0",
				"android",
				XABuildConfig.AndroidDefaultTargetDotnetApiLevel,
			},

			new object[] {
				"net8.0",
				$"android{XABuildConfig.AndroidDefaultTargetDotnetApiLevel}",
				XABuildConfig.AndroidDefaultTargetDotnetApiLevel,
			},

			new object[] {
				"net8.0",
				XABuildConfig.AndroidLatestStableApiLevel == XABuildConfig.AndroidDefaultTargetDotnetApiLevel ? null : $"android{XABuildConfig.AndroidLatestStableApiLevel}.0",
				XABuildConfig.AndroidLatestStableApiLevel,
			},
			new object[] {
				"net8.0",
				XABuildConfig.AndroidLatestUnstableApiLevel == XABuildConfig.AndroidLatestStableApiLevel ? null : $"android{XABuildConfig.AndroidLatestUnstableApiLevel}.0",
				XABuildConfig.AndroidLatestUnstableApiLevel,
			},
		};

		static bool IsPreviewFrameworkVersion (string targetFramework)
		{
			return (targetFramework.Contains ($"{XABuildConfig.AndroidLatestUnstableApiLevel}")
				&& XABuildConfig.AndroidLatestUnstableApiLevel != XABuildConfig.AndroidLatestStableApiLevel);
		}

		[Test]
		public void DotNetPublishDefaultValues([Values (false, true)] bool isRelease)
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = isRelease
			};
			var builder = CreateDllBuilder ();
			builder.Save (proj);
			var dotnet = new DotNetCLI (Path.Combine (Root, builder.ProjectDirectory, proj.ProjectFilePath));
			Assert.IsTrue (dotnet.Publish (), "`dotnet publish` should succeed");
		}

		[Test]
		public void DotNetPublish ([Values (false, true)] bool isRelease, [ValueSource(nameof(DotNetTargetFrameworks))] object[] data)
		{
			var dotnetVersion = (string)data[0];
			var platform = (string)data[1];
			var apiLevel = (int)data[2];

			if (string.IsNullOrEmpty (platform))
				Assert.Ignore ($"Test for API level {apiLevel} was skipped as it matched the default or latest stable API level.");

			var targetFramework = $"{dotnetVersion}-{platform}";
			const string runtimeIdentifier = "android-arm";
			var proj = new XamarinAndroidApplicationProject {
				TargetFramework = targetFramework,
				IsRelease = isRelease,
				EnableDefaultItems = true,
			};
			proj.SetProperty (KnownProperties.RuntimeIdentifier, runtimeIdentifier);

			var preview = IsPreviewFrameworkVersion (targetFramework);
			if (preview) {
				proj.SetProperty ("EnablePreviewFeatures", "true");
			}

			var projBuilder = CreateDllBuilder ();
			projBuilder.Save (proj);
			var dotnet = new DotNetCLI (Path.Combine (Root, projBuilder.ProjectDirectory, proj.ProjectFilePath));
			string[] configParam = isRelease ? new [] { "Configuration=Release" } : new [] { "Configuration=Debug" };
			Assert.IsTrue (dotnet.Publish (parameters: configParam), "first `dotnet publish` should succeed");

			// NOTE: Preview API levels emit XA4211
			if (!preview) {
				// TODO: disabled in .NET 7 due to: https://github.com/dotnet/runtime/issues/77385
				if (dotnetVersion != "net7.0")
					dotnet.AssertHasNoWarnings ();
			}

			// Only check latest TFM, as previous will come from NuGet
			if (dotnetVersion == "net8.0") {
				var refDirectory = Directory.GetDirectories (Path.Combine (TestEnvironment.DotNetPreviewPacksDirectory, $"Microsoft.Android.Ref.{apiLevel}")).LastOrDefault ();
				var expectedMonoAndroidRefPath = Path.Combine (refDirectory, "ref", dotnetVersion, "Mono.Android.dll");
				Assert.IsTrue (dotnet.LastBuildOutput.ContainsText (expectedMonoAndroidRefPath), $"Build should be using {expectedMonoAndroidRefPath}");

				var runtimeApiLevel = (apiLevel == XABuildConfig.AndroidDefaultTargetDotnetApiLevel && apiLevel < XABuildConfig.AndroidLatestStableApiLevel) ? XABuildConfig.AndroidLatestStableApiLevel : apiLevel;
				var runtimeDirectory = Directory.GetDirectories (Path.Combine (TestEnvironment.DotNetPreviewPacksDirectory, $"Microsoft.Android.Runtime.{runtimeApiLevel}.{runtimeIdentifier}")).LastOrDefault ();
				var expectedMonoAndroidRuntimePath = Path.Combine (runtimeDirectory, "runtimes", runtimeIdentifier, "lib", dotnetVersion, "Mono.Android.dll");
				Assert.IsTrue (dotnet.LastBuildOutput.ContainsText (expectedMonoAndroidRuntimePath), $"Build should be using {expectedMonoAndroidRuntimePath}");
			}

			var publishDirectory = Path.Combine (Root, projBuilder.ProjectDirectory, proj.OutputPath, runtimeIdentifier, "publish");
			var apk = Path.Combine (publishDirectory, $"{proj.PackageName}.apk");
			var apkSigned = Path.Combine (publishDirectory, $"{proj.PackageName}-Signed.apk");
			// NOTE: the unsigned .apk doesn't exist when $(AndroidPackageFormats) is `aab;apk`
			if (!isRelease) {
				FileAssert.Exists (apk);
			}
			FileAssert.Exists (apkSigned);

			// NOTE: $(AndroidPackageFormats) defaults to `aab;apk` in Release
			if (isRelease) {
				var aab = Path.Combine (publishDirectory, $"{proj.PackageName}.aab");
				var aabSigned = Path.Combine (publishDirectory, $"{proj.PackageName}-Signed.aab");
				FileAssert.Exists (aab);
				FileAssert.Exists (aabSigned);
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

		[Test]
		public void XamarinLegacySdk ([Values ("net7.0-android33.0", "net8.0-android34.0")] string dotnetTargetFramework)
		{
			var proj = new XamarinAndroidLibraryProject {
				Sdk = "Xamarin.Legacy.Sdk/0.2.0-alpha4",
				EnableDefaultItems = true,
				Sources = {
					new AndroidItem.AndroidLibrary ("javaclasses.jar") {
						BinaryContent = () => ResourceData.JavaSourceJarTestJar,
					}
				}
			};

			// NOTE: keep this on the latest Xamarin.Android shipped
			var legacyTargetFrameworkVersion = "13.0";
			var legacyTargetFramework = $"monoandroid{legacyTargetFrameworkVersion}";
			proj.SetProperty ("TargetFramework",  value: "");
			proj.SetProperty ("TargetFrameworks", value: $"{dotnetTargetFramework};{legacyTargetFramework}");

			var projBuilder = CreateDllBuilder ();
			projBuilder.Save (proj);
			var dotnet = new DotNetCLI (Path.Combine (Root, projBuilder.ProjectDirectory, proj.ProjectFilePath));
			Assert.IsTrue (dotnet.Pack (parameters: new [] { "Configuration=Debug" }), "`dotnet pack` should succeed");

			var nupkgPath = Path.Combine (Root, projBuilder.ProjectDirectory, proj.OutputPath, $"{proj.ProjectName}.1.0.0.nupkg");
			FileAssert.Exists (nupkgPath);
			using var nupkg = ZipHelper.OpenZip (nupkgPath);
			nupkg.AssertContainsEntry (nupkgPath, $"lib/{dotnetTargetFramework}/{proj.ProjectName}.dll");
			nupkg.AssertContainsEntry (nupkgPath, $"lib/{legacyTargetFramework}/{proj.ProjectName}.dll");
		}

		[Test]
		[TestCaseSource (nameof (DotNetTargetFrameworks))]
		public void MauiTargetFramework (string dotnetVersion, string platform, int apiLevel)
		{
			if (string.IsNullOrEmpty (platform))
				Assert.Ignore ($"Test for API level {apiLevel} was skipped as it matched the default or latest stable API level.");

			var targetFramework = $"{dotnetVersion}-{platform}";
			var library = new XamarinAndroidLibraryProject {
				TargetFramework = targetFramework,
				EnableDefaultItems = true,
			};

			var preview = IsPreviewFrameworkVersion (targetFramework);
			if (preview) {
				library.SetProperty ("EnablePreviewFeatures", "true");
			}
			library.Sources.Clear ();
			library.Sources.Add (new BuildItem.Source ("Foo.cs") {
				TextContent = () =>
@"public abstract partial class ViewHandler<TVirtualView, TNativeView> { }

public interface IView { }

public abstract class Foo<TVirtualView, TNativeView> : ViewHandler<TVirtualView, TNativeView>
	where TVirtualView : class, IView
#if ANDROID
	where TNativeView : Android.Views.View
#else
	where TNativeView : class
#endif
{
}",
			});

			var builder = CreateDllBuilder ();
			Assert.IsTrue (builder.Build (library), $"{library.ProjectName} should succeed");
			// NOTE: Preview API levels emit XA4211
			if (!preview) {
				builder.AssertHasNoWarnings ();
			}
		}

		[Test]
		public void DotNetIncremental ([Values (true, false)] bool isRelease, [Values ("", "android-arm64")] string runtimeIdentifier)
		{
			// Setup dependencies App A -> Lib B
			var path = Path.Combine ("temp", TestName);

			var libB = new XamarinAndroidLibraryProject {
				ProjectName = "LibraryB",
				IsRelease = isRelease,
				EnableDefaultItems = true,
			};
			libB.Sources.Clear ();
			libB.Sources.Add (new BuildItem.Source ("Foo.cs") {
				TextContent = () => "public class Foo { }",
			});

			var libBBuilder = CreateDllBuilder (Path.Combine (path, libB.ProjectName));
			Assert.IsTrue (libBBuilder.Build(libB), $"{libB.ProjectName} should build");

			var appA = new XamarinAndroidApplicationProject {
				ProjectName = "AppA",
				IsRelease = isRelease,
				EnableDefaultItems = true,
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar : Foo { }",
					}
				}
			};
			appA.AddReference (libB);
			var appBuilder = CreateApkBuilder (Path.Combine (path, appA.ProjectName));
			appBuilder.BuildLogFile = Path.Combine (Root, path, appA.ProjectName, "build1.log");
			appA.SetProperty (KnownProperties.RuntimeIdentifier, runtimeIdentifier);
			Assert.IsTrue (appBuilder.Build (appA), $"{appA.ProjectName} should succeed");
			appBuilder.Output.AssertTargetIsNotSkipped ("CoreCompile", occurrence: 1);
			if (isRelease) {
				appBuilder.Output.AssertTargetIsNotSkipped ("_RemoveRegisterAttribute");
				appBuilder.Output.AssertTargetIsNotSkipped ("_AndroidAot");
			}

			// Build again, no changes
			appBuilder.BuildLogFile = Path.Combine (Root, path, appA.ProjectName, "build2.log");
			Assert.IsTrue (appBuilder.Build (appA), $"{appA.ProjectName} should succeed");
			appBuilder.Output.AssertTargetIsSkipped ("CoreCompile", occurrence: 2);
			if (isRelease) {
				appBuilder.Output.AssertTargetIsSkipped ("_RemoveRegisterAttribute");
				appBuilder.Output.AssertTargetIsSkipped ("_AndroidAotCompilation");
			}
		}

		[Test]
		public void ProjectDependencies ([Values(true, false)] bool projectReference)
		{
			// Setup dependencies App A -> Lib B -> Lib C
			var path = Path.Combine ("temp", TestName);

			var libB = new XamarinAndroidLibraryProject () {
				ProjectName = "LibraryB",
				IsRelease = true,
			};
			libB.Sources.Clear ();
			libB.Sources.Add (new BuildItem.Source ("Foo.cs") {
				TextContent = () => @"public class Foo {
					public Foo () {
						var bar = new Bar();
					}
				}",
			});

			var libC = new XamarinAndroidLibraryProject () {
				ProjectName = "LibraryC",
				IsRelease = true,
				AppendTargetFrameworkToOutputPath = true,
			};
			libC.Sources.Clear ();
			libC.Sources.Add (new BuildItem.Source ("Bar.cs") {
				TextContent = () => "public class Bar { }",
			});
			libC.Sources.Add (new BuildItem ("EmbeddedResource", "Foo.resx") {
				TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancel</value></data>")
			});
			libC.Sources.Add (new BuildItem ("EmbeddedResource", "Foo.es.resx") {
				TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancelar</value></data>")
			});

			// Add a @(Reference) or @(ProjectReference)
			if (projectReference) {
				libB.AddReference (libC);
			} else {
				libB.OtherBuildItems.Add (new BuildItem.Reference ($@"..\{libC.ProjectName}\bin\Release\{libC.TargetFramework}\{libC.ProjectName}.dll"));
			}

			// Build libraries
			var libCBuilder = CreateDllBuilder (Path.Combine (path, libC.ProjectName));
			Assert.IsTrue (libCBuilder.Build (libC), $"{libC.ProjectName} should succeed");
			var libBBuilder = CreateDllBuilder (Path.Combine (path, libB.ProjectName));
			Assert.IsTrue (libBBuilder.Build (libB), $"{libB.ProjectName} should succeed");

			var appA = new XamarinAndroidApplicationProject {
				ProjectName = "AppA",
				IsRelease = true,
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar : Foo { }",
					},
					new BuildItem ("EmbeddedResource", "Foo.resx") {
						TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancel</value></data>")
					},
					new BuildItem ("EmbeddedResource", "Foo.es.resx") {
						TextContent = () => InlineData.ResxWithContents ("<data name=\"CancelButton\"><value>Cancelar</value></data>")
					},
				}
			};
			appA.AddReference (libB);
			var appBuilder = CreateApkBuilder (Path.Combine (path, appA.ProjectName));
			Assert.IsTrue (appBuilder.Build (appA), $"{appA.ProjectName} should succeed");

			var apkPath = Path.Combine (Root, appBuilder.ProjectDirectory, appA.OutputPath, $"{appA.PackageName}-Signed.apk");
			FileAssert.Exists (apkPath);
			var helper = new ArchiveAssemblyHelper (apkPath);
			helper.AssertContainsEntry ($"assemblies/{appA.ProjectName}.dll");
			helper.AssertContainsEntry ($"assemblies/{libB.ProjectName}.dll");
			helper.AssertContainsEntry ($"assemblies/{libC.ProjectName}.dll");
			helper.AssertContainsEntry ($"assemblies/es/{appA.ProjectName}.resources.dll");
			helper.AssertContainsEntry ($"assemblies/es/{libC.ProjectName}.resources.dll");
		}

		[Test]
		public void DotNetDesignTimeBuild ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				EnableDefaultItems = true,
			};
			proj.SetProperty ("AndroidUseDesignerAssembly", "true");
			var builder = CreateApkBuilder ();
			var parameters = new [] { "BuildingInsideVisualStudio=true"};
			builder.BuildLogFile = "update.log";
			Assert.IsTrue (builder.RunTarget (proj, "Compile", parameters: parameters), $"{proj.ProjectName} should succeed");
			builder.Output.AssertTargetIsNotSkipped ("_GenerateResourceCaseMap", occurrence: 1);
			builder.Output.AssertTargetIsNotSkipped ("_GenerateRtxt");
			builder.Output.AssertTargetIsNotSkipped ("_GenerateResourceDesignerIntermediateClass");
			builder.Output.AssertTargetIsNotSkipped ("_GenerateResourceDesignerAssembly", occurrence: 1);
			parameters = new [] { "BuildingInsideVisualStudio=true" };
			builder.BuildLogFile = "build1.log";
			Assert.IsTrue (builder.RunTarget (proj, "SignAndroidPackage", parameters: parameters), $"{proj.ProjectName} should succeed");
			builder.Output.AssertTargetIsNotSkipped ("_GenerateResourceCaseMap", occurrence: 2);
			builder.Output.AssertTargetIsSkipped ("_GenerateRtxt", occurrence: 1);
			builder.Output.AssertTargetIsSkipped ("_GenerateResourceDesignerIntermediateClass", occurrence: 1);
			builder.Output.AssertTargetIsSkipped ("_GenerateResourceDesignerAssembly", occurrence: 2);
			builder.BuildLogFile = "build2.log";
			Assert.IsTrue (builder.RunTarget (proj, "SignAndroidPackage", parameters: parameters), $"{proj.ProjectName} should succeed 2");
			builder.Output.AssertTargetIsNotSkipped ("_GenerateResourceCaseMap", occurrence: 3);
			builder.Output.AssertTargetIsSkipped ("_GenerateRtxt", occurrence: 2);
			builder.Output.AssertTargetIsSkipped ("_GenerateResourceDesignerIntermediateClass", occurrence: 2);
			builder.Output.AssertTargetIsSkipped ("_GenerateResourceDesignerAssembly");
		}

		[Test]
		public void WearProjectJavaBuildFailure ()
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true,
				EnableDefaultItems = true,
				PackageReferences = {
					new Package { Id = "Xamarin.AndroidX.Wear", Version = "1.2.0.5" },
					new Package { Id = "Xamarin.Android.Wear", Version = "2.2.0" },
					new Package { Id = "Xamarin.AndroidX.PercentLayout", Version = "1.0.0.14" },
					new Package { Id = "Xamarin.AndroidX.Legacy.Support.Core.UI", Version = "1.0.0.14" },
				},
				SupportedOSPlatformVersion = "23",
			};
			var builder = CreateApkBuilder ();
			builder.ThrowOnBuildFailure = false;
			Assert.IsFalse (builder.Build (proj), $"{proj.ProjectName} should fail.");
			var text = $"java.lang.RuntimeException";
			Assert.IsTrue (StringAssertEx.ContainsText (builder.LastBuildOutput, text), $"Output did not contain '{text}'");
			text = $"is defined multiple times";
			Assert.IsTrue (StringAssertEx.ContainsText (builder.LastBuildOutput, text), $"Output did not contain '{text}'");
			text = $"is from 'androidx.core.core.aar'";
			Assert.IsTrue (StringAssertEx.ContainsText (builder.LastBuildOutput, text), $"Output did not contain '{text}'");
		}

		[Test]
		public void BenchmarkDotNet ()
		{
			var proj = new XamarinAndroidApplicationProject {
				EnableDefaultItems = true,
				PackageReferences = {
					new Package { Id = "BenchmarkDotNet", Version = "0.13.1" },
				}
			};
			var builder = CreateApkBuilder ();
			Assert.IsTrue (builder.Build (proj), $"{proj.ProjectName} should succeed");
			builder.AssertHasNoWarnings ();
		}

		static readonly object [] SettingCombinationsSource = new object [] {
			// Interpreter + AOT
			new object [] {
				/* isRelease */      true,
				/* useInterpreter */ true,
				/* publishTrimmed */ true,
				/* aot */            true,
				/* expected */       true,
			},
			// Debug + AOT
			new object [] {
				/* isRelease */      false,
				/* useInterpreter */ false,
				/* publishTrimmed */ true,
				/* aot */            true,
				/* expected */       true,
			},
			// Debug + PublishTrimmed
			new object [] {
				/* isRelease */      false,
				/* useInterpreter */ false,
				/* publishTrimmed */ true,
				/* aot */            false,
				/* expected */       true,
			},
			// AOT + PublishTrimmed=false
			new object [] {
				/* isRelease */      true,
				/* useInterpreter */ false,
				/* publishTrimmed */ false,
				/* aot */            true,
				/* expected */       false,
			},
		};

		[Test]
		[TestCaseSource (nameof (SettingCombinationsSource))]
		public void SettingCombinations (bool isRelease, bool useInterpreter, bool publishTrimmed, bool aot, bool expected)
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = isRelease,
				EnableDefaultItems = true,
			};
			proj.SetProperty ("UseInterpreter", useInterpreter.ToString ());
			proj.SetProperty ("PublishTrimmed", publishTrimmed.ToString ());
			proj.SetProperty ("RunAOTCompilation", aot.ToString ());
			var builder = CreateApkBuilder ();
			builder.ThrowOnBuildFailure = false;
			Assert.AreEqual (expected, builder.Build (proj), $"{proj.ProjectName} should {(expected ? "succeed" : "fail")}");
		}

		[Test]
		public void EolFrameworks()
		{
			var library = new XamarinAndroidLibraryProject () {
				TargetFramework = "net6.0-android",
				EnableDefaultItems = true,
			};
			var builder = CreateApkBuilder ();
			builder.ThrowOnBuildFailure = false;
			Assert.IsFalse (builder.Restore (library), $"{library.ProjectName} restore should fail");
			Assert.IsTrue (StringAssertEx.ContainsText (builder.LastBuildOutput, "NETSDK1202"), $"{builder.BuildLogFile} should have NETSDK1202.");
		}
	}
}
#endif
