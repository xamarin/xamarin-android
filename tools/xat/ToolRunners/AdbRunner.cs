using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class AdbRunner : ToolRunner
	{
		sealed class LineForwarder : TextWriter
		{
			Action<string>? lineCallback;

			public override Encoding Encoding => Encoding.Default;

			public LineForwarder (Action<string> lineCallback)
			{
				this.lineCallback = lineCallback;
			}

			public LineForwarder (IFormatProvider formatProvider)
				: base (formatProvider)
			{
				lineCallback = null;
			}

			public override void WriteLine (string value)
			{
				if (lineCallback == null) {
					return;
				}

				lineCallback (value);
			}
		}

		static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds (120);

		protected override string DefaultToolExecutableName => "adb";
		protected override string ToolName                  => "ADB";

		public string AdbTarget { get; set; } = String.Empty;

		public AdbRunner (Context context, Log? log = null, string? toolPath = null)
			: base (context, log, toolPath)
		{
			EchoStandardOutput = true;
			EchoStandardError = true;
		}

		public async Task<bool> WaitForDevice (TimeSpan timeout = default, bool traceAdb = false)
		{
			if (timeout == default) {
				timeout = LongTimeout;
			}

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner.AddArgument ("wait-for-device");

			return await RunTool (() => runner.Run ());
		}

		/// <summary>
		///   Runs a shell command on Android device. The passed command MUST be properly shell-quoted!
		/// </summary>
		public async Task<(bool success, string output)> Shell (string command, TimeSpan timeout = default, bool traceAdb = false)
		{
			EnsureParameterValue (nameof (command), command);

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner
				.AddArgument ("shell")
				.AddArgument (command);

			string output = await Task.Run<string>(() => Utilities.GetStringFromStdout (runner));
			return (runner.ExitCode == 0, output);
		}

		public async Task<bool> SetProperty (string propertyName, string propertyValue, TimeSpan timeout = default, bool traceAdb = false)
		{
			EnsureParameterValue (nameof (propertyName), propertyName);

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner
				.AddArgument ("shell")
				.AddArgument ("setprop")
				.AddArgument (propertyName);

			if (propertyValue.Length == 0) {
				runner.AddQuotedArgument ("''");
			} else {
				runner.AddArgument (propertyValue);
			}

			return await RunTool (() => runner.Run ());
		}

		public async Task<(bool success, string output)> GetProperty (string propertyName, TimeSpan timeout = default, bool traceAdb = false)
		{
			EnsureParameterValue (nameof (propertyName), propertyName);

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner
				.AddArgument ("shell")
				.AddArgument ("getprop")
				.AddArgument (propertyName);

			string output = await Task.Run<string>(() => Utilities.GetStringFromStdout (runner));
			return (runner.ExitCode == 0, output);
		}

		public async Task<(bool success, string output)> Devices (TimeSpan timeout = default, bool traceAdb = false)
		{
			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner.AddArgument ("devices");

			string output = await Task.Run<string>(() => Utilities.GetStringFromStdout (runner));
			return (runner.ExitCode == 0, output);
		}

		public async Task<bool> Logcat (bool traceAdb, params string[] arguments)
		{
			return await Logcat (TimeSpan.Zero, traceAdb, arguments);
		}

		public async Task<bool> Logcat (TimeSpan timeout, params string[] arguments)
		{
			return await Logcat (timeout, traceAdb: false, arguments: arguments);
		}

		public async Task<bool> Logcat (TimeSpan timeout, bool traceAdb, params string[] arguments)
		{
			if (arguments.Length == 0) {
				throw new ArgumentException (nameof (arguments), "Logcat expects at least one argument");
			}

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner.AddArgument ("logcat");

			foreach (string argument in arguments) {
				runner.AddQuotedArgument (argument);
			}

			return await RunTool (() => runner.Run ());
		}

		public async Task<bool> LogcatSetBufferSize (string sizeSpec, TimeSpan timeout = default, bool traceAdb = false)
		{
			EnsureParameterValue (nameof (sizeSpec), sizeSpec);

			return await Logcat (timeout, traceAdb, "-G", sizeSpec);
		}

		public async Task<bool> LogcatClear (TimeSpan timeout = default, bool traceAdb = false)
		{
			return await Logcat (timeout, traceAdb, "-c");
		}

		public async Task<bool> LogcatDump (string? outputFilePath = null, string? format = null, List<string>? extraArguments = null, TimeSpan timeout = default, bool traceAdb = false, Action<string>? lineCallback = null)
		{
			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner.EchoStandardOutput = false;
			runner
				.AddArgument ("logcat")
				.AddArgument ("-d");

			if (!String.IsNullOrEmpty (format)) {
				runner
					.AddArgument ("-v")
					.AddArgument (format!);
			}

			AddExtraArguments (runner, extraArguments);

			LineForwarder? lf = AddLineCallback (runner, lineCallback);
			FileStream? logStream = null;
			StreamWriter? logWriter = null;

			if (!String.IsNullOrEmpty (outputFilePath)) {
				logStream = File.Open (outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
				logWriter = new StreamWriter (logStream);

				runner.AddStandardOutputSink (logWriter);
			}

			try {
				return await RunTool (() => runner.Run ());
			} finally {
				lf?.Dispose ();

				if (logStream != null) {
					logStream.Flush ();
					if (logWriter == null) {
						logStream.Close ();
						logStream.Dispose ();
					}
				}

				if (logWriter != null) {
					logWriter.Flush ();
					logWriter.Close ();
					logWriter.Dispose ();
				}
			}
		}

		public async Task<bool> AmInstrument (string componentName, List<string>? extraArguments = null, TimeSpan timeout = default, bool traceAdb = false, Action<string>? lineCallback = null)
		{
			EnsureParameterValue (nameof (componentName), componentName);

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner
				.AddArgument ("shell")
				.AddArgument ("am")
				.AddArgument ("instrument")
				.AddArgument ("-w");

			AddExtraArguments (runner, extraArguments);
			runner.AddQuotedArgument (componentName);

			LineForwarder? lf = AddLineCallback (runner, lineCallback);
			try {
				return await RunTool (() => runner.Run ());
			} finally {
				lf?.Dispose ();
			}
		}

		public async Task<bool> AmStart (string componentName, List<string>? extraArguments = default, TimeSpan timeout = default, bool traceAdb = false)
		{
			EnsureParameterValue (nameof (componentName), componentName);

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner
				.AddArgument ("shell")
				.AddArgument ("am")
				.AddArgument ("start")
				.AddArgument ("-n")
				.AddQuotedArgument (componentName);

			AddExtraArguments (runner, extraArguments);

			return await RunTool (() => runner.Run ());
		}

		public async Task<bool> InstallAPK (string packagePath, TimeSpan timeout = default, bool traceAdb = false)
		{
			EnsureParameterValue (nameof (packagePath), packagePath);

			if (timeout == default) {
				timeout = LongTimeout;
			}

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner
				.AddArgument ("install")
				.AddQuotedArgument (packagePath);

			return await RunTool (() => runner.Run ());
		}

		public async Task<bool> UninstallAPK (string packageName, TimeSpan timeout = default, bool traceAdb = false)
		{
			EnsureParameterValue (nameof (packageName), packageName);

			if (timeout == default) {
				timeout = LongTimeout;
			}

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner
				.AddArgument ("uninstall")
				.AddQuotedArgument (packageName);

			return await RunTool (() => runner.Run ());
		}

		public async Task<bool> GrantPermission (string packageName, string permissionName, TimeSpan timeout = default, bool traceAdb = false)
		{
			EnsureParameterValue (nameof (packageName), packageName);
			EnsureParameterValue (nameof (permissionName), permissionName);

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner
				.AddArgument ("shell")
				.AddArgument ("pm")
				.AddArgument ("grant")
				.AddArgument (packageName)
				.AddArgument (permissionName);

			return await RunTool (() => runner.Run ());
		}

		public async Task<bool> Pull (string remoteFilePath, string localFilePath, TimeSpan timeout = default, bool traceAdb = false)
		{
			EnsureParameterValue (nameof (remoteFilePath), remoteFilePath);
			EnsureParameterValue (nameof (localFilePath), localFilePath);

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner
				.AddArgument ("pull")
				.AddQuotedArgument (remoteFilePath)
				.AddQuotedArgument (localFilePath);

			return await RunTool (() => runner.Run ());
		}

		public async Task<bool> Emu (string command, TimeSpan timeout = default, bool traceAdb = false)
		{
			EnsureParameterValue (nameof (command), command);

			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner
				.AddArgument ("emu")
				.AddQuotedArgument (command);

			return await RunTool (() => runner.Run ());
		}

		public async Task<bool> EmuKill (TimeSpan timeout = default, bool traceAdb = false)
		{
			return await Emu ("kill", timeout, traceAdb);
		}

		public async Task<bool> KillServer (TimeSpan timeout = default, bool traceAdb = false)
		{
			ProcessRunner runner = CreateProcessRunner (timeout, traceAdb);
			runner.AddArgument ("kill-server");

			return await RunTool (() => runner.Run ());
		}

		ProcessRunner CreateProcessRunner (TimeSpan timeout, bool traceAdb)
		{
			ProcessRunner runner = CreateProcessRunner ();
			ConfigureCommon (runner, timeout, traceAdb);
			return runner;
		}

		LineForwarder? AddLineCallback (ProcessRunner runner, Action<string>? lineCallback)
		{
			if (lineCallback == default) {
				return null;
			}

			var lf = new LineForwarder (lineCallback);
			runner.AddStandardOutputSink (lf);

			return lf;
		}

		void AddExtraArguments (ProcessRunner runner, List<string>? extraArguments)
		{
			if (extraArguments == null || extraArguments.Count == 0) {
				return;
			}

			foreach (string a in extraArguments) {
				string arg = a.Trim ();
				if (arg.Length == 0) {
					continue;
				}

				runner.AddQuotedArgument (a);
			}
		}

		void ConfigureCommon (ProcessRunner runner, TimeSpan timeout, bool traceAdb)
		{
			if (traceAdb) {
				AddTraceEnvvar (runner);
			}

			if (timeout != default) {
				runner.ProcessTimeout = timeout;
			}

			if (AdbTarget.Length > 0) {
				runner
					.AddArgument ("-s")
					.AddQuotedArgument (AdbTarget);
			}

			if (Context.AdbOptions.Length > 0) {
				string[] args = Context.AdbOptions.Split (Context.NewlineSplit, StringSplitOptions.RemoveEmptyEntries);

				foreach (string arg in args) {
					runner.AddQuotedArgument (arg);
				}
			}
		}

		void AddTraceEnvvar (ProcessRunner runner)
		{
			runner.Environment ["ADB_TRACE"] = "all";
		}
	}
}
