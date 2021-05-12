// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

using System;
using System.Linq;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Text;
using System.Collections.Generic;
using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{

	public abstract class JavaCompileToolTask : JavaToolTask
	{
		public string StubSourceDirectory { get; set; }

		public ITaskItem[] JavaSourceFiles { get; set; }

		public ITaskItem[] Jars { get; set; }

		protected override string ToolName {
			get { return OS.IsWindows ? "javac.exe" : "javac"; }
		}

		private bool IsRunningInsideVS {
			get {
				var vside = false;
				return bool.TryParse(Environment.GetEnvironmentVariable("VSIDE"), out vside) && vside;
			}
		}

		internal string TemporarySourceListFile;

		public override bool RunTask ()
		{
			GenerateResponseFile ();

			var retval = base.RunTask ();

			try {
				File.Delete (TemporarySourceListFile);
			} catch (Exception) {
				// Ignore exception, a tiny temp file will get left on the user's system
			}

			return retval;
		}

		protected virtual void WriteOptionsToResponseFile (StreamWriter sw)
		{
		}

		private void GenerateResponseFile ()
		{
			TemporarySourceListFile = Path.GetTempFileName ();

			using (var sw = new StreamWriter (path:TemporarySourceListFile, append:false,
						encoding: Files.UTF8withoutBOM)) {

				WriteOptionsToResponseFile (sw);
				// Include any user .java files
				if (JavaSourceFiles != null)
					foreach (var file in JavaSourceFiles.Where (p => Path.GetExtension (p.ItemSpec) == ".java"))
						sw.WriteLine (string.Format ("\"{0}\"", file.ItemSpec.Replace (@"\", @"\\")));

				if (string.IsNullOrEmpty (StubSourceDirectory))
					return;

				if (!Directory.Exists (StubSourceDirectory))
					return;

				foreach (var file in Directory.GetFiles (StubSourceDirectory, "*.java", SearchOption.AllDirectories)) {
					// This makes sense.  BAD sense.  but sense.
					// Problem:
					//    A perfectly sensible path like "E:\tmp\a.java" generates a
					//    javac error that "E:       mp.java" can't be found.
					// Cause:
					//    javac uses java.io.StreamTokenizer to parse @response files, and
					//    the docs for StreamTokenizer.quoteChar(int) [0] say:
					//      The usual escape sequences such as "\n" and "\t" are recognized
					//      and converted to single characters as the string is parsed.
					//    i.e. '\' is an escape character!
					// Solution:
					//    Since '\' is an escape character, we need to escape it.
					// [0] http://download.oracle.com/javase/1.4.2/docs/api/java/io/StreamTokenizer.html#quoteChar(int)
					sw.WriteLine (string.Format ("\"{0}\"",
								file.Replace (@"\", @"\\").Normalize (NormalizationForm.FormC)));
				}
			}
		}
	}
}
