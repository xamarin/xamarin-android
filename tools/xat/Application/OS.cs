using System;
using System.Collections.Generic;
using System.IO;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	partial class OS : AppObject
	{
		public Dictionary<string, string> EnvironmentVariables { get; }
		public StringComparer DefaultStringComparer => FilePathComparer;
		public StringComparison DefaultStringComparison => FilePathComparison;

		public OS ()
		{
			EnvironmentVariables = new Dictionary<string, string> (StringComparer.Ordinal);
			InitOS ();
		}

		static bool IsExecutableEnsureValidFile (string filePath)
		{
			if (filePath.Length == 0) {
				return false;
			}

			if (!Utilities.FileExists (filePath)) {
				Log.Instance.DebugLine ($"File {filePath} does not exist");
				return false;
			}

			return true;
		}

		public string Which (string programPath, bool required = true)
		{
			if (String.IsNullOrEmpty (programPath)) {
				goto doneAndOut;
			}

			string match;
			// If it's any form of path, just return it as-is, possibly with executable extension added
			if (programPath.IndexOf (Path.DirectorySeparatorChar) >= 0) {
				match = GetExecutableWithExtension (programPath, (string ext) => {
					string fp = $"{programPath}{ext}";
					if (Utilities.FileExists (fp))
						return fp;
					return String.Empty;
				}
				);

				if (match.Length == 0 && Utilities.FileExists (programPath))
					match = programPath;

				if (match.Length > 0)
					return match;
				else if (required) {
					goto doneAndOut;
				}

				return programPath;
			}

			List<string> directories = GetPathDirectories ();
			match = GetExecutableWithExtension (programPath, (string ext) => FindProgram ($"{programPath}{ext}", directories));
			if (match.Length > 0)
				return AssertIsExecutable (match);

			match = FindProgram ($"{programPath}", directories);
			if (match.Length > 0)
				return AssertIsExecutable (match);

		  doneAndOut:
			if (required)
				throw new InvalidOperationException ($"Required program '{programPath}' could not be found");

			return String.Empty;
		}

		string GetExecutableWithExtension (string programPath, Func<string, string> finder)
		{
			List<string>? extensions = ExecutableExtensions;
			if (extensions == null || extensions.Count == 0)
				return String.Empty;

			foreach (string extension in extensions) {
				string match = finder (extension);
				if (match.Length > 0)
					return match;
			}

			return String.Empty;
		}

		protected static string FindProgram (string programName, List<string> directories)
		{
			foreach (string dir in directories) {
				string path = Path.Combine (dir, programName);
				if (Utilities.FileExists (path))
					return path;
			}

			return String.Empty;
		}

		protected static List <string> GetPathDirectories ()
		{
			var ret = new List <string> ();
			string path = Environment.GetEnvironmentVariable ("PATH")?.Trim () ?? String.Empty;
			if (String.IsNullOrEmpty (path))
				return ret;

			ret.AddRange (path.Split (new []{ Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries));
			return ret;
		}
	}
}
