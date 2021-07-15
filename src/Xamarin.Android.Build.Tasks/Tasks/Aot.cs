using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Java.Interop.Tools.Diagnostics;
using Xamarin.Android.Tools;
using Xamarin.Build;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public enum AotMode : uint
	{
		None      = 0x0000,
		Normal    = 0x0001,
		Hybrid    = 0x0002,
		Full      = 0x0003,
		Interp    = 0x0004,
	}

	public enum SequencePointsMode {
		None,
		Normal,
		Offline,
	}

	// can't be a single ToolTask, because it has to run mkbundle many times for each arch.
	public class Aot : AndroidAsyncTask
	{
		public override string TaskPrefix => "AOT";

		[Required]
		public string AndroidAotMode { get; set; }

		public string AndroidNdkDirectory { get; set; }

		[Required]
		public string AndroidApiLevel { get; set; }

		[Required]
		public ITaskItem ManifestFile { get; set; }

		[Required]
		public ITaskItem[] ResolvedAssemblies { get; set; }

		// Which ABIs to include native libs for
		[Required]
		public string [] SupportedAbis { get; set; }

		[Required]
		public string AotOutputDirectory { get; set; }

		[Required]
		public string IntermediateAssemblyDir { get; set; }

		public string LinkMode { get; set; }

		public bool EnableLLVM { get; set; }

		public string AndroidSequencePointsMode { get; set; }

		public string AotAdditionalArguments { get; set; }

		public ITaskItem[] AdditionalNativeLibraryReferences { get; set; }

		public string ExtraAotOptions { get; set; }

		public ITaskItem [] Profiles { get; set; }

		[Required]
		public string AndroidBinUtilsDirectory { get; set; }

		[Output]
		public string[] NativeLibrariesReferences { get; set; }

		AotMode AotMode;
		SequencePointsMode sequencePointsMode;

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
				aotMode = AotMode.Interp;
				return true; // We don't do anything here for this mode, this is just to set the flag for the XA
							  // runtime to initialize Mono in the inrepreter "AOT" mode.
			}

			return false;
		}

		public static bool TryGetSequencePointsMode (string value, out SequencePointsMode mode)
		{
			mode = SequencePointsMode.None;
			switch ((value ?? string.Empty).ToLowerInvariant().Trim ()) {
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

		static string GetNdkToolchainLibraryDir (NdkTools ndk, string binDir, string archDir = null)
		{
			var baseDir = Path.GetFullPath(Path.Combine(binDir, ".."));

			string libDir = Path.Combine (baseDir, "lib", "gcc");
			if (!String.IsNullOrEmpty (archDir))
				libDir = Path.Combine (libDir, archDir);

			var gccLibDir = Directory.EnumerateDirectories (libDir).ToList();
			gccLibDir.Sort();

			var libPath = gccLibDir.LastOrDefault();
			if (libPath == null) {
				goto no_toolchain_error;
			}

			if (ndk.UsesClang)
				return libPath;

			gccLibDir = Directory.EnumerateDirectories(libPath).ToList();
			gccLibDir.Sort();

			libPath = gccLibDir.LastOrDefault();
			if (libPath == null) {
				goto no_toolchain_error;
			}

			return libPath;

		  no_toolchain_error:
			throw new Exception("Could not find a valid NDK compiler toolchain library path");
		}

		static string GetNdkToolchainLibraryDir (NdkTools ndk, string binDir, AndroidTargetArch arch)
		{
			return GetNdkToolchainLibraryDir (ndk, binDir, ndk.GetArchDirName (arch));
		}

		static string QuoteFileName(string fileName)
		{
			var builder = new CommandLineBuilder();
			builder.AppendFileNameIfNotNull(fileName);
			return builder.ToString();
		}

		int GetNdkApiLevel (NdkTools ndk, string androidApiLevel, AndroidTargetArch arch)
		{
			var manifest    = AndroidAppManifest.Load (ManifestFile.ItemSpec, MonoAndroidHelper.SupportedVersions);

			int level;
			if (manifest.MinSdkVersion.HasValue) {
				level       = manifest.MinSdkVersion.Value;
			}
			else if (int.TryParse (androidApiLevel, out level)) {
				// level already set
			}
			else {
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

		public async override System.Threading.Tasks.Task RunTaskAsync ()
		{
			NdkTools? ndk = NdkTools.Create (AndroidNdkDirectory, Log);
			if (ndk == null) {
				return; // NdkTools.Create will log appropriate error
			}

			bool hasValidAotMode = GetAndroidAotMode (AndroidAotMode, out AotMode);
			if (!hasValidAotMode) {
				LogCodedError ("XA3002", Properties.Resources.XA3002, AndroidAotMode);
				return;
			}

			if (AotMode == AotMode.Interp) {
				LogDebugMessage ("Interpreter AOT mode enabled");
				return;
			}

			TryGetSequencePointsMode (AndroidSequencePointsMode, out sequencePointsMode);

			var nativeLibs = new List<string> ();

			await this.WhenAllWithLock (GetAotConfigs (ndk),
				(config, lockObject) => {
					if (!config.Valid) {
						Cancel ();
						return;
					}

					if (!RunAotCompiler (config.AssembliesPath, config.AotCompiler, config.AotOptions, config.AssemblyPath, config.ResponseFile)) {
						LogCodedError ("XA3001", Properties.Resources.XA3001, Path.GetFileName (config.AssemblyPath));
						Cancel ();
						return;
					}

					File.Delete (config.ResponseFile);

					lock (lockObject)
						nativeLibs.Add (config.OutputFile);
				}
			);

			NativeLibrariesReferences = nativeLibs.ToArray ();

			LogDebugMessage ("Aot Outputs:");
			LogDebugTaskItems ("  NativeLibrariesReferences: ", NativeLibrariesReferences);
		}

		IEnumerable<Config> GetAotConfigs (NdkTools ndk)
		{
			if (!Directory.Exists (AotOutputDirectory))
				Directory.CreateDirectory (AotOutputDirectory);

			var sdkBinDirectory = MonoAndroidHelper.GetOSBinPath ();
			foreach (var abi in SupportedAbis) {
				string aotCompiler = "";
				string outdir = "";
				string mtriple = "";
				AndroidTargetArch arch;

				switch (abi) {
				case "armeabi-v7a":
					aotCompiler = Path.Combine (sdkBinDirectory, "cross-arm");
					outdir = Path.Combine (AotOutputDirectory, "armeabi-v7a");
					mtriple = "armv7-linux-gnueabi";
					arch = AndroidTargetArch.Arm;
					break;

				case "arm64":
				case "arm64-v8a":
				case "aarch64":
					aotCompiler = Path.Combine (sdkBinDirectory, "cross-arm64");
					outdir = Path.Combine (AotOutputDirectory, "arm64-v8a");
					mtriple = "aarch64-linux-android";
					arch = AndroidTargetArch.Arm64;
					break;

				case "x86":
					aotCompiler = Path.Combine (sdkBinDirectory, "cross-x86");
					outdir = Path.Combine (AotOutputDirectory, "x86");
					mtriple = "i686-linux-android";
					arch = AndroidTargetArch.X86;
					break;

				case "x86_64":
					aotCompiler = Path.Combine (sdkBinDirectory, "cross-x86_64");
					outdir = Path.Combine (AotOutputDirectory, "x86_64");
					mtriple = "x86_64-linux-android";
					arch = AndroidTargetArch.X86_64;
					break;

				// case "mips":
				default:
					throw new Exception ("Unsupported Android target architecture ABI: " + abi);
				}

				if (EnableLLVM && !ndk.ValidateNdkPlatform (LogMessage, LogCodedError, arch, enableLLVM:EnableLLVM)) {
					yield return Config.Invalid;
					yield break;
				}

				outdir = Path.GetFullPath (outdir);
				if (!Directory.Exists (outdir))
					Directory.CreateDirectory (outdir);

				// dont use a full path if the outdir is withing the WorkingDirectory.
				if (outdir.StartsWith (WorkingDirectory, StringComparison.InvariantCultureIgnoreCase)) {
					outdir = outdir.Replace (WorkingDirectory + Path.DirectorySeparatorChar, string.Empty);
				}

				int level = 0;
				string toolPrefix = EnableLLVM
					? ndk.GetNdkToolPrefixForAOT (arch, level = GetNdkApiLevel (ndk, AndroidApiLevel, arch))
					: Path.Combine (AndroidBinUtilsDirectory, $"{ndk.GetArchDirName (arch)}-");
				var toolchainPath = toolPrefix.Substring(0, toolPrefix.LastIndexOf(Path.DirectorySeparatorChar));
				var ldFlags = string.Empty;
				if (EnableLLVM) {
					if (string.IsNullOrEmpty (AndroidNdkDirectory)) {
						yield return Config.Invalid;
						yield break;
					}

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

					var libs = new List<string>();
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

					ldFlags = $"\\\"{string.Join("\\\";\\\"", libs)}\\\"";
				}

				string ldName = String.Empty;
				if (EnableLLVM) {
					ldName = ndk.GetToolPath (NdkToolKind.Linker, arch, level);
					if (!String.IsNullOrEmpty (ldName)) {
						ldName = Path.GetFileName (ldName);
						if (ldName.IndexOf ('-') >= 0) {
							ldName = ldName.Substring (ldName.LastIndexOf ("-") + 1);
						}
					}
				} else {
					ldName = "ld";
				}

				foreach (var assembly in ResolvedAssemblies) {
					string outputFile = Path.Combine(outdir, string.Format ("libaot-{0}.so",
						Path.GetFileName (assembly.ItemSpec)));

					string seqpointsFile = Path.Combine(outdir, string.Format ("{0}.msym",
						Path.GetFileName (assembly.ItemSpec)));

					string tempDir = Path.Combine (outdir, Path.GetFileName (assembly.ItemSpec));
					if (!Directory.Exists (tempDir))
						Directory.CreateDirectory (tempDir);

					List<string> aotOptions = new List<string> ();

					if (Profiles != null && Profiles.Length > 0) {
						aotOptions.Add ("profile-only");
						foreach (var p in Profiles) {
							var fp = Path.GetFullPath (p.ItemSpec);
							aotOptions.Add ($"profile={fp}");
						}
					}
					if (!string.IsNullOrEmpty (AotAdditionalArguments))
						aotOptions.Add (AotAdditionalArguments);
					if (sequencePointsMode == SequencePointsMode.Offline)
						aotOptions.Add ($"msym-dir={outdir}");
					if (AotMode != AotMode.Normal)
						aotOptions.Add (AotMode.ToString ().ToLowerInvariant ());

					aotOptions.Add ($"outfile={outputFile}");
					aotOptions.Add ("asmwriter");
					aotOptions.Add ($"mtriple={mtriple}");
					aotOptions.Add ($"tool-prefix={toolPrefix}");
					aotOptions.Add ($"llvm-path={sdkBinDirectory}");
					aotOptions.Add ($"temp-path={tempDir}");

					if (!String.IsNullOrEmpty (ldName)) {
						// MUST be before `ld-flags`, otherwise Mono fails to parse it on Windows
						aotOptions.Add ($"ld-name={ldName}");
					}

					aotOptions.Add ($"ld-flags={ldFlags}");

					// we need to quote the entire --aot arguments here to make sure it is parsed
					// on windows as one argument. Otherwise it will be split up into multiple
					// values, which wont work.
					string aotOptionsStr = (EnableLLVM ? "--llvm " : "") + $"\"--aot={string.Join (",", aotOptions)}\"";

					if (!string.IsNullOrEmpty (ExtraAotOptions)) {
						aotOptionsStr += (aotOptions.Count > 0 ? " " : "") + ExtraAotOptions;
					}

					// Due to a Monodroid MSBuild bug we can end up with paths to assemblies that are not in the intermediate
					// assembly directory (typically obj/assemblies). This can lead to problems with the Mono loader not being
					// able to find their dependency laters, since framework assemblies are stored in different directories.
					// This can happen when linking is disabled (AndroidLinkMode=None). Workaround this problem by resolving
					// the paths to the right assemblies manually.
					var resolvedPath = Path.GetFullPath (assembly.ItemSpec);
					var intermediateAssemblyPath = Path.Combine (IntermediateAssemblyDir, Path.GetFileName (assembly.ItemSpec));

					if (LinkMode.ToLowerInvariant () == "none") {
						if (!resolvedPath.Contains (IntermediateAssemblyDir) && File.Exists (intermediateAssemblyPath))
							resolvedPath = intermediateAssemblyPath;
					}

					var assembliesPath = Path.GetFullPath (Path.GetDirectoryName (resolvedPath));
					var assemblyPath = Path.GetFullPath (resolvedPath);

					yield return new Config (assembliesPath, aotCompiler, aotOptionsStr, assemblyPath, outputFile, Path.Combine (tempDir, "response.txt"));
				}
			}
		}

		bool RunAotCompiler (string assembliesPath, string aotCompiler, string aotOptions, string assembly, string responseFile)
		{
			var stdout_completed = new ManualResetEvent (false);
			var stderr_completed = new ManualResetEvent (false);

			using (var sw = new StreamWriter (responseFile, append: false, encoding: Files.UTF8withoutBOM)) {
				sw.WriteLine (aotOptions + " " + QuoteFileName (assembly));
			}

			var psi = new ProcessStartInfo () {
				FileName = QuoteFileName (aotCompiler),
				Arguments = $"--response={QuoteFileName (responseFile)}",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
				CreateNoWindow=true,
				WindowStyle=ProcessWindowStyle.Hidden,
				WorkingDirectory = WorkingDirectory,
			};

			// we do not want options to be provided out of band to the cross compilers
			psi.EnvironmentVariables ["MONO_ENV_OPTIONS"] = String.Empty;
			// the C code cannot parse all the license details, including the activation code that tell us which license level is allowed
			// so we provide this out-of-band to the cross-compilers - this can be extended to communicate a few others bits as well
			psi.EnvironmentVariables ["MONO_PATH"] = assembliesPath;

			LogDebugMessage ("[AOT] MONO_PATH=\"{0}\" MONO_ENV_OPTIONS=\"{1}\" {2} {3}",
				psi.EnvironmentVariables ["MONO_PATH"], psi.EnvironmentVariables ["MONO_ENV_OPTIONS"], psi.FileName, psi.Arguments);

			if (!string.IsNullOrEmpty (responseFile))
				LogDebugMessage ("[AOT] response file {0}: {1}", responseFile, File.ReadAllText (responseFile));

			using (var proc = new Process ()) {
				proc.OutputDataReceived += (s, e) => {
					if (e.Data != null)
						OnAotOutputData (s, e);
					else
						stdout_completed.Set ();
				};
				proc.ErrorDataReceived += (s, e) => {
					if (e.Data != null)
						OnAotErrorData (s, e);
					else
						stderr_completed.Set ();
				};
				proc.StartInfo = psi;
				proc.Start ();
				proc.BeginOutputReadLine ();
				proc.BeginErrorReadLine ();
				CancellationToken.Register (() => { try { proc.Kill (); } catch (Exception) { } });
				proc.WaitForExit ();
				if (psi.RedirectStandardError)
					stderr_completed.WaitOne (TimeSpan.FromSeconds (30));
				if (psi.RedirectStandardOutput)
					stdout_completed.WaitOne (TimeSpan.FromSeconds (30));
				return proc.ExitCode == 0;
			}
		}

		void OnAotOutputData (object sender, DataReceivedEventArgs e)
		{
			if (e.Data != null)
				LogMessage ("[aot-compiler stdout] {0}", e.Data);
		}

		void OnAotErrorData (object sender, DataReceivedEventArgs e)
		{
			if (e.Data != null)
				LogMessage ("[aot-compiler stderr] {0}", e.Data);
		}

		struct Config {
			public string AssembliesPath { get; }
			public string AotCompiler { get; }
			public string AotOptions { get; }
			public string AssemblyPath { get; }
			public string OutputFile { get; }
			public string ResponseFile { get; }

			public bool Valid { get; private set; }

			public Config (string assembliesPath, string aotCompiler, string aotOptions, string assemblyPath, string outputFile, string responseFile)
			{
				AssembliesPath = assembliesPath;
				AotCompiler = aotCompiler;
				AotOptions = aotOptions;
				AssemblyPath = assemblyPath;
				OutputFile = outputFile;
				ResponseFile = responseFile;
				Valid = true;
			}

			public static Config Invalid {
				get { return new Config { Valid = false }; }
			}
		}
	}
}
