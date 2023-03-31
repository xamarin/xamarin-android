using System.IO;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

class DetectorIsELFBinary : InputTypeDetector
{
	//
	// First 16 bytes of a file identify it as ELF object
	// All values are taken from the C header elf.h
	//
	const int EI_INDENT           = 16; // Length of the ELF magic bytes

	const int EI_MAG0             = 0;    // File identification byte 0 index
	const int ELFMAG0             = 0x7f; // Magic number byte 0

	const int EI_MAG1             = 1;    // File identification byte 1 index
	const int ELFMAG1             = 'E';  // Magic number byte 1

	const int EI_MAG2             = 2;    // File identification byte 2 index
	const int ELFMAG2             = 'L';  // Magic number byte 2

	const int EI_MAG3             = 3;    // File identification byte 3 index
	const int ELFMAG3             = 'F';  // Magic number byte 3

	const int EI_CLASS            = 4;    // File class byte index
	const int ELFCLASSNONE        = 0;    // Invalid class
	const int ELFCLASS32          = 1;    // 32-bit objects
	const int ELFCLASS64          = 2;    // 64-bit objects

	const int EI_DATA             = 5;    // Data encoding byte index
	const int ELFDATANONE         = 0;    // Invalid data encoding
	const int ELFDATA2LSB         = 1;    // 2's complement, little endian
	const int ELFDATA2MSB         = 2;    // 2's complement, big endian

	const int EI_VERSION          = 6;    // File version byte index

	const int EI_OSABI            = 7;    // OS ABI identification

	const int ELFOSABI_NONE       = 0;   // UNIX System V ABI
	const int ELFOSABI_SYSV       = 0;   // Alias
	const int ELFOSABI_HPUX       = 1;   // HP-UX
	const int ELFOSABI_NETBSD     = 2;   // NetBSD
	const int ELFOSABI_GNU        = 3;   // Object uses GNU ELF extensions.
	const int ELFOSABI_LINUX      = ELFOSABI_GNU; // Compatibility alias.
	const int ELFOSABI_SOLARIS    = 6;   // Sun Solaris.
	const int ELFOSABI_AIX        = 7;   // IBM AIX.
	const int ELFOSABI_IRIX       = 8;   // SGI Irix.
	const int ELFOSABI_FREEBSD    = 9;   // FreeBSD.
	const int ELFOSABI_TRU64      = 10;  // Compaq TRU64 UNIX.
	const int ELFOSABI_MODESTO    = 11;  // Novell Modesto.
	const int ELFOSABI_OPENBSD    = 12;  // OpenBSD.
	const int ELFOSABI_ARM_AEABI  = 64;  // ARM EABI
	const int ELFOSABI_ARM        = 97;  // ARM
	const int ELFOSABI_STANDALONE = 255; // Standalone (embedded) application

	const int EI_ABIVERSION       = 8;   // ABI version byte index

	const int EI_PAD              = 9;   // Byte index of padding bytes

	const int EV_NONE             = 0;   // Invalid ELF version
	const int EV_CURRENT          = 1;   // Current version

	public override (bool accepted, InputReader? reader) Detect (string inputFilePath, InputTypeDetector? parent, ILogger log)
	{
		using var fs = File.OpenRead (inputFilePath);
		using var reader = new BinaryReader (fs);

		var bytes = new byte[EI_INDENT];
		int nread = reader.Read (bytes, 0, bytes.Length);
		if (nread != bytes.Length) {
			return (false, null);
		}

		// We don't check all the fields, just the magic number and a handful of others - we want to know that it is an ELF image and whether it is one we support
		if (bytes[EI_MAG0] != ELFMAG0 || bytes[EI_MAG1] != ELFMAG1 || bytes[EI_MAG2] != ELFMAG2 || bytes[EI_MAG3] != ELFMAG3) {
			log.DebugLine ($"ELF detector: file '{inputFilePath}' does not have a valid ELF magic signature");
			return (false, null);
		}

		if (bytes[EI_VERSION] != EV_CURRENT) {
			log.DebugLine ($"ELF detector: file '{inputFilePath}' has an invalid ELF version ({bytes[EI_VERSION]} instead of {EV_CURRENT})");
			return (false, null);
		}

		if (bytes[EI_OSABI] != ELFOSABI_LINUX && bytes[EI_OSABI] != ELFOSABI_NONE) {
			log.DebugLine ($"ELF detector: file '{inputFilePath}' has unsupported OS ABI ({bytes[EI_OSABI]} instead of '{ELFOSABI_LINUX}')");
			return (false, null);
		}

		return (true, null);
	}
}
