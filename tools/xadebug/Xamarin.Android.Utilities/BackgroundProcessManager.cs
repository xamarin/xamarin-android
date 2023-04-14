using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xamarin.Android.Tasks;

namespace Xamarin.Android.Utilities;

class BackgroundProcessManager : IDisposable
{
	readonly object runnersLock = new object ();
	readonly List<ToolRunner> runners;

	bool disposed;

	public BackgroundProcessManager ()
	{
		runners = new List<ToolRunner> ();
		Console.CancelKeyPress += ConsoleCanceled;
	}

	// TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
	~BackgroundProcessManager ()
	{
		Dispose (disposing: false);
	}

	protected virtual void Dispose (bool disposing)
	{
		if (!disposed) {
			if (disposing) {
				// TODO: dispose managed state (managed objects)
			}

			FinishAllTasks ();
			disposed = true;
		}
	}

	public void Dispose ()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose (disposing: true);
		GC.SuppressFinalize (this);
	}

	public void RunInBackground (ProcessRunner2 runner, ProcessRunner2.BackgroundActionCompletionHandler? completionHandler)
	{}

	void TaskFailed (Task task)
	{
	}

	void FinishAllTasks ()
	{
	}

	void ConsoleCanceled (object? sender, ConsoleCancelEventArgs args)
	{
	}
}
