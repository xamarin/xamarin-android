using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Xamarin.Android.Tools;
using Xamarin.ProjectTools;
using Xamarin.Tools.Zip;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Build.Tests
{
	/// <summary>
	/// Fixture containing tests which use custom dotnet commands (e.g. `dotnet new`, `dotnet pack`),
	///  and in most cases don't use the common ProjectBuilder infrastructure.
	/// </summary>
	[TestFixture]
	[NonParallelizable] // On MacOS, parallel /restore causes issues
	public class XASdkTests : BaseTest
	{
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
			Assert.IsTrue (dotnet.Build (parameters: new [] { "Configuration=Debug", "TrimmerSingleWarn=false" }), "`dotnet build` should succeed");
			dotnet.AssertHasNoWarnings ();

			// Release build
			Assert.IsTrue (dotnet.Build (parameters: new [] { "Configuration=Release", "TrimmerSingleWarn=false" }), "`dotnet build` should succeed");
			dotnet.AssertHasNoWarnings ();
		}

		static readonly object[] DotNetPackTargetFrameworks = new object[] {
			new object[] {
				"net8.0",
				"android",
				34,
			},
			new object[] {
				"net8.0",
				"android34",
				34,
			},
			new object[] {
				"net9.0",
				"android",
				XABuildConfig.AndroidDefaultTargetDotnetApiLevel,
			},
			new object[] {
				"net9.0",
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
			nupkg.AssertDoesNotContainEntry (nupkgPath, $"lib/{dotnetVersion}-android{apiLevel}.0/_Microsoft.Android.Resource.Designer.dll");
		}

		static readonly object[] DotNetTargetFrameworks = new object[] {
			new object[] {
				"net8.0",
				"android",
				34,
			},
			new object[] {
				"net9.0",
				"android",
				XABuildConfig.AndroidDefaultTargetDotnetApiLevel,
			},

			new object[] {
				"net9.0",
				$"android{XABuildConfig.AndroidDefaultTargetDotnetApiLevel}",
				XABuildConfig.AndroidDefaultTargetDotnetApiLevel,
			},

			new object[] {
				"net9.0",
				XABuildConfig.AndroidLatestStableApiLevel == XABuildConfig.AndroidDefaultTargetDotnetApiLevel ? null : $"android{XABuildConfig.AndroidLatestStableApiLevel}.0",
				XABuildConfig.AndroidLatestStableApiLevel,
			},
			new object[] {
				"net9.0",
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

			//FIXME: will revisit this in a future PR
			if (dotnetVersion == "net8.0") {
				Assert.Ignore ("error NETSDK1185: The Runtime Pack for FrameworkReference 'Microsoft.Android.Runtime.34.android-arm' was not available. This may be because DisableTransitiveFrameworkReferenceDownloads was set to true.");
			}

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
			dotnet.Verbosity = "detailed";
			string[] configParam = isRelease ? new [] { "Configuration=Release" } : new [] { "Configuration=Debug" };
			Assert.IsTrue (dotnet.Publish (parameters: configParam), "first `dotnet publish` should succeed");

			// NOTE: Preview API levels emit XA4211
			if (!preview) {
				dotnet.AssertHasNoWarnings ();
			}

			// Only check latest TFM, as previous will come from NuGet
			if (dotnetVersion == "net9.0") {
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
		public void XamarinLegacySdk ([Values ("net8.0-android34.0", "net9.0-android35.0")] string dotnetTargetFramework)
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
	}
}
