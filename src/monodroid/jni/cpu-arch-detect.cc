#include <cstdio>
#include <cstring>

#if __APPLE__
#include <sys/types.h>
#include <sys/sysctl.h>
#include <mach/machine.h>
#elif _WIN32
#include <windows.h>
#endif

#include "cpp-util.hh"
#include "cpu-arch.hh"

using namespace xamarin::android::internal;

#if __ANDROID__

#if __arm__
constexpr size_t BUF_SIZE = 512;

static int
find_in_maps (const char *str)
{
	abort_if_invalid_pointer_argument (str);

	FILE *maps = fopen ("/proc/self/maps", "r");
	char *line;
	char  buf [BUF_SIZE];

	if (!maps)
		return -1;

	while ((line = fgets (buf, BUF_SIZE, maps))) {
		if (strstr (line, str))
			return 1;
	}

	return 0;
}

static int
detect_houdini ()
{
	return find_in_maps ("libhoudini");
}
#endif
#endif // __ANDROID__

static int
get_running_on_cpu_windows ([[maybe_unused]] unsigned short *running_on_cpu)
{
#if _WIN32
	SYSTEM_INFO si;

	GetSystemInfo (&si);
	switch (si.wProcessorArchitecture) {
		case PROCESSOR_ARCHITECTURE_AMD64:
			*running_on_cpu = CPU_KIND_X86_64;
			break;

		case PROCESSOR_ARCHITECTURE_ARM:
			*running_on_cpu = CPU_KIND_ARM;
			break;

		case PROCESSOR_ARCHITECTURE_INTEL:
			*running_on_cpu = CPU_KIND_X86;
			break;

		default:
			*running_on_cpu = CPU_KIND_UNKNOWN;
			break;
	}

	return 1;
#else
	return 0;
#endif
}

static int
get_running_on_cpu_apple ([[maybe_unused]] unsigned short *running_on_cpu)
{
#if __APPLE__
	cpu_type_t cputype;
	size_t length;

	length = sizeof (cputype);
	sysctlbyname ("hw.cputype", &cputype, &length, nullptr, 0);
	switch (cputype) {
		case CPU_TYPE_X86:
			*running_on_cpu = CPU_KIND_X86;
			break;

		case CPU_TYPE_X86_64:
			*running_on_cpu = CPU_KIND_X86_64;
			break;

		default:
			*running_on_cpu = CPU_KIND_UNKNOWN;
			break;
	}

	return 1;
#else
	return 0;
#endif
}

static bool
get_running_on_cpu_android ([[maybe_unused]] unsigned short *running_on_cpu)
{
	bool retval = true;

#if __arm__
	if (!detect_houdini ()) {
		*running_on_cpu = CPU_KIND_ARM;
	} else {
		/* If houdini is mapped in we're running on x86 */
		*running_on_cpu = CPU_KIND_X86;
	}
#elif __aarch64__
	*running_on_cpu = CPU_KIND_ARM64;
#elif __x86_64__
	*running_on_cpu = CPU_KIND_X86_64;
#elif __i386__
	*running_on_cpu = BuiltForCpu::is_64_bit () ? CPU_KIND_X86_64 : CPU_KIND_X86;
#elif __mips__
	*running_on_cpu = CPU_KIND_MIPS;
#else
	retval = false;
#endif

	return retval;
}

static void
get_running_on_cpu (unsigned short *running_on_cpu)
{
	if (get_running_on_cpu_android (running_on_cpu))
		return;

	if (get_running_on_cpu_windows (running_on_cpu))
		return;

	if (get_running_on_cpu_apple (running_on_cpu))
		return;

	*running_on_cpu = CPU_KIND_UNKNOWN;
}

void
_monodroid_detect_running_cpu (unsigned short *running_on_cpu)
{
	abort_if_invalid_pointer_argument (running_on_cpu);
	get_running_on_cpu (running_on_cpu);
}

void
_monodroid_detect_cpu_and_architecture (unsigned short *built_for_cpu, unsigned short *running_on_cpu, unsigned char *is64bit)
{
	abort_if_invalid_pointer_argument (built_for_cpu);
	abort_if_invalid_pointer_argument (is64bit);

	_monodroid_detect_running_cpu (running_on_cpu);
	*built_for_cpu = BuiltForCpu::cpu ();
	*is64bit = BuiltForCpu::is_64_bit ();
}
