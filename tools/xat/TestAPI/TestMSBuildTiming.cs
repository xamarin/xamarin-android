using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class TestMSBuildTiming : XATest
	{
		static MSBuildTimingState? globalState;
		MSBuildTimingState? state;

		public override string KindName       => "MSBuild Timing";
		public override string TestFamilyName => KindName;
		public string ProjectPath             => TestFilePath;

		/// <summary>
		///   Lists commands to run before the FIRST test is executed
		/// </summary>
		public static List<TestCommand> GlobalInitCommands     { get; set; } = new List<TestCommand> ();

		/// <summary>
		///   Lists commands to run after the LAST test is executed
		/// </summary>
		public static List<TestCommand> GlobalShutdownCommands { get; set; } = new List<TestCommand> ();

		public string ShortName                                { get; }
		public string DirectoryPath                            { get; set; } = String.Empty;
		public string CSharpFile                               { get; set; } = String.Empty;
		public string AndroidResourceFile                      { get; set; } = String.Empty;
		public string Restore                                  { get; set; } = String.Empty;
		public string Repo                                     { get; set; } = String.Empty;
		public string Commit                                   { get; set; } = String.Empty;

		public string OutputDirPath                            { get; set; } = String.Empty;
		public string TimingResultsPath                        { get; protected set; } = String.Empty;

		public TestMSBuildTiming (string name, string shortName, string testProjectFilePath)
			: base ($"MSBuildTiming: {name}", testProjectFilePath, testProjectFilePath)
		{
			ShortName = EnsureParameterValue (nameof (shortName), shortName);
		}

		public override async Task<bool> RunGlobalInitCommands ()
		{
			return await ExecuteCommands (GlobalInitCommands, GlobalInitPhaseName);
		}

		public override async Task<bool> RunGlobalShutdownCommands ()
		{
			return await ExecuteCommands (GlobalShutdownCommands, GlobalShutdownPhaseName);
		}

		protected override bool BeforeExecuteCommands (List<TestCommand> commands, string phaseName)
		{
			EnsurePropertyValue (nameof (DirectoryPath), DirectoryPath);
			EnsurePropertyValue (nameof (CSharpFile), CSharpFile);
			EnsurePropertyValue (nameof (AndroidResourceFile), CSharpFile);
			EnsurePropertyValue (nameof (OutputDirPath), OutputDirPath);

			DirectoryPath = Utilities.EnsureFullPath (DirectoryPath);

			if (String.Compare (phaseName, GlobalInitPhaseName, StringComparison.Ordinal) == 0 && globalState == null) {
				globalState = new MSBuildTimingState ();
				return true;
			}

			if (state == null) {
				state = new MSBuildTimingState ();
				if (globalState != null) {
					state.EmulatorProcessId = globalState.EmulatorProcessId;
					state.AdbTarget = globalState.AdbTarget;
				}
			}

			return true;
		}

		protected override async Task<bool> ExecuteCommand (TestCommand command, string phaseName)
		{
			switch (command) {
				case MSBuildTiming.MSBuildTimingTestCommand msbuildTimingCommand:
					if (IsGlobalPhase (phaseName)) {
						msbuildTimingCommand.State = globalState;
					} else {
						msbuildTimingCommand.State = state;
					}
					break;

				case Shared.SharedTestCommand sharedCommand:
					break;

				default:
					throw new InvalidOperationException ($"Command must be of the {nameof(Shared.SharedTestCommand)} or {nameof(MSBuildTiming.MSBuildTimingTestCommand)} types");
			}

			return await base.ExecuteCommand (command, phaseName);
		}
	}
}
