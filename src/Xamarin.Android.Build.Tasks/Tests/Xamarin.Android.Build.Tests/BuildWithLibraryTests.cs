using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Android.Build.Tasks;
using Mono.Cecil;
using NUnit.Framework;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	public class BuildWithLibraryTests : BaseTest
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
			libC.OtherBuildItems.Add (new BuildItem ("None", "AndroidManifest.xml") {
				TextContent = () => @"<?xml version='1.0' encoding='utf-8'?>
<manifest xmlns:android='http://schemas.android.com/apk/res/android'>
  <queries>
    <package android:name='com.companyname.someappid' />
  </queries>
</manifest>",
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
				aar.AssertContainsEntry (aarPath, "AndroidManifest.xml");
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
					new AndroidItem.AndroidLibrary ("sub\\directory\\baz.aar") {
						WebContent = "https://repo1.maven.org/maven2/com/soundcloud/android/android-crop/1.0.1/android-crop-1.0.1.aar",
						MetadataValues = "Bind=false",
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
			libB.OtherBuildItems.Add (new AndroidItem.AndroidLibrary (default (Func<string>)) {
				Update = () => "sub\\directory\\baz.aar",
				MetadataValues = "Bind=false",
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
			FileAssert.Exists (Path.Combine (libBOutputPath, "baz.aar"));
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
			className = "Lcom/balysv/material/drawable/menu/MaterialMenu;"; // from material-menu-1.1.0.aar
			Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
			className = "Lcom/soundcloud/android/crop/Crop;"; // from android-crop-1.0.1.aar
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
				TextContent = () => "public class Foo : Bar { }",
			});

			var libC = new XamarinAndroidLibraryProject () {
				ProjectName = "LibraryC",
				IsRelease = true,
				AppendTargetFrameworkToOutputPath = true,
			};
			libC.Sources.Clear ();
			libC.Sources.Add (new BuildItem.Source ("Bar.cs") {
				TextContent = () => "public class Bar : Java.Lang.Object { }",
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
					new BuildItem.Source ("Baz.cs") {
						TextContent = () => "public class Baz : Foo { }",
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
			if (!projectReference) {
				// @(ProjectReference) implicits adds this reference. For `class Baz : Foo : Bar`:
				appA.OtherBuildItems.Add (new BuildItem.Reference ($@"..\{libC.ProjectName}\bin\Release\{libC.TargetFramework}\{libC.ProjectName}.dll"));
			}
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

			var intermediate = Path.Combine (Root, appBuilder.ProjectDirectory, appA.IntermediateOutputPath);
			var dexFile = Path.Combine (intermediate, "android", "bin", "classes.dex");
			FileAssert.Exists (dexFile);

			// NOTE: the crc hashes here might change one day, but if we used [Android.Runtime.Register("")]
			// LibraryB.dll would have a reference to Mono.Android.dll, which invalidates the test.
			string className = "Lcrc6414a4b78410c343a2/Bar;";
			Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
			className = "Lcrc646d2d82b4d8b39bd8/Foo;";
			Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
		}

		[Test]
		[NonParallelizable]
		public void BuildWithNativeLibraries ([Values (true, false)] bool isRelease)
		{
			var dll = new XamarinAndroidLibraryProject () {
				ProjectName = "Library1",
				IsRelease = isRelease,
				OtherBuildItems = {
					new AndroidItem.EmbeddedNativeLibrary ("foo\\armeabi-v7a\\libtest.so") {
						BinaryContent = () => new byte[10],
						MetadataValues = "Link=libs\\armeabi-v7a\\libtest.so",
					},
					new AndroidItem.EmbeddedNativeLibrary ("foo\\x86\\libtest.so") {
						BinaryContent = () => new byte[10],
						MetadataValues = "Link=libs\\x86\\libtest.so",
					},
					new AndroidItem.AndroidNativeLibrary ("armeabi-v7a\\libRSSupport.so") {
						BinaryContent = () => new byte[10],
					},
				},
			};
			var dll2 = new XamarinAndroidLibraryProject () {
				ProjectName = "Library2",
				IsRelease = isRelease,
				References = {
					new BuildItem ("ProjectReference","..\\Library1\\Library1.csproj"),
				},
				OtherBuildItems = {
					new AndroidItem.EmbeddedNativeLibrary ("foo\\armeabi-v7a\\libtest1.so") {
						BinaryContent = () => new byte[10],
						MetadataValues = "Link=libs\\armeabi-v7a\\libtest1.so",
					},
					new AndroidItem.EmbeddedNativeLibrary ("foo\\x86\\libtest1.so") {
						BinaryContent = () => new byte[10],
						MetadataValues = "Link=libs\\x86\\libtest1.so",
					},
				},
			};
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = isRelease,
				References = {
					new BuildItem ("ProjectReference","..\\Library1\\Library1.csproj"),
					new BuildItem ("ProjectReference","..\\Library2\\Library2.csproj"),
				},
				OtherBuildItems = {
					new AndroidItem.AndroidNativeLibrary ("armeabi-v7a\\libRSSupport.so") {
						BinaryContent = () => new byte[10],
					},
				}
			};
			proj.SetRuntimeIdentifiers (["armeabi-v7a", "x86"]);
			var path = Path.Combine (Root, "temp", string.Format ("BuildWithNativeLibraries_{0}", isRelease));
			using (var b1 = CreateDllBuilder (Path.Combine (path, dll2.ProjectName))) {
				Assert.IsTrue (b1.Build (dll2), "Build should have succeeded.");
				using (var b = CreateDllBuilder (Path.Combine (path, dll.ProjectName))) {
					Assert.IsTrue (b.Build (dll), "Build should have succeeded.");
					using (var builder = CreateApkBuilder (Path.Combine (path, proj.ProjectName))) {
						Assert.IsTrue (builder.Build (proj), "Build should have succeeded.");
						var apk = Path.Combine (Root, builder.ProjectDirectory,
							proj.OutputPath, $"{proj.PackageName}-Signed.apk");
						FileAssert.Exists (apk);
						Assert.IsTrue (StringAssertEx.ContainsText (builder.LastBuildOutput, "warning XA4301: APK already contains the item lib/armeabi-v7a/libRSSupport.so; ignoring."),
							"warning about skipping libRSSupport.so should have been raised");
						using (var zipFile = ZipHelper.OpenZip (apk)) {
							var data = ZipHelper.ReadFileFromZip (zipFile, "lib/x86/libtest.so");
							Assert.IsNotNull (data, "libtest.so for x86 should exist in the apk.");
							data = ZipHelper.ReadFileFromZip (zipFile, "lib/armeabi-v7a/libtest.so");
							Assert.IsNotNull (data, "libtest.so for armeabi-v7a should exist in the apk.");
							data = ZipHelper.ReadFileFromZip (zipFile, "lib/x86/libtest1.so");
							Assert.IsNotNull (data, "libtest1.so for x86 should exist in the apk.");
							data = ZipHelper.ReadFileFromZip (zipFile, "lib/armeabi-v7a/libtest1.so");
							Assert.IsNotNull (data, "libtest1.so for armeabi-v7a should exist in the apk.");
							data = ZipHelper.ReadFileFromZip (zipFile, "lib/armeabi-v7a/libRSSupport.so");
							Assert.IsNotNull (data, "libRSSupport.so for armeabi-v7a should exist in the apk.");
							data = ZipHelper.ReadFileFromZip (zipFile, "lib/x86/libSystem.Native.so");
							Assert.IsNotNull (data, "libSystem.Native.so for x86 should exist in the apk.");
							data = ZipHelper.ReadFileFromZip (zipFile, "lib/armeabi-v7a/libSystem.Native.so");
							Assert.IsNotNull (data, "libSystem.Native.so for armeabi-v7a should exist in the apk.");
						}
					}
				}
			}
		}

		[Test]
		public void BuildWithNativeLibraryUnknownAbi ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				OtherBuildItems = {
					new AndroidItem.AndroidNativeLibrary ("not-a-real-abi\\libtest.so") {
						BinaryContent = () => new byte[10],
					},
				}
			};
			proj.SetAndroidSupportedAbis ("armeabi-v7a", "x86");

			using (var builder = CreateApkBuilder (Path.Combine ("temp", TestContext.CurrentContext.Test.Name))) {
				builder.ThrowOnBuildFailure = false;
				Assert.IsFalse (builder.Build (proj), "Build should have failed.");
				Assert.IsTrue (StringAssertEx.ContainsText (builder.LastBuildOutput, $"error XA4301: Cannot determine ABI of native library 'not-a-real-abi{Path.DirectorySeparatorChar}libtest.so'. Move this file to a directory with a valid Android ABI name such as 'libs/armeabi-v7a/'."),
					"error about libtest.so should have been raised");
			}
		}

		[Test]
		public void BuildWithExternalJavaLibrary ()
		{
			var path = Path.Combine ("temp", TestName);
			var binding = new XamarinAndroidBindingProject {
				ProjectName = "BuildWithExternalJavaLibraryBinding",
				AndroidClassParser = "class-parse",
			};
			using (var bbuilder = CreateDllBuilder (Path.Combine (path, "BuildWithExternalJavaLibraryBinding"))) {
				string multidex_jar = Path.Combine (TestEnvironment.AndroidMSBuildDirectory, "android-support-multidex.jar");
				binding.Jars.Add (new AndroidItem.InputJar (() => multidex_jar));

				Assert.IsTrue (bbuilder.Build (binding), "Binding build should succeed.");
				var proj = new XamarinAndroidApplicationProject {
					References = { new BuildItem ("ProjectReference", "..\\BuildWithExternalJavaLibraryBinding\\BuildWithExternalJavaLibraryBinding.csproj"), },
					OtherBuildItems = { new BuildItem ("AndroidExternalJavaLibrary", multidex_jar) },
					Sources = {
						new BuildItem ("Compile", "Foo.cs") {
							TextContent = () => "public class Foo { public void X () { new Android.Support.Multidex.MultiDexApplication (); } }"
						}
					},
				};
				using (var builder = CreateApkBuilder (Path.Combine (path, "BuildWithExternalJavaLibrary"))) {
					Assert.IsTrue (builder.Build (proj), "App build should succeed");
				}
			}
		}

		[Test]
		public void AndroidLibraryProjectsZipWithOddPaths ()
		{
			var proj = new XamarinAndroidLibraryProject ();
			proj.Imports.Add (new Import ("foo.props") {
				TextContent = () => $@"
					<Project>
					  <PropertyGroup>
						<IntermediateOutputPath>$(MSBuildThisFileDirectory)../{TestContext.CurrentContext.Test.Name}/obj/$(Configuration)/foo/</IntermediateOutputPath>
					  </PropertyGroup>
					</Project>"
			});
			proj.AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\values\\foo.xml") {
				TextContent = () => @"<?xml version=""1.0"" encoding=""utf-8""?><resources><string name=""foo"">bar</string></resources>",
			});
			using (var b = CreateDllBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				var zipFile = Path.Combine (Root, b.ProjectDirectory, b.Output.OutputPath, $"{proj.ProjectName}.aar");
				FileAssert.Exists (zipFile);
				using (var zip = ZipHelper.OpenZip (zipFile)) {
					Assert.IsTrue (zip.ContainsEntry ("res/values/foo.xml"), $"{zipFile} should contain a res/values/foo.xml entry");
				}
			}
		}

		[Test]
		public void DuplicateJCWNames ()
		{
			var source = @"[Android.Runtime.Register (""examplelib.EmptyClass"")] public class EmptyClass : Java.Lang.Object { }";
			var library1 = new XamarinAndroidLibraryProject () {
				ProjectName = "Library1",
				Sources = {
					new BuildItem.Source ("EmptyClass.cs") {
						TextContent = () => source
					}
				}
			};
			var library2 = new XamarinAndroidLibraryProject () {
				ProjectName = "Library2",
				Sources = {
					new BuildItem.Source ("EmptyClass.cs") {
						TextContent = () => source
					}
				}
			};
			var app = new XamarinAndroidApplicationProject {
				ProjectName = "App1",
				References = {
					new BuildItem ("ProjectReference", "..\\Library1\\Library1.csproj"),
					new BuildItem ("ProjectReference", "..\\Library2\\Library2.csproj")
				},
			};
			var projectPath = Path.Combine ("temp", TestName);
			using (var lib1b = CreateDllBuilder (Path.Combine (projectPath, library1.ProjectName), cleanupAfterSuccessfulBuild: false))
			using (var lib2b = CreateDllBuilder (Path.Combine (projectPath, library2.ProjectName), cleanupAfterSuccessfulBuild: false)) {
				Assert.IsTrue (lib1b.Build (library1), "Build of Library1 should have succeeded");
				Assert.IsTrue (lib2b.Build (library2), "Build of Library2 should have succeeded");
				using (var appb = CreateApkBuilder (Path.Combine (projectPath, app.ProjectName))) {
					appb.ThrowOnBuildFailure = false;
					Assert.IsFalse (appb.Build (app), "Build of App1 should have failed");
					IEnumerable<string> errors = appb.LastBuildOutput.Where (x => x.Contains ("error XA4215"));
					Assert.NotNull (errors, "Error should be XA4215");
					StringAssertEx.Contains ("EmptyClass", errors, "Error should mention the conflicting type name");
					StringAssertEx.Contains ("Library1", errors, "Error should mention all of the assemblies with conflicts");
					StringAssertEx.Contains ("Library2", errors, "Error should mention all of the assemblies with conflicts");
				}
			}
		}

		[Test]
		public void DuplicateManagedNames ()
		{
			var source = @"public class EmptyClass : Java.Lang.Object { }";
			var library1 = new XamarinAndroidLibraryProject () {
				ProjectName = "Library1",
				Sources = {
					new BuildItem.Source ("EmptyClass.cs") {
						TextContent = () => source
					}
				}
			};
			var library2 = new XamarinAndroidLibraryProject () {
				ProjectName = "Library2",
				Sources = {
					new BuildItem.Source ("EmptyClass.cs") {
						TextContent = () => source
					}
				}
			};
			var app = new XamarinAndroidApplicationProject {
				ProjectName = "App1",
				References = {
					new BuildItem ("ProjectReference", "..\\Library1\\Library1.csproj"),
					new BuildItem ("ProjectReference", "..\\Library2\\Library2.csproj")
				},
			};
			var projectPath = Path.Combine ("temp", TestName);
			using (var lib1b = CreateDllBuilder (Path.Combine (projectPath, library1.ProjectName), cleanupAfterSuccessfulBuild: false))
			using (var lib2b = CreateDllBuilder (Path.Combine (projectPath, library2.ProjectName), cleanupAfterSuccessfulBuild: false)) {
				Assert.IsTrue (lib1b.Build (library1), "Build of Library1 should have succeeded");
				Assert.IsTrue (lib2b.Build (library2), "Build of Library2 should have succeeded");
				using (var appb = CreateApkBuilder (Path.Combine (projectPath, app.ProjectName))) {
					appb.ThrowOnBuildFailure = false;
					Assert.IsTrue (appb.Build (app), "Build of App1 should have succeeded");
					IEnumerable<string> warnings = appb.LastBuildOutput.Where (x => x.Contains ("warning XA4214"));
					Assert.NotNull (warnings, "Warning should be XA4214");
					StringAssertEx.Contains ("EmptyClass", warnings, "Warning should mention the conflicting type name");
					StringAssertEx.Contains ("Library1", warnings, "Warning should mention all of the assemblies with conflicts");
					StringAssertEx.Contains ("Library2", warnings, "Warning should mention all of the assemblies with conflicts");
				}
			}
		}

		[Test]
		public void LibraryProjectsShouldSkipGetPrimaryCpuAbi ()
		{
			AssertCommercialBuild ();

			const string target = "_GetPrimaryCpuAbi";
			var proj = new XamarinAndroidLibraryProject ();
			using (var b = CreateDllBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				Assert.IsTrue (b.Output.IsTargetSkipped (target, defaultIfNotUsed: true), $"`{target}` should be skipped!");
			}
		}

		[Test]
		public void AllResourcesInClassLibrary ([Values (false, true)] bool useDesignerAssembly)
		{
			var path = Path.Combine ("temp", TestName);

			// Create a "library" with all the application stuff in it
			var lib = new XamarinAndroidApplicationProject {
				ProjectName = "MyLibrary",
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar { }"
					},
				}
			};
			lib.SetProperty ("AndroidApplication", "False");
			lib.SetProperty ("AndroidUseDesignerAssembly", useDesignerAssembly.ToString ());
			lib.RemoveProperty ("OutputType");
			lib.AndroidManifest = lib.AndroidManifest.
				Replace ("application android:label=\"${PROJECT_NAME}\"", "application android:label=\"com.test.foo\" ");

			// Create an "app" that is basically empty and references the library
			var app = new XamarinAndroidLibraryProject {
				ProjectName = "MyApp",
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () => "public class Foo : Bar { }"
					},
				},
				OtherBuildItems = {
					new BuildItem ("None", "Properties\\AndroidManifest.xml") {
						TextContent = () => lib.AndroidManifest,
					},
				}
			};
			app.SetProperty ("AndroidUseDesignerAssembly", useDesignerAssembly.ToString ());
			app.AndroidResources.Clear (); // No Resources
			app.SetProperty (KnownProperties.OutputType, "Exe");
			app.References.Add (new BuildItem.ProjectReference ($"..\\{lib.ProjectName}\\{lib.ProjectName}.csproj", lib.ProjectName, lib.ProjectGuid));

			using (var libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName)))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
				Assert.IsTrue (libBuilder.Build (lib), "library build should have succeeded.");
				Assert.IsTrue (appBuilder.Build (app), "app build should have succeeded.");

				var r_txt = Path.Combine (Root, appBuilder.ProjectDirectory, app.IntermediateOutputPath, "R.txt");
				FileAssert.Exists (r_txt);

				var resource_designer_cs = GetResourceDesignerPath (appBuilder, app);
				FileAssert.Exists (resource_designer_cs);
				var contents = GetResourceDesignerText (app, resource_designer_cs);
				Assert.AreNotEqual ("", contents);
			}
		}

		[Test]
		[NonParallelizable] // fails on NuGet restore
		/// <summary>
		/// Reference https://bugzilla.xamarin.com/show_bug.cgi?id=29568
		/// </summary>
		public void BuildLibraryWhichUsesResources ([Values (false, true)] bool isRelease)
		{
			var proj = new XamarinAndroidLibraryProject { IsRelease = isRelease };
			proj.PackageReferences.Add (KnownPackages.AndroidXAppCompat);
			proj.AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\values\\Styles.xml") {
				TextContent = () => @"<?xml version=""1.0"" encoding=""UTF-8"" ?>
<resources>
	<style name=""AppTheme"" parent=""Theme.AppCompat.Light.NoActionBar"" />
</resources>"
			});
			proj.SetProperty ("AndroidResgenClass", "Resource");
			proj.SetProperty ("AndroidResgenFile", () => "Resources\\Resource.designer" + proj.Language.DefaultExtension);
			using (var b = CreateDllBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void AndroidXClassLibraryNoResources ()
		{
			var proj = new XamarinAndroidLibraryProject ();
			proj.AndroidResources.Clear ();
			proj.PackageReferences.Add (KnownPackages.AndroidXLegacySupportV4);
			using (var b = CreateDllBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void CheckContentBuildAction ()
		{
			var metadata = "CopyToOutputDirectory=PreserveNewest";
			var path = Path.Combine ("temp", TestName);

			var lib = new XamarinAndroidLibraryProject {
				ProjectName = "Library1",
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar { }"
					},
				},
				OtherBuildItems = {
					new BuildItem.Content ("TestContent.txt") {
						TextContent = () => "Test Content from Library",
						MetadataValues = metadata,
					},
					new BuildItem.Content ("TestContent2.txt") {
						TextContent = () => "Content excluded from check",
						MetadataValues = "ExcludeFromContentCheck=true",
					}
				}
			};

			var proj = new XamarinAndroidApplicationProject {
				ProjectName = "App",
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () => "public class Foo : Bar { }"
					},
				},
				References = {
					new BuildItem ("ProjectReference", "..\\Library1\\Library1.csproj"),
				}
			};
			using (var libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName)))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, proj.ProjectName))) {
				Assert.IsTrue (libBuilder.Build (lib), "library should have built successfully");
				StringAssertEx.Contains ("TestContent.txt : warning XA0101: @(Content) build action is not supported", libBuilder.LastBuildOutput,
					"Build Output did not contain 'TestContent.txt : warning XA0101'.");

				proj.AndroidResources.Add (new BuildItem.Content ("TestContent.txt") {
					TextContent = () => "Test Content",
					MetadataValues = metadata,
				});
				proj.AndroidResources.Add (new BuildItem.Content ("TestContent1.txt") {
					TextContent = () => "Test Content 1",
					MetadataValues = metadata,
				});
				Assert.IsTrue (appBuilder.Build (proj), "app should have built successfully");
				StringAssertEx.Contains ("TestContent.txt : warning XA0101: @(Content) build action is not supported", appBuilder.LastBuildOutput,
					"Build Output did not contain 'TestContent.txt : warning XA0101'.");
				StringAssertEx.Contains ("TestContent1.txt : warning XA0101: @(Content) build action is not supported", appBuilder.LastBuildOutput,
					"Build Output did not contain 'TestContent1.txt : warning XA0101'.");
				// Ensure items excluded from check do not produce warnings.
				StringAssertEx.DoesNotContain ("TestContent2.txt : warning XA0101", libBuilder.LastBuildOutput,
					"Build Output contains 'TestContent2.txt : warning XA0101'.");
			}
		}

		// Combination of class libraries that triggered the problem:
		// error APT2144: invalid file path 'obj/Release/net8.0-android/lp/86.stamp'.
		[Test]
		public void ClassLibraryAarDependencies ()
		{
			var path = Path.Combine ("temp", TestName);
			var material = new Package { Id = "Xamarin.Google.Android.Material", Version = "1.9.0.1" };
			var libraryA = new XamarinAndroidLibraryProject {
				ProjectName = "LibraryA",
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar { }",
					},
				},
				PackageReferences = { material },
			};
			using var builderA = CreateDllBuilder (Path.Combine (path, libraryA.ProjectName));
			Assert.IsTrue (builderA.Build (libraryA), "Build should have succeeded.");

			var libraryB = new XamarinAndroidLibraryProject {
				ProjectName = "LibraryB",
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () => "public class Foo : Bar { }",
					}
				},
				PackageReferences = { material },
			};
			libraryB.AddReference (libraryA);
			using var builderB = CreateDllBuilder (Path.Combine (path, libraryB.ProjectName));
			Assert.IsTrue (builderB.Build (libraryB), "Build should have succeeded.");
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

	}
}
