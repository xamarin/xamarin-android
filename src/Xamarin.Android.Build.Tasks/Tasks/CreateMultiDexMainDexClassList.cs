﻿// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using System.Collections.Specialized;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	public class CreateMultiDexMainDexClassList : JavaToolTask
	{
		public override string TaskPrefix => "CMD";

		protected override string MainClass => mainClass;

		string mainClass;

		[Required]
		public string ClassesOutputDirectory { get; set; }

		[Required]
		public string AndroidSdkBuildToolsPath { get; set; }

		[Required]
		public ITaskItem[] JavaLibraries { get; set; }

		[Required]
		public string DxJarPath { get; set; }

		[Required]
		public string ProguardJarPath { get; set; }

		public string MultiDexMainDexListFile { get; set; }
		public ITaskItem[] CustomMainDexListFiles { get; set; }
		public string ProguardInputJarFilter { get; set; }
		public string ExtraArgs { get; set; }

		Action<CommandLineBuilder> commandlineAction;
		string tempJar;
		bool writeOutputToKeepFile = false;

		public override bool RunTask ()
		{
			tempJar = Path.Combine (Path.GetTempPath (), Path.GetRandomFileName () + ".jar");
			mainClass = "proguard.ProGuard";
			JarPath = ProguardJarPath;
			commandlineAction = GenerateProguardCommands;
			// run proguard first
			var retval = base.RunTask ();
			if (!retval || Log.HasLoggedErrors)
				return false;

			mainClass = "com.android.multidex.MainDexListBuilder";
			JarPath = DxJarPath;
			commandlineAction = GenerateMainDexListBuilderCommands;
			// run java second

			if (File.Exists (MultiDexMainDexListFile))
				File.WriteAllText (MultiDexMainDexListFile, string.Empty);

			var result = base.RunTask () && !Log.HasLoggedErrors;

			if (result && CustomMainDexListFiles?.Length > 0) {
				var content = new List<string> ();
				foreach (var file in CustomMainDexListFiles) {
					if (File.Exists (file.ItemSpec)) {
						content.Add (File.ReadAllText (file.ItemSpec));
					} else {
						Log.LogCodedWarning ("XA4305", file.ItemSpec, 0, $"'MultiDexMainDexList' file '{file.ItemSpec}' does not exist.");
					}
				}
				File.AppendAllText (MultiDexMainDexListFile, string.Concat (content));
			}

			return result;
		}

		protected override string GenerateCommandLineCommands ()
		{
			var cmd = base.GetCommandLineBuilder ();
			commandlineAction (cmd);
			return cmd.ToString ();
		}

		void GenerateProguardCommands (CommandLineBuilder cmd)
		{
			var enclosingChar = OS.IsWindows ? "\"" : string.Empty;
			var jars = JavaLibraries.Select (i => i.ItemSpec).Concat (new string [] { Path.Combine (ClassesOutputDirectory, "..", "classes.zip") });
			cmd.AppendSwitchUnquotedIfNotNull ("-injars ", "\"'" + string.Join ($"'{ProguardInputJarFilter}{Path.PathSeparator}'", jars) + $"'{ProguardInputJarFilter}\"");
			cmd.AppendSwitch ("-dontwarn");
			cmd.AppendSwitch ("-forceprocessing");
			cmd.AppendSwitchIfNotNull ("-outjars ", tempJar);
			cmd.AppendSwitchIfNotNull ("-libraryjars ", $"'{Path.Combine (AndroidSdkBuildToolsPath, "lib", "shrinkedAndroid.jar")}'");
			cmd.AppendSwitch ("-dontoptimize");
			cmd.AppendSwitch ("-dontobfuscate");
			cmd.AppendSwitch ("-dontpreverify");
			cmd.AppendSwitchUnquotedIfNotNull ("-include ", $"{enclosingChar}'{Path.Combine (AndroidSdkBuildToolsPath, "mainDexClasses.rules")}'{enclosingChar}");
		}

		void GenerateMainDexListBuilderCommands(CommandLineBuilder cmd)
		{
			var enclosingDoubleQuote = OS.IsWindows ? "\"" : string.Empty;
			var enclosingQuote = OS.IsWindows ? string.Empty : "'";
			var jars = JavaLibraries.Select (i => i.ItemSpec).Concat (new string [] { Path.Combine (ClassesOutputDirectory, "..", "classes.zip") });
			if (!string.IsNullOrWhiteSpace (ExtraArgs))
				cmd.AppendSwitch (ExtraArgs);
			cmd.AppendSwitch ($"{enclosingDoubleQuote}{tempJar}{enclosingDoubleQuote}");
			cmd.AppendSwitchUnquotedIfNotNull ("", $"{enclosingDoubleQuote}{enclosingQuote}" +
				string.Join ($"{enclosingQuote}{Path.PathSeparator}{enclosingQuote}", jars) + 
				$"{enclosingQuote}{enclosingDoubleQuote}");
			writeOutputToKeepFile = true;
		}

		protected override void LogEventsFromTextOutput (string singleLine, MessageImportance messageImportance)
		{
			var match = CodeErrorRegEx.Match (singleLine);
			var exceptionMatch = ExceptionRegEx.Match (singleLine);

			if (writeOutputToKeepFile && !match.Success && !exceptionMatch.Success)
				File.AppendAllText (MultiDexMainDexListFile, singleLine + "\n");
			base.LogEventsFromTextOutput (singleLine, messageImportance);
		}
	}
}

