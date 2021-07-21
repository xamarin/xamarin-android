using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Mono.Cecil;
using NUnit.Framework;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	[Category ("Node-2")]
	[Parallelizable (ParallelScope.Children)]
	public class EnvironmentContentTests : BaseTest
	{
		[Test]
		[NonParallelizable]
		[Category ("SmokeTests")]
		public void BuildApplicationWithMonoEnvironment ([Values ("", "Normal", "Offline")] string sequencePointsMode)
		{
			const string supportedAbis = "armeabi-v7a;x86";

			var lib = new XamarinAndroidLibraryProject {
				ProjectName = "Library1",
				IsRelease = true,
				OtherBuildItems = { new AndroidItem.AndroidEnvironment ("Mono.env") {
						TextContent = () => "MONO_DEBUG=soft-breakpoints"
					},
				},
			};
			var app = new XamarinFormsAndroidApplicationProject () {
				IsRelease = true,
				AndroidLinkModeRelease = AndroidLinkMode.Full,
				References = {
					new BuildItem ("ProjectReference","..\\Library1\\Library1.csproj"),
				},
			};
			//LinkSkip one assembly that contains __AndroidLibraryProjects__.zip
			string linkSkip = "FormsViewGroup";
			app.SetProperty ("AndroidLinkSkip", linkSkip);
			app.SetProperty ("_AndroidSequencePointsMode", sequencePointsMode);
			app.SetAndroidSupportedAbis (supportedAbis);
			using (var libb = CreateDllBuilder (Path.Combine ("temp", TestName, lib.ProjectName)))
			using (var appb = CreateApkBuilder (Path.Combine ("temp", TestName, app.ProjectName))) {
				Assert.IsTrue (libb.Build (lib), "Library build should have succeeded.");
				Assert.IsTrue (appb.Build (app), "App should have succeeded.");
				if (!Builder.UseDotNet) {
					//TODO: $(AndroidLinkSkip) is not yet implemented
					Assert.IsTrue (StringAssertEx.ContainsText (appb.LastBuildOutput, $"Save assembly: {linkSkip}"), $"{linkSkip} should be saved, and not linked!");
				}

				string intermediateOutputDir = Path.Combine (Root, appb.ProjectDirectory, app.IntermediateOutputPath);
				List<string> envFiles = EnvironmentHelper.GatherEnvironmentFiles (intermediateOutputDir, supportedAbis, true);
				Dictionary<string, string> envvars = EnvironmentHelper.ReadEnvironmentVariables (envFiles);
				Assert.IsTrue (envvars.Count > 0, $"No environment variables defined");

				string monoDebugVar;
				Assert.IsTrue (envvars.TryGetValue ("MONO_DEBUG", out monoDebugVar), "Environment should contain MONO_DEBUG");
				Assert.IsFalse (String.IsNullOrEmpty (monoDebugVar), "Environment must contain MONO_DEBUG with a value");
				Assert.IsTrue (monoDebugVar.IndexOf ("soft-breakpoints") >= 0, "Environment must contain MONO_DEBUG with 'soft-breakpoints' in its value");

				if (!String.IsNullOrEmpty (sequencePointsMode))
					Assert.IsTrue (monoDebugVar.IndexOf ("gen-compact-seq-points") >= 0, "The values from Mono.env should have been merged into environment");

				EnvironmentHelper.AssertValidEnvironmentSharedLibrary (intermediateOutputDir, AndroidSdkPath, AndroidNdkPath, supportedAbis);

				var assemblyDir = Path.Combine (Root, appb.ProjectDirectory, app.IntermediateOutputPath, "android", "assets");
				var rp = new ReaderParameters { ReadSymbols = false };
				foreach (var assemblyFile in Directory.EnumerateFiles (assemblyDir, "*.dll")) {
					using (var assembly = AssemblyDefinition.ReadAssembly (assemblyFile)) {
						foreach (var module in assembly.Modules) {
							var resources = module.Resources.Select (r => r.Name).ToArray ();
							Assert.IsFalse (StringAssertEx.ContainsText (resources, "__AndroidEnvironment__"), "AndroidEnvironment EmbeddedResource should be stripped!");
							Assert.IsFalse (StringAssertEx.ContainsText (resources, "__AndroidLibraryProjects__.zip"), "__AndroidLibraryProjects__.zip should be stripped!");
							Assert.IsFalse (StringAssertEx.ContainsText (resources, "__AndroidNativeLibraries__.zip"), "__AndroidNativeLibraries__.zip should be stripped!");
						}
					}
				}
			}
		}

		[Test]
		public void CheckMonoDebugIsAddedToEnvironment ([Values ("", "Normal", "Offline")] string sequencePointsMode)
		{
			const string supportedAbis = "armeabi-v7a;x86";

			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.SetProperty ("_AndroidSequencePointsMode", sequencePointsMode);
			proj.SetAndroidSupportedAbis (supportedAbis);
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				string intermediateOutputDir = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				List<string> envFiles = EnvironmentHelper.GatherEnvironmentFiles (intermediateOutputDir, supportedAbis, true);
				Dictionary<string, string> envvars = EnvironmentHelper.ReadEnvironmentVariables (envFiles);
				Assert.IsTrue (envvars.Count > 0, $"No environment variables defined");

				string monoDebugVar;
				bool monoDebugVarFound = envvars.TryGetValue ("MONO_DEBUG", out monoDebugVar);
				if (String.IsNullOrEmpty (sequencePointsMode))
					Assert.IsFalse (monoDebugVarFound, $"environment should not contain MONO_DEBUG={monoDebugVar}");
				else {
					Assert.IsTrue (monoDebugVarFound, "environment should contain MONO_DEBUG");
					Assert.AreEqual ("gen-compact-seq-points", monoDebugVar, "environment should contain MONO_DEBUG=gen-compact-seq-points");
				}

				EnvironmentHelper.AssertValidEnvironmentSharedLibrary (intermediateOutputDir, AndroidSdkPath, AndroidNdkPath, supportedAbis);
			}
		}

		[Test]
		public void CheckConcurrentGC ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			var gcVarName = "MONO_GC_PARAMS";
			var expectedDefaultValue = "major=marksweep";
			var expectedUpdatedValue = "major=marksweep-conc";
			var supportedAbis = "armeabi-v7a;arm64-v8a";
			proj.SetAndroidSupportedAbis (supportedAbis);

			using (var b = CreateApkBuilder ()) {
				proj.SetProperty ("AndroidEnableSGenConcurrent", "False");
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var intermediateOutputDir = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				// AndroidEnableSGenConcurrent=False by default
				List<string> envFiles = EnvironmentHelper.GatherEnvironmentFiles (intermediateOutputDir, supportedAbis, true);
				Dictionary<string, string> envvars = EnvironmentHelper.ReadEnvironmentVariables (envFiles);
				Assert.IsTrue (envvars.ContainsKey (gcVarName), $"Environment should contain '{gcVarName}'.");
				Assert.AreEqual (expectedDefaultValue, envvars[gcVarName], $"'{gcVarName}' should have been '{expectedDefaultValue}' when concurrent GC is disabled.");

				proj.SetProperty ("AndroidEnableSGenConcurrent", "True");
				Assert.IsTrue (b.Build (proj), "Second build should have succeeded.");
				envFiles = EnvironmentHelper.GatherEnvironmentFiles (intermediateOutputDir, supportedAbis, true);
				envvars = EnvironmentHelper.ReadEnvironmentVariables (envFiles);
				Assert.IsTrue (envvars.ContainsKey (gcVarName), $"Environment should contain '{gcVarName}'.");
				Assert.AreEqual (expectedUpdatedValue, envvars[gcVarName], $"'{gcVarName}' should have been '{expectedUpdatedValue}' when concurrent GC is enabled.");
			}
		}

		[Test]
		[Category ("MonoSymbolicate")]
		public void CheckBuildIdIsUnique ([Values ("apk", "aab")] string packageFormat)
		{
			const string supportedAbis = "armeabi-v7a;x86";

			Dictionary<string, string> buildIds = new Dictionary<string, string> ();
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.SetProperty (proj.ReleaseProperties, "MonoSymbolArchive", "True");
			proj.SetProperty (proj.ReleaseProperties, "DebugSymbols", "true");
			proj.SetProperty (proj.ReleaseProperties, "DebugType", "Portable");
			proj.SetProperty (proj.ReleaseProperties, KnownProperties.AndroidCreatePackagePerAbi, "true");
			proj.SetProperty (proj.ReleaseProperties, "AndroidPackageFormat", packageFormat);
			proj.SetAndroidSupportedAbis (supportedAbis);
			using (var b = CreateApkBuilder ()) {
				b.ThrowOnBuildFailure = false;
				Assert.IsTrue (b.Build (proj), "first build failed");
				var outputPath = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath);
				var archivePath = Path.Combine (outputPath, $"{proj.PackageName}.{packageFormat}.mSYM");
				var allFilesInArchive = Directory.GetFiles (archivePath, "*", SearchOption.AllDirectories);
				string extension = "dll";
				Assert.IsTrue (allFilesInArchive.Any (x => Path.GetFileName (x) == $"{proj.ProjectName}.{extension}"), $"{proj.ProjectName}.{extension} should exist in {archivePath}");
				extension = "pdb";
				Assert.IsTrue (allFilesInArchive.Any (x => Path.GetFileName (x) == $"{proj.ProjectName}.{extension}"), $"{proj.ProjectName}.{extension} should exist in {archivePath}");

				string intermediateOutputDir = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				List<string> envFiles = EnvironmentHelper.GatherEnvironmentFiles (intermediateOutputDir, supportedAbis, true);
				Dictionary<string, string> envvars = EnvironmentHelper.ReadEnvironmentVariables (envFiles);
				Assert.IsTrue (envvars.Count > 0, $"No environment variables defined");

				string buildID;
				Assert.IsTrue (envvars.TryGetValue ("XAMARIN_BUILD_ID", out buildID), "The environment should contain a XAMARIN_BUILD_ID");
				buildIds.Add ("all", buildID);

				var msymDirectory = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.PackageName}.{packageFormat}.mSYM");
				Assert.IsTrue (File.Exists (Path.Combine (msymDirectory, "manifest.xml")), "manifest.xml should exist in", msymDirectory);
				var doc = XDocument.Load (Path.Combine (msymDirectory, "manifest.xml"));

				Assert.IsTrue (doc.Element ("mono-debug")
					.Elements ()
					.Any (x => x.Name == "app-id" && x.Value == proj.PackageName), "app-id is has an incorrect value.");
				var buildId = buildIds.First ().Value;
				Assert.IsTrue (doc.Element ("mono-debug")
					.Elements ()
					.Any (x => x.Name == "build-id" && x.Value == buildId), "build-id is has an incorrect value.");

				EnvironmentHelper.AssertValidEnvironmentSharedLibrary (intermediateOutputDir, AndroidSdkPath, AndroidNdkPath, supportedAbis);
			}
		}

		[Test]
		public void CheckHttpClientHandlerType ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			var httpClientHandlerVarName = "XA_HTTP_CLIENT_HANDLER_TYPE";
			var expectedDefaultValue = "System.Net.Http.HttpClientHandler, System.Net.Http";
			var expectedUpdatedValue = "Xamarin.Android.Net.AndroidClientHandler";
			var supportedAbis = "armeabi-v7a;arm64-v8a";
			proj.SetAndroidSupportedAbis (supportedAbis);

			using (var b = CreateApkBuilder ()) {
				proj.SetProperty ("AndroidHttpClientHandlerType", expectedDefaultValue);
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var intermediateOutputDir = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				List<string> envFiles = EnvironmentHelper.GatherEnvironmentFiles (intermediateOutputDir, supportedAbis, true);
				Dictionary<string, string> envvars = EnvironmentHelper.ReadEnvironmentVariables (envFiles);
				Assert.IsTrue (envvars.ContainsKey (httpClientHandlerVarName), $"Environment should contain '{httpClientHandlerVarName}'.");
				Assert.AreEqual (expectedDefaultValue, envvars[httpClientHandlerVarName]);

				proj.SetProperty ("AndroidHttpClientHandlerType", expectedUpdatedValue);
				Assert.IsTrue (b.Build (proj), "Second build should have succeeded.");
				envFiles = EnvironmentHelper.GatherEnvironmentFiles (intermediateOutputDir, supportedAbis, true);
				envvars = EnvironmentHelper.ReadEnvironmentVariables (envFiles);
				Assert.IsTrue (envvars.ContainsKey (httpClientHandlerVarName), $"Environment should contain '{httpClientHandlerVarName}'.");
				Assert.AreEqual (expectedUpdatedValue, envvars[httpClientHandlerVarName]);
			}
		}

		static object [] TlsProviderTestCases =
		{
			// androidTlsProvider, isRelease, extpected
			new object[] { "", true, true, },
			new object[] { "default", true, true, },
			new object[] { "legacy", true, true, },
			new object[] { "btls", true, true, }
		};

		[Test]
		[Category ("DotNetIgnore")] // .NET 5+ does not use these native libraries
		[TestCaseSource (nameof (TlsProviderTestCases))]
		public void BuildWithTlsProvider (string androidTlsProvider, bool isRelease, bool expected)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = isRelease,
			};
			var supportedAbis = new string [] { "armeabi-v7a", "arm64-v8a" };
			proj.SetAndroidSupportedAbis (supportedAbis);

			using (var b = CreateApkBuilder (Path.Combine ("temp", $"BuildWithTlsProvider_{androidTlsProvider}_{isRelease}_{expected}"))) {
				proj.SetProperty ("AndroidTlsProvider", androidTlsProvider);
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var intermediateOutputDir = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
				var outpath = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath);
				var apk = Path.Combine (outpath, $"{proj.PackageName}-Signed.apk");
				using (var zipFile = ZipHelper.OpenZip (apk)) {
					foreach (var abi in supportedAbis) {
						if (expected) {
							Assert.IsNotNull (ZipHelper.ReadFileFromZip (zipFile,
								$"lib/{abi}/libmono-btls-shared.so"),
								$"lib/{abi}/libmono-btls-shared.so should exist in the apk.");
						}
						else {
							Assert.IsNull (ZipHelper.ReadFileFromZip (zipFile,
								$"lib/{abi}/libmono-btls-shared.so"),
								$"lib/{abi}/libmono-btls-shared.so should not exist in the apk.");
						}
					}
				}
				List<string> envFiles = EnvironmentHelper.GatherEnvironmentFiles (intermediateOutputDir, string.Join (";", supportedAbis), true);
				Dictionary<string, string> envvars = EnvironmentHelper.ReadEnvironmentVariables (envFiles);
				Assert.IsTrue (envvars.ContainsKey ("XA_TLS_PROVIDER"), "Environment should contain XA_TLS_PROVIDER.");
				if (androidTlsProvider == string.Empty) {
					Assert.AreEqual ("btls", envvars["XA_TLS_PROVIDER"], "'XA_TLS_PROVIDER' should have been 'btls' when provider is not set.");
				} else {
					Assert.AreEqual (androidTlsProvider, envvars["XA_TLS_PROVIDER"], $"'XA_TLS_PROVIDER' should have been '{androidTlsProvider}'.");
				}
			}
		}

	}
}
