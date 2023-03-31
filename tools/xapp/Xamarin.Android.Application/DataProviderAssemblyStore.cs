using System.Collections.Generic;
using System.IO;

using Xamarin.Android.Application.Utilities;
using Xamarin.Android.AssemblyStore;

namespace Xamarin.Android.Application;

class DataProviderAssemblyStore : DataProvider
{
	public List<AssemblyStoreReader> Blobs { get; } = new List<AssemblyStoreReader> ();
	public AssemblyStoreManifestReader? Manifest { get; private set; }

	public DataProviderAssemblyStore (Stream inputStream, string? inputPath, ILogger log)
		: base (inputStream, inputPath, log)
	{
		Blobs.Add (new AssemblyStoreReader (inputStream, inputPath, keepStoreInMemory: true));
	}

	public bool ExtractAssembly (string assemblyNameRegex, string outputDirectory, bool decompress)
	{
		return false;
	}

	public void EnsureFullAssemblyInformation ()
	{
		foreach (AssemblyStoreReader blob in Blobs) {
			blob.EnsureAssemblyNames (Manifest);
		}
	}

	public ICollection<string> GetAssemblyNames ()
	{
		var ret = new HashSet<string> ();

		foreach (AssemblyStoreReader blob in Blobs) {
			blob.EnsureAssemblyNames (Manifest);

			foreach (AssemblyStoreAssembly asm in blob.Assemblies) {
				if (asm.Name.Length == 0 || ret.Contains (asm.Name)) {
					continue;
				}

				ret.Add (asm.Name);
			}
		}

		return ret;
	}
}
