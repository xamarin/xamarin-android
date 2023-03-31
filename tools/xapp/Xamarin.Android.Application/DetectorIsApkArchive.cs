using Xamarin.Android.Application.Utilities;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Application;

class DetectorIsApkArchive : DetectorSupportedAndroidArchive
{
	protected override InputReader? CreateReader (ZipArchive archive, string inputFilePath, ILogger log)
	{
		return new InputReaderApk (archive, inputFilePath, log);
	}

	protected override bool HasAllRequiredEntries (ZipArchive archive, string inputFilePath, ILogger log)
	{
		return archive.ContainsEntry ("AndroidManifest.xml", caseSensitive: true);
	}
}
