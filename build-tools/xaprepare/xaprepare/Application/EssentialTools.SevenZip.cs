using System;

namespace Xamarin.Android.Prepare
{
	partial class EssentialTools
	{
		string sevenZipPath = String.Empty;

		public string SevenZipPath {
			get => GetRequiredValue (sevenZipPath, "7zip");
			set => sevenZipPath = value;
		}

		partial void InitSevenZip (Context context, bool require)
		{
			if (!quiet) {
				Log.StatusLine ($"  {context.Characters.Bullet} 7za", ConsoleColor.White);
			}
			SevenZipPath = context.OS.Which ("7za", required: require);
			ReportToolPath (sevenZipPath);
		}
	}
}
