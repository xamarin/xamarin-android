using System.IO;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

class InputReaderXamarinApp : InputReader
{
	Stream? inputStream;
	string? filePath;
	DataProviderXamarinApp? xamarinApp;
	IDataProviderTypemaps? typeMaps;

	public override bool SupportsAssemblyExtraction => false;
	public override bool SupportsAssemblyStore      => false;
	public override bool SupportsXamarinApp         => true;
	public override bool SupportsTypemaps           => true;
	public override bool SupportsAppInfo            => false;

	public InputReaderXamarinApp (string filePath, ILogger log)
		: base (log)
	{
		this.filePath = filePath;
		inputStream = null;
	}

	public InputReaderXamarinApp (Stream inputStream, string? filePath, ILogger log)
		: base (log)
	{
		this.inputStream = inputStream;
		filePath = null;
	}

	protected override DataProviderXamarinApp? ReadXamarinApp ()
	{
		return CreateProvider (
			filePath,
			ref inputStream,
			ref xamarinApp,
			(Stream s, string? path, ILogger logger) => new DataProviderXamarinApp (s, path, logger)
		);
	}

	protected override IDataProviderTypemaps? ReadTypemaps ()
	{
		return CreateProvider (
			filePath,
			ref inputStream,
			ref typeMaps,
			(Stream s, string? path, ILogger logger) => DataProviderTypemapsXamarinApp.Create (s, path, logger)
		);
	}
}
