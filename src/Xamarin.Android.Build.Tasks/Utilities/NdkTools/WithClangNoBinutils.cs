using System;
using System.IO;
using System.Collections.Generic;

using Microsoft.Build.Utilities;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	class NdkToolsWithClangNoBinutils : NdkToolsWithClangNoPlatforms
	{
		public NdkToolsWithClangNoBinutils (string androidNdkPath, NdkVersion version, TaskLoggingHelper? log)
			: base (androidNdkPath, version, log)
		{
			NdkToolNames[NdkToolKind.Linker] = "ld";
			NoBinutils = true;
		}

		public override string GetToolPath (NdkToolKind kind, AndroidTargetArch arch, int apiLevel)
		{
			switch (kind) {
				case NdkToolKind.Assembler:
				case NdkToolKind.Linker:
				case NdkToolKind.Strip:
					return GetEmbeddedToolPath (kind, arch);

				default:
					return base.GetToolPath (kind, arch, apiLevel);
			}
		}

		public override string GetToolPath (string name, AndroidTargetArch arch, int apiLevel)
		{
			// The only triple-prefixed binaries in NDK r23+ are the compilers, and these are
			// handled by the other GetToolPath overload.

			// Some tools might not have any prefix, let's check that first
			string toolPath = MakeToolPath (name, mustExist: false);
			if (!String.IsNullOrEmpty (toolPath)) {
				return toolPath;
			}

			// Otherwise, they might be prefixed with llvm-
			return MakeToolPath ($"llvm-{name}");
		}

		string GetEmbeddedToolPath (NdkToolKind kind, AndroidTargetArch arch)
		{
			string toolName = GetToolName (kind);
			string triple = GetArchTriple (arch);
			string binutilsDir = Path.Combine (MonoAndroidHelper.GetOSBinPath (), "binutils");

			return GetExecutablePath (Path.Combine (binutilsDir, $"{triple}-{toolName}"), mustExist: true);
		}
	}
}
