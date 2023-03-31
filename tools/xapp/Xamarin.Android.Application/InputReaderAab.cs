using Xamarin.Android.Application.Utilities;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Application;

class InputReaderAab : InputReaderZip
{
	public override string DexDirPath            => "base/dex";
	public override string InternalAssetsDirPath => "base/assets/xa-internal";
	public override string ManifestDirPath       => "base/manifest";
	public override string NativeLibsDirPath     => "base/lib";
	public override string AssembliesDirPath     => "base/root/assemblies";
	public override string ArchiveType           =>"AAB";

	public InputReaderAab (ZipArchive archive, string archivePath, ILogger log)
		: base (archive, archivePath, log)
	{}
}
