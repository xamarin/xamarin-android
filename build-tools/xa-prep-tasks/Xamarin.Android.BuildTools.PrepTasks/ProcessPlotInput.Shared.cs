//
// Code shared with ../../../tools/xat
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Xamarin.Android.BuildTools.PrepTasks
{
	partial class ProcessPlotInput
	{
		protected Dictionary<string, Regex> definedRegexs = new Dictionary<string, Regex> ();
		protected Dictionary<string, string> results = new Dictionary<string, string> ();

		bool DoExecute ()
		{
			LoadDefinitions ();

			if (!CheckInputFile ())
				return false;

			using (var reader = new StreamReader (InputFilename)) {
				string line;

				while ((line = reader.ReadLine ()) != null) {
					foreach (var regex in definedRegexs) {
						var definedMatch = regex.Value.Match (line);
						if (!definedMatch.Success)
							continue;
						string logMessage = definedMatch.Value;
						var v = definedMatch.Groups ["value"];
						if (!v.Success)
							continue;
						results [regex.Key] = v.Value;
						var m = definedMatch.Groups ["message"];
						if (m.Success)
							logMessage = m.Value;

						LogDebug ($"Message: {logMessage} Value: {v.Value}");
					}
				}

				WriteResults ();

				reader.Close ();
			}

			return true;
		}

		protected void LoadDefinitions ()
		{
			using (var reader = new StreamReader (DefinitionsFilename)) {
				string line;

				while ((line = reader.ReadLine ()) != null) {
					if (line.StartsWith ("#", StringComparison.Ordinal))
						continue;
					int index = line.IndexOf ('=');
					if (index < 1 || index == line.Length)
						continue;
					var label = line.Substring (0, index);
					var pattern = line.Substring (index + 1);
					Regex regex;
					try {
						regex = new Regex (pattern);
					} catch (Exception e) {
						LogWarning ($"unable to create regex for label: {label} from pattern: {pattern}\n{e}");
						continue;
					}
					if (definedRegexs.ContainsKey (label))
						LogWarning ($"label '{label}' is defined multiple times. the last definition will be used");
					definedRegexs [label] = regex;
				}
			}
		}

		protected bool CheckInputFile ()
		{
			if (File.Exists (InputFilename))
				return true;

			var errorMessage = $"Input file '{InputFilename}' doesn't exist.";
			LogError (errorMessage);

			ErrorResultsHelper.CreateErrorResultsFile (
				Path.ChangeExtension (ResultsFilename, null) + LabelSuffix + ".xml",
				ApplicationPackageName,
				"logcat output",
				new Exception (errorMessage),
				$"Input file '{InputFilename}' doesn't exist. It might be caused by various reasons. Among them: the test crashed or test did not run.");

			return false;
		}

		protected void WriteResults ()
		{
			if (ResultsFilename != null) {
				string line1 = null, line2 = null;
				if (AddResults && File.Exists (ResultsFilename))
					using (var reader = new StreamReader (ResultsFilename)) {
						try {
							line1 = reader.ReadLine ();
							line2 = reader.ReadLine ();
						} catch (Exception e) {
							LogWarning ($"unable to read previous results from {ResultsFilename}\n{e}");
							line1 = line2 = null;
						}
					}
				using (var resultsFile = new StreamWriter (ResultsFilename)) {
					WriteValues (resultsFile, results.Keys, line1, LabelSuffix);
					WriteValues (resultsFile, results.Values, line2);
					resultsFile.Close ();
				}
			}
		}

		void WriteValues (StreamWriter writer, ICollection<string> values, string line, string suffix = null)
		{
			bool first;
			if (string.IsNullOrEmpty (line))
				first = true;
			else {
				writer.Write (line);
				first = false;
			}
			foreach (var key in values) {
				if (!first)
					writer.Write (',');
				writer.Write (key);
				if (!string.IsNullOrEmpty (suffix))
					writer.Write (suffix);
				first = false;
			}
			writer.WriteLine ();
		}
	}
}
