#ifndef __MONODROID_LOGGER_H__
#define __MONODROID_LOGGER_H__

#include "java-interop-logger.h"

#include <array>
#include <cstdint>
#include <cstdio>
#include <memory>
#include <type_traits>

//#define ENABLE_FUNC_ENTER_LEAVE_TRACING

#ifndef ANDROID
typedef enum android_LogPriority {
    ANDROID_LOG_UNKNOWN = 0,
    ANDROID_LOG_DEFAULT,    /* only for SetMinPriority() */
    ANDROID_LOG_VERBOSE,
    ANDROID_LOG_DEBUG,
    ANDROID_LOG_INFO,
    ANDROID_LOG_WARN,
    ANDROID_LOG_ERROR,
    ANDROID_LOG_FATAL,
    ANDROID_LOG_SILENT,     /* only for SetMinPriority(); must be last */
} android_LogPriority;
#endif

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

namespace xamarin::android
{
	class Log final
	{
		static constexpr char LOG_LEVEL_ENVVAR[] = "DEBUG_MONO_LOGLEVEL";

	public:
		static void init () noexcept;

		static LogLevel log_level () noexcept
		{
			return _log_level;
		}

		template<size_t Size>
		static void trace (LogCategories category, const char (&message)[Size]) noexcept
		{
			log_write (category, LogLevel::Verbose, message);
		}

		static void trace_func_enter (LogCategories category, const char *func_name, const char *additional_info = nullptr) noexcept
		{
			if (additional_info == nullptr) {
				log_debug_nocheck (category, "%s ENTER", func_name);
			} else {
				log_debug_nocheck (category, "%S ENTER (%s)", func_name, additional_info);
			}
		}

		static void trace_func_leave (LogCategories category, const char *func_name, const char *file, int line, const char *additional_info = nullptr) noexcept
		{
			log_debug_nocheck (
				category,
				"%s LEAVE at %s:%d%s",
				func_name,
				file,
				line,
				additional_info == nullptr ? "" : additional_info
			);
		}

		static void trace_location (LogCategories category, const char *func_name, const char *file, int line, const char *additional_info = nullptr) noexcept
		{
			log_debug_nocheck (
				category,
				"Location: %s %s:%i%s",
				func_name,
				file,
				line,
				additional_info == nullptr ? "" : additional_info
			);
		}

	private:
		static inline LogLevel _log_level = LogLevel::Info;
	};
}

#if defined (ENABLE_FUNC_ENTER_LEAVE_TRACING)
#define LOG_FUNC_ENTER() xamarin::android::Log::trace_func_enter (LOG_DEFAULT, __PRETTY_FUNCTION__)
#define LOG_FUNC_LEAVE() xamarin::android::Log::trace_func_leave (LOG_DEFAULT, __PRETTY_FUNCTION__, __FILE__, __LINE__)
#define LOG_LOCATION() xamarin::android::Log::trace_location (LOG_DEFAULT, __PRETTY_FUNCTION__, __FILE__, __LINE__)
#else
#define LOG_FUNC_ENTER()
#define LOG_FUNC_LEAVE()
#define LOG_LOCATION()
#endif

#endif
