using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xamarin.Android.Tools;


namespace Xamarin.Android.Tasks
{
	public abstract class JavaToolTask : AndroidToolTask
	{
		/*
		Example Javac output for errors. Regex Matches on the first line, we then need to 
		process the second line to get the column number so the IDE can correctly
		mark where the error is. 
		
		TestMe.java:1: error: class, interface, or enum expected
		public classo TestMe { }
		^
		TestMe2.java:1: error: ';' expected
		public class TestMe2 {public vod Test ()}
		^
		2 errors
		*/
		const string CodeErrorRegExString = @"(?<file>.+\.java):(?<line>\d+):(?<error>.+)";
		/*
		 
		Sample OutOfMemoryError raised by java. RegEx matches the java.lang.* line of the error
		and splits it into an exception and an error

		UNEXPECTED TOP-LEVEL ERROR:
		java.lang.OutOfMemoryError: GC overhead limit exceeded
			at java.util.Arrays.copyOf(Arrays.java:2219)
			at java.util.ArrayList.grow(ArrayList.java:242)
			at java.util.ArrayList.ensureExplicitCapacity(ArrayList.java:216)
			at java.util.ArrayList.ensureCapacityInternal(ArrayList.java:208)
			at java.util.ArrayList.add(ArrayList.java:440)
			at com.android.dx.ssa.Dominators$DfsWalker.visitBlock(Dominators.java:263)
			at com.android.dx.ssa.SsaMethod.forEachBlockDepthFirst(SsaMethod.java:783)
			at com.android.dx.ssa.Dominators.run(Dominators.java:185)
			at com.android.dx.ssa.Dominators.make(Dominators.java:90)
			at com.android.dx.ssa.DomFront.run(DomFront.java:86)
			at com.android.dx.ssa.SsaConverter.placePhiFunctions(SsaConverter.java:297)
			at com.android.dx.ssa.SsaConverter.convertToSsaMethod(SsaConverter.java:51)
			at com.android.dx.ssa.Optimizer.optimize(Optimizer.java:98)
			at com.android.dx.ssa.Optimizer.optimize(Optimizer.java:72)
			at com.android.dx.dex.cf.CfTranslator.processMethods(CfTranslator.java:297)
			at com.android.dx.dex.cf.CfTranslator.translate0(CfTranslator.java:137)
			at com.android.dx.dex.cf.CfTranslator.translate(CfTranslator.java:93)
			at com.android.dx.command.dexer.Main.processClass(Main.java:729)
			at com.android.dx.command.dexer.Main.processFileBytes(Main.java:673)
			at com.android.dx.command.dexer.Main.access$300(Main.java:83)
			at com.android.dx.command.dexer.Main$1.processFileBytes(Main.java:602)
			at com.android.dx.cf.direct.ClassPathOpener.processArchive(ClassPathOpener.java:284)
			at com.android.dx.cf.direct.ClassPathOpener.processOne(ClassPathOpener.java:166)
			at com.android.dx.cf.direct.ClassPathOpener.process(ClassPathOpener.java:144)
			at com.android.dx.command.dexer.Main.processOne(Main.java:632)
			at com.android.dx.command.dexer.Main.processAllFiles(Main.java:505)
			at com.android.dx.command.dexer.Main.runMultiDex(Main.java:334)
			at com.android.dx.command.dexer.Main.run(Main.java:244)
			at com.android.dx.command.dexer.Main.main(Main.java:215)
			at com.android.dx.command.Main.main(Main.java:106)
		*/
		const string ExceptionRegExString = @"(?<exception>java.lang.+):(?<error>.+)";
		protected static readonly Regex CodeErrorRegEx = new Regex (CodeErrorRegExString, RegexOptions.Compiled);
		protected static readonly Regex ExceptionRegEx = new Regex (ExceptionRegExString, RegexOptions.Compiled);
		bool foundError = false;
		List<string> errorLines = new List<string> ();
		StringBuilder errorText = new StringBuilder ();
		string file;
		int line, column;

		public string JarPath { get; set; }

		public string JavaOptions { get; set; }

		public string JavaMaximumHeapSize { get; set; }

		public bool UseDaemon { get; set; }

		public ITaskItem [] DaemonClassPath { get; set; }

		public override bool RunTask ()
		{
			if (UseDaemon) {
				// NOTE: we require running a daemon per-working directory
				var key = ($"{nameof (JavaToolTask)}_{nameof (JavaDaemonClient)}", Directory.GetCurrentDirectory ());
				var client = BuildEngine4.GetRegisteredTaskObject (key, RegisteredTaskObjectLifetime.AppDomain) as JavaDaemonClient;
				if (client == null) {
					client = new JavaDaemonClient ();
					BuildEngine4.RegisterTaskObject (key, client, RegisteredTaskObjectLifetime.AppDomain, allowEarlyCollection: false);
				} else {
					Log.LogDebugMessage ("java daemon already running.");
				}
				client.Log = m => Log.LogDebugMessage (m);
				try {
					if (!client.IsConnected) {
						var cmd = new CommandLineBuilder ();
						if (!string.IsNullOrEmpty (JavaOptions)) {
							cmd.AppendSwitch (JavaOptions);
						}
						cmd.AppendSwitch ("-XX:+UseG1GC");
						cmd.AppendSwitchIfNotNull ("-Xmx", JavaMaximumHeapSize);
						cmd.AppendSwitchIfNotNull ("-classpath ", string.Join (Path.PathSeparator.ToString (), DaemonClassPath.Select (c => c.GetMetadata ("FullPath"))));
						cmd.AppendSwitchIfNotNull ("-Dfile.encoding=", "UTF8");
						cmd.AppendSwitch ("xamarin.android.JavaDaemon");
						client.Connect (GenerateFullPathToTool (), cmd.ToString ());
					}

					(int exitCode, string stdout, string stderr) = client.Invoke (MainClass, JarPath, GenerateCommandLineCommands ());
					foreach (var line in stdout.Split ('\n')) {
						var text = line.Trim ();
						if (!string.IsNullOrEmpty (text))
							LogEventsFromTextOutput (text, MessageImportance.Normal);
					}
					foreach (var line in stderr.Split ('\n')) {
						var text = line.Trim ();
						if (!string.IsNullOrEmpty (text))
							LogEventsFromTextOutput (line, MessageImportance.High);
					}
					if (exitCode != 0) {
						Log.LogError ($"{MainClass} exited with code: {exitCode}");
					}
				} finally {
					client.Log = null;
				}
				return !Log.HasLoggedErrors;
			} else {
				return base.RunTask ();
			}
		}

		public virtual string DefaultErrorCode => null;

		protected override string ToolName {
			get { return OS.IsWindows ? "java.exe" : "java"; }
		}

		protected override string GenerateCommandLineCommands ()
		{
			return GetCommandLineBuilder ().ToString ();
		}

		protected virtual CommandLineBuilder GetCommandLineBuilder ()
		{
			var cmd = new CommandLineBuilder ();
			if (!UseDaemon) {
				if (!string.IsNullOrEmpty (JavaOptions)) {
					cmd.AppendSwitch (JavaOptions);
				}
				cmd.AppendSwitchIfNotNull ("-Xmx", JavaMaximumHeapSize);
				cmd.AppendSwitchIfNotNull ("-classpath ", JarPath);
				cmd.AppendSwitchIfNotNull ("-Dfile.encoding=", "UTF8");
				cmd.AppendSwitch (MainClass);
			}
			return cmd;
		}

		protected override bool HandleTaskExecutionErrors ()
		{
			if (foundError) {
				errorText.Clear ();
				foreach (var line in errorLines) {
					if (!ProcessOutput (line))
						break;
				}
				if (foundError && errorText.Length > 0) {
					Log.LogError (ToolName, DefaultErrorCode, null, file, line - 1, column + 1, 0, 0, errorText.ToString ());
				}
				return !Log.HasLoggedErrors;
			}
			return base.HandleTaskExecutionErrors ();
		}

		protected override string GenerateFullPathToTool ()
		{
			return Path.Combine (ToolPath, ToolExe);
		}

		protected abstract string MainClass { get; }

		void LogFromException (string exception, string error) {
			switch (exception) {
				case "java.lang.OutOfMemoryError":
					Log.LogCodedError ("XA5213", 
						"java.lang.OutOfMemoryError. Consider increasing the value of $(JavaMaximumHeapSize). Java ran out of memory while executing '{0} {1}'",
						ToolName, GenerateCommandLineCommands ());
					break;
				default:
					Log.LogCodedError (DefaultErrorCode, "{0} : {1}", exception, error);
					break;
			}
		}

		bool ProcessOutput (string singleLine)
		{
			var match = CodeErrorRegEx.Match (singleLine);
			var exceptionMatch = ExceptionRegEx.Match (singleLine);

			if (match.Success) {
				if (!string.IsNullOrEmpty (file)) {
					Log.LogError (ToolName, DefaultErrorCode, null, file, line - 1, column + 1, 0, 0, errorText.ToString ());
					errorText.Clear ();
				}
				file = match.Groups ["file"].Value;
				line = int.Parse (match.Groups ["line"].Value) + 1;
				var error = match.Groups ["error"].Value;
				column = 0;

				errorText.AppendLine (error);
				return true;
			} else if (exceptionMatch.Success) {
				var error = exceptionMatch.Groups ["error"].Value;
				var exception = exceptionMatch.Groups ["exception"].Value;
				line = 1;
				file = "";
				column = 0;
				LogFromException (exception, error);
				return false;
			} else if (foundError) {
				if (singleLine.Trim () == "^") {
					column = singleLine.IndexOf ("^");
					return true;
				}

				if (singleLine.StartsWith ("Note:") || singleLine.Trim ().EndsWith ("errors")) {
					// See if we have one last error to print out
					Log.LogError (ToolName, DefaultErrorCode, null, file, line - 1, column + 1, 0, 0, errorText.ToString ());
					errorText.Clear ();
					foundError = false;
					return true;
				}
				errorText.AppendLine (singleLine);
			} 
			return true;
		}

		protected override void LogEventsFromTextOutput (string singleLine, MessageImportance messageImportance)
		{
			var match = CodeErrorRegEx.Match (singleLine);
			var exceptionMatch = ExceptionRegEx.Match (singleLine);

			if (match.Success || exceptionMatch.Success) {
				Log.LogMessage (MessageImportance.High, singleLine);
				foundError = true;
				errorLines.Add (singleLine);
				return;
			} else if (foundError) {
				Log.LogMessage (MessageImportance.High, singleLine);
				errorLines.Add (singleLine);
				return;
			} 
			base.LogEventsFromTextOutput (singleLine, messageImportance);
		}
	}
}

