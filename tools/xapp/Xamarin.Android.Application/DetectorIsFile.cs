using System.IO;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

class DetectorIsFile : InputTypeDetector
{
	public override (bool accepted, InputReader? reader) Detect (string inputFilePath, InputTypeDetector? parent, ILogger log)
	{
		if (Directory.Exists (inputFilePath)) {
			FailureReason = $"Input path '{inputFilePath}' refers to directory";
			return (false, null);
		}

		if (!File.Exists (inputFilePath)) {
			FailureReason = $"Input path '{inputFilePath}' does not exist";
			return (false, null);
		}

		return (true, null);
	}
}
