﻿using System;

using Android.Util;

namespace Xamarin.Android.UnitTests
{
	public class LogWriter
	{
		public MinimumLogLevel MinimumLogLevel { get; set; } = MinimumLogLevel.Info;

		public void OnError (string tag, string message)
		{
			if (MinimumLogLevel < MinimumLogLevel.Error)
				return;
			Log.Error (tag, message);
		}

		public void OnWarning (string tag, string message)
		{
			if (MinimumLogLevel < MinimumLogLevel.Warning)
				return;
			Log.Warn (tag, message);
		}

		public void OnDebug (string tag, string message)
		{
			if (MinimumLogLevel < MinimumLogLevel.Debug)
				return;
			Log.Debug (tag, message);
		}

		public void OnDiagnostic (string tag, string message)
		{
			if (MinimumLogLevel < MinimumLogLevel.Verbose)
				return;
			Log.Verbose (tag, message);
		}

		public void OnInfo (string tag, string message)
		{
			if (MinimumLogLevel < MinimumLogLevel.Info)
				return;
			Log.Info (tag, message);
		}
	}
}
