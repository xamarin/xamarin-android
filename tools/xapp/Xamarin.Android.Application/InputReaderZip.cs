using System;

using Xamarin.Android.Application.Utilities;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Application;

abstract class InputReaderZip : InputReader
{
	public const string AndroidManifestName = "AndroidManifest.xml";

	public override bool SupportsAssemblyExtraction => true;
	public override bool SupportsAssemblyStore      => true;
	public override bool SupportsXamarinApp         => true;
	public override bool SupportsTypemaps           => true;
	public override bool SupportsAppInfo            => true;

	public ZipArchive Archive { get; }
	public string ArchivePath { get; }

	public abstract string DexDirPath            { get; }
	public abstract string InternalAssetsDirPath { get; }
	public abstract string ManifestDirPath       { get; }
	public abstract string NativeLibsDirPath     { get; }
	public abstract string AssembliesDirPath     { get; }
	public abstract string ArchiveType           { get; }

	protected InputReaderZip (ZipArchive archive, string archivePath, ILogger log)
		: base (log)
	{
		Archive = archive;
		ArchivePath = archivePath;
	}

	protected override bool DoExtractAssembly (string assemblyNameRegex, string outputDirectory, bool decompress )
	{
		throw new NotImplementedException ();
	}

	protected override DataProviderAppInfo? ReadAppInfo ()
	{
		return new DataProviderAppInfo (this, Log);
	}

	protected override DataProviderXamarinApp? ReadXamarinApp ()
	{
		throw new NotImplementedException ();
	}

	protected override IDataProviderTypemaps? ReadTypemaps ()
	{
		throw new NotImplementedException ();
	}

	protected override DataProviderAssemblyStore? ReadAssemblyStore ()
	{
		return null;
	}
}
