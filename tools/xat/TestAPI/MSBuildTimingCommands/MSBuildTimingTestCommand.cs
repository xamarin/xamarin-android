using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	abstract class MSBuildTimingTestCommand : TestCommand
	{
		public delegate string PathCreatorF (TestMSBuildTiming test, MSBuildTimingTestCommand command);

		public static string XABuild                     { get; set; } = String.Empty;
		public static string TimingLogger                { get; set; } = String.Empty;
		public static string DefaultAndroidSupportedAbis { get; set; } = String.Empty;
		public static PathCreatorF OutputFilePathCreator { get; set; } = (TestMSBuildTiming t, MSBuildTimingTestCommand c) => throw new InvalidOperationException ("Must be provided");

		public abstract string Target                    { get; }
		public abstract string ID                        { get; }

		public MSBuildTimingState? State                 { get; set; }
		public string AndroidSupportedAbis               { get; set; } = DefaultAndroidSupportedAbis;

		public MSBuildTimingTestCommand (string name, string description)
			: base (name, description)
		{}

		protected override async Task<bool> Execute (XATest test)
		{
			if (!(test is TestMSBuildTiming msbuildTimingTest)) {
				throw new InvalidOperationException ($"Test must be of the {nameof(TestMSBuildTiming)} type");
			}

			if (State == null) {
				throw new InvalidOperationException ("State not set");
			}

			EnsurePropertyValue (nameof (Target), Target);
			EnsurePropertyValue (nameof (XABuild), XABuild);
			EnsurePropertyValue (nameof (TimingLogger), TimingLogger);
			EnsurePropertyValue (nameof (AndroidSupportedAbis), AndroidSupportedAbis);

			return await Run (msbuildTimingTest);
		}

		protected override void SetState (TestCommand command)
		{
			if (command is MSBuildTimingTestCommand msbuildTimingCommand) {
				msbuildTimingCommand.State = State;
				return;
			}

			throw new InvalidOperationException ($"Invalid command type {command}");
		}

		protected async Task<bool> RunMSBuild (TestMSBuildTiming test)
		{
			string description = $"{Description} for {test.Name}";
			string xaCommit = Context.Properties.GetRequiredValue (KnownProperties.XAVersionHash);
			string outputFileBase = OutputFilePathCreator (test, this);
			string outputFileXML = $"{outputFileBase}.xml";
			string logFile = $"{outputFileBase}.log";

			LogFilePath = logFile;

			var arguments = new List<string> {
				"/r",
				"/fl",
				$"/flp:LogFile={logFile}",
				$"/logger:{TimingLogger};ID={ID};Description={description};Commit={xaCommit};OutputPath={outputFileXML}",
				$"/p:Configuration={Context.Configuration}",
				$"/p:AndroidSupportedAbis={AndroidSupportedAbis}",
				$"/t:{Target}"
			};

			var msbuild = new MSBuildRunner (Context, msbuildPath: XABuild);
			bool success = await msbuild.Run (
				projectPath: test.ProjectPath,
				logTag: $"MSBuildTiming_{ID}",
				arguments: arguments,
				workingDirectory: test.DirectoryPath
			);

			if (!success) {
				Log.ErrorLine ($"Build of '{test.ProjectPath}' for test '{test.Name}' failed");
				Log.InfoLine ("MSBuild log path: ", Utilities.GetPathRelativeToCWD (logFile), tailColor: ConsoleColor.Cyan);
				return false;
			}

			State!.Results.Add (new MSBuildTimingResult (test.Name, outputFileXML));
			return true;
		}

		protected abstract Task<bool> Run (TestMSBuildTiming test);
	}
}
