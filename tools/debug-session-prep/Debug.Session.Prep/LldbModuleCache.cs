using System;
using System.Collections.Generic;
using System.IO;

using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using Xamarin.Android.Utilities;

namespace Xamarin.Debug.Session.Prep;

abstract class LldbModuleCache
{
	protected AndroidDevice Device     { get; }
	protected string CacheDirPath      { get; }
	protected XamarinLoggingHelper Log { get; }

	protected LldbModuleCache (XamarinLoggingHelper log, AndroidDevice device)
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
		using IELF? elf = ReadElfFile (localPath);

		FetchDependencies (elf, alreadyDownloaded, localPath);
	}

	void FetchDependencies (IELF? elf, HashSet<string> alreadyDownloaded, string localPath)
	{
		if (elf == null) {
			Log.DebugLine ($"Failed to open '{localPath}' as an ELF file. Ignoring.");
			return;
		}

		var dynstr = GetSection (elf, ".dynstr") as IStringTable;
		if (dynstr == null) {
			Log.DebugLine ($"ELF binary {localPath} has no .dynstr section, unable to read referenced shared library names");
			return;
		}

		var needed = new HashSet<string> (StringComparer.Ordinal);
		foreach (IDynamicSection section in elf.GetSections<IDynamicSection> ()) {
			foreach (IDynamicEntry entry in section.Entries) {
				if (entry.Tag != DynamicTag.Needed) {
					continue;
				}

				AddNeeded (dynstr, entry);
			}
		}

		Log.DebugLine ($"Binary {localPath} references the following libraries:");
		foreach (string lib in needed) {
			Log.Debug ($"  {lib}");
			if (alreadyDownloaded.Contains (lib)) {
				Log.DebugLine (" [already downloaded]");
				continue;
			}

			string? deviceLibraryPath = GetSharedLibraryPath (lib);
			if (String.IsNullOrEmpty (deviceLibraryPath)) {
				Log.DebugLine (" [device path unknown]");
				Log.WarningLine ($"Referenced libary '{lib}' not found on device");
				continue;
			}

			Log.DebugLine (" [downloading]");
			Log.Status ("Downloading", deviceLibraryPath);
			string? localLibraryPath = FetchFileFromDevice (deviceLibraryPath);
			if (String.IsNullOrEmpty (localLibraryPath)) {
				Log.Log (LogLevel.Info, " [FAILED]", XamarinLoggingHelper.ErrorColor);
				continue;
			}
			Log.LogLine (LogLevel.Info, " [SUCCESS]", XamarinLoggingHelper.InfoColor);

			alreadyDownloaded.Add (lib);
			using IELF? libElf = ReadElfFile (localLibraryPath);
			FetchDependencies (libElf, alreadyDownloaded, localLibraryPath);
		}

		void AddNeeded (IStringTable stringTable, IDynamicEntry entry)
		{
			ulong index;
			if (entry is DynamicEntry<ulong> entry64) {
				index = entry64.Value;
			} else if (entry is DynamicEntry<uint> entry32) {
				index = (ulong)entry32.Value;
			} else {
				Log.WarningLine ($"DynamicEntry neither 32 nor 64 bit? Weird");
				return;
			}

			string name = stringTable[(long)index];
			if (needed.Contains (name)) {
				return;
			}

			needed.Add (name);
		}
	}

	string? FetchFileFromDevice (string deviceFilePath)
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

	protected string GetUnixFileName (string path)
	{
		int idx = path.LastIndexOf ('/');
		if (idx >= 0 && idx != path.Length - 1) {
			return path.Substring (idx + 1);
		}

		return path;
	}

	protected abstract string? GetSharedLibraryPath (string libraryName);

	IELF? ReadElfFile (string path)
	{
		try {
			if (ELFReader.TryLoad (path, out IELF ret)) {
				return ret;
			}
		} catch (Exception ex) {
			Log.WarningLine ($"{path} may not be a valid ELF binary.");
			Log.WarningLine (ex.ToString ());
		}

		return null;
	}

	ISection? GetSection (IELF elf, string sectionName)
	{
		if (!elf.TryGetSection (sectionName, out ISection section)) {
			return null;
		}

		return section;
	}
}
