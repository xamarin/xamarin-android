using System;
using System.Diagnostics;
using System.IO;
using Xamarin.Android.Tools;

namespace Xamarin.ProjectTools
{
	public static class AndroidSdkResolver
	{
		static string HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		static string DefaultToolchainPath = Path.Combine (HomeDirectory, "android-toolchain");
		static string AzureToolchainPathMacOS = Path.Combine (HomeDirectory, "Library", "Android");
		static string ToolchainPath = (TestEnvironment.IsMacOS && TestEnvironment.IsRunningOnCI) ? AzureToolchainPathMacOS : DefaultToolchainPath;

		static string GetPathFromRegistry (string valueName)
		{
			if (TestEnvironment.IsWindows) {
				return (string) Microsoft.Win32.Registry.GetValue ("HKEY_CURRENT_USER\\SOFTWARE\\Novell\\Mono for Android", valueName, null);
			}
			return null;
		}

		public static string GetAndroidSdkPath ()
		{
			var sdkPath = Environment.GetEnvironmentVariable ("ANDROID_SDK_PATH");
			if (String.IsNullOrEmpty (sdkPath))
				sdkPath = GetPathFromRegistry ("AndroidSdkDirectory");
			if (String.IsNullOrEmpty (sdkPath))
				sdkPath = Path.GetFullPath (Path.Combine (ToolchainPath, "sdk"));

			return sdkPath;

		}

		public static string GetAndroidNdkPath ()
		{
			var ndkPath = Environment.GetEnvironmentVariable ("ANDROID_NDK_PATH");
			if (String.IsNullOrEmpty (ndkPath))
				ndkPath = GetPathFromRegistry ("AndroidNdkDirectory");
			if (String.IsNullOrEmpty (ndkPath))
				ndkPath = Path.GetFullPath (Path.Combine (ToolchainPath, "ndk"));

			return ndkPath;
		}

		public static string GetJavaSdkPath ()
		{
			var androidSdk = new AndroidSdkInfo ((l, m) => {
				Console.WriteLine ($"{l}: {m}");
				if (l == TraceLevel.Error) {
					throw new Exception (m);
				}
			}, GetAndroidSdkPath ());

			return androidSdk.JavaSdkPath;
		}
	}
}
