using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Xamarin.Android.Prepare
{
	partial class AndroidToolchain : AppObject
	{
		public static readonly Uri AndroidUri = Configurables.Urls.AndroidToolchain_AndroidUri;

		public List<AndroidToolchainComponent> Components { get; }

		public AndroidToolchain ()
		{
			string AndroidNdkVersion       = BuildAndroidPlatforms.AndroidNdkVersion;
			string AndroidPkgRevision      = BuildAndroidPlatforms.AndroidNdkPkgRevision;
			string AndroidNdkDirectory     = GetRequiredProperty (KnownProperties.AndroidNdkDirectory);
			string AndroidCmakeUrlPrefix   = Context.Instance.Properties.GetValue (KnownProperties.AndroidCmakeUrlPrefix) ?? String.Empty;
			string AndroidCmakeVersion     = GetRequiredProperty (KnownProperties.AndroidCmakeVersion);
			string AndroidCmakeVersionPath = GetRequiredProperty (KnownProperties.AndroidCmakeVersionPath);
			string CommandLineToolsVersion = GetRequiredProperty (KnownProperties.CommandLineToolsVersion);
			string CommandLineToolsFolder  = GetRequiredProperty (KnownProperties.CommandLineToolsFolder);
			string EmulatorVersion         = GetRequiredProperty (KnownProperties.EmulatorVersion);
			string EmulatorPkgRevision     = GetRequiredProperty (KnownProperties.EmulatorPkgRevision);
			string XABuildToolsFolder      = GetRequiredProperty (KnownProperties.XABuildToolsFolder);
			string XABuildToolsVersion         = GetRequiredProperty (KnownProperties.XABuildToolsVersion);
			string XABuildToolsPackagePrefix   = Context.Instance.Properties [KnownProperties.XABuildToolsPackagePrefix] ?? String.Empty;
			string XAPlatformToolsVersion  = GetRequiredProperty (KnownProperties.XAPlatformToolsVersion);
			string XAPlatformToolsPackagePrefix = Context.Instance.Properties [KnownProperties.XAPlatformToolsPackagePrefix] ?? String.Empty;
			bool isArm64Apple = Context.Instance.OS.Flavor == "macOS" && RuntimeInformation.OSArchitecture == Architecture.Arm64;
			string emulatorArch = isArm64Apple ? "aarch64" : "x64";
			string systemImageArch = isArm64Apple ? "arm64-v8a" : "x86_64";

			// Upstream manifests with version information:
			//
			//  https://dl-ssl.google.com/android/repository/repository2-1.xml
			//  https://dl-ssl.google.com/android/repository/repository2-3.xml
			//    * platform APIs
			//    * build-tools
			//    * command-line tools
			//    * sdk-tools
			//    * platform-tools
			//
			//  https://dl-ssl.google.com/android/repository/addon2-1.xml
			//    * android_m2repository_r47
			//
			//  https://dl-ssl.google.com/android/repository/sys-img/android/sys-img2-1.xml
			//  https://dl-ssl.google.com/android/repository/sys-img/google_apis/sys-img2-1.xml
			//    * system images
			//
			Components = new List<AndroidToolchainComponent> {
				new AndroidPlatformComponent ("android-2.3.3_r02", apiLevel: "10", pkgRevision: "2"),
				new AndroidPlatformComponent ("android-15_r05",    apiLevel: "15", pkgRevision: "5"),
				new AndroidPlatformComponent ("android-16_r05",    apiLevel: "16", pkgRevision: "5"),
				new AndroidPlatformComponent ("android-17_r03",    apiLevel: "17", pkgRevision: "3"),
				new AndroidPlatformComponent ("android-18_r03",    apiLevel: "18", pkgRevision: "3"),
				new AndroidPlatformComponent ("android-19_r04",    apiLevel: "19", pkgRevision: "4"),
				new AndroidPlatformComponent ("android-20_r02",    apiLevel: "20", pkgRevision: "2"),
				new AndroidPlatformComponent ("android-21_r02",    apiLevel: "21", pkgRevision: "2"),
				new AndroidPlatformComponent ("android-22_r02",    apiLevel: "22", pkgRevision: "2"),
				new AndroidPlatformComponent ("platform-23_r03",   apiLevel: "23", pkgRevision: "3"),
				new AndroidPlatformComponent ("platform-24_r02",   apiLevel: "24", pkgRevision: "3"), // Local package revision is actually .3
				new AndroidPlatformComponent ("platform-25_r03",   apiLevel: "25", pkgRevision: "3"),
				new AndroidPlatformComponent ("platform-26_r02",   apiLevel: "26", pkgRevision: "2"),
				new AndroidPlatformComponent ("platform-27_r03",   apiLevel: "27", pkgRevision: "3"),
				new AndroidPlatformComponent ("platform-28_r04",   apiLevel: "28", pkgRevision: "4"),
				new AndroidPlatformComponent ("platform-29_r01",   apiLevel: "29", pkgRevision: "1"),
				new AndroidPlatformComponent ("platform-30_r01",   apiLevel: "30", pkgRevision: "1"),
				new AndroidPlatformComponent ("platform-31_r01",   apiLevel: "31", pkgRevision: "1"),
				new AndroidPlatformComponent ("platform-32_r01",   apiLevel: "32", pkgRevision: "1"),
				new AndroidPlatformComponent ("platform-33-ext3_r03",   apiLevel: "33", pkgRevision: "3"),
				new AndroidPlatformComponent ("platform-34-ext7_r02",   apiLevel: "34", pkgRevision: "2", isLatestStable: true),
				new AndroidPlatformComponent ("platform-VanillaIceCream_r04",   apiLevel: "VanillaIceCream", pkgRevision: "4", isLatestStable: true),

				new AndroidToolchainComponent ("sources-34_r01",
					destDir: Path.Combine ("sources", "android-34"),
					pkgRevision: "1",
					dependencyType: AndroidToolchainComponentType.BuildDependency,
					buildToolVersion: "34.1"
				),
				new AndroidToolchainComponent ("docs-24_r01",
					destDir: "docs",
					pkgRevision: "1",
					dependencyType: AndroidToolchainComponentType.BuildDependency,
					buildToolVersion: "24.1"
				),
				new AndroidToolchainComponent ("android_m2repository_r47",
					destDir: Path.Combine ("extras", "android", "m2repository"),
					pkgRevision: "47.0.0",
					dependencyType: AndroidToolchainComponentType.BuildDependency,
					buildToolVersion: "47.0.0"
				),
				new AndroidToolchainComponent ($"{systemImageArch}-34_r04",
					destDir: Path.Combine ("system-images", "android-34", "default", systemImageArch),
					relativeUrl: new Uri ("sys-img/android/", UriKind.Relative),
					pkgRevision: "4",
					dependencyType: AndroidToolchainComponentType.EmulatorDependency
				),
				new AndroidToolchainComponent ($"android-ndk-r{AndroidNdkVersion}-{osTag}",
					destDir: AndroidNdkDirectory,
					pkgRevision: AndroidPkgRevision,
					buildToolName: $"android-ndk-r{AndroidNdkVersion}",
					buildToolVersion: AndroidPkgRevision
				),
				new AndroidToolchainComponent ($"{XABuildToolsPackagePrefix}build-tools_r{XABuildToolsVersion}-{altOsTag}",
					destDir: Path.Combine ("build-tools", XABuildToolsFolder),
					isMultiVersion: true,
					buildToolName: "android-sdk-build-tools",
					buildToolVersion: $"{XABuildToolsVersion}"
				),
				new AndroidToolchainComponent ($"commandlinetools-{cltOsTag}-{CommandLineToolsVersion}",
					destDir: Path.Combine ("cmdline-tools", CommandLineToolsFolder),
					isMultiVersion: true,
					buildToolName: "android-sdk-cmdline-tools",
					buildToolVersion: $"{CommandLineToolsFolder}.{CommandLineToolsVersion}"
				),
				new AndroidToolchainComponent ($"{XAPlatformToolsPackagePrefix}platform-tools_r{XAPlatformToolsVersion}-{osTag}",
					destDir: "platform-tools",
					pkgRevision: XAPlatformToolsVersion,
					buildToolName: "android-sdk-platform-tools",
					buildToolVersion: XAPlatformToolsVersion
				),
				new AndroidToolchainComponent ($"emulator-{osTag}_{emulatorArch}-{EmulatorVersion}",
					destDir: "emulator",
					pkgRevision: EmulatorPkgRevision,
					dependencyType: AndroidToolchainComponentType.EmulatorDependency
				),
				new AndroidToolchainComponent ($"{AndroidCmakeUrlPrefix}cmake-{AndroidCmakeVersion}-{osTag}",
					destDir: Path.Combine ("cmake", AndroidCmakeVersionPath),
					isMultiVersion: true,
					noSubdirectory: true,
					pkgRevision: AndroidCmakeVersion,
					buildToolName: "android-sdk-cmake",
					buildToolVersion: AndroidCmakeVersion
				),
			};
		}

		static string GetRequiredProperty (string propertyName)
		{
			string? value = Context.Instance.Properties [propertyName];
			if (String.IsNullOrEmpty (value))
				throw new InvalidOperationException ($"Required property '{propertyName}' not defined");
			return value!;
		}
	}
}
