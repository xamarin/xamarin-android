using System;
using System.IO;

namespace Xamarin.Android.Utilities;

enum LogLevel
{
	Error,
	Warning,
	Info,
	Message,
	Debug
}

class XamarinLoggingHelper : ILogger, IProcessOutputLogger
{
	static readonly object consoleLock = new object ();
	string? logFilePath = null;
	string? logFileDir = null;

	public const ConsoleColor ErrorColor   = ConsoleColor.Red;
	public const ConsoleColor DebugColor   = ConsoleColor.DarkGray;
	public const ConsoleColor InfoColor    = ConsoleColor.Green;
	public const ConsoleColor MessageColor = ConsoleColor.Gray;
	public const ConsoleColor WarningColor = ConsoleColor.Yellow;
	public const ConsoleColor StatusLabel  = ConsoleColor.Cyan;
	public const ConsoleColor StatusText   = ConsoleColor.White;

	public bool Verbose { get; set; }
	public string? LogFilePath {
		get => logFilePath;
		set {
			if (!String.IsNullOrEmpty (value)) {
				string? dir = Path.GetDirectoryName (value);
				if (!String.IsNullOrEmpty (dir)) {
					Directory.CreateDirectory (dir);
				}
			}

			logFilePath = value;
			logFileDir = Path.GetDirectoryName (value);
		}
	}

	public IProcessOutputLogger? WrappedLogger => null;

	public string? StdoutPrefix {
		get => String.Empty;
		set {
			// no op
		}
	}

	public string? StderrPrefix {
		get => "stderr> ";
		set {
			// no op
		}
	}

	public void Message (string? message)
	{
		Log (LogLevel.Message, message);
	}

	public void MessageLine (string? message = null)
	{
		Message ($"{message ?? String.Empty}{Environment.NewLine}");
	}

	public void Warning (string? message)
	{
		Log (LogLevel.Warning, message);
	}

	public void WarningLine (string? message = null)
	{
		Warning ($"{message ?? String.Empty}{Environment.NewLine}");
	}

	public void Error (string? message)
	{
		Log (LogLevel.Error, message);
	}

	public void ErrorLine (string? message = null)
	{
		Error ($"{message ?? String.Empty}{Environment.NewLine}");
	}

	public void Info (string? message)
	{
		Log (LogLevel.Info, message);
	}

	public void InfoLine (string? message = null)
	{
		Info ($"{message ?? String.Empty}{Environment.NewLine}");
	}

	public void Debug (string? message)
	{
		Log (LogLevel.Debug, message);
	}

	public void DebugLine (string? message = null)
	{
		Debug ($"{message ?? String.Empty}{Environment.NewLine}");
	}

	public void Status (string label, string text)
	{
		Log (LogLevel.Info, $"{label}: ", StatusLabel);
		Log (LogLevel.Info, $"{text}", StatusText);
	}

	public void StatusLine (string label, string text)
	{
		Status (label, text);
		Log (LogLevel.Info, Environment.NewLine);
	}

	public void Log (LogLevel level, string? message)
	{
		if (!Verbose && level == LogLevel.Debug) {
			LogToFile (message);
			return;
		}

		Log (level, message, ForegroundColor (level));
	}

	public void LogLine (LogLevel level, string? message, ConsoleColor color)
	{
		Log (level, message, color);
		Log (level, Environment.NewLine, color);
	}

	public void Log (LogLevel level, string? message, ConsoleColor color)
	{
		LogToFile (message);

		if (!Verbose && level == LogLevel.Debug) {
			return;
		}

		TextWriter writer = level == LogLevel.Error ? Console.Error : Console.Out;
		message = message ?? String.Empty;

		ConsoleColor fg = ConsoleColor.Gray;
		try {
			lock (consoleLock) {
				fg = Console.ForegroundColor;
				Console.ForegroundColor = color;
			}

			writer.Write (message);
		} finally {
			Console.ForegroundColor = fg;
		}
	}

	void LogToFile (string? message)
	{
		if (String.IsNullOrEmpty (LogFilePath)) {
			return;
		}

		if (!String.IsNullOrEmpty (logFileDir) && !Directory.Exists (logFileDir)) {
			Directory.CreateDirectory (logFileDir);
		}

		File.AppendAllText (LogFilePath, message);
	}

	ConsoleColor ForegroundColor (LogLevel level) => level switch {
		LogLevel.Error => ErrorColor,
		LogLevel.Warning => WarningColor,
		LogLevel.Info => InfoColor,
		LogLevel.Debug => DebugColor,
		LogLevel.Message => MessageColor,
		_ => MessageColor,
	};

#region MSBuild compatibility methods
	public void LogDebugMessage (string message, params object[] messageArgs)
	{
		if (messageArgs == null || messageArgs.Length == 0) {
			DebugLine (message);
		} else {
			DebugLine (String.Format (message, messageArgs));
		}
	}

	public void LogError (string message, params object[] messageArgs)
	{
		if (messageArgs == null || messageArgs.Length == 0) {
			ErrorLine (message);
		} else {
			ErrorLine (String.Format (message, messageArgs));
		}
	}

	public void LogMessage (string message, params object[] messageArgs)
	{
		if (messageArgs == null || messageArgs.Length == 0) {
			MessageLine (message);
		} else {
			MessageLine (String.Format (message, messageArgs));
		}
	}

	public void LogWarning (string message, params object[] messageArgs)
	{
		if (messageArgs == null || messageArgs.Length == 0) {
			WarningLine (message);
		} else {
			WarningLine (String.Format (message, messageArgs));
		}
	}

	public void WriteStdout (string text, bool writeLine = true)
	{
		LogToFile ($"{StdoutPrefix}{text}{GetNewline (writeLine)}");
	}

	public void WriteStderr (string text, bool writeLine = true)
	{
		LogToFile ($"{StderrPrefix}{text}{GetNewline (writeLine)}");
	}

	string GetNewline (bool yes) => yes ? Environment.NewLine : String.Empty;
    #endregion
}
