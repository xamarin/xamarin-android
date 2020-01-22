// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;

using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;

namespace Xamarin.Android.Tasks
{
	public class CompileToDalvik : JavaToolTask
	{
		public override string TaskPrefix => "CTX";

		protected override string MainClass => "com.android.dx.command.Main";

		public ITaskItem [] AdditionalJavaLibraryReferences { get; set; }

		[Required]
		public string ClassesOutputDirectory { get; set; }

		public string ClassesZip { get; set; }

		public string DxExtraArguments { get; set; }

		[Required]
		public ITaskItem[] JavaLibrariesToCompile { get; set; }

		public ITaskItem [] AlternativeJarFiles { get; set; }

		public bool MultiDexEnabled { get; set; }
		
		public string MultiDexMainDexListFile { get; set; }

		string inputListFile;

		public override bool RunTask ()
		{
			if (!Directory.Exists (ClassesOutputDirectory))
				Directory.CreateDirectory (ClassesOutputDirectory);

			bool ret = false;
			inputListFile = Path.GetTempFileName ();
			try {
				ret = base.RunTask ();
			} catch (FileNotFoundException ex) {
				Log.LogCodedError ("XA1003", ex.ToString ());
			} finally {
				if (File.Exists (inputListFile) && !Log.HasLoggedErrors)
					File.Delete (inputListFile);
			}

			return ret && !Log.HasLoggedErrors;
		}

		protected override CommandLineBuilder GetCommandLineBuilder ()
		{
			//   Running command: C:\Program Files\Java\jdk1.6.0_25\bin\java.exe -jar
			//     C:\Program Files (x86)\Android\android-sdk\platform-tools\lib\dx.jar --dex
			//     --output=C:\Users\jeff\Documents\Visual Studio 2010\Projects\<project>\...\android\bin\classes.dex
			//     C:\Users\jeff\Documents\Visual Studio 2010\Projects\<project>\...\android\bin\classes
			//     C:\Users\jeff\Documents\Visual Studio 2010\Projects\<project>\...\android\bin\mono.android.jar

			var cmd = base.GetCommandLineBuilder ();
			cmd.AppendSwitch (DxExtraArguments);
			cmd.AppendSwitchIfNotNull ("--input-list=", inputListFile);

			if (MultiDexEnabled) {
				if (string.IsNullOrEmpty (MultiDexMainDexListFile)) {
					Log.LogCodedWarning ("XA4305", $"MultiDex is enabled, but '{nameof (MultiDexMainDexListFile)}' was not specified.");
				} else if (!File.Exists (MultiDexMainDexListFile)) {
					Log.LogCodedWarning ("XA4305", MultiDexMainDexListFile, 0, $"MultiDex is enabled, but main dex list file '{MultiDexMainDexListFile}' does not exist.");
				} else {
					cmd.AppendSwitch ("--multi-dex");
					cmd.AppendSwitchIfNotNull ("--main-dex-list=", MultiDexMainDexListFile);
				}
			}
			cmd.AppendSwitchIfNotNull ("--output ", Path.GetDirectoryName (ClassesOutputDirectory));

			using (var sw = new StreamWriter (path: inputListFile, append: false,
					encoding: new UTF8Encoding (encoderShouldEmitUTF8Identifier: false))) {
				// .jar files
				if (AlternativeJarFiles != null && AlternativeJarFiles.Any ()) {
					Log.LogDebugMessage ("  processing AlternativeJarFiles...");
					foreach (var l in AlternativeJarFiles) {
						var fullPath = Path.GetFullPath (l.ItemSpec);
						Log.LogDebugMessage ($"    {fullPath}");
						sw.WriteLine (fullPath);
					}
				} else {
					Log.LogDebugMessage ("  processing ClassesOutputDirectory...");
					if (!string.IsNullOrEmpty (ClassesZip) && File.Exists (ClassesZip)) {
						Log.LogDebugMessage ($"    {ClassesZip}");
						sw.WriteLine (Path.GetFullPath (ClassesZip));
					}
					foreach (var jar in JavaLibrariesToCompile) {
						var fullPath = Path.GetFullPath (jar.ItemSpec);
						Log.LogDebugMessage ($"    {fullPath}");
						sw.WriteLine (fullPath);
					}
				}
			}

			return cmd;
		}
	}
}
