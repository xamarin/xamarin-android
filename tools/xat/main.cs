using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Mono.Options;
using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	abstract class XatCommandOption : Command
	{
		protected bool ShowHelp { get; set; }

		protected XatCommandOption (string name, string description)
			: base (name, description)
		{}

		protected async Task<int> Invoke (XatCommand cmd)
		{
			return await cmd.Invoke () ? 0 : 1;
		}

		protected bool ParseOptions (IEnumerable<string> args)
		{
			Options.Parse (args);
			if (ShowHelp) {
				Options.WriteOptionDescriptions (CommandSet.Out);
				return true;
			}

			return false;
		}
	}

	class ListCommand : XatCommandOption
	{
		ListItem whatToList = ListItem.None;
		bool verbose;

		public ListCommand ()
			: base ("list", "List known tests/categories/etc")
		{
			Options = new OptionSet {
				"usage: xat list [OPTIONS]",
				"By default the command lists everything. Options can be combined",
				"",
				{ "g|groups", "list only test groups", v => whatToList |= ListItem.Groups },
				{ "s|suites", "list only test suites", v => whatToList |= ListItem.Suites },
				{ "" },
				{ "m|more", "list more information", v => verbose = true },
				{ "?|h|help", "show this help", v => ShowHelp = v != null },
			};
		}

		public override int Invoke (IEnumerable<string> args)
		{
			if (ParseOptions (args)) {
				return 0;
			}

			var list = new List (whatToList == ListItem.None ? ListItem.All : whatToList, verbose);
			return Invoke (list).Result;
		}
	}

	class RunCommand : XatCommandOption
	{
		static readonly char[] CommaSplit = new []{ ',' };

		HashSet<string> usedGroupNames = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		List<string> groupNames = new List<string> ();

		HashSet<string> usedSuiteNames = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		List<string> suiteNames = new List<string> ();

		HashSet<string> usedTestNames = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		List<string> testNames = new List<string> ();

		HashSet<string> usedIncludeTests = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		List<string> includeTests = new List<string> ();

		HashSet<string> usedExcludeTests = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		List<string> excludeTests = new List<string> ();

		HashSet<string> usedIncludeCategories = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		List<string> includeCategories = new List<string> ();

		HashSet<string> usedExcludeCategories = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		List<string> excludeCategories = new List<string> ();

		public RunCommand ()
			: base ("run", "Invoke selected tests/categories")
		{
			Options = new OptionSet {
				"usage: xat run [OPTIONS]",
				"By default the command runs all the tests. Options can be combined and repeated on the command line unless stated otherwise",
				"",
				{ "m|msbuild=", "Use the specified MSBuild {BINARY}. If omitted, XAT will first try to find `xabuild` and fall back to `msbuild`", v => Context.Instance.MSBuildBinary = v },
				"",
				{ "g|group=", "Run the specified {GROUPS}. Takes a comma-separated list of test group names or can be repeated on command line", v => AddName ("test group", groupNames, usedGroupNames, v) },
				{ "s|suite=", "Run the specified {SUITES}. Takes a comma-separated list of test suite names or can be repeated on command line", v => AddName ("test suite", suiteNames, usedSuiteNames, v) },
				"",
				"The arguments below use suite-specific format for each entry. They are ignored if a test suite runs as part of predefined group:",
				{ "t|test=", "Run the specified {TESTS}. Takes a comma-separated list of test names", v => AddName ("test", testNames, usedTestNames, v) },
				{ "i|include-categories=", "Run tests in the specified {CATEGORIES}. Takes a comma-separated list of test categories", v => AddName ("category to include", includeCategories, usedIncludeCategories, v) },
				{ "e|exclude-categories=", "Do not tun tests in the specified {CATEGORIES}. Takes a comma-separated list of test categories", v => AddName ("category to exclude", excludeCategories, usedExcludeCategories, v) },
				{ "j|include-tests=", "Run the specified {TESTS}. Takes a comma-separated list of test names", v => AddName ("test to include", includeTests, usedIncludeTests, v) },
				{ "f|exclude-tests=", "Do not run the specified {TESTS}. Takes a comma-separated list of test names", v => AddName ("test to exclude", excludeTests, usedExcludeTests, v) },
				"",
				{ "n|new-emulator", "Create emulator AVD even if it already exists", v => Context.Instance.RequireNewEmulator = v != null },
				{ "d|device=", "ID/name of Android {DEVICE} to use", v => Context.Instance.AdbTarget = v },
				{ "a|adb-options=", "Additional {OPTIONS} to pass to ADB", v => Context.Instance.AdbOptions = v },
				{ "o|nunit-options=", "Additional {OPTIONS} to pass to NUnit console runner", v => Context.Instance.NUnitOptions = v },
				"",
				{ "?|h|help", "Show this help", v => ShowHelp = v != null },
			};
		}

		public override int Invoke (IEnumerable<string> args)
		{
			Log.Instance.DebugLine ("Parsing command line arguments");
			if (ParseOptions (args)) {
				return 0;
			}

			var run = new Run ();
			run.GroupNames.AddRange (groupNames);
			run.SuiteNames.AddRange (suiteNames);
			run.TestNames.AddRange (testNames);
			run.IncludeCategories.AddRange (includeCategories);
			run.ExcludeCategories.AddRange (excludeCategories);
			run.IncludeTests.AddRange (includeTests);
			run.ExcludeTests.AddRange (excludeTests);

			return Invoke (run).Result;
		}

		void AddName (string desc, List<string> list, HashSet<string> names, string v)
		{
			string val = v.Trim ();
			if (val.Length == 0 || names.Contains (val))
				return;
			foreach (string name in v.Split (CommaSplit, StringSplitOptions.RemoveEmptyEntries)) {
				if (names.Contains (name)) {
					continue;
				}

				Log.Instance.DebugLine ($" {Context.Instance.Characters.Bullet} adding {desc}: {name}");
				names.Add (name);
				list.Add (name);
			}
		}
	}

	class App
	{
		public static int Main (string[] args)
		{
			Context.Instance.Init ();
			Log.SetContext (Context.Instance);
			Log.Instance.SetLogFile (Context.Instance.MainLogFilePath);

			var commands = new CommandSet ("xat") {
				"usage: xat COMMAND [OPTIONS]",
				"",
				$"Runs xamarin-android unit tests and applications.",
				"",
				"See tools/xat/README.md for more information",
				"",
				"Global options:",
				{"v|verbosity=", $"Set console log verbosity to {{LEVEL}}. Level name may be abbreviated to the smallest unique part (one of: {GetVerbosityLevels ()}). Default: {Context.Instance.LoggingVerbosity.ToString().ToLowerInvariant ()}", v => Context.Instance.LoggingVerbosity = ParseLogVerbosity (v) },
				{"c|configuration=", $"Set build {{CONFIGURATION}} instead of the default '{Context.Instance.Configuration}'", v => Context.Instance.Configuration = v.Trim () },
				{"D|dull-mode", "Run xat in \"dull mode\" - no colors, no emoji", v => Context.Instance.DullMode = true },
				{"E|no-emoji", "Run xat without using emoji, but still allow color", v => Context.Instance.NoEmoji = true },
				"",
				"Available commands:",
				new ListCommand (),
				new RunCommand (),
				"",
				"Examples:",
				"",
			};

			try {
				return commands.Run (args);
			} catch (AggregateException aex) {
				foreach (Exception ex in aex.InnerExceptions) {
					PrintException (ex);
				}
			} catch (Exception ex) {
				PrintException (ex);
			} finally {
				Log.Instance.Dispose ();
				ResetConsoleColors ();
			}

			return 1;

			void PrintException (Exception ex)
			{
				Log.Instance.ErrorLine (showSeverity: false);
				Log.Instance.ErrorLine (ex.Message, showSeverity: false);
				Log.Instance.ErrorLine (ex.ToString (), showSeverity: false);
				Log.Instance.ErrorLine (showSeverity: false);
			}
		}

		static void ResetConsoleColors ()
		{
			try {
				Console.CursorVisible = true;
				Console.ResetColor ();
			} catch {
				// Ignore
			}
		}

		static string GetVerbosityLevels ()
		{
			return EnumNamesToCommaSeparatedList<LoggingVerbosity> ();
		}

		static string EnumNamesToCommaSeparatedList<T> ()
		{
			return String.Join (", ", GetEnumNames<T> ());
		}

		static IEnumerable<string> GetEnumNames <T> ()
		{
			return Enum.GetNames (typeof (T)).Select (v => v.ToLowerInvariant ());
		}

		static LoggingVerbosity ParseLogVerbosity (string name)
		{
			switch (Char.ToLowerInvariant (name [0])) {
				case 's':
					return LoggingVerbosity.Silent;

				case 'q':
					return LoggingVerbosity.Quiet;

				case 'n':
					return LoggingVerbosity.Normal;

				case 'v':
					return LoggingVerbosity.Verbose;

				case 'd':
					return LoggingVerbosity.Diagnostic;

				default:
					throw new InvalidOperationException ($"Unknown logging verbosity level '{name}'");
			}
		}
	}
}
