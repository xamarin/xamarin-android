using System;
using System.Threading.Tasks;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class ProcessMSBuildTiming : MSBuildTimingTestCommand
	{
		Func<TestMSBuildTiming, string> resultsFilePathCreator;
		string labelSuffix;

		public override string Target => "UNUSED";
		public override string ID     => nameof (ProcessMSBuildTiming);

		public ProcessMSBuildTiming (Func<TestMSBuildTiming, string> resultsFilePathCreator, string labelSuffix)
			: base (nameof (ProcessMSBuildTiming), "Process MSBuild timing results")
		{
			this.resultsFilePathCreator = resultsFilePathCreator;
			this.labelSuffix = EnsureParameterValue (nameof (labelSuffix), labelSuffix);
		}

#pragma warning disable 1998
		protected async override Task<bool> Run (TestMSBuildTiming test)
		{
			string resultsFilePath = resultsFilePathCreator (test);
			var processor = new MSBuildTimingProcessor (State!.Results, resultsFilePath, labelSuffix);
			bool success = processor.Run ();

			if (test.FailedCommands.Count > 0) {
				Log.WarningLine ($"Some commands in '{test.Name}' failed. Timing results might be inaccurate.");
			}

			Log.InfoLine ($"MSBuild timing results for '{test.Name}': ", resultsFilePath);
			return success;
		}
#pragma warning restore 1998
	}
}
