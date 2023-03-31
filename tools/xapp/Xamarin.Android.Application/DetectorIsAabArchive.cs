using Xamarin.Android.Application.Utilities;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Application;

class DetectorIsAabArchive : DetectorSupportedAndroidArchive
{
	protected override InputReader? CreateReader (ZipArchive archive, string inputFilePath, ILogger log)
	{
		return new InputReaderAab (archive, inputFilePath, log);
	}

	protected override bool HasAllRequiredEntries (ZipArchive archive, string inputFilePath, ILogger log)
	{
		return archive.ContainsEntry ("base/manifest/AndroidManifest.xml", caseSensitive: true);
	}
}
