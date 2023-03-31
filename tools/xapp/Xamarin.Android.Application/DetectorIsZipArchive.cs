using System;
using System.IO;

using Xamarin.Android.Application.Utilities;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Application;

class DetectorIsZipArchive : InputTypeDetector
{
	public ZipArchive? Archive { get; private set; }

	public override (bool accepted, InputReader? reader) Detect (string inputFilePath, InputTypeDetector? parent, ILogger log)
	{
		using var fs = File.OpenRead (inputFilePath);
		using var reader = new BinaryReader (fs);

		// If it's a ZIP file, then its first bytes will be local header for the first stored entry, the bytes will then be 'PK\3\4'
		var signature = new byte[4];
		int nread = reader.Read (signature);
		if (nread != signature.Length) {
			return (false, null);
		}

		bool isZipEntry = signature[0] == 'P' && signature[1] == 'K' && signature[2] == 3 && signature[3] == 4;
		if (!isZipEntry) {
			return (false, null);
		}

		Archive = ZipArchive.Open (inputFilePath, FileMode.Open);
		return (true, null);
	}
}
