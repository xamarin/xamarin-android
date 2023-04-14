namespace Xamarin.Android.Utilities;

interface IProcessOutputLogger
{
	IProcessOutputLogger? WrappedLogger { get; }
	string? StdoutPrefix { get; set; }
	string? StderrPrefix { get; set; }

	void WriteStdout (string text, bool writeLine = true);
	void WriteStderr (string text, bool writeLine = true);
}
