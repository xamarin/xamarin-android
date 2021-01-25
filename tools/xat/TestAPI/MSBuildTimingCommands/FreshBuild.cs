using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.MSBuildTiming
{
	class FreshBuild : MSBuildTimingTestCommand
	{
		public override string Target => "SignAndroidPackage";
		public override string ID     => nameof (FreshBuild);

		public FreshBuild ()
			: base (nameof (FreshBuild), "A fresh build after a clean checkout")
		{}

		protected async override Task<bool> Run (TestMSBuildTiming test)
		{
			// Simulate a clean checkout with git commands
			var git = new GitRunner (Context);

			bool success;
			if (test.Repo.Length == 0) {
				success = await git.Clean (
					path: test.DirectoryPath,
					force: true,
					recurse: true,
					noStandardIgnoreRules: true,
					workingDirectory: BuildPaths.XamarinAndroidSourceRoot
				);

				if (!success) {
					Log.ErrorLine ($"`git clean` in '{test.DirectoryPath}' failed.");
					return false;
				}
			}

			// If the test defines Repo, we need to clone it
			if (test.Repo.Length > 0) {
				Utilities.DeleteDirectorySilent (test.DirectoryPath);

				if (!await git.Clone (test.Repo, test.DirectoryPath)) {
					Log.ErrorLine ($"Failed to clone git repository '{test.Repo}' into '{test.DirectoryPath}'");
					return false;
				}

				if (test.Commit.Length > 0 && !await git.CheckoutCommit (test.DirectoryPath, test.Commit)) {
					Log.ErrorLine ($"Failed to checkout commit '{test.Commit}' in '{test.DirectoryPath}'");
					return false;
				}
			}

			if (test.Restore.Length > 0) {
				var nuget = new NuGetRunner (Context);
				if (!await nuget.Restore (Utilities.EnsureFullPath (test.Restore))) {
					Log.ErrorLine ($"Failed to restore nuget packages for '{test.Name}'");
					return false;
				}
			}

			return await RunMSBuild (test);
		}
	}
}
