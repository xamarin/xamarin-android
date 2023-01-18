using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Xamarin.Android.Utilities;

class ProcessRunner2 : IDisposable
{
	static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromMinutes (5);
	static readonly TimeSpan DefaultOutputTimeout = TimeSpan.FromSeconds (10);

	readonly object runLock = new object ();
	readonly IProcessOutputLogger? outputLogger;
	readonly ILogger? logger;
	readonly string command;

	bool disposed;
	bool running;
	List<string>? arguments;

	public bool CreateWindow                            { get; set; }
	public Dictionary<string, string> Environment       { get; } = new Dictionary<string, string> (StringComparer.Ordinal);
	public string? FullCommandLine                      { get; private set; }
	public bool LogRunInfo                              { get; set; } = true;
	public bool LogStderr                               { get; set; }
	public bool LogStdout                               { get; set; }
	public bool MakeProcessGroupLeader                  { get; set; }
	public TimeSpan ProcessTimeout                      { get; set; } = DefaultProcessTimeout;
	public Encoding StandardOutputEncoding              { get; set; } = Encoding.Default;
	public Encoding StandardErrorEncoding               { get; set; } = Encoding.Default;
	public TimeSpan StandardOutputTimeout               { get; set; } = DefaultOutputTimeout;
	public TimeSpan StandardErrorTimeout                { get; set; } = DefaultOutputTimeout;
	public Action<ProcessStartInfo>? CustomizeStartInfo { get; set; }
	public bool UseShell                                { get; set; }
	public ProcessWindowStyle WindowStyle               { get; set; } = ProcessWindowStyle.Hidden;
	public string? WorkingDirectory                     { get; set; }

	public ProcessRunner2 (string command, IProcessOutputLogger? outputLogger = null, ILogger? logger = null)
	{
		if (String.IsNullOrEmpty (command)) {
			throw new ArgumentException ("must not be null or empty", nameof (command));
		}

		this.command = command;
		this.outputLogger = outputLogger;
		this.logger = logger;
	}

	~ProcessRunner2 ()
	{
		Dispose (disposing: false);
	}

	public void Kill (bool gracefully = true)
	{}

	public void AddArgument (string arg)
	{
		if (arguments == null) {
			arguments = new List<string> ();
		}

		arguments.Add (arg);
	}

	public void AddQuotedArgument (string arg)
	{
		AddArgument ($"\"{arg}\"");
	}

	/// <summary>
	/// Run process synchronously on the calling thread
	/// </summary>
	public ProcessStatus Run ()
	{
		try {
			return DoRun (PrepareForRun ());
		} finally {
			MarkNotRunning ();
		}
	}

	ProcessStatus DoRun (ProcessStartInfo psi)
	{
		ManualResetEventSlim? stdout_done = null;
		ManualResetEventSlim? stderr_done = null;

		if (LogStderr) {
			stderr_done = new ManualResetEventSlim (false);
		}

		if (LogStdout) {
			stdout_done = new ManualResetEventSlim (false);
		}

		if (LogRunInfo) {
			logger?.DebugLine ($"Running: {FullCommandLine}");
		}

		var process = new Process {
			StartInfo = psi
		};

		try {
			process.Start ();
		} catch (System.ComponentModel.Win32Exception ex) {
			if (logger != null) {
				logger.ErrorLine ($"Process failed to start: {ex.Message}");
				logger.DebugLine (ex.ToString ());
			}

			return new ProcessStatus ();
		}

		if (psi.RedirectStandardError) {
			process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) => {
				if (e.Data != null) {
					outputLogger!.WriteStderr (e.Data ?? String.Empty);
				} else {
					stderr_done!.Set ();
				}
			};
			process.BeginErrorReadLine ();
		}

		if (psi.RedirectStandardOutput) {
			process.OutputDataReceived += (object sender, DataReceivedEventArgs e) => {
				if (e.Data != null) {
					outputLogger!.WriteStdout (e.Data ?? String.Empty);
				} else {
					stdout_done!.Set ();
				}
			};
			process.BeginOutputReadLine ();
		}

		int timeout = ProcessTimeout == TimeSpan.MaxValue ? -1 : (int)ProcessTimeout.TotalMilliseconds;
		bool exited = process.WaitForExit (timeout);
		if (!exited) {
			logger?.ErrorLine ($"Process '{FullCommandLine}' timed out after {ProcessTimeout}");
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

		return new ProcessStatus (process.ExitCode, exited, process.ExitCode == 0);
	}

	/// <summary>
	/// Run process in a separate thread.  The caller is responsible for awaiting on the returned <c>Task</c>
	/// </summary>
	public Task<ProcessStatus> RunAsync ()
	{
		return Task.Run (() => Run ());
	}

	/// <summary>
	/// Run process in background, calling the <param ref="completionHandler"/> on completion. This is meant to be used for processes which are to run under control of our
	/// process but without us actively monitoring them or awaiting their completion.
	/// </summary>
	public void RunInBackground (Action<ProcessRunner2, ProcessStatus> completionHandler)
	{
		ProcessStartInfo psi = PrepareForRun ();
	}

	protected virtual void Dispose (bool disposing)
	{
		if (disposed) {
			return;
		}

		if (disposing) {
			// TODO: dispose managed state (managed objects)
		}

		// TODO: free unmanaged resources (unmanaged objects) and override finalizer
		// TODO: set large fields to null
		disposed = true;
	}

	public void Dispose ()
	{
		Dispose (disposing: true);
		GC.SuppressFinalize (this);
	}

	ProcessStartInfo PrepareForRun ()
	{
		MarkRunning ();

		var psi = new ProcessStartInfo (command) {
			CreateNoWindow = !CreateWindow,
			RedirectStandardError = LogStderr,
			RedirectStandardOutput = LogStdout,
			UseShellExecute = UseShell,
			WindowStyle = WindowStyle,
		};

		if (arguments != null && arguments.Count > 0) {
			psi.Arguments = String.Join (" ", arguments);
		}

		if (Environment.Count > 0) {
			foreach (var kvp in Environment) {
				psi.Environment.Add (kvp.Key, kvp.Value);
			}
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

		if (CustomizeStartInfo != null) {
			CustomizeStartInfo (psi);
		}

		EnsureValidConfig (psi);

		FullCommandLine = $"{psi.FileName} {psi.Arguments}";
		return psi;
	}

	void MarkRunning ()
	{
		lock (runLock) {
			if (running) {
				throw new InvalidOperationException ("Process already running");
			}

			running = true;
		}
	}

	void MarkNotRunning ()
	{
		lock (runLock) {
			running = false;
		}
	}

	void EnsureValidConfig (ProcessStartInfo psi)
	{
		if ((psi.RedirectStandardOutput || psi.RedirectStandardError) && outputLogger == null) {
			throw new InvalidOperationException ("Process output logger must be set in order to capture standard output streams");
		}
	}
}
