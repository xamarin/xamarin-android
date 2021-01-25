using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Xamarin.Android.BuildTools.PrepTasks
{
	public partial class ProcessPlotInput : Task
	{
		[Required]
		public string InputFilename { get; set; }

		public string ApplicationPackageName { get; set; }

		[Required]
		public string DefinitionsFilename { get; set; }

		public string ResultsFilename { get; set; }

		public bool AddResults { get; set; }

		public string LabelSuffix { get; set; }

		public override bool Execute ()
		{
			return DoExecute ();;
		}

		protected void LogDebug (string message)
		{
			Log.LogMessage (MessageImportance.Low, message);
		}

		protected void LogWarning (string message)
		{
			Log.LogWarning (message);
		}

		protected void LogError (string message)
		{
			Log.LogError (message);
		}
	}
}
