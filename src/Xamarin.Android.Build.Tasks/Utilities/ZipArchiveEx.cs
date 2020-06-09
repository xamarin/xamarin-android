using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Tasks
{
	public class ZipArchiveEx : IDisposable
	{

		public static int ZipFlushLimit = 50;

		ZipArchive zip;
		string archive;

		public ZipArchive Archive {
			get { return zip; }
		}

		public ZipArchiveEx (string archive) : this (archive, FileMode.CreateNew)
		{
		}

		public ZipArchiveEx(string archive, FileMode filemode)
		{
			this.archive = archive;
			zip = ZipArchive.Open(archive, filemode);
		}

		public void Flush ()
		{
			if (zip != null) {
				zip.Close ();
				zip.Dispose ();
				zip = null;
			}
			zip = ZipArchive.Open (archive, FileMode.Open);
		}

		string ArchiveNameForFile (string filename, string directoryPathInZip)
		{
			if (string.IsNullOrEmpty (filename)) {
				throw new ArgumentNullException (nameof (filename));
			}
			string pathName;
			if (string.IsNullOrEmpty (directoryPathInZip)) {
				pathName = Path.GetFileName (filename);
			}
			else {
				pathName = Path.Combine (directoryPathInZip, Path.GetFileName (filename));
			}
			return pathName.Replace ("\\", "/");
		}

		void AddFiles (string folder, string folderInArchive, CompressionMethod method)
		{
			int count = 0;
			foreach (string fileName in Directory.GetFiles (folder, "*.*", SearchOption.TopDirectoryOnly)) {
				var fi = new FileInfo (fileName);
				if ((fi.Attributes & FileAttributes.Hidden) != 0)
					continue;
				var archiveFileName = ArchiveNameForFile (fileName, folderInArchive);
				long index = -1;
				if (zip.ContainsEntry (archiveFileName, out index)) {
					var e = zip.First (x => x.FullName == archiveFileName);
					if (e.ModificationTime < fi.LastWriteTimeUtc)
						zip.AddFile (fileName, archiveFileName, compressionMethod: method);
				} else {
					zip.AddFile (fileName, archiveFileName, compressionMethod: method);
				}
				count++;
				if (count == ZipArchiveEx.ZipFlushLimit) {
					Flush ();
					count = 0;
				}
			}
		}

		public void RemoveFile (string folder, string file)
		{
			var archiveName = ArchiveNameForFile (file, Path.Combine (folder, Path.GetDirectoryName (file)));
			long index = -1;
			if (zip.ContainsEntry (archiveName, out index))
				zip.DeleteEntry ((ulong)index);
		}

		public void AddDirectory (string folder, string folderInArchive, CompressionMethod method = CompressionMethod.Default)
		{
			if (!string.IsNullOrEmpty (folder)) {
				folder = folder.Replace ('/', Path.DirectorySeparatorChar).Replace ('\\', Path.DirectorySeparatorChar);
				folder = Path.GetFullPath (folder);
				if (folder [folder.Length - 1] == Path.DirectorySeparatorChar) {
					folder = folder.Substring (0, folder.Length - 1);
				}
			}

			AddFiles (folder, folderInArchive, method);
			foreach (string dir in Directory.GetDirectories (folder, "*", SearchOption.AllDirectories)) {
				var di = new DirectoryInfo (dir);
				if ((di.Attributes & FileAttributes.Hidden) != 0)
					continue;
				var internalDir = dir.Replace (folder, string.Empty);
				string fullDirPath = folderInArchive + internalDir;
				try {
					zip.CreateDirectory (fullDirPath);
				} catch (ZipException) {
					
				}
				AddFiles (dir, fullDirPath, method);
			}
		}

		/// <summary>
		/// HACK: aapt2 is creating zip entries on Windows such as `assets\subfolder/asset2.txt`
		/// </summary>
		public void FixupWindowsPathSeparators (HashSet<ulong> deletedEntries, Action<string, string> onRename)
		{
			bool modified = false;

			long entryCount = zip.EntryCount;
			if (entryCount < 0)
				return;

			for (ulong i = 0; i < (ulong) entryCount; ++i) {
				// If an entry index has been deleted then ReadEntry will throw a "Entry has been deleted" ZipException.
				// So we skip deleted entries instead. Later, when we move to a newer LibZipSharp (>= 1.0.13) with this fix
				// https://github.com/xamarin/LibZipSharp/pull/53 we can remove this workaround and go back to using
				// a foreach enumerator to enumerate the contents of the zip. With the fix, deleted entries are
				// skipped on the LibZipSharp side when enumerating. But we want to keep the LibZipSharp bump as
				// a separate change, with the other changes that come with it, and not depenend on that here.
				if (deletedEntries.Contains (i))
					continue;

				var entry = zip.ReadEntry (i);
				if (entry.FullName.Contains ('\\')) {
					var name = entry.FullName.Replace ('\\', '/');
					onRename?.Invoke (entry.FullName, name);
					entry.Rename (name);
					modified = true;
				}
			}
			if (modified) {
				Flush ();
			}
		}

		public bool SkipExistingFile (string file, string fileInArchive)
		{
			if (!zip.ContainsEntry (fileInArchive)) {
				return false;
			}
			var lastWrite = File.GetLastWriteTimeUtc (file);
			var entry = zip.ReadEntry (fileInArchive);
			return WithoutMilliseconds (lastWrite) <= WithoutMilliseconds (entry.ModificationTime);
		}

		public bool SkipExistingEntry (ZipEntry sourceEntry, string fileInArchive)
		{
			if (!zip.ContainsEntry (fileInArchive)) {
				return false;
			}
			var entry = zip.ReadEntry (fileInArchive);
			return WithoutMilliseconds (sourceEntry.ModificationTime) <= WithoutMilliseconds (entry.ModificationTime);
		}

		// The zip file and macOS/mono does not contain milliseconds
		// Windows *does* contain milliseconds
		static DateTime WithoutMilliseconds (DateTime t) =>
			new DateTime (t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, t.Kind);

		public void Dispose ()
		{
			Dispose(true);
			GC.SuppressFinalize (this);
		}

		protected virtual void Dispose(bool disposing) {
			if (disposing) {
				if (zip != null) {
					zip.Close ();
					zip.Dispose ();
					zip = null;
				}
			}	
		}
	}
}
