using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Mono.Options;

namespace Xamarin.Android.ApiMerge {

	class Merge {
		public static int Main (string[] args)
		{
			bool show_help = false, show_version = false;
			string dest = null;
			string glob = null;
			string lastDescription = null;
			string configFile = null;
			string configBaseInput = string.Empty;
			string configBaseOutput = string.Empty;

			var options = new OptionSet () {
				"Usage: api-merge -o=FILE DESCRIPTIONS+",
				"",
				"Merge API descriptions into a single API description.",
				"",
				"Options:",
				{ "o=",
				  "Output {FILE} to create.",
				  v => dest = v },
				{ "s|sort-glob=",
				  "{GLOB} pattern for sorting DESCRIPTIONS.",
				  v => glob = v },
				{ "last-description=",
				  "Last {DESCRIPTION} to process. Any later descriptions are ignored.",
				  v => lastDescription = NormalizePath (v) },
				{ "config=",
				  "XML configuration file (used instead of other options).",
				  v => configFile = v },
				{ "config-input-dir=",
				  "Base input directory for XML configuration file.",
				  v => configBaseInput = v },
				{ "config-output-dir=",
				  "Base output directory for XML configuration file.",
				  v => configBaseOutput = v },
				{ "version",
				  "Output version information and exit.",
				  v => show_version = v != null },
				{ "h|?|help",
				  "Show this help message and exit.",
				  v => show_help = v != null },
			};
			List<string> sources;
			try {
				sources = options.Parse (args);
			}
			catch (Exception e) {
				Console.Error.WriteLine ("api-merge: {0}", e);
				return 1;
			}
			if (show_version) {
				Console.WriteLine ("api-merge {0}", "<<Not generated at this time>>");
				return 0;
			}
			if (show_help) {
				options.WriteOptionDescriptions (Console.Out);
				return 0;
			}
			if (!string.IsNullOrEmpty (configFile))
				return RunConfigurationFile (configFile, configBaseInput, configBaseOutput);
			for (int i = sources.Count - 1; i >= 0; i--) {
				if (!File.Exists (sources [i])) {
					Console.WriteLine ("warning: skipping file {0}...", sources [i]);
					sources.RemoveAt (i);
				}
			}
			if (sources.Count == 0) {
				Console.Error.WriteLine ("api-merge: Missing DESCRIPTIONS+.");
				return 2;
			}
			SortSources (sources, glob);
			ApiDescription context = new ApiDescription (sources [0]);
			for (int i = 1; i < sources.Count; i++) {
				if (NormalizePath (sources [i-1]) == lastDescription)
					break;
				context.Merge (sources [i]);
			}
			context.Save (dest);
			return 0;
		}

		static int RunConfigurationFile (string config, string inputDir, string outputDir)
		{
			if (!File.Exists (config)) {
				Console.WriteLine ($"error: config file {config} not found");
				return 3;
			}

			var doc = XDocument.Load (config);
			var inputs = doc.Root.Element ("Inputs").Elements ("File").Select (elem => Tuple.Create (Path.Combine (inputDir, elem.Attribute ("Path").Value), elem.Attribute ("Level").Value)).ToList ();

			// Remove any missing inputs
			foreach (var missing in inputs.Where (i => !File.Exists (i.Item1)).ToList ()) {
				Console.WriteLine ($"warning: skipping missing file {missing.Item1}...");
				inputs.Remove (missing);
			}

			if (inputs.Count == 0) {
				Console.WriteLine ($"error: no input files found...");
				return 4;
			}

			// Create the initial context
			var context = new ApiDescription (inputs [0].Item1);

			var outputs = doc.Root.Element ("Outputs").Elements ("File").Select (elem => Tuple.Create (Path.Combine (outputDir, elem.Attribute ("Path").Value), elem.Attribute ("LastLevel").Value)).ToList ();
			var current_input = 0;
			var current_output = 0;

			// Handle the initial case if needed
			if (inputs[current_input].Item2 == outputs[current_output].Item2) {
				Console.WriteLine ($"api-merge: writing output {outputs [current_output].Item1}...");
				context.Save (outputs [current_output].Item1);
				current_output++;
			}

			// Write each output
			while (current_output < outputs.Count) {
				context.Merge (inputs [++current_input].Item1);

				if (inputs [current_input].Item2 == outputs [current_output].Item2) {
					Console.WriteLine ($"api-merge: writing output {outputs [current_output].Item1}...");
					context.Save (outputs [current_output].Item1);
					current_output++;
				}
			}

			return 0;
		}

		static string NormalizePath (string path)
		{
			if (String.IsNullOrEmpty (path))
				return path;

			return Path.GetFullPath (path);
		}

		static void SortSources (List<string> sources, string globPattern)
		{
			if (globPattern == null)
				return;

			var regex = GlobToRegex (globPattern);
			Comparison<string> c = (x, y) => {
				var mx = regex.Match (x);
				var my = regex.Match (y);
				if (!(mx.Success && my.Success))
					return string.Compare (x, y, StringComparison.OrdinalIgnoreCase);
				for (int i = 1; i < mx.Groups.Count; i++) {
					var vx = mx.Groups [i].Value;
					var vy = my.Groups [i].Value;
					int nx, ny;
					if (int.TryParse (vx, out nx) && int.TryParse (vy, out ny)) {
						if (nx == ny)
							continue;
						return nx.CompareTo (ny);
					}
					var r = string.Compare (vx, vy, StringComparison.OrdinalIgnoreCase);
					if (r != 0) {
						return r;
					}
				}
				return 0;
			};
			sources.Sort (c);
		}

		static Regex GlobToRegex (string globPattern)
		{
			var regexPattern = new StringBuilder ("^")
				.Append (globPattern)
				.Append ("$");
			for (int i = regexPattern.Length - 1; i >= 0; --i) {
				switch (regexPattern [i]) {
				case '.':
				case '(':
				case ')':
					regexPattern.Insert (i, "\\");
					break;
				case '*':
					regexPattern.Insert (i + 1, ')');
					regexPattern.Insert (i,     "(.");
					break;
				case '?':
					regexPattern.Insert (i + 1, ')');
					regexPattern.Insert (i,     '(');
					break;
				}
			}
			return new Regex (regexPattern.ToString ());
		}
	}
}
