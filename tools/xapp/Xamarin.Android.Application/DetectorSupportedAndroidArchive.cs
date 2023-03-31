using System;

using Xamarin.Android.Application.Utilities;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Application;

abstract class DetectorSupportedAndroidArchive : InputTypeDetector
{
	public override (bool accepted, InputReader? reader) Detect (string inputFilePath, InputTypeDetector? parent, ILogger log)
	{
		if (parent == null) {
			throw new ArgumentNullException (nameof (parent));
		}

		var zipDetector = parent as DetectorIsZipArchive ?? throw new ArgumentException ("must be an instance of DetectorIsZipArchive class", nameof (parent));
		ZipArchive archive = zipDetector.Archive ?? throw new InvalidOperationException ("Parent detector must have a valid instance of ZipArchive");

		if (!HasAllRequiredEntries (archive, inputFilePath, log)) {
			return (false, null);
		}

		return (true, CreateReader (archive, inputFilePath, log));
	}

	protected abstract bool HasAllRequiredEntries (ZipArchive archive, string inputFilePath, ILogger log);
	protected abstract InputReader? CreateReader (ZipArchive archive, string inputFilePath, ILogger log);
}
