using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Tasks;

namespace Xamarin.Android.Utilities;

using Utils = Xamarin.Android.Debug.Utilities;

class DotNetRunner : ToolRunner
{
	class StreamOutputSink : ToolOutputSink
	{
		readonly StreamWriter output;

		public StreamOutputSink (XamarinLoggingHelper logger, StreamWriter output)
			: base (logger)
		{
			this.output = output;
		}

		public override void WriteLine (string? value)
		{
			output.WriteLine (value ?? String.Empty);
		}
	}

	readonly string workDirectory;

	public DotNetRunner (XamarinLoggingHelper logger, string toolPath, string workDirectory)
		: base (logger, toolPath)
	{
		this.workDirectory = workDirectory;
	}

	/// <summary>
	/// Build project at <paramref name="projectPath"/> (either path to a project directory or a project file) and return full
	/// path to binary build log, if the build succeded, or <c>null</c> otherwise.
	/// </summary>
	public async Task<string?> Build (string projectPath, string configuration, params string[] extraArgs)
	{
		string projectWorkDir = Path.Combine (workDirectory, Utils.StringHash (projectPath));

		Directory.CreateDirectory (projectWorkDir);
		string binlogPath = Path.Combine (projectWorkDir, "build.binlog");

		var runner = CreateProcessRunner ("build");
		runner.
			AddArgument ("-c").AddArgument (configuration).
			AddArgument ($"-bl:\"{binlogPath}\"");

		if (extraArgs != null && extraArgs.Length > 0) {
			foreach (string arg in extraArgs) {
				runner.AddArgument (arg);
			}
		}
		runner.AddQuotedArgument (projectPath);

		if (!await RunDotNet (runner)) {
			return null;
		}

		//return await ConvertBinlogToText (binlogPath);
		return binlogPath;
	}

	public async Task<string?> ConvertBinlogToText (string binlogPath)
	{
		if (!File.Exists (binlogPath)) {
			Logger.ErrorLine ($"Binlog '{binlogPath}' does not exist, cannot convert to text");
			return null;
		}

		bool stdoutEchoOrig = EchoStandardOutput;
		ProcessRunner runner;

		try {
			EchoStandardOutput = false;
			runner = CreateProcessRunner ("msbuild");
		} finally {
			EchoStandardOutput = stdoutEchoOrig;
		}

		runner.
			AddArgument ("-v:diag").
			AddQuotedArgument (binlogPath);

		string logOutput = Path.ChangeExtension (binlogPath, ".txt");
		using var fs = File.Open (logOutput, FileMode.Create);
		using var sw = new StreamWriter (fs, Utils.UTF8NoBOM);
		using var sink = new StreamOutputSink (Logger, sw);

		try {
			if (!await RunDotNet (runner, sink)) {
				return null;
			}
		} finally {
			sw.Flush ();
		}

		return logOutput;
	}

	async Task<bool> RunDotNet (ProcessRunner runner, ToolOutputSink? outputSink = null)
	{
		if (outputSink != null) {
			SetupOutputSinks (runner, stdoutSink: outputSink, stderrSink: null, ignoreStderr: true);
		}

		return await RunTool (() => runner.Run ());
	}
}
