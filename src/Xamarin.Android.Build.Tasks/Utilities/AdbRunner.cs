using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

#if NO_MSBUILD
using LoggerType = Xamarin.Android.Utilities.XamarinLoggingHelper;
#else // def NO_MSBUILD
using LoggerType = Microsoft.Build.Utilities.TaskLoggingHelper;
#endif // ndef NO_MSBUILD

namespace Xamarin.Android.Tasks
{
	class AdbRunner : ToolRunner
	{
		public delegate bool OutputLineFilter (bool isStdErr, string line);

		class AdbOutputSink : ToolOutputSink
		{
			bool isStdError;
			OutputLineFilter? lineFilter;

			public Action<bool, string>? LineCallback { get; set; }

			public AdbOutputSink (LoggerType logger, bool isStdError, OutputLineFilter? lineFilter)
				: base (logger)
			{
				this.isStdError = isStdError;
				this.lineFilter = lineFilter;

				LogLinePrefix = "adb";
			}

			public override void WriteLine (string? value)
			{
				if (lineFilter != null && lineFilter (isStdError, value ?? String.Empty)) {
					return;
				}

				base.WriteLine (value);
				LineCallback?.Invoke (isStdError, value ?? String.Empty);
			}
		}

		class AdbStdErrorWrapper : ProcessStandardStreamWrapper
		{
			OutputLineFilter? lineFilter;

			public AdbStdErrorWrapper (LoggerType logger, OutputLineFilter? lineFilter)
				: base (logger)
			{
				this.lineFilter = lineFilter;
			}

			protected override string? PreprocessMessage (string? message, out bool ignoreLine)
			{
				if (lineFilter == null) {
					return base.PreprocessMessage (message, out ignoreLine);
				}

				ignoreLine = lineFilter (isStdErr: true, line: message ?? String.Empty);
				return message;
			}
		}

		string[]? initialParams;

		public int ExitCode { get; private set; }

		public AdbRunner (LoggerType logger, string adbPath, string? deviceSerial = null)
			: base (logger, adbPath)
		{
			if (!String.IsNullOrEmpty (deviceSerial)) {
				initialParams = new string[] { "-s", deviceSerial };
			}
		}

		public async Task<(bool success, string output)> Forward (string local, string remote)
		{
			var runner = CreateAdbRunner ();
			runner.AddArgument ("forward");
			runner.AddArgument (local);
			runner.AddArgument (remote);

			return await CaptureAdbOutput (runner);
		}

		public async Task<bool> Pull (string remotePath, string localPath)
		{
			var runner = CreateAdbRunner ();
			runner.AddArgument ("pull");
			runner.AddArgument (remotePath);
			runner.AddArgument (localPath);

			return await RunAdb (runner);
		}

		public async Task<bool> Push (string localPath, string remotePath)
		{
			var runner = CreateAdbRunner ();
			runner.AddArgument ("push");
			runner.AddArgument (localPath);
			runner.AddArgument (remotePath);

			return await RunAdb (runner);
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

			return await RunAdb (runner);
		}

		public async Task<(bool success, string output)> CreateDirectoryAs (string packageName, string directoryPath)
		{
			return await RunAs (packageName, "mkdir", "-p", directoryPath);
		}

		public async Task<(bool success, string output)> RunAs (string packageName, string command, params string[] args)
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

			return await Shell ("run-as", (IEnumerable<string>)shellArgs, lineFilter: null);
		}

		public async Task<(bool success, string output)> GetAppDataDirectory (string packageName)
		{
			return await RunAs (packageName, "/system/bin/sh", "-c", "pwd");
		}

		public async Task<(bool success, string output)> Shell (string command, List<string> args, OutputLineFilter? lineFilter = null)
		{
			return await Shell (command, (IEnumerable<string>)args, lineFilter: null);
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

			var runner = CreateAdbRunner ();

			runner.AddArgument ("shell");
			runner.AddArgument (command);
			if (args != null) {
				foreach (string arg in args) {
					runner.AddArgument (arg);
				}
			}

			return await CaptureAdbOutput (runner, lineFilter);
		}

		public async Task<(bool success, string output)> GetPropertyValue (string propertyName)
		{
			var runner = CreateAdbRunner ();
			return await GetPropertyValue (runner, propertyName);
		}

		async Task<(bool success, string output)> GetPropertyValue (ProcessRunner runner, string propertyName)
		{
			runner.ClearArguments ();
			runner.ClearOutputSinks ();
			return await Shell ("getprop", propertyName);
		}

		async Task<(bool success, string output)> CaptureAdbOutput (ProcessRunner runner, OutputLineFilter? lineFilter = null, bool firstLineOnly = false)
                {
                        string? outputLine = null;
                        List<string>? lines = null;

                        using AdbOutputSink? stderrSink = lineFilter != null ? new AdbOutputSink (Logger, isStdError: true, lineFilter: lineFilter) : null;
                        using var stdoutSink = new AdbOutputSink (Logger, isStdError: false, lineFilter: lineFilter);

                        SetupOutputSinks (runner, stdoutSink, stderrSink, ignoreStderr: stderrSink == null);
                        stdoutSink.LineCallback = (bool isStdErr, string line) => {
	                        if (firstLineOnly) {
		                        if (outputLine != null) {
			                        return;
		                        }
		                        outputLine = line.Trim ();
		                        return;
	                        }

	                        if (lines == null) {
		                        lines = new List<string> ();
	                        }
	                        lines.Add (line.Trim ());
                        };

                        ProcessStandardStreamWrapper? origStderrWrapper = runner.StandardErrorEchoWrapper;
                        using AdbStdErrorWrapper? stderrWrapper = lineFilter != null ? new AdbStdErrorWrapper (Logger, lineFilter) : null;

                        try {
	                        runner.StandardErrorEchoWrapper = stderrWrapper;
	                        if (!await RunAdb (runner, setupOutputSink: false)) {
		                        return (false, FormatOutputWithLines (lines));
	                        }
                        } finally {
	                        runner.StandardErrorEchoWrapper = origStderrWrapper;
                        }

                        if (firstLineOnly) {
                                return (true, outputLine ?? String.Empty);
                        }

                        return (true, FormatOutputWithLines (lines));

                        string FormatOutputWithLines (List<string>? lines) => lines != null ? String.Join (Environment.NewLine, lines) : String.Empty;
                }

		async Task<bool> RunAdb (ProcessRunner runner, bool setupOutputSink = true, bool ignoreStderr = true)
		{
			return await RunTool (
				() => {
					TextWriter? stdoutSink = null;
					TextWriter? stderrSink = null;
					if (setupOutputSink) {
						stdoutSink = new AdbOutputSink (Logger, isStdError: false, lineFilter: null);
						if (!ignoreStderr) {
							stderrSink = new AdbOutputSink (Logger, isStdError: true, lineFilter: null);
						}

						SetupOutputSinks (runner, stdoutSink, stderrSink, ignoreStderr);
					}

					try {
						bool ret = runner.Run ();
						ExitCode = runner.ExitCode;
						return ret;
					} catch {
						ExitCode = -0xDEAD;
						throw;
					} finally {
						stdoutSink?.Dispose ();
						stderrSink?.Dispose ();
					}
				}
			);
		}

		ProcessRunner CreateAdbRunner ()
		{
			ProcessRunner ret = CreateProcessRunner (initialParams);

			// Let's make sure all the messages we get are in English, since we need to parse some of them to detect problems
			ret.Environment["LANG"] = "C";
			return ret;
		}
	}
}
