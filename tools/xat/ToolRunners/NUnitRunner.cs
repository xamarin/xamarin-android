using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class NUnitRunner : ToolRunner
	{
		/// <summary>
		///   This class exists to reduce the number of arguments to Run
		/// </summary>
		public sealed class TestCriteria
		{
			public List<string>? TestNames;
			public List<string>? IncludeCategories;
			public List<string>? ExcludeCategories;
			public List<string>? IncludeTests;
			public List<string>? ExcludeTests;
		}

		protected override string DefaultToolExecutableName => "nunit3-console.exe";
		protected override string ToolName                  => "NUnit";

		public NUnitRunner (Context context, Log? log = null, string? toolPath = null)
			: base (context, log, toolPath)
		{}

		public async Task<bool> Run (string assemblyPath, string resultFilePath, string? textOutputPath = null, TestCriteria? testCriteria = null, TimeSpan timeout = default)
		{
			EnsureParameterValue (nameof (assemblyPath), assemblyPath);
			EnsureParameterValue (nameof (resultFilePath), resultFilePath);

			ProcessRunner runner = CreateProcessRunner ();
			runner.DoNotKillOnTimeout = true;
			runner.WorkingDirectory = BuildPaths.XamarinAndroidSourceRoot;

			if (timeout != default) {
				Log.InfoLine ("NUnit runner timeout: ", timeout.ToString ());
				runner.ProcessTimeout = timeout;
			}

			bool foundWhere = false;
			if (Context.NUnitOptions.Length > 0) {
				string[] args = Context.NUnitOptions.Split (Context.WhitespaceSplit, StringSplitOptions.RemoveEmptyEntries);

				foreach (string arg in args) {
					if (arg.StartsWith ("--where", StringComparison.Ordinal)) {
						foundWhere = true;
					}

					runner.AddQuotedArgument (arg);
				}
			}

			AddTestCriteria (runner, testCriteria, out bool addedWhere);

			if (!String.IsNullOrEmpty (textOutputPath)) {
				runner.AddArgumentWithQuotedValue ("--output=", textOutputPath!);
			}

			runner
				.AddArgument ("--labels=BeforeAndAfter")
				.AddArgumentWithQuotedValue ("--result=", resultFilePath)
				.AddQuotedArgument (assemblyPath);

			if (foundWhere && addedWhere) {
				Log.WarningLine ("NUnit `--where` option was added from the `xat` command line as well as to specify test category conditions.");
				Log.WarningLine ("NUnit honors only the last occurrence of `--where`");
				Log.WarningLine ("The full command line now looks as follows:");
				Log.StatusLine  ($"  {runner.FullCommandLine}");
			}

			bool success = await RunTool (() => runner.Run ());
#if WINDOWS
			return success;
#else
			if (success) {
				return true;
			}

			// See docs on ProcessRunner.DoNotKillOnTimeout for explanation
			if (!runner.TimedOut) {
				return false;
			}

			// We have no way of finding what the child processes were now since once the main process is gone, they are
			// reparented to init (PID 1)
			Log.WarningLine ($"NUnit runner (PID {runner.ProcessId}) timed out, some child processes might have been left behind.");

			// TODO: consider killing the nunig-agent.exe process by matching its full path (since we know it) - might
			// be dangerous, as we might kill not the process that was created by our runner, but the danger is rather
			// small.
			return false;
#endif
		}

		void AddTestCriteria (ProcessRunner runner, TestCriteria? testCriteria, out bool addedWhere)
		{
			addedWhere = false;
			if (testCriteria == null) {
				return;
			}

			AddList ("--test=", testCriteria.TestNames);
			AddList ("--include=", testCriteria.IncludeTests);
			AddList ("--exclude=", testCriteria.ExcludeTests);

			// Only the last --where parameter is considered, so we need to put all the category expressions together
			var catlist = new List<string> ();
			AddCategories ("==", testCriteria.IncludeCategories);
			AddCategories ("!=", testCriteria.ExcludeCategories);

			if (catlist.Count > 0) {
				runner.AddArgumentWithQuotedValue ("--where=", String.Join (" && ", catlist));
				addedWhere = true;
			}

			void AddList (string arg, List<string>? list)
			{
				if (list == null || list.Count == 0) {
					return;
				}

				runner.AddArgumentWithQuotedValue (arg, String.Join (",", list));
			}

			void AddCategories (string op, List<string>? list)
			{
				if (list == null || list.Count == 0) {
					return;
				}

				foreach (string cat in list) {
					catlist.Add ($"cat{op}{cat}");
				}
			}
		}
	}
}
