using System.IO;

using Xamarin.Android.Application.Utilities;
using Xamarin.Android.AssemblyStore;

namespace Xamarin.Android.Application;

class DetectorIsAssemblyStore : InputTypeDetector
{
	public override (bool accepted, InputReader? reader) Detect (string inputFilePath, InputTypeDetector? parent, ILogger log)
	{
		using var fs = File.OpenRead (inputFilePath);
		using var reader = new BinaryReader (fs);

		uint signature = reader.ReadUInt32 ();
		if (signature != AssemblyStoreReader.ASSEMBLY_STORE_MAGIC) {
			return (false, null);
		}

		return (true, new InputReaderAssemblyStore (inputFilePath, log));
	}
}
