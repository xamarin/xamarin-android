using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Xamarin.Android.BuildTools.PrepTasks
{
	public partial class ProcessLogcatTiming : ProcessPlotInput
	{
		public string Activity { get; set; }

		public int PID { get; set; } = -1;

		public override bool Execute ()
		{
			return DoExecute ();
		}
	}
}
