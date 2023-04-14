using System;

namespace Xamarin.Android.Utilities;

abstract class ToolRunner2 : IDisposable
{
	sealed class ConsoleProcessLogger : IProcessOutputLogger
	{
		bool echoStdout;
		bool echoStderr;
		IProcessOutputLogger wrappedLogger;
		ILogger logger;

		public IProcessOutputLogger? WrappedLogger => wrappedLogger;
		public string? StdoutPrefix { get; set; }
		public string? StderrPrefix { get; set; } = "stderr> ";

		public ConsoleProcessLogger (ILogger logger, IProcessOutputLogger wrappedLogger, bool echoStdout, bool echoStderr)
		{
			this.logger = logger;
			this.wrappedLogger = wrappedLogger;
			this.echoStdout = echoStdout;
			this.echoStderr = echoStderr;
		}

		public void WriteStderr (string text, bool writeNewline)
		{
			if (echoStderr) {
				string message = $"{GetPrefix (StderrPrefix)}{text}";
				if (writeNewline) {
					logger.ErrorLine (message);
				} else {
					logger.Error (message);
				}
			}

			wrappedLogger.WriteStderr (text, writeNewline);
		}

		public void WriteStdout (string text, bool writeNewline)
		{
			if (echoStdout) {
				string message = $"{GetPrefix (StdoutPrefix)}{text}";
				if (writeNewline) {
					logger.MessageLine (message);
				} else {
					logger.Message (message);
				}
			}

			wrappedLogger.WriteStdout (text, writeNewline);
		}

		string GetPrefix (string? prefix) => prefix ?? String.Empty;
	}

	static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromMinutes (15);

	bool disposed;
	ILogger logger;
	IProcessOutputLogger processOutputLogger;

	protected ILogger Log                       => logger;
	protected IProcessOutputLogger OutputLogger => processOutputLogger;

	public string ToolPath          { get; }
	public bool EchoCmdAndArguments { get; set; } = true;
	public bool EchoStandardError   { get; set; } = true;
	public bool EchoStandardOutput  { get; set; }
	public TimeSpan ProcessTimeout  { get; set; } = DefaultProcessTimeout;

	protected ToolRunner2 (string toolPath, ILogger logger, IProcessOutputLogger processOutputLogger)
	{
		if (String.IsNullOrEmpty (toolPath)) {
			throw new ArgumentException ("must not be null or empty", nameof (toolPath));
		}

		this.logger = logger;
		this.processOutputLogger = processOutputLogger;

		ToolPath = toolPath;
	}

	~ToolRunner2 ()
	{
		Dispose (disposing: false);
	}

	protected ProcessRunner2 InitProcessRunner (object? state, params string?[]? initialParams)
	{
		var consoleLogger = new ConsoleProcessLogger (logger, processOutputLogger, EchoStandardOutput, EchoStandardError);
		return CreateProcessRunner (consoleLogger, state, initialParams);
	}

	protected virtual ProcessRunner2 CreateProcessRunner (IProcessOutputLogger consoleProcessLogger, object? state, params string?[]? initialParams)
	{
		var runner = new ProcessRunner2 (ToolPath, consoleProcessLogger, logger) {
			ProcessTimeout = ProcessTimeout,
			LogRunInfo = EchoCmdAndArguments,
			LogStdout = true,
			LogStderr = true,
		};

		runner.AddArguments (initialParams);
		return runner;
	}

	protected virtual void Dispose (bool disposing)
	{
		if (disposed) {
			return;
		}

		if (disposing) {
			// TODO: dispose managed state (managed objects)
		}

		disposed = true;
	}

	public void Dispose ()
	{
		Dispose (disposing: true);
		GC.SuppressFinalize (this);
	}
}
