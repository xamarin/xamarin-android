#ifndef __CPU_ARCH_H
#define __CPU_ARCH_H

#include <monodroid.h>

constexpr uint16_t CPU_KIND_UNKNOWN = 0;
constexpr uint16_t CPU_KIND_ARM     = 1;
constexpr uint16_t CPU_KIND_ARM64   = 2;
constexpr uint16_t CPU_KIND_MIPS    = 3;
constexpr uint16_t CPU_KIND_X86     = 4;
constexpr uint16_t CPU_KIND_X86_64  = 5;
constexpr size_t   CPU_KIND_COUNT   = CPU_KIND_X86_64 + 1;

namespace xamarin::android::internal
{
	class BuiltForCpu final
	{
	private:
#if defined (ANDROID) || defined (__linux) || defined (__linux__)
		static constexpr uint16_t __get_cpu () noexcept
		{
#if __arm__
			return CPU_KIND_ARM;
#elif __aarch64__
			return CPU_KIND_ARM64;
#elif __x86_64__
			return CPU_KIND_X86_64;
#elif __i386__
			return CPU_KIND_X86;
#elif __mips__
			return CPU_KIND_MIPS;
#else
			return CPU_KIND_UNKNOWN;
#endif
		}
#elif defined (__APPLE__)
		constexpr static uint16_t __get_cpu () noexcept
		{
#if __x86_64__
			return CPU_KIND_X86_64;
#elif __i386__
			return CPU_KIND_X86;
#elif __aarch64__
			return CPU_KIND_ARM64;
#else
			return CPU_KIND_UNKNOWN;
#endif
		}
#elif defined (_WIN32)
		constexpr static uint16_t __get_cpu () noexcept
		{
#if _M_AMD64 || _M_X64
			return CPU_KIND_X86_64;
#elif _M_IX86
			return CPU_KIND_X86;
#elif _M_ARM
			return CPU_KIND_ARM;
#else
			return CPU_KIND_UNKNOWN;
#endif
		}
#endif

	public:
		static constexpr uint16_t cpu () noexcept
		{
			return __get_cpu ();
		}

		static constexpr bool is_64_bit () noexcept
		{
			return sizeof (char*) == 8;
		}
	};
}

#if !defined(NET)
MONO_API
#endif // def NET
void _monodroid_detect_cpu_and_architecture (unsigned short *built_for_cpu, unsigned short *running_on_cpu, unsigned char *is64bit);
void _monodroid_detect_running_cpu (unsigned short *running_on_cpu);
#endif // ndef NET
