// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class GetJavaPlatformJar : AndroidTask
	{
		public override string TaskPrefix => "GJP";

		private XNamespace androidNs = "http://schemas.android.com/apk/res/android";

		[Required]
		public string AndroidSdkPlatform { get; set; }

		public string AndroidManifest { get; set; }

		[Output]
		public string JavaPlatformJarPath { get; set; }

		[Output]
		public string TargetSdkVersion    { get; set; }

		public override bool RunTask ()
		{
			var platform = AndroidSdkPlatform;

			XAttribute target_sdk = null;

			// Look for targetSdkVersion in the user's AndroidManifest.xml
			if (!string.IsNullOrWhiteSpace (AndroidManifest)) {
				if (!File.Exists (AndroidManifest)) {
					Log.LogCodedError ("XA1018", Properties.Resources.XA1018, AndroidManifest);
					return false;
				}

				try {
					var doc = XDocument.Load (AndroidManifest);
					var manifest = doc.Root;

					if (manifest != null) {
						var uses_sdk = manifest.Element ("uses-sdk");

						if (uses_sdk != null) {
							target_sdk = uses_sdk.Attribute (androidNs + "targetSdkVersion");

							if (target_sdk != null && !string.IsNullOrWhiteSpace (target_sdk.Value))
								platform = target_sdk.Value;

							var min_sdk = uses_sdk.Attribute (androidNs + "minSdkVersion");
							if (min_sdk != null && (!int.TryParse (min_sdk.Value, out int minSdkVersion) || minSdkVersion < XABuildConfig.NDKMinimumApiAvailable)) {
								Log.LogWarningForXmlNode (
										code:             "XA4216",
										file:             AndroidManifest,
										node:             min_sdk,
										message:          Properties.Resources.XA4216_MinSdkVersion,
										messageArgs:      new object [] { min_sdk?.Value, XABuildConfig.NDKMinimumApiAvailable }
								);
							}
							if (target_sdk != null && (!int.TryParse (target_sdk.Value, out int targetSdkVersion) || targetSdkVersion < XABuildConfig.NDKMinimumApiAvailable)) {
								Log.LogWarningForXmlNode (
										code:             "XA4216",
										file:             AndroidManifest,
										node:             target_sdk,
										message:          Properties.Resources.XA4216_TargetSdkVersion,
										messageArgs:      new object [] { target_sdk?.Value, XABuildConfig.NDKMinimumApiAvailable }
								);
							}
						}
					}
				} catch (Exception ex) {
					// If they're manifest is bad, let's error them out
					Log.LogErrorFromException (ex, true);
					return false;
				}
			}

			platform            = GetTargetSdkVersion (platform, target_sdk);
			JavaPlatformJarPath =  MonoAndroidHelper.TryGetAndroidJarPath (Log, platform);
			if (JavaPlatformJarPath == null)
				return !Log.HasLoggedErrors;

			TargetSdkVersion = MonoAndroidHelper.SupportedVersions.GetApiLevelFromId (platform).ToString ();

			return !Log.HasLoggedErrors;
		}

		string GetTargetSdkVersion (string target, XAttribute target_sdk)
		{
			string targetFrameworkVersion = MonoAndroidHelper.SupportedVersions.GetIdFromApiLevel (AndroidSdkPlatform);
			string targetSdkVersion       = MonoAndroidHelper.SupportedVersions.GetIdFromApiLevel (target);

			if (!int.TryParse (targetFrameworkVersion, out int frameworkSdk)) {
				// AndroidSdkPlatform is likely a *preview* API level; use it.
				Log.LogWarningForXmlNode (
						code:             "XA4211",
						file:             AndroidManifest,
						node:             target_sdk,
						message:          Properties.Resources.XA4211,
						messageArgs:      new [] {
							targetSdkVersion,
							MonoAndroidHelper.SupportedVersions.GetIdFromFrameworkVersion (targetFrameworkVersion),
							MonoAndroidHelper.SupportedVersions.GetIdFromApiLevel (targetFrameworkVersion),
						}
				);
				return targetFrameworkVersion;
			}
			if (int.TryParse (targetSdkVersion, out int targetSdk) &&
					targetSdk < frameworkSdk) {
				Log.LogWarningForXmlNode (
						code:             "XA4211",
						file:             AndroidManifest,
						node:             target_sdk,
						message:          Properties.Resources.XA4211,
						messageArgs:      new [] {
							targetSdkVersion,
							MonoAndroidHelper.SupportedVersions.GetIdFromFrameworkVersion (targetFrameworkVersion),
							MonoAndroidHelper.SupportedVersions.GetIdFromApiLevel (targetFrameworkVersion),
						}
				);
				return targetFrameworkVersion;
			}
			return targetSdkVersion ?? targetFrameworkVersion;
		}
	}
}
