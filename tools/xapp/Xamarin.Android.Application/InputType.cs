using System;
using System.Collections.Generic;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

class InputType
{
	static readonly List<InputTypeDetector> detectors = new List<InputTypeDetector> {
		new DetectorIsFile {
			Nested = {
				new DetectorIsZipArchive {
					Nested = {
						new DetectorIsApkArchive (),
						new DetectorIsAabArchive (),
					},
				},

				new DetectorIsELFBinary {
					Nested = {
						new DetectorIsXamarinAppSharedLibrary (),
					},
				},

				new DetectorIsAssemblyStore (),
			},
		},
	};

	public static InputReader? Detect (string inputFilePath, ILogger log)
	{
		foreach (InputTypeDetector detector in detectors) {
			InputReader? reader = DetectRecursively (detector, null, inputFilePath, log);
			if (reader != null) {
				return reader;
			}
		}

		return null;
	}

	static InputReader? DetectRecursively (InputTypeDetector detector, InputTypeDetector? parentDetector, string inputFilePath, ILogger log)
	{
		bool success;
		InputReader? reader;

		try {
			(success, reader) = detector.Detect (inputFilePath, parentDetector, log);
		} catch (Exception ex) {
			// TODO: real logging, no trace
			log.ErrorLine ("Detector failed with exception:");
			log.ErrorLine (ex.ToString ());
			log.MessageLine ();
			return null;
		}

		if (!success) {
			return null;
		}

		if (reader != null) {
			return reader;
		}

		if (detector.Nested.Count == 0) {
			return null;
		}

		foreach (InputTypeDetector nestedDetector in detector.Nested) {
			reader = DetectRecursively (nestedDetector, detector, inputFilePath, log);
			if (reader != null) {
				return reader;
			}
		}

		return null;
	}
}
