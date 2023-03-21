#include <cstdio>
#include <cstring>

#include "cpp-util.hh"
#include "cpu-arch.hh"

#define BUF_SIZE 512

#if __arm__
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

static unsigned char
is_64_bit ()
{
	return sizeof (char*) == 8;
}

static int
get_built_for_cpu_android ([[maybe_unused]] unsigned short *built_for_cpu)
{
	int retval = 1;

#if __arm__
	*built_for_cpu = CPU_KIND_ARM;
#elif __aarch64__
	*built_for_cpu = CPU_KIND_ARM64;
#elif __x86_64__
	*built_for_cpu = CPU_KIND_X86_64;
#elif __i386__
	*built_for_cpu = CPU_KIND_X86;
#elif __mips__
	*built_for_cpu = CPU_KIND_MIPS;
#else
	retval = 0;
#endif

	return retval;
}

static void
get_built_for_cpu (unsigned short *built_for_cpu)
{
	if (get_built_for_cpu_android (built_for_cpu))
		return;

	*built_for_cpu = CPU_KIND_UNKNOWN;
}

static int
get_running_on_cpu_android ([[maybe_unused]] unsigned short *running_on_cpu)
{
	int retval = 1;

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
	*running_on_cpu = is_64_bit () ? CPU_KIND_X86_64 : CPU_KIND_X86;
#elif __mips__
	*running_on_cpu = CPU_KIND_MIPS;
#else
	retval = 0;
#endif

	return retval = 1;
}

static void
get_running_on_cpu (unsigned short *running_on_cpu)
{
	if (get_running_on_cpu_android (running_on_cpu))
		return;

	*running_on_cpu = CPU_KIND_UNKNOWN;
}

void
_monodroid_detect_cpu_and_architecture (unsigned short *built_for_cpu, unsigned short *running_on_cpu, unsigned char *is64bit)
{
	abort_if_invalid_pointer_argument (built_for_cpu);
	abort_if_invalid_pointer_argument (running_on_cpu);
	abort_if_invalid_pointer_argument (is64bit);

	*is64bit = is_64_bit ();
	get_built_for_cpu (built_for_cpu);
	get_running_on_cpu (running_on_cpu);
}
