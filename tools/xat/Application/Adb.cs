//
// Code ported from build-tools/Xamarin.Android.Tools.BootstrapTasks/Xamarin.Android.Tools.BootstrapTasks/Adb.cs
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	abstract class Adb : AppObject
	{
		public string AdbTarget                                 { get; set; } = String.Empty;
		public TimeSpan Timeout                                 { get; set; } = default;

		public abstract Task<bool> Run ();

		protected AdbRunner CreateAdbRunner ()
		{
			var ret = new AdbRunner (Context, toolPath: Context.AdbPath) {
				AdbTarget = AdbTarget,
			};

			if (Timeout != default) {
				ret.ProcessTimeout = Timeout;
			}

			return ret;
		}
	}
}
