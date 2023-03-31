using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Xamarin.Android.AssemblyStore
{
	class AssemblyStoreManifestReader
	{
		static readonly char[] fieldSplit = new char[] { ' ' };

		public List<AssemblyStoreManifestEntry> Entries                      { get; } = new List<AssemblyStoreManifestEntry> ();
		public Dictionary<uint, AssemblyStoreManifestEntry> EntriesByHash32  { get; } = new Dictionary<uint, AssemblyStoreManifestEntry> ();
		public Dictionary<ulong, AssemblyStoreManifestEntry> EntriesByHash64 { get; } = new Dictionary<ulong, AssemblyStoreManifestEntry> ();

		public AssemblyStoreManifestReader (Stream manifest)
		{
			manifest.Seek (0, SeekOrigin.Begin);
			using (var sr = new StreamReader (manifest, Encoding.UTF8, detectEncodingFromByteOrderMarks: false)) {
				ReadManifest (sr);
			}
		}

		void ReadManifest (StreamReader reader)
		{
			// First line is ignored, it contains headers
			reader.ReadLine ();

			// Each subsequent line consists of fields separated with any number of spaces (for the pleasure of a human being reading the manifest)
			while (!reader.EndOfStream) {
				string[]? fields = reader.ReadLine ()?.Split (fieldSplit, StringSplitOptions.RemoveEmptyEntries);
				if (fields == null) {
					continue;
				}

				var entry = new AssemblyStoreManifestEntry (fields);
				Entries.Add (entry);
				if (entry.Hash32 != 0) {
					EntriesByHash32.Add (entry.Hash32, entry);
				}

				if (entry.Hash64 != 0) {
					EntriesByHash64.Add (entry.Hash64, entry);
				}
			}
		}
	}
}
