using System;
using System.IO;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	static class TestUtilities
	{
		public static string GetApkPath (string androidPackageName)
		{
			return GetApkPath (androidPackageName, "apk");
		}

		public static string GetApkPath (string androidPackageName, string packageExtension)
		{
			if (packageExtension.Length == 0) {
				throw new ArgumentException ("must not be empty", nameof (packageExtension));
			}

			return Path.Combine (Configurables.Paths.TestBinDir, $"{androidPackageName}-Signed.{packageExtension}");
		}
	}
}
