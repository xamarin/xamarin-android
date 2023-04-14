using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Microsoft.Build.Utilities;

namespace Xamarin.Android.Tasks
{
	class DotnetSymbolRunner : ToolRunner
	{
		public DotnetSymbolRunner (TaskLoggingHelper logger, string dotnetSymbolPath)
			: base (logger, dotnetSymbolPath)
		{}

		public async Task<bool> Fetch (string nativeLibraryPath, bool enableDiagnostics = false)
		{
			var runner = CreateProcessRunner ();
			runner.AddArgument ("--symbols");
			runner.AddArgument ("--timeout").AddArgument ("1");
			runner.AddArgument ("--overwrite");

			if (enableDiagnostics) {
				runner.AddArgument ("--diagnostics");
			}

			runner.AddArgument (nativeLibraryPath);

			return await Run (runner);
		}

		async Task<bool> Run (ProcessRunner runner)
		{
			return await RunTool (
				() => {
					return runner.Run ();
				}
			);
		}
	}
}
