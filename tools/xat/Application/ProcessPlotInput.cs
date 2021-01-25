using System;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.BuildTools.PrepTasks
{
	partial class ProcessPlotInput : AppObject
	{
		public string InputFilename          { get; set; } = String.Empty;
		public string ApplicationPackageName { get; set; } = String.Empty;
		public string DefinitionsFilename    { get; set; } = String.Empty;
		public string ResultsFilename        { get; set; } = String.Empty;
		public bool AddResults               { get; set; }
		public string LabelSuffix            { get; set; } = String.Empty;

		public virtual bool Run ()
		{
			EnsurePropertyValue (nameof (InputFilename), InputFilename);
			EnsurePropertyValue (nameof (DefinitionsFilename), DefinitionsFilename);

			return DoExecute ();;
		}

		protected void LogDebug (string message)
		{
			Log.DebugLine (message);
		}

		protected void LogWarning (string message)
		{
			Log.WarningLine (message);
		}

		protected void LogError (string message)
		{
			Log.ErrorLine (message);
		}
	}
}
