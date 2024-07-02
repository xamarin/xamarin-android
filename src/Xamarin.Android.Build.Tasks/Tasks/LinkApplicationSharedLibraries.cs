using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class LinkApplicationSharedLibraries : AsyncTask
	{
		public override string TaskPrefix => "LAS";

		sealed class Config
		{
			public string LinkerPath;
			public string LinkerOptions;
			public string OutputSharedLibrary;
		}

		sealed class InputFiles
		{
			public List<string> ObjectFiles;
			public string OutputSharedLibrary;
			public List<string> ExtraLibraries;
		}

		[Required]
		public ITaskItem[] ObjectFiles { get; set; }

		[Required]
		public ITaskItem[] ApplicationSharedLibraries { get; set; }

		[Required]
		public bool DebugBuild { get; set; }

		[Required]
		public string AndroidBinUtilsDirectory { get; set; }

		public int ZipAlignmentPages { get; set; } = AndroidZipAlign.DefaultZipAlignment64Bit;

		public override System.Threading.Tasks.Task RunTaskAsync ()
		{
			return this.WhenAll (GetLinkerConfigs (), RunLinker);
		}

		void RunLinker (Config config)
		{
			var stdout_completed = new ManualResetEvent (false);
			var stderr_completed = new ManualResetEvent (false);
			var psi = new ProcessStartInfo () {
				FileName = config.LinkerPath,
				Arguments = config.LinkerOptions,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
			};

			string targetDir = Path.GetDirectoryName (config.OutputSharedLibrary);
			if (!Directory.Exists (targetDir))
				Directory.CreateDirectory (targetDir);

			string linkerName = Path.GetFileName (config.LinkerPath);
			LogDebugMessage ($"[Native Linker] {psi.FileName} {psi.Arguments}");

			var stdoutLines = new List<string> ();
			var stderrLines = new List<string> ();

			using (var proc = new Process ()) {
				proc.OutputDataReceived += (s, e) => {
					if (e.Data != null) {
						OnOutputData (linkerName, s, e);
						stdoutLines.Add (e.Data);
					} else
						stdout_completed.Set ();
				};

				proc.ErrorDataReceived += (s, e) => {
					if (e.Data != null) {
						OnErrorData (linkerName, s, e);
						stderrLines.Add (e.Data);
					} else
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

				if (proc.ExitCode != 0) {
					var sb = MonoAndroidHelper.MergeStdoutAndStderrMessages (stdoutLines, stderrLines);
					LogCodedError ("XA3007", Properties.Resources.XA3007, Path.GetFileName (config.OutputSharedLibrary), sb.ToString ());
					Cancel ();
				}
			}
		}

		IEnumerable<Config> GetLinkerConfigs ()
		{
			string runtimeNativeLibsDir = MonoAndroidHelper.GetNativeLibsRootDirectoryPath (AndroidBinUtilsDirectory);
			string runtimeNativeLibStubsDir = MonoAndroidHelper.GetLibstubsRootDirectoryPath (AndroidBinUtilsDirectory);
			var abis = new Dictionary <string, InputFiles> (StringComparer.Ordinal);
			ITaskItem[] dsos = ApplicationSharedLibraries;
			foreach (ITaskItem item in dsos) {
				string abi = item.GetMetadata ("abi");
				abis [abi] = GatherFilesForABI (item.ItemSpec, abi, ObjectFiles, runtimeNativeLibsDir, runtimeNativeLibStubsDir);
			}

			const string commonLinkerArgs =
				"--shared " +
				"--allow-shlib-undefined " +
				"--export-dynamic " +
				"-soname libxamarin-app.so " +
				"-z relro " +
				"-z noexecstack " +
				"--enable-new-dtags " +
				"--build-id " +
				"--warn-shared-textrel " +
				"--fatal-warnings";

			string stripSymbolsArg = DebugBuild ? String.Empty : " -s";

			string ld = Path.Combine (AndroidBinUtilsDirectory, MonoAndroidHelper.GetExecutablePath (AndroidBinUtilsDirectory, "ld"));
			var targetLinkerArgs = new List<string> ();
			foreach (var kvp in abis) {
				string abi = kvp.Key;
				InputFiles inputs = kvp.Value;

				targetLinkerArgs.Clear ();
				string elf_arch;
				uint maxPageSize;
				switch (abi) {
					case "armeabi-v7a":
						targetLinkerArgs.Add ("-X");
						elf_arch = "armelf_linux_eabi";
						maxPageSize = MonoAndroidHelper.ZipAlignmentToPageSize (AndroidZipAlign.ZipAlignment32Bit);
						break;

					case "arm64":
					case "arm64-v8a":
					case "aarch64":
						targetLinkerArgs.Add ("--fix-cortex-a53-843419");
						elf_arch = "aarch64linux";
						maxPageSize = MonoAndroidHelper.ZipAlignmentToPageSize (ZipAlignmentPages);
						break;

					case "x86":
						elf_arch = "elf_i386";
						maxPageSize = MonoAndroidHelper.ZipAlignmentToPageSize (AndroidZipAlign.ZipAlignment32Bit);
						break;

					case "x86_64":
						elf_arch = "elf_x86_64";
						maxPageSize = MonoAndroidHelper.ZipAlignmentToPageSize (ZipAlignmentPages);
						break;

					default:
						throw new NotSupportedException ($"Unsupported Android target architecture ABI: {abi}");
				}

				targetLinkerArgs.Add ("-m");
				targetLinkerArgs.Add (elf_arch);

				foreach (string file in inputs.ObjectFiles) {
					targetLinkerArgs.Add (QuoteFileName (file));
				}

				targetLinkerArgs.Add ("-o");
				targetLinkerArgs.Add (QuoteFileName (inputs.OutputSharedLibrary));

				if (inputs.ExtraLibraries != null) {
					foreach (string lib in inputs.ExtraLibraries) {
						targetLinkerArgs.Add (lib);
					}
				}

				targetLinkerArgs.Add ("-z");
				targetLinkerArgs.Add ($"max-page-size={maxPageSize}");

				string targetArgs = String.Join (" ", targetLinkerArgs);
				yield return new Config {
					LinkerPath = ld,
					LinkerOptions = $"{commonLinkerArgs}{stripSymbolsArg} {targetArgs}",
					OutputSharedLibrary = inputs.OutputSharedLibrary,
				};
			}
		}

		InputFiles GatherFilesForABI (string runtimeSharedLibrary, string abi, ITaskItem[] objectFiles, string runtimeNativeLibsDir, string runtimeNativeLibStubsDir)
		{
			List<string> extraLibraries = null;
			string RID = MonoAndroidHelper.AbiToRid (abi);
			AndroidTargetArch targetArch = MonoAndroidHelper.AbiToTargetArch (abi);
			string libStubsPath = Path.Combine (runtimeNativeLibStubsDir, RID);
			string runtimeLibsDir = Path.Combine (runtimeNativeLibsDir, RID);

			extraLibraries = new List<string> {
				$"-L \"{runtimeLibsDir}\"",
				$"-L \"{libStubsPath}\"",
				"-lc",
			};

			return new InputFiles {
				OutputSharedLibrary = runtimeSharedLibrary,
				ObjectFiles = GetItemsForABI (abi, objectFiles),
				ExtraLibraries = extraLibraries,
			};
		}

		List<string> GetItemsForABI (string abi, ITaskItem[] items)
		{
			var ret = new List <string> ();
			foreach (ITaskItem item in items) {
				if (String.Compare (abi, item.GetMetadata ("abi"), StringComparison.Ordinal) != 0)
					continue;
				ret.Add (item.ItemSpec);
			}

			return ret;
		}

		void OnOutputData (string linkerName, object sender, DataReceivedEventArgs e)
		{
			if (e.Data != null)
				LogMessage ($"[{linkerName} stdout] {e.Data}");
		}

		void OnErrorData (string linkerName, object sender, DataReceivedEventArgs e)
		{
			if (e.Data != null)
				LogMessage ($"[{linkerName} stderr] {e.Data}");
		}

		static string QuoteFileName (string fileName)
		{
			var builder = new CommandLineBuilder ();
			builder.AppendFileNameIfNotNull (fileName);
			return builder.ToString ();
		}
	}
}
