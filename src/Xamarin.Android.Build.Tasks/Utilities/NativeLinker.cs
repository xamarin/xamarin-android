using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks;

class NativeLinker
{
	static readonly List<string> standardArgs = new () {
		"--shared",
		"--as-needed",
		"--allow-shlib-undefined",
		"--compress-debug-sections",
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

	public bool StripDebugSymbols { get; set; }
	public bool SaveDebugSymbols  { get; set; }

	public NativeLinker (TaskLoggingHelper log, string abi, string soname, string binutilsDir, string intermediateDir)
	{
		this.log = log;
		this.abi = abi;
		this.intermediateDir = intermediateDir;

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
	}

	public void Link (ITaskItem outputLibraryPath, List<ITaskItem> objectFiles, List<ITaskItem> archives, List<ITaskItem> libraries)
	{
		log.LogDebugMessage ($"Linking: {outputLibraryPath}");
		EnsureCorrectAbi (outputLibraryPath);
		EnsureCorrectAbi (objectFiles);
		EnsureCorrectAbi (archives);
		EnsureCorrectAbi (libraries);

		string respFilePath = Path.Combine (intermediateDir, $"ld.{abi}.rsp");
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

			sw.Write ("-o ");
			sw.WriteLine (MonoAndroidHelper.QuoteFileNameArgument (outputLibraryPath.ItemSpec));

			WriteFilesToResponseFile (sw, objectFiles);
			WriteFilesToResponseFile (sw, archives);

			sw.Flush ();
		}

		log.LogDebugMessage ($"  Command line: {ld} @{respFilePath}");

		void WriteFilesToResponseFile (StreamWriter sw, List<ITaskItem> files)
		{
			foreach (ITaskItem file in files) {
				sw.WriteLine (MonoAndroidHelper.QuoteFileNameArgument (file.ItemSpec));
			}
		}
	}

	void EnsureCorrectAbi (ITaskItem item)
	{
		// The exception is just a precaution, since the items passed to us should have already been checked
		string itemAbi = item.GetMetadata ("Abi") ?? throw new InvalidOperationException ($"Internal error: 'Abi' metadata not found in item '{item}'");
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
}
