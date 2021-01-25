using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class MSBuildPrep : MSBuildTimingTestCommand
	{
		string deleteFilesGlob;

		public override string Target => "UNUSED";
		public override string ID     => "UNUSED";

		public MSBuildPrep (string deleteFilesGlob)
			: base (nameof (MSBuildPrep), "Prepare environment for MSBuild timing tests")
		{
			this.deleteFilesGlob = EnsureParameterValue (nameof (deleteFilesGlob), deleteFilesGlob);
		}

#pragma warning disable 1998
		protected async override Task<bool> Run (TestMSBuildTiming test)
		{
			foreach (string filePath in Directory.EnumerateFiles (Path.GetDirectoryName (deleteFilesGlob), Path.GetFileName (deleteFilesGlob))) {
				Utilities.DeleteFileSilent (filePath);
			}

			return true;
		}
#pragma warning restore 1998
	}
}
