#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Java.Interop.Tools.Diagnostics;
using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Framework;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	/// <summary>
	/// The <Aot/> task subclasses this in "legacy" Xamarin.Android.
	/// The <GetAotAssemblies/> task subclasses this in .NET 6+.
	/// </summary>
	public abstract class GetAotArguments : AsyncTask
	{
		[Required]
		public string AndroidApiLevel { get; set; } = "";

		[Required]
		public string AndroidAotMode { get; set; } = "";

		[Required]
		public string AotOutputDirectory { get; set; } = "";

		[Required]
		public string AndroidBinUtilsDirectory { get; set; } = "";

		[Required]
		public string TargetName { get; set; } = "";

		/// <summary>
		/// Will be blank in .NET 6+
		/// </summary>
		public string ManifestFile { get; set; } = "";

		/// <summary>
		/// $(AndroidMinimumSupportedApiLevel) in .NET 6+
		/// </summary>
		public string MinimumSupportedApiLevel { get; set; } = "";

		public string RuntimeIdentifier { get; set; } = "";

		public string AndroidNdkDirectory { get; set; } = "";

		public bool EnableLLVM { get; set; }

		public bool StripLibraries { get; set; }

		public string AndroidSequencePointsMode { get; set; } = "";

		public ITaskItem [] Profiles { get; set; } = Array.Empty<ITaskItem> ();

		public int ZipAlignmentPages { get; set; } = AndroidZipAlign.DefaultZipAlignment64Bit;

		[Required, Output]
		public ITaskItem [] ResolvedAssemblies { get; set; } = Array.Empty<ITaskItem> ();

		[Output]
		public string? Triple { get; set; }

		[Output]
		public string? ToolPrefix { get; set; }

		[Output]
		public string? MsymPath { get; set; }

		[Output]
		public string? LdName { get; set; }

		[Output]
		public string? LdFlags { get; set; }

		protected AotMode AotMode;
		protected SequencePointsMode SequencePointsMode;
		protected string SdkBinDirectory = "";
		protected bool UseAndroidNdk => !string.IsNullOrWhiteSpace (AndroidNdkDirectory);

		public static bool GetAndroidAotMode(string androidAotMode, out AotMode aotMode)
		{
			aotMode = AotMode.Normal;

			switch ((androidAotMode ?? string.Empty).ToLowerInvariant().Trim())
			{
			case "":
			case "none":
				aotMode = AotMode.None;
				return true;
			case "normal":
				aotMode = AotMode.Normal;
				return true;
			case "hybrid":
				aotMode = AotMode.Hybrid;
				return true;
			case "full":
				aotMode = AotMode.Full;
				return true;
			case "interpreter":
				// We don't do anything here for this mode, this is just to set the flag for the XA
				// runtime to initialize Mono in the interpreter "AOT" mode.
				aotMode = AotMode.Interp;
				return true;
			}

			return false;
		}

		public static bool TryGetSequencePointsMode (string value, out SequencePointsMode mode)
		{
			mode = SequencePointsMode.None;
			switch ((value ?? string.Empty).ToLowerInvariant ().Trim ()) {
			case "none":
				mode = SequencePointsMode.None;
				return true;
			case "normal":
				mode = SequencePointsMode.Normal;
				return true;
			case "offline":
				mode = SequencePointsMode.Offline;
				return true;
			}
			return false;
		}

		protected string GetToolPrefix (NdkTools ndk, AndroidTargetArch arch, out int level)
		{
			level = 0;
			return UseAndroidNdk
				? ndk.GetNdkToolPrefixForAOT (arch, level = GetNdkApiLevel (ndk, arch))
				: Path.Combine (AndroidBinUtilsDirectory, $"{ndk.GetArchDirName (arch)}-");
		}

		int GetNdkApiLevel (NdkTools ndk, AndroidTargetArch arch)
		{
			AndroidAppManifest? manifest = null;
			if (!string.IsNullOrEmpty (ManifestFile)) {
				manifest = AndroidAppManifest.Load (ManifestFile, MonoAndroidHelper.SupportedVersions);
			}

			int level;
			if (manifest?.MinSdkVersion != null) {
				level       = manifest.MinSdkVersion.Value;
			} else if (int.TryParse (MinimumSupportedApiLevel, out level)) {
				// level already set
			} else if (int.TryParse (AndroidApiLevel, out level)) {
				// level already set
			} else {
				// Probably not ideal!
				level       = MonoAndroidHelper.SupportedVersions.MaxStableVersion.ApiLevel;
			}

			// Some Android API levels do not exist on the NDK level. Workaround this my mapping them to the
			// most appropriate API level that does exist.
			if (level == 6 || level == 7) level = 5;
			else if (level == 10) level = 9;
			else if (level == 11) level = 12;
			else if (level == 20) level = 19;
			else if (level == 22) level = 21;
			else if (level == 23) level = 21;

			// API levels below level 21 do not provide support for 64-bit architectures.
			if (ndk.IsNdk64BitArch (arch) && level < 21) {
				level = 21;
			}

			// We perform a downwards API level lookup search since we might not have hardcoded the correct API
			// mapping above and we do not want to crash needlessly.
			for (; level >= 5; level--) {
				try {
					ndk.GetDirectoryPath (NdkToolchainDir.PlatformLib, arch, level);
					break;
				} catch (InvalidOperationException ex) {
					// Path not found, continue searching...
					continue;
				}
			}

			return level;
		}

		protected (string aotCompiler, string outdir, string mtriple, AndroidTargetArch arch) GetAbiSettings (string abi)
		{
			switch (abi) {
				case "armeabi-v7a":
					return (
						Path.Combine (SdkBinDirectory, "cross-arm"),
						Path.Combine (AotOutputDirectory, "armeabi-v7a"),
						"armv7-linux-gnueabi",
						AndroidTargetArch.Arm
					);

				case "arm64":
				case "arm64-v8a":
				case "aarch64":
					return (
						Path.Combine (SdkBinDirectory, "cross-arm64"),
						Path.Combine (AotOutputDirectory, "arm64-v8a"),
						"aarch64-linux-android",
						AndroidTargetArch.Arm64
					);

				case "x86":
					return (
						Path.Combine (SdkBinDirectory, "cross-x86"),
						Path.Combine (AotOutputDirectory, "x86"),
						"i686-linux-android",
						AndroidTargetArch.X86
					);

				case "x86_64":
					return (
						Path.Combine (SdkBinDirectory, "cross-x86_64"),
						Path.Combine (AotOutputDirectory, "x86_64"),
						"x86_64-linux-android",
						AndroidTargetArch.X86_64
					);

				// case "mips":
				default:
					throw new Exception ("Unsupported Android target architecture ABI: " + abi);
			}
		}

		/// <summary>
		/// Fills [Output] parameters to pass to the --aot switch
		/// </summary>
		protected void GetAotOptions (NdkTools ndk, AndroidTargetArch arch, int level, string outdir, string toolPrefix)
		{
			if (SequencePointsMode == SequencePointsMode.Offline)
				MsymPath = outdir;

			string ldName;
			if (UseAndroidNdk) {
				ldName = ndk.GetToolPath (NdkToolKind.Linker, arch, level);
				if (!string.IsNullOrEmpty (ldName)) {
					ldName = Path.GetFileName (ldName);
					if (ldName.IndexOf ('-') >= 0) {
						ldName = ldName.Substring (ldName.LastIndexOf ("-", StringComparison.Ordinal) + 1);
					}
				}
			} else {
				ldName = "ld";
			}
			string ldFlags = GetLdFlags (ndk, arch, level, toolPrefix);
			if (!string.IsNullOrEmpty (ldName)) {
				LdName = ldName;
			}
			if (!string.IsNullOrEmpty (ldFlags)) {
				LdFlags = ldFlags;
			}
		}

		string GetLdFlags (NdkTools ndk, AndroidTargetArch arch, int level, string toolPrefix)
		{
			var toolchainPath = toolPrefix.Substring (0, toolPrefix.LastIndexOf (Path.DirectorySeparatorChar));
			var ldFlags = new StringBuilder ();
			var libs = new List<string> ();
			if (UseAndroidNdk && EnableLLVM) {
				string androidLibPath = string.Empty;
				try {
					androidLibPath = ndk.GetDirectoryPath (NdkToolchainDir.PlatformLib, arch, level);
				} catch (InvalidOperationException ex) {
					Diagnostic.Error (5101, ex.Message);
				}

				string toolchainLibDir;
				if (ndk.UsesClang) {
					if (ndk.NoBinutils) {
						toolchainLibDir = String.Empty;
					} else {
						toolchainLibDir = GetNdkToolchainLibraryDir (ndk, toolchainPath, arch);
					}
				} else
					toolchainLibDir = GetNdkToolchainLibraryDir (ndk, toolchainPath);

				if (ndk.UsesClang) {
					if (!String.IsNullOrEmpty (toolchainLibDir)) {
						libs.Add ($"-L{toolchainLibDir.TrimEnd ('\\')}");
					}
					libs.Add ($"-L{androidLibPath.TrimEnd ('\\')}");

					if (arch == AndroidTargetArch.Arm) {
						// Needed for -lunwind to work
						string compilerLibDir = Path.Combine (toolchainPath, "..", "sysroot", "usr", "lib", ndk.GetArchDirName (arch));
						libs.Add ($"-L{compilerLibDir.TrimEnd ('\\')}");
					}
				}

				if (!String.IsNullOrEmpty (toolchainLibDir)) {
					libs.Add (Path.Combine (toolchainLibDir, "libgcc.a"));
				}
				libs.Add (Path.Combine (androidLibPath, "libc.so"));
				libs.Add (Path.Combine (androidLibPath, "libm.so"));
			} else if (!UseAndroidNdk && EnableLLVM) {
				string libstubsPath = MonoAndroidHelper.GetLibstubsArchDirectoryPath (AndroidBinUtilsDirectory, arch);

				libs.Add (Path.Combine (libstubsPath, "libc.so"));
				libs.Add (Path.Combine (libstubsPath, "libm.so"));
			}

			if (libs.Count > 0) {
				ldFlags.Append ($"\\\"{string.Join ("\\\";\\\"", libs)}\\\"");
			}

			//
			// This flag is needed for Mono AOT to work correctly with the LLVM 14 `lld` linker due to the following change:
			//
			//   The AArch64 port now supports adrp+ldr and adrp+add optimizations. --no-relax can suppress the optimization.
			//
			// Without the flag, `lld` will modify AOT-generated code in a way that the Mono runtime doesn't support. Until
			// the runtime issue is fixed, we need to pass this flag then.
			//
			if (!UseAndroidNdk) {
				if (ldFlags.Length > 0) {
					ldFlags.Append (' ');
				}
				ldFlags.Append ("--no-relax");
			}

			if (StripLibraries) {
				if (ldFlags.Length > 0) {
					ldFlags.Append (' ');
				}
				ldFlags.Append ("-s");
			}

			uint maxPageSize;
			switch (arch) {
				case AndroidTargetArch.Arm64:
				case AndroidTargetArch.X86_64:
					maxPageSize = MonoAndroidHelper.ZipAlignmentToPageSize (ZipAlignmentPages);
					break;

				case AndroidTargetArch.Arm:
				case AndroidTargetArch.X86:
					maxPageSize = MonoAndroidHelper.ZipAlignmentToPageSize (AndroidZipAlign.ZipAlignment32Bit);
					break;

				default:
					throw new InvalidOperationException ($"Internal error: unsupported target architecture {arch}");
			}

			if (ldFlags.Length > 0) {
				ldFlags.Append (' ');
			}

			ldFlags.Append ("-z ");
			ldFlags.Append ($"max-page-size={maxPageSize}");

			return ldFlags.ToString ();
		}

		static string GetNdkToolchainLibraryDir (NdkTools ndk, string binDir, string archDir = null)
		{
			var baseDir = Path.GetFullPath (Path.Combine (binDir, ".."));

			string libDir = Path.Combine (baseDir, "lib", "gcc");
			if (!String.IsNullOrEmpty (archDir))
				libDir = Path.Combine (libDir, archDir);

			var gccLibDir = Directory.EnumerateDirectories (libDir).ToList ();
			gccLibDir.Sort ();

			var libPath = gccLibDir.LastOrDefault ();
			if (libPath == null) {
				goto no_toolchain_error;
			}

			if (ndk.UsesClang)
				return libPath;

			gccLibDir = Directory.EnumerateDirectories (libPath).ToList ();
			gccLibDir.Sort ();

			libPath = gccLibDir.LastOrDefault ();
			if (libPath == null) {
				goto no_toolchain_error;
			}

			return libPath;

		no_toolchain_error:
			throw new Exception ("Could not find a valid NDK compiler toolchain library path");
		}

		static string GetNdkToolchainLibraryDir (NdkTools ndk, string binDir, AndroidTargetArch arch)
		{
			return GetNdkToolchainLibraryDir (ndk, binDir, ndk.GetArchDirName (arch));
		}
	}
}
