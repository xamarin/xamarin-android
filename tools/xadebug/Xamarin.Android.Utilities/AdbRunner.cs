using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Xamarin.Android.Utilities;

class AdbRunner2 : ToolRunner2
{
	public delegate bool OutputLineFilter (bool isStdErr, string line);

	sealed class CaptureOutputState
	{
		public OutputLineFilter? LineFilter;
		public CaptureProcessOutputLogger? Logger;
	}

	sealed class CaptureProcessOutputLogger : IProcessOutputLogger
	{
		IProcessOutputLogger? wrappedLogger;
		OutputLineFilter? lineFilter;
		List<string> lines;
		string? stderrPrefix;
		string? stdoutPrefix;

		public List<string> Lines => lines;

		public IProcessOutputLogger? WrappedLogger => wrappedLogger;

		public string? StdoutPrefix {
			get => stdoutPrefix ?? wrappedLogger?.StdoutPrefix ?? String.Empty;
			set => stdoutPrefix = value;
		}

		public string? StderrPrefix {
			get => stderrPrefix ?? wrappedLogger?.StderrPrefix ?? String.Empty;
			set => stderrPrefix = value;
		}

		public CaptureProcessOutputLogger (IProcessOutputLogger? wrappedLogger, OutputLineFilter? lineFilter = null)
		{
			this.wrappedLogger = wrappedLogger;
			this.lineFilter = lineFilter;

			lines = new List<string> ();
		}

		public void WriteStderr (string text, bool writeLine = true)
		{
			if (LineFiltered (text, isStdError: true)) {
				return;
			}

			wrappedLogger?.WriteStderr (text, writeLine);
		}

		public void WriteStdout (string text, bool writeLine = true)
		{
			if (LineFiltered (text, isStdError: false)) {
				return;
			}

			lines.Add (text);
		}

		bool LineFiltered (string text, bool isStdError)
		{
			if (lineFilter == null) {
				return false;
			}

			return lineFilter (isStdError, text);
		}
	}

	string[]? initialParams;

	public AdbRunner2 (ILogger logger, IProcessOutputLogger processOutputLogger, string adbPath, string? deviceSerial = null)
		: base (adbPath, logger, processOutputLogger)
	{
		if (!String.IsNullOrEmpty (deviceSerial)) {
			initialParams = new string[] { "-s", deviceSerial };
		}
	}

	public async Task<bool> Pull (string remotePath, string localPath)
	{
		var runner = CreateAdbRunner ();
		runner.AddArgument ("pull");
		runner.AddArgument (remotePath);
		runner.AddArgument (localPath);

		return await RunAdbAsync (runner);
	}

	public async Task<bool> Push (string localPath, string remotePath)
	{
		var runner = CreateAdbRunner ();
		runner.AddArgument ("push");
		runner.AddArgument (localPath);
		runner.AddArgument (remotePath);

		return await RunAdbAsync (runner);
	}

	public async Task<bool> Install (string apkPath, bool apkIsDebuggable = false, bool replaceExisting = true, bool noStreaming = true)
	{
		var runner = CreateAdbRunner ();
		runner.AddArgument ("install");

		if (replaceExisting) {
			runner.AddArgument ("-r");
		}

		if (apkIsDebuggable) {
			runner.AddArgument ("-d"); // Allow version code downgrade
		}

		if (noStreaming) {
			runner.AddArgument ("--no-streaming");
		}

		runner.AddQuotedArgument (apkPath);

		return await RunAdbAsync (runner);
	}

	public async Task<(bool success, string output)> GetAppDataDirectory (string packageName)
	{
		return await RunAs (packageName, "/system/bin/sh", "-c", "pwd");
	}

	public async Task<(bool success, string output)> CreateDirectoryAs (string packageName, string directoryPath)
	{
		return await RunAs (packageName, "mkdir", "-p", directoryPath);
	}

	public async Task<(bool success, string output)> GetPropertyValue (string propertyName)
	{
		var runner = CreateAdbRunner ();
		return await Shell ("getprop", propertyName);
	}

	public async Task<(bool success, string output)> RunAs (string packageName, string command, params string[] args)
	{
		return await Shell (
			"run-as",
			RunAsPrepareArgs (packageName, command, args),
			lineFilter: null
		);
	}

	public void RunAsInBackground (BackgroundProcessManager processManager, string packageName, string command, params string[] args)
	{
		RunAsInBackground (processManager, null, null, packageName, command, args);
	}

	public void RunAsInBackground (BackgroundProcessManager processManager, ProcessRunner2.BackgroundActionCompletionHandler? completionHandler, string packageName, string command, params string[] args)
	{
		RunAsInBackground (processManager, completionHandler, null, packageName, command, args);
	}

	public void RunAsInBackground (BackgroundProcessManager processManager, ProcessRunner2.BackgroundActionCompletionHandler? completionHandler, TimeSpan? processTimeout, string packageName, string command, params string[] args)
	{
		ShellInBackground (
			processManager,
			completionHandler,
			processTimeout,
			"run-as",
			RunAsPrepareArgs (packageName, command, args),
			lineFilter: null
		);
	}

	IEnumerable<string> RunAsPrepareArgs (string packageName, string command, params string[] args)
	{
		if (String.IsNullOrEmpty (packageName)) {
			throw new ArgumentException ("must not be null or empty", nameof (packageName));
		}

		var shellArgs = new List<string> {
			packageName,
			command,
		};

		if (args != null && args.Length > 0) {
			shellArgs.AddRange (args);
		}

		return shellArgs;
	}

	public async Task<(bool success, string output)> Shell (string command, List<string> args, OutputLineFilter? lineFilter = null)
	{
		return await Shell (command, (IEnumerable<string>)args, lineFilter);
	}

	public async Task<(bool success, string output)> Shell (string command, params string[] args)
	{
		return await Shell (command, (IEnumerable<string>)args, lineFilter: null);
	}

	public async Task<(bool success, string output)> Shell (OutputLineFilter lineFilter, string command, params string[] args)
	{
		return await Shell (command, (IEnumerable<string>)args, lineFilter);
	}

	async Task<(bool success, string output)> Shell (string command, IEnumerable<string>? args, OutputLineFilter? lineFilter)
	{
		if (String.IsNullOrEmpty (command)) {
			throw new ArgumentException ("must not be null or empty", nameof (command));
		}

		var captureState = new CaptureOutputState {
			LineFilter = lineFilter,
		};

		var runner = CreateAdbRunner (captureState);

		runner.AddArgument ("shell");
		runner.AddArgument (command);
		runner.AddArguments (args);

		return await CaptureAdbOutput (runner, captureState);
	}

	void ShellInBackground (BackgroundProcessManager processManager, ProcessRunner2.BackgroundActionCompletionHandler? completionHandler, TimeSpan? processTimeout,
	                        string command, IEnumerable<string>? args, OutputLineFilter? lineFilter)
	{
	}

	async Task<bool> RunAdbAsync (ProcessRunner2 runner)
	{
		ProcessStatus status = await runner.RunAsync ();
		return status.Success;
	}

	async Task<(bool success, string output)> CaptureAdbOutput (ProcessRunner2 runner, CaptureOutputState captureState)
	{
		ProcessStatus status = await runner.RunAsync ();

		string output = captureState.Logger != null ? String.Join (Environment.NewLine, captureState.Logger.Lines) : String.Empty;
		return (status.Success, output);
	}

	ProcessRunner2 CreateAdbRunner (CaptureOutputState? state = null) => InitProcessRunner (state, initialParams);

	protected override ProcessRunner2 CreateProcessRunner (IProcessOutputLogger consoleProcessLogger, object? state, params string?[]? initialParams)
	{
		IProcessOutputLogger outputLogger;

		if (state is CaptureOutputState captureState) {
			captureState.Logger = new CaptureProcessOutputLogger (consoleProcessLogger, captureState.LineFilter);
			outputLogger = captureState.Logger;
		} else {
			outputLogger = consoleProcessLogger;
		}

		outputLogger.StderrPrefix = "adb> ";
		ProcessRunner2 ret = base.CreateProcessRunner (outputLogger, initialParams);

		// Let's make sure all the messages we get are in English, since we need to parse some of them to detect problems
		ret.Environment["LANG"] = "C";
		return ret;
	}
}
