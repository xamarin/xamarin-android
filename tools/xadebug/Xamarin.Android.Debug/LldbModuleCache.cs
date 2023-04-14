using System;
using System.Collections.Generic;
using System.IO;

using Xamarin.Android.Utilities;

namespace Xamarin.Android.Debug;

abstract class LldbModuleCache
{
	List<string> deviceSharedLibraries;
	Dictionary<string, string?> libraryCache;

	protected AndroidDevice Device     { get; }
	protected string CacheDirPath      { get; }
	protected XamarinLoggingHelper Log { get; }

	protected LldbModuleCache (XamarinLoggingHelper log, AndroidDevice device, List<string> deviceSharedLibraries)
	{
		Device = device;
		Log = log;

		CacheDirPath = Path.Combine (
			Environment.GetFolderPath (Environment.SpecialFolder.UserProfile),
			".lldb",
			"module_cache",
			"remote-android", // "platform" used by LLDB in our case
			device.SerialNumber
		);

		this.deviceSharedLibraries = deviceSharedLibraries;
		libraryCache = new Dictionary<string, string?> (StringComparer.Ordinal);
	}

	public void Populate (string zygotePath)
	{
		string? localPath = FetchFileFromDevice (zygotePath);
		if (localPath == null) {
			// TODO: should we perhaps fetch a set of "basic" libraries here, as a fallback?
			Log.WarningLine ($"Unable to fetch Android application launcher binary ('{zygotePath}') from device. No cache of shared modules will be generated");
			return;
		}

		var alreadyDownloaded = new HashSet<string> (StringComparer.Ordinal);
		FetchDependencies (alreadyDownloaded, localPath);
	}

	protected abstract void FetchDependencies (HashSet<string> alreadyDownloaded, string localPath);

	protected string? FetchLibrary (string lib, HashSet<string> alreadyDownloaded)
	{
		Log.Debug ($"  {lib}");
		if (alreadyDownloaded.Contains (lib)) {
			Log.DebugLine (" [already downloaded]");
			return null;
		}

		string? deviceLibraryPath = GetSharedLibraryPath (lib);
		if (String.IsNullOrEmpty (deviceLibraryPath)) {
			Log.DebugLine (" [device path unknown]");
			Log.WarningLine ($"Referenced libary '{lib}' not found on device");
			return null;
		}

		Log.DebugLine (" [downloading]");
		Log.Status ("Downloading", deviceLibraryPath);
		string? localLibraryPath = FetchFileFromDevice (deviceLibraryPath);
		if (String.IsNullOrEmpty (localLibraryPath)) {
			Log.Log (LogLevel.Info, " [FAILED]", XamarinLoggingHelper.ErrorColor);
			return null;
		}
		Log.LogLine (LogLevel.Info, " [SUCCESS]", XamarinLoggingHelper.InfoColor);

		alreadyDownloaded.Add (lib);

		return localLibraryPath;
	}

	protected string? FetchFileFromDevice (string deviceFilePath)
	{
		string localFilePath = Utilities.MakeLocalPath (CacheDirPath, deviceFilePath);
		string localTempFilePath = $"{localFilePath}.tmp";

		Directory.CreateDirectory (Path.GetDirectoryName (localFilePath)!);

		if (!Device.AdbRunner.Pull (deviceFilePath, localTempFilePath).Result) {
			Log.ErrorLine ($"Failed to download {deviceFilePath} from the attached device");
			return null;
		}

		File.Move (localTempFilePath, localFilePath, true);
		return localFilePath;
	}

	string? GetSharedLibraryPath (string libraryName)
	{
		if (libraryCache.TryGetValue (libraryName, out string? libraryPath)) {
			return libraryPath;
		}

		foreach (string libPath in deviceSharedLibraries) {
			string fileName = Utilities.GetZipEntryFileName (libPath);

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
