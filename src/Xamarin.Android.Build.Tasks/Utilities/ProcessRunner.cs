using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

#if NO_MSBUILD
using LoggerType = Xamarin.Android.Utilities.XamarinLoggingHelper;
#else
using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Utilities;

using LoggerType = Microsoft.Build.Utilities.TaskLoggingHelper;
#endif

namespace Xamarin.Android.Tasks
{
	class ProcessRunner
	{
		public const string StdoutSeverityName = "stdout | ";
		public const string StderrSeverityName = "stderr | ";

		public enum ErrorReasonCode
		{
			NotExecutedYet,
			NoError,
			CommandNotFound,
			ExecutionTimedOut,
			ExitCodeNotZero,
		};

		static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromMinutes (5);
		static readonly TimeSpan DefaultOutputTimeout = TimeSpan.FromSeconds (10);

		sealed class WriterGuard
		{
			public readonly object WriteLock = new object ();
			public readonly TextWriter Writer;

			public WriterGuard (TextWriter writer)
			{
				Writer = writer;
			}
		}

		string command;
		List<string>? arguments;
		List<WriterGuard>? stderrSinks;
		List<WriterGuard>? stdoutSinks;
		Dictionary<TextWriter, WriterGuard>? guardCache;
		bool defaultStdoutEchoWrapperAdded;
		ProcessStandardStreamWrapper? defaultStderrEchoWrapper;
		LoggerType log;

		public string Command => command;

		public string Arguments {
			get {
				if (arguments == null)
					return String.Empty;
				return String.Join (" ", arguments);
			}
		}

		public string FullCommandLine {
			get {
				string args = Arguments;
				if (String.IsNullOrEmpty (args))
					return command;
				return $"{command} {args}";
			}
		}

		public Dictionary <string, string> Environment                       { get; } = new Dictionary <string, string> (StringComparer.Ordinal);
		public int ExitCode                                                  { get; private set; } = -1;
		public ErrorReasonCode ErrorReason                                   { get; private set; } = ErrorReasonCode.NotExecutedYet;
		public bool EchoCmdAndArguments                                      { get; set; } = true;
		public bool EchoStandardOutput                                       { get; set; }
		public ProcessStandardStreamWrapper.LogLevel EchoStandardOutputLevel { get; set; } = ProcessStandardStreamWrapper.LogLevel.Message;
		public bool EchoStandardError                                        { get; set; }
		public ProcessStandardStreamWrapper.LogLevel EchoStandardErrorLevel  { get; set; } = ProcessStandardStreamWrapper.LogLevel.Error;
		public ProcessStandardStreamWrapper? StandardOutputEchoWrapper       { get; set; }
		public ProcessStandardStreamWrapper? StandardErrorEchoWrapper        { get; set; }
		public Encoding StandardOutputEncoding                               { get; set; } = Encoding.Default;
		public Encoding StandardErrorEncoding                                { get; set; } = Encoding.Default;
		public TimeSpan StandardOutputTimeout                                { get; set; } = DefaultOutputTimeout;
		public TimeSpan StandardErrorTimeout                                 { get; set; } = DefaultOutputTimeout;
		public TimeSpan ProcessTimeout                                       { get; set; } = DefaultProcessTimeout;
		public string? WorkingDirectory                                      { get; set; }
		public Action<ProcessStartInfo>? StartInfoCallback                   { get; set; }

		public ProcessRunner (LoggerType logger, string command, params string?[] arguments)
			: this (logger, command, false, arguments)
		{}

		public ProcessRunner (LoggerType logger, string command, bool ignoreEmptyArguments, params string?[] arguments)
		{
			if (String.IsNullOrEmpty (command)) {
				throw new ArgumentException ("must not be null or empty", nameof (command));
			}

			log = logger;
			this.command = command;
			AddArgumentsInternal (ignoreEmptyArguments, arguments);
		}

		public ProcessRunner ClearArguments ()
		{
			arguments?.Clear ();
			return this;
		}

		public ProcessRunner ClearOutputSinks ()
		{
			stderrSinks?.Clear ();
			stdoutSinks?.Clear ();
			return this;
		}

		public ProcessRunner AddArguments (params string?[]? arguments)
		{
			return AddArguments (true, arguments);
		}

		public ProcessRunner AddArguments (bool ignoreEmptyArguments, params string?[]? arguments)
		{
			AddArgumentsInternal (ignoreEmptyArguments, arguments);
			return this;
		}

		void AddArgumentsInternal (bool ignoreEmptyArguments, params string?[]? arguments)
		{
			if (arguments == null || arguments.Length == 0) {
				return;
			}

			for (int i = 0; i < arguments.Length; i++) {
				string? argument = arguments [i]?.Trim ();
				if (String.IsNullOrEmpty (argument)) {
					if (ignoreEmptyArguments) {
						continue;
					}
					throw new InvalidOperationException ($"Argument {i} is null or empty");
				}

				AddQuotedArgument (argument!);
			}
		}

		public ProcessRunner AddArgument (string argument)
		{
			if (String.IsNullOrEmpty (argument)) {
				throw new ArgumentException ("must not be null or empty", nameof (argument));
			}

			AddToList (argument, ref arguments);
			return this;
		}

		public ProcessRunner AddQuotedArgument (string argument)
		{
			if (String.IsNullOrEmpty (argument)) {
				throw new ArgumentException ("must not be null or empty", nameof (argument));
			}

			return AddArgument (QuoteArgument (argument));
		}

		public static string QuoteArgument (string argument)
		{
			if (String.IsNullOrEmpty (argument)) {
				return String.Empty;
			}

			if (argument.IndexOf ('"') >= 0) {
				argument = argument.Replace ("\"", "\\\"");
			}

			return $"\"{argument}\"";
		}

		public ProcessRunner AddStandardErrorSink (TextWriter writer)
		{
			if (writer == null) {
				throw new ArgumentNullException (nameof (writer));
			}

			AddToList (GetGuard (writer), ref stderrSinks);
			return this;
		}

		public ProcessRunner AddStandardOutputSink (TextWriter writer)
		{
			if (writer == null) {
				throw new ArgumentNullException (nameof (writer));
			}

			AddToList (GetGuard (writer), ref stdoutSinks);
			return this;
		}

		WriterGuard GetGuard (TextWriter writer)
		{
			if (guardCache == null)
				guardCache = new Dictionary<TextWriter, WriterGuard> ();

			if (guardCache.TryGetValue (writer, out WriterGuard? ret) && ret != null)
				return ret;

			ret = new WriterGuard (writer);
			guardCache.Add (writer, ret);
			return ret;
		}

		public bool Run ()
		{
			if (EchoStandardOutput) {
				if (StandardOutputEchoWrapper != null) {
					AddStandardOutputSink (StandardOutputEchoWrapper);
				} else if (!defaultStdoutEchoWrapperAdded) {
					AddStandardOutputSink (new ProcessStandardStreamWrapper (log) { LoggingLevel = EchoStandardOutputLevel, LogPrefix = StdoutSeverityName });
					defaultStdoutEchoWrapperAdded = true;
				}
			}

			if (EchoStandardError) {
				if (StandardErrorEchoWrapper != null) {
					AddStandardErrorSink (StandardErrorEchoWrapper);
				} else if (defaultStderrEchoWrapper == null) {
					defaultStderrEchoWrapper = new ProcessStandardStreamWrapper (log) { LoggingLevel = EchoStandardErrorLevel, LogPrefix = StderrSeverityName };
					AddStandardErrorSink (defaultStderrEchoWrapper);
				}
			}

			ManualResetEventSlim? stdout_done = null;
			ManualResetEventSlim? stderr_done = null;

			if (stderrSinks != null && stderrSinks.Count > 0) {
				stderr_done = new ManualResetEventSlim (false);
			}

			if (stdoutSinks != null && stdoutSinks.Count > 0) {
				stdout_done = new ManualResetEventSlim (false);
			}

			var psi = new ProcessStartInfo (command) {
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				RedirectStandardError = stderr_done != null,
				RedirectStandardOutput = stdout_done != null,
			};

			if (Environment.Count > 0) {
				foreach (var kvp in Environment) {
					psi.Environment.Add (kvp.Key, kvp.Value);
				}
			}

			if (arguments != null) {
				psi.Arguments = String.Join (" ", arguments);
			}

			if (!String.IsNullOrEmpty (WorkingDirectory)) {
				psi.WorkingDirectory = WorkingDirectory;
			}

			if (psi.RedirectStandardError) {
				StandardErrorEncoding = StandardErrorEncoding;
			}

			if (psi.RedirectStandardOutput) {
				StandardOutputEncoding = StandardOutputEncoding;
			}

			if (StartInfoCallback != null) {
				StartInfoCallback (psi);
			}

			var process = new Process {
				StartInfo = psi
			};

			if (EchoCmdAndArguments) {
				log.LogDebugMessage ($"Running: {FullCommandLine}");
			}

			try {
				process.Start ();
			} catch (System.ComponentModel.Win32Exception ex) {
				log.LogError ($"Process failed to start: {ex.Message}");
				log.LogDebugMessage (ex.ToString ());

				ErrorReason = ErrorReasonCode.CommandNotFound;
				return false;
			}

			if (psi.RedirectStandardError) {
				process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) => {
					if (e.Data != null) {
						WriteOutput (e.Data, stderrSinks!);
					} else {
						stderr_done!.Set ();
					}
				};
				process.BeginErrorReadLine ();
			}

			if (psi.RedirectStandardOutput) {
				process.OutputDataReceived += (object sender, DataReceivedEventArgs e) => {
					if (e.Data != null) {
						WriteOutput (e.Data, stdoutSinks!);
					} else {
						stdout_done!.Set ();
					}
				};
				process.BeginOutputReadLine ();
			}

			int timeout = ProcessTimeout == TimeSpan.MaxValue ? -1 : (int)ProcessTimeout.TotalMilliseconds;
			bool exited = process.WaitForExit (timeout);
			if (!exited) {
				log.LogError ($"Process '{FullCommandLine}' timed out after {ProcessTimeout}");
				ErrorReason = ErrorReasonCode.ExecutionTimedOut;
				process.Kill ();
			}

			// See: https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexit?view=netframework-4.7.2#System_Diagnostics_Process_WaitForExit)
			if (psi.RedirectStandardError || psi.RedirectStandardOutput) {
				process.WaitForExit ();
			}

			if (stderr_done != null) {
				stderr_done.Wait (StandardErrorTimeout);
			}

			if (stdout_done != null) {
				stdout_done.Wait (StandardOutputTimeout);
			}

			ExitCode = process.ExitCode;
			if (ExitCode != 0 && ErrorReason == ErrorReasonCode.NotExecutedYet) {
				ErrorReason = ErrorReasonCode.ExitCodeNotZero;
				return false;
			}

			if (exited) {
				ErrorReason = ErrorReasonCode.NoError;
			}

			return exited;
		}

		void WriteOutput (string data, List<WriterGuard> sinks)
		{
			foreach (WriterGuard wg in sinks) {
				if (wg == null || wg.Writer == null)
					continue;

				lock (wg.WriteLock) {
					wg.Writer.WriteLine (data);
				}
			}
		}

		void AddToList <T> (T item, ref List<T>? list)
		{
			if (list == null)
				list = new List <T> ();
			list.Add (item);
		}
	}
}
