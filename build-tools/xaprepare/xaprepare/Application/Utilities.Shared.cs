using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Xamarin.Android.Prepare
{
	static partial class Utilities
	{
		static readonly TimeSpan ExceptionRetryInitialDelay = TimeSpan.FromSeconds (30);
		static readonly int ExceptionRetries = 5;
		static readonly Log Log = Log.Instance;
		static readonly Dictionary<string, string> versionCache = new Dictionary<string, string> (StringComparer.Ordinal);

		public static readonly Encoding UTF8NoBOM = new UTF8Encoding (false);

		public static string GetPathRelativeToCWD (string path)
		{
			return GetRelativePath (Environment.CurrentDirectory, path);
		}

		public static string ShortenGitHash (string fullHash)
		{
			if (String.IsNullOrEmpty (fullHash))
				return String.Empty;

			if (fullHash.Length <= Configurables.Defaults.AbbreviatedHashLength)
				return fullHash;

			return fullHash.Substring (0, (int)Configurables.Defaults.AbbreviatedHashLength);
		}

		public static void CopyFilesSimple (IEnumerable<string> sourceFiles, string destinationDirectory, bool overwriteDestinationFile = true)
		{
			if (sourceFiles == null)
				throw new ArgumentNullException (nameof (sourceFiles));
			if (String.IsNullOrEmpty (destinationDirectory))
				throw new ArgumentException ("must not be null or empty", nameof (destinationDirectory));

			CreateDirectory (destinationDirectory);
			foreach (string src in sourceFiles) {
				CopyFileInternal (src, destinationDirectory, destinationFileName: String.Empty, overwriteDestinationFile: overwriteDestinationFile);
			}
		}

		public static void CopyFile (string sourceFilePath, string destinationFilePath, bool overwriteDestinationFile = true)
		{
			if (String.IsNullOrEmpty (sourceFilePath))
				throw new ArgumentException ("must not be null or empty", nameof (sourceFilePath));
			if (String.IsNullOrEmpty (destinationFilePath))
				throw new ArgumentException ("must not be null or empty", nameof (destinationFilePath));

			CopyFileInternal (sourceFilePath, Path.GetDirectoryName (destinationFilePath), Path.GetFileName (destinationFilePath), overwriteDestinationFile);
		}

		public static void CopyFileToDir (string sourceFilePath, string destinationDirectory, string? destinationFileName = null, bool overwriteDestinationFile = true)
		{
			if (String.IsNullOrEmpty (sourceFilePath))
				throw new ArgumentException ("must not be null or empty", nameof (sourceFilePath));
			if (String.IsNullOrEmpty (destinationDirectory))
				throw new ArgumentException ("must not be null or empty", nameof (destinationDirectory));

			CreateDirectory (destinationDirectory);
			CopyFileInternal (sourceFilePath, destinationDirectory, destinationFileName ?? String.Empty, overwriteDestinationFile);
		}

		static void CopyFileInternal (string sourceFilePath, string destinationDirectory, string destinationFileName, bool overwriteDestinationFile)
		{
			string targetFileName;
			if (String.IsNullOrEmpty (destinationFileName))
				targetFileName = Path.GetFileName (sourceFilePath);
			else
				targetFileName = destinationFileName;

			if (!FileExists (sourceFilePath))
				throw new InvalidOperationException ($"Location '{sourceFilePath}' does not point to a file");

			CreateDirectory (destinationDirectory);
			string destFile = Path.Combine (destinationDirectory, targetFileName);
			Log.DebugLine ($"Copying file '{sourceFilePath}' to '{destFile}' (overwrite destination: {overwriteDestinationFile})");
			File.Copy (sourceFilePath, destFile, overwriteDestinationFile);
		}

		public static void DeleteDirectoryWithRetry (string directoryPath, bool recursive)
		{
			TimeSpan delay = ExceptionRetryInitialDelay;
			Exception? ex = null;
			bool tryResetFilePermissions = false;

			for (int i = 0; i < ExceptionRetries; i++) {
				ex = null;
				try {
					if (tryResetFilePermissions) {
						tryResetFilePermissions = false;
						ResetFilePermissions (directoryPath);
					}
					Log.DebugLine ($"Deleting directory {directoryPath} (recursively? {recursive})");
					Directory.Delete (directoryPath, recursive);
					return;
				} catch (IOException e) {
					ex = e;
				} catch (UnauthorizedAccessException e) {
					ex = e;
					tryResetFilePermissions = true;
				}

				WaitAWhile ($"Directory {directoryPath} deletion", i, ref ex, ref delay);
			}

			if (ex != null)
				throw ex;
		}

		static void ResetFilePermissions (string directoryPath)
		{
			foreach (string file in Directory.EnumerateFiles (directoryPath, "*", SearchOption.AllDirectories)) {
				FileAttributes attrs = File.GetAttributes (file);
				if ((attrs & (FileAttributes.Hidden | FileAttributes.ReadOnly | FileAttributes.System)) == 0)
					continue;

				File.SetAttributes (file, FileAttributes.Normal);
			}
		}

		static void WaitAWhile (string what, int which, ref Exception ex, ref TimeSpan delay)
		{
			Log.DebugLine ($"{what} attempt no. {which + 1} failed, retrying after delay of {delay}");
			if (ex != null)
				Log.DebugLine ($"Failure cause: {ex.Message}");
			Thread.Sleep (delay);
			delay = TimeSpan.FromMilliseconds (delay.TotalMilliseconds * 2);
		}

		public static void DeleteDirectory (string directoryPath, bool ignoreErrors = false, bool recurse = true)
		{
			if (String.IsNullOrEmpty (directoryPath))
				throw new ArgumentException ("must not be null or empty", nameof (directoryPath));

			if (!Directory.Exists (directoryPath))
				return;

			try {
				Log.DebugLine ($"Deleting directory recursively: {directoryPath}");
				DeleteDirectoryWithRetry (directoryPath, recurse);
			} catch (Exception ex) {
				if (ignoreErrors) {
					Log.DebugLine ($"Failed to delete directory: {directoryPath}");
					Log.DebugLine (ex.ToString ());
					return;
				}

				throw;
			}
		}

		public static void DeleteDirectorySilent (string directoryPath, bool recurse = true)
		{
			if (String.IsNullOrEmpty (directoryPath))
				return;

			DeleteDirectory (directoryPath, ignoreErrors: true, recurse: true);
		}

		public static void DeleteFile (string filePath, bool ignoreErrors = false)
		{
			if (String.IsNullOrEmpty (filePath))
				throw new ArgumentException ("must not be null or empty", nameof (filePath));

			// In this case we don't care if the file is a dangling symlink or not (which FileExists checks for) - we
			// want to remove whatever exists.
			if (!File.Exists (filePath))
				return;

			try {
				Log.DebugLine ($"Deleting file: {filePath}");
				File.Delete (filePath);
			} catch (Exception ex) {
				if (ignoreErrors) {
					Log.DebugLine ($"Failed to delete file: {filePath}");
					Log.DebugLine (ex.ToString ());
					return;
				}

				throw;
			}
		}

		public static void DeleteFileSilent (string filePath)
		{
			if (String.IsNullOrEmpty (filePath))
				return;

			DeleteFile (filePath, true);
		}

		public static void CreateDirectory (string directoryPath)
		{
			if (String.IsNullOrEmpty (directoryPath))
				throw new ArgumentException ("must not be null or empty", nameof (directoryPath));

			if (Directory.Exists (directoryPath))
				return;

			Log.DebugLine ($"Creating directory: {directoryPath}");
			Directory.CreateDirectory (directoryPath);
		}

		public static (bool success, string version) GetProgramVersion (string programPath)
		{
			if (String.IsNullOrEmpty (programPath))
				throw new ArgumentException ("must not be null or empty", nameof (programPath));

			string version;
			if (versionCache.TryGetValue (programPath, out version))
				return (true, version);

			bool fetcherPresent;

			(fetcherPresent, version) = RunVersionFetcher (programPath, programPath);
			if (fetcherPresent)
				return (IsValidVersion (), version);

			if (programPath.IndexOf (Path.DirectorySeparatorChar) >= 0) {
				(fetcherPresent, version) = RunVersionFetcher (Path.GetFileName (programPath), programPath);
				if (fetcherPresent)
					return (IsValidVersion (), version);
			}

			return (false, String.Empty);

			bool IsValidVersion ()
			{
				bool valid = !String.IsNullOrEmpty (version);
				if (!valid)
					return false;

				versionCache [programPath] = version;
				return valid;
			}
		}

		static (bool fetcherPresent, string version) RunVersionFetcher (string program, string programPath)
		{
			Log.DebugLine ($"Attempting to find version fetcher for: {program} ({programPath})");
			ProgramVersionParser vp;
			if (Context.Instance.VersionFetchers.Fetchers.TryGetValue (program, out vp)) {
				Log.DebugLine ("Fetcher found");
				string version = vp.GetVersion (Context.Instance, programPath);
				Log.DebugLine ($"{program} version: {version}");
				return (true, version);
			}

			Log.DebugLine ("Fetcher not found");
			return (false, String.Empty);
		}

		public static string GetStringFromStdout (string command, params string?[] arguments)
		{
			return GetStringFromStdout (command, throwOnErrors: false, trimTrailingWhitespace: true, arguments: arguments);
		}

		public static string GetStringFromStdout (string command, bool throwOnErrors, params string?[] arguments)
		{
			return GetStringFromStdout (command, throwOnErrors, trimTrailingWhitespace: true, arguments: arguments);
		}

		public static string GetStringFromStdout (string command, bool throwOnErrors, bool trimTrailingWhitespace, params string?[] arguments)
		{
			return GetStringFromStdout (new ProcessRunner (command, arguments), throwOnErrors, trimTrailingWhitespace);
		}

		public static string GetStringFromStdout (string command, bool throwOnErrors, bool trimTrailingWhitespace, bool quietErrors, params string?[] arguments)
		{
			return GetStringFromStdout (new ProcessRunner (command, arguments), throwOnErrors, trimTrailingWhitespace, quietErrors);
		}

		public static string GetStringFromStdout (ProcessRunner runner, bool throwOnErrors = false, bool trimTrailingWhitespace = true, bool quietErrors = false)
		{
			using (var sw = new StringWriter ()) {
				runner.AddStandardOutputSink (sw);
				if (!runner.Run ()) {
					LogError ("did not exit cleanly");
					return String.Empty;
				}

				if (runner.ExitCode != 0) {
					LogError ($"failed with exit code {runner.ExitCode}");
					return String.Empty;
				}

				string ret = sw.ToString ();
				if (trimTrailingWhitespace)
					return ret.TrimEnd ();
				return ret;
			}

			void LogError (string message)
			{
				string msg = $"{runner.FullCommandLine}: {message}";
				if (throwOnErrors)
					throw new InvalidOperationException (msg);
				if (quietErrors)
					Log.DebugLine (msg);
				else
					Log.ErrorLine (msg);
			}
		}

		public static string GetRelativePath (string relativeTo, string path)
		{
			return GetRelativePath (relativeTo, path, Context.Instance.OS.DefaultStringComparison);
		}

		// Adapted from CoreFX sources
		public static string GetRelativePath (string relativeTo, string path, StringComparison comparisonType)
		{
			if (String.IsNullOrEmpty (relativeTo))
				throw new ArgumentException ("must not be null or empty", nameof (relativeTo));

			if (String.IsNullOrEmpty (path))
				throw new ArgumentException ("must not be null or empty", nameof (path));

			relativeTo = Path.GetFullPath (relativeTo);
			path = Path.GetFullPath (path);

			// Need to check if the roots are different- if they are we need to return the "to" path.
			if (!AreRootsEqual (relativeTo, path, comparisonType))
				return path;

			int commonLength = GetCommonPathLength (relativeTo, path, ignoreCase: comparisonType == StringComparison.OrdinalIgnoreCase);

			// If there is nothing in common they can't share the same root, return the "to" path as is.
			if (commonLength == 0)
				return path;

			// Trailing separators aren't significant for comparison
			int relativeToLength = relativeTo.Length;
			if (EndsInDirectorySeparator (relativeTo))
				relativeToLength--;

			bool pathEndsInSeparator = EndsInDirectorySeparator (path);
			int pathLength = path.Length;
			if (pathEndsInSeparator)
				pathLength--;

			// If we have effectively the same path, return "."
			if (relativeToLength == pathLength && commonLength >= relativeToLength)
				return ".";

			// We have the same root, we need to calculate the difference now using the
			// common Length and Segment count past the length.
			//
			// Some examples:
			//
			//  C:\Foo C:\Bar L3, S1 -> ..\Bar
			//  C:\Foo C:\Foo\Bar L6, S0 -> Bar
			//  C:\Foo\Bar C:\Bar\Bar L3, S2 -> ..\..\Bar\Bar
			//  C:\Foo\Foo C:\Foo\Bar L7, S1 -> ..\Bar

			var sb = new StringBuilder (Math.Max (relativeTo.Length, path.Length));

			// Add parent segments for segments past the common on the "from" path
			if (commonLength < relativeToLength) {
				sb.Append ("..");

				for (int i = commonLength + 1; i < relativeToLength; i++) {
					if (IsDirectorySeparator (relativeTo[i])) {
						sb.Append (Path.DirectorySeparatorChar);
						sb.Append ("..");
					}
				}
			} else if (IsDirectorySeparator (path[commonLength])) {
				// No parent segments and we need to eat the initial separator
				//  (C:\Foo C:\Foo\Bar case)
				commonLength++;
			}

			// Now add the rest of the "to" path, adding back the trailing separator
			int differenceLength = pathLength - commonLength;
			if (pathEndsInSeparator)
				differenceLength++;

			if (differenceLength > 0) {
				if (sb.Length > 0) {
					sb.Append (Path.DirectorySeparatorChar);
				}

				sb.Append(path, commonLength, differenceLength);
			}

			return sb.ToString ();
		}

		// Adapted from CoreFX sources
		static bool AreRootsEqual (string first, string second, StringComparison comparisonType)
		{
			int firstRootLength = GetRootLength (first);
			int secondRootLength = GetRootLength (second);

			return firstRootLength == secondRootLength
				&& String.Compare (
					strA: first,
					indexA: 0,
					strB: second,
					indexB: 0,
					length: firstRootLength,
					comparisonType: comparisonType) == 0;
		}

		// Adapted from CoreFX sources
		static int GetCommonPathLength (string first, string second, bool ignoreCase)
		{
			int commonChars = EqualStartingCharacterCount (first, second, ignoreCase: ignoreCase);

			// If nothing matches
			if (commonChars == 0)
				return commonChars;

			// Or we're a full string and equal length or match to a separator
			if (commonChars == first.Length && (commonChars == second.Length || IsDirectorySeparator (second[commonChars])))
				return commonChars;

			if (commonChars == second.Length && IsDirectorySeparator (first[commonChars]))
				return commonChars;

			// It's possible we matched somewhere in the middle of a segment e.g. C:\Foodie and C:\Foobar.
			while (commonChars > 0 && !IsDirectorySeparator (first[commonChars - 1]))
				commonChars--;

			return commonChars;
		}

		// Adapted from CoreFX sources
		static unsafe int EqualStartingCharacterCount (string first, string second, bool ignoreCase)
		{
			if (String.IsNullOrEmpty (first) || string.IsNullOrEmpty (second))
				return 0;

			int commonChars = 0;
			fixed (char* f = first) {
				fixed (char* s = second) {
					char* l = f;
					char* r = s;
					char* leftEnd = l + first.Length;
					char* rightEnd = r + second.Length;

					while (l != leftEnd && r != rightEnd && (*l == *r || (ignoreCase && char.ToUpperInvariant ((*l)) == char.ToUpperInvariant ((*r))))) {
						commonChars++;
						l++;
						r++;
					}
				}
			}

			return commonChars;
		}

		// Adapted from CoreFX sources
		static bool EndsInDirectorySeparator (string path) => path.Length > 0 && IsDirectorySeparator (path[path.Length - 1]);
	}
}
