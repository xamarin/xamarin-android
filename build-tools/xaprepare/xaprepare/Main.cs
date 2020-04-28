using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Mono.Options;

namespace Xamarin.Android.Prepare
{
	class App
	{
		sealed class ParsedOptions
		{
			public bool ShowHelp               { get; set; } = false;
			public bool DumpProps              { get; set; } = false;
			public bool NoEmoji                { get; set; } = !Configurables.Defaults.UseEmoji;
			public bool ForceRuntimesBuild     { get; set; } = false;
			public string HashAlgorithm        { get; set; } = null;
			public uint MakeConcurrency        { get; set; } = 0;
			public ExecutionMode ExecutionMode { get; set; } = Configurables.Defaults.ExecutionMode;
			public LoggingVerbosity Verbosity  { get; set; } = Configurables.Defaults.LoggingVerbosity;
			public string DebugFileExtension   { get; set; } = Configurables.Defaults.DebugFileExtension;
			public string ScenarioName         { get; set; } = null;
			public bool ListScenarios          { get; set; } = false;
			public string CompressionFormat    { get; set; } = Configurables.Defaults.DefaultCompressionFormat.Name;
			public string Configuration        { get; set; }
			public bool AutoProvision          { get; set; }
			public bool AutoProvisionUsesSudo  { get; set; }
			public bool IgnoreMaxMonoVersion   { get; set; }
			public bool IgnoreMinMonoVersion   { get; set; }
			public string MonoArchiveCustomUrl { get; set; }
			public bool EnableAll              { get; set; }
			public RefreshableComponent RefreshList { get; set; }
		}

		public static int Main (string[] args)
		{
			try {
				return Run (args).Result;
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

		static async Task<int> Run (string[] args)
		{
			Log.SetContext (Context.Instance);

			var optionErrors = new List <string> ();
			ParsedOptions parsedOptions = new ParsedOptions {
				AutoProvision         = ParseBoolean (Context.Instance.Properties.GetValue (KnownProperties.AutoProvision)),
				AutoProvisionUsesSudo = ParseBoolean (Context.Instance.Properties.GetValue (KnownProperties.AutoProvisionUsesSudo)),
				IgnoreMaxMonoVersion  = ParseBoolean (Context.Instance.Properties.GetValue (KnownProperties.IgnoreMaxMonoVersion)),
			};

			var opts = new OptionSet {
				"Usage: xaprepare [OPTIONS]",
				$"Xamarin.Android v{BuildInfo.XAVersion} preparation utility",
				"",
				{"p|property={=}", "Set a {PROPERTY} to a {VALUE}", (string p, string v) => Context.Instance.Properties.Set (p, v) },
				{"d|dump-properties", "Dump values of all the defined properties to the screen", v => parsedOptions.DumpProps = true },
				{"j|make-concurrency=", "Number of concurrent jobs for make to run. A positive integer or 0 for the default. Defaults to the number of CPUs/cores", v => parsedOptions.MakeConcurrency = EnsureUInt (v, "Invalid Make concurrency value") },
				{"no-emoji", "Do not use any emoji characters in the output", v => parsedOptions.NoEmoji = true },
				{"r|run-mode=", $"Specify the execution mode: {GetExecutionModes()}. See documentation for mode descriptions. Default: {Configurables.Defaults.ExecutionMode}", v => parsedOptions.ExecutionMode = ParseExecutionMode (v)},
				{"f|build-runtimes", $"Build runtimes even if the bundle/archives are available.", v => parsedOptions.ForceRuntimesBuild = true },
				{"H|hash-algorithm=", "Use the specified hash algorithm instead of the default {Configurables.Defaults.HashAlgorithm}", v => parsedOptions.HashAlgorithm = v?.Trim () },
				{"D|debug-ext=", $"Extension of files with debug information for managed DLLs and executables. Default: {parsedOptions.DebugFileExtension}", v => parsedOptions.DebugFileExtension = v?.Trim () },
				{"v|verbosity=", $"Set console log verbosity to {{LEVEL}}. Level name may be abbreviated to the smallest unique part (one of: {GetVerbosityLevels ()}). Default: {Context.Instance.LoggingVerbosity.ToString().ToLowerInvariant ()}", v => parsedOptions.Verbosity = ParseLogVerbosity (v) },
				{"s|scenario=", "Run the specified scenario (use --ls to list all known scenarios) instead of the default one", v => parsedOptions.ScenarioName = v },
				{"ls", "List names of all known scenarios", v => parsedOptions.ListScenarios = true },
				{"cf=", $"{{NAME}} of the compression format to use for some archives (e.g. the XA bundle). One of: {GetCompressionFormatNames ()}; Default: {parsedOptions.CompressionFormat}", v => parsedOptions.CompressionFormat = v?.Trim ()},
				{"c|configuration=", $"Build {{CONFIGURATION}}. Default: {Context.Instance.Configuration}", v => parsedOptions.Configuration = v?.Trim ()},
				{"a|enable-all", "Enable preparation of all the supported targets, ABIs etc", v => parsedOptions.EnableAll = true},
				{"refresh:", "[sdk,ndk] Comma separated list of components which should be reinstalled. Defaults to all supported components if no value is provided.", v => parsedOptions.RefreshList = ParseRefreshableComponents (v?.Trim ())},
				"",
				{"auto-provision=", $"Automatically install software required by Xamarin.Android", v => parsedOptions.AutoProvision = ParseBoolean (v)},
				{"auto-provision-uses-sudo=", $"Allow use of sudo(1) when provisioning", v => parsedOptions.AutoProvisionUsesSudo = ParseBoolean (v)},
				{"ignore-max-mono-version:", $"Ignore the maximum supported Mono version restriction", v => parsedOptions.IgnoreMaxMonoVersion = ParseBoolean (v)},
				{"ignore-min-mono-version:", $"Ignore the minimum supported Mono version restriction", v => parsedOptions.IgnoreMinMonoVersion = ParseBoolean (v)},
				{"ignore-mono-version:", $"Ignore the both the minimum and the maximum supported Mono version restrictions", v => {
						bool yesno = ParseBoolean (v);
						parsedOptions.IgnoreMinMonoVersion = yesno;
						parsedOptions.IgnoreMaxMonoVersion = yesno;
					}
				},
				{"mono-archive-url=", "Use a specific URL for the mono archive.", v => parsedOptions.MonoArchiveCustomUrl = v?.Trim () },
				"",
				{"h|help", "Show this help message", v => parsedOptions.ShowHelp = true },
			};

			opts.Parse (args);
			if (parsedOptions.ShowHelp) {
				opts.WriteOptionDescriptions (Console.Out);
				return 0;
			}

			if (optionErrors.Count > 0) {
				Log.Instance.ErrorLine ("Invalid arguments passed, please run with --help to see option documentation:");
				Log.Instance.ErrorLine ();
				foreach (string errorLine in optionErrors) {
					Log.Instance.ErrorLine ($"  {errorLine}");
				}
			}

			// If we're running without a terminal or the output is redirected we must enforce the dull mode.
			if (!Context.Instance.InteractiveSession)
				parsedOptions.ExecutionMode = ExecutionMode.CI;

			Context.Instance.MakeConcurrency       = parsedOptions.MakeConcurrency;
			Context.Instance.NoEmoji               = parsedOptions.NoEmoji;
			Context.Instance.ExecutionMode         = parsedOptions.ExecutionMode;
			Context.Instance.ForceRuntimesBuild    = parsedOptions.ForceRuntimesBuild;
			Context.Instance.LoggingVerbosity      = parsedOptions.Verbosity;
			Context.Instance.DebugFileExtension    = parsedOptions.DebugFileExtension;
			Context.Instance.AutoProvision         = parsedOptions.AutoProvision;
			Context.Instance.AutoProvisionUsesSudo = parsedOptions.AutoProvisionUsesSudo;
			Context.Instance.IgnoreMaxMonoVersion  = parsedOptions.IgnoreMaxMonoVersion;
			Context.Instance.IgnoreMinMonoVersion  = parsedOptions.IgnoreMinMonoVersion;
			Context.Instance.MonoArchiveCustomUrl  = parsedOptions.MonoArchiveCustomUrl;
			Context.Instance.EnableAllTargets      = parsedOptions.EnableAll;
			Context.Instance.ComponentsToRefresh   = parsedOptions.RefreshList;

			if (!String.IsNullOrEmpty (parsedOptions.Configuration))
				Context.Instance.Configuration = parsedOptions.Configuration;

			if (!String.IsNullOrEmpty (parsedOptions.HashAlgorithm))
				Context.Instance.HashAlgorithm = parsedOptions.HashAlgorithm;

			SetCompressionFormat (parsedOptions.CompressionFormat);

			if (!await Context.Instance.Init (parsedOptions.ScenarioName)) {
				return 1;
			}

			if (parsedOptions.DumpProps)
				DumpProperties (Context.Instance);
			if (parsedOptions.ListScenarios) {
				ListScenarios (Context.Instance);
				return 0;
			}

			return await Context.Instance.Execute () ? 0 : 1;

			uint EnsureUInt (string v, string errorText)
			{
				if (UInt32.TryParse (v, out uint ret))
					return ret;

				optionErrors.Add (errorText);
				return 0;
			}
		}

		static bool SetCompressionFormat (string cfName)
		{
			if (String.IsNullOrEmpty (cfName)) {
				Log.Instance.ErrorLine ("Compression format name must be specified");
				return false;
			}

			if (String.Compare (cfName, Configurables.Defaults.DefaultCompressionFormat.Name, StringComparison.OrdinalIgnoreCase) == 0)
				return true;

			if (!Configurables.Defaults.CompressionFormats.TryGetValue (cfName, out CompressionFormat cf)) {
				Log.Instance.ErrorLine ($"Unknown compression format name: {cfName}");
				return false;
			}

			if (cf == null)
				throw new InvalidOperationException ($"Valid compression format name ({cfName}) but compression format is null!");

			Log.Instance.DebugLine ($"Setting compression format to: {cf.Description}; File extension: {cf.Extension}");
			Context.Instance.CompressionFormat = cf;

			return true;
		}

		static string GetCompressionFormatNames ()
		{
			return String.Join (", ", Configurables.Defaults.CompressionFormats.Keys);
		}

		static void ListScenarios (Context context)
		{
			Log.Instance.StatusLine ("Known scenarios:");
			foreach (var kvp in context.Scenarios) {
				Scenario scenario = kvp.Value;
				if (scenario == null)
					continue;

				Log.Instance.Status ($"  {context.Characters.Bullet} {scenario.Name}");
				Log.Instance.StatusLine (scenario == context.DefaultScenario ? " (default)" : String.Empty, ConsoleColor.Green);
			}
		}

		static void DumpProperties (Context context)
		{
			if (context.Properties.Count == 0) {
				Log.Instance.InfoLine ("No properties defined");
				return;
			}

			context.Banner ("Defined properties");
			foreach (var kvp in context.Properties) {
				Log.Instance.InfoLine ($"{kvp.Key} = ", kvp.Value ?? "<null>", ConsoleColor.White, ConsoleColor.White);
			}
		}

		static string GetVerbosityLevels ()
		{
			return EnumNamesToCommaSeparatedList<LoggingVerbosity> ();
		}

		static string GetExecutionModes ()
		{
			return EnumNamesToCommaSeparatedList<ExecutionMode> ();
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

		static ExecutionMode ParseExecutionMode (string name)
		{
			switch (Char.ToLowerInvariant (name [0])) {
				case 'c':
					return ExecutionMode.CI;

				case 's':
					return ExecutionMode.Standard;

				case 'i':
					return ExecutionMode.Interactive;

				default:
					throw new InvalidOperationException ($"Unknown execution mode '{name}'");
			}
		}

		static bool ParseBoolean (string value, bool defaultValue = true)
		{
			string v = value?.Trim ();
			if (String.IsNullOrEmpty (v))
				return defaultValue;

			if (String.Compare ("yes", v, StringComparison.OrdinalIgnoreCase) == 0 || String.Compare ("true", v, StringComparison.OrdinalIgnoreCase) == 0)
				return true;

			if (String.Compare ("no", v, StringComparison.OrdinalIgnoreCase) == 0 || String.Compare ("false", v, StringComparison.OrdinalIgnoreCase) == 0)
				return false;

			throw new InvalidOperationException ($"Unknown boolean value: {value}");
		}

		static RefreshableComponent ParseRefreshableComponents (string refreshList)
		{
			if (String.IsNullOrEmpty (refreshList))
				return RefreshableComponent.All;

			if (refreshList.IndexOf (',') == -1)
				return ParseSingleComponent (refreshList);

			var allParsedComponents = RefreshableComponent.None;
			var refreshListArray = refreshList.Split (',');
			foreach (var c in refreshListArray) {
				RefreshableComponent parsed = ParseSingleComponent (c);
				if (parsed != RefreshableComponent.None)
					allParsedComponents |= parsed;
			}

			return allParsedComponents;


			RefreshableComponent ParseSingleComponent (string component) {
				if (String.Compare ("sdk", component, StringComparison.OrdinalIgnoreCase) == 0)
					return RefreshableComponent.AndroidSDK;

				if (String.Compare ("ndk", component, StringComparison.OrdinalIgnoreCase) == 0)
					return RefreshableComponent.AndroidNDK;

				return RefreshableComponent.None;
			}
		}

	}
}
