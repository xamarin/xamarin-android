using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks;

class NativeLinker
{
	static readonly List<string> standardArgs = new () {
		"--shared",
		"--allow-shlib-undefined",
		// TODO: need to enable zstd in binutils build
		// "--compress-debug-sections=zstd",
		// TODO: test the commented-out flags
		// "--gc-sections",
		// "--icf=safe",
		// "--lto=full|thin",
		"--export-dynamic",
		"-z relro",
		"-z noexecstack",
		"--enable-new-dtags",
		"--build-id",
		"--warn-shared-textrel",
		"--fatal-warnings"
	};

	readonly List<string> extraArgs = new ();
	readonly TaskLoggingHelper log;
	readonly string abi;
	readonly string ld;
	readonly string intermediateDir;
	readonly CancellationToken? cancellationToken;
	readonly Action? cancelTask;

	public bool StripDebugSymbols { get; set; }
	public bool SaveDebugSymbols  { get; set; }

	public NativeLinker (TaskLoggingHelper log, string abi, string soname, string binutilsDir, string intermediateDir,
	                     CancellationToken? cancellationToken = null, Action? cancelTask = null)
	{
		this.log = log;
		this.abi = abi;
		this.intermediateDir = intermediateDir;
		this.cancellationToken = cancellationToken;
		this.cancelTask = cancelTask;

		ld = Path.Combine (binutilsDir, MonoAndroidHelper.GetExecutablePath (binutilsDir, "ld"));

		extraArgs.Add ($"-soname {soname}");

		string? elfArch = null;
		switch (abi) {
			case "armeabi-v7a":
				extraArgs.Add ("-X");
				elfArch = "armelf_linux_eabi";
				break;

			case "arm64":
			case "arm64-v8a":
			case "aarch64":
				extraArgs.Add ("--fix-cortex-a53-843419");
				elfArch = "aarch64linux";
				break;

			case "x86":
				elfArch = "elf_i386";
				break;

			case "x86_64":
				elfArch = "elf_x86_64";
				break;

			default:
				throw new NotSupportedException ($"Unsupported Android target architecture ABI: {abi}");
		}

		if (!String.IsNullOrEmpty (elfArch)) {
			extraArgs.Add ($"-m {elfArch}");
		}

		string runtimeNativeLibsDir = MonoAndroidHelper.GetNativeLibsRootDirectoryPath (binutilsDir);
		string runtimeNativeLibStubsDir = MonoAndroidHelper.GetLibstubsRootDirectoryPath (binutilsDir);
		string RID = MonoAndroidHelper.AbiToRid (abi);
		string libStubsPath = Path.Combine (runtimeNativeLibStubsDir, RID);
		string runtimeLibsDir = Path.Combine (runtimeNativeLibsDir, RID);

		extraArgs.Add ($"-L {MonoAndroidHelper.QuoteFileNameArgument (libStubsPath)}");
		extraArgs.Add ($"-L {MonoAndroidHelper.QuoteFileNameArgument (runtimeLibsDir)}");
	}

	public bool Link (ITaskItem outputLibraryPath, List<ITaskItem> objectFiles, List<ITaskItem> archives, List<ITaskItem> libraries)
	{
		log.LogDebugMessage ($"Linking: {outputLibraryPath}");
		EnsureCorrectAbi (outputLibraryPath);
		EnsureCorrectAbi (objectFiles);
		EnsureCorrectAbi (archives);
		EnsureCorrectAbi (libraries);

		Directory.CreateDirectory (Path.GetDirectoryName (outputLibraryPath.ItemSpec));

		string libBaseName = Path.GetFileNameWithoutExtension (outputLibraryPath.ItemSpec);
		string respFilePath = Path.Combine (intermediateDir, $"ld.{libBaseName}.{abi}.rsp");
		using (var sw = new StreamWriter (File.Open (respFilePath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding (false))) {
			foreach (string arg in standardArgs) {
				sw.WriteLine (arg);
			}

			foreach (string arg in extraArgs) {
				sw.WriteLine (arg);
			}

			if (StripDebugSymbols && !SaveDebugSymbols) {
				sw.WriteLine ("-s");
			}

			WriteFilesToResponseFile (sw, objectFiles);
			WriteFilesToResponseFile (sw, archives);

			foreach (ITaskItem libItem in libraries) {
				sw.WriteLine ($"-l{libItem.ItemSpec}");
			}

			sw.Flush ();
		}

		var ldArgs = new List<string> {
			$"@{respFilePath}",
			"-o",
			MonoAndroidHelper.QuoteFileNameArgument (outputLibraryPath.ItemSpec)
		};

		var watch = new Stopwatch ();
		watch.Start ();
		bool ret = RunLinker (ldArgs, outputLibraryPath);
		watch.Stop ();
		log.LogDebugMessage ($"[{Path.GetFileName (outputLibraryPath.ItemSpec)} link time] {watch.Elapsed}");

		return ret;

		void WriteFilesToResponseFile (StreamWriter sw, List<ITaskItem> files)
		{
			foreach (ITaskItem file in files) {
				bool wholeArchive = IncludeWholeArchive (file);

				if (wholeArchive) {
					sw.Write ("--whole-archive ");
				}
				sw.Write (MonoAndroidHelper.QuoteFileNameArgument (file.ItemSpec));
				if (wholeArchive) {
					sw.Write (" --no-whole-archive");
				}
				sw.WriteLine ();
			}
		}

		bool IncludeWholeArchive (ITaskItem item)
		{
			string? wholeArchive = item.GetMetadata (KnownMetadata.LinkWholeArchive);
			if (String.IsNullOrEmpty (wholeArchive)) {
				return false;
			}

			// Purposefully not calling TryParse, let it throw and let us know if the value isn't a boolean.
			return Boolean.Parse (wholeArchive);
		}
	}

	void EnsureCorrectAbi (ITaskItem item)
	{
		// The exception is just a precaution, since the items passed to us should have already been checked
		string itemAbi = item.GetMetadata (KnownMetadata.Abi) ?? throw new InvalidOperationException ($"Internal error: 'Abi' metadata not found in item '{item}'");
		if (String.Compare (abi, itemAbi, StringComparison.OrdinalIgnoreCase) == 0) {
			return;
		}

		throw new InvalidOperationException ($"Internal error: '{item}' ABI ('{itemAbi}') doesn't have the expected value '{abi}'");
	}

	void EnsureCorrectAbi (List<ITaskItem> items)
	{
		foreach (ITaskItem item in items) {
			EnsureCorrectAbi (item);
		}
	}

	bool RunLinker (List<string> args, ITaskItem outputSharedLibrary)
	{
		using var stdout_completed = new ManualResetEvent (false);
		using var stderr_completed = new ManualResetEvent (false);
		var psi = new ProcessStartInfo () {
			FileName = ld,
			Arguments = String.Join (" ", args),
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
		};

		string linkerName = Path.GetFileName (ld);
		log.LogDebugMessage ($"[Native Linker] {psi.FileName} {psi.Arguments}");

		var stdoutLines = new List<string> ();
		var stderrLines = new List<string> ();

		using var proc = new Process ();
		proc.OutputDataReceived += (s, e) => {
			if (e.Data != null) {
				OnOutputData (linkerName, s, e);
				stdoutLines.Add (e.Data);
			} else {
				stdout_completed.Set ();
			}
		};

		proc.ErrorDataReceived += (s, e) => {
			if (e.Data != null) {
				OnErrorData (linkerName, s, e);
				stderrLines.Add (e.Data);
			} else {
				stderr_completed.Set ();
			}
		};

		proc.StartInfo = psi;
		proc.Start ();
		proc.BeginOutputReadLine ();
		proc.BeginErrorReadLine ();
		cancellationToken?.Register (() => { try { proc.Kill (); } catch (Exception) { } });
		proc.WaitForExit ();

		if (psi.RedirectStandardError) {
			stderr_completed.WaitOne (TimeSpan.FromSeconds (30));
		}

		if (psi.RedirectStandardOutput) {
			stdout_completed.WaitOne (TimeSpan.FromSeconds (30));
		}

		if (proc.ExitCode != 0) {
			var sb = MonoAndroidHelper.MergeStdoutAndStderrMessages (stdoutLines, stderrLines);
			log.LogCodedError ("XA3007", Properties.Resources.XA3007, Path.GetFileName (outputSharedLibrary.ItemSpec), sb.ToString ());
			cancelTask?.Invoke ();
			return false;
		}

		return true;
	}

	void OnOutputData (string linkerName, object sender, DataReceivedEventArgs e)
	{
		if (e.Data != null) {
			log.LogMessage ($"[{linkerName} stdout] {e.Data}");
		}
	}

	void OnErrorData (string linkerName, object sender, DataReceivedEventArgs e)
	{
		if (e.Data != null) {
			log.LogMessage ($"[{linkerName} stderr] {e.Data}");
		}
	}
}
