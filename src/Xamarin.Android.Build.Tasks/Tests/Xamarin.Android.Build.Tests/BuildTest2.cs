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
	public partial class BuildTest2 : BaseTest
	{
		static object [] MarshalMethodsDefaultStatusSource = new object [] {
			new object[] {
				/* isRelease */              true,
				/* marshalMethodsEnabled */  false,
			},
			new object[] {
				/* isRelease */              true,
				/* marshalMethodsEnabled */  true,
			},
			new object[] {
				/* isRelease */              false,
				/* marshalMethodsEnabled */  true,
			},
		};

		[Test]
		[TestCaseSource (nameof (MarshalMethodsDefaultStatusSource))]
		public void MarshalMethodsDefaultEnabledStatus (bool isRelease, bool marshalMethodsEnabled)
		{
			var abis = new [] { "armeabi-v7a", "x86" };
			AndroidTargetArch[] supportedArches = new [] {
				AndroidTargetArch.Arm,
				AndroidTargetArch.X86,
			};
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = isRelease
			};
			proj.SetProperty (KnownProperties.AndroidEnableMarshalMethods, marshalMethodsEnabled.ToString ());
			proj.SetRuntimeIdentifiers (abis);
			bool shouldMarshalMethodsBeEnabled = isRelease && marshalMethodsEnabled;

			using (var b = CreateApkBuilder ()) {
				b.Verbosity = LoggerVerbosity.Diagnostic;
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				Assert.IsTrue (
					StringAssertEx.ContainsText (b.LastBuildOutput, $"_AndroidUseMarshalMethods = {shouldMarshalMethodsBeEnabled}"),
					$"The '_AndroidUseMarshalMethods' MSBuild property should have had the value of '{shouldMarshalMethodsBeEnabled}'"
				);

				string objPath = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				List<EnvironmentHelper.EnvironmentFile> envFiles = EnvironmentHelper.GatherEnvironmentFiles (
					objPath,
					String.Join (";", supportedArches.Select (arch => MonoAndroidHelper.ArchToAbi (arch))),
					true
				);
				EnvironmentHelper.ApplicationConfig app_config = EnvironmentHelper.ReadApplicationConfig (envFiles);

				Assert.That (app_config, Is.Not.Null, "application_config must be present in the environment files");
				Assert.AreEqual (app_config.marshal_methods_enabled, shouldMarshalMethodsBeEnabled, $"Marshal methods enabled status should be '{shouldMarshalMethodsBeEnabled}', but it was '{app_config.marshal_methods_enabled}'");
			}
		}

		[Test]
		public void CompressedWithoutLinker ()
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true
			};
			proj.SetProperty (proj.ReleaseProperties, KnownProperties.AndroidLinkMode, AndroidLinkMode.None.ToString ());
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void BuildBasicApplication ([Values (true, false)] bool isRelease, [Values ("", "en_US.UTF-8", "sv_SE.UTF-8")] string langEnvironmentVariable)
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = isRelease,
			};

			Dictionary<string, string> envvar = null;
			if (!String.IsNullOrEmpty (langEnvironmentVariable)) {
				envvar = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase) {
					{"LANG", langEnvironmentVariable},
				};
			}

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj, environmentVariables: envvar), "Build should have succeeded.");
			}
		}

		[Test]
		public void BuildWithMSBuildExe ([Values (true, false)] bool isRelease)
		{
			if (!IsWindows)
				Assert.Ignore ("Test is only valid on Windows");

			var proj = new XamarinAndroidApplicationProject {
				IsRelease = isRelease,
			};

			using var b = CreateApkBuilder ();
			b.UseMSBuildExe = true;
			Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
		}

		[Test]
		public void BuildBasicApplicationThenMoveIt ([Values (true, false)] bool isRelease)
		{
			string path = Path.Combine (Root, "temp", TestName, "App1");
			var proj = new XamarinAndroidApplicationProject {
				ProjectName = "App",
				IsRelease = isRelease,
			};
			using (var b = CreateApkBuilder (path)) {
				b.Target = "Build";
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				b.Target = "SignAndroidPackage";
				Assert.IsTrue (b.Build (proj), "SignAndroidPackage should have succeeded.");


				string path2 = Path.Combine (Root, "temp", TestName, "App2");
				if (Directory.Exists (path2))
					Directory.Delete (path2, recursive: true);
				Directory.Move (path, path2);
				b.ProjectDirectory = path2;
				foreach (var r in proj.AndroidResources)
					r.Timestamp = DateTime.UtcNow;
				b.Target = "Build";
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "Build should have succeeded.");
				Assert.IsTrue (!b.Output.IsTargetSkipped ("_CleanIntermediateIfNeeded"), "_CleanIntermediateIfNeeded should be built.");
				Assert.IsTrue (!b.Output.IsTargetSkipped ("_CompileResources"), "_CompileResources Should have built.");
				b.Target = "SignAndroidPackage";
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "SignAndroidPackage should have succeeded.");

			}
		}

		public static string GetLinkedPath (ProjectBuilder builder, bool isRelease, string filename)
		{
			return isRelease ?
				builder.Output.GetIntermediaryPath (Path.Combine ("android-arm64", "linked", filename)) :
				builder.Output.GetIntermediaryPath (Path.Combine ("android", "assets", filename));
		}

		[Test]
		public void BuildReleaseArm64 ([Values (false, true)] bool forms)
		{
			var proj = forms ?
				new XamarinFormsAndroidApplicationProject () :
				new XamarinAndroidApplicationProject ();
			proj.IsRelease = true;
			proj.AotAssemblies = false; // Release defaults to Profiled AOT for .NET 6
			proj.SetAndroidSupportedAbis ("arm64-v8a");
			proj.SetProperty ("LinkerDumpDependencies", "True");
			proj.SetProperty ("AndroidUseAssemblyStore", "False");

			byte [] apkDescData;
			var flavor = (forms ? "XForms" : "Simple") + "DotNet";
			var apkDescFilename = $"BuildReleaseArm64{flavor}.apkdesc";
			var apkDescReference = "reference.apkdesc";
			using (var stream = typeof (XamarinAndroidApplicationProject).Assembly.GetManifestResourceStream ($"Xamarin.ProjectTools.Resources.Base.{apkDescFilename}")) {
				apkDescData = new byte [stream.Length];
				stream.Read (apkDescData, 0, (int) stream.Length);
			}
			proj.OtherBuildItems.Add (new BuildItem ("ApkDescFile", apkDescReference) { BinaryContent = () => apkDescData });

			// use BuildHelper.CreateApkBuilder so that the test directory is not removed in tearup
			using (var b = BuildHelper.CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				var depsFile = GetLinkedPath (b, true, "linker-dependencies.xml");
				FileAssert.Exists (depsFile);

				const int ApkSizeThreshold = 5 * 1024;
				const int AssemblySizeThreshold = 5 * 1024;
				const int ApkPercentChangeThreshold = 3;
				const int FilePercentChangeThreshold = 5;
				var regressionCheckArgs = $"--test-apk-size-regression={ApkSizeThreshold} --test-assembly-size-regression={AssemblySizeThreshold}";
				//TODO Only make these checks more lenient during early previews. Report if any files increase by more than 5% or if the package size increases by more than 3%
				regressionCheckArgs = $"--test-apk-percentage-regression=\"{ApkPercentChangeThreshold}\" --test-content-percentage-regression=\"{FilePercentChangeThreshold}\"";
				var apkFile = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, proj.PackageName + "-Signed.apk");
				var apkDescPath = Path.Combine (Root, apkDescFilename);
				var apkDescReferencePath = Path.Combine (Root, b.ProjectDirectory, apkDescReference);
				var (code, stdOut, stdErr) = RunApkDiffCommand ($"-s --save-description-2={apkDescPath} --descrease-is-regression {regressionCheckArgs} {apkDescReferencePath} {apkFile}");
				Assert.IsTrue (code == 0, $"apkdiff regression test failed with exit code: {code}\ncontext: https://github.com/xamarin/xamarin-android/blob/main/Documentation/project-docs/ApkSizeRegressionChecks.md\nstdOut: {stdOut}\nstdErr: {stdErr}");
			}
		}

		static readonly object [] BuildHasNoWarningsSource = new object [] {
			new object [] {
				/* isRelease */     false,
				/* xamarinForms */  false,
				/* multidex */      false,
				/* packageFormat */ "apk",
			},
			new object [] {
				/* isRelease */     false,
				/* xamarinForms */  true,
				/* multidex */      false,
				/* packageFormat */ "apk",
			},
			new object [] {
				/* isRelease */     false,
				/* xamarinForms */  true,
				/* multidex */      true,
				/* packageFormat */ "apk",
			},
			new object [] {
				/* isRelease */     true,
				/* xamarinForms */  false,
				/* multidex */      false,
				/* packageFormat */ "apk",
			},
			new object [] {
				/* isRelease */     true,
				/* xamarinForms */  true,
				/* multidex */      false,
				/* packageFormat */ "apk",
			},
			new object [] {
				/* isRelease */     false,
				/* xamarinForms */  false,
				/* multidex */      false,
				/* packageFormat */ "aab",
			},
			new object [] {
				/* isRelease */     true,
				/* xamarinForms */  false,
				/* multidex */      false,
				/* packageFormat */ "aab",
			},
		};

		[Test]
		[TestCaseSource (nameof (BuildHasNoWarningsSource))]
		public void BuildHasNoWarnings (bool isRelease, bool xamarinForms, bool multidex, string packageFormat)
		{
			var proj = xamarinForms ?
				new XamarinFormsAndroidApplicationProject () :
				new XamarinAndroidApplicationProject ();
			proj.IsRelease = isRelease;
			// Enable full trimming
			if (!xamarinForms && isRelease) {
				proj.TrimModeRelease = TrimMode.Full;
			}
			if (multidex) {
				proj.SetProperty ("AndroidEnableMultiDex", "True");
			}
			if (packageFormat == "aab") {
				// Disable fast deployment for aabs, because we give:
				//	XA0119: Using Fast Deployment and Android App Bundles at the same time is not recommended.
				proj.EmbedAssembliesIntoApk = true;
			}
			// FIXME: Precompiling failed for TraceReloggerLib.dll, Dia2Lib.dll with exit code 1
			if (!isRelease)
				proj.PackageReferences.Add (new Package { Id = "BenchmarkDotNet", Version = "0.13.1" });
			proj.SetProperty ("XamarinAndroidSupportSkipVerifyVersions", "True"); // Disables API 29 warning in Xamarin.Build.Download
			proj.SetProperty ("AndroidPackageFormat", packageFormat);
			proj.SetProperty ("TrimmerSingleWarn", "false");
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				b.AssertHasNoWarnings ();
				Assert.IsFalse (StringAssertEx.ContainsText (b.LastBuildOutput, "Warning: end of file not at end of a line"),
					"Should not get a warning from the <CompileNativeAssembly/> task.");
				var lockFile = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, ".__lock");
				FileAssert.DoesNotExist (lockFile);
			}
		}

		[Test]
		[TestCase ("AndroidFastDeploymentType", "Assemblies", true, false)]
		[TestCase ("AndroidFastDeploymentType", "Assemblies", false, false)]
		[TestCase ("_AndroidUseJavaLegacyResolver", "true", false, true)]
		[TestCase ("_AndroidUseJavaLegacyResolver", "true", true, true)]
		[TestCase ("_AndroidEmitLegacyInterfaceInvokers", "true", false, true)]
		[TestCase ("_AndroidEmitLegacyInterfaceInvokers", "true", true, true)]
		public void XA1037PropertyDeprecatedWarning (string property, string value, bool isRelease, bool isBindingProject)
		{
			XamarinAndroidProject proj = isBindingProject ? new XamarinAndroidBindingProject () : new XamarinAndroidApplicationProject ();
			proj.IsRelease = isRelease;
			proj.SetProperty (property, value);
			
			using (ProjectBuilder b = isBindingProject ? CreateDllBuilder (Path.Combine ("temp", TestName)) : CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				Assert.IsTrue (StringAssertEx.ContainsText (b.LastBuildOutput, $"The '{property}' MSBuild property is deprecated and will be removed"),
					$"Should not get a warning about the {property} property");
			}
		}

		[Test]
		public void ClassLibraryHasNoWarnings ()
		{
			var proj = new XamarinAndroidLibraryProject ();
			//NOTE: these properties should not affect class libraries at all
			proj.SetProperty ("AndroidPackageFormat", "aab");
			proj.SetProperty ("AotAssemblies", "true");
			proj.SetProperty ("AndroidEnableMultiDex", "true");
			using (var b = CreateDllBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				b.AssertHasNoWarnings ();

				// $(AndroidEnableMultiDex) should not add android-support-multidex.jar!
				var aarPath = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.ProjectName}.aar");
				using var zip = Xamarin.Tools.Zip.ZipArchive.Open (aarPath, FileMode.Open);
				Assert.IsFalse (zip.Any (e => e.FullName.EndsWith (".jar", StringComparison.OrdinalIgnoreCase)),
					$"{aarPath} should not contain a .jar file!");
			}
		}

		[Test]
		public void BuildBasicApplicationWithNuGetPackageConflicts ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				PackageReferences = {
					new Package () {
						Id = "System.Buffers",
						Version = "4.4.0",
						TargetFramework = "monoandroid90",
					},
					new Package () {
						Id = "System.Memory",
						Version = "4.5.1",
						TargetFramework = "monoandroid90",
					},
				}
			};

			proj.Sources.Add (new BuildItem ("Compile", "IsAndroidDefined.fs") {
				TextContent = () => @"
using System;

class MemTest {
	static void Test ()
	{
		var x = new Memory<int> ().Length;
		Console.WriteLine (x);

		var array = new byte [100];
		var arraySpan = new Span<byte> (array);
		Console.WriteLine (arraySpan.IsEmpty);
	}
}"
			});

			using (var b = CreateApkBuilder ("temp/BuildBasicApplicationWithNuGetPackageConflicts")) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		static object [] BuildBasicApplicationFSharpSource = new object [] {
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
		[TestCaseSource (nameof (BuildBasicApplicationFSharpSource))]
		[Category ("Minor"), Category ("FSharp")]
		[NonParallelizable] // parallel NuGet restore causes failures
		public void BuildBasicApplicationFSharp (bool isRelease, bool aot)
		{
			var proj = new XamarinAndroidApplicationProject {
				Language = XamarinAndroidProjectLanguage.FSharp,
				IsRelease = isRelease,
				AotAssemblies = aot,
			};
			using var b = CreateApkBuilder ();
			Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
		}

		[Test]
		[NonParallelizable]
		public void BuildBasicApplicationAppCompat ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			var packages = proj.PackageReferences;
			packages.Add (KnownPackages.AndroidXAppCompat);
			proj.MainActivity = proj.DefaultMainActivity.Replace ("public class MainActivity : Activity", "public class MainActivity : AndroidX.AppCompat.App.AppCompatActivity");
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void DuplicateRJavaOutput ()
		{
			var proj = new XamarinAndroidApplicationProject {
				PackageReferences = {
					new Package { Id = "Xamarin.GooglePlayServices.Base", Version = "118.2.0.5" },
					new Package { Id = "Xamarin.GooglePlayServices.Basement", Version = "118.2.0.5" },
					new Package { Id = "Xamarin.GooglePlayServices.Tasks", Version = "118.0.2.6" },
				}
			};
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "build should have succeeded.");
				var lines = b.LastBuildOutput.Where (l => l.Contains ("Writing:") && l.Contains ("R.java"));
				var hash = new HashSet<string> (StringComparer.Ordinal);
				foreach (var duplicate in lines.Where (i => !hash.Add (i))) {
					Assert.Fail ($"Duplicate: {duplicate}");
				}
			}
		}

		[Test]
		[Category ("XamarinBuildDownload")]
		[NonParallelizable] // parallel NuGet restore causes failures
		public void BuildXamarinFormsMapsApplication ([Values (true, false)] bool multidex)
		{
			var proj = new XamarinFormsMapsApplicationProject ();
			if (multidex)
				proj.SetProperty ("AndroidEnableMultiDex", "True");
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "first should have succeeded.");
				b.BuildLogFile = "build2.log";
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "second should have succeeded.");
				var targets = new [] {
					"_CompileResources",
					"_UpdateAndroidResgen",
				};
				foreach (var target in targets) {
					b.Output.AssertTargetIsSkipped (target);
				}
				proj.Touch ("MainPage.xaml");
				b.BuildLogFile = "build3.log";
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "third should have succeeded.");
				foreach (var target in targets) {
					b.Output.AssertTargetIsSkipped (target);
				}
				b.Output.AssertTargetIsNotSkipped ("CoreCompile");
				b.BuildLogFile = "build4.log";
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true, saveProject: false), "forth should have succeeded.");
				foreach (var target in targets) {
					b.Output.AssertTargetIsSkipped (target);
				}
			}
		}

		[Test]
		[NonParallelizable]
		public void SkipConvertResourcesCases ()
		{
			var target = "ConvertResourcesCases";
			var proj = new XamarinFormsAndroidApplicationProject ();
			proj.OtherBuildItems.Add (new BuildItem ("AndroidAarLibrary", "Jars\\material-menu-1.1.0.aar") {
				WebContent = "https://repo1.maven.org/maven2/com/balysv/material-menu/1.1.0/material-menu-1.1.0.aar"
			});
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				b.Verbosity = LoggerVerbosity.Detailed;
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				Assert.IsFalse (b.Output.IsTargetSkipped (target), $"`{target}` should not be skipped.");

				List<string> skipped = new List<string> (), processed = new List<string> ();
				bool convertResourcesCases = false;
				foreach (var text in b.LastBuildOutput) {
					var line = text.Trim ();
					if (!convertResourcesCases) {
						convertResourcesCases = line.StartsWith ($"Task \"{target}\"", StringComparison.OrdinalIgnoreCase);
					} else if (line.StartsWith ($"Done executing task \"{target}\"", StringComparison.OrdinalIgnoreCase)) {
						convertResourcesCases = false; //end of target
					}
					if (convertResourcesCases) {
						if (line.IndexOf ("Processing:", StringComparison.OrdinalIgnoreCase) >= 0) {
							//Processing: obj\Debug\res\layout\main.xml   10/29/2018 8:19:36 PM > 1/1/0001 12:00:00 AM
							processed.Add (line);
						} else if (line.IndexOf ("Skipping:", StringComparison.OrdinalIgnoreCase) >= 0) {
							//Skipping: `obj\Debug\lp\5\jl\res` via `AndroidSkipResourceProcessing`, original file: `bin\TestDebug\temp\packages\Xamarin.Android.Support.Compat.27.0.2.1\lib\MonoAndroid81\Xamarin.Android.Support.Compat.dll`...
							skipped.Add (line);
						}
					}
				}

				var resources = new [] {
					Path.Combine ("layout", "main.xml"),
					Path.Combine ("layout", "tabbar.xml"),
					Path.Combine ("layout", "toolbar.xml"),
					Path.Combine ("values", "colors.xml"),
					Path.Combine ("values", "strings.xml"),
					Path.Combine ("values", "styles.xml"),
				};

				foreach (var resource in resources) {
					Assert.IsTrue (processed.ContainsText (resource), $"`{target}` should process `{resource}`.");
				}

				var files = new List<string> {
					"material-menu-1.1.0.aar",
				};
				files.Add ("androidx.core.core.aar");
				files.Add ("androidx.transition.transition.aar");
				files.Add ("androidx.recyclerview.recyclerview.aar");
				files.Add ("androidx.coordinatorlayout.coordinatorlayout.aar");
				files.Add ("androidx.cardview.cardview.aar");
				files.Add ("androidx.appcompat.appcompat-resources.aar");
				files.Add ("androidx.appcompat.appcompat.aar");
				files.Add ("com.google.android.material.material.aar");
				foreach (var file in files) {
					Assert.IsTrue (StringAssertEx.ContainsText (skipped, file), $"`{target}` should skip `{file}`.");
				}
			}
		}

		[Test]
		public void BuildInParallel ()
		{
			if (!IsWindows) {
				//TODO: one day we should fix the problems here, various MSBuild tasks step on each other when built in parallel
				Assert.Ignore ("Currently ignoring this test on non-Windows platforms.");
			}

			var proj = new XamarinFormsAndroidApplicationProject ();


			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				//We don't want these things stepping on each other
				b.BuildLogFile = null;
				b.Save (proj, saveProject: true);

				Parallel.For (0, 5, i => {
					try {
						//NOTE: things are going to break here
						b.Build (proj);
					} catch (Exception exc) {
						TestContext.WriteLine ("Expected error in {0}: {1}", nameof (BuildInParallel), exc);
					}
				});

				//The key here, is a build afterward should work
				b.BuildLogFile = "after.log";
				Assert.IsTrue (b.Build (proj), "The build after a parallel failed build should succeed!");
			}
		}

		[Test]
		public void CheckKeystoreIsCreated ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			using (var b = CreateApkBuilder ("temp/CheckKeystoreIsCreated", false, false)) {
				var file = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "debug.keystore");
				var p = new string [] {
					$"_ApkDebugKeyStore={file}",
				};
				Assert.IsTrue (b.Build (proj, parameters: p), "Build should have succeeded.");
				FileAssert.Exists (file, $"{file} should have been created.");
			}
		}

		[Test]
		[Category ("FSharp")]
		[NonParallelizable] // parallel NuGet restore causes failures
		public void FSharpAppHasAndroidDefine ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				Language = XamarinAndroidProjectLanguage.FSharp,
			};
			proj.Sources.Add (new BuildItem ("Compile", "IsAndroidDefined.fs") {
				TextContent = () => @"
module Xamarin.Android.Tests
// conditional compilation; can we elicit a compile-time error?
let x =
#if __ANDROID__
  42
#endif  // __ANDROID__

printf ""%d"" x
",
			});
			using (var b = CreateApkBuilder ("temp/" + nameof (FSharpAppHasAndroidDefine))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void DesignTimeBuildHasAndroidDefines ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			var androidDefines = new List<string> ();
			for (int i = 1; i <= XABuildConfig.AndroidDefaultTargetDotnetApiLevel; ++i) {
				androidDefines.Add ($"!__ANDROID_{i}__");
			}
			proj.Sources.Add (new BuildItem ("Compile", "IsAndroidDefined.cs") {
				TextContent = () => $@"
namespace Xamarin.Android.Tests
{{
	public class Foo {{
		public void FooMethod () {{
#if !__ANDROID__ || !__MOBILE__ || {string.Join (" || ", androidDefines)}
			Compile Error please :)
#endif
		}}
	}}
}}
",
			});
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				b.Target = "Compile";
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void SwitchBetweenDesignTimeBuild ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\layout\\custom_text.xml") {
				TextContent = () => @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<LinearLayout xmlns:android=""http://schemas.android.com/apk/res/android""
	android:orientation = ""vertical""
	android:layout_width = ""fill_parent""
	android:layout_height = ""fill_parent"">
	<unamedproject.CustomTextView
		android:id = ""@+id/myText1""
		android:layout_width = ""fill_parent""
		android:layout_height = ""wrap_content""
		android:text = ""namespace_lower"" />
	<UnamedProject.CustomTextView
		android:id = ""@+id/myText2""
		android:layout_width = ""fill_parent""
		android:layout_height = ""wrap_content""
		android:text = ""namespace_proper"" />
</LinearLayout>"
			});
			proj.Sources.Add (new BuildItem.Source ("CustomTextView.cs") {
				TextContent = () => @"using Android.Widget;
using Android.Content;
using Android.Util;
namespace UnamedProject
{
	public class CustomTextView : TextView
	{
		public CustomTextView(Context context, IAttributeSet attributes) : base(context, attributes)
		{
		}
	}
}"
			});

			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "first *regular* build should have succeeded.");
				var build_props = b.Output.GetIntermediaryPath ("build.props");
				var designtime_build_props = b.Output.GetIntermediaryPath (Path.Combine ("designtime", "build.props"));
				FileAssert.Exists (build_props, "build.props should exist after a first `Build`.");
				FileAssert.DoesNotExist (designtime_build_props, "designtime/build.props should *not* exist after a first `Build`.");

				b.Target = "Compile";
				Assert.IsTrue (b.Build (proj, parameters: new [] { "DesignTimeBuild=True" }), "first design-time build should have succeeded.");
				FileAssert.Exists (build_props, "build.props should exist after a design-time build.");
				FileAssert.Exists (designtime_build_props, "designtime/build.props should exist after a design-time build.");

				b.Target = "Build";
				Assert.IsTrue (b.Build (proj), "second *regular* build should have succeeded.");
				FileAssert.Exists (build_props, "build.props should exist after the second `Build`.");
				FileAssert.Exists (designtime_build_props, "designtime/build.props should exist after the second `Build`.");

				//NOTE: none of these targets should run, since we have not actually changed anything!
				var targetsToBeSkipped = new [] {
					//TODO: We would like for this assertion to work, but the <Compile /> item group changes between DTB and regular builds
					//      $(IntermediateOutputPath)designtime\Resource.designer.cs -> Resources\Resource.designer.cs
					//      And so the built assembly changes between DTB and regular build, triggering `_LinkAssembliesNoShrink`
					//"_LinkAssembliesNoShrink",
					"_UpdateAndroidResgen",
					"_BuildLibraryImportsCache",
					"_CompileJava",
				};
				foreach (var targetName in targetsToBeSkipped) {
					Assert.IsTrue (b.Output.IsTargetSkipped (targetName), $"`{targetName}` should be skipped!");
				}

				b.Target = "Clean";
				Assert.IsTrue (b.Build (proj), "clean should have succeeded.");

				FileAssert.DoesNotExist (build_props, "build.props should *not* exist after `Clean`.");
				FileAssert.Exists (designtime_build_props, "designtime/build.props should exist after `Clean`.");
			}
		}

		[Test]
		public void AndroidResourceNotExist ()
		{
			var proj = new XamarinAndroidApplicationProject {
				Imports = {
					new Import (() => "foo.projitems") {
						TextContent = () =>
@"<Project>
	<ItemGroup>
		<AndroidResource Include=""Resources\layout\noexist.xml"" />
	</ItemGroup>
</Project>"
					},
				},
			};
			using (var b = CreateApkBuilder ()) {
				b.ThrowOnBuildFailure = false;
				Assert.IsFalse (b.Build (proj), "Build should have failed.");
				Assert.IsTrue (b.LastBuildOutput.ContainsText ("XA2001"), "Should recieve XA2001 error.");
			}
		}

		[Test]
		public void TargetFrameworkMonikerAssemblyAttributesPath ()
		{
			const string filePattern = ".NETCoreApp,Version=*.AssemblyAttributes.cs";
			var proj = new XamarinAndroidApplicationProject {
			};

			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "build should have succeeded.");

				var intermediate = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);

				var new_assemblyattributespath = Directory.EnumerateFiles (intermediate, filePattern).SingleOrDefault ();
				Assert.IsNotNull (new_assemblyattributespath, $"A *single* file of pattern {filePattern} should exist in `$(IntermediateOutputPath)`.");
			}
		}

		[Test]
		[NonParallelizable]
		public void CheckTimestamps ([Values (true, false)] bool isRelease)
		{
			var start = DateTime.UtcNow.AddSeconds (-1);
			var proj = new XamarinFormsAndroidApplicationProject {
				IsRelease = isRelease,

			};

			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				//To be sure we are at a clean state
				var projectDir = Path.Combine (Root, b.ProjectDirectory);
				if (Directory.Exists (projectDir))
					Directory.Delete (projectDir, true);

				var intermediate = Path.Combine (projectDir, proj.IntermediateOutputPath);
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");

				// None of these files should be *older* than the starting time of this test!
				var files = Directory.EnumerateFiles (intermediate, "*", SearchOption.AllDirectories).ToList ();
				foreach (var file in files) {
					//NOTE: ILLink from the dotnet/sdk currently copies assemblies with older timestamps, and only $(_LinkSemaphore) is touched
					//see: https://github.com/dotnet/sdk/blob/a245b6ff06b483927e57d953b803a390ad31db95/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.ILLink.targets#L113-L116
					if (Directory.GetParent (file).Name == "linked") {
						continue;
					}
					var info = new FileInfo (file);
					Assert.IsTrue (info.LastWriteTimeUtc > start, $"`{file}` is older than `{start}`, with a timestamp of `{info.LastWriteTimeUtc}`!");
				}

				//Build again after a code change (renamed Java.Lang.Object subclass), checking a few files
				proj.MainActivity = proj.DefaultMainActivity.Replace ("MainActivity", "MainActivity2");
				proj.Touch ("MainActivity.cs");
				start = DateTime.UtcNow;
				Assert.IsTrue (b.Build (proj), "second build should have succeeded.");

				// These files won't exist in OSS .NET for Android, thus the existence check and
				// Assert.Ignore below. They will also not exist in the commercial version of
				// .NET for Android unless fastdev is enabled.
				foreach (var file in new [] { "typemap.mj", "typemap.jm" }) {
					var info = new FileInfo (Path.Combine (intermediate, "android", file));
					if (info.Exists) {
						Assert.IsTrue (info.LastWriteTimeUtc > start, $"`{file}` is older than `{start}`, with a timestamp of `{info.LastWriteTimeUtc}`!");
					}
				}

				//One last build with no changes
				Assert.IsTrue (b.Build (proj), "third build should have succeeded.");
				b.Output.AssertTargetIsSkipped (isRelease ? KnownTargets.LinkAssembliesShrink : KnownTargets.LinkAssembliesNoShrink);
				b.Output.AssertTargetIsSkipped ("_UpdateAndroidResgen");
				b.Output.AssertTargetIsSkipped ("_BuildLibraryImportsCache");
				b.Output.AssertTargetIsSkipped ("_CompileJava");
			}
		}

		[Test]
		[NonParallelizable] // On MacOS, parallel /restore causes issues
		public void BuildApplicationAndClean ([Values (false, true)] bool isRelease, [Values ("apk", "aab")] string packageFormat)
		{
			var proj = new XamarinFormsAndroidApplicationProject {
				IsRelease = isRelease,
			};
			proj.SetProperty ("AndroidPackageFormat", packageFormat);
			if (packageFormat == "aab")
				// Disable fast deployment for aabs because it is not currently compatible and so gives an XA0119 build error.
				proj.EmbedAssembliesIntoApk = true;
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				Assert.IsTrue (b.Clean (proj), "Clean should have succeeded.");

				var ignoreFiles = new string [] {
					"TemporaryGeneratedFile",
					"FileListAbsolute.txt",
				};
				var files = Directory.GetFiles (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath), "*", SearchOption.AllDirectories)
					.Where (x => !ignoreFiles.Any (i => !Path.GetFileName (x).Contains (i)));
				Assert.AreEqual (0, files.Count (), "{0} should be Empty. Found {1}", proj.IntermediateOutputPath, string.Join (Environment.NewLine, files));
				files = Directory.GetFiles (Path.Combine (Root, b.ProjectDirectory, proj.OutputPath), "*", SearchOption.AllDirectories);
				Assert.AreEqual (0, files.Count (), "{0} should be Empty. Found {1}", proj.OutputPath, string.Join (Environment.NewLine, files));
			}
		}

		[Test]
		public void BuildApplicationWithLibraryAndClean ([Values (false, true)] bool isRelease)
		{
			var lib = new XamarinAndroidLibraryProject () {
				IsRelease = isRelease,
				ProjectName = "Library1",
				OtherBuildItems = {
					new AndroidItem.AndroidAsset ("Assets\\somefile.txt") {
						TextContent =  () => "some readonly file...",
						Attributes = FileAttributes.ReadOnly,
					},
				},
			};
			for (int i = 0; i < 1000; i++) {
				lib.OtherBuildItems.Add (new AndroidItem.AndroidAsset (string.Format ("Assets\\somefile{0}.txt", i)) {
					TextContent = () => "some readonly file...",
					Attributes = FileAttributes.ReadOnly | FileAttributes.Normal,
				});
				lib.AndroidResources.Add (new AndroidItem.AndroidResource (string.Format ("Resources\\values\\Strings{0}.xml", i)) {
					TextContent = () => string.Format (@"<?xml version=""1.0"" encoding=""utf-8""?>
<resources>
	<string name=""hello{0}"">Hello World, Click Me! {0}</string>
</resources>", i++),
					Attributes = FileAttributes.ReadOnly | FileAttributes.Normal,
				});
			}
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = isRelease,
				ProjectName = "App1",
				References = { new BuildItem ("ProjectReference", "..\\Library1\\Library1.csproj") },
			};
			var projectPath = Path.Combine ("temp", TestContext.CurrentContext.Test.Name);
			using (var libb = CreateDllBuilder (Path.Combine (projectPath, lib.ProjectName), false, false)) {
				Assert.IsTrue (libb.Build (lib), "Build of library should have succeeded");
				using (var b = CreateApkBuilder (Path.Combine (projectPath, proj.ProjectName), false, false)) {
					Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
					//var fi = new FileInfo (Path.Combine (b.ProjectDirectory, proj.IntermediateOutputPath,
					//	"__library_projects__", "Library1", "library_project_imports", ""));
					//fi.Attributes != FileAttributes.ReadOnly;
					var ignoreFiles = new string [] {
						"TemporaryGeneratedFile",
						"CopyComplete"
					};
					Assert.IsTrue (b.Clean (proj), "Clean should have succeeded.");
					var fileCount = Directory.GetFiles (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath), "*", SearchOption.AllDirectories)
						.Where (x => !ignoreFiles.Any (i => !Path.GetFileName (x).Contains (i))).Count ();
					Assert.AreEqual (0, fileCount, "{0} should be Empty", proj.IntermediateOutputPath);
					fileCount = Directory.GetFiles (Path.Combine (Root, b.ProjectDirectory, proj.OutputPath), "*", SearchOption.AllDirectories)
						.Where (x => !ignoreFiles.Any (i => !Path.GetFileName (x).Contains (i))).Count ();
					Assert.AreEqual (0, fileCount, "{0} should be Empty", proj.OutputPath);
				}
			}
		}

		[Test]
		public void BuildIncrementingAssemblyVersion ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			proj.SetProperty ("GenerateAssemblyInfo", "false");
			proj.SetProperty ("Deterministic", "false"); // Required for AssemblyVersion wildcards
			proj.Sources.Add (new BuildItem ("Compile", "AssemblyInfo.cs") {
				TextContent = () => "[assembly: System.Reflection.AssemblyVersion (\"1.0.0.*\")]"
			});

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				var acwmapPath = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "acw-map.txt");
				var assemblyPath = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, "UnnamedProject.dll");
				var firstAssemblyVersion = AssemblyName.GetAssemblyName (assemblyPath).Version;
				var expectedAcwMap = File.ReadAllText (acwmapPath);

				b.Target = "Rebuild";
				b.BuildLogFile = "rebuild.log";
				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "Rebuild should have succeeded.");

				var secondAssemblyVersion = AssemblyName.GetAssemblyName (assemblyPath).Version;
				Assert.AreNotEqual (firstAssemblyVersion, secondAssemblyVersion);
				var actualAcwMap = File.ReadAllText (acwmapPath);
				Assert.AreEqual (expectedAcwMap, actualAcwMap);
			}
		}

		[Test]
		public void BuildIncrementingClassName ()
		{
			int count = 0;
			var source = new BuildItem ("Compile", "World.cs") {
				TextContent = () => {
					int current = ++count;
					return $"namespace Hello{current} {{ public class World{current} : Java.Lang.Object {{ }} }}";
				}
			};
			var proj = new XamarinAndroidApplicationProject ();
			proj.Sources.Add (source);

			using (var b = CreateApkBuilder ("temp/BuildIncrementingClassName")) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				var classesZipPath = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "bin", "classes.zip");
				FileAssert.Exists (classesZipPath);
				var expectedBuilder = new StringBuilder ();
				using (var zip = ZipHelper.OpenZip (classesZipPath)) {
					foreach (var file in zip) {
						expectedBuilder.AppendLine (file.FullName);
					}
				}
				var expectedZip = expectedBuilder.ToString ();

				source.Timestamp = null; //Force the file to re-save w/ new Timestamp
				Assert.IsTrue (b.Build (proj), "Second build should have succeeded.");

				var actualBuilder = new StringBuilder ();
				using (var zip = ZipHelper.OpenZip (classesZipPath)) {
					foreach (var file in zip) {
						actualBuilder.AppendLine (file.FullName);
					}
				}
				var actualZip = actualBuilder.ToString ();
				Assert.AreNotEqual (expectedZip, actualZip);

				//Build with no changes
				Assert.IsTrue (b.Build (proj), "Third build should have succeeded.");
				FileAssert.Exists (classesZipPath);

				//Clean
				Assert.IsTrue (b.Clean (proj), "Clean should have succeeded.");
				FileAssert.DoesNotExist (classesZipPath);
			}
		}

		[Test]
		public void CSharp8Features ([Values (true, false)] bool bindingProject)
		{
			XamarinAndroidProject proj;
			if (bindingProject) {
				proj = new XamarinAndroidBindingProject {
					AndroidClassParser = "class-parse",
					Jars = {
						new AndroidItem.EmbeddedJar ("Jars\\svg-android.jar") {
							WebContentFileNameFromAzure = "javaBindingIssue.jar"
						}
					}
				};
			} else {
				proj = new XamarinAndroidApplicationProject ();
			}

			proj.Sources.Add (new BuildItem.Source ("Foo.cs") {
				TextContent = () => "class A { void B () { using var s = new System.IO.MemoryStream (); } }",
			});
			using (var b = bindingProject ? CreateDllBuilder () : CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		static readonly object [] BuildProguardEnabledProjectSource = new object [] {
			new object [] {
				/* rid */       "",
			},
			new object [] {
				/* rid */       "android-arm64",
			},
		};

		[Test]
		[TestCaseSource (nameof (BuildProguardEnabledProjectSource))]
		public void BuildProguardEnabledProject (string rid)
		{
			var proj = new XamarinFormsAndroidApplicationProject {
				IsRelease = true,
				LinkTool = "r8",
			};
			if (!string.IsNullOrEmpty (rid)) {
				proj.SetProperty ("RuntimeIdentifier", rid);
			}
			using (var b = CreateApkBuilder (Path.Combine ("temp", $"BuildProguard Enabled(1){rid}"))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				// warning XA4304: ProGuard configuration file 'XYZ' was not found.
				StringAssertEx.DoesNotContain ("XA4304", b.LastBuildOutput, "Output should *not* contain XA4304 warnings");

				var intermediate = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				if (!string.IsNullOrEmpty (rid)) {
					intermediate = Path.Combine (intermediate, rid);
				}

				var toolbar_class = "androidx.appcompat.widget.Toolbar";
				var proguardProjectPrimary = Path.Combine (intermediate, "proguard", "proguard_project_primary.cfg");
				FileAssert.Exists (proguardProjectPrimary);
				Assert.IsTrue (StringAssertEx.ContainsText (File.ReadAllLines (proguardProjectPrimary), $"-keep class {proj.JavaPackageName}.MainActivity"), $"`{proj.JavaPackageName}.MainActivity` should exist in `proguard_project_primary.cfg`!");

				var aapt_rules = Path.Combine (intermediate, "aapt_rules.txt");
				FileAssert.Exists (aapt_rules);
				var lines = File.ReadAllLines (aapt_rules);
				Assert.IsTrue (StringAssertEx.ContainsText (lines, $"-keep class {toolbar_class}"), $"`{toolbar_class}` should exist in `{aapt_rules}`!");
				var activity_class = $"{proj.PackageName}.MainActivity";
				Assert.IsTrue (StringAssertEx.ContainsText (lines, $"-keep class {activity_class}"), $"`{activity_class}` should exist in `{aapt_rules}`!");

				var dexFile = Path.Combine (intermediate, "android", "bin", "classes.dex");
				FileAssert.Exists (dexFile);
				var classes = new [] {
					"Lmono/MonoRuntimeProvider;",
					"Lmono/android/view/View_OnClickListenerImplementor;",
					"Landroid/runtime/JavaProxyThrowable;",
					$"L{toolbar_class.Replace ('.', '/')};"
				};
				foreach (var className in classes) {
					Assert.IsTrue (DexUtils.ContainsClassWithMethod (className, "<init>", "()V", dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
				}
			}
		}

		XamarinAndroidApplicationProject CreateMultiDexRequiredApplication (string debugConfigurationName = "Debug", string releaseConfigurationName = "Release")
		{
			var proj = new XamarinAndroidApplicationProject (debugConfigurationName, releaseConfigurationName);
			proj.AndroidJavaSources.Add (new BuildItem (AndroidBuildActions.AndroidJavaSource, "ManyMethods.java") {
				TextContent = () => "public class ManyMethods { \n"
					+ string.Join (Environment.NewLine, Enumerable.Range (0, 32768).Select (i => "public void method" + i + "() {}"))
					+ "}",
				Encoding = Encoding.ASCII,
				Metadata = { { "Bind", "False "}},
			});
			proj.AndroidJavaSources.Add (new BuildItem (AndroidBuildActions.AndroidJavaSource, "ManyMethods2.java") {
				TextContent = () => "public class ManyMethods2 { \n"
					+ string.Join (Environment.NewLine, Enumerable.Range (0, 32768).Select (i => "public void method" + i + "() {}"))
					+ "}",
				Encoding = Encoding.ASCII,
				Metadata = { { "Bind", "False "}},
			});
			return proj;
		}

		[Test]
		public void CreateMultiDexWithSpacesInConfig ()
		{
			var proj = CreateMultiDexRequiredApplication (releaseConfigurationName: "Test Config");
			proj.IsRelease = true;
			proj.SetProperty ("AndroidEnableMultiDex", "True");
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void BuildMultiDexApplication ()
		{
			var proj = CreateMultiDexRequiredApplication ();
			proj.SetProperty ("AndroidEnableMultiDex", "True");
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName), false, false)) {
				string intermediateDir = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				Assert.IsTrue (File.Exists (Path.Combine (Root, b.ProjectDirectory, intermediateDir, "android/bin/classes.dex")),
					"multidex-ed classes.zip exists");
				Assert.IsFalse (b.LastBuildOutput.ContainsText ("Duplicate zip entry"), "Should not get warning about [META-INF/MANIFEST.MF]");
			}
		}

		[Test]
		public void BuildAfterMultiDexIsNotRequired ()
		{
			var proj = CreateMultiDexRequiredApplication ();
			proj.SetProperty ("AndroidEnableMultiDex", "True");

			using (var b = CreateApkBuilder ()) {
				string intermediateDir = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				string androidBinDir = Path.Combine (intermediateDir, "android", "bin");
				string apkPath = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.PackageName}-Signed.apk");

				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				FileAssert.Exists (Path.Combine (androidBinDir, "classes.dex"));
				FileAssert.Exists (Path.Combine (androidBinDir, "classes2.dex"));

				using (var zip = ZipHelper.OpenZip (apkPath)) {
					var entries = zip.Select (e => e.FullName).ToList ();
					Assert.IsTrue (entries.Contains ("classes.dex"), "APK must contain `classes.dex`.");
					Assert.IsTrue (entries.Contains ("classes2.dex"), "APK must contain `classes2.dex`.");
				}

				//Now build project again after it no longer requires multidex, remove the *HUGE* AndroidJavaSource build items
				while (proj.AndroidJavaSources.Count > 1)
					proj.AndroidJavaSources.RemoveAt (proj.AndroidJavaSources.Count - 1);
				proj.SetProperty ("AndroidEnableMultiDex", "False");

				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "Build should have succeeded.");
				FileAssert.Exists (Path.Combine (androidBinDir, "classes.dex"));
				FileAssert.DoesNotExist (Path.Combine (androidBinDir, "classes2.dex"));
				FileAssert.DoesNotExist (Path.Combine (androidBinDir, "classes3.dex"));

				using (var zip = ZipHelper.OpenZip (apkPath)) {
					var entries = zip.Select (e => e.FullName).ToList ();
					Assert.IsTrue (entries.Contains ("classes.dex"), "APK must contain `classes.dex`.");
					Assert.IsFalse (entries.Contains ("classes2.dex"), "APK must *not* contain `classes2.dex`.");
				}
			}
		}

		[Test]
		public void CustomApplicationClassAndMultiDex ([Values (true, false)] bool isRelease)
		{
			var proj = CreateMultiDexRequiredApplication ();
			proj.IsRelease = isRelease;
			proj.TrimModeRelease = TrimMode.Full;
			proj.SetProperty ("AndroidEnableMultiDex", "True");
			proj.Sources.Add (new BuildItem ("Compile", "CustomApp.cs") { TextContent = () => @"
using System;
using Android.App;
using Android.Runtime;
namespace UnnamedProject {
    [Application(Name = ""com.foxsports.test.CustomApp"")]
    public class CustomApp : Application
    {
        public CustomApp(IntPtr handle, JniHandleOwnership ownerShip) :
			base(handle, ownerShip)
		{


        }

        public override void OnCreate()
        {
            base.OnCreate();
        }
    }
}" });
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				Assert.IsFalse (b.LastBuildOutput.ContainsText ("Duplicate zip entry"), "Should not get warning about [META-INF/MANIFEST.MF]");
				var customAppContent = File.ReadAllText (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "src", "com", "foxsports", "test", "CustomApp.java"));
				Assert.IsTrue (customAppContent.Contains ("extends android.support.multidex.MultiDexApplication"),
					"Custom App class should have inherited from android.support.multidex.MultiDexApplication.");
			}
		}

		[Test]
		public void MultiDexAndCodeShrinker ()
		{
			var proj = CreateMultiDexRequiredApplication ();
			proj.SetProperty ("AndroidEnableMultiDex", "True");
			proj.IsRelease = true;
			proj.LinkTool = "r8";
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				var className = "Landroid/support/multidex/MultiDexApplication;";
				var dexFile = b.Output.GetIntermediaryPath (Path.Combine ("android", "bin", "classes.dex"));
				FileAssert.Exists (dexFile);
				Assert.IsTrue (DexUtils.ContainsClassWithMethod (className, "<init>", "()V", dexFile, AndroidSdkPath), $"`{dexFile}` should include `{className}`!");
			}
		}

		[Test]
		public void MultiDexR8ConfigWithNoCodeShrinking ()
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true
			};
			proj.SetProperty ("AndroidEnableMultiDex", "True");
			/* The source for the library is a single class:
			*
			abstract class ExtendsClassValue extends ClassValue<Boolean> {}
			*
			* The reason `ClassValue` is used for this test is precisely that it
			* does not exist in `android.jar`.  This means the library cannot be
			* compiled using `@(AndroidJavaSource)`.  It was instead compiled
			* using `javac ExtendsClassValue.java` and then manually archived
			* using `jar cvf ExtendsClassValue.jar
			* ExtendsClassValue.class`.
			*/
			proj.OtherBuildItems.Add (new BuildItem ("AndroidJavaLibrary", "ExtendsClassValue.jar") { BinaryContent = () => Convert.FromBase64String (@"
UEsDBBQACAgIAChzjVAAAAAAAAAAAAAAAAAJAAQATUVUQS1JTkYv/soAAAMAUEsHCAAAAAACAAAAA
AAAAFBLAwQUAAgICAAoc41QAAAAAAAAAAAAAAAAFAAAAE1FVEEtSU5GL01BTklGRVNULk1G803My
0xLLS7RDUstKs7Mz7NSMNQz4OVyLkpNLElN0XWqBAlY6BnoGpkqaPhmJhflF+enlWjycvFyAQBQS
wcIv1FGtTsAAAA7AAAAUEsDBBQACAgIABxzjVAAAAAAAAAAAAAAAAAXAAAARXh0ZW5kc0NsYXNzV
mFsdWUuY2xhc3NtT7sKwkAQnNWYaHwExVaw9AHa2CkWilZi46M/9ZCT8wJ5iL9lJVj4AX6UuEmjh
Qu7M8wwu+zr/XgCGMBzkUXJQdlBhWCPlFHRmJBttbcEa+ofJMFbKCOX8Xkng7XYaVYKK3U0IooD5
t3FSVxEXwtz7E+1CMOt0LEc/agT39dSmOF4SHBXfhzs5Vwla2qbUH4jvSRRgoUcoTq7RtIcwq9Lq
P+7YzWR4Q+SIm4OM9rMGoyJkuvcQbfUdnjaqUgcyjNmUICbYvEDUEsHCB4E1g/HAAAAEgEAAFBLA
QIUABQACAgIAChzjVAAAAAAAgAAAAAAAAAJAAQAAAAAAAAAAAAAAAAAAABNRVRBLUlORi/+ygAAU
EsBAhQAFAAICAgAKHONUL9RRrU7AAAAOwAAABQAAAAAAAAAAAAAAAAAPQAAAE1FVEEtSU5GL01BT
klGRVNULk1GUEsBAhQAFAAICAgAHHONUB4E1g/HAAAAEgEAABcAAAAAAAAAAAAAAAAAugAAAEV4d
GVuZHNDbGFzc1ZhbHVlLmNsYXNzUEsFBgAAAAADAAMAwgAAAMYBAAAAAA==
				") });
			proj.OtherBuildItems.Add (new BuildItem ("ProguardConfiguration", "proguard.cfg") {
				TextContent = () => "-dontwarn java.lang.ClassValue"
			});
			using (var builder = CreateApkBuilder ()) {
				Assert.True (builder.Build (proj), "Build should have succeeded.");
				string warning = builder.LastBuildOutput
						.SkipWhile (x => !x.StartsWith ("Build succeeded.", StringComparison.Ordinal))
						.FirstOrDefault (x => x.Contains ("R8 : warning : Missing class: java.lang.ClassValue"));
				Assert.IsNull (warning, "Build should have completed without an R8 warning for `java.lang.ClassValue`.");
			}
		}


		[Test]
		public void BuildBasicApplicationCheckPdb ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				foreach (string abi in proj.GetRuntimeIdentifiersAsAbis ()) {
					Assert.IsTrue (File.Exists (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, $"android/assets/{abi}/UnnamedProject.pdb")),
					               $"UnnamedProject.pdb must be copied to the Intermediate directory for ABI {abi}");
				}
			}
		}

		[Test]
		public void BuildBasicApplicationCheckPdbRepeatBuild ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				foreach (string abi in proj.GetRuntimeIdentifiersAsAbis ()) {
					Assert.IsTrue (File.Exists (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, $"android/assets/{abi}/UnnamedProject.pdb")),
					               $"UnnamedProject.pdb must be copied to the Intermediate directory for ABI {abi}");
				}

				Assert.IsTrue (b.Build (proj), "second build failed");

				foreach (string abi in proj.GetRuntimeIdentifiersAsAbis ()) {
					Assert.IsTrue (File.Exists (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, $"android/assets/{abi}/UnnamedProject.pdb")),
					               $"UnnamedProject.pdb must be copied to the Intermediate directory for ABI {abi}");
				}
			}
		}

		[Test]
		public void BuildAppCheckDebugSymbols ()
		{
			var path = Path.Combine ("temp", TestContext.CurrentContext.Test.Name);
			var lib = new XamarinAndroidLibraryProject () {
				IsRelease = false,
				ProjectName = "Library1",
				Sources = {
					new BuildItem.Source ("Class1.cs") {
						TextContent = () => @"using System;
namespace Library1 {
	public class Class1 : Java.Lang.Object, global::Android.Views.View.IOnClickListener {
		void global::Android.Views.View.IOnClickListener.OnClick(global::Android.Views.View v)
		{
		}
	}
}
",
					},
				},
			};
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = false,
				ProjectName = "App1",
				References = { new BuildItem ("ProjectReference", "..\\Library1\\Library1.csproj") },
				Sources = {
					new BuildItem.Source ("Class2.cs") {
						TextContent= () => @"
using System;
namespace App1
{
	public class Class2
	{
		Library1.Class1 c;
		public Class2 ()
		{
		}
	}
}"
					},
				},
			};
			proj.SetProperty (KnownProperties.AndroidLinkMode, AndroidLinkMode.None.ToString ());
			using (var libb = CreateDllBuilder (Path.Combine (path, "Library1"))) {
				Assert.IsTrue (libb.Build (lib), "Library1 Build should have succeeded.");
				using (var b = CreateApkBuilder (Path.Combine (path, "App1"))) {
					Assert.IsTrue (b.Build (proj), "App1 Build should have succeeded.");
					var intermediate = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
					var outputPath = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath);

					const string LibraryBaseName = "Library1";
					const string AppBaseName = "App1";
					const string LibraryPdbName = LibraryBaseName + ".pdb";
					const string LibraryDllName = LibraryBaseName + ".dll";
					const string AppPdbName = AppBaseName + ".pdb";
					const string AppDllName = AppBaseName + ".dll";

					string libraryPdbBinSrc = Path.Combine (outputPath, LibraryPdbName);
					string appPdbBinSrc = Path.Combine (outputPath, AppPdbName);

					Assert.IsTrue (
						File.Exists (libraryPdbBinSrc),
						$"{LibraryPdbName} must be copied to bin directory");

					Assert.IsTrue (
						File.Exists (appPdbBinSrc),
						$"{AppPdbName} must be copied to bin directory");

					var fileNames = new List<(string path, bool existsInBin)> {
						("Mono.Android.pdb", false),
						(AppPdbName,         true),
						(LibraryPdbName,     true),
						(AppDllName,         true),
						(LibraryDllName,     true),
					};

					string apkPath = Path.Combine (outputPath, proj.PackageName + "-Signed.apk");
					var helper = new ArchiveAssemblyHelper (apkPath, useAssemblyStores: false, proj.GetRuntimeIdentifiers ().ToArray ());
					foreach (string abi in proj.GetRuntimeIdentifiersAsAbis ()) {
						foreach ((string fileName, bool existsInBin) in fileNames) {
							EnsureFilesAreTheSame (intermediate, existsInBin ? outputPath : null, fileName, abi, helper, uncompressIfNecessary: fileName.EndsWith (".dll", StringComparison.Ordinal));
						}
					}
				}
			}

			void EnsureFilesAreTheSame (string intermediatePath, string? binPath, string fileName, string abi, ArchiveAssemblyHelper helper, bool uncompressIfNecessary)
			{
				string assetsFilePath = Path.Combine (intermediatePath, "android", "assets", abi, fileName);
				Assert.IsTrue (File.Exists (assetsFilePath), $"'{fileName}' must be copied to Intermediate directory for ABI {abi}");

				using var assetsFileStream = File.OpenRead (assetsFilePath);
				string apkEntryPath = MonoAndroidHelper.MakeZipArchivePath ("assemblies", abi, fileName);
				using Stream? apkEntryStream = helper.ReadEntry (apkEntryPath, MonoAndroidHelper.AbiToTargetArch (abi), uncompressIfNecessary);

				if (apkEntryStream != null) { // FastDev won't put assemblies in the APK
					FileAssert.AreEqual (apkEntryStream, assetsFileStream, $"'{apkEntryPath}' and '{assetsFilePath}' should not differ");
				}

				if (String.IsNullOrEmpty (binPath)) {
					return;
				}

				// This is a bit fragile. We don't know which RID the `bin/` files were copied from, so we'll do our best to compare
				// oranges to oranges by looking at file sizes before attempting the compare.  This is a very weak predicate, because
				// the files may differ in e.g. the MVID and still have the same size.  The real fix for this is to have per-rid `bin/`
				// subdirectories.
				string binFilePath = Path.Combine (binPath, fileName);
				Assert.IsTrue (File.Exists (binFilePath), $"'{fileName}' must be copied to the Output directory");

				var assetsInfo = new FileInfo (assetsFilePath);
				var binInfo = new FileInfo (binFilePath);

				if (assetsInfo.Length != binInfo.Length) {
					Assert.Warn ($"Ignoring comparison of '{binFilePath}' with '{assetsFilePath}' because their sizes differ");
					return;
				}

				using var binFileStream = File.OpenRead (binFilePath);
				assetsFileStream.Seek (0, SeekOrigin.Begin);
				FileAssert.AreEqual (assetsFileStream, binFileStream, $"'{assetsFilePath}' and '{binFilePath}' should not differ");

				if (apkEntryStream != null) {
					binFileStream.Seek (0, SeekOrigin.Begin);
					apkEntryStream.Seek (0, SeekOrigin.Begin);
					FileAssert.AreEqual (apkEntryStream, binFileStream,  $"'{apkEntryPath}' and '{binFilePath}' should not differ");
				}
			}
		}

		[Test]
		public void BuildBasicApplicationCheckConfigFiles ()
		{
			var proj = new XamarinAndroidApplicationProject ();
			using (var b = CreateApkBuilder ()) {
				var config = new BuildItem.NoActionResource ("UnnamedProject.dll.config") {
					TextContent = () => {
						return "<?xml version='1.0' ?><configuration/>";
					},
					Metadata = {
						{ "CopyToOutputDirectory", "PreserveNewest"},
					}
				};
				proj.OtherBuildItems.Add (config);
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				StringAssertEx.Contains ("XA1024", b.LastBuildOutput, "Output should contain XA1024 warnings");
			}
		}

		[Test]
		[NonParallelizable] // Environment variables are global!
		public void BuildWithJavaToolOptions ()
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true
			};
			var oldEnvVar = Environment.GetEnvironmentVariable ("JAVA_TOOL_OPTIONS");
			try {
				Environment.SetEnvironmentVariable ("JAVA_TOOL_OPTIONS",
						"-Dcom.sun.jndi.ldap.object.trustURLCodebase=false -Dcom.sun.jndi.rmi.object.trustURLCodebase=false -Dcom.sun.jndi.cosnaming.object.trustURLCodebase=false -Dlog4j2.formatMsgNoLookups=true");
				using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
					b.Target = "Build";
					Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
					b.AssertHasNoWarnings ();
				}
			} finally {
				Environment.SetEnvironmentVariable ("JAVA_TOOL_OPTIONS", oldEnvVar);
			}
		}
	}
}
