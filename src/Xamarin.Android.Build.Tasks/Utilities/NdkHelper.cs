using System;
using System.IO;

using System.Runtime.InteropServices;

namespace Xamarin.Android.Tasks
{
	static class NdkHelper
	{
		static readonly string toolchainHostName;
		static readonly string relativeToolchainDir;

		public static string ToolchainHostName => toolchainHostName;
		public static string RelativeToolchainDir => relativeToolchainDir;

		static NdkHelper ()
		{
			string os;
			if (RuntimeInformation.IsOSPlatform (OSPlatform.Windows)) {
				os = "windows";
			} else if (RuntimeInformation.IsOSPlatform (OSPlatform.OSX)) {
				os = "darwin";
			} else if (RuntimeInformation.IsOSPlatform (OSPlatform.Linux)) {
				os = "linux";
			} else {
				throw new InvalidOperationException ($"Unsupported OS {RuntimeInformation.OSDescription}");
			}

			// We care only about the latest NDK versions, they have only x86_64 versions. We'll need to revisit the code once
			// native macOS/arm64 toolchain is out.
			toolchainHostName = $"{os}-x86_64";

			relativeToolchainDir = Path.Combine ("toolchains", "llvm", "prebuilt", toolchainHostName);
		}

		public static string TranslateAbiToLLVM (string xaAbi)
		{
			return xaAbi switch {
				"armeabi-v7a" => "arm",
				"arm64-v8a" => "aarch64",
				"x86" => "i386",
				"x86_64" => "x86_64",
				_ => throw new InvalidOperationException ($"Unknown ABI '{xaAbi}'"),
			};
		}

		public static string RIDToABI (string rid)
		{
			return rid switch {
				"android-arm" => "armeabi-v7a",
				"android-arm64" => "arm64-v8a",
				"android-x86" => "x86",
				"android-x64" => "x86_64",
				_ => throw new InvalidOperationException ($"Unknown RID '{rid}'")
			};
		}
	}
}
