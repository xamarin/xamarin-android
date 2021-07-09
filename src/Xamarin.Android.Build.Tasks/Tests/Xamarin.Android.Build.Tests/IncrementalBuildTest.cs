using Microsoft.Build.Framework;
using Mono.Cecil;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xamarin.Android.Tasks;
using Xamarin.ProjectTools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Build.Tests
{
	[Category ("Node-3")]
	[Parallelizable (ParallelScope.Children)]
	public class IncrementalBuildTest : BaseTest
	{
		[Test]
		public void BasicApplicationRepetitiveBuild ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			using (var b = CreateApkBuilder ()) {
				b.ThrowOnBuildFailure = false;
				Assert.IsTrue (b.Build (proj), "first build failed");
				var firstBuildTime = b.LastBuildTime;
				Assert.IsTrue (b.Build (proj), "second build failed");
				Assert.IsTrue (
					firstBuildTime > b.LastBuildTime, "Second build ({0}) should have been faster than the first ({1})",
					b.LastBuildTime, firstBuildTime
				);
				Assert.IsTrue (
					b.Output.IsTargetSkipped ("_Sign"),
					"the _Sign target should not run");
				var item = proj.AndroidResources.First (x => x.Include () == "Resources\\values\\Strings.xml");
				item.TextContent = () => proj.StringsXml.Replace ("${PROJECT_NAME}", "Foo");
				item.Timestamp = null;
				Assert.IsTrue (b.Build (proj), "third build failed");
				Assert.IsFalse (
					b.Output.IsTargetSkipped ("_Sign"),
					"the _Sign target should run");
			}
		}

		[Test]
		[Category ("SmokeTests")]
		public void BasicApplicationRepetitiveReleaseBuild ()
		{
			var proj = new XamarinAndroidApplicationProject () { IsRelease = true };
			using (var b = CreateApkBuilder ()) {
				var foo = new BuildItem.Source ("Foo.cs") {
					TextContent = () => @"using System;
	namespace UnnamedProject {
		public class Foo {
		}
	}"
				};
				proj.Sources.Add (foo);
				Assert.IsTrue (b.Build (proj), "first build failed");
				var firstBuildTime = b.LastBuildTime;
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "second build failed");
				Assert.IsTrue (
					firstBuildTime > b.LastBuildTime, "Second build ({0}) should have been faster than the first ({1})",
					b.LastBuildTime, firstBuildTime
				);
				b.Output.AssertTargetIsSkipped ("_Sign");
				b.Output.AssertTargetIsSkipped (KnownTargets.LinkAssembliesShrink);
				proj.Touch ("Foo.cs");
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "third build failed");
				b.Output.AssertTargetIsNotSkipped ("CoreCompile");
				b.Output.AssertTargetIsNotSkipped ("_Sign");
			}
		}

		[Test]
		public void CheckNothingIsDeletedByIncrementalClean ([Values (true, false)] bool enableMultiDex, [Values (true, false)] bool useAapt2)
		{
			AssertAaptSupported (useAapt2);
			var path = Path.Combine ("temp", TestName);
			var proj = new XamarinFormsAndroidApplicationProject () {
				ProjectName = "App1",
				IsRelease = true,
				AndroidUseAapt2 = useAapt2,
			};
			if (enableMultiDex)
				proj.SetProperty ("AndroidEnableMultiDex", "True");
			using (var b = CreateApkBuilder (path)) {
				//To be sure we are at a clean state
				var projectDir = Path.Combine (Root, b.ProjectDirectory);
				if (Directory.Exists (projectDir))
					Directory.Delete (projectDir, true);

				Assert.IsTrue (b.Build (proj), "First should have succeeded" );
				var intermediate = Path.Combine (projectDir, proj.IntermediateOutputPath);
				var output = Path.Combine (projectDir, proj.OutputPath);
				var fileWrites = Path.Combine (intermediate, $"{proj.ProjectName}.csproj.FileListAbsolute.txt");
				FileAssert.Exists (fileWrites);
				var expected = File.ReadAllText (fileWrites);
				var files = Directory.EnumerateFiles (intermediate, "*", SearchOption.AllDirectories).ToList ();
				files.AddRange (Directory.EnumerateFiles (output, "*", SearchOption.AllDirectories));

				//Touch a few files, do an incremental build
				var filesToTouch = new [] {
 					Path.Combine (intermediate, "build.props"),
 					Path.Combine (intermediate, $"{proj.ProjectName}.pdb"),
 				};
				foreach (var file in filesToTouch) {
					FileAssert.Exists (file);
					File.SetLastWriteTimeUtc (file, DateTime.UtcNow);
				}
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "Second should have succeeded");

				//No changes
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "Third should have succeeded");
				Assert.IsFalse (b.Output.IsTargetSkipped ("IncrementalClean"), "`IncrementalClean` should have run!");
				foreach (var file in files) {
					FileAssert.Exists (file, $"{file} should not have been deleted!" );
				}
				FileAssert.Exists (fileWrites);
				var actual = File.ReadAllText (fileWrites);
				Assert.AreEqual (expected, actual, $"`{fileWrites}` has changes!");
			}
		}

		[Test]
		public void CheckResourceDirectoryDoesNotGetHosed ()
		{
			// do a release build
			// change one of the properties (say AotAssemblies)
			// do another build. it should NOT hose the resource directory.
			var path = Path.Combine ("temp", TestName);
			var proj = new XamarinAndroidApplicationProject () {
				ProjectName = "App1",
				IsRelease = true,
			};
			using (var b = CreateApkBuilder (path, false, false)) {
				Assert.IsTrue(b.Build (proj), "First should have succeeded");
				Assert.IsFalse (
					b.Output.IsTargetSkipped ("_GenerateAndroidResourceDir"),
					"the _GenerateAndroidResourceDir target should not be skipped");
				File.SetLastWriteTimeUtc (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "build.props"),
					DateTime.UtcNow);
				Assert.IsTrue(b.Build (proj), "Second should have succeeded");
				Assert.IsFalse (
					b.Output.IsTargetSkipped ("_GenerateAndroidResourceDir"),
					"the _GenerateAndroidResourceDir target should not be skipped");
			}
		}

		[Test]
		public void IncrementalCleanDuringClean ()
		{
			var path = Path.Combine ("temp", TestName);
			var proj = new XamarinAndroidApplicationProject () {
				ProjectName = "App1",
				IsRelease = true,
			};
			proj.SetProperty ("AndroidUseManagedDesignTimeResourceGenerator", "True");
			using (var b = CreateApkBuilder (path)) {
				b.Target = "Compile";
				Assert.IsTrue(b.Build (proj), "DesignTime Build should have succeeded");
				var designTimeDesigner = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "designtime", "Resource.designer.cs");
				FileAssert.Exists (designTimeDesigner, $"{designTimeDesigner} should have been created.");
				b.Target = "Build";
				Assert.IsTrue(b.Build (proj), "Build should have succeeded");
				FileAssert.Exists (designTimeDesigner, $"{designTimeDesigner} should still exist after Build.");
				b.Target = "Clean";
				Assert.IsTrue(b.Build (proj), "Clean should have succeeded");
				FileAssert.Exists (designTimeDesigner, $"{designTimeDesigner} should still exist after Clean.");
				b.Target = "Compile";
				Assert.IsTrue(b.Build (proj), "Build should have succeeded");
				FileAssert.Exists (designTimeDesigner, $"{designTimeDesigner} should still exist after Compile.");
				b.Target = "Build";
				Assert.IsTrue(b.Build (proj), "Build should have succeeded");
				FileAssert.Exists (designTimeDesigner, $"{designTimeDesigner} should still exist after second Build.");
				Assert.IsTrue(b.Build (proj), "Build should have succeeded");
				FileAssert.Exists (designTimeDesigner, $"{designTimeDesigner} should still exist after third Build.");
				b.Target = "Compile";
				Assert.IsTrue(b.Build (proj), "Build should have succeeded");
				FileAssert.Exists (designTimeDesigner, $"{designTimeDesigner} should still exist after second Compile.");
				b.Target = "Clean";
				Assert.IsTrue(b.Build (proj), "Clean should have succeeded");
				FileAssert.Exists (designTimeDesigner, $"{designTimeDesigner} should still exist after second Clean.");
				b.Target = "ReBuild";
				Assert.IsTrue(b.Build (proj), "ReBuild should have succeeded");
				FileAssert.Exists (designTimeDesigner, $"{designTimeDesigner} should still exist after ReBuild.");
			}

		}

		[Test]
		public void LibraryIncrementalBuild ()
		{
			var lib = new XamarinAndroidLibraryProject {
				ProjectName = "Lib",
				Sources = {
					new BuildItem.Source ("Class1.cs") {
						TextContent = () => "public class Class1 { }",
					}
				},
			};
			using (var b = CreateDllBuilder ()) {
				Assert.IsTrue (b.Build (lib), "first build should have succeeded.");
				var aarPath = Path.Combine (Root, b.ProjectDirectory, lib.OutputPath, $"{lib.ProjectName}.aar");
				if (Builder.UseDotNet) {
					FileAssert.Exists (aarPath);
				} else {
					Assert.IsTrue (b.LastBuildOutput.ContainsText ("LogicalName=__AndroidLibraryProjects__.zip") ||
						b.LastBuildOutput.ContainsText ("Lib.obj.Debug.__AndroidLibraryProjects__.zip,__AndroidLibraryProjects__.zip"),
						"The LogicalName for __AndroidLibraryProjects__.zip should be set.");
				}
				lib.Touch ("Class1.cs");
				Assert.IsTrue (b.Build (lib), "second build should have succeeded.");
				if (Builder.UseDotNet) {
					FileAssert.Exists (aarPath);
				} else {
					Assert.IsTrue (b.LastBuildOutput.ContainsText ("LogicalName=__AndroidLibraryProjects__.zip") ||
						b.LastBuildOutput.ContainsText ("Lib.obj.Debug.__AndroidLibraryProjects__.zip,__AndroidLibraryProjects__.zip"),
						"The LogicalName for __AndroidLibraryProjects__.zip should be set.");
				}
			}
		}

		[Test]
		public void AllProjectsHaveSameOutputDirectory()
		{
			var testPath = Path.Combine ("temp", "AllProjectsHaveSameOutputDirectory");
			var sb = new SolutionBuilder("AllProjectsHaveSameOutputDirectory.sln") {
				SolutionPath = Path.Combine (Root, testPath),
			};

			var app1 = new XamarinAndroidApplicationProject () {
				ProjectName = "App1",
				PackageName = "com.companyname.App1",
				OutputPath = Path.Combine("..","bin","Debug"),
			};
			sb.Projects.Add (app1);
			var app2 = new XamarinAndroidApplicationProject () {
				ProjectName = "App2",
				PackageName = "com.companyname.App2",
				OutputPath = Path.Combine("..","bin","Debug"),
			};
			sb.Projects.Add (app2);
			Assert.IsTrue (sb.Build (), "Build of solution should have succeeded");
			Assert.IsTrue (sb.ReBuild (), "ReBuild of solution should have succeeded");
			sb.Dispose ();
		}

		[Test]
		public void JavacTaskDoesNotRunOnSecondBuild ()
		{
			var app = new XamarinAndroidApplicationProject () {
				IsRelease = true,
				ProjectName = "App",
				OtherBuildItems = {
					new AndroidItem.AndroidJavaSource ("TestMe.java") {
						TextContent = () => @"package com.android.test;
public class TestMe {
	public TestMe createTestMe () {
		return new TestMe ();
	}
}",
						Encoding = Encoding.ASCII,
						MetadataValues = "Bind=False",
					}
				},
			};

			using (var b = CreateApkBuilder (Path.Combine ("temp", "JavacTaskDoesNotRunOnSecondBuild"), false, false)) {
				Assert.IsTrue (b.Build (app), "First build should have succeeded");
				Assert.IsFalse (
					b.Output.IsTargetSkipped ("_CompileJava"),
					"the _CompileJava target should not be skipped");
				Assert.IsFalse (
					b.Output.IsTargetSkipped ("_BuildApkEmbed"),
					"the _BuildApkEmbed target should not be skipped");
				var expectedOutput = Path.Combine (Root, b.ProjectDirectory, app.IntermediateOutputPath, "android", "bin", "classes",
					"com", "android", "test", "TestMe.class");
				Assert.IsTrue (File.Exists (expectedOutput), string.Format ("{0} should exist.", expectedOutput));
				Assert.IsTrue (b.Build (app), "Second build should have succeeded");
				Assert.IsTrue (
					b.Output.IsTargetSkipped ("_CompileJava"),
					"the _CompileJava target should be skipped");
				Assert.IsTrue (
					b.Output.IsTargetSkipped ("_BuildApkEmbed"),
					"the _BuildApkEmbed target should be skipped");
				Assert.IsTrue (File.Exists (expectedOutput), string.Format ("{0} should exist.", expectedOutput));
			}
		}

		[Test]
		public void ResolveNativeLibrariesInManagedReferences ()
		{
			var lib = new XamarinAndroidLibraryProject () {
				ProjectName = "Lib",
				IsRelease = true,
				ProjectGuid = Guid.NewGuid ().ToString (),
				OtherBuildItems = {
					new BuildItem (AndroidBuildActions.EmbeddedNativeLibrary, "libs/armeabi-v7a/libfoo.so") {
						TextContent = () => string.Empty,
						Encoding = Encoding.ASCII,
					}
				},
				Sources = {
					new BuildItem.Source ("Class1.cs") {
						TextContent= () => @"
using System;
namespace Lib
{
	public class Class1
	{
		public Class1 ()
		{
		}
	}
}"
					},
				},
			};
			var so = lib.OtherBuildItems.First (x => x.Include () == "libs/armeabi-v7a/libfoo.so");

			var lib2 = new XamarinAndroidLibraryProject () {
				ProjectName = "Lib2",
				ProjectGuid = Guid.NewGuid ().ToString (),
				IsRelease = true,
				OtherBuildItems = {
					new BuildItem (AndroidBuildActions.EmbeddedNativeLibrary, "libs/armeabi-v7a/libfoo2.so") {
						TextContent = () => string.Empty,
						Encoding = Encoding.ASCII,
					},
					new BuildItem.ProjectReference (@"..\Lib\Lib.csproj", "Lib", lib.ProjectGuid) {
					}
				},
				Sources = {
					new BuildItem.Source ("Class2.cs") {
						TextContent= () => @"
using System;
namespace Lib2
{
	public class Class2
	{
		public Class2 ()
		{
			var c = new Lib.Class1 ();
		}
	}
}"
					},
				},
			};
			var path = Path.Combine (Root, "temp", TestName);
			using (var libbuilder = CreateDllBuilder (Path.Combine(path, "Lib"))) {

				Assert.IsTrue (libbuilder.Build (lib), "lib 1st. build failed");

				using (var libbuilder2 = CreateDllBuilder (Path.Combine (path, "Lib2"))) {

					Assert.IsTrue (libbuilder2.Build (lib2), "lib 1st. build failed");

					var app = new XamarinAndroidApplicationProject () { ProjectName = "App",
						IsRelease = true,
						OtherBuildItems = {
							new BuildItem.ProjectReference (@"..\Lib2\Lib2.csproj", "Lib2", lib2.ProjectGuid),
						}
					};
					app.SetAndroidSupportedAbis ("armeabi-v7a");
					using (var builder = CreateApkBuilder (Path.Combine (path, "App"))) {
						Assert.IsTrue (builder.Build (app), "app 1st. build failed");

						var libfoo = ZipHelper.ReadFileFromZip (Path.Combine (Root, builder.ProjectDirectory, app.OutputPath, app.PackageName + "-Signed.apk"),
							"lib/armeabi-v7a/libfoo.so");
						Assert.IsNotNull (libfoo, "libfoo.so should exist in the .apk");

						so.TextContent = () => "newValue";
						so.Timestamp = DateTimeOffset.UtcNow;
						Assert.IsTrue (libbuilder.Build (lib), "lib 2nd. build failed");
						Assert.IsTrue (libbuilder2.Build (lib2), "lib 2nd. build failed");
						Assert.IsTrue (builder.Build (app), "app 2nd. build failed");

						Assert.IsNotNull (libfoo, "libfoo.so should exist in the .apk");

						if (!Builder.UseDotNet) {
							Assert.AreEqual (so.TextContent ().Length, new FileInfo (Path.Combine (Root, libbuilder.ProjectDirectory, lib.IntermediateOutputPath,
								"nl", "armeabi-v7a", "libfoo.so")).Length,
								"intermediate size mismatch");
						}
						libfoo = ZipHelper.ReadFileFromZip (Path.Combine (Root, builder.ProjectDirectory, app.OutputPath, app.PackageName + "-Signed.apk"),
							"lib/armeabi-v7a/libfoo.so");
						Assert.AreEqual (so.TextContent ().Length, libfoo.Length, "compressed size mismatch");
						var libfoo2 = ZipHelper.ReadFileFromZip (Path.Combine (Root, builder.ProjectDirectory, app.OutputPath, app.PackageName + "-Signed.apk"),
							"lib/armeabi-v7a/libfoo2.so");
						Assert.IsNotNull (libfoo2, "libfoo2.so should exist in the .apk");
						Directory.Delete (path, recursive: true);
					}
				}
			}
		}

		//https://github.com/xamarin/xamarin-android/issues/2247
		[Test]
		[Category ("SmokeTests")]
		[NonParallelizable] // Do not run timing sensitive tests in parallel
		public void AppProjectTargetsDoNotBreak ()
		{
			var targets = new List<string> {
				"_GeneratePackageManagerJava",
				"_ResolveLibraryProjectImports",
				"_CleanIntermediateIfNeeded",
			};
			if (!Builder.UseDotNet) {
				targets.Add ("_CopyConfigFiles");
			}
			var proj = new XamarinFormsAndroidApplicationProject {
				OtherBuildItems = {
					new BuildItem.NoActionResource ("UnnamedProject.dll.config") {
						TextContent = () => "<?xml version='1.0' ?><configuration/>",
						Metadata = {
							{ "CopyToOutputDirectory", "PreserveNewest"},
						}
					}
				}
			};
			if (IsWindows && !Builder.UseDotNet) {
				//NOTE: pdb2mdb will run on Windows on the current project's symbols if DebugType=Full
				proj.SetProperty (proj.DebugProperties, "DebugType", "Full");
				targets.Add ("_ConvertPdbFiles");
			}
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "first build should succeed");
				var firstBuildTime = b.LastBuildTime;
				foreach (var target in targets) {
					Assert.IsFalse (b.Output.IsTargetSkipped (target), $"`{target}` should *not* be skipped!");
				}

				var output = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath);
				var intermediate = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				var filesToTouch = new [] {
					Path.Combine (intermediate, "..", "project.assets.json"),
					Path.Combine (intermediate, "build.props"),
					Path.Combine (intermediate, $"{proj.ProjectName}.dll"),
					Path.Combine (intermediate, $"{proj.ProjectName}.pdb"),
					Path.Combine (intermediate, "android", "assets", $"{proj.ProjectName}.dll"),
					Path.Combine (output, $"{proj.ProjectName}.dll.config"),
				};
				foreach (var file in filesToTouch) {
					FileAssert.Exists (file);
					File.SetLastWriteTimeUtc (file, DateTime.UtcNow);
				}

				//NOTE: second build, targets will run because inputs changed
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "second build should succeed");
				var secondBuildTime = b.LastBuildTime;
				foreach (var target in targets) {
					b.Output.AssertTargetIsNotSkipped (target);
				}

				//NOTE: third build, targets should certainly *not* run! there are no changes
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "third build should succeed");
				var thirdBuildTime = b.LastBuildTime;
				foreach (var target in targets) {
					b.Output.AssertTargetIsSkipped (target);
				}
				Assert.IsTrue (thirdBuildTime < firstBuildTime, $"Third unchanged build: '{thirdBuildTime}' should be faster than clean build: '{firstBuildTime}'.");
				Assert.IsTrue (thirdBuildTime < secondBuildTime, $"Third unchanged build: '{thirdBuildTime}' should be faster than partially incremental second build: '{secondBuildTime}'.");
			}
		}

		[Test]
		public void LibraryProjectTargetsDoNotBreak ()
		{
			var targets = Builder.UseDotNet ?
				new [] {
					"_CreateAar"
				} :
				new [] {
					"_CreateNativeLibraryArchive",
					"_CreateManagedLibraryResourceArchive",
				};
			var proj = new XamarinAndroidLibraryProject {
				Sources = {
					new BuildItem.Source ("Class1.cs") {
						TextContent= () => "public class Class1 { }"
					},
				},
				OtherBuildItems = {
					new AndroidItem.EmbeddedNativeLibrary ("foo\\armeabi-v7a\\libtest.so") {
						BinaryContent = () => new byte [10],
						MetadataValues = "Link=libs\\armeabi-v7a\\libtest.so",
					},
					new AndroidItem.EmbeddedNativeLibrary ("foo\\x86\\libtest.so") {
						BinaryContent = () => new byte [10],
						MetadataValues = "Link=libs\\x86\\libtest.so",
					},
					new AndroidItem.AndroidAsset ("Assets\\foo.txt") {
						TextContent =  () => "bar",
					},
				},
			};
			using (var b = CreateDllBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "first build should succeed");
				foreach (var target in targets) {
					Assert.IsFalse (b.Output.IsTargetSkipped (target), $"`{target}` should *not* be skipped!");
				}

				WaitFor (1000);
				proj.Touch ("foo\\armeabi-v7a\\libtest.so");
				proj.Touch ("Assets\\foo.txt");

				//NOTE: second build, targets will run because inputs changed
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "second build should succeed");
				foreach (var target in targets) {
					Assert.IsFalse (b.Output.IsTargetSkipped (target), $"`{target}` should *not* be skipped on second build!");
				}

				//NOTE: third build, targets should certainly *not* run! there are no changes
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "third build should succeed");
				foreach (var target in targets) {
					Assert.IsTrue (b.Output.IsTargetSkipped (target), $"`{target}` should be skipped on third build!");
				}
			}
		}

		[Test]
		public void ManifestMergerIncremental ()
		{
			var proj = new XamarinAndroidApplicationProject {
				ManifestMerger = "manifestmerger.jar"
			};
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "first build should succeed");
				b.Output.AssertTargetIsNotSkipped ("_ManifestMerger");

				// Change .csproj & build again
				proj.SetProperty ("Foo", "Bar");
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "second build should succeed");
				b.Output.AssertTargetIsNotSkipped ("_ManifestMerger");

				// Build with no changes
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "third build should succeed");
				b.Output.AssertTargetIsSkipped ("_ManifestMerger");
			}
		}

		[Test]
		[Category ("SmokeTests")]
		public void ProduceReferenceAssembly ()
		{
			var path = Path.Combine ("temp", TestName);
			var app = new XamarinAndroidApplicationProject {
				ProjectName = "MyApp",
				//NOTE: so _BuildApkEmbed runs in commercial tests
				EmbedAssembliesIntoApk = true,
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () => "public class Foo : Bar { }"
					},
				}
			};

			int count = 0;
			var lib = new DotNetStandard {
				ProjectName = "MyLibrary",
				Sdk = "Microsoft.NET.Sdk",
				TargetFramework = "netstandard2.0",
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar { public Bar () { System.Console.WriteLine (" + count++ + "); } }"
					},
				}
			};
			lib.SetProperty ("ProduceReferenceAssembly", "True");
			app.References.Add (new BuildItem.ProjectReference ($"..\\{lib.ProjectName}\\{lib.ProjectName}.csproj", lib.ProjectName, lib.ProjectGuid));

			using (var libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName), false))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
				Assert.IsTrue (libBuilder.Build (lib), "first library build should have succeeded.");
				Assert.IsTrue (appBuilder.Build (app), "first app build should have succeeded.");

				lib.Touch ("Bar.cs");

				Assert.IsTrue (libBuilder.Build (lib, doNotCleanupOnUpdate: true, saveProject: false), "second library build should have succeeded.");
				Assert.IsTrue (appBuilder.Build (app, doNotCleanupOnUpdate: true, saveProject: false), "second app build should have succeeded.");

				appBuilder.Output.AssertTargetIsSkipped ("CoreCompile");
				appBuilder.Output.AssertTargetIsSkipped ("_BuildLibraryImportsCache");
				appBuilder.Output.AssertTargetIsSkipped ("_ResolveLibraryProjectImports");
				appBuilder.Output.AssertTargetIsSkipped ("_GenerateJavaStubs");

				appBuilder.Output.AssertTargetIsPartiallyBuilt (KnownTargets.LinkAssembliesNoShrink);

				appBuilder.Output.AssertTargetIsNotSkipped ("_BuildApkEmbed");
				appBuilder.Output.AssertTargetIsNotSkipped ("_CopyPackage");
				appBuilder.Output.AssertTargetIsNotSkipped ("_Sign");
			}
		}

		[Test]
		public void TransitiveDependencyProduceReferenceAssembly ()
		{
			var path = Path.Combine (Root, "temp", TestName);
			var app = new XamarinAndroidApplicationProject {
				ProjectName = "App",
				Sources = {
					new BuildItem.Source ("Class1.cs") {
						TextContent = () => "public class Class1 : Library1.Class1 { }"
					},
				}
			};
			var lib1 = new DotNetStandard {
				ProjectName = "Library1",
				Sdk = "Microsoft.NET.Sdk",
				TargetFramework = "netstandard2.0",
				Sources = {
					new BuildItem.Source ("Class1.cs") {
						TextContent = () => "namespace Library1 { public class Class1 { } }"
					},
					new BuildItem.Source ("Class2.cs") {
						TextContent = () => "namespace Library1 { public class Class2 : Library2.Class1 { } }"
					}
				}
			};
			lib1.SetProperty ("ProduceReferenceAssembly", "True");
			var lib2 = new DotNetStandard {
				ProjectName = "Library2",
				Sdk = "Microsoft.NET.Sdk",
				TargetFramework = "netstandard2.0",
				Sources = {
					new BuildItem.Source ("Class1.cs") {
						TextContent = () => "namespace Library2 { public class Class1 { } }"
					},
				}
			};
			lib2.SetProperty ("ProduceReferenceAssembly", "True");
			lib1.OtherBuildItems.Add (new BuildItem.ProjectReference ($"..\\{lib2.ProjectName}\\{lib2.ProjectName}.csproj", lib2.ProjectName, lib2.ProjectGuid));
			app.References.Add (new BuildItem.ProjectReference ($"..\\{lib1.ProjectName}\\{lib1.ProjectName}.csproj", lib1.ProjectName, lib1.ProjectGuid));

			using (var lib2Builder = CreateDllBuilder (Path.Combine (path, lib2.ProjectName), cleanupAfterSuccessfulBuild: false))
			using (var lib1Builder = CreateDllBuilder (Path.Combine (path, lib1.ProjectName), cleanupAfterSuccessfulBuild: false))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
				Assert.IsTrue (lib2Builder.Build (lib2), "first Library2 build should have succeeded.");
				Assert.IsTrue (lib1Builder.Build (lib1), "first Library1 build should have succeeded.");
				Assert.IsTrue (appBuilder.Build (app), "first app build should have succeeded.");

				lib2.Sources.Add (new BuildItem.Source ("Class2.cs") {
					TextContent = () => "namespace Library2 { public class Class2 { } }"
				});

				Assert.IsTrue (lib2Builder.Build (lib2, doNotCleanupOnUpdate: true), "second Library2 build should have succeeded.");
				Assert.IsTrue (lib1Builder.Build (lib1, doNotCleanupOnUpdate: true, saveProject: false), "second Library1 build should have succeeded.");
				appBuilder.Target = "SignAndroidPackage";
				Assert.IsTrue (appBuilder.Build (app, doNotCleanupOnUpdate: true, saveProject: false), "app SignAndroidPackage build should have succeeded.");

				var lib2Output = Path.Combine (path, lib2.ProjectName, "bin", "Debug", "netstandard2.0", $"{lib2.ProjectName}.dll");
				var lib2InAppOutput = Path.Combine (path, app.ProjectName, app.IntermediateOutputPath, "android", "assets", $"{lib2.ProjectName}.dll");
				FileAssert.AreEqual (lib2Output, lib2InAppOutput, "new Library2 should have been copied to app output directory");
			}
		}

		[Test]
		public void LinkAssembliesNoShrink ()
		{
			var proj = new XamarinFormsAndroidApplicationProject ();
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "build should have succeeded.");

				// Touch an assembly to a timestamp older than build.props
				var formsViewGroup = b.Output.GetIntermediaryPath (Path.Combine ("android", "assets", "FormsViewGroup.dll"));
				File.SetLastWriteTimeUtc (formsViewGroup, new DateTime (1970, 1, 1));
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "build should have succeeded.");
				b.Output.AssertTargetIsNotSkipped (KnownTargets.LinkAssembliesNoShrink);

				// No changes
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "build should have succeeded.");
				b.Output.AssertTargetIsSkipped (KnownTargets.LinkAssembliesNoShrink);
			}
		}

		[Test]
		public void CSProjUserFileChanges ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			var selectedDevice = "foo";
			var csproj_user_file = $"{proj.ProjectName}.csproj.user";
			proj.Sources.Add (new BuildItem.NoActionResource (csproj_user_file) {
				TextContent = () => $"<Project><PropertyGroup><SelectedDevice>{selectedDevice}</SelectedDevice></PropertyGroup></Project>"
			});
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");

				// Simulates a different device/emulator selection in the IDE
				selectedDevice = "bar";
				proj.Touch (csproj_user_file);

				Assert.IsTrue (b.Build (proj), "second build should have succeeded.");
				b.Output.AssertTargetIsSkipped ("_CompileJava");
				b.Output.AssertTargetIsSkipped ("_CompileToDalvik");
				b.Output.AssertTargetIsSkipped ("_Sign");
			}
		}

		[Test]
		[NonParallelizable] // /restore can fail on Mac in parallel
		public void ConvertCustomView ([Values (true, false)] bool useAapt2)
		{
			AssertAaptSupported (useAapt2);
			var path = Path.Combine ("temp", TestName);
			var app = new XamarinAndroidApplicationProject {
				ProjectName = "MyApp",
				//NOTE: so _BuildApkEmbed runs in commercial tests
				EmbedAssembliesIntoApk = true,
				AndroidUseAapt2 = useAapt2,
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () => "public class Foo : Bar { }"
					},
					new BuildItem.Source ("CustomTextView.cs") {
						TextContent = () =>
							@"using Android.Widget;
							using Android.Content;
							using Android.Util;
							namespace MyApp
							{
								public class CustomTextView : TextView
								{
									public CustomTextView(Context context, IAttributeSet attributes) : base(context, attributes)
									{
									}
								}
							}"
					}
				}
			};
			// Use a custom view
			app.LayoutMain = app.LayoutMain.Replace ("</LinearLayout>", "<MyApp.CustomTextView android:id=\"@+id/myText\" android:text=\"à请\" /></LinearLayout>");

			int count = 0;
			var lib = new DotNetStandard {
				ProjectName = "MyLibrary",
				Sdk = "Microsoft.NET.Sdk",
				TargetFramework = "netstandard2.0",
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => "public class Bar { public Bar () { System.Console.WriteLine (" + count++ + "); } }"
					},
				}
			};
			//NOTE: this test is checking when $(ProduceReferenceAssembly) is False
			lib.SetProperty ("ProduceReferenceAssembly", "False");
			app.References.Add (new BuildItem.ProjectReference ($"..\\{lib.ProjectName}\\{lib.ProjectName}.csproj", lib.ProjectName, lib.ProjectGuid));

			using (var libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName), false))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
				libBuilder.BuildLogFile = "build.log";
				Assert.IsTrue (libBuilder.Build (lib), "first library build should have succeeded.");
				appBuilder.BuildLogFile = "build.log";
				Assert.IsTrue (appBuilder.Build (app), "first app build should have succeeded.");

				if (useAapt2) {
					var aapt2TargetsShouldRun = new [] {
						"_FixupCustomViewsForAapt2",
						"_CompileResources"
					};
					foreach (var target in aapt2TargetsShouldRun) {
						Assert.IsFalse (appBuilder.Output.IsTargetSkipped (target), $"{target} should run!");
					}
				}

				lib.Touch ("Bar.cs");

				libBuilder.BuildLogFile = "build2.log";
				Assert.IsTrue (libBuilder.Build (lib, doNotCleanupOnUpdate: true, saveProject: false), "second library build should have succeeded.");
				appBuilder.BuildLogFile = "build2.log";
				Assert.IsTrue (appBuilder.Build (app, doNotCleanupOnUpdate: true, saveProject: false), "second app build should have succeeded.");

				var targetsShouldSkip = new [] {
					"_BuildLibraryImportsCache",
					"_ResolveLibraryProjectImports",
					"_ConvertCustomView",
				};
				foreach (var target in targetsShouldSkip) {
					Assert.IsTrue (appBuilder.Output.IsTargetSkipped (target), $"`{target}` should be skipped!");
				}

				var targetsShouldRun = new [] {
					//MyLibrary.dll changed and $(ProduceReferenceAssembly)=False
					"CoreCompile",
					"_GenerateJavaStubs",
					"_BuildApkEmbed",
					"_CopyPackage",
					"_Sign",
				};
				foreach (var target in targetsShouldRun) {
					Assert.IsFalse (appBuilder.Output.IsTargetSkipped (target), $"`{target}` should *not* be skipped!");
				}

				if (useAapt2) {
					var aapt2TargetsShouldBeSkipped = new [] {
						"_FixupCustomViewsForAapt2",
						"_CompileResources"
					};
					foreach (var target in aapt2TargetsShouldBeSkipped) {
						Assert.IsTrue (appBuilder.Output.IsTargetSkipped (target), $"{target} should be skipped!");
					}
				}
			}
		}

		[Test]
		public void ResolveLibraryProjectImports ([Values (true, false)] bool useAapt2)
		{
			AssertAaptSupported (useAapt2);
			var proj = new XamarinFormsAndroidApplicationProject ();
			proj.AndroidUseAapt2 = useAapt2;
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");
				var intermediate = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				var cacheFile = Path.Combine (intermediate, "libraryprojectimports.cache");
				FileAssert.Exists (cacheFile);
				var expected = ReadCache (cacheFile);
				Assert.AreNotEqual (0, expected.Jars.Length,
					$"{nameof (expected.Jars)} should not be empty");
				Assert.AreNotEqual (0, expected.ResolvedResourceDirectories.Length,
					$"{nameof (expected.ResolvedResourceDirectories)} should not be empty");
				Assert.AreNotEqual (0, expected.ResolvedResourceDirectoryStamps.Length,
					$"{nameof (expected.ResolvedResourceDirectoryStamps)} should not be empty");

				// Delete the stamp file; this triggers <ResolveLibraryProjectImports/> to re-run.
				// However, the task will skip everything, since the hashes of each assembly will be the same.
				var stamp = Path.Combine (intermediate, "stamp", "_ResolveLibraryProjectImports.stamp");
				FileAssert.Exists (stamp);
				File.Delete (stamp);

				Assert.IsTrue (b.Build (proj), "second build should have succeeded.");
				var actual = ReadCache (cacheFile);
				CollectionAssert.AreEqual (actual.Jars.Select (j => j.ItemSpec),
					expected.Jars.Select (j => j.ItemSpec));
				CollectionAssert.AreEqual (actual.ResolvedResourceDirectories.Select (j => j.ItemSpec),
					expected.ResolvedResourceDirectories.Select (j => j.ItemSpec));

				// Add a new AAR file to the project
				var aar = new AndroidItem.AndroidAarLibrary ("Jars\\android-crop-1.0.1.aar") {
					WebContent = "https://repo1.maven.org/maven2/com/soundcloud/android/android-crop/1.0.1/android-crop-1.0.1.aar"
				};
				proj.OtherBuildItems.Add (aar);

				Assert.IsTrue (b.Build (proj), "third build should have succeeded.");
				actual = ReadCache (cacheFile);
				Assert.AreEqual (expected.Jars.Length + 1, actual.Jars.Length,
					$"{nameof (expected.Jars)} should have one more item");
				Assert.AreEqual (expected.ResolvedResourceDirectories.Length + 1, actual.ResolvedResourceDirectories.Length,
					$"{nameof (expected.ResolvedResourceDirectories)} should have one more item");
				Assert.AreEqual (expected.ResolvedResourceDirectoryStamps.Length + 1, actual.ResolvedResourceDirectoryStamps.Length,
					$"{nameof (expected.ResolvedResourceDirectoryStamps)} should have one more item");
				foreach (var s in actual.ResolvedResourceDirectoryStamps) {
					FileAssert.Exists (s.ItemSpec);
				}

				// Build with no changes, checking we are skipping targets appropriately
				Assert.IsTrue (b.Build (proj), "fourth build should have succeeded.");
				var targets = new List<string> {
					"_UpdateAndroidResgen",
					"_CompileJava",
					"_CreateBaseApk",
				};
				if (useAapt2) {
					targets.Add ("_ConvertResourcesCases");
				}
				foreach (var targetName in targets) {
					Assert.IsTrue (b.Output.IsTargetSkipped (targetName), $"`{targetName}` should be skipped!");
				}
			}
		}

		ReadLibraryProjectImportsCache ReadCache (string cacheFile)
		{
			var task = new ReadLibraryProjectImportsCache {
				BuildEngine = new MockBuildEngine (new StringWriter ()),
				CacheFile = cacheFile,
			};
			Assert.IsTrue (task.Execute (), $"{nameof (ReadLibraryProjectImportsCache)} should have succeeded.");
			return task;
		}

		[Test]
		[NonParallelizable]
		public void InvalidAndroidResource ([Values (true, false)] bool useAapt2)
		{
			AssertAaptSupported (useAapt2);
			var invalidXml = new AndroidItem.AndroidResource (@"Resources\values\ids.xml") {
				TextContent = () => "<?xml version=\"1.0\" encoding=\"utf-8\" ?><resources><item/></resources>"
			};

			var proj = new XamarinAndroidApplicationProject ();
			proj.AndroidUseAapt2 = useAapt2;
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				var projectFile = Path.Combine (Root, b.ProjectDirectory, proj.ProjectFilePath);
				b.ThrowOnBuildFailure = false;
				proj.OtherBuildItems.Add (invalidXml);
				Assert.IsFalse (b.Build (proj), "Build should *not* have succeeded.");

				b.ThrowOnBuildFailure = true;
				proj.OtherBuildItems.Remove (invalidXml);

				//HACK: for random test failure
				b.Save (proj, doNotCleanupOnUpdate: true);
				File.SetLastWriteTimeUtc (projectFile, DateTime.UtcNow.AddMinutes (1));

				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				b.ThrowOnBuildFailure = false;
				proj.OtherBuildItems.Add (invalidXml);

				//HACK: for random test failure
				b.Save (proj, doNotCleanupOnUpdate: true);
				File.SetLastWriteTimeUtc (projectFile, DateTime.UtcNow.AddMinutes (1));

				Assert.IsFalse (b.Build (proj), "Build should *not* have succeeded.");
			}
		}

		[Test]
		public void CasingOnJavaLangObject ()
		{
			var className = "Foo";
			var proj = new XamarinAndroidApplicationProject {
				Sources = {
					new BuildItem ("Compile", "Foo.cs") {
						TextContent = () => {
							return $"public class {className} : Java.Lang.Object {{ }}";
						}
					},
				}
			};
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");
				className = "fOO";
				proj.Touch ("Foo.cs");
				Assert.IsTrue (b.Build (proj), "second build should have succeeded.");
			}
		}

		[Test]
		public void GenerateJavaStubsAndAssembly ([Values (true, false)] bool isRelease)
		{
			var targets = new [] {
				"_GenerateJavaStubs",
				"_GeneratePackageManagerJava",
				"_CompileNativeAssemblySources",
				"_CreateApplicationSharedLibraries",
			};
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = isRelease,
			};
			proj.SetAndroidSupportedAbis ("armeabi-v7a");
			proj.OtherBuildItems.Add (new AndroidItem.AndroidEnvironment ("Foo.txt") {
				TextContent = () => "Foo=Bar",
			});
			proj.MainActivity = proj.DefaultMainActivity;
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");
				foreach (var target in targets) {
					Assert.IsFalse (b.Output.IsTargetSkipped (target), $"`{target}` should *not* be skipped!");
				}
				AssertAssemblyFilesInFileWrites (proj, b);

				// Change C# file and AndroidEvironment file
				proj.MainActivity += Environment.NewLine + "// comment";
				proj.Touch ("MainActivity.cs");
				proj.Touch ("Foo.txt");
				Assert.IsTrue (b.Build (proj), "second build should have succeeded.");
				foreach (var target in targets) {
					Assert.IsFalse (b.Output.IsTargetSkipped (target), $"`{target}` should *not* be skipped!");
				}
				AssertAssemblyFilesInFileWrites (proj, b);

				// No changes
				Assert.IsTrue (b.Build (proj), "third build should have succeeded.");
				foreach (var target in targets) {
					Assert.IsTrue (b.Output.IsTargetSkipped (target), $"`{target}` should be skipped!");
				}
				AssertAssemblyFilesInFileWrites (proj, b);
			}
		}

		readonly string [] ExpectedAssemblyFiles = new [] {
			Path.Combine ("android", "environment.armeabi-v7a.o"),
			Path.Combine ("android", "environment.armeabi-v7a.s"),
			Path.Combine ("android", "typemaps.armeabi-v7a-shared.inc"),
			Path.Combine ("android", "typemaps.armeabi-v7a.o"),
			Path.Combine ("android", "typemaps.armeabi-v7a.s"),
			Path.Combine ("app_shared_libraries", "armeabi-v7a", "libxamarin-app.so")
		};

		void AssertAssemblyFilesInFileWrites (XamarinAndroidApplicationProject proj, ProjectBuilder b)
		{
			var intermediate = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
			var lines = File.ReadAllLines (Path.Combine (intermediate, $"{proj.ProjectName}.csproj.FileListAbsolute.txt"));
			foreach (var file in ExpectedAssemblyFiles) {
				var path = Path.Combine (intermediate, file);
				CollectionAssert.Contains (lines, path, $"{file} is not in FileWrites!");
				FileAssert.Exists (path);
			}
		}

#pragma warning disable 414
		static object [] AotChecks () => new object [] {
			new object[] {
				/* supportedAbis */   "arm64-v8a",
				/* androidAotMode */  "", // None, Normal, Hybrib, Full
				/* aotAssemblies */   false,
				/* expectedResult */  true,
			},
			new object[] {
				/* supportedAbis */   "arm64-v8a",
				/* androidAotMode */  "Normal", // None, Normal, Hybrid, Full
				/* aotAssemblies */   true,
				/* expectedResult */  true,
			},
			new object[] {
				/* supportedAbis */   "arm64-v8a",
				/* androidAotMode */  "Normal", // None, Normal, Hybrid, Full
				/* aotAssemblies */   false,
				/* expectedResult */  true,
			},
			new object[] {
				/* supportedAbis */   "arm64-v8a",
				/* androidAotMode */  "Full", // None, Normal, Hybrid, Full
				/* aotAssemblies */   true,
				/* expectedResult */  true,
			},
			new object[] {
				/* supportedAbis */   "arm64-v8a",
				/* androidAotMode */  "Full", // None, Normal, Hybrid, Full
				/* aotAssemblies */   false,
				/* expectedResult */  true,
			},
			new object[] {
				/* supportedAbis */   "arm64-v8a",
				/* androidAotMode */  "Hybrid", // None, Normal, Hybrid, Full
				/* aotAssemblies */   true,
				/* expectedResult */  true,
			},
			new object[] {
				/* supportedAbis */   "arm64-v8a",
				/* androidAotMode */  "Hybrid", // None, Normal, Hybrid, Full
				/* aotAssemblies */   false,
				/* expectedResult */  true,
			},
		};
#pragma warning restore 414

		[Test]
		[Category ("AOT")]
		[TestCaseSource (nameof (AotChecks))]
		public void BuildIncrementalAot (string supportedAbis, string androidAotMode, bool aotAssemblies, bool expectedResult)
		{
			var targets = new string [] {
				"_RemoveRegisterAttribute",
				"_BuildApkEmbed",
			};
			var path = Path.Combine ("temp", $"BuildAotApplication_{supportedAbis}_{androidAotMode}_{aotAssemblies}_{expectedResult}");
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
				BundleAssemblies = false,
				AotAssemblies = aotAssemblies,
			};
			proj.SetAndroidSupportedAbis (supportedAbis);
			if (!string.IsNullOrEmpty (androidAotMode))
			    proj.SetProperty ("AndroidAotMode", androidAotMode);
			using (var b = CreateApkBuilder (path)) {
				if (!b.CrossCompilerAvailable (supportedAbis))
					Assert.Ignore ($"Cross compiler for {supportedAbis} was not available");
				if (!b.GetSupportedRuntimes ().Any (x => supportedAbis == x.Abi))
					Assert.Ignore ($"Runtime for {supportedAbis} was not available.");
				b.BuildLogFile = "first.log";
				b.CleanupAfterSuccessfulBuild = false;
				b.CleanupOnDispose = false;
				b.ThrowOnBuildFailure = false;
				Assert.AreEqual (expectedResult, b.Build (proj, doNotCleanupOnUpdate: true), "Build should have {0}.", expectedResult ? "succeeded" : "failed");
				if (!expectedResult)
					return;
				foreach (var target in targets) {
					Assert.IsFalse (b.Output.IsTargetSkipped (target), $"`{target}` should *not* be skipped on first build!");
				}

				b.BuildLogFile = "second.log";
				b.CleanupAfterSuccessfulBuild = false;
				b.CleanupOnDispose = false;
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "Second build should have succeeded.");
				foreach (var target in targets) {
					Assert.IsTrue (b.Output.IsTargetSkipped (target), $"`{target}` should be skipped on second build!");
				}

				proj.Touch ("MainActivity.cs");

				b.BuildLogFile = "third.log";
				b.CleanupAfterSuccessfulBuild = false;
				b.CleanupOnDispose = false;
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "Third build should have succeeded.");
				foreach (var target in targets) {
					Assert.IsFalse (b.Output.IsTargetSkipped (target), $"`{target}` should *not* be skipped on third build!");
				}
			}
		}

		[Test]
		public void DeterministicBuilds ([Values (true, false)] bool deterministic)
		{
			var proj = new XamarinAndroidApplicationProject {
				Deterministic = deterministic,
				//NOTE: so _BuildApkEmbed runs in commercial tests
				EmbedAssembliesIntoApk = true,
			};
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");
				var output = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.ProjectName}.dll");
				FileAssert.Exists (output);
				string expectedHash = Files.HashFile (output);
				Guid expectedMvid;
				using (var assembly = AssemblyDefinition.ReadAssembly (output)) {
					expectedMvid = assembly.MainModule.Mvid;
				}

				proj.Touch ("MainActivity.cs");
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "second build should have succeeded.");
				FileAssert.Exists (output);
				using (var assembly = AssemblyDefinition.ReadAssembly (output)) {
					if (deterministic) {
						Assert.AreEqual (expectedMvid, assembly.MainModule.Mvid, "Mvid should match");
						Assert.AreEqual (expectedHash, Files.HashFile (output), "hash should match");
					} else {
						Assert.AreNotEqual (expectedMvid, assembly.MainModule.Mvid, "Mvid should *not* match");
						Assert.AreNotEqual (expectedHash, Files.HashFile (output), "hash should *not* match");
					}
				}
			}
		}

		[Test]
		public void DesignTimeBuild ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			using (var b = CreateApkBuilder (Path.Combine ("temp", $"{nameof (IncrementalBuildTest)}{TestName}"))) {
				Assert.IsTrue (b.DesignTimeBuild (proj), "first dtb should have succeeded.");
				// DesignTimeBuild=true lowercased
				var parameters = new [] { "DesignTimeBuild=true" };
				Assert.IsTrue (b.RunTarget (proj, "Compile", doNotCleanupOnUpdate: true, parameters: parameters), "second dtb should have succeeded.");
				var target = "_ResolveLibraryProjectImports";
				Assert.IsTrue (b.Output.IsTargetSkipped (target), $"`{target}` should have been skipped.");
				Assert.IsTrue (b.RunTarget (proj, "UpdateGeneratedFiles", doNotCleanupOnUpdate: true, parameters: parameters), "UpdateGeneratedFiles should have succeeded.");
				Assert.IsTrue (b.Output.IsTargetSkipped (target), $"`{target}` should have been skipped.");
			}
		}

		[Test]
		public void ChangePackageNamingPolicy ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.Sources.Add (new BuildItem.Source ("Bar.cs") {
				TextContent = () => "namespace Foo { class Bar : Java.Lang.Object { } }"
			});
			proj.SetProperty ("AndroidPackageNamingPolicy", "Lowercase");
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");
				var dexFile = b.Output.GetIntermediaryPath (Path.Combine ("android", "bin", "classes.dex"));
				var className = "Lfoo/Bar;";
				Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");

				proj.SetProperty ("AndroidPackageNamingPolicy", "LowercaseCrc64");
				Assert.IsTrue (b.Build (proj), "second build should have succeeded.");
				Assert.IsFalse (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should *not* include `{className}`!");
				className = "Lcrc64dca3aed1e0ff8a1a/Bar;";
				Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
			}
		}

		[Test]
		public void MissingProjectReference ()
		{
			var path = Path.Combine ("temp", TestName);

			var bar = "public class Bar { }";
			var lib = new XamarinAndroidLibraryProject {
				ProjectName = "MyLibrary",
				Sources = {
					new BuildItem.Source ("Bar.cs") {
						TextContent = () => bar
					},
				}
			};
			var app = new XamarinAndroidApplicationProject {
				ProjectName = "MyApp",
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () => "public class Foo : Bar { }"
					},
				}
			};
			var reference = $"..\\{lib.ProjectName}\\{lib.ProjectName}.csproj";
			app.References.Add (new BuildItem.ProjectReference (reference, lib.ProjectName, lib.ProjectGuid));

			using (var libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName)))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
				libBuilder.ThrowOnBuildFailure =
					appBuilder.ThrowOnBuildFailure = false;

				// Build app before library is built
				Assert.IsFalse (appBuilder.Build (app), "app build should have failed.");
				Assert.IsTrue (StringAssertEx.ContainsText (appBuilder.LastBuildOutput, $"The referenced project '{reference.Replace ('\\', Path.DirectorySeparatorChar)}' does not exist."));
				Assert.IsTrue (StringAssertEx.ContainsText (appBuilder.LastBuildOutput, " 1 Warning(s)"), "Should receive 1 Warning");
				Assert.IsTrue (StringAssertEx.ContainsText (appBuilder.LastBuildOutput, "error CS0246"), "Should receive CS0246");
				Assert.IsTrue (StringAssertEx.ContainsText (appBuilder.LastBuildOutput, " 1 Error(s)"), "Should receive 1 Error");

				// Successful build
				Assert.IsTrue (libBuilder.Build (lib), "lib build should have succeeded.");
				Assert.IsTrue (appBuilder.Build (app), "app build should have succeeded.");

				// Create compiler error in library, the app will still be able to build
				bar += "}";
				lib.Touch ("Bar.cs");
				Assert.IsFalse (libBuilder.Build (lib), "lib build should have failed.");
				Assert.IsTrue (appBuilder.Build (app), "app build should have succeeded.");
			}
		}

		[Test]
		public void AaptError ([Values (true, false)] bool useAapt2)
		{
			AssertAaptSupported (useAapt2);
			var proj = new XamarinAndroidApplicationProject {
				Sources = {
					new BuildItem.Source ("TestActivity.cs") {
						TextContent = () => @"using Android.App; [Activity(Theme = ""@style/DoesNotExist"")] class TestActivity : Activity { }"
					}
				}
			};
			proj.AndroidUseAapt2 = useAapt2;
			using (var builder = CreateApkBuilder ()) {
				builder.ThrowOnBuildFailure = false;
				Assert.IsFalse (builder.Build (proj), "Build should *not* have succeeded on the first build.");
				Assert.IsFalse (builder.Build (proj), "Build should *not* have succeeded on the second build.");
			}
		}

		[Test]
		public void AndroidResourceChange ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			using (var builder = CreateApkBuilder ()) {
				Assert.IsTrue (builder.Build (proj), "first build should succeed");

				// AndroidResource change
				proj.LayoutMain += $"{Environment.NewLine}<!--comment-->";
				proj.Touch ("Resources\\layout\\Main.axml");
				Assert.IsTrue (builder.Build (proj), "second build should succeed");

				builder.Output.AssertTargetIsSkipped ("_ResolveLibraryProjectImports");
				builder.Output.AssertTargetIsSkipped ("_GenerateJavaStubs");
				builder.Output.AssertTargetIsSkipped ("_CompileJava");
				builder.Output.AssertTargetIsSkipped ("_CompileToDalvik");
			}
		}

		[Test]
		public void AndroidAssetChange ()
		{
			var text = "Foo";
			var proj = new XamarinAndroidApplicationProject ();
			proj.OtherBuildItems.Add (new AndroidItem.AndroidAsset ("Assets\\Foo.txt") {
				TextContent = () => text
			});
			using (var b = CreateApkBuilder ()) {
				var apk = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.PackageName}.apk");
				Assert.IsTrue (b.Build (proj), "first build should succeed");
				AssertAssetContents (apk);

				// AndroidAsset change
				text = "Bar";
				proj.Touch ("Assets\\Foo.txt");
				Assert.IsTrue (b.Build (proj), "second build should succeed");

				AssertAssetContents (apk);
			}

			void AssertAssetContents (string apk)
			{
				FileAssert.Exists (apk);
				using (var zip = ZipHelper.OpenZip (apk)) {
					var entry = zip.ReadEntry ("assets/Foo.txt");
					Assert.IsNotNull (entry, "Foo.txt should exist in apk!");
					using (var stream = new MemoryStream ())
					using (var reader = new StreamReader (stream)) {
						entry.Extract (stream);
						stream.Position = 0;
						Assert.AreEqual (text, reader.ReadToEnd ());
					}
				}
			}
		}

		[Test]
		public void AndroidAssetMissing ()
		{
			var proj = new XamarinAndroidApplicationProject {
				OtherBuildItems = {
					new AndroidItem.AndroidAsset ("Assets\\foo\\bar.txt") {
						TextContent = () => "bar",
					},
				}
			};
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "first build should succeed");

				var apk = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.PackageName}.apk");
				FileAssert.Exists (apk);
				using (var zip = ZipHelper.OpenZip (apk)) {
					Assert.IsTrue (zip.ContainsEntry ("assets/foo/bar.txt"), "bar.txt should exist in apk!");
				}

				// Touch $(MSBuildProjectFile)
				var projectFile = Path.Combine (Root, b.ProjectDirectory, proj.ProjectFilePath);
				File.SetLastWriteTimeUtc (projectFile, DateTime.UtcNow.AddMinutes (1));

				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "second build should succeed");
				FileAssert.Exists (apk);
				using (var zip = ZipHelper.OpenZip (apk)) {
					Assert.IsTrue (zip.ContainsEntry ("assets/foo/bar.txt"), "bar.txt should exist in apk!");
				}
			}
		}

		[Test]
		[NonParallelizable]
		public void AndroidXMigrationBug ()
		{
			var proj = new XamarinFormsAndroidApplicationProject ();
			proj.PackageReferences.Add (KnownPackages.AndroidXMigration);
			proj.PackageReferences.Add (KnownPackages.AndroidXAppCompat);
			proj.PackageReferences.Add (KnownPackages.AndroidXAppCompatResources);
			proj.PackageReferences.Add (KnownPackages.AndroidXBrowser);
			proj.PackageReferences.Add (KnownPackages.AndroidXMediaRouter);
			proj.PackageReferences.Add (KnownPackages.AndroidXLegacySupportV4);
			proj.PackageReferences.Add (KnownPackages.AndroidXLifecycleLiveData);
			proj.PackageReferences.Add (KnownPackages.XamarinGoogleAndroidMaterial);

			string source = "class Foo { }";
			proj.Sources.Add (new BuildItem.Source ("Foo.cs") { TextContent = () => source });

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");
				source = source.Replace ("Foo", "Bar");
				proj.Touch ("Foo.cs");
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "second build should have succeeded.");
				var targets = new [] {
					"_CompileResources",
					"_UpdateAndroidResgen",
					"_GenerateAndroidResourceDir",
				};
				foreach (var target in targets) {
					Assert.IsTrue (b.Output.IsTargetSkipped (target), $"`{target}` should be skipped.");
				}
			}
		}

		[Test]
		public void ChangeSupportedAbis ()
		{
			var proj = new XamarinFormsAndroidApplicationProject ();
			proj.SetAndroidSupportedAbis ("armeabi-v7a");
			using (var b = CreateApkBuilder ()) {
				b.Build (proj);

				var parameters = Builder.UseDotNet ?
					new [] { $"{KnownProperties.RuntimeIdentifier}=android-x86" } :
					new [] { $"{KnownProperties.AndroidSupportedAbis}=x86" };
				b.Build (proj, parameters: parameters, doNotCleanupOnUpdate: true);
			}
		}

		[Test]
		public void BuildPropsBreaksConvertResourcesCasesOnSecondBuild ([Values (true, false)] bool useAapt2)
		{
			AssertAaptSupported (useAapt2);
			var proj = new XamarinAndroidApplicationProject () {
				AndroidResources = {
					new AndroidItem.AndroidResource (() => "Resources\\drawable\\IMALLCAPS.png") {
						BinaryContent = () => XamarinAndroidApplicationProject.icon_binary_mdpi,
					},
					new AndroidItem.AndroidResource ("Resources\\layout\\test.axml") {
						TextContent = () => {
							return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ImageView xmlns:android=\"http://schemas.android.com/apk/res/android\" android:src=\"@drawable/IMALLCAPS\" />";
						}
					}
				}
			};
			proj.AndroidUseAapt2 = useAapt2;
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");
				var assemblyPath = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, "UnnamedProject.dll");
				var apkPath = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				var firstAssemblyWrite = new FileInfo (assemblyPath).LastWriteTime;
				var firstApkWrite = new FileInfo (apkPath).LastWriteTime;

				// Invalidate build.props with newer timestamp, by updating a @(_PropertyCacheItems) property
				proj.SetProperty ("AndroidLinkMode", "SdkOnly");
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "second build should have succeeded.");
				var secondAssemblyWrite = new FileInfo (assemblyPath).LastWriteTime;
				var secondApkWrite = new FileInfo (apkPath).LastWriteTime;
				Assert.IsTrue (secondAssemblyWrite > firstAssemblyWrite,
					$"Assembly write time was not updated on partially incremental build. Before: {firstAssemblyWrite}. After: {secondAssemblyWrite}.");
				Assert.IsTrue (secondApkWrite > firstApkWrite,
					$"Apk write time was not updated on partially incremental build. Before: {firstApkWrite}. After: {secondApkWrite}.");
			}
		}

	}
}
