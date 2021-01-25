using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class TestHostUnit : XATest
	{
		static AndroidState? globalState;
		AndroidState? state;

		public override string KindName => "Host Unit";
		public override string TestFamilyName { get; }

		public string TestAssemblyPath => TestFilePath;

		public string OutputFilePath { get; set; } = String.Empty;
		public string ResultPath     { get; set; } = String.Empty;
		public TimeSpan Timeout      { get; set; } = TimeSpan.FromMinutes (15);

		/// <summary>
		///   Lists commands to run before the FIRST test is executed
		/// </summary>
		public List<TestCommand> GlobalInitCommands { get; set; } = new List<TestCommand> ();

		/// <summary>
		///   Lists commands to run after the LAST test is executed
		/// </summary>
		public List<TestCommand> GlobalShutdownCommands { get; set; } = new List<TestCommand> ();

		public TestHostUnit (string familyName, string name, string testAssemblyPath, string testProjectFilePath)
			: base (name, testAssemblyPath, testProjectFilePath)
		{
			TestFamilyName = EnsureParameterValue (nameof (familyName), familyName);
			string testFileName = Path.GetFileName (testAssemblyPath);

			OutputFilePath = MakeOutputFilePath (testFileName);
			ResultPath = MakeResultPath (testFileName);
		}

		protected override bool BeforeExecuteCommands (List<TestCommand> commands, string phaseName)
		{
			EnsurePropertyValue (nameof (OutputFilePath), OutputFilePath);
			EnsurePropertyValue (nameof (ResultPath), ResultPath);

			if (String.Compare (phaseName, GlobalInitPhaseName, StringComparison.Ordinal) == 0 && globalState == null) {
				globalState = new AndroidState ();
				return true;
			}

			if (state == null) {
				state = new AndroidState ();
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
				case Host.HostTestCommand hostCommand:
					if (IsGlobalPhase (phaseName)) {
						hostCommand.State = globalState;
					} else {
						hostCommand.State = state;
					}
					break;

				case Shared.SharedTestCommand sharedCommand:
					break;

				default:
					throw new InvalidOperationException ($"Command must be of the {nameof(Host.HostTestCommand)} or {nameof(Shared.SharedTestCommand)} types");
			}

			return await base.ExecuteCommand (command, phaseName);
		}

		public override async Task<bool> RunGlobalInitCommands ()
		{
			return await ExecuteCommands (GlobalInitCommands, GlobalInitPhaseName);
		}

		public override async Task<bool> RunGlobalShutdownCommands ()
		{
			return await ExecuteCommands (GlobalShutdownCommands, GlobalShutdownPhaseName);
		}

		protected string MakeOutputFilePath (string testFileName)
		{
			return Path.Combine (Configurables.Paths.NUnitOutputDir, $"TestOutput-{testFileName}.txt");
		}

		protected string MakeResultPath (string testFileName)
		{
			return Path.Combine (Configurables.Paths.NUnitResultDir, $"TestResult-{testFileName}.xml");
		}
	}
}
