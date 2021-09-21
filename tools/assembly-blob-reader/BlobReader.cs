using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Xamarin.Android.AssemblyBlobReader
{
	class BlobReader
	{
		// These two constants must be identical to the native ones in src/monodroid/jni/xamarin-app.hh
		const uint BUNDLED_ASSEMBLIES_BLOB_MAGIC = 0x41424158; // 'XABA', little-endian
		const uint BUNDLED_ASSEMBLIES_BLOB_VERSION = 1; // The highest format version this reader understands

		public uint Version                      { get; private set; }
		public uint LocalEntryCount              { get; private set; }
		public uint GlobalEntryCount             { get; private set; }
		public uint BlobID                       { get; private set; }
		public List<BlobAssembly> Assemblies     { get; }
		public List<BlobHashEntry> GlobalIndex32 { get; }
		public List<BlobHashEntry> GlobalIndex64 { get; }

		public bool HasGlobalIndex => BlobID == 0;

		public BlobReader (Stream blob)
		{
			using (var reader = new BinaryReader (blob, Encoding.UTF8, leaveOpen: true)) {
				ReadHeader (reader);

				Assemblies = new List<BlobAssembly> ();
				ReadLocalEntries (reader, Assemblies);
				if (HasGlobalIndex) {
					GlobalIndex32 = new List<BlobHashEntry> ();
					GlobalIndex64 = new List<BlobHashEntry> ();
					ReadGlobalIndex (reader, GlobalIndex32, GlobalIndex64);
				}
			}
		}

		void ReadHeader (BinaryReader reader)
		{
			uint magic = reader.ReadUInt32 ();
			if (magic != BUNDLED_ASSEMBLIES_BLOB_MAGIC) {
				throw new InvalidOperationException ("Invalid header magic number");
			}

			Version = reader.ReadUInt32 ();
			if (Version == 0) {
				throw new InvalidOperationException ("Invalid version number: 0");
			}

			if (Version > BUNDLED_ASSEMBLIES_BLOB_VERSION) {
				throw new InvalidOperationException ($"Blob format version {Version} is higher than the one understood by this reader, {BUNDLED_ASSEMBLIES_BLOB_VERSION}");
			}

			LocalEntryCount = reader.ReadUInt32 ();
			GlobalEntryCount = reader.ReadUInt32 ();
			BlobID = reader.ReadUInt32 ();
		}

		void ReadLocalEntries (BinaryReader reader, List<BlobAssembly> assemblies)
		{
			for (uint i = 0; i < LocalEntryCount; i++) {
				assemblies.Add (new BlobAssembly (reader));
			}
		}

		void ReadGlobalIndex (BinaryReader reader, List<BlobHashEntry> index32, List<BlobHashEntry> index64)
		{
			ReadIndex (true, index32);
			ReadIndex (true, index64);

			void ReadIndex (bool is32Bit, List<BlobHashEntry> index) {
				for (uint i = 0; i < GlobalEntryCount; i++) {
					index.Add (new BlobHashEntry (reader, is32Bit));
				}
			}
		}
	}
}
