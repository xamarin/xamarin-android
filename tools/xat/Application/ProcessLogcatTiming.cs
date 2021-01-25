using System;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.BuildTools.PrepTasks
{
	partial class ProcessLogcatTiming : ProcessPlotInput
	{
		public string Activity { get; set; } = String.Empty;
		public int PID         { get; set; } = -1;

		public override bool Run ()
		{
			return DoExecute ();
		}
	}
}
