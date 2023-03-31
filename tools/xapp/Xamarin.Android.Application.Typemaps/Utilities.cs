using System;
using System.IO;

using ELFSharp.ELF;

namespace Xamarin.Android.Application.Typemaps;

static class TypemapUtilities
{
	public static string GetOutputFileBaseName (string outputDirectory, string formatVersion, MapKind kind, MapArchitecture architecture)
	{
		string ret = $"typemap-v{formatVersion}-{kind}-{architecture}";
		if (outputDirectory.Length == 0) {
			return ret;
		}

		return Path.Combine (outputDirectory, ret);
	}

	public static string GetManagedOutputFileName (string baseFileName, string extension)
	{
		return $"{baseFileName}-managed.{extension}";
	}

	public static string GetJavaOutputFileName (string baseFileName, string extension)
	{
		return $"{baseFileName}-java.{extension}";
	}

	public static MapArchitecture GetMapArchitecture (IELF elf)
        {
                switch (elf.Machine) {
                        case Machine.ARM:
                                return MapArchitecture.ARM;

                        case Machine.Intel386:
                                return MapArchitecture.X86;

                        case Machine.AArch64:
                                return MapArchitecture.ARM64;

                        case Machine.AMD64:
                                return MapArchitecture.X86_64;

                        default:
                                throw new InvalidOperationException ($"Unsupported ELF machine type {elf.Machine}");
                }
        }
}
