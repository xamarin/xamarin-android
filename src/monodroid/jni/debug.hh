// This is a -*- C++ -*- header
#ifndef __MONODROID_DEBUG_H__
#define __MONODROID_DEBUG_H__

#include <cstdint>
#include <string_view>

#include <pthread.h>
#include <sys/time.h>

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
		static inline constexpr std::string_view INITIALIZER_NAME { "mono_profiler_init" };

	public:
		/* Android property containing connection information, set by XS */
		static inline constexpr std::string_view DEBUG_MONO_CONNECT_PROPERTY      { "debug.mono.connect" };
		static inline constexpr std::string_view DEBUG_MONO_DEBUG_PROPERTY        { "debug.mono.debug" };
		static inline constexpr std::string_view DEBUG_MONO_ENV_PROPERTY          { "debug.mono.env" };
		static inline constexpr std::string_view DEBUG_MONO_EXTRA_PROPERTY        { "debug.mono.extra" };
		static inline constexpr std::string_view DEBUG_MONO_GC_PROPERTY           { "debug.mono.gc" };
		static inline constexpr std::string_view DEBUG_MONO_GDB_PROPERTY          { "debug.mono.gdb" };
		static inline constexpr std::string_view DEBUG_MONO_LOG_PROPERTY          { "debug.mono.log" };
		static inline constexpr std::string_view DEBUG_MONO_MAX_GREFC             { "debug.mono.max_grefc" };
		static inline constexpr std::string_view DEBUG_MONO_PROFILE_PROPERTY      { "debug.mono.profile" };
		static inline constexpr std::string_view DEBUG_MONO_RUNTIME_ARGS_PROPERTY { "debug.mono.runtime_args" };
		static inline constexpr std::string_view DEBUG_MONO_SOFT_BREAKPOINTS      { "debug.mono.soft_breakpoints" };
		static inline constexpr std::string_view DEBUG_MONO_TRACE_PROPERTY        { "debug.mono.trace" };
		static inline constexpr std::string_view DEBUG_MONO_TIMING                { "debug.mono.timing" };
		static inline constexpr std::string_view DEBUG_MONO_WREF_PROPERTY         { "debug.mono.wref" };

	public:
		explicit Debug ()
		{}

		void monodroid_profiler_load (const char *libmono_path, const char *desc, const char *logfile);

	private:
		bool load_profiler (void *handle, const char *desc, const char *symbol);
		bool load_profiler_from_handle (void *dso_handle, const char *desc, const char *name);

#if defined (DEBUG)
	public:
		bool         enable_soft_breakpoints ();
		void         start_debugging_and_profiling ();
		void         set_debugger_log_level (const char *level);

		bool         have_debugger_log_level () const
		{
			return got_debugger_log_level;
		}

		int          get_debugger_log_level () const
		{
			return debugger_log_level;
		}

	private:
		DebuggerConnectionStatus start_connection (char *options);
		void         parse_options (char *options, ConnOptions *opts);
		bool         process_connection (int fd);
		int          handle_server_connection (void);
		bool         process_cmd (int fd, char *cmd);
		void         start_debugging ();
		void         start_profiling ();

		friend void* conn_thread (void *arg);

	private:
		pthread_mutex_t  process_cmd_mutex = PTHREAD_MUTEX_INITIALIZER;
		pthread_cond_t   process_cmd_cond  = PTHREAD_COND_INITIALIZER;
		uint16_t         conn_port = 0;
		pthread_t        conn_thread_id = 0;
		bool             debugging_configured;
		int              sdb_fd;
		bool             profiler_configured;
		int              profiler_fd;
		char            *profiler_description;
		bool             config_timedout;
		timeval          wait_tv;
		timespec         wait_ts;
		bool             got_debugger_log_level = false;
		int              debugger_log_level = 0;
#endif // def DEBUG
	};
}
#else // __cplusplus
const char *__get_debug_mono_log_property (void);

#ifndef DEBUG_MONO_LOG_PROPERTY
#define DEBUG_MONO_LOG_PROPERTY __get_debug_mono_log_property ()
#endif

#endif // __cplusplus

#endif /* __MONODROID_DEBUG_H__ */
