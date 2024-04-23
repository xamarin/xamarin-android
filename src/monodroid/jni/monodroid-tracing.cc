#include <android/log.h>
#include <string>

#include "java-interop-logger.h"
#include "mono/utils/details/mono-dl-fallback-types.h"
#include "monodroid-glue-internal.hh"
#include "native-tracing.hh"
#include "cppcompat.hh"
#include <cpp-util.hh>

using namespace xamarin::android::internal;

static decltype(xa_get_native_backtrace)* _xa_get_native_backtrace;
static decltype(xa_get_managed_backtrace)* _xa_get_managed_backtrace;
static decltype(xa_get_java_backtrace)* _xa_get_java_backtrace;
static decltype(xa_get_interesting_signal_handlers)* _xa_get_interesting_signal_handlers;
static bool tracing_init_done;

static std::mutex tracing_init_lock {};

void
MonodroidRuntime::log_traces (JNIEnv *env, TraceKind kind, const char *first_line) noexcept
{
	if (!tracing_init_done) {
		std::lock_guard lock (tracing_init_lock);

		char *err = nullptr;
		void *handle = monodroid_dlopen (xamarin_native_tracing_name, MONO_DL_EAGER, &err, nullptr);
		if (handle == nullptr) {
			log_warn (LOG_DEFAULT, "Failed to load native tracing library '%s'. %s", xamarin_native_tracing_name, err == nullptr ? "Unknown error" : err);
		} else {
			load_symbol (handle, "xa_get_native_backtrace", _xa_get_native_backtrace);
			load_symbol (handle, "xa_get_managed_backtrace", _xa_get_managed_backtrace);
			load_symbol (handle, "xa_get_java_backtrace", _xa_get_java_backtrace);
			load_symbol (handle, "xa_get_interesting_signal_handlers", _xa_get_interesting_signal_handlers);
		}

		tracing_init_done = true;
	}

	std::string trace;
	if (first_line != nullptr) {
		trace.append (first_line);
		trace.append ("\n");
	}

	bool need_newline = false;
	auto add_trace = [&] (c_unique_ptr<const char> const& data, const char *banner) -> void {
		if (need_newline) {
			trace.append ("\n  ");
		} else {
			trace.append ("  ");
		}

		trace.append (banner);
		if (!data) {
			trace.append (": unavailable");
		} else {
			trace.append (":\n");
			trace.append (data.get ());
			trace.append ("\n");
		}
		need_newline = true;
	};

	if ((kind & TraceKind::Native) == TraceKind::Native) {
		c_unique_ptr<const char> native { _xa_get_native_backtrace != nullptr ? _xa_get_native_backtrace () : nullptr };
		add_trace (native, "Native stacktrace");
	}

	if ((kind & TraceKind::Java) == TraceKind::Java && env != nullptr) {
		c_unique_ptr<const char> java { _xa_get_java_backtrace != nullptr ?_xa_get_java_backtrace (env) : nullptr };
		add_trace (java, "Java stacktrace");
	}

	if ((kind & TraceKind::Managed) == TraceKind::Managed) {
		c_unique_ptr<const char> managed { _xa_get_managed_backtrace != nullptr ? _xa_get_managed_backtrace () : nullptr };
		add_trace (managed, "Managed stacktrace");
	}

	if ((kind & TraceKind::Signals) == TraceKind::Signals) {
		c_unique_ptr<const char> signals { _xa_get_interesting_signal_handlers != nullptr ? _xa_get_interesting_signal_handlers () : nullptr };
		add_trace (signals, "Signal handlers");
	}

	// Use this call because it is slightly faster (doesn't need to parse the format) and it doesn't truncate longer
	// strings (like the stack traces we've just produced), unlike __android_log_vprint used by our `log_*` functions
	__android_log_write (ANDROID_LOG_INFO, SharedConstants::LOG_CATEGORY_NAME_MONODROID, trace.c_str ());
}
