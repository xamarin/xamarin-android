using System;

namespace Xamarin.Android.Application.Typemaps;

[Flags]
enum AndroidArch
{
	None    = 0x00,

	ARM     = 0x01,
	ARM64   = 0x02,
	X86     = 0x04,
	X86_64  = 0x08,

	All     = ARM | ARM64 | X86 | X86_64,
}
