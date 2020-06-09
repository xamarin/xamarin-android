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

		[Required]
		public string UseSharedRuntime { get; set; }

		public bool EmbedAssemblies { get; set; }

		public bool BundleAssemblies { get; set; }

		public ITaskItem[] JavaSourceFiles { get; set; }

		public ITaskItem[] JavaLibraries { get; set; }

		public string[] DoNotPackageJavaLibraries { get; set; }

		public string Debug { get; set; }

		public string AndroidSequencePointsMode { get; set; }

		public string TlsProvider { get; set; }
		public string UncompressedFileExtensions { get; set; }
		public bool InterpreterEnabled { get; set; }

		// Make it required after https://github.com/xamarin/monodroid/pull/1094 is merged
		//[Required]
		public bool EnableCompression { get; set; }

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

		void ExecuteWithAbi (string [] supportedAbis, string apkInputPath, string apkOutputPath)
		{
			if (InterpreterEnabled) {
				foreach (string abi in supportedAbis) {
					if (String.Compare ("x86", abi, StringComparison.OrdinalIgnoreCase) == 0) {
						Log.LogCodedError ("XA0124", Properties.Resources.XA0124);
						return;
					}
				}
			}

			ArchiveFileList files = new ArchiveFileList ();

			using (var notice = Assembly.GetExecutingAssembly ().GetManifestResourceStream ("NOTICE.txt"))
			using (var apk = new ZipArchiveEx (apkOutputPath, File.Exists (apkOutputPath) ? FileMode.Open : FileMode.Create )) {
				bool apkInputPathExists = apkInputPath != null && File.Exists (apkInputPath);

				DateTime lastWriteOutput = DateTime.MinValue;
				DateTime lastWriteInput = DateTime.MinValue;
				if (apkInputPathExists) {
					lastWriteOutput = File.Exists (apkOutputPath) ? File.GetLastWriteTimeUtc (apkOutputPath) : DateTime.MinValue;
					lastWriteInput = File.GetLastWriteTimeUtc (apkInputPath);
				}

				for (long i = 0; i < apk.Archive.EntryCount; i++) {
					ZipEntry e = apk.Archive.ReadEntry ((ulong) i);
					Log.LogDebugMessage ($"Registering item {e.FullName}");
					existingEntries.Add (e.FullName);
				}
				string noticeName = RootPath + "NOTICE";
				existingEntries.Remove (noticeName);
				if (!apk.Archive.ContainsEntry (noticeName))
					apk.Archive.AddEntry (noticeName, notice);

				// Add classes.dx
				foreach (var dex in DalvikClasses) {
					string apkName = dex.GetMetadata ("ApkName");
					string dexPath = string.IsNullOrWhiteSpace (apkName) ? Path.GetFileName (dex.ItemSpec) : apkName;
					AddFileToArchiveIfNewer (apk, dex.ItemSpec, DalvikPath + dexPath);
				}

				if (EmbedAssemblies && !BundleAssemblies)
					AddAssemblies (apk);

				AddRuntimeLibraries (apk, supportedAbis);
				apk.Flush();
				AddNativeLibraries (files, supportedAbis);
				AddAdditionalNativeLibraries (files, supportedAbis);

				if (TypeMappings != null) {
					foreach (ITaskItem typemap in TypeMappings) {
						AddFileToArchiveIfNewer (apk, typemap.ItemSpec, RootPath + Path.GetFileName(typemap.ItemSpec), compressionMethod: UncompressedMethod);
					}
				}

				int count = 0;
				foreach (var file in files) {
					var item = Path.Combine (file.archivePath.Replace (Path.DirectorySeparatorChar, '/'));
					existingEntries.Remove (item);
					if (apk.SkipExistingFile (file.filePath, item)) {
						Log.LogDebugMessage ($"Skipping {file.filePath} as the archive file is up to date.");
						continue;
					}
					Log.LogDebugMessage ("\tAdding {0}", file.filePath);
					apk.Archive.AddFile (file.filePath, item, compressionMethod: GetCompressionMethod (file.filePath));
					count++;
					if (count == ZipArchiveEx.ZipFlushLimit) {
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
					using (var jar = ZipArchive.Open (File.OpenRead (jarFile))) {
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
					if (count == ZipArchiveEx.ZipFlushLimit) {
						apk.Flush();
						count = 0;
					}
				}

				// For now, we keep track of deleted entry indices & skip them ourselves when iterating zip contents in FixupWindowsPathSeparators.
				// For this to work, no one shoud call Flush on the zip before calling FixupWindowsPathSeparators, since Flush changes all the indices
				// (and gets rid of the deleted stuff). Later when we move to a newer LibZipSharp, with the fix to skip deleted entries itself,
				// we can remove this workaround. See FixupWindowsPathSeparators for more info.
				HashSet<ulong> deletedEntries = new HashSet<ulong> ();
				if (apkInputPathExists)
					UpdateEntriesFromInputApk (apkInputPath, apk, lastWriteOutput, lastWriteInput, deletedEntries);
				apk.FixupWindowsPathSeparators (deletedEntries, (a, b) => Log.LogDebugMessage ($"Fixing up malformed entry `{a}` -> `{b}`"));

				// Clean up Removed files. 
				foreach (var entry in existingEntries) {
					Log.LogDebugMessage ($"Removing {entry} as it is not longer required.");
					apk.Archive.DeleteEntry (entry);
				}
				apk.Flush ();
				FixupArchive (apk);
			}
		}

		private void UpdateEntriesFromInputApk (string apkInputPath, ZipArchiveEx apk, DateTime lastWriteOutput, DateTime lastWriteInput, HashSet<ulong> deletedEntries)
		{
			using (var packaged = new ZipArchiveEx (apkInputPath, FileMode.Open)) {
				foreach (var entry in packaged.Archive) {
					Log.LogDebugMessage ($"Deregistering item {entry.FullName}");
					existingEntries.Remove (entry.FullName);
					if (lastWriteInput <= lastWriteOutput)
						continue;

					long entryIndexInOutput;
					if (apk.Archive.ContainsEntry (entry.FullName, out entryIndexInOutput)) {
						ZipEntry e = apk.Archive.ReadEntry (entry.FullName);
						// check the CRC values as the ModifiedDate is always 01/01/1980 in the aapt generated file.
						if (entry.CRC == e.CRC) {
							Log.LogDebugMessage ($"Skipping {entry.FullName} from {apkInputPath} as its up to date.");
							continue;
						}
					} else {
						entryIndexInOutput = -1;
					}

					var ms = new MemoryStream ();
					entry.Extract (ms);

					if (entryIndexInOutput != -1) {
						Log.LogDebugMessage ($"Refreshing {entry.FullName} from {apkInputPath}");

						// Force the modified resource to move to the end of the file, by deleting it first so that AddStream adds it
						// back with a new index. Keeping modified resources toward the end optimizes the delta install for the typical
						// dev scenario where the user is editing a few resources but most of the APK contents (e.g. the native libs)
						// don't change and we want to keep their byte offset in the APK fixed. Delta install need not update APK
						// contents that don't change and don't move.
						apk.Archive.DeleteEntry ((ulong) entryIndexInOutput);
						deletedEntries.Add ((ulong) entryIndexInOutput);
					} else {
						Log.LogDebugMessage ($"Adding {entry.FullName} from {apkInputPath}");
					}

					apk.Archive.AddStream (ms, entry.FullName, compressionMethod: entry.CompressionMethod);
				}
			}
		}

		public override bool RunTask ()
		{
			Aot.TryGetSequencePointsMode (AndroidSequencePointsMode, out sequencePointsMode);

			var outputFiles = new List<string> ();
			uncompressedFileExtensions = UncompressedFileExtensions?.Split (new char [] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries) ?? new string [0];

			existingEntries.Clear ();
			ExecuteWithAbi (SupportedAbis, ApkInputPath, ApkOutputPath);
			outputFiles.Add (ApkOutputPath);
			if (CreatePackagePerAbi && SupportedAbis.Length > 1) {
				foreach (var abi in SupportedAbis) {
					existingEntries.Clear ();
					var path = Path.GetDirectoryName (ApkOutputPath);
					var apk = Path.GetFileNameWithoutExtension (ApkOutputPath);
					ExecuteWithAbi (new [] { abi }, String.Format ("{0}-{1}", ApkInputPath, abi), 
						Path.Combine (path, String.Format ("{0}-{1}.apk", apk, abi)));
					outputFiles.Add (Path.Combine (path, String.Format ("{0}-{1}.apk", apk, abi)));
				}
			}

			OutputFiles = outputFiles.Select (a => new TaskItem (a)).ToArray ();

			Log.LogDebugTaskItems ("  [Output] OutputFiles :", OutputFiles);

			return !Log.HasLoggedErrors;
		}

		private void AddAssemblies (ZipArchiveEx apk)
		{
			bool debug = _Debug;
			bool compress = !debug && EnableCompression;
			bool use_shared_runtime = String.Equals (UseSharedRuntime, "true", StringComparison.OrdinalIgnoreCase);
			string sourcePath;
			AssemblyCompression.AssemblyData compressedAssembly = null;
			IDictionary<string, CompressedAssemblyInfo> compressedAssembliesInfo = null;

			if (compress) {
				compressedAssembliesInfo = BuildEngine4.GetRegisteredTaskObject (CompressedAssemblyInfo.CompressedAssembliesInfoKey, RegisteredTaskObjectLifetime.Build) as IDictionary<string, CompressedAssemblyInfo>;
			}

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
				AddFileToArchiveIfNewer (apk, sourcePath, GetTargetDirectory (assembly.ItemSpec) + "/"  + Path.GetFileName (assembly.ItemSpec), compressionMethod: UncompressedMethod);

				// Try to add config if exists
				var config = Path.ChangeExtension (assembly.ItemSpec, "dll.config");
				AddAssemblyConfigEntry (apk, config);

				// Try to add symbols if Debug
				if (debug) {
					var symbols = Path.ChangeExtension (assembly.ItemSpec, "dll.mdb");

					if (File.Exists (symbols))
						AddFileToArchiveIfNewer (apk, symbols, AssembliesPath + Path.GetFileName (symbols), compressionMethod: UncompressedMethod);

					symbols = Path.ChangeExtension (assembly.ItemSpec, "pdb");

					if (File.Exists (symbols))
						AddFileToArchiveIfNewer (apk, symbols, AssembliesPath + Path.GetFileName (symbols), compressionMethod: UncompressedMethod);
				}
				count++;
				if (count == ZipArchiveEx.ZipFlushLimit) {
					apk.Flush();
					count = 0;
				}
			}

			if (use_shared_runtime)
				return;

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
				AddFileToArchiveIfNewer (apk, sourcePath, AssembliesPath + Path.GetFileName (assembly.ItemSpec), compressionMethod: UncompressedMethod);
				var config = Path.ChangeExtension (assembly.ItemSpec, "dll.config");
				AddAssemblyConfigEntry (apk, config);
				// Try to add symbols if Debug
				if (debug) {
					var symbols = Path.ChangeExtension (assembly.ItemSpec, "dll.mdb");

					if (File.Exists (symbols))
						AddFileToArchiveIfNewer (apk, symbols, AssembliesPath + Path.GetFileName (symbols), compressionMethod: UncompressedMethod);

					symbols = Path.ChangeExtension (assembly.ItemSpec, "pdb");

					if (File.Exists (symbols))
						AddFileToArchiveIfNewer (apk, symbols, AssembliesPath + Path.GetFileName (symbols), compressionMethod: UncompressedMethod);
				}
				count++;
				if (count == ZipArchiveEx.ZipFlushLimit) {
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

				if (compressedAssembliesInfo.TryGetValue (Path.GetFileName (assembly.ItemSpec), out CompressedAssemblyInfo info) && info != null) {
					EnsureCompressedAssemblyData (assembly.ItemSpec, info.DescriptorIndex);
					AssemblyCompression.CompressionResult result = AssemblyCompression.Compress (compressedAssembly);
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
				}

				return assembly.ItemSpec;
			}
		}

		bool AddFileToArchiveIfNewer (ZipArchiveEx apk, string file, string inArchivePath, CompressionMethod compressionMethod = CompressionMethod.Default)
		{
			existingEntries.Remove (inArchivePath);
			if (apk.SkipExistingFile (file, inArchivePath)) {
				Log.LogDebugMessage ($"Skipping {file} as the archive file is up to date.");
				return false;
			}
			Log.LogDebugMessage ($"Adding {file} as the archive file is out of date.");
			apk.Archive.AddFile (file, inArchivePath, compressionMethod: compressionMethod);
			return true;
		}

		void AddAssemblyConfigEntry (ZipArchiveEx apk, string configFile)
		{
			string inArchivePath = AssembliesPath + Path.GetFileName (configFile);
			existingEntries.Remove (inArchivePath);

			if (!File.Exists (configFile))
				return;

			if (apk.SkipExistingFile (configFile, inArchivePath)) {
				Log.LogDebugMessage ($"Skipping {configFile} as the archive file is up to date.");
				return;
			}

			Log.LogDebugMessage ($"Adding {configFile} as the archive file is out of date.");
			using (var source = File.OpenRead (configFile)) {
				var dest = new MemoryStream ();
				source.CopyTo (dest);
				dest.WriteByte (0);
				dest.Position = 0;
				apk.Archive.AddEntry (inArchivePath, dest, compressionMethod: UncompressedMethod);
			}
		}

		string GetTargetDirectory (string path)
		{
			string culture, file;
			if (SatelliteAssembly.TryGetSatelliteCultureAndFileName (path, out culture, out file)) {
				return AssembliesPath + culture;
			}
			return AssembliesPath.TrimEnd ('/');
		}

		class LibInfo
		{
			public string Path;
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
			if (apk.SkipExistingFile (filesystemPath, archivePath)) {
				Log.LogDebugMessage ($"Skipping {filesystemPath} (APK path: {archivePath}) as it is up to date.");
				return;
			}
			Log.LogDebugMessage ($"Adding native library: {filesystemPath} (APK path: {archivePath})");
			apk.Archive.AddEntry (archivePath, File.OpenRead (filesystemPath), compressionMethod: GetCompressionMethod (archivePath));
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

		private void AddNativeLibraries (ArchiveFileList files, string [] supportedAbis)
		{
			var frameworkLibs = FrameworkNativeLibraries.Select (v => new LibInfo {
				Path = v.ItemSpec,
				Abi = GetNativeLibraryAbi (v),
				ArchiveFileName = v.GetMetadata ("ArchiveFileName")
			});

			AddNativeLibraries (files, supportedAbis, frameworkLibs);

			var libs = NativeLibraries.Concat (BundleNativeLibraries ?? Enumerable.Empty<ITaskItem> ())
				.Select (v => new LibInfo { Path = v.ItemSpec, Abi = GetNativeLibraryAbi (v) });

			AddNativeLibraries (files, supportedAbis, libs);
		}

		string GetNativeLibraryAbi (ITaskItem lib)
		{
			// If Abi is explicitly specified, simply return it.
			var lib_abi = MonoAndroidHelper.GetNativeLibraryAbi (lib);
			
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
				.Select (l => new LibInfo { Path = l.ItemSpec, Abi = MonoAndroidHelper.GetNativeLibraryAbi (l) });

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
	}
}
