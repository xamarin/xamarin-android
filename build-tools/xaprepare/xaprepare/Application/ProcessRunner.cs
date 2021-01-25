using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Xamarin.Android.Prepare
{
	class ProcessRunner : AppObject
	{
		public const string StdoutSeverityName = "stdout";
		public const string StderrSeverityName = "stderr";

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
		Process? process;

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
		public bool TimedOut                                                 { get; private set; }
		public string? WorkingDirectory                                      { get; set; }
		public Action<ProcessStartInfo>? StartInfoCallback                   { get; set; }

		/// <summary>
		///   Return a process ID. A value is assigned to this property only after <see name="Run"/> was called and
		///   remains the same even after process exits.  Therefore, it is possible that the returned ID will no longer
		///   refer to a valid process or that it will refer to a different process.
		/// </summary>
		public int ProcessId                                                 { get; private set; } = -1;
		public Process? Process                                              => process;

		/// <summary>
		///   <para>
		///   This property exists to work around an issue in the Mono runtime that can cause the entire main process to
		///   hang if we reach a timeout of the process we launched but the child process hasn't exited yet.  In this
		///   case we call process.Kill() which delivers (on Unix) either the <c>SIGTERM</c> or <c>SIGKILL</c> to the
		///   child process and then returns back to the caller.  As per <c>System.Diagnostic.Process</c> documentation,
		///   we then wait for the process to exit AGAIN with infinite wait call to `WaitForExit`.  However, in
		///   some instances this wait will indeed be infinite.
		///   </para>
		///
		///   <para>
		///   The problem was observed when running the <c>NUnit</c> console runner from XAT.  <c>NUnit</c> is launched
		///   by us and it launches its own sub-process, <c>nunit-agent.exe</c>.  We know the PID only of the process we
		///   started, but the agent sub-process is part of the same process group.  Once the timeout is reached, we
		///   kill the main <c>NUnit</c> process and then proceed to waiting for its exit, as described above.  However,
		///   it turns out that the child process of the main <c>NUnit</c> process is <b>not</b> killed.  This is
		///   because of the fact that Mono kills the main process with the code below:
		///   </para>
		///
		///      <code>kill (pid, exitcode == -1 ? SIGKILL : SIGTERM);</code>
		///
		///   <para>
		///   This delivers the signal ONLY to the main process, but NOT to the child processes.  In order to deliver
		///   the signal to the entire process group, the call should negate the PID value.  However, here we encounter
		///   another problem.  The parent process of the <c>NUnit</c> runner, ourselves, is ALSO part of the same
		///   process group.  So attempting to send the signal to the process group will kill us as well, which is not
		///   what we want, obviously.
		///   </para>
		///
		///   <para>
		///   The real fix for this issue is to change Mono runtime to create a separate process group for the processes
		///   it launches, so that it can send the kill signal to that process group without affecting the parent
		///   process.  However, until such time that it is fixed upstream, we need to work around the issue. Note: both
		///   Mono and .NETCore are affected (tested dotnet till v5)
		///   </para>
		///
		///   <para>
		///   Another, theoretical, way to fix this would be to walk the process tree downwards starting from the child
		///   process.  However, this is an inherently non-portable area and it would require implementations specific
		///   to various operating systems (at least Linux, BSD* and macOS) and even then it would require gross hacks.
		///   </para>
		///
		///   <para>
		///   This property allows the caller to implement a specific workaround. It will cause
		///   <c>ProcessRunner</c> to effectively abandon the process that "timed out" and let the caller handle the
		///   situation. This is less than ideal, but there's no way for <c>ProcessRunner</c> to know the structure of
		///   the timed out process and its process group, while the caller should be aware of what's going on and be
		///   able to deal with the situation as it arises.
		///   </para>
		/// </summary>
		public bool DoNotKillOnTimeout                                       { get; set; }

		public ProcessRunner (string command, params string?[] arguments)
			: this (command, false, arguments)
		{}

		public ProcessRunner (string command, bool ignoreEmptyArguments, params string?[] arguments)
		{
			if (String.IsNullOrEmpty (command))
				throw new ArgumentException ("must not be null or empty", nameof (command));

			this.command = command;
			AddArgumentsInternal (ignoreEmptyArguments, arguments);
		}

		public ProcessRunner AddArguments (params string?[] arguments)
		{
			return AddArguments (true, arguments);
		}

		public ProcessRunner AddArguments (bool ignoreEmptyArguments, params string?[] arguments)
		{
			AddArgumentsInternal (ignoreEmptyArguments, arguments);
			return this;
		}

		void AddArgumentsInternal (bool ignoreEmptyArguments, params string?[] arguments)
		{
			if (arguments == null)
				return;

			for (int i = 0; i < arguments.Length; i++) {
				string? argument = arguments [i]?.Trim ();
				if (String.IsNullOrEmpty (argument)) {
					if (ignoreEmptyArguments)
						continue;
					throw new InvalidOperationException ($"Argument {i} is null or empty");
				}

				AddQuotedArgument (argument!);
			}
		}

		public ProcessRunner AddArgument (string argument)
		{
			if (String.IsNullOrEmpty (argument))
				throw new ArgumentException ("must not be null or empty", nameof (argument));

			AddToList (argument, ref arguments);
			return this;
		}

		public ProcessRunner AddQuotedArgument (string argument)
		{
			if (String.IsNullOrEmpty (argument))
				throw new ArgumentException ("must not be null or empty", nameof (argument));

			return AddArgument (QuoteArgument (argument));
		}

		/// <summary>
		///   <paramref name="argument"/> must be formatted exactly as expected by the process.
		///   Value passed in <paramref name="value"/> will be quoted and appended to <paramref name="argument"/>.
		///   Value must not be an empty string.
		/// </summary>
		///
		/// <example>
		///   runner.AddArgumentWithQuotedValue ("--files=", "test1,test2");
		/// </example>
		public ProcessRunner AddArgumentWithQuotedValue (string argument, string value)
		{
			if (String.IsNullOrEmpty (argument)) {
				throw new ArgumentException ("must not be null or empty", nameof (argument));
			}

			if (String.IsNullOrEmpty (value)) {
				throw new ArgumentException ("must not be null or empty", nameof (value));
			}

			return AddArgument ($"{argument}{QuoteArgument (value)}");
		}

		public static string QuoteArgument (string argument)
		{
			if (String.IsNullOrEmpty (argument))
				return String.Empty;

			if (argument.IndexOf ('"') >= 0)
				argument = argument.Replace ("\"", "\\\"");

			return $"\"{argument}\"";
		}

		public ProcessRunner AddStandardErrorSink (TextWriter writer)
		{
			if (writer == null)
				throw new ArgumentNullException (nameof (writer));

			AddToList (GetGuard (writer), ref stderrSinks);
			return this;
		}

		public ProcessRunner AddStandardOutputSink (TextWriter writer)
		{
			if (writer == null)
				throw new ArgumentNullException (nameof (writer));

			AddToList (GetGuard (writer), ref stdoutSinks);
			return this;
		}

		/// <summary>
		///   Some programs will not write errors to stderr but we might want to "redirect" them to the error stream for
		///   console reporting, this is the purpose of this method. Not that this method bypassess stderr sinks other
		///   than the default one (created when <see cref="EchoStandardOutput"/> is set to <c>true</c>) or the one
		///   specified by the caller by setting the <see cref="StandardErrorEchoWrapper" />.
		/// </summary>
		public void WriteStderrLine (string line)
		{
			if (StandardErrorEchoWrapper != null) {
				StandardErrorEchoWrapper.WriteLine (line);
				return;
			}

			defaultStderrEchoWrapper?.WriteLine (line ?? String.Empty, stderrSinks);
		}

		WriterGuard GetGuard (TextWriter writer)
		{
			if (guardCache == null)
				guardCache = new Dictionary<TextWriter, WriterGuard> ();

			if (guardCache.TryGetValue (writer, out WriterGuard ret) && ret != null)
				return ret;

			ret = new WriterGuard (writer);
			guardCache.Add (writer, ret);
			return ret;
		}

		public bool Run (bool fireAndForget = false)
		{
			TimedOut = false;
			if (EchoStandardOutput) {
				if (StandardOutputEchoWrapper != null) {
					AddStandardOutputSink (StandardOutputEchoWrapper);
				} else if (!defaultStdoutEchoWrapperAdded) {
					AddStandardOutputSink (new ProcessStandardStreamWrapper { LoggingLevel = EchoStandardOutputLevel, CustomSeverityName = StdoutSeverityName });
					defaultStdoutEchoWrapperAdded = true;
				}
			}

			if (EchoStandardError) {
				if (StandardErrorEchoWrapper != null) {
					AddStandardErrorSink (StandardErrorEchoWrapper);
				} else if (defaultStderrEchoWrapper == null) {
					defaultStderrEchoWrapper = new ProcessStandardStreamWrapper { LoggingLevel = EchoStandardErrorLevel, CustomSeverityName = StderrSeverityName };
					AddStandardErrorSink (defaultStderrEchoWrapper);
				}
			}

			ManualResetEventSlim? stdout_done = null;
			ManualResetEventSlim? stderr_done = null;

			if (stderrSinks != null && stderrSinks.Count > 0)
				stderr_done = new ManualResetEventSlim (false);

			if (stdoutSinks != null && stdoutSinks.Count > 0)
				stdout_done = new ManualResetEventSlim (false);

			var psi = new ProcessStartInfo (command) {
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				RedirectStandardError = stderr_done != null,
				RedirectStandardOutput = stdout_done != null,
			};

			if (Environment.Count > 0) {
				Log.DebugLine ($"Setting up environment for {Command}:");
				foreach (var kvp in Environment) {
					Log.DebugLine ($"  {kvp.Key} = {kvp.Value}");
				}
			}

			if (arguments != null)
				psi.Arguments = String.Join (" ", arguments);

			if (!String.IsNullOrEmpty (WorkingDirectory))
				psi.WorkingDirectory = WorkingDirectory;

			if (psi.RedirectStandardError)
				StandardErrorEncoding = StandardErrorEncoding;

			if (psi.RedirectStandardOutput)
				StandardOutputEncoding = StandardOutputEncoding;

			if (StartInfoCallback != null)
				StartInfoCallback (psi);

			process = new Process {
				StartInfo = psi
			};

			if (EchoCmdAndArguments)
				Log.DebugLine ($"Running: {FullCommandLine}");

			try {
				process.Start ();
				ProcessId = process.Id;
			} catch (System.ComponentModel.Win32Exception ex) {
				Log.ErrorLine ($"Process failed to start: {ex.Message}");
				Log.DebugLine (ex.ToString ());

				Log.Todo ("need to check NativeErrorCode to make sure it's command not found");
				ErrorReason = ErrorReasonCode.CommandNotFound;
				return false;
			}

			DataReceivedEventHandler? errorHandler = null;
			if (psi.RedirectStandardError) {
				errorHandler = (object sender, DataReceivedEventArgs e) => {
					if (e.Data != null)
						WriteOutput (e.Data, stderrSinks!);
					else
						stderr_done!.Set ();
				};
				process.ErrorDataReceived += errorHandler;
				process.BeginErrorReadLine ();
			}

			DataReceivedEventHandler? outputHandler = null;
			if (psi.RedirectStandardOutput) {
				outputHandler = (object sender, DataReceivedEventArgs e) => {
					if (e.Data != null)
						WriteOutput (e.Data, stdoutSinks!);
					else
						stdout_done!.Set ();
				};
				process.OutputDataReceived += outputHandler;
				process.BeginOutputReadLine ();
			}

			bool exited = process.WaitForExit ((int)ProcessTimeout.TotalMilliseconds);
			if (!exited && !fireAndForget) {
				Log.ErrorLine ($"Process '{FullCommandLine}' timed out after {ProcessTimeout}");
				ErrorReason = ErrorReasonCode.ExecutionTimedOut;
				process.Kill ();
				TimedOut = true;
			}

			if (!fireAndForget) {
				// See: https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexit?view=netframework-4.7.2#System_Diagnostics_Process_WaitForExit)
				if (psi.RedirectStandardError || psi.RedirectStandardOutput) {
					if (!TimedOut || (TimedOut && !DoNotKillOnTimeout)) {
						Log.DebugLine ("Waiting for the process to exit");
						process.WaitForExit ();
					} else if (TimedOut) {
						Log.WarningLine ($"Process '{FullCommandLine}' timed out but we are not to wait for it to exit. CALLER MUST HANDLE THE SITUATION!");
					}
				}

				if (stderr_done != null) {
					stderr_done.Wait (StandardErrorTimeout);
				}

				if (stdout_done != null) {
					stdout_done.Wait (StandardOutputTimeout);
				}
			} else {
				if (psi.RedirectStandardError) {
					process.CancelErrorRead ();
					if (errorHandler != null) {
						process.ErrorDataReceived -= errorHandler;
					}
					process.BeginErrorReadLine ();
				}

				if (psi.RedirectStandardOutput) {
					process.CancelOutputRead ();
					if (outputHandler != null) {
						process.OutputDataReceived -= outputHandler;
					}
					process.BeginOutputReadLine ();
				}
			}

			try {
				if (process.HasExited) { // Should be safe to use this property here
					ExitCode = process.ExitCode;
					if (ExitCode != 0 && ErrorReason == ErrorReasonCode.NotExecutedYet) {
						ErrorReason = ErrorReasonCode.ExitCodeNotZero;
						return false;
					}
				}

				if (exited || fireAndForget)
					ErrorReason = ErrorReasonCode.NoError;

				if (fireAndForget) {
					return !exited;
				}

				return exited;
			} finally {
				if (!fireAndForget) {
					process = null;
				}
			}
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
