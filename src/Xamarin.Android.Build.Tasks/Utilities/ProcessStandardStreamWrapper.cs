using System;
using System.IO;
using System.Text;

#if NO_MSBUILD
using LoggerType = Xamarin.Android.Utilities.XamarinLoggingHelper;
#else
using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Utilities;

using LoggerType = Microsoft.Build.Utilities.TaskLoggingHelper;
#endif

namespace Xamarin.Android.Tasks
{
	class ProcessStandardStreamWrapper : TextWriter
	{
		public enum LogLevel
		{
			Error,
			Warning,
			Info,
			Message,
			Debug,
		}

		LoggerType log;

		public LogLevel LoggingLevel { get; set; } = LogLevel.Debug;
		public string?  LogPrefix    { get; set; }

		public override Encoding Encoding => Encoding.Default;

		public ProcessStandardStreamWrapper (LoggerType logger)
		{
			log = logger;
		}

		public override void WriteLine (string? value)
		{
			DoWrite (value);
		}

		protected virtual string? PreprocessMessage (string? message, out bool ignoreLine)
		{
			ignoreLine = false;
			return message;
		}

		void DoWrite (string? message)
		{
			bool ignoreLine;

			message = PreprocessMessage (message, out ignoreLine) ?? String.Empty;
			if (ignoreLine) {
				return;
			}

			if (!String.IsNullOrEmpty (LogPrefix)) {
				message = $"{LogPrefix}{message}";
			}

			switch (LoggingLevel) {
				case LogLevel.Error:
					log.LogError (message);
					break;

				case LogLevel.Warning:
					log.LogWarning (message);
					break;

				case LogLevel.Info:
				case LogLevel.Message:
					log.LogMessage (message);
					break;

				case LogLevel.Debug:
					log.LogDebugMessage (message);
					break;

				default:
					log.LogWarning ($"Unsupported log level {LoggingLevel}");
					log.LogMessage (message);
					break;
			}
		}
	}
}
