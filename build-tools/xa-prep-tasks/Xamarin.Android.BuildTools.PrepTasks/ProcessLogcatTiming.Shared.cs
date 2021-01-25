//
// Code shared with ../../../tools/xat
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Xamarin.Android.BuildTools.PrepTasks
{
	partial class ProcessLogcatTiming
	{
		static readonly string activityManagerPrefix = @"^(?<timestamp>\d+-\d+\s+[\d:\.]+)\s+.*Activity(Task)??Manager: ";

		bool DoExecute ()
		{
			LoadDefinitions ();

			if (!CheckInputFile ())
				return false;

			using (var reader = new StreamReader (InputFilename)) {
				string line;
				var procIdentification = string.IsNullOrEmpty (Activity) ? $"added application {ApplicationPackageName}" : $"activity {{?{Activity}}}?";
				var startIdentification = PID > 0 ? $".*: pid={PID}" : $@"{procIdentification}: pid=(?<pid>\d+)";
				var procStartRegex = new Regex ($"{activityManagerPrefix}Start proc.*for {startIdentification}");
				var startIdentification2 = PID > 0 ? $"{PID}:" : $@"(?<pid>\d+):.*{procIdentification}";
				var procStartRegex2 = new Regex ($"{activityManagerPrefix}Start proc {startIdentification2}");
				var activityDisplayed = new Regex ($"{activityManagerPrefix}Displayed {Activity}:");
				Regex timingRegex = null;
				DateTime start = DateTime.Now;
				DateTime last = start;
				bool started = false;

				while ((line = reader.ReadLine ()) != null) {
					if (!started) {
						var match = procStartRegex.Match (line);
						if (!match.Success)
							match = procStartRegex2.Match (line);
						if (!match.Success)
							continue;

						last = start = ParseTime (match.Groups ["timestamp"].Value);
						if (PID < 1)
							PID = Int32.Parse (match.Groups ["pid"].Value);
						LogDebug ($"Time:      0ms process start, application: '{ApplicationPackageName}' PID: {PID}");
						timingRegex = new Regex ($@"^(?<timestamp>\d+-\d+\s+[\d:\.]+)\s+{PID}\s+(?<message>.*)$");
						started = true;
					} else {
						if (line.Contains ($"Process {ApplicationPackageName} (pid {PID}) has died")) {
							LogError ("Application crash detected. Could not collect performance data.");
							return false;
						}

						var match = timingRegex.Match (line);
						if (!match.Success) {
							if (!string.IsNullOrEmpty (Activity)) {
								var matchActivity = activityDisplayed.Match (line);
								if (matchActivity.Success) {
									var timeString = (ParseTime (matchActivity.Groups ["timestamp"].Value) - start).TotalMilliseconds.ToString ();
									results ["ActivityDisplayed"] = timeString;
									LogDebug ($"Time: {timeString.PadLeft (6)}ms Activity displayed");
								}
							}

							continue;
						}

						var time = ParseTime (match.Groups ["timestamp"].Value);
						var span = time - start;

						string message = match.Groups ["message"].Value;
						string logMessage = message;

						foreach (var regex in definedRegexs) {
							var definedMatch = regex.Value.Match (message);
							if (!definedMatch.Success)
								continue;
							results [regex.Key] = span.TotalMilliseconds.ToString ();
							var m = definedMatch.Groups ["message"];
							if (m.Success)
								logMessage = m.Value;

							LogDebug ($"Time: {span.TotalMilliseconds.ToString ().PadLeft (6)}ms Message: {logMessage}");
							last = time;
						}
					}
				}

				if (PID > 0) {
					LogDebug (" -- Performance summary --");
					LogDebug ($"Last timing message: {(last - start).TotalMilliseconds}ms");

					WriteResults ();
				} else {
					LogError ("Application start wasn't detected. Could not collect performance data.");
					return false;
				}

				return true;
			}
		}

		static Regex timeRegex = new Regex (@"(?<month>\d+)-(?<day>\d+)\s+(?<hour>\d+):(?<minute>\d+):(?<second>\d+)\.(?<millisecond>\d+)");
		DateTime ParseTime (string s)
		{
			var match = timeRegex.Match (s);
			if (!match.Success)
				throw new InvalidOperationException ($"Unable to parse time: '{s}'");

			// we don't handle year boundary here as the logcat timestamp doesn't include year information
			return new DateTime (DateTime.Now.Year,
					     int.Parse (match.Groups ["month"].Value),
					     int.Parse (match.Groups ["day"].Value),
					     int.Parse (match.Groups ["hour"].Value),
					     int.Parse (match.Groups ["minute"].Value),
					     int.Parse (match.Groups ["second"].Value),
					     int.Parse (match.Groups ["millisecond"].Value));
		}
	}
}
