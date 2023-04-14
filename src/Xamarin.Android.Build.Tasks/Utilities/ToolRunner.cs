using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

#if NO_MSBUILD
using LoggerType = Xamarin.Android.Utilities.XamarinLoggingHelper;
#else // def NO_MSBUILD
using Microsoft.Android.Build.Tasks;

using LoggerType = Microsoft.Build.Utilities.TaskLoggingHelper;
#endif // ndef NO_MSBUILD

using TPLTask = System.Threading.Tasks.Task;

namespace Xamarin.Android.Tasks
{
	abstract class ToolRunner
	{
		protected abstract class ToolOutputSink : TextWriter
		{
			protected string LogLinePrefix { get; set; } = String.Empty;

			LoggerType log;

			public override Encoding Encoding => Encoding.Default;

			protected ToolOutputSink (LoggerType logger)
			{
				log = logger;
			}

			public override void WriteLine (string? value)
			{
				string message;

				if (!String.IsNullOrEmpty (LogLinePrefix)) {
					message = $"{LogLinePrefix}> {value ?? String.Empty}";
				} else {
					message = value ?? String.Empty;
				}

				log.LogDebugMessage (message);
			}
		}

		static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromMinutes (15);

		protected LoggerType Logger            { get; }
		public string ToolPath                 { get; }
		public bool EchoCmdAndArguments        { get; set; } = true;
		public bool EchoStandardError          { get; set; } = true;
		public bool EchoStandardOutput         { get; set; }
		public virtual TimeSpan ProcessTimeout { get; set; } = DefaultProcessTimeout;

		protected ToolRunner (LoggerType logger, string toolPath)
		{
			if (String.IsNullOrEmpty (toolPath)) {
				throw new ArgumentException ("must not be null or empty", nameof (toolPath));
			}

			Logger = logger;
			ToolPath = toolPath;
		}

                protected virtual ProcessRunner CreateProcessRunner (params string?[]? initialParams)
                {
	                var runner = new ProcessRunner (Logger, ToolPath) {
                                ProcessTimeout = ProcessTimeout,
                                EchoCmdAndArguments = EchoCmdAndArguments,
                                EchoStandardError = EchoStandardError,
                                EchoStandardOutput = EchoStandardOutput,
                        };

                        runner.AddArguments (initialParams);
                        return runner;
                }

		protected virtual async Task<bool> RunTool (Func<bool> runner)
		{
			return await TPLTask.Run (runner);
		}

		protected void SetupOutputSinks (ProcessRunner runner, TextWriter stdoutSink, TextWriter? stderrSink = null, bool ignoreStderr = false)
                {
                        if (!ignoreStderr) {
                                runner.AddStandardErrorSink (stderrSink ?? stdoutSink);
                        }
                        runner.AddStandardOutputSink (stdoutSink);
                }
	}
}
