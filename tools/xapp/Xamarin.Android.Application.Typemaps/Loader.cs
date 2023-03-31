using System;
using System.Collections.Generic;
using System.IO;

using Xamarin.Android.Application.Utilities;

using Xamarin.Tools.Zip;

namespace Xamarin.Android.Application.Typemaps;

class Loader
{
	const string xamarinApp = "libxamarin-app.so";
	const string sharedLibsDirName = "app_shared_libraries";
	const string typemapIndexFileName = "typemap.index";

	static readonly string TypemapIndexRelPath = Path.Combine ("android", "typemaps", typemapIndexFileName);
	static readonly List<string> archDirs = new List<string> {
		"arm64-v8a",
		"armeabi-v7a",
		"x86",
		"x86_64",
	};

	static readonly char dirSep = Path.DirectorySeparatorChar;

	AndroidArch archFilter;
	bool loadOnlyFirst;
	ILogger log;

	public Loader (ILogger log, AndroidArch archFilter, bool loadOnlyFirst)
	{
		this.log = log;
		this.archFilter = archFilter;
		this.loadOnlyFirst = loadOnlyFirst;
	}

	public List<ITypemap> TryLoad (string path)
	{
		if (File.Exists (path)) {
			return LoadFromFile (path);
		}

		if (!Directory.Exists (path)) {
			log.InfoLine ($"Directory or file '{path}' not found.");
			return new List<ITypemap> ();
		}

		string fsPath = Path.Combine (path, typemapIndexFileName);
		if (File.Exists (fsPath)) {
			return LoadFromFile (fsPath);
		}

		fsPath = Path.Combine (path, xamarinApp);
		if (File.Exists (fsPath)) {
			return LoadFromFile (fsPath);
		}

		if (PathEndsWith (path, sharedLibsDirName)) {
			return LoadFromSharedLibsDir (path);
		}

		// typemap.index must come first, since we always create `libxamarin-app.so`, but it will be empty if
		// FastDev is used, which would cause an error
		fsPath = Path.Combine (path, TypemapIndexRelPath);
		if (File.Exists (fsPath)) {
			return LoadFromFile (fsPath);
		}

		fsPath = Path.Combine (path, sharedLibsDirName);
		if (Directory.Exists (fsPath)) {
			return LoadFromSharedLibsDir (fsPath);
		}

		if (PathEndsWith (path, "obj")) {
			return LoadFromObjDir (path);
		}

		fsPath = Path.Combine (path, "obj");
		if (Directory.Exists (fsPath)) {
			return LoadFromObjDir (fsPath);
		}

		log.WarningLine ($"Cannot determine what to load from {path}");
		return new List<ITypemap> ();
	}

	List<ITypemap> LoadFromObjDir (string objDirPath)
	{
		string fsPath;

		// The reason for two loops here is that we cannot guarantee the order in which subdirectories will be
		// reported and we must always load FastDev typemap before we attempt to load one from `libxamarin-app.so`,
		// since the latter would be empty and error out in case FastDev is used
		foreach (string subdir in Directory.EnumerateDirectories (objDirPath)) {
			fsPath = Path.Combine (subdir, TypemapIndexRelPath);
			if (File.Exists (fsPath)) {
				// We don't need to search more - typemap.index exists only in the obj/Debug directory
				return LoadFromFile (fsPath);
			}
		}

		var ret = new List<ITypemap> ();
		foreach (string subdir in Directory.EnumerateDirectories (objDirPath)) {
			fsPath = Path.Combine (subdir, sharedLibsDirName);
			if (!Directory.Exists (fsPath)) {
				continue;
			}

			ret.AddRange (LoadFromSharedLibsDir (fsPath));
		}

		return ret;
	}

	List<ITypemap> LoadFromSharedLibsDir (string dirPath)
	{
		var ret = new List<ITypemap> ();
		foreach (string archDir in archDirs) {
			string fsPath = Path.Combine (dirPath, archDir, xamarinApp);
			if (File.Exists (fsPath)) {
				ITypemap? dsoTypemap = LoadDSOFromFilesystem (fsPath);
				if (dsoTypemap != null) {
					ret.Add (dsoTypemap);
					if (loadOnlyFirst) {
						break;
					}
				}
			}
		}

		return ret;
	}

	List<ITypemap> LoadFromFile (string filePath)
	{
		if (String.Compare (Path.GetFileName (filePath), "typemap.index", StringComparison.OrdinalIgnoreCase) == 0) {
			return CreateListAndReturn (LoadFromFastDevTypemap (filePath));
		}

		string ext = Path.GetExtension (filePath);
		if (String.Compare (ext, ".typemap", StringComparison.OrdinalIgnoreCase) == 0) {
			return CreateListAndReturn (LoadFromFastDevTypemap (filePath));
		}

		if (String.Compare (ext, ".so", StringComparison.OrdinalIgnoreCase) == 0) {
			return CreateListAndReturn (LoadDSOFromFilesystem (filePath));
		}

		if (String.Compare (ext, ".apk", StringComparison.OrdinalIgnoreCase) == 0) {
			return LoadFromAPK (filePath, "assemblies/");
		}

		if (String.Compare (ext, ".aab", StringComparison.OrdinalIgnoreCase) == 0) {
			return LoadFromAPK (filePath, "base/root/assemblies/");
		}

		log.InfoLine ($"Unsupported file extension '{ext}', unable to load");
		return CreateListAndReturn (null);

		List<ITypemap> CreateListAndReturn (ITypemap? tm)
		{
			if (tm == null) {
				return new List<ITypemap> ();
			}

			return new List<ITypemap> {
				tm
			};
		}
	}

	ITypemap? LoadFromFastDevTypemap (string filePath)
	{
		ITypemap tm = new FastDevTypeMap (log);

		var fs = File.Open (filePath, FileMode.Open, FileAccess.Read);
		try {
			if (tm.CanLoad (fs, filePath)) {
				return tm;
			}
		} catch {
			DisposeStream (fs);
			throw;
		}

		DisposeStream (fs);
		return null;

		void DisposeStream (FileStream fs)
		{
			fs.Close ();
			fs.Dispose ();
		}
	}

	List<ITypemap> LoadFromAPK (string filePath, string assemblyEntryPrefix)
	{
		const string xamarinAppEntryTail = "/" + xamarinApp;

		var ret = new List<ITypemap> ();
		ZipArchive zip = ZipArchive.Open (filePath, FileMode.Open);
		var managedResolver = new ApkManagedTypeResolver (log, zip, assemblyEntryPrefix);
		foreach (ZipEntry entry in zip) {
			if (!entry.FullName.EndsWith (xamarinAppEntryTail, StringComparison.Ordinal)) {
				continue;
			}

			var stream = new MemoryStream ();
			entry.Extract (stream);

			ITypemap? tm = LoadDSO (stream, $"{filePath}!{entry.FullName}", managedResolver);
			if (tm != null) {
				ret.Add (tm);
				if (loadOnlyFirst) {
					break;
				}
			}
		}

		return ret;
	}

	ITypemap? LoadDSOFromFilesystem (string filePath)
	{
		filePath = Path.GetFullPath (filePath);
		string? fileDir = Path.GetDirectoryName (filePath);
		if (String.IsNullOrEmpty (fileDir)) {
			fileDir = "./";
		}
		fileDir = Path.GetFullPath (fileDir);

		string objConfigurationDir = Path.GetFullPath (Path.Combine (fileDir, "..", ".."));
		if (!IsObjConfigurationDir (objConfigurationDir)) {
			// We might be loading the DSO from a location outside the project tree, there will likely be no
			// managed assemblies.
			objConfigurationDir = String.Empty;
		}

		var fs = File.Open (filePath, FileMode.Open, FileAccess.Read);
		var searchPaths = new List<string> ();

		if (objConfigurationDir.Length > 0) {
			// Assemblies are kept in:
			//   obj/$CONFIGURATION/android/assets and
			//   obj/$CONFIGURATION/android/assets/shrunk
			// with the latter being the preferred location
			//
			string assetsDir = Path.Combine (objConfigurationDir, "android", "assets");
			AddIfExists (searchPaths, Path.Combine (assetsDir, "shrunk"));
			AddIfExists (searchPaths, assetsDir);
		}

		return LoadDSO (fs, filePath, new FilesystemManagedTypeResolver (log, searchPaths));
	}

	ITypemap? LoadDSO (Stream stream, string filePath, ManagedTypeResolver managedResolver)
	{
		ITypemap tm = new XamarinAppDebugDSO (log, managedResolver, filePath);
		if (tm.CanLoad (stream, filePath)) {
			return ReturnIfCorrectArch (tm);
		}

		tm = new XamarinAppReleaseDSO (log, managedResolver, filePath);
		if (tm.CanLoad (stream, filePath)) {
			return ReturnIfCorrectArch (tm);
		}

		return null;

		ITypemap? ReturnIfCorrectArch (ITypemap tm)
		{
			return IsMatchingArch (tm) ? tm : null;
		}
	}

	bool IsMatchingArch (ITypemap tm)
	{
		bool matches;

		switch (tm.MapArchitecture) {
			case MapArchitecture.ARM:
				matches = (archFilter & AndroidArch.ARM) == AndroidArch.ARM;
				break;

			case MapArchitecture.ARM64:
				matches = (archFilter & AndroidArch.ARM64) == AndroidArch.ARM64;
				break;

			case MapArchitecture.X86:
				matches = (archFilter & AndroidArch.X86) == AndroidArch.X86;
				break;

			case MapArchitecture.X86_64:
				matches = (archFilter & AndroidArch.X86_64) == AndroidArch.X86_64;
				break;

			case MapArchitecture.FastDev:
				return true;

			default:
				throw new NotSupportedException ($"Type map architecture {tm.Map.Architecture} is not supported");
		}

		return matches;
	}

	bool IsObjConfigurationDir (string path)
	{
		return Directory.Exists (Path.Combine (path, sharedLibsDirName));
	}

	void AddIfExists (List<string> searchPaths, string dirPath)
	{
		if (!Directory.Exists (dirPath))
			return;

		searchPaths.Add (Path.GetFullPath (dirPath));
	}

	bool PathEndsWith (string path, string tail)
	{
		return path.EndsWith ($"{dirSep}{tail}", StringComparison.OrdinalIgnoreCase) || path.EndsWith ($"{dirSep}{tail}{dirSep}", StringComparison.OrdinalIgnoreCase);
	}
}
