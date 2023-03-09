using System;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

abstract class InputReader
{
	public abstract bool SupportsAssemblyExtraction { get; }
	public abstract bool SupportsAssemblyStore      { get; }
	public abstract bool SupportsXamarinApp         { get; }
	public abstract bool SupportsTypemaps           { get; }
	public abstract bool SupportsAppInfo            { get; }

	protected ILogger Log                           { get; }

	protected InputReader (ILogger log)
	{
		Log = log;
	}

	protected virtual DataProviderAppInfo? ReadAppInfo ()
	{
		throw new NotImplementedException ("Not implemented by this input reader");
	}

	protected virtual DataProviderXamarinApp? ReadXamarinApp ()
	{
		throw new NotImplementedException ("Not implemented by this input reader");
	}

	protected virtual DataProviderTypemaps? ReadTypemaps ()
	{
		throw new NotImplementedException ("Not implemented by this input reader");
	}

	protected virtual DataProviderAssemblyStore? ReadAssemblyStore ()
	{
		throw new NotImplementedException ("Not implemented by this input reader");
	}

	protected virtual bool DoExtractAssembly (string assemblyNameRegex, string outputDirectory, bool decompress)
	{
		throw new NotImplementedException ("Not implemented by this input reader");
	}

	public bool ExtractAssembly (string assemblyNameRegex, string outputDirectory, bool decompress = true)
	{
		if (!SupportsAssemblyExtraction) {
			throw new NotSupportedException ("Assemlby extraction is not supported by this input reader");
		}

		return DoExtractAssembly (assemblyNameRegex, outputDirectory, decompress);
	}

	public DataProviderAppInfo? GetAppInfo ()
	{
		if (!SupportsAppInfo) {
			throw new NotSupportedException ("Application information is not supported by this input reader");
		}

		return ReadAppInfo ();
	}

	public DataProviderXamarinApp? GetXamarinApp ()
	{
		if (!SupportsXamarinApp) {
			throw new NotSupportedException ("libxamarin-app.so is not supported by this input reader");
		}

		return ReadXamarinApp ();
	}

	public DataProviderTypemaps? GetTypemaps ()
	{
		if (!SupportsTypemaps) {
			throw new NotSupportedException ("Type maps are not supported by this input reader");
		}

		return ReadTypemaps ();
	}

	public DataProviderAssemblyStore? GetAssemblyStore ()
	{
		if (!SupportsAssemblyStore) {
			throw new NotSupportedException ("Assembly stores are not supported by this input reader");
		}

		return ReadAssemblyStore ();
	}
}
