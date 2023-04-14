using System;

namespace Xamarin.Android.Utilities;

class ProcessStatus
{
	public int ExitCode         { get; } = -1;
	public bool Exited          { get; } = false;
	public bool Success         { get; } = false;
	public Exception? Exception { get; }

	public ProcessStatus ()
	{}

	public ProcessStatus (Exception ex)
	{
		Exception = ex;
	}

	public ProcessStatus (int exitCode, bool exited, bool success)
	{
		ExitCode = exitCode;
		Exited = exited;
		Success = success;
	}
}
