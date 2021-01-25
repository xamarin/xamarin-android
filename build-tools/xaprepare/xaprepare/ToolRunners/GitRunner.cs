using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Xamarin.Android.Prepare
{
	partial class GitRunner : ToolRunner
	{
		// Redirects git progress output from standard error to standard output so that we show less red on the screen
		sealed class GitProgressStderrWrapper : ProcessStandardStreamWrapper
		{
			public readonly List<string> Messages = new List<string> ();

			protected override string PreprocessMessage (string message, ref bool writeLine, out bool ignoreLine)
			{
				ignoreLine = true;

				if (message.Length == 0)
					return message;

				Messages.Add (message);
				Log.Instance.MessageLine (message);
				ignoreLine = true;

				return message;
			}
		}

		// These are passed to `git` itself *before* the command
		static readonly List<string> standardGlobalOptions = new List<string> {
			"--no-pager"
		};

		protected override string DefaultToolExecutableName => "git";
		protected override string ToolName                  => "Git";

		public GitRunner (Context context, Log? log = null, string? gitPath = null)
			: base (context, log, gitPath ?? context.Tools.GitPath)
		{
			ProcessTimeout = TimeSpan.FromMinutes (30);
		}

		/// <summary>
		///   Perform the clean operation in the current directory or, if <paramref name="path"/> is given, in the
		///   specified directory.
		/// </summary>
		///
		/// <param name="path">path to perform the clean operation in. May contain space-separated list of paths</param>
		/// <param name="force">add the <c>--force</c> argument, if <c>true</c></param>
		/// <param name="recurse">add the <c>-d</c> argument, to recurse into untracked directories when cleaning, if <c>true</c></param>
		/// <param name="noStandardIgnoreRules">Add the <c>-x</c> argument, to clean all untracked files, if <c>true</c></param>
		/// <param name="workingDirectory">set the working directory, if not <c>null</c> or empty</param>
		public async Task<bool> Clean (string? path = null, bool force = false, bool recurse = false, bool noStandardIgnoreRules = false, string? workingDirectory = null)
		{
			var runner = CreateGitRunner (workingDirectory);
			runner.AddArgument ("clean");
			if (force) {
				runner.AddArgument ("--force");
			}

			if (recurse) {
				runner.AddArgument ("-d");
			}

			if (noStandardIgnoreRules) {
				runner.AddArgument ("-x");
			}

			if (!String.IsNullOrEmpty (path)) {
				runner.AddQuotedArgument (path!);
			}

			return await RunGit (runner);
		}

		public async Task<bool> CheckoutCommit (string repositoryPath, string commitRef, bool force = true)
		{
			if (String.IsNullOrEmpty (repositoryPath))
				throw new ArgumentException ("must not be null or empty", nameof (repositoryPath));
			if (String.IsNullOrEmpty (commitRef))
				throw new ArgumentException ("must not be null or empty", nameof (commitRef));

			var runner = CreateGitRunner (repositoryPath, useCustomStderrWrapper: true);
			runner.AddArgument ("checkout");
			if (force)
				runner.AddArgument ("--force");
			runner.AddArgument ("--progress");
			runner.AddQuotedArgument (commitRef);

			return await RunGit (runner, $"checkout-{Path.GetFileName (repositoryPath)}-{commitRef}");
		}

		public async Task<bool> Fetch (string repositoryPath)
		{
			if (String.IsNullOrEmpty (repositoryPath))
				throw new ArgumentException ("must not be null or empty", nameof (repositoryPath));

			if (!Directory.Exists (repositoryPath)) {
				Log.ErrorLine ($"Repository {repositoryPath} does not exist");
				return false;
			}

			var runner = CreateGitRunner (repositoryPath, useCustomStderrWrapper: true);
			runner.AddArgument ("fetch");
			runner.AddArgument ("--all");
			runner.AddArgument ("--no-recurse-submodules");
			runner.AddArgument ("--progress");

			return await RunGit (runner, $"fetch-{Path.GetFileName (repositoryPath)}");
		}

		public async Task<bool> Clone (string url, string destinationDirectoryPath)
		{
			if (String.IsNullOrEmpty (url))
				throw new ArgumentException ("must not be null or empty", nameof (url));
			if (String.IsNullOrEmpty (destinationDirectoryPath))
				throw new ArgumentException ("must not be null or empty", nameof (destinationDirectoryPath));

			string parentDir = Path.GetDirectoryName (destinationDirectoryPath);
			string dirName = Path.GetFileName (destinationDirectoryPath);
			Utilities.CreateDirectory (parentDir);
			var runner = CreateGitRunner (parentDir, useCustomStderrWrapper: true);
			runner.AddArgument ("clone");
			runner.AddArgument ("--progress");
			runner.AddQuotedArgument (url);

			return await RunGit (runner, $"clone-{dirName}");
		}

		public async Task<List<string>?> SubmoduleStatus (string? workingDirectory = null)
		{
			string runnerWorkingDirectory = DetermineRunnerWorkingDirectory (workingDirectory);

			var runner = CreateGitRunner (runnerWorkingDirectory);;
			runner.AddArgument ("submodule");
			runner.AddArgument ("status");

			var lines = new List<string> ();

			bool success = await RunTool (
				() => {
					using (var outputSink = (OutputSink)SetupOutputSink (runner)) {
						outputSink.LineCallback = (string line) => lines.Add (line);
						return runner.Run ();
					}
				}
			);

			if (!success)
				return null;

			return lines;
		}

		public async Task<bool> SubmoduleUpdate (string? workingDirectory = null, bool init = true, bool recursive = true)
		{
			string runnerWorkingDirectory = DetermineRunnerWorkingDirectory (workingDirectory);

			var runner = CreateGitRunner (runnerWorkingDirectory, useCustomStderrWrapper: true);;
			runner.AddArgument ("submodule");
			runner.AddArgument ("update");
			if (init)
				runner.AddArgument ("--init");
			if (recursive)
				runner.AddArgument ("--recursive");

			return await RunGit (runner, "submodule-update");
		}

		public string GetTopCommitHash (string? workingDirectory = null, bool shortHash = true)
		{
			string runnerWorkingDirectory = DetermineRunnerWorkingDirectory (workingDirectory);

			var runner = CreateGitRunner (runnerWorkingDirectory);
			runner.AddArgument ("rev-parse");
			runner.AddArgument ("HEAD");

			Log.StatusLine (GetLogMessage (runner), CommandMessageColor);

			string hash = String.Empty;
			using (var outputSink = (OutputSink)SetupOutputSink (runner)) {
				outputSink.LineCallback = (string line) => {
					if (!String.IsNullOrEmpty (hash))
						return;
					hash = line.Trim ();
				};

				if (!runner.Run ())
					return String.Empty;

				if (shortHash)
					return Utilities.ShortenGitHash (hash);

				return hash;
			}
		}

		public async Task<IList<BlamePorcelainEntry>?> Blame (string filePath)
		{
			return await Blame (filePath, gitArguments: null, blameArguments: null, workingDirectory: null);
		}

		public async Task<IList<BlamePorcelainEntry>?> Blame (string filePath, List <string> blameArguments)
		{
			return await Blame (filePath, gitArguments: null, blameArguments: blameArguments, workingDirectory: null);
		}

		public async Task<IList<BlamePorcelainEntry>?> Blame (string filePath, List<string>? gitArguments, List <string>? blameArguments, string? workingDirectory = null)
		{
			if (String.IsNullOrEmpty(filePath))
				throw new ArgumentException ("must not be null or empty", nameof (filePath));

			var runner = CreateGitRunner (workingDirectory, gitArguments);
			SetCommandArguments (runner, "blame", blameArguments);
			runner.AddArgument ("-p");
			runner.AddQuotedArgument (filePath);

			var parserState = new BlameParserState ();

			Log.StatusLine (GetLogMessage (runner), CommandMessageColor);
			bool success = await RunTool (
				() => {
					using (var outputSink = (OutputSink)SetupOutputSink (runner)) {
						outputSink.LineCallback = (string line) => ParseBlameLine (line, parserState);
						runner.WorkingDirectory = DetermineRunnerWorkingDirectory (workingDirectory);
						return runner.Run ();
					}
				}
			);

			if (!success)
				return null;

			return parserState.Entries;
		}

		public async Task<List<string>?> ConfigList (string[] fileOptions, string? workingDirectory = null)
		{
			var runner = CreateGitRunner (workingDirectory);
			runner.AddArgument ("config");
			foreach (var opt in fileOptions)
				runner.AddArgument (opt);
			runner.AddArgument ("--list");

			var lines = new List<string> ();

			bool success = await RunTool (
				() => {
					using (var outputSink = (OutputSink)SetupOutputSink (runner)) {
						outputSink.LineCallback = (string line) => lines.Add (line);
						return runner.Run ();
					}
				}
			);

			if (!success)
				return null;

			return lines;
		}

		public async Task<bool> IsRepoUrlHttps (string workingDirectory)
		{
			if (!Directory.Exists (workingDirectory))
				throw new ArgumentException ("must exist", nameof (workingDirectory));

			var runner = CreateGitRunner (workingDirectory);
			runner.AddArgument ("config");
			runner.AddArgument ("--get");
			runner.AddArgument ("remote.origin.url");

			bool containsHttps = false;

			bool success = await RunTool (
				() => {
					using (var outputSink = (OutputSink)SetupOutputSink (runner)) {
						outputSink.LineCallback = (string line) => {
							containsHttps = !string.IsNullOrEmpty (line) && line.Contains ("https://");
						};
						runner.WorkingDirectory = DetermineRunnerWorkingDirectory (workingDirectory);
						return runner.Run ();
					}
				}
			);

			if (!success)
				return false;

			return containsHttps;
		}

		public async Task<List<string>> RunCommandForOutputAsync (string workingDirectory, params string[] args)
		{
			if (!Directory.Exists (workingDirectory))
				throw new ArgumentException ("must exist", nameof (workingDirectory));

			var runner = CreateGitRunner (workingDirectory);
			runner.AddArguments (args);
			return await RunForOutputAsync (runner);
		}

		async Task<List<string>> RunForOutputAsync (ProcessRunner runner)
		{
			var lines = new List<string> ();
			bool success = await RunTool (
				() => {
					using (var outputSink = (OutputSink) SetupOutputSink (runner)) {
						outputSink.LineCallback = (string line) => lines.Add (line);
						return runner.Run ();
					}
				}
			);

			if (!success) {
				return new List<string>();
			}

			return lines;
		}

		ProcessRunner CreateGitRunner (string? workingDirectory, List<string>? arguments = null, bool useCustomStderrWrapper = false)
		{
			var runner = CreateProcessRunner ();
			runner.WorkingDirectory = workingDirectory;
			if (useCustomStderrWrapper) {
				runner.StandardErrorEchoWrapper = new GitProgressStderrWrapper {
					LoggingLevel = runner.EchoStandardErrorLevel,
					CustomSeverityName = ProcessRunner.StderrSeverityName,
				};
			}

			SetGitArguments (runner, workingDirectory, arguments);

			return runner;
		}

		async Task<bool> RunGit (ProcessRunner runner, string? logTag = null)
		{
			try {
				return await RunTool (
					() => {
						using (var outputSink = (OutputSink)SetupOutputSink (runner)) {
							StartTwiddler ();
							return runner.Run ();
						}
					}
				);
			} finally {
				StopTwiddler ();
				if (runner.ExitCode != 0 && runner.StandardErrorEchoWrapper is GitProgressStderrWrapper wrapper) {
					foreach (string message in wrapper.Messages) {
						Log.ErrorLine (message);
					}
				}
			}
		}

		void ParseBlameLine (string line, BlameParserState state)
		{
			if (state.CurrentEntry == null)
				state.CurrentEntry = new BlamePorcelainEntry ();

			if (!state.CurrentEntry.ProcessLine (line))
				return;

			state.Entries.Add (state.CurrentEntry);
			state.CurrentEntry = null;
		}

		protected override TextWriter CreateLogSink (string? logFilePath)
		{
			return new OutputSink (Log, logFilePath);
		}

		void SetCommandArguments (ProcessRunner runner, string command, List<string>? commandArguments)
		{
			runner.AddArgument (command);
			if (commandArguments == null || commandArguments.Count == 0)
				return;
			AddArguments (runner, commandArguments);
		}

		string DetermineRunnerWorkingDirectory (string? workingDirectory)
		{
			if (!String.IsNullOrEmpty (workingDirectory))
				return workingDirectory!;

			return BuildPaths.XamarinAndroidSourceRoot;
		}

		void SetGitArguments (ProcessRunner runner, string? workingDirectory, List<string>? gitArguments)
		{
			foreach (string arg in standardGlobalOptions) {
				runner.AddArgument (arg);
			}

			if (!String.IsNullOrEmpty (workingDirectory)) {
				runner.AddArgument ("-C");
				runner.AddQuotedArgument (workingDirectory!);
			}

			AddArguments (runner, gitArguments);
		}
	}
}
