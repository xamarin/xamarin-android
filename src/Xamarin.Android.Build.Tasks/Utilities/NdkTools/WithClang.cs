using System;
using System.IO;

using Microsoft.Build.Utilities;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	abstract class NdkToolsWithClang : NdkTools
	{
		protected string UnifiedHeadersDirPath { get; }

		// See NdkToolsWithClangWithPlatforms.ctor for explanation
		protected bool NeedClangWorkaround { get; set; }

		protected NdkToolsWithClang (string androidNdkPath, NdkVersion version, TaskLoggingHelper? log)
			: base (androidNdkPath, version, log)
		{
			NdkToolNames[NdkToolKind.CompilerC] = "clang";
			NdkToolNames[NdkToolKind.CompilerCPlusPlus] = "clang++";
			UnifiedHeadersDirPath = GetUnifiedHeadersDirPath (androidNdkPath);
			UsesClang = true;
		}

		public override bool ValidateNdkPlatform (Action<string> logMessage, Action<string, string> logError, AndroidTargetArch arch, bool enableLLVM)
		{
			// Check that we have a compatible NDK version for the targeted ABIs.
			if (Version.Main.Major < 19) {
				logMessage (
					"The detected Android NDK version is incompatible with this version of Xamarin.Android, " +
					"please upgrade to NDK r19 or newer.");
			}

			return true;
		}

		public override int GetMinimumApiLevelFor (AndroidTargetArch arch)
		{
			int minValue = 0;
			string archName = GetPlatformArch (arch);
			if (!XABuildConfig.ArchAPILevels.TryGetValue (archName, out minValue))
				throw new InvalidOperationException ($"Unable to determine minimum API level for architecture {arch}");

			return minValue;
		}

		public override string GetToolPath (NdkToolKind kind, AndroidTargetArch arch, int apiLevel)
		{
			string toolName = GetToolName (kind);

			if (kind == NdkToolKind.CompilerC || kind == NdkToolKind.CompilerCPlusPlus) {
				if (!NeedClangWorkaround) {
					// See NdkToolsWithClangWithPlatforms.ctor for explanation
					toolName = $"{GetCompilerTriple (arch)}{apiLevel}-{toolName}";
				}
			} else {
				toolName = $"{GetArchTriple (arch)}-{toolName}";
			}

			return MakeToolPath (toolName);
		}

		public override string GetToolPath (string name, AndroidTargetArch arch, int apiLevel)
		{
			return MakeToolPath ($"{GetArchTriple (arch)}-{name}", mustExist: false);
		}

		public override string GetClangDeviceLibraryPath ()
		{
			string toolchainDir = GetToolchainDir ();
			string clangBaseDir = Path.Combine (toolchainDir, "lib64", "clang");

			if (!Directory.Exists (clangBaseDir)) {
				throw new InvalidOperationException ($"Clang toolchain directory '{clangBaseDir}' not found");
			}

			// There should be just one subdir - clang version - but it's better to be safe than sorry...
			foreach (string dir in Directory.EnumerateDirectories (clangBaseDir)) {
				string libDir = Path.Combine (dir, "lib", "linux");
				if (Directory.Exists (libDir)) {
					return libDir;
				}
			}

			throw new InvalidOperationException ("Unable to locate clang device library path");
		}

		public override string GetNdkToolchainPrefix (AndroidTargetArch arch)
		{
			if (arch == AndroidTargetArch.Arm) {
				return "armv7a-linux-androideabi-";
			}

			return base.GetNdkToolchainPrefix (arch);
		}

		protected string GetCompilerTriple (AndroidTargetArch arch)
		{
			return arch == AndroidTargetArch.Arm ? "armv7a-linux-androideabi" : GetArchTriple (arch);
		}

		protected string MakeToolPath (string toolName, bool mustExist = true)
		{
			string toolPath = Path.Combine (GetToolchainDir (), "bin", toolName);

			return GetExecutablePath (toolPath, mustExist) ?? String.Empty;
		}

		protected string GetToolchainDir ()
		{
			return Path.Combine (NdkRootDirectory, "toolchains", "llvm", "prebuilt", HostPlatform);
		}

		protected override string GetAsmIncludeDirPath (AndroidTargetArch arch, int apiLevel)
		{
			return UnifiedHeaders_GetAsmIncludeDirPath (arch, apiLevel);
		}

		protected override string GetPlatformIncludeDirPath (AndroidTargetArch arch, int apiLevel)
		{
			return UnifiedHeaders_GetPlatformIncludeDirPath (arch, apiLevel);
		}
	}
}
