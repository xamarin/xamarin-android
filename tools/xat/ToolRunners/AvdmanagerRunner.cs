using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class AvdmanagerRunner : ToolRunner
	{
		sealed class ShallAnswerThyQuestion : ProcessStandardStreamWrapper
		{
			Action callback;
			bool callbackInvoked;

			public ShallAnswerThyQuestion (Action questionCallback)
			{
				callback = questionCallback;
			}

			protected override string PreprocessMessage (string message, ref bool writeLine, out bool ignoreLine)
			{
				ignoreLine = false;
				if (!callbackInvoked) {
					callback ();
					callbackInvoked = true;
				}

				return message;
			}
		}

		protected override string DefaultToolExecutableName => "avdmanager";
		protected override string ToolName                  => "AVD Manager";

		public AvdmanagerRunner (Context context, Log? log = null, string? toolPath = null)
			: base (context, log, toolPath)
		{
			EchoStandardOutput = true;
			EchoStandardError = true;
		}

		public async Task<bool> Create (string androidABI, string avdName, string systemImageName)
		{
			EnsureParameterValue (nameof (androidABI), androidABI);
			EnsureParameterValue (nameof (avdName), avdName);
			EnsureParameterValue (nameof (systemImageName), systemImageName);

			// AVDmanager has no notion of a "headless" work mode an it will always ask us a question whether we want to
			// create a hardware profile for our emulator image (we don't). Unfortunately, courtesy of
			// System.Diagnostic.Process, we have no way of capturing that question - S.D.Process works in line mode,
			// calling the stdout/stderr callback only when it registers a new line character. However, avdmanager asks
			// the question and waits for the answer on the same line... To work around it we create a generic stdout
			// wrapper which will write to the process stdin as soon as the first line of output is produced. This is
			// fine since the process stdin will buffer what we write and avdmanager will get it once it's ready to read
			// the answer.
			ProcessRunner runner = CreateProcessRunner ();
			runner.StandardOutputEchoWrapper = new ShallAnswerThyQuestion (() => {
				if (runner.Process == null) {
					return;
				}
				runner.Process.StandardInput.WriteLine ();
			});

			runner.StartInfoCallback = (ProcessStartInfo psi) => psi.RedirectStandardInput = true;

			runner
				.AddArgument ("create")
				.AddArgument ("avd")
				.AddArgument ("--abi")
				.AddQuotedArgument (androidABI)
				.AddArgument ("-f")
				.AddArgument ("-n")
				.AddQuotedArgument (avdName)
				.AddArgument ("--package")
				.AddQuotedArgument (systemImageName);

			return await RunTool (() => runner.Run ());
		}
	}
}
