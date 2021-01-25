using System;

namespace Xamarin.Android.Prepare
{
	partial class EssentialTools
	{
		string gitPath = String.Empty;

		public string GitPath {
			get => GetRequiredValue (gitPath, "git");
			set => gitPath = value;
		}

		partial void InitGit (Context context, bool require)
		{
			if (!quiet) {
				Log.StatusLine ($"  {context.Characters.Bullet} git", ConsoleColor.White);
			}
			GitPath = context.OS.Which ("git", required: require);
			ReportToolPath (gitPath);
		}
	}
}
