using System;
using System.Collections.Generic;
using System.IO;

using Xamarin.Tools.Zip;

namespace Xamarin.Android.AssemblyBlobReader
{
	class BlobExplorer
	{
		BlobReader? indexBlob;
		BlobManifestReader? manifest;
		int numberOfBlobs = 0;
		Action<BlobExplorerLogLevel, string>? logger;
		bool keepBlobInMemory;

		public IDictionary<string, BlobAssembly> AssembliesByName  { get; } = new SortedDictionary<string, BlobAssembly> (StringComparer.OrdinalIgnoreCase);
		public IDictionary<uint, BlobAssembly> AssembliesByHash32  { get; } = new Dictionary<uint, BlobAssembly> ();
		public IDictionary<ulong, BlobAssembly> AssembliesByHash64 { get; } = new Dictionary<ulong, BlobAssembly> ();
		public List<BlobAssembly> Assemblies                       { get; } = new List<BlobAssembly> ();
		public IDictionary<uint, List<BlobReader>> Blobs           { get; } = new SortedDictionary<uint, List<BlobReader>> ();
		public string BlobPath                                     { get; }
		public string BlobSetName                                  { get; }
		public bool HasErrors                                      { get; private set; }
		public bool HasWarnings                                    { get; private set; }

		public bool IsCompleteSet                          => indexBlob != null && manifest != null;
		public int NumberOfBlobs                           => numberOfBlobs;

		// blobPath can point to:
		//
		//   aab
		//   apk
		//   index blob (e.g. base_assemblies.blob)
		//   arch blob (e.g. base_assemblies.arm64_v8a.blob)
		//   blob manifest (e.g. base_assemblies.manifest)
		//   blob base name (e.g. base or base_assemblies)
		//
		// In each case the whole set of blobs and manifests will be read (if available). Search for the various members of the blob set (common/main blob, arch blobs,
		// manifest) is based on this naming convention:
		//
		//   {BASE_NAME}[.ARCH_NAME].{blob|manifest}
		//
		// Whichever file is referenced in `blobPath`, the BASE_NAME component is extracted and all the found files are read.
		// If `blobPath` points to an aab or an apk, BASE_NAME will always be `assemblies`
		//
		public BlobExplorer (string blobPath, Action<BlobExplorerLogLevel, string>? customLogger = null, bool keepBlobInMemory = false)
		{
			if (String.IsNullOrEmpty (blobPath)) {
				throw new ArgumentException ("must not be null or empty", nameof (blobPath));
			}

			if (Directory.Exists (blobPath)) {
				throw new ArgumentException ($"'{blobPath}' points to a directory", nameof (blobPath));
			}

			logger = customLogger;
			this.keepBlobInMemory = keepBlobInMemory;
			BlobPath = blobPath;
			string? extension = Path.GetExtension (blobPath);
			string? baseName = null;

			if (String.IsNullOrEmpty (extension)) {
				baseName = GetBaseNameNoExtension (blobPath);
			} else {
				baseName = GetBaseNameHaveExtension (blobPath, extension);
			}

			if (String.IsNullOrEmpty (baseName)) {
				throw new InvalidOperationException ($"Unable to determine base name of a blob set from path '{blobPath}'");
			}

			BlobSetName = baseName;
			if (!IsAndroidArchive (extension)) {
				string? directoryName = Path.GetDirectoryName (blobPath);
				if (String.IsNullOrEmpty (directoryName)) {
					directoryName = ".";
				}

				ReadBlobSetFromFilesystem (baseName, directoryName);
			} else {
				ReadBlobSetFromArchive (baseName, blobPath, extension);
			}

			ProcessBlobs ();
		}

		void Logger (BlobExplorerLogLevel level, string message)
		{
			if (level == BlobExplorerLogLevel.Error) {
				HasErrors = true;
			} else if (level == BlobExplorerLogLevel.Warning) {
				HasWarnings = true;
			}

			if (logger != null) {
				logger (level, message);
			} else {
				DefaultLogger (level, message);
			}
		}

		void DefaultLogger (BlobExplorerLogLevel level, string message)
		{
			Console.WriteLine ($"{level}: {message}");
		}

		void ProcessBlobs ()
		{
			if (Blobs.Count == 0 || indexBlob == null) {
				return;
			}

			ProcessIndex (indexBlob.GlobalIndex32, "32", (BlobHashEntry he, BlobAssembly assembly) => {
				assembly.Hash32 = (uint)he.Hash;
				assembly.RuntimeIndex = he.MappingIndex;

				if (manifest != null && manifest.EntriesByHash32.TryGetValue (assembly.Hash32, out BlobManifestEntry? me) && me != null) {
					assembly.Name = me.Name;
				}

				if (!AssembliesByHash32.ContainsKey (assembly.Hash32)) {
					AssembliesByHash32.Add (assembly.Hash32, assembly);
				}
			});

			ProcessIndex (indexBlob.GlobalIndex64, "64", (BlobHashEntry he, BlobAssembly assembly) => {
				assembly.Hash64 = he.Hash;
				if (assembly.RuntimeIndex != he.MappingIndex) {
					Logger (BlobExplorerLogLevel.Warning, $"assembly with hashes 0x{assembly.Hash32} and 0x{assembly.Hash64} has a different 32-bit runtime index ({assembly.RuntimeIndex}) than the 64-bit runtime index({he.MappingIndex})");
				}

				if (manifest != null && manifest.EntriesByHash64.TryGetValue (assembly.Hash64, out BlobManifestEntry? me) && me != null) {
					if (String.IsNullOrEmpty (assembly.Name)) {
						Logger (BlobExplorerLogLevel.Warning, $"32-bit hash 0x{assembly.Hash32:x} did not match any assembly name in the manifest");
						assembly.Name = me.Name;
						if (String.IsNullOrEmpty (assembly.Name)) {
							Logger (BlobExplorerLogLevel.Warning, $"64-bit hash 0x{assembly.Hash64:x} did not match any assembly name in the manifest");
						}
					} else if (String.Compare (assembly.Name, me.Name, StringComparison.Ordinal) != 0) {
						Logger (BlobExplorerLogLevel.Warning, $"32-bit hash 0x{assembly.Hash32:x} maps to assembly name '{assembly.Name}', however 64-bit hash 0x{assembly.Hash64:x} for the same entry matches assembly name '{me.Name}'");
					}
				}

				if (!AssembliesByHash64.ContainsKey (assembly.Hash64)) {
					AssembliesByHash64.Add (assembly.Hash64, assembly);
				}
			});

			foreach (var kvp in Blobs) {
				List<BlobReader> list = kvp.Value;
				if (list.Count < 2) {
					continue;
				}

				BlobReader template = list[0];
				for (int i = 1; i < list.Count; i++) {
					BlobReader other = list[i];
					if (!template.HasIdenticalContent (other)) {
						Logger (BlobExplorerLogLevel.Error, $"Blob ID {template.BlobID} for architecture {other.Arch} is not identical to other blobs with the same ID");
					}
				}
			}

			void ProcessIndex (List<BlobHashEntry> index, string bitness, Action<BlobHashEntry, BlobAssembly> assemblyHandler)
			{
				foreach (BlobHashEntry he in index) {
					if (!Blobs.TryGetValue (he.BlobID, out List<BlobReader>? blobList) || blobList == null) {
						Logger (BlobExplorerLogLevel.Warning, $"blob with id {he.BlobID} not part of the set");
						continue;
					}

					foreach (BlobReader blob in blobList) {
						if (he.LocalBlobIndex >= (uint)blob.Assemblies.Count) {
							Logger (BlobExplorerLogLevel.Warning, $"{bitness}-bit index entry with hash 0x{he.Hash:x} has invalid blob {blob.BlobID} index {he.LocalBlobIndex} (maximum allowed is {blob.Assemblies.Count})");
							continue;
						}

						BlobAssembly assembly = blob.Assemblies[(int)he.LocalBlobIndex];
						assemblyHandler (he, assembly);

						if (!AssembliesByName.ContainsKey (assembly.Name)) {
							AssembliesByName.Add (assembly.Name, assembly);
						}
					}
				}
			}
		}

		void ReadBlobSetFromArchive (string baseName, string archivePath, string extension)
		{
			string basePathInArchive;

			if (String.Compare (".aab", extension, StringComparison.OrdinalIgnoreCase) == 0) {
				basePathInArchive = "base/root/assemblies";
			} else if (String.Compare (".apk", extension, StringComparison.OrdinalIgnoreCase) == 0) {
				basePathInArchive = "assemblies";
			} else if (String.Compare (".zip", extension, StringComparison.OrdinalIgnoreCase) == 0) {
				basePathInArchive = "root/assemblies";
			} else {
				throw new InvalidOperationException ($"Unrecognized archive extension '{extension}'");
			}

			basePathInArchive = $"{basePathInArchive}/{baseName}.";
			using (ZipArchive archive = ZipArchive.Open (archivePath, FileMode.Open)) {
				ReadBlobSetFromArchive (archive, basePathInArchive);
			}
		}

		void ReadBlobSetFromArchive (ZipArchive archive, string basePathInArchive)
		{
			foreach (ZipEntry entry in archive) {
				if (!entry.FullName.StartsWith (basePathInArchive, StringComparison.Ordinal)) {
					continue;
				}

				using (var stream = new MemoryStream ()) {
					entry.Extract (stream);

					if (entry.FullName.EndsWith (".blob", StringComparison.Ordinal)) {
						AddBlob (new BlobReader (stream, GetBlobArch (entry.FullName), keepBlobInMemory));
					} else if (entry.FullName.EndsWith (".manifest", StringComparison.Ordinal)) {
						manifest = new BlobManifestReader (stream);
					}
				}
			}
		}

		void AddBlob (BlobReader reader)
		{
			if (reader.HasGlobalIndex) {
				indexBlob = reader;
			}

			List<BlobReader>? blobList;
			if (!Blobs.TryGetValue (reader.BlobID, out blobList)) {
				blobList = new List<BlobReader> ();
				Blobs.Add (reader.BlobID, blobList);
			}
			blobList.Add (reader);

			Assemblies.AddRange (reader.Assemblies);
		}

		string? GetBlobArch (string path)
		{
			string? arch = Path.GetFileNameWithoutExtension (path);
			if (!String.IsNullOrEmpty (arch)) {
				arch = Path.GetExtension (arch);
				if (!String.IsNullOrEmpty (arch)) {
					arch = arch.Substring (1);
				}
			}

			return arch;
		}

		void ReadBlobSetFromFilesystem (string baseName, string setPath)
		{
			foreach (string de in Directory.EnumerateFiles (setPath, $"{baseName}.*", SearchOption.TopDirectoryOnly)) {
				string? extension = Path.GetExtension (de);
				if (String.IsNullOrEmpty (extension)) {
					continue;
				}

				if (String.Compare (".blob", extension, StringComparison.OrdinalIgnoreCase) == 0) {
					AddBlob (ReadBlob (de));
				} else if (String.Compare (".manifest", extension, StringComparison.OrdinalIgnoreCase) == 0) {
					manifest = ReadManifest (de);
				}
			}

			BlobReader ReadBlob (string filePath)
			{
				string? arch = GetBlobArch (filePath);
				using (var fs = File.OpenRead (filePath)) {
					return CreateBlobReader (fs, arch);
				}
			}

			BlobManifestReader ReadManifest (string filePath)
			{
				using (var fs = File.OpenRead (filePath)) {
					return new BlobManifestReader (fs);
				}
			}
		}

		BlobReader CreateBlobReader (Stream input, string? arch)
		{
			numberOfBlobs++;
			return new BlobReader (input, arch, keepBlobInMemory);
		}

		bool IsAndroidArchive (string extension)
		{
			return
				String.Compare (".aab", extension, StringComparison.OrdinalIgnoreCase) == 0 ||
				String.Compare (".apk", extension, StringComparison.OrdinalIgnoreCase) == 0 ||
				String.Compare (".zip", extension, StringComparison.OrdinalIgnoreCase) == 0;
		}

		string GetBaseNameHaveExtension (string blobPath, string extension)
		{
			if (IsAndroidArchive (extension)) {
				return "assemblies";
			}

			string fileName = Path.GetFileNameWithoutExtension (blobPath);
			int dot = fileName.IndexOf ('.');
			if (dot >= 0) {
				return fileName.Substring (0, dot);
			}

			return fileName;
		}

		string GetBaseNameNoExtension (string blobPath)
		{
			string fileName = Path.GetFileName (blobPath);
			if (fileName.EndsWith ("_assemblies", StringComparison.OrdinalIgnoreCase)) {
				return fileName;
			}
			return $"{fileName}_assemblies";
		}
	}
}
