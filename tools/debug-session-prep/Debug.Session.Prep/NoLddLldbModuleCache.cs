using System;
using System.Collections.Generic;

using Xamarin.Android.Utilities;

namespace Xamarin.Debug.Session.Prep;

class NoLddLldbModuleCache : LldbModuleCache
{
	List<string> deviceSharedLibraries;
	Dictionary<string, string?> libraryCache;

	public NoLddLldbModuleCache (XamarinLoggingHelper log, AndroidDevice device, List<string> deviceSharedLibraries)
		: base (log, device)
	{
		this.deviceSharedLibraries = deviceSharedLibraries;
		libraryCache = new Dictionary<string, string?> (StringComparer.Ordinal);
	}

	protected override string? GetSharedLibraryPath (string libraryName)
	{
		if (libraryCache.TryGetValue (libraryName, out string? libraryPath)) {
			return libraryPath;
		}

		// List is sorted on the order of directories as specified by ld.config.txt, file entries aren't
		// sorted inside.
		foreach (string libPath in deviceSharedLibraries) {
			string fileName = GetUnixFileName (libPath);

			if (String.Compare (libraryName, fileName, StringComparison.Ordinal) == 0) {
				libraryCache.Add (libraryName, libPath);
				return libPath;
			}
		}

		// Cache misses, too, the list isn't going to change
		libraryCache.Add (libraryName, null);
		return null;
	}
}
