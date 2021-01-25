using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Xamarin.Android.Prepare
{
	abstract partial class ToolRunner : AppObject
	{
		static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromMinutes (15);

		ThumbTwiddler? twiddler;
		string? programVersion;

		protected const ConsoleColor CommandMessageColor = ConsoleColor.White;

		protected abstract string DefaultToolExecutableName { get; }
		protected abstract string ToolName                  { get; }

#if !XAT
		protected Context Context              { get; }
#endif
		public string FullToolPath             { get; }
		protected string VersionString         => GetVersion ();

		public bool EchoCmdAndArguments        { get; set; } = true;
		public bool EchoStandardError          { get; set; } = true;
		public bool EchoStandardOutput         { get; set; }
		public string LogMessageIndent         { get; set; } = String.Empty;
		public virtual TimeSpan ProcessTimeout { get; set; } = DefaultProcessTimeout;
		public Dictionary<string, string> EnvironmentVariables { get; } = new Dictionary<string, string> (StringComparer.Ordinal);

		protected ToolRunner (Context context, Log? log = null, string? toolPath = null)
			: base (log)
		{
#if !XAT
			Context = context ?? throw new ArgumentNullException (nameof (context));
#endif
			if (String.IsNullOrEmpty (toolPath)) {
				Log.DebugLine ($"Locating {ToolName} executable '{DefaultToolExecutableName}'");
				FullToolPath = context.OS.Which (DefaultToolExecutableName);
			} else {
				Log.DebugLine ($"Custom tool path: {toolPath}");
				if (toolPath!.IndexOf (Path.DirectorySeparatorChar) < 0) {
					Log.DebugLine ($"Locating custom {ToolName} executable '{toolPath}'");
					FullToolPath = context.OS.Which (toolPath);
				} else if (Path.IsPathRooted (toolPath)) {
					Log.DebugLine ($"{ToolName} executable path is rooted, using verbatim");
					FullToolPath = toolPath;
				} else {
					Log.DebugLine ($"{ToolName} executable path is relative to Xamarin.Android repository root");
					FullToolPath = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, toolPath);
				}
			}

			if (String.IsNullOrEmpty (FullToolPath))
				throw new InvalidOperationException ($"{ToolName} executable path must be specified");

			Log.DebugLine ($"Full {ToolName} executable path value: {FullToolPath}");
			if (!File.Exists (FullToolPath))
				throw new InvalidOperationException ($"{ToolName} executable '{FullToolPath}' not found");
		}

		protected virtual string GetLogMessage (ProcessRunner runner)
		{
			string message = $"{LogMessageIndent}{Path.GetFileName (FullToolPath)}";
			string formattedArguments = runner.Arguments;
			if (!String.IsNullOrEmpty (formattedArguments))
				message = $"{message} {runner.Arguments}";

			return $"{message} ";
		}

		protected void AddArguments (ProcessRunner runner, IEnumerable<string>? arguments)
		{
			if (arguments == null)
				return;

			foreach (string a in arguments) {
				string arg = a.Trim ();
				if (String.IsNullOrEmpty (arg))
					continue;

				runner.AddQuotedArgument (arg);
			}
		}

		protected virtual ProcessRunner CreateProcessRunner (params string[] initialParams)
		{
			bool usingManagedRunner;
			string command = Context.OS.GetManagedProgramRunner (FullToolPath);
			if (command.Length > 0) {
				command = Context.OS.Which (command);
				usingManagedRunner = true;
			} else {
				command = FullToolPath;
				usingManagedRunner = false;
			}

			var runner = new ProcessRunner (command) {
				ProcessTimeout = ProcessTimeout,
				EchoCmdAndArguments = EchoCmdAndArguments,
				EchoStandardError = EchoStandardError,
				EchoStandardOutput = EchoStandardOutput,
			};

			CopyDictionary (Context.Instance.OS.EnvironmentVariables, runner.Environment);
			CopyDictionary (EnvironmentVariables, runner.Environment);

			if (usingManagedRunner)
				runner.AddQuotedArgument (FullToolPath);

			runner.AddArguments (initialParams);
			return runner;

			void CopyDictionary (Dictionary<string, string>? src, Dictionary<string, string> dst)
			{
				if (src == null || src.Count == 0) {
					return;
				}

				foreach (var kvp in src) {
					dst [kvp.Key] = kvp.Value;
				}
			}
		}

		protected virtual async Task<bool> RunTool (Func<bool> runner)
		{
			return await Task.Run (runner);
		}

		protected TextWriter SetupOutputSink (ProcessRunner runner, string? tags = null, string? messagePrefix = null)
		{
			string? logFilePath = null;

			if (!String.IsNullOrEmpty (tags)) {
				logFilePath = Context.GetLogFilePath (tags ?? String.Empty);
				if (String.IsNullOrEmpty (messagePrefix))
					messagePrefix = "running";
				Log.StatusLine ($"{LogMessageIndent}[{ToolName}] {messagePrefix}");
				if (!String.IsNullOrEmpty (logFilePath)) {
					Log.StatusLine ($"[{ToolName}] log file: ", $"{Utilities.GetPathRelativeToCWD (logFilePath)}", tailColor: Log.DestinationColor);
				}
			}

			TextWriter ret = CreateLogSink (logFilePath);

			runner.AddStandardErrorSink (ret);
			runner.AddStandardOutputSink (ret);

			return ret;
		}

		protected virtual TextWriter CreateLogSink (string? logFilePath)
		{
			throw new NotSupportedException ("Child class must implement this method if it uses GetLogFileSink");
		}

		protected void StartTwiddler (bool showElapsedTime = true)
		{
			twiddler = new ThumbTwiddler (Context.Instance, Context.Instance.DullMode, showElapsedTime);
			twiddler.Start ();
		}

		protected void StopTwiddler ()
		{
			if (twiddler == null)
				return;

			twiddler.Stop ();
			Log.StatusLine ();
		}

		string GetVersion ()
		{
			if (!String.IsNullOrEmpty (programVersion))
				return programVersion!;

			(bool success, string version) = Utilities.GetProgramVersion (FullToolPath);
			if (!success)
				return String.Empty;

			programVersion = version;
			return version;
		}
	}
}
