using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.Shared
{
	class CopyFiles : SharedTestCommand
	{
		string sourcePathGlob;
		string destinationDirectoryPath;
		bool required;

		public CopyFiles (string sourcePathGlob, string destinationDirectoryPath, bool required)
			: base (nameof (CopyFiles), "Copy files using a glob pattern")
		{
			this.sourcePathGlob = EnsureParameterValue (nameof (sourcePathGlob), sourcePathGlob);
			this.destinationDirectoryPath = EnsureParameterValue (nameof (destinationDirectoryPath), destinationDirectoryPath);
			this.required = required;

			if (!Path.IsPathRooted (this.destinationDirectoryPath)) {
				this.destinationDirectoryPath = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, this.destinationDirectoryPath);
			}

			if (!Path.IsPathRooted (this.sourcePathGlob)) {
				this.sourcePathGlob = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, this.sourcePathGlob);
			}
		}

#pragma warning disable 1998
		protected override async Task<bool> Execute (XATest test)
		{
			Utilities.CreateDirectory (destinationDirectoryPath);

			bool copiedAny = false;
			foreach (string filePath in Directory.EnumerateFiles (Path.GetDirectoryName (sourcePathGlob), Path.GetFileName (sourcePathGlob))) {
				Utilities.CopyFileToDir (filePath, destinationDirectoryPath);
				copiedAny = true;
			}

			if (required && !copiedAny) {
				Log.ErrorLine ($"Failed to copy required file(s) using glob '{sourcePathGlob}'");
				return false;
			}

			return true;
		}
#pragma warning restore 1998
	}
}
