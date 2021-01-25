using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	/// <summary>
	///   Represents a "command" to run at a certain stage of the test execution. The command can be either an actual
	///   external/shell command or something implemented fully in code. The latter should support a "shell
	///   representation" to be used when generating shell scripts.
	/// </summary>
	abstract class TestCommand : AppObject
	{
		public string Name                       { get; }
		public string Description                { get; }

		/// <summary>
		///   How to fail. Ignored when <see cref="Commands"/> is not empty.
		/// <summary>
		public CommandFailureMode FailureMode    { get; set; } = CommandFailureMode.WarnAndContinue;

		public bool RunsNestedCommands => Commands.Count > 0;

		/// <summary>
		///   A list of subcommands to be executed instead of calling <see cref="Execute"/>
		/// </summary>
		public List<TestCommand> Commands        { get; } = new List<TestCommand> ();
		public List<TestCommand> FailedCommands  { get; private set; } = new List<TestCommand> ();
		public string LogFilePath                { get; protected set; } = String.Empty;

		protected TestCommand (string name, string description)
		{
			Name = EnsureValidArgument (name, nameof(name));
			Description = EnsureValidArgument (description, nameof(description));
		}

		public async Task<bool> Run (XATest test)
		{
			Log.DebugLine ();
			if (Commands.Count == 0) {
				Log.DebugLine ($"Running command: {Name}");
				return LogResultAndReturn (await Execute (test));
			}

			Log.DebugLine ("Running nested commands");
			bool ret = true;
			foreach (TestCommand command in Commands) {
				if (command == this) {
					continue;
				}

				SetState (command);
				if (await command.Run (test)) {
					continue;
				}

				FailedCommands.Add (command);
				if (command.FailureMode == CommandFailureMode.Error) {
					return LogResultAndReturn (false);
				}

				ret = false;
			}

			return LogResultAndReturn (ret);
		}

		protected bool LogResultAndReturn (bool result)
		{
			const string Failure = "failed";
			const string Success = "succeeded";

			Log.DebugLine ($"Command finished: {Name} [{(result ? Success : Failure)}]");
			return result;
		}

		protected virtual void SetState (TestCommand command)
		{}

		protected abstract Task<bool> Execute (XATest test);

		public bool WriteShellScript (ShellScriptWriter scriptWriter)
		{
			return true;
		}

		string EnsureValidArgument (string value, string name)
		{
			if (value.Length == 0)
				throw new ArgumentException ("must not be empty", name);
			return value;
		}
	}
}
