#include "timing-internal.hh"
#include "util.hh"

using namespace xamarin::android;
using namespace xamarin::android::internal;

namespace xamarin::android::internal {
	FastTiming *internal_timing = nullptr;
}

TimingEvent FastTiming::init_time {};

void
FastTiming::really_initialize (bool log_immediately) noexcept
{
	internal_timing = new FastTiming ();
	is_enabled = true;
	immediate_logging = log_immediately;

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> value;
	if (androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_LOG_PROPERTY, value) != 0) {
	}

	if (immediate_logging) {
		return;
	}

	log_write (LOG_TIMING, LogLevel::Info, "[2/1] To get timing results, send the mono.android.app.DUMP_TIMING_DATA intent to the application");
}

void FastTiming::parse_options (dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> const& value) noexcept
{
	if (value.length () == 0) {
		return;
	}

	string_segment param;
	while (value.next_token (',', param)) {
		if (param.equal (OPT_FAST)) {
			immediate_logging = true;
			continue;
		}

		if (param.starts_with (OPT_MODE)) {
			if (param.equal (OPT_MODE.length (), OPT_MODE_BARE)) {
				timing_mode = TimingMode::Bare;
				continue;
			}

			if (param.equal (OPT_MODE.length (), OPT_MODE_EXTENDED)) {
				timing_mode = TimingMode::Extended;
				continue;
			}

			if (param.equal (OPT_MODE.length (), OPT_MODE_VERBOSE)) {
				timing_mode = TimingMode::Verbose;
				continue;
			}

			log_warn (LOG_TIMING, "Unsupported timing mode '%s'", param.start ());
			continue;
		}

		if (param.equal (OPT_TO_FILE)) {
			log_to_file = true;
			continue;
		}

		if (param.starts_with (OPT_FILE_NAME)) {
			output_file_name = utils.strdup_new (param.start () + OPT_FILE_NAME.length (), param.length () - OPT_FILE_NAME.length ());
			continue;
		}

		if (param.starts_with (OPT_DURATION)) {
			if (!param.to_integer (duration_ms, OPT_DURATION.length ())) {
				log_warn (LOG_TIMING, "Failed to parse duration in milliseconds from '%s'", param.start ());
				duration_ms = default_duration_milliseconds;
			}
			continue;
		}
	}

	if (output_file_name != nullptr) {
		log_to_file = true;
	}

	// If logging to file is requested, turn off immediate logging.
	if (log_to_file) {
		immediate_logging = false;
	}
}

void
FastTiming::dump_to_logcat (size_t entries) noexcept
{
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

void
FastTiming::dump_to_file (size_t entries) noexcept
{
	std::unique_ptr<char> timing_log_path {
		utils.path_combine (
			androidSystem.get_override_dir (0),
			output_file_name == nullptr ? default_timing_file_name.data () : output_file_name
		)
	};

	log_info (LOG_TIMING, "[2/2] Performance measurement results logged to file: %s", timing_log_path.get ());
	if (entries == 0) {
		log_write (LOG_TIMING, LogLevel::Info, "[2/3] No events logged");
		return;
	}

	// TODO: implement
}

void
FastTiming::dump () noexcept
{
	if (immediate_logging) {
		return;
	}

	StartupAwareLock lock { event_vector_realloc_mutex };
	size_t entries = next_event_index.load ();

	if (log_to_file) {
		dump_to_file (entries);
	} else {
		dump_to_logcat (entries);
	}
}
