using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Xamarin.Android.Tasks
{
	abstract class AssemblyStore
	{
		// The two constants below must match their counterparts in src/monodroid/jni/xamarin-app.hh
		const uint BlobMagic = 0x41424158; // 'XABA', little-endian, must match the BUNDLED_ASSEMBLIES_BLOB_MAGIC native constant
		const uint BlobVersion = 1; // Must match the BUNDLED_ASSEMBLIES_BLOB_VERSION native constant

		// MUST be equal to the size of the BlobBundledAssembly struct in src/monodroid/jni/xamarin-app.hh
		const uint BlobBundledAssemblyNativeStructSize = 6 * sizeof (uint);

		// MUST be equal to the size of the BlobHashEntry struct in src/monodroid/jni/xamarin-app.hh
		const uint BlobHashEntryNativeStructSize = sizeof (ulong) + (3 * sizeof (uint));

		// MUST be equal to the size of the BundledAssemblyBlobHeader struct in src/monodroid/jni/xamarin-app.hh
		const uint BlobHeaderNativeStructSize = sizeof (uint) * 5;

		protected const string BlobPrefix = "assemblies";
		protected const string BlobExtension = ".blob";

		static readonly ArrayPool<byte> bytePool = ArrayPool<byte>.Shared;

		string archiveAssembliesPrefix;
		string indexBlobPath;

		protected string ApkName { get; }
		protected TaskLoggingHelper Log { get; }
		protected AssemblyStoreGlobalIndex GlobalIndexCounter { get; }

		public uint ID { get; }
		public bool IsIndexStore => ID == 0;

		protected AssemblyStore (string apkName, string archiveAssembliesPrefix, TaskLoggingHelper log, uint id, AssemblyStoreGlobalIndex globalIndexCounter)
		{
			if (String.IsNullOrEmpty (archiveAssembliesPrefix)) {
				throw new ArgumentException ("must not be null or empty", nameof (archiveAssembliesPrefix));
			}

			if (String.IsNullOrEmpty (apkName)) {
				throw new ArgumentException ("must not be null or empty", nameof (apkName));
			}

			GlobalIndexCounter = globalIndexCounter ?? throw new ArgumentNullException (nameof (globalIndexCounter));
			ID = id;

			this.archiveAssembliesPrefix = archiveAssembliesPrefix;
			ApkName = apkName;
			Log = log;
		}

		public abstract void Add (AssemblyStoreAssemblyInfo blobAssembly);
		public abstract void Generate (string outputDirectory, List<AssemblyStoreIndexEntry> globalIndex, List<string> blobPaths);

		public virtual string WriteIndex (List<AssemblyStoreIndexEntry> globalIndex)
		{
			if (!IsIndexStore) {
				throw new InvalidOperationException ("Assembly index may be written only to blob with index 0");
			}

			if (String.IsNullOrEmpty (indexBlobPath)) {
				throw new InvalidOperationException ("Index blob path not set, was Generate called properly?");
			}

			if (globalIndex == null) {
				throw new ArgumentNullException (nameof (globalIndex));
			}

			string indexBlobHeaderPath = $"{indexBlobPath}.hdr";
			string indexBlobManifestPath = Path.ChangeExtension (indexBlobPath, "manifest");

			using (var hfs = File.Open (indexBlobHeaderPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
				using (var writer = new BinaryWriter (hfs, Encoding.UTF8, leaveOpen: true)) {
					WriteIndex (writer, indexBlobManifestPath, globalIndex);
					writer.Flush ();
				}

				using (var ifs = File.Open (indexBlobPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
					ifs.CopyTo (hfs);
					hfs.Flush ();
				}
			}

			File.Delete (indexBlobPath);
			File.Move (indexBlobHeaderPath, indexBlobPath);

			return indexBlobManifestPath;
		}

		void WriteIndex (BinaryWriter blobWriter, string manifestPath, List<AssemblyStoreIndexEntry> globalIndex)
		{
			using (var manifest = File.Open (manifestPath, FileMode.Create, FileAccess.Write)) {
				using (var manifestWriter = new StreamWriter (manifest, new UTF8Encoding (false))) {
					WriteIndex (blobWriter, manifestWriter, globalIndex);
					manifestWriter.Flush ();
				}
			}
		}

		void WriteIndex (BinaryWriter blobWriter, StreamWriter manifestWriter, List<AssemblyStoreIndexEntry> globalIndex)
		{
			uint localEntryCount = 0;
			var localAssemblies = new List<AssemblyStoreIndexEntry> ();

			manifestWriter.WriteLine ("Hash 32\tHash 64\tBlob ID\tBlob idx\tName");

			var seenHashes32 = new HashSet<ulong> ();
			var seenHashes64 = new HashSet<ulong> ();
			bool haveDuplicates = false;
			foreach (AssemblyStoreIndexEntry assembly in globalIndex) {
				if (assembly.StoreID == ID) {
					localEntryCount++;
					localAssemblies.Add (assembly);
				}

				if (WarnAboutDuplicateHash ("32", assembly.Name, assembly.NameHash32, seenHashes32) ||
				    WarnAboutDuplicateHash ("64", assembly.Name, assembly.NameHash64, seenHashes64)) {
					haveDuplicates = true;
				}

				manifestWriter.WriteLine ($"0x{assembly.NameHash32:x08}\t0x{assembly.NameHash64:x016}\t{assembly.StoreID:d03}\t{assembly.LocalBlobIndex:d04}\t{assembly.Name}");
			}

			if (haveDuplicates) {
				throw new InvalidOperationException ("Duplicate assemblies encountered");
			}

			uint globalAssemblyCount = (uint)globalIndex.Count;

			blobWriter.Seek (0, SeekOrigin.Begin);
			WriteBlobHeader (blobWriter, localEntryCount, globalAssemblyCount);

			// Header and two tables of the same size, each for 32 and 64-bit hashes
			uint offsetFixup = BlobHeaderNativeStructSize + (BlobHashEntryNativeStructSize * globalAssemblyCount * 2);

			WriteAssemblyDescriptors (blobWriter, localAssemblies, CalculateOffsetFixup ((uint)localAssemblies.Count, offsetFixup));

			var sortedIndex = new List<AssemblyStoreIndexEntry> (globalIndex);
			sortedIndex.Sort ((AssemblyStoreIndexEntry a, AssemblyStoreIndexEntry b) => a.NameHash32.CompareTo (b.NameHash32));
			foreach (AssemblyStoreIndexEntry entry in sortedIndex) {
				WriteHash (entry, entry.NameHash32);
			}

			sortedIndex.Sort ((AssemblyStoreIndexEntry a, AssemblyStoreIndexEntry b) => a.NameHash64.CompareTo (b.NameHash64));
			foreach (AssemblyStoreIndexEntry entry in sortedIndex) {
				WriteHash (entry, entry.NameHash64);
			}

			void WriteHash (AssemblyStoreIndexEntry entry, ulong hash)
			{
				blobWriter.Write (hash);
				blobWriter.Write (entry.MappingIndex);
				blobWriter.Write (entry.LocalBlobIndex);
				blobWriter.Write (entry.StoreID);
			}

			bool WarnAboutDuplicateHash (string bitness, string assemblyName, ulong hash, HashSet<ulong> seenHashes)
			{
				if (seenHashes.Contains (hash)) {
					Log.LogMessage (MessageImportance.High, $"Duplicate {bitness}-bit hash 0x{hash} encountered for assembly {assemblyName}");
					return true;
				}

				seenHashes.Add (hash);
				return false;
			}
		}

		protected string GetAssemblyName (AssemblyStoreAssemblyInfo assembly)
		{
			string assemblyName = Path.GetFileNameWithoutExtension (assembly.FilesystemAssemblyPath);
			if (assemblyName.EndsWith (".dll", StringComparison.OrdinalIgnoreCase)) {
				assemblyName = Path.GetFileNameWithoutExtension (assemblyName);
			}

			return assemblyName;
		}

		protected void Generate (string outputFilePath, List<AssemblyStoreAssemblyInfo> assemblies, List<AssemblyStoreIndexEntry> globalIndex, List<string> blobPaths, bool addToGlobalIndex = true)
		{
			if (globalIndex == null) {
				throw new ArgumentNullException (nameof (globalIndex));
			}

			if (blobPaths == null) {
				throw new ArgumentNullException (nameof (blobPaths));
			}

			if (IsIndexStore) {
				indexBlobPath = outputFilePath;
			}

			blobPaths.Add (outputFilePath);
			Log.LogMessage (MessageImportance.Low, $"AssemblyBlobGenerator: generating blob: {outputFilePath}");

			using (var fs = File.Open (outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read)) {
				using (var writer = new BinaryWriter (fs, Encoding.UTF8)) {
					Generate (writer, assemblies, globalIndex, addToGlobalIndex);
					writer.Flush ();
				}
			}
		}

		void Generate (BinaryWriter writer, List<AssemblyStoreAssemblyInfo> assemblies, List<AssemblyStoreIndexEntry> globalIndex, bool addToGlobalIndex)
		{
			var localAssemblies = new List<AssemblyStoreIndexEntry> ();

			if (!IsIndexStore) {
				// Index blob's header and data before the assemblies is handled in WriteIndex in a slightly different
				// way.
				uint nbytes = BlobHeaderNativeStructSize + (BlobBundledAssemblyNativeStructSize * (uint)assemblies.Count);
				var zeros = bytePool.Rent ((int)nbytes);
				writer.Write (zeros, 0, (int)nbytes);
				bytePool.Return (zeros);
			}

			foreach (AssemblyStoreAssemblyInfo assembly in assemblies) {
				string assemblyName = GetAssemblyName (assembly);
				string archivePath = assembly.ArchiveAssemblyPath;
				if (archivePath.StartsWith (archiveAssembliesPrefix, StringComparison.OrdinalIgnoreCase)) {
					archivePath = archivePath.Substring (archiveAssembliesPrefix.Length);
				}

				if (!String.IsNullOrEmpty (assembly.Abi)) {
					string abiPath = $"{assembly.Abi}/";
					if (archivePath.StartsWith (abiPath, StringComparison.Ordinal)) {
						archivePath = archivePath.Substring (abiPath.Length);
					}
				}

				if (!String.IsNullOrEmpty (archivePath)) {
					if (archivePath.EndsWith ("/", StringComparison.Ordinal)) {
						assemblyName = $"{archivePath}{assemblyName}";
					} else {
						assemblyName = $"{archivePath}/{assemblyName}";
					}
				}
				AssemblyStoreIndexEntry entry = WriteAssembly (writer, assembly, assemblyName, (uint)localAssemblies.Count);
				if (addToGlobalIndex) {
					globalIndex.Add (entry);
				}
				localAssemblies.Add (entry);
			}

			writer.Flush ();

			if (IsIndexStore) {
				return;
			}

			writer.Seek (0, SeekOrigin.Begin);
			WriteBlobHeader (writer, (uint)localAssemblies.Count);
			WriteAssemblyDescriptors (writer, localAssemblies);
		}

		uint CalculateOffsetFixup (uint localAssemblyCount, uint extraOffset = 0)
		{
			return (BlobBundledAssemblyNativeStructSize * (uint)localAssemblyCount) + extraOffset;
		}

		void WriteBlobHeader (BinaryWriter writer, uint localEntryCount, uint globalEntryCount = 0)
		{
			// Header, must be identical to the BundledAssemblyBlobHeader structure in src/monodroid/jni/xamarin-app.hh
			writer.Write (BlobMagic);               // magic
			writer.Write (BlobVersion);             // version
			writer.Write (localEntryCount);         // local_entry_count
			writer.Write (globalEntryCount);        // global_entry_count
			writer.Write ((uint)ID);                // blob_id
		}

		void WriteAssemblyDescriptors (BinaryWriter writer, List<AssemblyStoreIndexEntry> assemblies, uint offsetFixup = 0)
		{
			// Each assembly must be identical to the BlobBundledAssembly structure in src/monodroid/jni/xamarin-app.hh

			foreach (AssemblyStoreIndexEntry assembly in assemblies) {
				AdjustOffsets (assembly, offsetFixup);

				writer.Write (assembly.DataOffset);
				writer.Write (assembly.DataSize);

				writer.Write (assembly.DebugDataOffset);
				writer.Write (assembly.DebugDataSize);

				writer.Write (assembly.ConfigDataOffset);
				writer.Write (assembly.ConfigDataSize);
			}
		}

		void AdjustOffsets (AssemblyStoreIndexEntry assembly, uint offsetFixup)
		{
			if (offsetFixup == 0) {
				return;
			}

			assembly.DataOffset += offsetFixup;

			if (assembly.DebugDataOffset > 0) {
				assembly.DebugDataOffset += offsetFixup;
			}

			if (assembly.ConfigDataOffset > 0) {
				assembly.ConfigDataOffset += offsetFixup;
			}
		}

		AssemblyStoreIndexEntry WriteAssembly (BinaryWriter writer, AssemblyStoreAssemblyInfo assembly, string assemblyName, uint localBlobIndex)
		{
			uint offset;
			uint size;

			(offset, size) = WriteFile (assembly.FilesystemAssemblyPath, true);

			// NOTE: globalAssemblIndex++ is not thread safe but it **must** increase monotonically (see also ArchAssemblyStore.Generate for a special case)
			var ret = new AssemblyStoreIndexEntry (assemblyName, ID, GlobalIndexCounter.Increment (), localBlobIndex) {
				DataOffset = offset,
				DataSize = size,
			};

			(offset, size) = WriteFile (assembly.DebugInfoPath, required: false);
			if (offset != 0 && size != 0) {
				ret.DebugDataOffset = offset;
				ret.DebugDataSize = size;
			}

			// Config files must end with \0 (nul)
			(offset, size) = WriteFile (assembly.ConfigPath, required: false, appendNul: true);
			if (offset != 0 && size != 0) {
				ret.ConfigDataOffset = offset;
				ret.ConfigDataSize = size;
			}

			return ret;

			(uint offset, uint size) WriteFile (string filePath, bool required, bool appendNul = false)
			{
				if (!File.Exists (filePath)) {
					if (required) {
						throw new InvalidOperationException ($"Required file '{filePath}' not found");
					}

					return (0, 0);
				}

				var fi = new FileInfo (filePath);
				if (fi.Length == 0) {
					return (0, 0);
				}

				if (fi.Length > UInt32.MaxValue || writer.BaseStream.Position + fi.Length > UInt32.MaxValue) {
					throw new InvalidOperationException ($"Writing assembly '{filePath}' to assembly blob would exceed the maximum allowed data size.");
				}

				uint offset = (uint)writer.BaseStream.Position;
				using (var fs = File.Open (filePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
					fs.CopyTo (writer.BaseStream);
				}

				uint length = (uint)fi.Length;
				if (appendNul) {
					length++;
					writer.Write ((byte)0);
				}

				return (offset, length);
			}
		}
	}
}
