using System.Collections.Generic;

using Xamarin.Android.Utilities;

namespace Xamarin.Android.Debug;

class LddLldbModuleCache : LldbModuleCache
{
	List<string> dependencyLibraryNames;

	public LddLldbModuleCache (XamarinLoggingHelper log, AndroidDevice device, List<string> deviceSharedLibraryPaths, List<string> dependencyLibraryNames)
		: base (log, device, deviceSharedLibraryPaths)
	{
		this.dependencyLibraryNames = dependencyLibraryNames;
	}

	protected override void FetchDependencies (HashSet<string> alreadyDownloaded, string localPath)
	{
		Log.DebugLine ($"Binary {localPath} references the following libraries:");
		foreach (string lib in dependencyLibraryNames) {
			FetchLibrary (lib, alreadyDownloaded);
		}
	}
}
