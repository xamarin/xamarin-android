#ifndef __CPU_ARCH_H
#define __CPU_ARCH_H

#include <monodroid.h>

#define CPU_KIND_UNKNOWN ((unsigned short)0)
#define CPU_KIND_ARM     ((unsigned short)1)
#define CPU_KIND_ARM64   ((unsigned short)2)
#define CPU_KIND_MIPS    ((unsigned short)3)
#define CPU_KIND_X86     ((unsigned short)4)
#define CPU_KIND_X86_64  ((unsigned short)5)

void _monodroid_detect_cpu_and_architecture (unsigned short *built_for_cpu, unsigned short *running_on_cpu, unsigned char *is64bit);
#endif // ndef NET
