// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;

using Java.Interop.Tools.Cecil;

using ArchiveFileList = System.Collections.Generic.List<(string filePath, string archivePath)>;
using Mono.Cecil;
using Xamarin.Android.Tools;
using Xamarin.Tools.Zip;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class BuildApk : AndroidTask
	{
		public override string TaskPrefix => "BLD";

		public string AndroidNdkDirectory { get; set; }

		[Required]
		public string ApkInputPath { get; set; }

		[Required]
		public string ApkOutputPath { get; set; }

		[Required]
		public ITaskItem[] ResolvedUserAssemblies { get; set; }

		[Required]
		public ITaskItem[] ResolvedFrameworkAssemblies { get; set; }

		public ITaskItem[] AdditionalNativeLibraryReferences { get; set; }

		public ITaskItem[] EmbeddedNativeLibraryAssemblies { get; set; }

		[Required]
		public ITaskItem[] FrameworkNativeLibraries { get; set; }

		[Required]
		public ITaskItem[] NativeLibraries { get; set; }

		[Required]
		public ITaskItem[] ApplicationSharedLibraries { get; set; }

		public ITaskItem[] BundleNativeLibraries { get; set; }

		public ITaskItem[] TypeMappings { get; set; }

		[Required]
		public ITaskItem [] DalvikClasses { get; set; }

		[Required]
		public string [] SupportedAbis { get; set; }

		public bool CreatePackagePerAbi { get; set; }

		public bool EmbedAssemblies { get; set; }

		public bool BundleAssemblies { get; set; }

		public ITaskItem[] JavaSourceFiles { get; set; }

		public ITaskItem[] JavaLibraries { get; set; }

		public string[] DoNotPackageJavaLibraries { get; set; }

		public string Debug { get; set; }

		public string AndroidSequencePointsMode { get; set; }

		public string TlsProvider { get; set; }
		public string UncompressedFileExtensions { get; set; }

		// Make it required after https://github.com/xamarin/monodroid/pull/1094 is merged
		//[Required]
		public bool EnableCompression { get; set; }

		public bool IncludeWrapSh { get; set; }

		public string CheckedBuild { get; set; }

		public string RuntimeConfigBinFilePath { get; set; }

		[Required]
		public string ProjectFullPath { get; set; }

		[Output]
		public ITaskItem[] OutputFiles { get; set; }

		bool _Debug {
			get {
				return string.Equals (Debug, "true", StringComparison.OrdinalIgnoreCase);
			}
		}

		SequencePointsMode sequencePointsMode = SequencePointsMode.None;

		public ITaskItem[] LibraryProjectJars { get; set; }
		string [] uncompressedFileExtensions;

		protected virtual string RootPath => "";

		protected virtual string AssembliesPath => RootPath + "assemblies/";

		protected virtual string DalvikPath => "";

		protected virtual CompressionMethod UncompressedMethod => CompressionMethod.Store;

		protected virtual void FixupArchive (ZipArchiveEx zip) { }

		List<string> existingEntries = new List<string> ();

		void ExecuteWithAbi (string [] supportedAbis, string apkInputPath, string apkOutputPath, bool debug, bool compress, IDictionary<string, CompressedAssemblyInfo> compressedAssembliesInfo)
		{
			ArchiveFileList files = new ArchiveFileList ();
			bool refresh = true;
			if (apkInputPath != null && File.Exists (apkInputPath) && !File.Exists (apkOutputPath)) {
				Log.LogDebugMessage ($"Copying {apkInputPath} to {apkInputPath}");
				File.Copy (apkInputPath, apkOutputPath, overwrite: true);
				refresh = false;
			}
			using (var apk = new ZipArchiveEx (apkOutputPath, File.Exists (apkOutputPath) ? FileMode.Open : FileMode.Create )) {
				if (refresh) {
					for (long i = 0; i < apk.Archive.EntryCount; i++) {
						ZipEntry e = apk.Archive.ReadEntry ((ulong) i);
						Log.LogDebugMessage ($"Registering item {e.FullName}");
						existingEntries.Add (e.FullName);
					}
				}
				if (apkInputPath != null && File.Exists (apkInputPath) && refresh) {
					var lastWriteOutput = File.Exists (apkOutputPath) ? File.GetLastWriteTimeUtc (apkOutputPath) : DateTime.MinValue;
					var lastWriteInput = File.GetLastWriteTimeUtc (apkInputPath);
					using (var packaged = new ZipArchiveEx (apkInputPath, FileMode.Open)) {
						foreach (var entry in packaged.Archive) {
							// NOTE: aapt2 is creating zip entries on Windows such as `assets\subfolder/asset2.txt`
							var entryName = entry.FullName;
							if (entryName.Contains ("\\")) {
								entryName = entryName.Replace ('\\', '/');
								Log.LogDebugMessage ($"Fixing up malformed entry `{entry.FullName}` -> `{entryName}`");
							}
							Log.LogDebugMessage ($"Deregistering item {entryName}");
							existingEntries.Remove (entryName);
							if (lastWriteInput <= lastWriteOutput) {
								Log.LogDebugMessage ($"Skipping to next item. {lastWriteInput} <= {lastWriteOutput}.");
								continue;
							}
							if (apk.Archive.ContainsEntry (entryName)) {
								ZipEntry e = apk.Archive.ReadEntry (entryName);
								// check the CRC values as the ModifiedDate is always 01/01/1980 in the aapt generated file.
								if (entry.CRC == e.CRC && entry.CompressedSize == e.CompressedSize) {
									Log.LogDebugMessage ($"Skipping {entryName} from {apkInputPath} as its up to date.");
									continue;
								}
							}
							var ms = new MemoryStream ();
							entry.Extract (ms);
							Log.LogDebugMessage ($"Refreshing {entryName} from {apkInputPath}");
							apk.Archive.AddStream (ms, entryName, compressionMethod: entry.CompressionMethod);
						}
					}
				}
				apk.FixupWindowsPathSeparators ((a, b) => Log.LogDebugMessage ($"Fixing up malformed entry `{a}` -> `{b}`"));

				// Add classes.dx
				CompressionMethod dexCompressionMethod = GetCompressionMethod (".dex");
				foreach (var dex in DalvikClasses) {
					string apkName = dex.GetMetadata ("ApkName");
					string dexPath = string.IsNullOrWhiteSpace (apkName) ? Path.GetFileName (dex.ItemSpec) : apkName;
					AddFileToArchiveIfNewer (apk, dex.ItemSpec, DalvikPath + dexPath, compressionMethod: dexCompressionMethod);
				}

				if (EmbedAssemblies && !BundleAssemblies)
					AddAssemblies (apk, debug, compress, compressedAssembliesInfo);

				AddRuntimeLibraries (apk, supportedAbis);
				apk.Flush();
				AddNativeLibraries (files, supportedAbis);
				AddAdditionalNativeLibraries (files, supportedAbis);

				if (TypeMappings != null) {
					foreach (ITaskItem typemap in TypeMappings) {
						AddFileToArchiveIfNewer (apk, typemap.ItemSpec, RootPath + Path.GetFileName(typemap.ItemSpec), compressionMethod: UncompressedMethod);
					}
				}

				if (!String.IsNullOrEmpty (RuntimeConfigBinFilePath) && File.Exists (RuntimeConfigBinFilePath)) {
					AddFileToArchiveIfNewer (apk, RuntimeConfigBinFilePath, $"{AssembliesPath}rc.bin", compressionMethod: UncompressedMethod);
				}

				int count = 0;
				foreach (var file in files) {
					var item = Path.Combine (file.archivePath.Replace (Path.DirectorySeparatorChar, '/'));
					existingEntries.Remove (item);
					CompressionMethod compressionMethod = GetCompressionMethod (file.filePath);
					if (apk.SkipExistingFile (file.filePath, item, compressionMethod)) {
						Log.LogDebugMessage ($"Skipping {file.filePath} as the archive file is up to date.");
						continue;
					}
					Log.LogDebugMessage ("\tAdding {0}", file.filePath);
					apk.Archive.AddFile (file.filePath, item, compressionMethod: compressionMethod);
					count++;
					if (count >= ZipArchiveEx.ZipFlushFilesLimit) {
						apk.Flush();
						count = 0;
					}
				}

				var jarFiles = (JavaSourceFiles != null) ? JavaSourceFiles.Where (f => f.ItemSpec.EndsWith (".jar")) : null;
				if (jarFiles != null && JavaLibraries != null)
					jarFiles = jarFiles.Concat (JavaLibraries);
				else if (JavaLibraries != null)
					jarFiles = JavaLibraries;

				var libraryProjectJars  = MonoAndroidHelper.ExpandFiles (LibraryProjectJars)
					.Where (jar => !MonoAndroidHelper.IsEmbeddedReferenceJar (jar));

				var jarFilePaths = libraryProjectJars.Concat (jarFiles != null ? jarFiles.Select (j => j.ItemSpec) : Enumerable.Empty<string> ());
				jarFilePaths = MonoAndroidHelper.DistinctFilesByContent (jarFilePaths);

				count = 0;
				foreach (var jarFile in jarFilePaths) {
					using (var stream = File.OpenRead (jarFile))
					using (var jar = ZipArchive.Open (stream)) {
						foreach (var jarItem in jar) {
							if (jarItem.IsDirectory)
								continue;
							var name = jarItem.FullName;
							if (!PackagingUtils.CheckEntryForPackaging (name)) {
								continue;
							}
							var path = RootPath + name;
							existingEntries.Remove (path);
							if (apk.SkipExistingEntry (jarItem, path)) {
								Log.LogDebugMessage ($"Skipping {path} as the archive file is up to date.");
								continue;
							}
							if (apk.Archive.Any (e => e.FullName == path)) {
								Log.LogDebugMessage ("Failed to add jar entry {0} from {1}: the same file already exists in the apk", name, Path.GetFileName (jarFile));
								continue;
							}
							byte [] data;
							using (var d = new MemoryStream ()) {
								jarItem.Extract (d);
								data = d.ToArray ();
							}
							Log.LogDebugMessage ($"Adding {path} as the archive file is out of date.");
							apk.Archive.AddEntry (data, path);
						}
					}
					count++;
					if (count >= ZipArchiveEx.ZipFlushFilesLimit) {
						apk.Flush();
						count = 0;
					}
				}
				// Clean up Removed files.
				foreach (var entry in existingEntries) {
					// never remove an AndroidManifest. It may be renamed when using aab.
					if (string.Compare (Path.GetFileName (entry), "AndroidManifest.xml", StringComparison.OrdinalIgnoreCase) == 0)
						continue;
					Log.LogDebugMessage ($"Removing {entry} as it is not longer required.");
					apk.Archive.DeleteEntry (entry);
				}
				apk.Flush ();
				FixupArchive (apk);
			}
		}

		public override bool RunTask ()
		{
			Aot.TryGetSequencePointsMode (AndroidSequencePointsMode, out sequencePointsMode);

			var outputFiles = new List<string> ();
			uncompressedFileExtensions = UncompressedFileExtensions?.Split (new char [] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries) ?? new string [0];

			existingEntries.Clear ();

			bool debug = _Debug;
			bool compress = !debug && EnableCompression;
			IDictionary<string, CompressedAssemblyInfo> compressedAssembliesInfo = null;

			if (compress) {
				string key = CompressedAssemblyInfo.GetKey (ProjectFullPath);
				Log.LogDebugMessage ($"Retrieving assembly compression info with key '{key}'");
				compressedAssembliesInfo = BuildEngine4.UnregisterTaskObjectAssemblyLocal<IDictionary<string, CompressedAssemblyInfo>> (key, RegisteredTaskObjectLifetime.Build);
				if (compressedAssembliesInfo == null)
					throw new InvalidOperationException ($"Assembly compression info not found for key '{key}'. Compression will not be performed.");
			}

			ExecuteWithAbi (SupportedAbis, ApkInputPath, ApkOutputPath, debug, compress, compressedAssembliesInfo);
			outputFiles.Add (ApkOutputPath);
			if (CreatePackagePerAbi && SupportedAbis.Length > 1) {
				foreach (var abi in SupportedAbis) {
					existingEntries.Clear ();
					var path = Path.GetDirectoryName (ApkOutputPath);
					var apk = Path.GetFileNameWithoutExtension (ApkOutputPath);
					ExecuteWithAbi (new [] { abi }, String.Format ("{0}-{1}", ApkInputPath, abi),
						Path.Combine (path, String.Format ("{0}-{1}.apk", apk, abi)),
						debug, compress, compressedAssembliesInfo);
					outputFiles.Add (Path.Combine (path, String.Format ("{0}-{1}.apk", apk, abi)));
				}
			}

			OutputFiles = outputFiles.Select (a => new TaskItem (a)).ToArray ();

			Log.LogDebugTaskItems ("  [Output] OutputFiles :", OutputFiles);

			return !Log.HasLoggedErrors;
		}

		void AddAssemblies (ZipArchiveEx apk, bool debug, bool compress, IDictionary<string, CompressedAssemblyInfo> compressedAssembliesInfo)
		{
			string sourcePath;
			AssemblyCompression.AssemblyData compressedAssembly = null;
			string compressedOutputDir = Path.GetFullPath (Path.Combine (Path.GetDirectoryName (ApkOutputPath), "..", "lz4"));

			int count = 0;
			foreach (ITaskItem assembly in ResolvedUserAssemblies) {
				if (bool.TryParse (assembly.GetMetadata ("AndroidSkipAddToPackage"), out bool value) && value) {
					Log.LogDebugMessage ($"Skipping {assembly.ItemSpec} due to 'AndroidSkipAddToPackage' == 'true' ");
					continue;
				}
				if (MonoAndroidHelper.IsReferenceAssembly (assembly.ItemSpec)) {
					Log.LogCodedWarning ("XA0107", assembly.ItemSpec, 0, Properties.Resources.XA0107, assembly.ItemSpec);
				}

				sourcePath = CompressAssembly (assembly);

				// Add assembly
				var assemblyPath = GetAssemblyPath (assembly, frameworkAssembly: false);
				AddFileToArchiveIfNewer (apk, sourcePath, assemblyPath + Path.GetFileName (assembly.ItemSpec), compressionMethod: UncompressedMethod);

				// Try to add config if exists
				var config = Path.ChangeExtension (assembly.ItemSpec, "dll.config");
				AddAssemblyConfigEntry (apk, assemblyPath, config);

				// Try to add symbols if Debug
				if (debug) {
					var symbols = Path.ChangeExtension (assembly.ItemSpec, "dll.mdb");

					if (File.Exists (symbols))
						AddFileToArchiveIfNewer (apk, symbols, assemblyPath + Path.GetFileName (symbols), compressionMethod: UncompressedMethod);

					symbols = Path.ChangeExtension (assembly.ItemSpec, "pdb");

					if (File.Exists (symbols))
						AddFileToArchiveIfNewer (apk, symbols, assemblyPath + Path.GetFileName (symbols), compressionMethod: UncompressedMethod);
				}
				count++;
				if (count >= ZipArchiveEx.ZipFlushFilesLimit) {
					apk.Flush();
					count = 0;
				}
			}

			count = 0;
			// Add framework assemblies
			foreach (ITaskItem assembly in ResolvedFrameworkAssemblies) {
				if (bool.TryParse (assembly.GetMetadata ("AndroidSkipAddToPackage"), out bool value) && value) {
					Log.LogDebugMessage ($"Skipping {assembly.ItemSpec} due to 'AndroidSkipAddToPackage' == 'true' ");
					continue;
				}

				if (MonoAndroidHelper.IsReferenceAssembly (assembly.ItemSpec)) {
					Log.LogCodedWarning ("XA0107", assembly.ItemSpec, 0, Properties.Resources.XA0107, assembly.ItemSpec);
				}

				sourcePath = CompressAssembly (assembly);
				var assemblyPath = GetAssemblyPath (assembly, frameworkAssembly: true);
				AddFileToArchiveIfNewer (apk, sourcePath, assemblyPath + Path.GetFileName (assembly.ItemSpec), compressionMethod: UncompressedMethod);
				var config = Path.ChangeExtension (assembly.ItemSpec, "dll.config");
				AddAssemblyConfigEntry (apk, assemblyPath, config);
				// Try to add symbols if Debug
				if (debug) {
					var symbols = Path.ChangeExtension (assembly.ItemSpec, "dll.mdb");

					if (File.Exists (symbols))
						AddFileToArchiveIfNewer (apk, symbols, assemblyPath + Path.GetFileName (symbols), compressionMethod: UncompressedMethod);

					symbols = Path.ChangeExtension (assembly.ItemSpec, "pdb");

					if (File.Exists (symbols))
						AddFileToArchiveIfNewer (apk, symbols, assemblyPath + Path.GetFileName (symbols), compressionMethod: UncompressedMethod);
				}
				count++;
				if (count >= ZipArchiveEx.ZipFlushFilesLimit) {
					apk.Flush();
					count = 0;
				}
			}

			void EnsureCompressedAssemblyData (string sourcePath, uint descriptorIndex)
			{
				if (compressedAssembly == null)
					compressedAssembly = new AssemblyCompression.AssemblyData (sourcePath, descriptorIndex);
				else
					compressedAssembly.SetData (sourcePath, descriptorIndex);
			}

			string CompressAssembly (ITaskItem assembly)
			{
				if (!compress) {
					return assembly.ItemSpec;
				}

				if (bool.TryParse (assembly.GetMetadata ("AndroidSkipCompression"), out bool value) && value) {
					Log.LogDebugMessage ($"Skipping compression of {assembly.ItemSpec} due to 'AndroidSkipCompression' == 'true' ");
					return assembly.ItemSpec;
				}

				var key = CompressedAssemblyInfo.GetDictionaryKey (assembly);
				if (compressedAssembliesInfo.TryGetValue (key, out CompressedAssemblyInfo info) && info != null) {
					EnsureCompressedAssemblyData (assembly.ItemSpec, info.DescriptorIndex);
					string assemblyOutputDir;
					string subDirectory = assembly.GetMetadata ("DestinationSubDirectory");
					if (!String.IsNullOrEmpty (subDirectory))
						assemblyOutputDir = Path.Combine (compressedOutputDir, subDirectory);
					else
						assemblyOutputDir = compressedOutputDir;
					AssemblyCompression.CompressionResult result = AssemblyCompression.Compress (compressedAssembly, assemblyOutputDir);
					if (result != AssemblyCompression.CompressionResult.Success) {
						switch (result) {
							case AssemblyCompression.CompressionResult.EncodingFailed:
								Log.LogMessage ($"Failed to compress {assembly.ItemSpec}");
								break;

							case AssemblyCompression.CompressionResult.InputTooBig:
								Log.LogMessage ($"Input assembly {assembly.ItemSpec} exceeds maximum input size");
								break;

							default:
								Log.LogMessage ($"Unknown error compressing {assembly.ItemSpec}");
								break;
						}
						return assembly.ItemSpec;
					}
					return compressedAssembly.DestinationPath;
				} else {
					Log.LogDebugMessage ($"Assembly missing from {nameof (CompressedAssemblyInfo)}: {key}");
				}

				return assembly.ItemSpec;
			}
		}

		bool AddFileToArchiveIfNewer (ZipArchiveEx apk, string file, string inArchivePath, CompressionMethod compressionMethod = CompressionMethod.Default)
		{
			existingEntries.Remove (inArchivePath);
			if (apk.SkipExistingFile (file, inArchivePath, compressionMethod)) {
				Log.LogDebugMessage ($"Skipping {file} as the archive file is up to date.");
				return false;
			}
			Log.LogDebugMessage ($"Adding {file} as the archive file is out of date.");
			apk.Archive.AddFile (file, inArchivePath, compressionMethod: compressionMethod);
			return true;
		}

		void AddAssemblyConfigEntry (ZipArchiveEx apk, string assemblyPath, string configFile)
		{
			string inArchivePath = assemblyPath + Path.GetFileName (configFile);
			existingEntries.Remove (inArchivePath);

			if (!File.Exists (configFile))
				return;

			CompressionMethod compressionMethod = UncompressedMethod;
			if (apk.SkipExistingFile (configFile, inArchivePath, compressionMethod)) {
				Log.LogDebugMessage ($"Skipping {configFile} as the archive file is up to date.");
				return;
			}

			Log.LogDebugMessage ($"Adding {configFile} as the archive file is out of date.");
			using (var source = File.OpenRead (configFile)) {
				var dest = new MemoryStream ();
				source.CopyTo (dest);
				dest.WriteByte (0);
				dest.Position = 0;
				apk.Archive.AddEntry (inArchivePath, dest, compressionMethod);
			}
		}

		/// <summary>
		/// Returns the in-archive path for an assembly
		/// </summary>
		string GetAssemblyPath (ITaskItem assembly, bool frameworkAssembly)
		{
			var assembliesPath = AssembliesPath;
			var subDirectory = assembly.GetMetadata ("DestinationSubDirectory");
			if (!string.IsNullOrEmpty (subDirectory)) {
				assembliesPath += subDirectory.Replace ('\\', '/');
				if (!assembliesPath.EndsWith ("/", StringComparison.Ordinal)) {
					assembliesPath += "/";
				}
			} else if (!frameworkAssembly && SatelliteAssembly.TryGetSatelliteCultureAndFileName (assembly.ItemSpec, out var culture, out _)) {
				assembliesPath += culture + "/";
			}
			return assembliesPath;
		}

		sealed class LibInfo
		{
			public string Path;
			public string Link;
			public string Abi;
			public string ArchiveFileName;
		}

		CompressionMethod GetCompressionMethod (string fileName)
		{
			if (uncompressedFileExtensions.Any (x => string.Compare (x.StartsWith (".", StringComparison.OrdinalIgnoreCase) ? x : $".{x}", Path.GetExtension (fileName), StringComparison.OrdinalIgnoreCase) == 0))
				return UncompressedMethod;
			return CompressionMethod.Default;
		}

		void AddNativeLibraryToArchive (ZipArchiveEx apk, string abi, string filesystemPath, string inArchiveFileName)
		{
			string archivePath = $"lib/{abi}/{inArchiveFileName}";
			existingEntries.Remove (archivePath);
			CompressionMethod compressionMethod = GetCompressionMethod (archivePath);
			if (apk.SkipExistingFile (filesystemPath, archivePath, compressionMethod)) {
				Log.LogDebugMessage ($"Skipping {filesystemPath} (APK path: {archivePath}) as it is up to date.");
				return;
			}
			Log.LogDebugMessage ($"Adding native library: {filesystemPath} (APK path: {archivePath})");
			apk.Archive.AddEntry (archivePath, File.OpenRead (filesystemPath), compressionMethod);
		}

		void AddRuntimeLibraries (ZipArchiveEx apk, string [] supportedAbis)
		{
			foreach (var abi in supportedAbis) {
				foreach (ITaskItem item in ApplicationSharedLibraries) {
					if (String.Compare (abi, item.GetMetadata ("abi"), StringComparison.Ordinal) != 0)
						continue;
					AddNativeLibraryToArchive (apk, abi, item.ItemSpec, Path.GetFileName (item.ItemSpec));
				}
			}
		}

		bool IsWrapperScript (string path, string link)
		{
			if (Path.DirectorySeparatorChar == '/') {
				path = path.Replace ('\\', '/');
			}

			if (String.Compare (Path.GetFileName (path), "wrap.sh", StringComparison.Ordinal) == 0) {
				return true;
			}

			if (String.IsNullOrEmpty (link)) {
				return false;
			}

			if (Path.DirectorySeparatorChar == '/') {
				link = link.Replace ('\\', '/');
			}

			return String.Compare (Path.GetFileName (link), "wrap.sh", StringComparison.Ordinal) == 0;
		}

		bool IncludeNativeLibrary (ITaskItem item)
		{
			if (IncludeWrapSh)
				return true;

			return !IsWrapperScript (item.ItemSpec, item.GetMetadata ("Link"));
		}

		string GetArchiveFileName (ITaskItem item)
		{
			string archiveFileName = item.GetMetadata ("ArchiveFileName");
			if (!String.IsNullOrEmpty (archiveFileName))
				return archiveFileName;

			if (!IsWrapperScript (item.ItemSpec, item.GetMetadata ("Link"))) {
				return null;
			}

			return "wrap.sh";
		}

		private void AddNativeLibraries (ArchiveFileList files, string [] supportedAbis)
		{
			var frameworkLibs = FrameworkNativeLibraries.Select (v => new LibInfo {
				Path = v.ItemSpec,
				Link = v.GetMetadata ("Link"),
				Abi = GetNativeLibraryAbi (v),
				ArchiveFileName = GetArchiveFileName (v)
			});

			AddNativeLibraries (files, supportedAbis, frameworkLibs);

			var libs = NativeLibraries.Concat (BundleNativeLibraries ?? Enumerable.Empty<ITaskItem> ())
				.Where (v => IncludeNativeLibrary (v))
				.Select (v => new LibInfo {
						Path = v.ItemSpec,
						Link = v.GetMetadata ("Link"),
						Abi = GetNativeLibraryAbi (v),
						ArchiveFileName = GetArchiveFileName (v)
					}
				);

			AddNativeLibraries (files, supportedAbis, libs);

			if (String.IsNullOrWhiteSpace (CheckedBuild))
				return;

			string mode = CheckedBuild;
			string sanitizerName;
			if (String.Compare ("asan", mode, StringComparison.Ordinal) == 0) {
				sanitizerName = "asan";
			} else if (String.Compare ("ubsan", mode, StringComparison.Ordinal) == 0) {
				sanitizerName = "ubsan_standalone";
			} else {
				LogSanitizerWarning ($"Unknown checked build mode '{CheckedBuild}'");
				return;
			}

			if (!IncludeWrapSh) {
				LogSanitizerError ("Checked builds require the wrapper script to be packaged. Please set the `$(AndroidIncludeWrapSh)` MSBuild property to `true` in your project.");
				return;
			}

			if (!libs.Any (info => IsWrapperScript (info.Path, info.Link))) {
				LogSanitizerError ($"Checked builds require the wrapper script to be packaged. Please add `wrap.sh` appropriate for the {CheckedBuild} checker to your project.");
				return;
			}

			NdkTools? ndk = NdkTools.Create (AndroidNdkDirectory, Log);
			if (ndk == null) {
				return; // NdkTools.Create will log appropriate error
			}

			string clangDir = ndk.GetClangDeviceLibraryPath ();
			if (String.IsNullOrEmpty (clangDir)) {
				LogSanitizerError ($"Unable to find the clang compiler directory. Is NDK installed?");
				return;
			}

			foreach (string abi in supportedAbis) {
				string clangAbi = MonoAndroidHelper.MapAndroidAbiToClang (abi);
				if (String.IsNullOrEmpty (clangAbi)) {
					LogSanitizerError ($"Unable to map Android ABI {abi} to clang ABI");
					return;
				}

				string sanitizerLib = $"libclang_rt.{sanitizerName}-{clangAbi}-android.so";
				string sanitizerLibPath = Path.Combine (clangDir, sanitizerLib);
				if (!File.Exists (sanitizerLibPath)) {
					LogSanitizerError ($"Unable to find sanitizer runtime for the {CheckedBuild} checker at {sanitizerLibPath}");
					return;
				}

				AddNativeLibrary (files, sanitizerLibPath, abi, sanitizerLib);
			}
		}

		string GetNativeLibraryAbi (ITaskItem lib)
		{
			// If Abi is explicitly specified, simply return it.
			var lib_abi = AndroidRidAbiHelper.GetNativeLibraryAbi (lib);

			if (string.IsNullOrWhiteSpace (lib_abi)) {
				Log.LogCodedError ("XA4301", lib.ItemSpec, 0, Properties.Resources.XA4301_ABI, lib.ItemSpec);
				return null;
			}

			return lib_abi;
		}

		void AddNativeLibraries (ArchiveFileList files, string [] supportedAbis, System.Collections.Generic.IEnumerable<LibInfo> libs)
		{
			if (libs.Any (lib => lib.Abi == null))
				Log.LogCodedWarning (
						"XA4301",
						Properties.Resources.XA4301_ABI_Ignoring,
						string.Join (", ", libs.Where (lib => lib.Abi == null).Select (lib => lib.Path)));
			libs = libs.Where (lib => lib.Abi != null);
			libs = libs.Where (lib => supportedAbis.Contains (lib.Abi));
			foreach (var info in libs)
				AddNativeLibrary (files, info.Path, info.Abi, info.ArchiveFileName);
		}

		private void AddAdditionalNativeLibraries (ArchiveFileList files, string [] supportedAbis)
		{
			if (AdditionalNativeLibraryReferences == null || !AdditionalNativeLibraryReferences.Any ())
				return;

			var libs = AdditionalNativeLibraryReferences
				.Select (l => new LibInfo {
					Path = l.ItemSpec,
					Abi = AndroidRidAbiHelper.GetNativeLibraryAbi (l),
					ArchiveFileName = l.GetMetadata ("ArchiveFileName"),
				});

			AddNativeLibraries (files, supportedAbis, libs);
		}

		void AddNativeLibrary (ArchiveFileList files, string path, string abi, string archiveFileName)
		{
			string fileName = string.IsNullOrEmpty (archiveFileName) ? Path.GetFileName (path) : archiveFileName;
			var item = (filePath: path, archivePath: $"lib/{abi}/{fileName}");
			if (files.Any (x => x.archivePath == item.archivePath)) {
				Log.LogCodedWarning ("XA4301", path, 0, Properties.Resources.XA4301, item.archivePath);
				return;
			}
			files.Add (item);
		}

		// This method is used only for internal warnings which will never be shown to the end user, therefore there's
		// no need to use coded warnings.
		void LogSanitizerWarning (string message)
		{
			Log.LogWarning (message);
		}

		void LogSanitizerError (string message)
		{
			Log.LogError (message);
		}
	}
}
