// This is a -*- C++ -*- header
#ifndef __MONODROID_DEBUG_H__
#define __MONODROID_DEBUG_H__

#include <cstdint>
#include <pthread.h>
#include <sys/time.h>

#include "android-system.hh"
#include "strings.hh"

#ifdef __cplusplus
namespace xamarin::android
{
	enum class DebuggerConnectionStatus : int
	{
		Connected = 1,
		Unconnected = 0,
		Error = -1,
	};

	class Debug
	{
	private:
		struct ConnOptions
		{
			int64_t timeout_time;
		};

	private:
		static constexpr char INITIALIZER_NAME[] = "mono_profiler_init";

	public:
		/* Android property containing connection information, set by XS */
		static constexpr char DEBUG_MONO_CONNECT_PROPERTY[]      = "debug.mono.connect";
		static constexpr char DEBUG_MONO_DEBUG_PROPERTY[]        = "debug.mono.debug";
		static constexpr char DEBUG_MONO_ENV_PROPERTY[]          = "debug.mono.env";
		static constexpr char DEBUG_MONO_EXTRA_PROPERTY[]        = "debug.mono.extra";
		static constexpr char DEBUG_MONO_GC_PROPERTY[]           = "debug.mono.gc";
		static constexpr char DEBUG_MONO_GDB_PROPERTY[]          = "debug.mono.gdb";
		static constexpr char DEBUG_MONO_LOG_PROPERTY[]          = "debug.mono.log";
		static constexpr char DEBUG_MONO_MAX_GREFC[]             = "debug.mono.max_grefc";
		static constexpr char DEBUG_MONO_PROFILE_PROPERTY[]      = "debug.mono.profile";
		static constexpr char DEBUG_MONO_RUNTIME_ARGS_PROPERTY[] = "debug.mono.runtime_args";
		static constexpr char DEBUG_MONO_SOFT_BREAKPOINTS[]      = "debug.mono.soft_breakpoints";
		static constexpr char DEBUG_MONO_TRACE_PROPERTY[]        = "debug.mono.trace";
		static constexpr char DEBUG_MONO_WREF_PROPERTY[]         = "debug.mono.wref";

	public:
		explicit Debug ()
		{}

		static void monodroid_profiler_load (const char *libmono_path, const char *desc, const char *logfile) noexcept;

	private:
		static bool load_profiler (void *handle, const char *desc, const char *symbol) noexcept;
		static bool load_profiler_from_handle (void *dso_handle, const char *desc, const char *name) noexcept;

#if !defined (WINDOWS) && defined (DEBUG)
	public:
		static bool         enable_soft_breakpoints () noexcept;
		static void         start_debugging_and_profiling () noexcept;
		static void         set_debugger_log_level (const char *level) noexcept;

		static bool         have_debugger_log_level () noexcept
		{
			return got_debugger_log_level;
		}

		static int          get_debugger_log_level () noexcept
		{
			return debugger_log_level;
		}

	private:
		static DebuggerConnectionStatus start_connection (internal::dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> const& options) noexcept;
		static void         parse_options (internal::dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> const& options, ConnOptions *opts) noexcept;
		static bool         process_connection (int fd) noexcept;
		static int          handle_server_connection (void) noexcept;
		static bool         process_cmd (int fd, char *cmd) noexcept;
		static void         start_debugging () noexcept;
		static void         start_profiling () noexcept;

		friend void* conn_thread (void *arg) noexcept;

	private:
		static inline pthread_mutex_t  process_cmd_mutex = PTHREAD_MUTEX_INITIALIZER;
		static inline pthread_cond_t   process_cmd_cond  = PTHREAD_COND_INITIALIZER;
		static inline uint16_t         conn_port = 0;
		static inline pthread_t        conn_thread_id = 0;
		static inline bool             debugging_configured = false;
		static inline int              sdb_fd = -1;
		static inline bool             profiler_configured = false;
		static inline int              profiler_fd = false;
		static inline char            *profiler_description = nullptr;
		static inline bool             config_timedout = false;
		static inline timeval          wait_tv{};
		static inline timespec         wait_ts{};
		static inline bool             got_debugger_log_level = false;
		static inline int              debugger_log_level = 0;
#endif
	};
}
#else // __cplusplus
const char *__get_debug_mono_log_property (void);

#ifndef DEBUG_MONO_LOG_PROPERTY
#define DEBUG_MONO_LOG_PROPERTY __get_debug_mono_log_property ()
#endif

#endif // __cplusplus

#endif /* __MONODROID_DEBUG_H__ */
