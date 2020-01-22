using System;
using System.Linq;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Text;
using System.Collections.Generic;
using Xamarin.Android.Tools;
using Xamarin.Android.Tools.Aidl;

namespace Xamarin.Android.Tasks
{

	public class JavaDoc : JavaToolTask
	{
		public override string TaskPrefix => "JDC";

		protected override string MainClass => throw new NotSupportedException ("javadoc does not support daemon mode!");

		public string [] SourceDirectories { get; set; }

		public string [] DestinationDirectories { get; set; }

		public string [] ReferenceJars { get; set; }

		public string JavaPlatformJar { get; set; }

		public string [] ExtraArgs { get; set; }

		protected override string ToolName {
			get { return OS.IsWindows ? "javadoc.exe" : "javadoc"; }
		}

		public override bool RunTask ()
		{
			foreach (var dir in DestinationDirectories)
				if (!Directory.Exists (dir))
					Directory.CreateDirectory (dir);

			// Basically, javadoc will return non-zero return code with those expected errors. We have to ignore them.
			foreach (var pair in SourceDirectories.Zip (DestinationDirectories, (src, dst) => new { Source = src, Destination = dst })) {
				context_src = pair.Source;
				context_dst = pair.Destination;
				base.RunTask ();
			}
			return true;
		}

		string context_src;
		string context_dst;

		protected override string GenerateCommandLineCommands ()
		{
			var cmd = new CommandLineBuilder ();

			cmd.AppendSwitch ("-d");
			cmd.AppendFileNameIfNotNull (context_dst);
			cmd.AppendSwitch ("-sourcepath");
			cmd.AppendFileNameIfNotNull (context_src);
			cmd.AppendSwitch ("-subpackages");
			cmd.AppendSwitch (".");
			var cps = ReferenceJars?.ToList () ?? new List<string> ();
			if (JavaPlatformJar != null)
				cps.Add (JavaPlatformJar);
			if (cps.Any ()) {
				if (OS.IsWindows)
					cmd.AppendSwitch ("-cp " + string.Join (";", cps.Select (cp => '"' + cp + '"')));
				else 
					cmd.AppendSwitch ("-cp " + '"' + string.Join (":", cps) + '"');
			}
			if (ExtraArgs != null)
				foreach (var extraArg in ExtraArgs)
					cmd.AppendSwitch (extraArg);

			return cmd.ToString ();
		}

		// log them as is, regardless of message importance. Javadoc compilation errors should never be reported as errors.
		protected override void LogEventsFromTextOutput (string singleLine, MessageImportance messageImportance)
		{
			Log.LogDebugMessage (singleLine);
		}
	}
	
}
