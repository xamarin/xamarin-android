#ifndef __MONODROID_LOGGER_H__
#define __MONODROID_LOGGER_H__

#include "java-interop-logger.h"

void init_logging_categories (char*& mono_log_mask, char*& mono_log_level);

void init_reference_logging (const char *override_dir);

typedef enum _LogTimingCategories {
	LOG_TIMING_DEFAULT = 0,
	LOG_TIMING_BARE = 1 << 0,
	LOG_TIMING_FAST_BARE = 1 << 1,
} LogTimingCategories;

extern unsigned int log_timing_categories;

#if DEBUG
extern int gc_spew_enabled;
#endif

// Keep in sync with LogLevel defined in JNIEnv.cs
enum class LogLevel : unsigned int
{
	Unknown = 0x00,
	Default = 0x01,
	Verbose = 0x02,
	Debug   = 0x03,
	Info    = 0x04,
	Warn    = 0x05,
	Error   = 0x06,
	Fatal   = 0x07,
	Silent  = 0x08
};

// A slightly faster alternative to other log functions as it doesn't parse the message
// for format placeholders nor it uses variable arguments
void log_write (LogCategories category, LogLevel level, const char *message) noexcept;
#endif
