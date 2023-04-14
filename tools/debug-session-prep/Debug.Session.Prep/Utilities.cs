using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Xamarin.Android.Utilities;

namespace Xamarin.Debug.Session.Prep;

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
}
