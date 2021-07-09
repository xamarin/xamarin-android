using Mono.Cecil;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Xamarin.ProjectTools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Build.Tests
{
	[Category ("Node-3")]
	[Parallelizable (ParallelScope.Children)]
	public class BindingBuildTest : BaseTest {
#pragma warning disable 414
		static object [] ClassParseOptions = new object [] {
			new object[] {
				/* classParser */   "class-parse",
				},
		};

		[Test]
		[TestCaseSource (nameof (ClassParseOptions))]
		[Category ("SmokeTests")]
		public void BuildBasicBindingLibrary (string classParser)
		{
			var targets = new List<string> {
				"_ExportJarToXml",
				"GenerateBindings",
				"_ResolveLibraryProjectImports",
				"CoreCompile",
			};
			if (Builder.UseDotNet) {
				targets.Add ("_CreateAar");
			} else {
				targets.Add ("_CreateBindingResourceArchive");
				//TODO: .NET 5+ cannot support javadoc yet, due to missing mdoc
				targets.Add ("_ExtractJavaDocJars");
				targets.Add ("BuildDocumentation");
			}

			var proj = new XamarinAndroidBindingProject () {
				IsRelease = true,
			};
			proj.Jars.Add (new AndroidItem.AndroidLibrary ("Jars\\svg-android.jar") {
				WebContent = "https://storage.googleapis.com/google-code-archive-downloads/v2/code.google.com/svg-android/svg-android.jar"
			});
			proj.AndroidClassParser = classParser;
			using (var b = CreateDllBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				var assemblyPath = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.ProjectName}.dll");
				using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
					var typeName = "Com.Larvalabs.Svgandroid.SVG";
					var type = assembly.MainModule.GetType (typeName);
					Assert.IsNotNull (type, $"{assemblyPath} should contain {typeName}");
				}

				//TODO: see https://github.com/dotnet/msbuild/issues/6609
				if (!Builder.UseDotNet) {
					//A list of properties we check exist in binding projects
					var properties = new [] {
						"AndroidSdkBuildToolsVersion",
						"AndroidSdkPlatformToolsVersion",
						"AndroidSdkToolsVersion",
						"AndroidNdkVersion",
					};
					foreach (var property in properties) {
						Assert.IsTrue (StringAssertEx.ContainsText (b.LastBuildOutput, property + " = "), $"$({property}) should be set!");
					}
				}

				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "second build should succeed");
				foreach (var target in targets) {
					Assert.IsTrue (b.Output.IsTargetSkipped (target), $"`{target}` should be skipped on second build!");
				}
			}
		}

		[Test]
		[TestCaseSource (nameof (ClassParseOptions))]
		public void CleanBasicBindingLibrary (string classParser)
		{
			var proj = new XamarinAndroidBindingProject () {
				IsRelease = true,
			};
			proj.Jars.Add (new AndroidItem.EmbeddedJar ("Jars\\svg-android.jar") {
				WebContent = "https://storage.googleapis.com/google-code-archive-downloads/v2/code.google.com/svg-android/svg-android.jar"
			});
			proj.AndroidClassParser = classParser;
			using (var b = CreateDllBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				Assert.IsTrue (b.Clean (proj), "Clean should have succeeded");
				var ignoreFiles = new string [] {
					"TemporaryGeneratedFile",
					"FileListAbsolute.txt",
				};
				var files = Directory.GetFiles (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath), "*", SearchOption.AllDirectories)
					.Where (x => !ignoreFiles.Any (i => !Path.GetFileName (x).Contains (i)));
				var directories = Directory.GetDirectories (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath), "*", SearchOption.AllDirectories)
					// designtime folder is left behind, so Intellisense continues to work after a Clean
					.Where (x => Path.GetFileName (x) != "designtime")
					// .NET 5+ sets $(ProduceReferenceAssembly) by default
					// https://github.com/dotnet/sdk/blob/18ee4eac8b3abe6d554d2e0c39d8952da0f23ce5/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.TargetFrameworkInference.targets#L242-L244
					.Where (x => Path.GetFileName (x) != "ref");
				CollectionAssert.IsEmpty (directories, $"{proj.IntermediateOutputPath} should have no directories.");
				CollectionAssert.IsEmpty (files, $"{proj.IntermediateOutputPath} should have no files.");
			}
		}

		[Test]
		[TestCaseSource (nameof (ClassParseOptions))]
		public void BuildAarBindingLibraryStandalone (string classParser)
		{
			var proj = new XamarinAndroidBindingProject () {
				UseLatestPlatformSdk = true,
				IsRelease = true,
			};
			proj.Jars.Add (new AndroidItem.AndroidLibrary ("Jars\\material-menu-1.1.0.aar") {
				WebContent = "https://repo.jfrog.org/artifactory/libs-release-bintray/com/balysv/material-menu/1.1.0/material-menu-1.1.0.aar"
			});
			proj.AndroidClassParser = classParser;
			using (var b = CreateDllBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				if (Builder.UseDotNet) {
					FileAssert.Exists (Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, "material-menu-1.1.0.aar"));
				}
			}
		}

		[Test]
		[TestCaseSource (nameof (ClassParseOptions))]
		public void BuildAarBindigLibraryWithNuGetPackageOfJar (string classParser)
		{
			var proj = new XamarinAndroidBindingProject () {
				UseLatestPlatformSdk = true,
				IsRelease = true,
			};
			proj.PackageReferences.Add (KnownPackages.AndroidSupportV4_27_0_2_1);
			proj.Jars.Add (new AndroidItem.LibraryProjectZip ("Jars\\android-crop-1.0.1.aar") {
				WebContent = "https://repo1.maven.org/maven2/com/soundcloud/android/android-crop/1.0.1/android-crop-1.0.1.aar"
			});
			proj.MetadataXml = @"
				<metadata>
					<attr path=""/api/package[@name='com.soundcloud.android.crop']"" name='managedName'>AndroidCropBinding</attr>
					<attr path=""/api/package[@name='com.soundcloud.android.crop']/class[@name='MonitoredActivity']"" name='visibility'>public</attr>
					<attr path=""/api/package[@name='com.soundcloud.android.crop']/class[@name='ImageViewTouchBase']"" name='visibility'>public</attr>
					<attr path=""/api/package[@name='com.soundcloud.android.crop']/class[@name='RotateBitmap']"" name='visibility'>public</attr>
				</metadata>";
			proj.AndroidClassParser = classParser;
			using (var b = CreateDllBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		[TestCaseSource (nameof (ClassParseOptions))]
		[NonParallelizable]
		[Category ("SmokeTests")]
		public void BuildLibraryZipBindigLibraryWithAarOfJar (string classParser)
		{
			var proj = new XamarinAndroidBindingProject () {
				UseLatestPlatformSdk = true,
				IsRelease = true,
			};
			proj.AndroidClassParser = classParser;
			proj.PackageReferences.Add (KnownPackages.AndroidSupportV4_27_0_2_1);
			proj.Jars.Add (new AndroidItem.LibraryProjectZip ("Jars\\aFileChooserBinaries.zip") {
				WebContentFileNameFromAzure = "aFileChooserBinaries.zip"
			});
			proj.MetadataXml = @"
				<metadata>
					<attr path=""/api/package[@name='com.ipaulpro.afilechooser']/class[@name='FileListAdapter']/method[@name='getItem' and count(parameter)=1 and parameter[1][@type='int']]"" name=""managedReturn"">Java.Lang.Object</attr>
					<attr path=""/api/package[@name='com.ipaulpro.afilechooser']/class[@name='FileLoader']/method[@name='loadInBackground' and count(parameter)=0]"" name=""managedName"">LoadInBackgroundImpl</attr>
				</metadata>";
			proj.Sources.Add (new BuildItem (BuildActions.Compile, "Fixup.cs") {
				TextContent = () => @"using System;
using System.Collections.Generic;
using Android.App;
using Android.Runtime;

namespace Com.Ipaulpro.Afilechooser {
	[Activity (Name = ""com.ipaulpro.afilechooser.FileChooserActivity"",
	           Icon = ""@drawable/ic_chooser"",
	           Exported = true)]
	[IntentFilter (new string [] {""android.intent.action.GET_CONTENT""},
	               Categories = new string [] {
				""android.intent.category.DEFAULT"",
				//""android.intent.category.OPENABLE""
				},
	               DataMimeType = ""*/*"")]
	public partial class FileChooserActivity
	{
	}

	public partial class FileListFragment : global::Android.Support.V4.App.ListFragment, global::Android.Support.V4.App.LoaderManager.ILoaderCallbacks {

		public void OnLoadFinished (global::Android.Support.V4.Content.Loader p0, Java.Lang.Object p1)
		{
			OnLoadFinished (p0, (IList<Java.IO.File>) new JavaList<Java.IO.File> (p1.Handle, JniHandleOwnership.DoNotTransfer));
		}
	}
	public partial class FileLoader : Android.Support.V4.Content.AsyncTaskLoader {
		public override Java.Lang.Object LoadInBackground ()
		{
			return (Java.Lang.Object) LoadInBackgroundImpl ();
		}
	}
}"
			});
			using (var b = CreateDllBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		[Category ("Minor")]
		public void BindByteArrayInMethodParameter ()
		{
			var proj = new XamarinAndroidBindingProject () {
				IsRelease = true,
				AndroidClassParser = "class-parse",
			};
			proj.Jars.Add (new AndroidItem.EmbeddedJar ("Jars\\svg-android.jar") {
				WebContentFileNameFromAzure = "javaBindingIssue.jar"
			});
			using (var b = CreateDllBuilder ("temp/BindByteArrayInMethodParameter")) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void MergeAndroidManifest ()
		{
			var path = Path.Combine ("temp", TestName);
			var binding = new XamarinAndroidBindingProject {
				ProjectName = "AdalBinding",
				IsRelease = true,
			};
			binding.AndroidClassParser = "class-parse";
			binding.Jars.Add (new AndroidItem.LibraryProjectZip ("Jars\\adal-1.0.7.aar") {
				WebContentFileNameFromAzure = "adal-1.0.7.aar"
			});
			binding.MetadataXml = @"
<metadata>
	<remove-node path=""/api/package/class[@visibility='']"" />
</metadata>";
			using (var bindingBuilder = CreateDllBuilder (Path.Combine (path, binding.ProjectName))) {
				bindingBuilder.Build (binding);
				var proj = new XamarinAndroidApplicationProject {
					ProjectName = "App",
					IsRelease = true,
				};
				proj.OtherBuildItems.Add (new BuildItem ("ProjectReference", $"..\\{binding.ProjectName}\\{binding.ProjectName}.csproj"));
				using (var b = CreateApkBuilder (Path.Combine (path, "App"))) {
					Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
					var manifest = File.ReadAllText (Path.Combine (Root, b.ProjectDirectory, "obj", "Release", "android", "AndroidManifest.xml"));
					Assert.IsTrue (manifest.Contains ("com.microsoft.aad.adal.AuthenticationActivity"), "manifest merge failure");
				}
			}
		}

		[Test]
		public void AnnotationSupport ()
		{
			// https://trello.com/c/a36dDVS6/37-support-for-annotations-zip
			var binding = new XamarinAndroidBindingProject () {
				IsRelease = true,
			};
			binding.AndroidClassParser = "class-parse";
			binding.Jars.Add (new AndroidItem.LibraryProjectZip ("Jars\\mylibrary.aar") {
				WebContentFileNameFromAzure = "mylibrary-debug.aar"
			});
			using (var bindingBuilder = CreateDllBuilder ()) {
				Assert.IsTrue (bindingBuilder.Build (binding), "binding build failed");
				var cs_file = Path.Combine (Root, bindingBuilder.ProjectDirectory, "obj", "Release", "generated", "src", "Com.Example.Atsushi.Mylibrary.AnnotSample.cs");
				FileAssert.Exists (cs_file);
				StringAssert.Contains ("IntDef", File.ReadAllText (cs_file));
			}
		}

		[Test]
		public void BindingCustomJavaApplicationClass ()
		{
			var binding = new XamarinAndroidBindingProject () {
				IsRelease = true,
				ProjectName = "Binding",
			};
			binding.AndroidClassParser = "class-parse";

			using (var bindingBuilder = CreateDllBuilder ("temp/BindingCustomJavaApplicationClass/MultiDexBinding")) {
				string multidexJar = Path.Combine (bindingBuilder.AndroidMSBuildDirectory, "android-support-multidex.jar");
				binding.Jars.Add (new AndroidItem.EmbeddedJar (() => multidexJar));
				bindingBuilder.Build (binding);
				var proj = new XamarinAndroidApplicationProject ();
				proj.OtherBuildItems.Add (new BuildItem ("ProjectReference", $"..\\MultiDexBinding\\{binding.ProjectName}.csproj"));
				using (var b = CreateApkBuilder ("temp/BindingCustomJavaApplicationClass/App")) {
					Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				}
			}
		}

		[Test]
		public void BindngFilterUnsupportedNativeAbiLibraries ()
		{
			var binding = new XamarinAndroidBindingProject () {
				ProjectName = "Binding",
				IsRelease = true,
			};
			binding.AndroidClassParser = "class-parse";
			binding.Jars.Add (new AndroidItem.LibraryProjectZip ("Jars\\mylibrary.aar") {
				WebContentFileNameFromAzure = "card.io-5.3.0.aar"
			});
			var path = Path.Combine ("temp", TestName);
			using (var bindingBuilder = CreateDllBuilder (Path.Combine (path, binding.ProjectName))) {
				Assert.IsTrue (bindingBuilder.Build (binding), "binding build should have succeeded");
				var proj = new XamarinAndroidApplicationProject {
					ProjectName = "App"
				};
				proj.OtherBuildItems.Add (new BuildItem ("ProjectReference", $"..\\{binding.ProjectName}\\{binding.ProjectName}.csproj"));
				using (var b = CreateApkBuilder (Path.Combine (path, proj.ProjectName))) {
					Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				}
			}
		}

		[Test]
		public void BindingCheckHiddenFiles ()
		{
			var binding = new XamarinAndroidBindingProject {
				ProjectName = "Binding",
				IsRelease = true,
			};
			binding.AndroidClassParser = "class-parse";
			binding.Jars.Add (new AndroidItem.LibraryProjectZip ("Jars\\mylibrary.aar") {
				WebContentFileNameFromAzure = "mylibrary.aar"
			});
			binding.Jars.Add (new AndroidItem.EmbeddedJar ("Jars\\svg-android.jar") {
				WebContentFileNameFromAzure = "javaBindingIssue.jar"
			});
			var path = Path.Combine ("temp", TestName);
			using (var bindingBuilder = CreateDllBuilder (Path.Combine (path, binding.ProjectName))) {
				Assert.IsTrue (bindingBuilder.Build (binding), "binding build should have succeeded");
				var proj = new XamarinAndroidApplicationProject {
					ProjectName = "App",
				};
				proj.OtherBuildItems.Add (new BuildItem ("ProjectReference", $"..\\{binding.ProjectName}\\{binding.ProjectName}.csproj"));
				proj.AndroidManifest = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" xmlns:tools=""http://schemas.android.com/tools"" android:versionCode=""1"" android:versionName=""1.0"" package=""{proj.PackageName}"">
	<uses-sdk />
	<application android:label=""{proj.ProjectName}"" tools:replace=""android:label"">
	</application>
</manifest>";
				using (var b = CreateApkBuilder (Path.Combine (path, proj.ProjectName))) {
					Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
					var assemblyMap = b.Output.GetIntermediaryPath (Path.Combine ("lp", "map.cache"));
					FileAssert.Exists (assemblyMap);
					var libraryProjects = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "lp");
					var assemblyIdentityMap = b.Output.GetAssemblyMapCache ();
					var assemblyIdentityName = Builder.UseDotNet ? "mylibrary.aar" : $"{binding.ProjectName}.dll";
					var assemblyIdentity = assemblyIdentityMap.IndexOf (assemblyIdentityName).ToString ();
					var dsStorePath = Path.Combine (libraryProjects, assemblyIdentity, "jl");
					DirectoryAssert.Exists (dsStorePath);
					FileAssert.DoesNotExist (Path.Combine (dsStorePath, ".DS_Store"));
					DirectoryAssert.DoesNotExist (Path.Combine (dsStorePath, "_MACOSX"));
					var svgJar = Builder.UseDotNet ?
						Path.Combine (libraryProjects, assemblyIdentityMap.IndexOf ($"{binding.ProjectName}.aar").ToString (), "jl", "libs", "FD575F2BC294C4A9.jar") :
						Path.Combine (dsStorePath, "svg-android.jar");
					FileAssert.Exists (svgJar);
				}
			}
		}

		[Test]
		public void BindingDoNotPackage ()
		{
			var binding = new XamarinAndroidBindingProject () {
				IsRelease = true,
				Jars = {
					new AndroidItem.EmbeddedJar ("Jars\\svg-android.jar") {
						WebContentFileNameFromAzure = "javaBindingIssue.jar"
					}
				},
				AssemblyInfo = @"
using Java.Interop;
[assembly:DoNotPackage(""svg-android.jar"")]
			"
			};
			binding.AndroidClassParser = "class-parse";
			using (var bindingBuilder = CreateDllBuilder (Path.Combine ("temp", "BindingDoNotPackage", "Binding"))) {
				Assert.IsTrue (bindingBuilder.Build (binding), "binding build should have succeeded");
				var proj = new XamarinAndroidApplicationProject () {
					ProjectName = "App1",
					OtherBuildItems = {
						new BuildItem ("ProjectReference", "..\\Binding\\UnnamedProject.csproj")
					},
					Sources = {
						new BuildItem.Source ("MyClass.cs") { TextContent = ()=> @"
using System;
using Foo.Bar;

namespace Foo {
	public class MyClass : Java.Lang.Object, IUpdateListener {

		public MyClass ()
		{
			var sub = new Subscriber ();
		}

		public void OnUpdate (Java.Lang.Object p0)
		{
		}
	}
}
						"}
					}
				};
				using (var b = CreateApkBuilder (Path.Combine ("temp", "BindingDoNotPackage", "App"))) {
					Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				}
			}
		}

		[Test]
		public void RemoveEventHandlerResolution ()
		{
			var binding = new XamarinAndroidBindingProject () {
				IsRelease = true,
				UseLatestPlatformSdk = true,
				Jars = {
					new AndroidItem.LibraryProjectZip ("Jars\\ActionBarSherlock-4.3.1.zip") {
						WebContent = "https://github.com/xamarin/monodroid-samples/blob/master/ActionBarSherlock/ActionBarSherlock/Jars/ActionBarSherlock-4.3.1.zip?raw=true"
					}
				},
				AndroidClassParser = "class-parse",
				MetadataXml = @"<metadata>
	<remove-node path=""/api/package[starts-with(@name, 'com.actionbarsherlock.internal')]"" />
	<attr path=""/api/package[@name='com.actionbarsherlock']"" name=""managedName"">Xamarin.ActionbarSherlockBinding</attr>
	<attr path=""/api/package[@name='com.actionbarsherlock.widget']"" name=""managedName"">Xamarin.ActionbarSherlockBinding.Widget</attr>
	<attr path=""/api/package[@name='com.actionbarsherlock.app']"" name=""managedName"">Xamarin.ActionbarSherlockBinding.App</attr>
	<attr path=""/api/package[@name='com.actionbarsherlock.view']"" name=""managedName"">Xamarin.ActionbarSherlockBinding.Views</attr>
</metadata>",
			};
			binding.PackageReferences.Add (KnownPackages.AndroidSupportV4_27_0_2_1);
			using (var bindingBuilder = CreateDllBuilder (Path.Combine ("temp", "RemoveEventHandlerResolution", "Binding"))) {
				Assert.IsTrue (bindingBuilder.Build (binding), "binding build should have succeeded");
			}
		}

		[Test]
		[Category ("DotNetIgnore")]
		public void JavaDocJar ()
		{
			var binding = new XamarinAndroidBindingProject () {
				AndroidClassParser = "class-parse",
			};
			binding.SetProperty ("DocumentationFile", "UnnamedProject.xml");
			using (var bindingBuilder = CreateDllBuilder ()) {
				binding.Jars.Add (new AndroidItem.EmbeddedJar ("javasourcejartest.jar") {
					BinaryContent = () => ResourceData.JavaSourceJarTestJar,
				});
				binding.OtherBuildItems.Add (new BuildItem ("JavaDocJar", "javasourcejartest-javadoc.jar") {
					BinaryContent = () => ResourceData.JavaSourceJarTestJavadocJar,
				});
				Assert.IsTrue (bindingBuilder.Build (binding), "binding build should have succeeded");

				var cs_file = bindingBuilder.Output.GetIntermediaryPath (
					Path.Combine ("generated", "src", "Com.Xamarin.Android.Test.Msbuildtest.JavaSourceJarTest.cs"));
				FileAssert.Exists (cs_file);
				StringAssert.Contains ("Greet (string name, global::Java.Util.Date date)", File.ReadAllText (cs_file));
			}
		}

		[Test]
		public void JavaSourceJar ()
		{
			var binding = new XamarinAndroidBindingProject () {
				AndroidClassParser = "class-parse",
			};
			binding.SetProperty ("DocumentationFile", "UnnamedProject.xml");
			using (var bindingBuilder = CreateDllBuilder ()) {
				binding.Jars.Add (new AndroidItem.EmbeddedJar ("javasourcejartest.jar") {
					BinaryContent = () => ResourceData.JavaSourceJarTestJar,
				});
				binding.OtherBuildItems.Add (new BuildItem ("JavaSourceJar", "javasourcejartest-sources.jar") {
					BinaryContent = () => ResourceData.JavaSourceJarTestSourcesJar,
				});
				Assert.IsTrue (bindingBuilder.Build (binding), "binding build should have succeeded");

				var path    = Path.Combine (Root, bindingBuilder.ProjectDirectory, binding.OutputPath, "UnnamedProject.xml");
				var xml     = File.ReadAllText (path);
				Assert.IsTrue (xml.Contains ("<param name=\"name\">name to display.</param>"), "param `name` documentation not imported!");
			}
		}

		[Test]
		[TestCaseSource (nameof (ClassParseOptions))]
		public void DesignTimeBuild (string classParser)
		{
			var proj = new XamarinAndroidBindingProject {
				AndroidClassParser = classParser
			};
			proj.Jars.Add (new AndroidItem.LibraryProjectZip ("Jars\\material-menu-1.1.0.aar") {
				WebContent = "https://repo.jfrog.org/artifactory/libs-release-bintray/com/balysv/material-menu/1.1.0/material-menu-1.1.0.aar"
			});
			using (var b = CreateDllBuilder ()) {
				Assert.IsTrue (b.DesignTimeBuild (proj), "design-time build should have succeeded.");

				var intermediate = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				var api_xml = Path.Combine (intermediate, "api.xml");
				FileAssert.Exists (api_xml);
				var xml = XDocument.Load (api_xml);
				var element = xml.Element ("api");
				Assert.IsNotNull (element, "api.xml should contain an `api` element!");
				Assert.IsTrue (element.HasElements, "api.xml should contain elements!");

				var assemblyFile = Path.Combine (intermediate, proj.ProjectName + ".dll");
				using (var assembly = AssemblyDefinition.ReadAssembly (assemblyFile)) {
					var typeName = "Com.Balysv.Material.Drawable.Menu.MaterialMenuView";
					Assert.IsTrue (assembly.MainModule.Types.Any (t => t.FullName == typeName), $"Type `{typeName}` should exist!");
				}
			}
		}

		[Test]
		[TestCaseSource (nameof (ClassParseOptions))]
		public void NullableReferenceTypes (string classParser)
		{
			var proj = new XamarinAndroidBindingProject {
				AndroidClassParser = classParser,
				Jars = {
					new AndroidItem.EmbeddedJar ("foo.jar") {
						BinaryContent = () => ResourceData.JavaSourceJarTestJar,
					}
				}
			};
			proj.SetProperty ("Nullable", "enable");
			using (var b = CreateDllBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				var cs_file = b.Output.GetIntermediaryPath (
					Path.Combine ("generated", "src", "Com.Xamarin.Android.Test.Msbuildtest.JavaSourceJarTest.cs"));
				FileAssert.Exists (cs_file);
				StringAssert.Contains ("string? Greet", File.ReadAllText (cs_file));
			}
		}

		[Test]
		[TestCaseSource (nameof (ClassParseOptions))]
		public void BindDefaultInterfaceMethods (string classParser)
		{
			var proj = new XamarinAndroidBindingProject {
				IsRelease = true,
			};

			// The sources for the .jar is in the jar itself.
			string classesJarBase64 = @"
UEsDBBQACAgIANWk6UwAAAAAAAAAAAAAAAAJAAQATUVUQS1JTkYv/soAAAMAUEsHCAAAAAACAAAAAAA
AAFBLAwQUAAgICADVpOlMAAAAAAAAAAAAAAAAFAAAAE1FVEEtSU5GL01BTklGRVNULk1G803My0xLLS
7RDUstKs7Mz7NSMNQz4OVyLkpNLElN0XWqBAlY6BnEG5obKmj4FyUm56QqOOcXFeQXJZYA1WvycvFyA
QBQSwcIFGFrLUQAAABFAAAAUEsDBAoAAAgAAK2k6UwAAAAAAAAAAAAAAAAEAAAAY29tL1BLAwQKAAAI
AACtpOlMAAAAAAAAAAAAAAAADAAAAGNvbS94YW1hcmluL1BLAwQKAAAIAACwpOlMAAAAAAAAAAAAAAA
AEQAAAGNvbS94YW1hcmluL3Rlc3QvUEsDBBQACAgIAJmk6UwAAAAAAAAAAAAAAAAuAAAAY29tL3hhbW
FyaW4vdGVzdC9EZWZhdWx0SW50ZXJmYWNlTWV0aG9kcy5jbGFzc3WOvU7DMBSFjxsnKeWnXUE8QLrgh
ScAhBSJnwHE7qQ3JVUSC8dGFc/EwsrAA/BQiOuqLKnw8Pn4+LuWv38+vwCcY5biKMVUIKqMYWbzXEBe
mgUJTG/qju58W5B91EXDTbIkd6HtX3jj0G+DzPL5E28v3q8FJg/G25Ku6zB1ekWV9o3LO0e20iXdkns
2i/5spV+1QFaaVq11q23dKUe9U//4ArMwoRrdLdV9saLSJQICI4QVc4ogmTGfThBugFH0zuR/MpNNE7
x015NDL3C868VDL2XuYbL1joMTjI+BNpYC+/xcaA820uEvUEsHCIw1aijpAAAAhQEAAFBLAwQUAAgIC
ACYpOlMAAAAAAAAAAAAAAAAHAAAAERlZmF1bHRJbnRlcmZhY2VNZXRob2RzLmphdmF1zLEOwiAQBuCd
p7hRl0Zd2YyLgw9xwlGJFCocTWPTdxdSHarxxv///utR3bElUKFrRuwwWt8wJZZC9PnqrALrmaJBRXA
ig9nx+RNciG9BJzEJKKeXtnowIcBmCxNE4hw97CTMP6glPmJcuf1f91y5w7cbgtWQ3rCOBnSZymJhNX
nkPJYnUsziBVBLBwgzfz2miQAAAPUAAABQSwECFAAUAAgICADVpOlMAAAAAAIAAAAAAAAACQAEAAAAA
AAAAAAAAAAAAAAATUVUQS1JTkYv/soAAFBLAQIUABQACAgIANWk6UwUYWstRAAAAEUAAAAUAAAAAAAA
AAAAAAAAAD0AAABNRVRBLUlORi9NQU5JRkVTVC5NRlBLAQIKAAoAAAgAAK2k6UwAAAAAAAAAAAAAAAA
EAAAAAAAAAAAAAAAAAMMAAABjb20vUEsBAgoACgAACAAAraTpTAAAAAAAAAAAAAAAAAwAAAAAAAAAAA
AAAAAA5QAAAGNvbS94YW1hcmluL1BLAQIKAAoAAAgAALCk6UwAAAAAAAAAAAAAAAARAAAAAAAAAAAAA
AAAAA8BAABjb20veGFtYXJpbi90ZXN0L1BLAQIUABQACAgIAJmk6UyMNWoo6QAAAIUBAAAuAAAAAAAA
AAAAAAAAAD4BAABjb20veGFtYXJpbi90ZXN0L0RlZmF1bHRJbnRlcmZhY2VNZXRob2RzLmNsYXNzUEs
BAhQAFAAICAgAmKTpTDN/PaaJAAAA9QAAABwAAAAAAAAAAAAAAAAAgwIAAERlZmF1bHRJbnRlcmZhY2
VNZXRob2RzLmphdmFQSwUGAAAAAAcABwDOAQAAVgMAAAAA
";
			proj.Jars.Add (new AndroidItem.EmbeddedJar ("dim.jar") {
				BinaryContent = () => Convert.FromBase64String (classesJarBase64)
			});

			proj.AndroidClassParser = classParser;

			proj.SetProperty ("_EnableInterfaceMembers", "True");
			proj.SetProperty ("LangVersion", "preview");

			using (var b = CreateDllBuilder ()) {
				proj.NuGetRestore (b.ProjectDirectory);
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				string asmpath = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.ProjectName}.dll");
				Assert.IsTrue (File.Exists (asmpath), "assembly does not exist");

				var cs = b.Output.GetIntermediaryAsText (Path.Combine ("generated", "src", "Com.Xamarin.Test.IDefaultInterfaceMethods.cs"));
				Assert.IsTrue (cs.Contains ("int Quux ();"), "Quux not generated.");
				Assert.IsTrue (cs.Contains ("virtual unsafe int Foo ()"), "Foo not generated.");
				Assert.IsTrue (cs.Contains ("virtual unsafe int Bar {"), "Bar not generated.");
				Assert.IsTrue (cs.Contains ("set {"), "(Baz) setter not generated.");
			}
		}

		[Test]
		[Category ("DotNetIgnore")] //TODO: @(LibraryProjectProperties) not supported yet in .NET 5+
		public void BugzillaBug11964 ()
		{
			var proj = new XamarinAndroidBindingProject ();

			proj.Sources.Add (new BuildItem ("LibraryProjectProperties", "project.properties") {
				TextContent = () => ""
			});

			using (var builder = CreateDllBuilder ()) {
				builder.ThrowOnBuildFailure = false;
				Assert.IsFalse (builder.Build (proj), "Build should have failed.");
				string error = builder.LastBuildOutput
						.SkipWhile (x => !x.StartsWith ("Build FAILED."))
						.FirstOrDefault (x => x.Contains ("error XA1019:"));
				Assert.IsNotNull (error, "Build should have failed with XA1019.");
			}
		}

		[Test]
		public void BindingWithAndroidJavaSource ()
		{
			if (!Builder.UseDotNet)
				Assert.Ignore ("This Feature and Test is not available in Legacy Projects");
			var path = Path.Combine ("temp", TestName);
			var lib = new XamarinAndroidBindingProject () {
				ProjectName = "BindingsProject",
				AndroidClassParser = "class-parse",
				Jars = {
					new AndroidItem.EmbeddedJar ("javasourcejartest.jar") {
						BinaryContent = () => ResourceData.JavaSourceJarTestJar,
					}
				},
				OtherBuildItems = {
					new AndroidItem.AndroidJavaSource ("JavaSourceTestExtension.java") {
						Encoding = Encoding.ASCII,
						TextContent = () => ResourceData.JavaSourceTestExtension,
						Metadata = { { "Bind", "True"} },
					},
				},

			};
			var app = new XamarinAndroidApplicationProject () {
				ProjectName = "App",
				References = { new BuildItem.ProjectReference ($"..\\{lib.ProjectName}\\{lib.ProjectName}.csproj", lib.ProjectName, lib.ProjectGuid) }
			};
			using (var libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName), cleanupAfterSuccessfulBuild: false))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
				Assert.IsTrue (libBuilder.Build (lib), "Library build should have succeeded.");
				var generatedCode = Path.Combine (Root, libBuilder.ProjectDirectory, lib.IntermediateOutputPath,
					"generated", "src", "Com.Xamarin.Android.Test.Msbuildtest.JavaSourceTestExtension.cs");
				FileAssert.Exists (generatedCode, $"'{generatedCode}' should have been generated.");
				appBuilder.Target = "SignAndroidPackage";
				Assert.IsTrue (appBuilder.Build (app), "App build should have succeeded.");
				var hash = Files.HashString (Path.Combine (lib.IntermediateOutputPath,
					"binding", "bin", $"{lib.ProjectName}.jar").Replace ("\\", "/"));
				var intermediate = Path.Combine (Root, appBuilder.ProjectDirectory, app.IntermediateOutputPath);
				var lpPath = Path.Combine ("0", "jl", $"{lib.ProjectName}.jar");
				if (Builder.UseDotNet) {
					lpPath = Path.Combine ("1", "jl", "libs", $"{hash}.jar");
				}
				var jar = Path.Combine (intermediate, "lp", lpPath);
				FileAssert.Exists (jar, $"'{jar}' should have been generated.");
				var dexFile = Path.Combine (intermediate, "android", "bin", "classes.dex");
				FileAssert.Exists (dexFile);
				string className = "Lcom/xamarin/android/test/msbuildtest/JavaSourceJarTest;";
				Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
				className = "Lcom/xamarin/android/test/msbuildtest/JavaSourceTestExtension;";
				Assert.IsTrue (DexUtils.ContainsClass (className, dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
			}
		}

		[Test]
		public void LibraryProjectZipWithLint ()
		{
			var path = Path.Combine ("temp", TestName);
			var lib = new XamarinAndroidBindingProject () {
				ProjectName = "BindingsProject",
				AndroidClassParser = "class-parse",
				Jars = {
					new AndroidItem.LibraryProjectZip ("fragment-1.2.2.aar") {
						WebContent = "https://maven.google.com/androidx/fragment/fragment/1.2.2/fragment-1.2.2.aar"
					}
				},
				MetadataXml = @"<metadata><remove-node path=""/api/package[@name='androidx.fragment.app']/interface[@name='FragmentManager.OpGenerator']"" /></metadata>"
			};
			var app = new XamarinAndroidApplicationProject () {
				ProjectName = "App",
				IsRelease = true,
				LinkTool = "r8",
				References = { new BuildItem.ProjectReference ($"..\\{lib.ProjectName}\\{lib.ProjectName}.csproj", lib.ProjectName, lib.ProjectGuid) }
			};
			using (var libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName), cleanupAfterSuccessfulBuild: false))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
				Assert.IsTrue (libBuilder.Build (lib), "Library build should have succeeded.");
				Assert.IsTrue (appBuilder.Build (app), "App build should have succeeded.");
				StringAssertEx.DoesNotContain ("warning : Missing class: com.android.tools.lint.detector.api.Detector", appBuilder.LastBuildOutput, "Build output should contain no warnings about com.android.tools.lint.detector.api.Detector");
				var libraryProjects = Path.Combine (Root, appBuilder.ProjectDirectory, app.IntermediateOutputPath, "lp");
				Assert.IsFalse (Directory.EnumerateFiles (libraryProjects, "lint.jar", SearchOption.AllDirectories).Any (),
					"`lint.jar` should not be extracted!");
			}
		}

		/// <summary>
		/// Tests two .aar files with r-classes.jar
		/// </summary>
		[Test]
		public void AarWithRClassesJar ()
		{
			var path = Path.Combine ("temp", TestName);
			var lib1 = new XamarinAndroidBindingProject {
				ProjectName = "Library1",
				AndroidClassParser = "class-parse",
				Jars = {
					new AndroidItem.LibraryProjectZip ("Library1.aar") {
						BinaryContent = () => ResourceData.Library1Aar
					}
				},
			};
			var lib2 = new XamarinAndroidBindingProject {
				ProjectName = "Library2",
				AndroidClassParser = "class-parse",
				Jars = {
					new AndroidItem.LibraryProjectZip ("Library2.aar") {
						BinaryContent = () => ResourceData.Library2Aar
					}
				},
			};
			var app = new XamarinAndroidApplicationProject ();
			app.AddReference (lib1);
			app.AddReference (lib2);
			using (var lib1Builder = CreateDllBuilder (Path.Combine (path, lib1.ProjectName)))
			using (var lib2Builder = CreateDllBuilder (Path.Combine (path, lib2.ProjectName)))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
				Assert.IsTrue (lib1Builder.Build (lib1), "Library1 build should have succeeded.");
				Assert.IsTrue (lib2Builder.Build (lib2), "Library2 build should have succeeded.");
				Assert.IsTrue (appBuilder.Build (app), "App build should have succeeded.");
			}
		}
	}
}
