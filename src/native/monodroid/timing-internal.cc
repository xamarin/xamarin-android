#include "timing-internal.hh"
#include "util.hh"

using namespace xamarin::android;
using namespace xamarin::android::internal;

namespace xamarin::android::internal {
	FastTiming *internal_timing = nullptr;
}

bool FastTiming::is_enabled = false;
bool FastTiming::immediate_logging = false;
TimingEvent FastTiming::init_time {};

void
FastTiming::parse_options (dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> const& value) noexcept
{
	if (value.empty ()) {
		return;
	}

	string_segment param;
	while (value.next_token (',', param)) {
		if (param.starts_with (OPT_MODE)) {
			if (param.equal (OPT_MODE.length (), OPT_MODE_BARE)) {
				profiling_mode = ProfilingMode::Bare;
				continue;
			}

			if (param.equal (OPT_MODE.length (), OPT_MODE_EXTENDED)) {
				profiling_mode = ProfilingMode::Extended;
				continue;
			}

			if (param.equal (OPT_MODE.length (), OPT_MODE_VERBOSE)) {
				profiling_mode = ProfilingMode::Verbose;
				continue;
			}

			if (param.equal (OPT_MODE.length (), OPT_MODE_EXTREME)) {
				profiling_mode = ProfilingMode::Extreme;
				continue;
			}
		}
	}

#if defined(PERFETTO_ENABLED)
	// Clamp the profiling mode to extended, otherwise using Perfetto makes no sense
	if (profiling_mode < ProfilingMode::Extended) {
		profiling_mode = ProfilingMode::Extended;
	}
#endif
}

void
FastTiming::really_initialize (bool log_immediately) noexcept
{
	internal_timing = new FastTiming ();
	is_enabled = true;
	immediate_logging = log_immediately;

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> value;
	int prop_len = 0;

	if constexpr (SharedConstants::PerfettoEnabled) {
		prop_len = AndroidSystem::monodroid_get_system_property (SharedConstants::DEBUG_MONO_PERFETTO, value);
	}

	if (prop_len <= 0) {
		AndroidSystem::monodroid_get_system_property (SharedConstants::DEBUG_MONO_TIMING, value);
	}
	parse_options (value);

	if (immediate_logging) {
		return;
	}

	log_write (LOG_TIMING, LogLevel::Info, "[2/1] To get timing results, send the mono.android.app.DUMP_TIMING_DATA intent to the application");
}

void
FastTiming::dump () noexcept
{
	if (immediate_logging) {
		return;
	}

	StartupAwareLock lock { event_vector_realloc_mutex };
	size_t entries = next_event_index.load ();

	log_write (LOG_TIMING, LogLevel::Info, "[2/2] Performance measurement results");
	if (entries == 0) {
		log_write (LOG_TIMING, LogLevel::Info, "[2/3] No events logged");
		return;
	}

	dynamic_local_string<SharedConstants::MAX_LOGCAT_MESSAGE_LENGTH, char> message;

	// Values are in nanoseconds
	uint64_t total_assembly_load_time = 0;
	uint64_t total_java_to_managed_time = 0;
	uint64_t total_managed_to_java_time = 0;
	uint64_t total_ns;

	format_and_log (init_time, message, total_ns, true /* indent */);
	for (size_t i = 0; i < entries; i++) {
		TimingEvent const& event = events[i];
		format_and_log (event, message, total_ns, true /* indent */);

		switch (event.kind) {
			case TimingEventKind::AssemblyLoad:
				total_assembly_load_time += total_ns;
				break;

			case TimingEventKind::JavaToManaged:
				total_java_to_managed_time += total_ns;
				break;

			case TimingEventKind::ManagedToJava:
				total_managed_to_java_time += total_ns;
				break;

			default:
				// Ignore other kinds
				break;
		}
	}

	uint32_t sec, ms, ns;
	log_write (LOG_TIMING, LogLevel::Info, "[2/4] Accumulated performance results");

	ns_to_time (total_assembly_load_time, sec, ms, ns);
	log_info_nocheck (LOG_TIMING, "  [2/5] Assembly load: %u:%u::%u", sec, ms, ns);

	ns_to_time (total_java_to_managed_time, sec, ms, ns);
	log_info_nocheck (LOG_TIMING, "  [2/6] Java to Managed lookup: %u:%u::%u", sec, ms, ns);

	ns_to_time (total_managed_to_java_time, sec, ms, ns);
	log_info_nocheck (LOG_TIMING, "  [2/7] Managed to Java lookup: %u:%u::%u", sec, ms, ns);
}
