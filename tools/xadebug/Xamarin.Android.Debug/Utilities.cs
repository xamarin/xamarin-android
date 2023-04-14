using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Java.Interop.Tools.JavaCallableWrappers;
using Xamarin.Android.Utilities;

namespace Xamarin.Android.Debug;

static class Utilities
{
	public static readonly UTF8Encoding UTF8NoBOM = new UTF8Encoding (false);

	public static bool IsMacOS   { get; private set; }
	public static bool IsLinux   { get; private set; }
	public static bool IsWindows { get; private set; }

	static Utilities ()
	{
		if (RuntimeInformation.IsOSPlatform (OSPlatform.Windows)) {
			IsWindows = true;
		} else if (RuntimeInformation.IsOSPlatform (OSPlatform.OSX)) {
			IsMacOS = true;
		} else if (RuntimeInformation.IsOSPlatform (OSPlatform.Linux)) {
			IsLinux = true;
		}
	}

	public static void MakeFileDirectory (string filePath)
	{
		if (String.IsNullOrEmpty (filePath)) {
			return;
		}

		string? dirName = Path.GetDirectoryName (filePath);
		if (String.IsNullOrEmpty (dirName)) {
			return;
		}

		Directory.CreateDirectory (dirName);
	}

	public static string? ReadManifestResource (XamarinLoggingHelper log, string resourceName)
	{
		using (var from = Assembly.GetExecutingAssembly ().GetManifestResourceStream (resourceName)) {
			if (from == null) {
				log.ErrorLine ($"Manifest resource '{resourceName}' cannot be loaded");
				return null;
			}

			using (var sr = new StreamReader (from)) {
				return sr.ReadToEnd ();
			}
		}
	}

	public static string NormalizeDirectoryPath (string dirPath)
	{
		if (dirPath.EndsWith ('/')) {
			return dirPath;
		}

		return $"{dirPath}/";
	}

	public static string ToLocalPathFormat (string path) => IsWindows ? path.Replace ("/", "\\") : path;

	public static string MakeLocalPath (string localDirectory, string remotePath)
	{
		string remotePathLocalFormat = ToLocalPathFormat (remotePath);
		if (remotePath[0] == '/') {
			return $"{localDirectory}{remotePathLocalFormat}";
		}

		return Path.Combine (localDirectory, remotePathLocalFormat);
	}

	public static string StringHash (string input, Encoding? encoding = null)
	{
		if (encoding == null) {
			encoding = UTF8NoBOM;
		}

		byte[] hash = Crc64Helper.Compute (encoding.GetBytes (input));
		if (hash.Length == 0) {
			return input.GetHashCode ().ToString ("x");
		}

		var sb = new StringBuilder ();
		foreach (byte b in hash) {
			sb.Append (b.ToString ("x02"));
		}

		return sb.ToString ();
	}

	public static string GetZipEntryFileName (string zipEntryName)
	{
		int idx = zipEntryName.LastIndexOf ('/');
		if (idx >= 0 && idx != zipEntryName.Length - 1) {
			return zipEntryName.Substring (idx + 1);
		}

		return zipEntryName;
	}

	public static string GetZipEntryDirName (string zipEntryName)
	{
		int idx = zipEntryName.LastIndexOf ('/');
		if (idx < 0) {
			return String.Empty;
		}

		return zipEntryName.Substring (0, idx + 1);
	}
}
