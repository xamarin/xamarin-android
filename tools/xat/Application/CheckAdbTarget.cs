//
// Code ported from build-tools/Xamarin.Android.Tools.BootstrapTasks/Xamarin.Android.Tools.BootstrapTasks/CheckAdbTarget.cs
//
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class CheckAdbTarget : Adb
	{
		static readonly char[] SpaceSplit = new [] { ' ', '\t' };

		public string SdkVersion  { get; set; } = String.Empty;
		public bool IsValidTarget { get; private set; }

		public override async Task<bool> Run ()
		{
			IsValidTarget = false;

			AdbRunner adb = CreateAdbRunner ();
			await CheckIfTargetIsValid (adb);

			(bool success, string output) = await adb.Shell ("pm path com.android.shell");
			if (!success) {
				// ignore
				Log.WarningLine ("Attempt to access Package Manager failed");
				return true;
			}

			if (output.IndexOf ("Error: Could not access the Package Manager", StringComparison.Ordinal) >= 0 ||
			    output.IndexOf ("Failure ", StringComparison.Ordinal) >= 0) {
				// ignore
				IsValidTarget   = false;
				return true;
			}

			if (AdbTarget.Length > 0) {
				return true;
			}

			(success, output) = await adb.Devices ();
			if (!success) {
				// ignore
				return true;
			}

			string[] lines = output.Split (Context.NewlineSplit, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length == 0) {
				// ignore
				return true;
			}

			string adbTarget = String.Empty;
			foreach (string line in lines) {
				if (line.IndexOf ("List of devices attached", StringComparison.OrdinalIgnoreCase) >= 0) {
					continue;
				}

				if (!line.EndsWith ("device", StringComparison.OrdinalIgnoreCase)) {
					continue;
				}

				if (adbTarget.Length > 0) {
					Log.WarningLine ("More than one device attached, unable to determine the default ADB target");
					return true;
				}

				string[] device = line.Split (SpaceSplit, StringSplitOptions.RemoveEmptyEntries);
				adbTarget = device[0];
			}

			AdbTarget = adbTarget;
			return true;
		}

		async Task CheckIfTargetIsValid (AdbRunner adb)
		{
			if (SdkVersion.Length == 0) {
				IsValidTarget = true;
				return;
			}

			const string buildVersionProperty = "ro.build.version.sdk";
			(bool success, string output) = await adb.GetProperty (buildVersionProperty);
			if (!success) {
				Log.WarningLine ($"Failed to retrieve value of the '{buildVersionProperty}' property");
				return;
			}

			string[] lines = output.Split (Context.NewlineSplit, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length == 0) {
				Log.WarningLine ($"Attempt to retrieve value o the '{buildVersionProperty}' property returned an empty value");
				return;
			}

			string reportedVersion = lines[lines.Length - 1].Trim ();
			if (String.Compare (SdkVersion, reportedVersion, StringComparison.OrdinalIgnoreCase) == 0) {
				IsValidTarget = true;
				return;
			}

			if (int.TryParse (SdkVersion, out int required) && int.TryParse (reportedVersion, out int target) && target >= required) {
				IsValidTarget   = true;
			}
		}
	}
}
