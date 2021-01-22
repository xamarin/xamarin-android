using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
	public class Aot : GetAotArguments
	{
		public override string TaskPrefix => "AOT";

		[Required]
		public ITaskItem[] ResolvedAssemblies { get; set; }

		// Which ABIs to include native libs for
		[Required]
		public string [] SupportedAbis { get; set; }

		[Required]
		public string IntermediateAssemblyDir { get; set; }

		public string LinkMode { get; set; }

		public ITaskItem[] AdditionalNativeLibraryReferences { get; set; }

		public string ExtraAotOptions { get; set; }

		[Output]
		public string[] NativeLibrariesReferences { get; set; }

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

		public async override System.Threading.Tasks.Task RunTaskAsync ()
		{
			NdkTools? ndk = NdkTools.Create (AndroidNdkDirectory, Log);
			if (ndk == null) {
				return; // NdkTools.Create will log appropriate error
			}

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

			foreach (var abi in SupportedAbis) {
				(string aotCompiler, string outdir, string mtriple, AndroidTargetArch arch) = GetAbiSettings (abi);

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

				string toolPrefix = GetToolPrefix (ndk, arch, out int level);
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
					if (ndk.UsesClang)
						toolchainLibDir = GetNdkToolchainLibraryDir (ndk, toolchainPath, arch);
					else
						toolchainLibDir = GetNdkToolchainLibraryDir (ndk, toolchainPath);

					var libs = new List<string>();
					if (ndk.UsesClang) {
						libs.Add ($"-L{toolchainLibDir.TrimEnd ('\\')}");
						libs.Add ($"-L{androidLibPath.TrimEnd ('\\')}");

						if (arch == AndroidTargetArch.Arm) {
							// Needed for -lunwind to work
							string compilerLibDir = Path.Combine (toolchainPath, "..", "sysroot", "usr", "lib", ndk.GetArchDirName (arch));
							libs.Add ($"-L{compilerLibDir.TrimEnd ('\\')}");
						}
					}

					libs.Add (Path.Combine (toolchainLibDir, "libgcc.a"));
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

					var aotOptions = GetAotOptions (outdir, mtriple, toolPrefix);
					aotOptions.Add ($"outfile={outputFile}");
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
