// Code ported from: build-tools/Xamarin.Android.Tools.BootstrapTasks/Xamarin.Android.Tools.BootstrapTasks/MSBuildTimingProcessor.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	/// <summary>
	///   This class takes in XML file output from TimingLogger and aggregates the total build time for each build into
	///   a CSV file to be used on Jenkins
	/// </summary>
	class MSBuildTimingProcessor : AppObject
	{
		Dictionary<string, string> results = new Dictionary<string, string> (StringComparer.Ordinal);
		List<MSBuildTimingResult> inputResults;
		string resultsFilePath;
		string labelSuffix;

		public bool AddResults { get; set; }

		public MSBuildTimingProcessor (List<MSBuildTimingResult> inputResults, string resultsFilePath, string labelSuffix)
		{
			this.inputResults = inputResults;
			this.resultsFilePath = EnsureParameterValue (nameof (resultsFilePath), resultsFilePath);
			this.labelSuffix = EnsureParameterValue (nameof (labelSuffix), labelSuffix);
		}

		public bool Run ()
		{
			foreach (MSBuildTimingResult result in inputResults) {
				Log.DebugLine ($"Processing MSBuild timing results file: {result.OutputFilePath}");

				XElement element = XElement.Load (result.OutputFilePath);
				XElement build = element.Element ("build");
				string id = build.Attribute ("id")?.Value ?? String.Empty;
				string elapsed = build.Attribute ("elapsed")?.Value ?? String.Empty;
				if (TimeSpan.TryParse (elapsed, out TimeSpan t)) {
					results [id] = t.TotalMilliseconds.ToString ();
				}
			}
			WriteResults ();

			return true;
		}

		void WriteResults ()
		{
			string line1 = String.Empty, line2 = String.Empty;
			if (AddResults && Utilities.FileExists (resultsFilePath)) {
				Log.DebugLine ($"Appending MSBuild results to file: {resultsFilePath}");
				using (var reader = new StreamReader (resultsFilePath)) {
					try {
						line1 = reader.ReadLine ();
						line2 = reader.ReadLine ();
					} catch (Exception e) {
						Log.WarningLine ($"unable to read previous results from {resultsFilePath}");
						Log.DebugLine (e.ToString ());
						line1 = line2 = String.Empty;
					}
				}
			} else {
				Log.DebugLine ($"Creating MSBuild results file: {resultsFilePath}");
			}

			using (var resultsFile = new StreamWriter (resultsFilePath)) {
				WriteValues (resultsFile, results.Keys, line1, labelSuffix);
				WriteValues (resultsFile, results.Values, line2);
				resultsFile.Flush ();
				resultsFile.Close ();
			}
		}

		void WriteValues (StreamWriter writer, ICollection<string> values, string line, string? suffix = null)
		{
			bool first;
			if (String.IsNullOrEmpty (line)) {
				first = true;
			} else {
				writer.Write (line);
				first = false;
			}
			foreach (var key in values) {
				if (!first) {
					writer.Write (',');
				}
				writer.Write (key);
				if (!String.IsNullOrEmpty (suffix)) {
					writer.Write (suffix);
				}
				first = false;
			}
			writer.WriteLine ();
		}
	}
}
