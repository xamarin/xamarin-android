using System;
using System.IO;
using System.Linq;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Tasks
{
	public class ZipArchiveEx : IDisposable
	{

		const int DEFAULT_FLUSH_SIZE_LIMIT = 100 * 1024 * 1024;
		const int DEFAULT_FLUSH_FILES_LIMIT = 512;

		ZipArchive zip;
		string archive;
		long filesWrittenTotalSize = 0;
		long filesWrittenTotalCount = 0;

		public ZipArchive Archive {
			get { return zip; }
		}

		public bool AutoFlush { get; set; } = true;

		public bool CreateDirectoriesInZip { get; set; } = true;

		public int ZipFlushSizeLimit { get; set; } = DEFAULT_FLUSH_SIZE_LIMIT;
		public int ZipFlushFilesLimit { get; set; } = DEFAULT_FLUSH_FILES_LIMIT;

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
			filesWrittenTotalSize = 0;
			filesWrittenTotalCount = 0;
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
			return pathName.Replace ("\\", "/").TrimStart ('/');
		}

		void AddFileAndFlush (string filename, long fileLength, string archiveFileName, CompressionMethod compressionMethod)
		{
			filesWrittenTotalSize += fileLength;
			zip.AddFile (filename, archiveFileName, compressionMethod: compressionMethod);
			if ((filesWrittenTotalSize >= ZipFlushSizeLimit || filesWrittenTotalCount >= ZipFlushFilesLimit) && AutoFlush) {
				Flush ();
			}
		}

		public void AddFileAndFlush (string filename, string archiveFileName, CompressionMethod compressionMethod)
		{
			var fi = new FileInfo (filename);
			AddFileAndFlush (filename, fi.Length, archiveFileName, compressionMethod);
		}

		public void AddEntryAndFlush (byte[] data, string archiveFileName)
		{
			filesWrittenTotalSize += data.Length;
			zip.AddEntry (data, archiveFileName);
			if ((filesWrittenTotalSize >= ZipFlushSizeLimit || filesWrittenTotalCount >= ZipFlushFilesLimit) && AutoFlush) {
				Flush ();
			}
		}

		public void AddEntryAndFlush (string archiveFileName, Stream data, CompressionMethod method)
		{
			filesWrittenTotalSize += data.Length;
			zip.AddEntry (archiveFileName, data, method);
			if ((filesWrittenTotalSize >= ZipFlushSizeLimit || filesWrittenTotalCount >= ZipFlushFilesLimit) && AutoFlush) {
				Flush ();
			}
		}

		void AddFiles (string folder, string folderInArchive, CompressionMethod method)
		{
			foreach (string fileName in Directory.GetFiles (folder, "*.*", SearchOption.TopDirectoryOnly)) {
				var fi = new FileInfo (fileName);
				if ((fi.Attributes & FileAttributes.Hidden) != 0)
					continue;
				var archiveFileName = ArchiveNameForFile (fileName, folderInArchive);
				long index = -1;
				if (zip.ContainsEntry (archiveFileName, out index)) {
					var e = zip.First (x => x.FullName == archiveFileName);
					if (e.ModificationTime < fi.LastWriteTimeUtc || e.Size != (ulong)fi.Length) {
						AddFileAndFlush (fileName, fi.Length, archiveFileName, compressionMethod: method);
					}
				} else {
					AddFileAndFlush (fileName, fi.Length, archiveFileName, compressionMethod: method);
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

		public bool MoveEntry (string from, string to)
		{
			if (!zip.ContainsEntry (from)) {
				return false;
			}
			var entry = zip.ReadEntry (from);
			using (var stream = new MemoryStream ()) {
				entry.Extract (stream);
				stream.Position = 0;
				zip.AddEntry (to, stream);
				zip.DeleteEntry (entry);
				Flush ();
			}
			return true;
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
				AddFiles (dir, fullDirPath, method);
			}
		}

		/// <summary>
		/// HACK: aapt2 is creating zip entries on Windows such as `assets\subfolder/asset2.txt`
		/// </summary>
		public void FixupWindowsPathSeparators (Action<string, string> onRename)
		{
			bool modified = false;
			foreach (var entry in zip) {
				if (entry.FullName.Contains ("\\")) {
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

		public bool SkipExistingFile (string file, string fileInArchive, CompressionMethod compressionMethod)
		{
			if (!zip.ContainsEntry (fileInArchive)) {
				return false;
			}
			var entry = zip.ReadEntry (fileInArchive);
			switch (compressionMethod) {
				case CompressionMethod.Unknown:
					// If incoming value is Unknown, don't check anything
					break;
				case CompressionMethod.Default:
					// For Default, existing entries could have CompressionMethod.Deflate
					// Only compare against CompressionMethod.Store
					if (entry.CompressionMethod == CompressionMethod.Store)
						return false;
					break;
				default:
					// Other values can just compare CompressionMethod
					if (entry.CompressionMethod != compressionMethod)
						return false;
					break;
			}
			var lastWrite = File.GetLastWriteTimeUtc (file);
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
