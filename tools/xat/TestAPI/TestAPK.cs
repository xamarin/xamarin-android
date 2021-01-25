using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Xamarin.Android.Tests
{
	class TestAPK : XATest
	{
		static APKState? globalState;
		APKState? state;

		public override string KindName => "APK";
		public override string TestFamilyName => KindName;

		public string ApkPath                                    { get; }
		public string AndroidPackageName                         { get; }
		public bool IsApk                                        { get; }
		public string ProjectPath                                => TestFilePath;

		/// <summary>
		///   Lists commands to run before the FIRST test is executed
		/// </summary>
		public static List<TestCommand> GlobalInitCommands { get; set; } = new List<TestCommand> ();

		/// <summary>
		///   Lists commands to run after the LAST test is executed
		/// </summary>
		public static List<TestCommand> GlobalShutdownCommands { get; set; } = new List<TestCommand> ();

		/// <summary>
		///   Name of the Activity via which the UI test is launched.
		/// </summary>
		public string Activity                                   { get; set; } = String.Empty;

		public List<TestAndroidInstrumentation> Instrumentations { get; } = new List<TestAndroidInstrumentation> ();

		public List<string> AndroidPermissions                   { get; } = new List<string> ();

		public string TimingDefinitionsFilename                  { get; set; } = String.Empty;
		public string TimingResultsFilename                      { get; set; } = String.Empty;
		public string ApkSizesInputFilename                      { get; set; } = String.Empty;
		public string ApkSizesDefinitionFilename                 { get; set; } = String.Empty;
		public string ApkSizesResultsFilename                    { get; set; } = String.Empty;
		public APKTestFlavor TestFlavor                          { get; set; } = APKTestFlavor.Plain;

		public TestAPK (string androidPackageName, string name, string projectFilePath, bool isAndroidAppBundle = false)
			: this (GetPackagePath (androidPackageName, isAndroidAppBundle), androidPackageName, name, projectFilePath)
		{
			if (isAndroidAppBundle) {
				TestFlavor = APKTestFlavor.AndroidApplicationBundle;
			}
		}

		public TestAPK (string apkPath, string androidPackageName, string name, string projectFilePath)
			: base (name, projectFilePath, projectFilePath)
		{
			ApkPath = EnsureParameterValue (nameof (apkPath), apkPath);
			AndroidPackageName = EnsureParameterValue (nameof (androidPackageName), androidPackageName);
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
			if (String.Compare (phaseName, GlobalInitPhaseName, StringComparison.Ordinal) == 0) {
				if (globalState == null) {
					globalState = new APKState ();
				}

				return true;
			}

			if (state == null) {
				state = new APKState ();
				if (globalState != null) {
					state.EmulatorProcessId = globalState.EmulatorProcessId;
					state.AdbTarget = globalState.AdbTarget;
					state.SdkVersion = globalState.SdkVersion;
				}
			}

			return true;
		}

		protected override async Task<bool> ExecuteCommand (TestCommand command, string phaseName)
		{
			bool isGlobalPhase = String.Compare (phaseName, GlobalInitPhaseName, StringComparison.Ordinal) == 0 ||
				String.Compare (phaseName, GlobalShutdownPhaseName, StringComparison.Ordinal) == 0;

			switch (command) {
				case APK.APKTestCommand apkCommand:
					if (isGlobalPhase) {
						apkCommand.State = globalState;
					} else {
						apkCommand.State = state;
					}
					break;

				case Shared.SharedTestCommand sharedCommand:
					break;

				default:
					throw new InvalidOperationException ($"Command must be of the {nameof(APK.APKTestCommand)} or {nameof(Shared.SharedTestCommand)} types");
			}

			return await base.ExecuteCommand (command, phaseName);
		}

		static string GetPackagePath (string androidPackageName, bool isAndroidAppBundle)
		{
			if (isAndroidAppBundle) {
				return TestUtilities.GetApkPath (androidPackageName, "aab");
			}

			return TestUtilities.GetApkPath (androidPackageName);
		}
	}
}
