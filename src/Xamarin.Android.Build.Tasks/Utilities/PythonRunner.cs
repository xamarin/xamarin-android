using System.Threading.Tasks;

using Microsoft.Build.Utilities;

namespace Xamarin.Android.Tasks
{
	class PythonRunner : ToolRunner
	{
		public PythonRunner (TaskLoggingHelper logger, string pythonPath)
			: base (logger, pythonPath)
		{}

		public async Task<bool> RunScript (string scriptPath, params string[] arguments)
		{
			ProcessRunner runner = CreateProcessRunner ();

			if (arguments != null && arguments.Length > 0) {
				foreach (string arg in arguments) {
					runner.AddArgument (arg);
				}
			}

			return await RunPython (runner);
		}

		async Task<bool> RunPython (ProcessRunner runner)
		{
			return await RunTool (
				() => {
					return runner.Run ();
				}
			);
		}
	}
}
