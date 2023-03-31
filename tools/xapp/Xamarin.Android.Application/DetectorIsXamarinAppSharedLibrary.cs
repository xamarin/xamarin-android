using System;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

class DetectorIsXamarinAppSharedLibrary : InputTypeDetector
{
	// A handful of symbols libxamarin-app.so must have
	static readonly string[] XamarinAppExportedSymbols = new string[] {
		"application_config",
		"app_environment_variables",
		"app_system_properties",
	};

	public override (bool accepted, InputReader? reader) Detect (string inputFilePath, InputTypeDetector? parent, ILogger log)
	{
		if (parent == null) {
			throw new ArgumentNullException (nameof (parent));
		}

		var elfDetector = parent as DetectorIsELFBinary ?? throw new ArgumentException ("must be an instance of DetectorIsELFBinary class", nameof (parent));

		if (!AnELF.TryLoad (log, inputFilePath, out AnELF? elf) || elf == null) {
			log.DebugLine ($"Unable to load ELF image from {inputFilePath}");
			return (false, null);
		}

		foreach (string symbol in XamarinAppExportedSymbols) {
			if (!elf.HasSymbol (symbol)) {
				log.DebugLine ($"ELF binary '{inputFilePath}' is missing required libxamarin-app symbol '{symbol}'");
				return (false, null);
			}
		}

		return (true, new InputReaderXamarinApp (inputFilePath, log));
	}
}
