using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Xamarin.Android.Prepare
{
	partial class BuildInfo : AppObject
	{
		static readonly char[] NDKPropertySeparator = new [] { '=' };

		public string CommitOfLastVersionChange { get; private set; } = String.Empty;

		// Not available from the start, only after NDK is installed
		public string NDKRevision            { get; private set; } = String.Empty;
		public string NDKVersionMajor        { get; private set; } = String.Empty;
		public string NDKVersionMinor        { get; private set; } = String.Empty;
		public string NDKVersionMicro        { get; private set; } = String.Empty;
		public string NDKVersionTag          { get; private set; } = String.Empty;
		public string NDKMinimumApiAvailable { get; private set; } = String.Empty;

		public string VersionHash            { get; private set; } = String.Empty;
		public string MonoHash               { get; private set; } = String.Empty;
		public string FullMonoHash           { get; private set; } = String.Empty;

		public async Task GatherGitInfo (Context context)
		{
			if (context == null)
				throw new ArgumentNullException (nameof (context));

			Log.StatusLine ();
			Log.StatusLine ("Determining basic build information", ConsoleColor.DarkGreen);
			await DetermineLastVersionChangeCommit (context);
			Log.StatusLine ();
			DetermineMonoHash (context);
			Log.StatusLine ();
		}

		public bool GatherNDKInfo (Context context)
		{
			string ndkDir = Configurables.Paths.AndroidNdkDirectory;
			string props = Path.Combine (ndkDir, "source.properties");
			if (!File.Exists (props)) {
				Log.ErrorLine ("NDK properties file does not exist: ", props, tailColor: Log.DestinationColor);
				return false;
			}

			string[] lines = File.ReadAllLines (props);
			foreach (string l in lines) {
				string line = l.Trim ();
				string[] parts = line.Split (NDKPropertySeparator, 2);
				if (parts.Length != 2)
					continue;

				if (String.Compare ("Pkg.Revision", parts [0].Trim (), StringComparison.Ordinal) != 0)
					continue;

				string rev = parts [1].Trim ();
				NDKRevision = rev;

				if (!Utilities.ParseAndroidPkgRevision (rev, out Version? ver, out string tag)) {
					Log.ErrorLine ($"Unable to parse NDK revision '{rev}' as a valid version string");
					return false;
				}

				NDKVersionMajor = ver.Major.ToString ();
				NDKVersionMinor = ver.Minor.ToString ();
				NDKVersionMicro = ver.Build.ToString ();
				NDKVersionTag = tag;
				break;
			}

			Log.DebugLine ($"Looking for minimum API available in {ndkDir}");
			int minimumApi = Int32.MaxValue;
			foreach (var kvp in Configurables.Defaults.AndroidToolchainPrefixes) {
				string dirName = kvp.Value;
				string platforms = Path.Combine (Configurables.Paths.AndroidToolchainSysrootLibDirectory, dirName);
				Log.DebugLine ($"  searching in {platforms}");
				foreach (string p in Directory.EnumerateDirectories (platforms, "*", SearchOption.TopDirectoryOnly)) {
					string plibc = Path.Combine (p, "libc.so");
					if (!Utilities.FileExists (plibc)) {
						continue;
					}

					Log.DebugLine ($"    found {p}");
					string pdir = Path.GetFileName (p);
					int api;
					if (!Int32.TryParse (pdir, out api))
						continue;

					if (api >= minimumApi)
						continue;

					minimumApi = api;
				}
			}

			Log.DebugLine ($"Detected minimum NDK API level: {minimumApi}");
			NDKMinimumApiAvailable = minimumApi.ToString ();
			return true;
		}

		void DetermineMonoHash (Context context)
		{
			GitRunner git = CreateGitRunner (context);

			Log.StatusLine ($"  {context.Characters.Bullet} Mono commit hash", ConsoleColor.Gray);
			List<ExternalGitDependency> externalDependencies = ExternalGitDependency.GetDependencies (context, Configurables.Paths.ExternalGitDepsFilePath, quiet: true);
			ExternalGitDependency? mono = externalDependencies.Where (
				eg => eg != null &&
				      String.Compare ("mono", eg.Owner, StringComparison.Ordinal) == 0 &&
				      String.Compare ("mono", eg.Name, StringComparison.Ordinal) == 0).FirstOrDefault ();

			FullMonoHash = (mono?.Commit ?? String.Empty).Trim ();
			MonoHash = EnsureHash ("Mono", Utilities.ShortenGitHash (FullMonoHash));

			string EnsureHash (string name, string hash)
			{
				if (String.IsNullOrEmpty (hash))
					throw new InvalidOperationException ($"Unable to determine {name} commit hash");
				Log.StatusLine ("    Commit: ", hash, tailColor: ConsoleColor.Cyan);
				Log.StatusLine ();

				return hash;
			}
		}

		async Task DetermineLastVersionChangeCommit (Context context)
		{
			Log.StatusLine ($"  {context.Characters.Bullet} Commit of last version change", ConsoleColor.Gray);
			GitRunner git = CreateGitRunner (context);
			IList <GitRunner.BlamePorcelainEntry>? blameEntries;

			blameEntries = await git.Blame ("Configuration.props");
			if (blameEntries == null || blameEntries.Count == 0)
				throw new InvalidOperationException ("Unable to determine the last version change commit");

			foreach (GitRunner.BlamePorcelainEntry be in blameEntries) {
				if (be == null || String.IsNullOrEmpty (be.Line))
					continue;

				if (be.Line.IndexOf ("<ProductVersion>") >= 0) {
					Log.DebugLine ($"Last version change on {GetCommitDate (be)} by {be.Author}");
					CommitOfLastVersionChange = be.Commit;
					Log.StatusLine ("    Commit: ", be.Commit, tailColor: ConsoleColor.Cyan);
					break;
				}
			}
		}

		string GetCommitDate (GitRunner.BlamePorcelainEntry be)
		{
			int tzOffset = GetTZOffset (be.CommitterTZ, be);
			int committerTimeUTC = (int)be.CommitterTime + tzOffset;
			var unixEpoch = new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			DateTime dt = unixEpoch.AddSeconds (committerTimeUTC);

			return dt.ToString ("R", DateTimeFormatInfo.InvariantInfo);
		}

		int GetTZOffset (string tz, GitRunner.BlamePorcelainEntry be)
		{
			if (String.IsNullOrEmpty (tz)) {
				Log.DebugLine ($"No timezone information from `git blame` for commit {be.Commit}");
				return 0;
			}

			if (tz.Length != 5) {
				LogUnexpectedFormat ();
				return 0;
			}

			int tzSign;
			switch (tz [0]) {
				case '-':
					tzSign = 1;
					break;

				case '+':
					tzSign = -1;
					break;

				default:
					LogUnexpectedFormat ();
					return 0;
			}

			if (!Int32.TryParse (tz.Substring (1, 2), out int hours)) {
				LogUnexpectedFormat ();
				return 0;
			}

			if (!Int32.TryParse (tz.Substring (3, 2), out int minutes)) {
				LogUnexpectedFormat ();
				return 0;
			}

			return tzSign * ((hours * 3600) + (minutes * 60));

			void LogUnexpectedFormat ()
			{
				Log.DebugLine ($"Unexpected timezone format from `git blame` for commit {be.Commit}: {tz}");
			}
		}

		GitRunner CreateGitRunner (Context context)
		{
			return new GitRunner (context, Log) {
				LogMessageIndent = "    Running: "
			};
		}
	}
}
