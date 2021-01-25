using System;

namespace Xamarin.Android.Prepare
{
	partial class EssentialTools : AppObject
	{
		bool initialized;
		bool quiet;

		public bool IsInitialized => initialized;

		public EssentialTools (bool quiet = false)
		{
			this.quiet = quiet;
		}

		partial void InitGit (Context context, bool require);
		partial void InitSevenZip (Context context, bool require);

		public void Init (Context context)
		{
			bool require = AreToolsRequired (context);

			if (!quiet) {
				Log.StatusLine ();
				Log.StatusLine ("Locating essential tool binaries", ConsoleColor.DarkGreen);
			}

			InitGit (context, require);
			InitSevenZip (context, require);
			InitOS (context);
			initialized = true;
		}

		void ReportToolPath (string path)
		{
			if (quiet) {
				return;
			}

			if (!String.IsNullOrEmpty (path))
				Log.StatusLine ("     Found: ", path, tailColor: Log.DestinationColor);
			else
				Log.StatusLine ("   Missing: will be installed later");
		}

		string GetRequiredValue (string val, string name)
		{
			if (String.IsNullOrEmpty (val)) {
				throw new InvalidOperationException ($"{name} not found but required by this scenario");
			}

			return val;
		}

		partial void InitOS (Context context);
	}
}
