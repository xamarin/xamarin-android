using System;
using System.IO;

namespace Xamarin.Android.Application.Utilities;

class XamarinLoggingHelper : ILogger
{
	static readonly object consoleLock = new object ();
	string? logFilePath = null;
	string? logFileDir = null;

	public const ConsoleColor ErrorColor   = ConsoleColor.Red;
	public const ConsoleColor DebugColor   = ConsoleColor.DarkGray;
	public const ConsoleColor InfoColor    = ConsoleColor.Green;
	public const ConsoleColor MessageColor = ConsoleColor.Gray;
	public const ConsoleColor WarningColor = ConsoleColor.Yellow;
	public const ConsoleColor StatusLabel  = ConsoleColor.White;
	public const ConsoleColor StatusText   = ConsoleColor.Cyan;
	public const ConsoleColor StatusYes    = ConsoleColor.Green;
	public const ConsoleColor StatusNo     = ConsoleColor.Red;

	public LogLevel Level { get; set; } = LogLevel.Message;

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

	public void Verbose (string? message)
	{
		Log (LogLevel.Verbose, message);
	}

	public void VerboseLine (string? message = null)
	{
		Verbose ($"{message ?? String.Empty}{Environment.NewLine}");
	}

	void Status (string label, string text, ConsoleColor color)
	{
		Log (LogLevel.Info, $"{label}: ", StatusLabel);
		Log (LogLevel.Info, text, color);
	}

	public void Status (string label, string text)
	{
		Status (label, text, StatusText);
	}

	public void Status (string label, IFormattable? val, string missingText = "missing")
	{
		Status (label, val?.ToString () ?? missingText);
	}

	public void StatusLine (string label, string text)
	{
		Status (label, text);
		Log (LogLevel.Info, Environment.NewLine);
	}

	public void StatusLine (string label, IFormattable? val, string missingText = "missing")
	{
		Status (label, val);
		Log (LogLevel.Info, Environment.NewLine);
	}

	public void StatusYesNo (string label, bool yes)
	{
		Status (label, YesNo (yes), yes ? StatusYes : StatusNo);
	}

	public void StatusYesNoLine (string label, bool yes)
	{
		StatusYesNo (label, yes);
		Log (LogLevel.Info, Environment.NewLine);
	}

	static string YesNo (bool yes) => yes ? "yes" : "no";

	public void Log (LogLevel level, string? message)
	{
		if (level > Level) {
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

		if (level > Level) {
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

	public void ExceptionError (string desc, Exception ex)
	{
		LogLine (LogLevel.Error, desc, ErrorColor);
		LogLine (LogLevel.Error, ex.ToString (), ErrorColor);
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
}
