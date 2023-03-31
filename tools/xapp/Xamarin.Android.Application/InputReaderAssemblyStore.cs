using System;
using System.IO;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

class InputReaderAssemblyStore : InputReader
{
	Stream? inputStream;
	string? filePath;
	DataProviderAssemblyStore? store;

	public override bool SupportsAssemblyExtraction => true;
	public override bool SupportsAssemblyStore      => true;
	public override bool SupportsXamarinApp         => false;
	public override bool SupportsTypemaps           => false;
	public override bool SupportsAppInfo            => false;

	public InputReaderAssemblyStore (string filePath, ILogger log)
		: base (log)
	{
		this.filePath = filePath;
		inputStream = null;
	}

	public InputReaderAssemblyStore (Stream inputStream, string? filePath, ILogger log)
		: base (log)
	{
		this.inputStream = inputStream;
		filePath = null;
	}

	protected override bool DoExtractAssembly (string assemblyNameRegex, string outputDirectory, bool decompress)
	{
		DataProviderAssemblyStore? assemblyStore = ReadAssemblyStore ();
		if (assemblyStore == null) {
			return false;
		}

		throw new NotImplementedException ();
	}

	protected override DataProviderAssemblyStore? ReadAssemblyStore ()
	{
		return CreateProvider (
			filePath,
			ref inputStream,
			ref store,
			(Stream s, string? path, ILogger logger) => new DataProviderAssemblyStore (s, path, logger)
		);
	}
}
