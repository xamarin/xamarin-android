#include <array>
#include <cstdlib>
#include <cstdarg>
#include <strings.h>
#include <cstring>
#include <unistd.h>
#include <cerrno>

#ifdef ANDROID
#include <android/log.h>
#endif

#include "logger.hh"

#include "monodroid.h"
#include "monodroid-glue.hh"
#include "debug.hh"
#include "util.hh"
#include "strings.hh"

#undef DO_LOG
#define DO_LOG(_level_,_category_,_format_,_args_)						                        \
	va_start ((_args_), (_format_));									                        \
	__android_log_vprint ((_level_), CATEGORY_NAME((_category_)), (_format_), (_args_)); \
	va_end ((_args_));

using namespace xamarin::android;
using namespace xamarin::android::internal;

// Must match the same ordering as LogCategories
static constexpr const char* const log_names[] = {
	"*none*",
	"monodroid",
	"monodroid-assembly",
	"monodroid-debug",
	"monodroid-gc",
	"monodroid-gref",
	"monodroid-lref",
	"monodroid-timing",
	"monodroid-bundle",
	"monodroid-network",
	"monodroid-netlink",
	"*error*",
};

#if defined(__i386__) && defined(__GNUC__)
#define ffs(__value__) __builtin_ffs ((__value__))
#elif defined(__x86_64__) && defined(__GNUC__)
#define ffs(__value__) __builtin_ffsll ((__value__))
#endif

// ffs(value) returns index of lowest bit set in `value`
force_inline static const char* CATEGORY_NAME (LogCategories value) noexcept
{
	return value == 0 ? log_names [0] : log_names [ffs (value)];
}

#ifndef ANDROID
static void
__android_log_vprint (int prio, const char* tag, const char* fmt, va_list ap)
{
  printf ("%d [%s] ", prio, tag);
  vprintf (fmt, ap);
  putchar ('\n');
  fflush (stdout);
}

static void
__android_log_write (int prio, const char* tag, const char* message)
{
	printf ("%d [%s] %s\n", prio, tag, message);
}
#endif

unsigned int log_categories = LOG_NONE;
unsigned int log_timing_categories;
int gc_spew_enabled;

static FILE*
open_file (LogCategories category, const char *path, const char *override_dir, const char *filename)
{
	char *p = nullptr;
	FILE *f;

	if (path && access (path, W_OK) < 0) {
		log_warn (category, "Could not open path '%s' for logging (\"%s\"). Using '%s/%s' instead.",
				path, strerror (errno), override_dir, filename);
		path  = nullptr;
	}

	if (!path) {
		Util::create_public_directory (override_dir);
		p     = Util::path_combine (override_dir, filename);
		path  = p;
	}

	unlink (path);

	f = Util::monodroid_fopen (path, "a");

	if (f) {
		Util::set_world_accessable (path);
	} else {
		log_warn (category, "Could not open path '%s' for logging: %s",
				path, strerror (errno));
	}

	free (p);

	return f;
}

static const char *gref_file = nullptr;
static const char *lref_file = nullptr;
static bool light_gref  = false;
static bool light_lref  = false;

void
init_reference_logging (const char *override_dir)
{
	if ((log_categories & LOG_GREF) != 0 && !light_gref) {
		gref_log  = open_file (LOG_GREF, gref_file, override_dir, "grefs.txt");
	}

	if ((log_categories & LOG_LREF) != 0 && !light_lref) {
		// if both lref & gref have files specified, and they're the same path, reuse the FILE*.
		if (lref_file != nullptr && strcmp (lref_file, gref_file != nullptr ? gref_file : "") == 0) {
			lref_log  = gref_log;
		} else {
			lref_log  = open_file (LOG_LREF, lref_file, override_dir, "lrefs.txt");
		}
	}
}

template<size_t NameSize>
force_inline static bool
set_category (const char (&name)[NameSize], string_segment& arg, unsigned int entry, bool arg_starts_with_name = false)
{
	if ((log_categories & entry) == entry) {
		return false;
	}

	if (arg_starts_with_name ? arg.starts_with (name, NameSize - 1) : arg.equal (name, NameSize - 1)) {
		log_categories |= entry;
		return true;
	}

	return false;
}

void
init_logging_categories (char*& mono_log_mask, char*& mono_log_level)
{
	mono_log_mask = nullptr;
	mono_log_level = nullptr;

#if !ANDROID
	log_categories = LOG_DEFAULT | LOG_ASSEMBLY;
#endif
	log_timing_categories = LOG_TIMING_DEFAULT;

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> value;
	if (AndroidSystem::monodroid_get_system_property (Debug::DEBUG_MONO_LOG_PROPERTY, value) == 0)
		return;

	string_segment param;
	while (value.next_token (',', param)) {
		constexpr char CAT_ALL[] = "all";
		constexpr size_t CAT_ALL_SIZE = sizeof(CAT_ALL) - 1;

		if (param.equal (CAT_ALL, CAT_ALL_SIZE)) {
			log_categories = 0xFFFFFFFF;
			break;
		}

		if (set_category ("assembly", param, LOG_ASSEMBLY)) {
			continue;
		}

		if (set_category ("default",  param, LOG_DEFAULT)) {
			continue;
		}

		if (set_category ("debugger", param, LOG_DEBUGGER))  {
			continue;
		}

		if (set_category ("gc",       param, LOG_GC)) {
			continue;
		}

		if (set_category ("gref",     param, LOG_GREF)) {
			continue;
		}

		if (set_category ("lref",     param, LOG_LREF)) {
			continue;
		}

		if (set_category ("timing",   param, LOG_TIMING)) {
			continue;
		}

		if (set_category ("bundle",   param, LOG_BUNDLE)) {
			continue;
		}

		if (set_category ("network",  param, LOG_NET)) {
			continue;
		}

		if (set_category ("netlink",  param, LOG_NETLINK)) {
			continue;
		}

		constexpr char CAT_GREF_EQUALS[] = "gref=";
		constexpr size_t CAT_GREF_EQUALS_LEN = sizeof(CAT_GREF_EQUALS) - 1;
		if (set_category (CAT_GREF_EQUALS, param, LOG_GREF, true /* arg_starts_with_name */)) {
			gref_file = Util::strdup_new (param, CAT_GREF_EQUALS_LEN);
			continue;
		}

		if (set_category ("gref-", param, LOG_GREF)) {
			light_gref = true;
			continue;
		}

		if (set_category ("gref+", param, LOG_GREF)) {
			gref_to_logcat = true;
			continue;
		}

		constexpr char CAT_LREF_EQUALS[] = "lref=";
		constexpr size_t CAT_LREF_EQUALS_LEN = sizeof(CAT_LREF_EQUALS) - 1;
		if (set_category (CAT_LREF_EQUALS, param, LOG_LREF, true /* arg_starts_with_name */)) {
			lref_file = Util::strdup_new (param, CAT_LREF_EQUALS_LEN);
			continue;
		}

		if (set_category ("lref-", param, LOG_LREF)) {
			light_lref = true;
			continue;
		}

		if (set_category ("lref+", param, LOG_LREF)) {
			lref_to_logcat = true;
			continue;
		}

		if (param.starts_with ("timing=fast-bare")) {
			log_categories |= LOG_TIMING;
			log_timing_categories |= LOG_TIMING_FAST_BARE;
			continue;
		}

		if (param.starts_with ("timing=bare")) {
			log_categories |= LOG_TIMING;
			log_timing_categories |= LOG_TIMING_BARE;
			continue;
		}

		constexpr char MONO_LOG_MASK_ARG[] = "mono_log_mask=";
		constexpr size_t MONO_LOG_MASK_ARG_LEN = sizeof(MONO_LOG_MASK_ARG) - 1;
		if (param.starts_with (MONO_LOG_MASK_ARG)) {
			mono_log_mask = Util::strdup_new (param, MONO_LOG_MASK_ARG_LEN);
			continue;
		}

		constexpr char MONO_LOG_LEVEL_ARG[] = "mono_log_level=";
		constexpr size_t MONO_LOG_LEVEL_ARG_LEN = sizeof(MONO_LOG_LEVEL_ARG) - 1;
		if (param.starts_with (MONO_LOG_LEVEL_ARG)) {
			mono_log_level = Util::strdup_new (param, MONO_LOG_LEVEL_ARG_LEN);
			continue;
		}

#if !defined (WINDOWS) && defined (DEBUG)
		constexpr char DEBUGGER_LOG_LEVEL[] = "debugger-log-level=";
		constexpr size_t DEBUGGER_LOG_LEVEL_LEN = sizeof (DEBUGGER_LOG_LEVEL) - 1;
		if (param.starts_with (DEBUGGER_LOG_LEVEL)) {
			dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> level;
			level.assign (param.start () + DEBUGGER_LOG_LEVEL_LEN, param.length () - DEBUGGER_LOG_LEVEL_LEN);
			Debug::set_debugger_log_level (level.get ());
		}
#endif
	}

#if DEBUG
	if ((log_categories & LOG_GC) != 0)
		gc_spew_enabled = 1;
#endif  /* DEBUG */
}

void
log_error (LogCategories category, const char *format, ...)
{
	va_list args;

	DO_LOG (ANDROID_LOG_ERROR, category, format, args);
}

void
log_fatal (LogCategories category, const char *format, ...)
{
	va_list args;

	DO_LOG (ANDROID_LOG_FATAL, category, format, args);
}

void
log_info_nocheck (LogCategories category, const char *format, ...)
{
	va_list args;

	if ((log_categories & category) == 0)
		return;

	DO_LOG (ANDROID_LOG_INFO, category, format, args);
}

void
log_warn (LogCategories category, const char *format, ...)
{
	va_list args;

	DO_LOG (ANDROID_LOG_WARN, category, format, args);
}

void
log_debug_nocheck (LogCategories category, const char *format, ...)
{
	va_list args;

	if ((log_categories & category) == 0)
		return;

	DO_LOG (ANDROID_LOG_DEBUG, category, format, args);
}

constexpr android_LogPriority DEFAULT_PRIORITY = ANDROID_LOG_INFO;

// relies on the fact that the LogLevel enum has sequential values
static constexpr android_LogPriority loglevel_map[] = {
	DEFAULT_PRIORITY, // Unknown
	DEFAULT_PRIORITY, // Default
	ANDROID_LOG_VERBOSE, // Verbose
	ANDROID_LOG_DEBUG, // Debug
	ANDROID_LOG_INFO, // Info
	ANDROID_LOG_WARN, // Warn
	ANDROID_LOG_ERROR, // Error
	ANDROID_LOG_FATAL, // Fatal
	ANDROID_LOG_SILENT, // Silent
};

static constexpr size_t loglevel_map_max_index = (sizeof(loglevel_map) / sizeof(android_LogPriority)) - 1;

void
log_write (LogCategories category, LogLevel level, const char *message) noexcept
{
	auto map_index = static_cast<size_t>(level);
	android_LogPriority priority;

	if (map_index > loglevel_map_max_index) {
		priority = DEFAULT_PRIORITY;
	} else {
		priority = loglevel_map[map_index];
	}

	__android_log_write (priority, CATEGORY_NAME (category), message);
}

void
Log::init () noexcept
{
	const char *envvar = getenv (LOG_LEVEL_ENVVAR);
	if (envvar == nullptr) {
		return;
	}

	dynamic_local_string<SharedConstants::SENSIBLE_LOG_LEVEL_ENVVAR_SIZE> level;
	level.assign_c (envvar);

	// TODO: switch to https://github.com/Neargye/magic_enum in a separate PR
	constexpr char LEVEL_VERBOSE[] = "verbose";
	constexpr char LEVEL_DEBUG[] = "debug";
	constexpr char LEVEL_INFO[] = "info";
	constexpr char LEVEL_WARN[] = "warn";
	constexpr char LEVEL_ERROR[] = "error";
	constexpr char LEVEL_FATAL[] = "fatal";
	constexpr char LEVEL_SILENT[] = "silent";

	if (level.equals (LEVEL_VERBOSE)) {
		_log_level = LogLevel::Verbose;
	} else if (level.equals (LEVEL_DEBUG)) {
		_log_level = LogLevel::Debug;
	} else if (level.equals (LEVEL_INFO)) {
		_log_level = LogLevel::Info;
	} else if (level.equals (LEVEL_WARN)) {
		_log_level = LogLevel::Warn;
	} else if (level.equals (LEVEL_ERROR)) {
		_log_level = LogLevel::Error;
	} else if (level.equals (LEVEL_FATAL)) {
		_log_level = LogLevel::Fatal;
	} else if (level.equals (LEVEL_SILENT)) {
		_log_level = LogLevel::Silent;
	}
}
