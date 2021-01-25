//
// Code ported from build-tools/Xamarin.Android.Tools.BootstrapTasks/Xamarin.Android.Tools.BootstrapTasks/RunUITests.cs
//
using System;
using System.Threading;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class RunUITests : Adb
	{
		static readonly TimeSpan DefaultActivityWaitTime = TimeSpan.FromSeconds (30);

		public string Activity           { get; set; } = String.Empty;
		public string LogcatFilename     { get; set; } = String.Empty;
		public TimeSpan ActivityWaitTime { get; set; } = DefaultActivityWaitTime;

		public override async Task<bool> Run ()
		{
			EnsurePropertyValue (nameof (Activity), Activity);
			EnsurePropertyValue (nameof (LogcatFilename), LogcatFilename);

			if (ActivityWaitTime == default || ActivityWaitTime == TimeSpan.Zero) {
				ActivityWaitTime = DefaultActivityWaitTime;
			}

			Log.StatusLine ("Starting test run for:");
			Log.InfoLine ($"  Activity: ", Activity);

			AdbRunner adb = CreateAdbRunner ();
			bool startSuccess = await adb.AmStart (Activity);

			Log.MessageLine ($"Going to wait for {ActivityWaitTime}");
			Thread.Sleep ((int)ActivityWaitTime.TotalMilliseconds);
			
			Log.StatusLine ("Logcat output path: ", LogcatFilename);

			if (!await adb.LogcatDump (LogcatFilename, format: "threadtime")) {
				Log.WarningLine ("Failed to dump logcat buffer");
			}

			return startSuccess;
		}
	}
}
