using System;
using System.Collections.Generic;
using System.IO;

using K4os.Compression.LZ4;
using Mono.Cecil;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Application.Utilities;

class ApkManagedTypeResolver : ManagedTypeResolver
{
	Dictionary<string, ZipEntry> assemblies;
	ZipArchive apk;

	public ApkManagedTypeResolver (ILogger log, ZipArchive apk, string assemblyEntryPrefix)
		: base (log)
	{
		this.apk = apk;
		assemblies = new Dictionary<string, ZipEntry> (StringComparer.Ordinal);

		foreach (ZipEntry entry in apk) {
			if (!entry.FullName.StartsWith (assemblyEntryPrefix, StringComparison.Ordinal)) {
				continue;
			}

			if (!entry.FullName.EndsWith (".dll", StringComparison.Ordinal)) {
				continue;
			}

			assemblies.Add (Path.GetFileNameWithoutExtension (entry.FullName), entry);
			assemblies.Add (entry.FullName, entry);
		}
	}

	protected override string? FindAssembly (string assemblyName)
	{
		if (assemblies.Count == 0) {
			return null;
		}

		if (!assemblies.TryGetValue (assemblyName, out ZipEntry? entry) || entry == null) {
			return null;
		}

		return entry.FullName;
	}

	protected override AssemblyDefinition? ReadAssembly (string assemblyPath)
	{
		if (!assemblies.TryGetValue (assemblyPath, out ZipEntry? entry) || entry == null) {
			// Should "never" happen - if the assembly wasn't there, FindAssembly should have returned `null`
			throw new InvalidOperationException ($"Should not happen: assembly {assemblyPath} not found in the APK archive.");
		}

		var stream = new MemoryStream ();
		entry.Extract (stream);
		stream.Seek (0, SeekOrigin.Begin);

		Stream? decompressed = null;
		if (Util.IsCompressedAssembly (stream)) {
			decompressed = new MemoryStream ();
			if (!Util.DecompressAssembly (stream, decompressed)) {
				decompressed.Dispose ();
				return null;
			}
		}

		if (decompressed != null) {
			stream.Close ();
			stream.Dispose ();
			stream = new MemoryStream ();
			decompressed.CopyTo (stream);
			stream.Flush ();
			stream.Seek (0, SeekOrigin.Begin);
			decompressed.Dispose ();
		}

		return AssemblyDefinition.ReadAssembly (stream);
	}
}
