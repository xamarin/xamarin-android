#include "debug.hh"
#include "strings.hh"
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
	if (androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_TIMING, value) != 0) {
		parse_options (value);
	}

	if (immediate_logging) {
		return;
	}

	log_info (LOG_TIMING, "[%s] To get timing results, send the mono.android.app.DUMP_TIMING_DATA intent to the application", DUMP_STAGE_INIT_TAG.data ());
}

void
FastTiming::parse_options (dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> const& value) noexcept
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

			if (param.equal (OPT_MODE.length (), OPT_MODE_EXTREME)) {
				timing_mode = TimingMode::Extreme;
				continue;
			}
			continue;
		}

		if (param.equal (OPT_TO_FILE)) {
			log_to_file = true;
			continue;
		}

		if (param.starts_with (OPT_FILE_NAME)) {
			output_file_name = new std::string (param.start () + OPT_FILE_NAME.length (), param.length () - OPT_FILE_NAME.length ());
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

force_inline bool
FastTiming::no_events_logged (size_t entries) noexcept
{
	if (entries > 0) {
		return false;
	}

	log_info_nocheck (LOG_TIMING, "[%s] No events logged", DUMP_STAGE_NO_EVENTS_TAG.data ());
	return true;
}

template<
	size_t BufferSize,
	void(line_writer)(dynamic_local_string<BufferSize, char> const& buffer),
	void(event_writer)(TimingEvent const& event, dynamic_local_string<BufferSize, char>& buffer, uint64_t& total_ns, bool indent)
>
force_inline void
FastTiming::dump (size_t entries, bool indent) noexcept
{
	dynamic_local_string<BufferSize, char> message;

	// Values are in nanoseconds, we don't need to worry about overflow for our needs, when accumulating.
	uint64_t total_assembly_load_time = 0;
	uint64_t total_java_to_managed_time = 0;
	uint64_t total_managed_to_java_time = 0;
	uint64_t total_ns; // initialized by `calculate_interval`, via `event_writer` and the `format_*` functions

	format_message (init_time, message, total_ns, indent);
	line_writer (message);

	for (size_t i = 0; i < entries; i++) {
		TimingEvent const& event = events[i];
		event_writer (event, message, total_ns, indent);

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

	message.clear ();
	message.append ("[")
		.append (DUMP_STAGE_RESULTS_TAG)
		.append ("]")
		.append (" Accumulated performance results");

	auto make_message = [](dynamic_local_string<BufferSize> &buf, std::string_view const& tag, std::string_view const& label, uint32_t sec, uint32_t ms, uint32_t ns) -> void {
		buf.clear ();

		buf.append ("  [")
			.append (tag)
			.append ("] ")
			.append (label)
			.append (": ")
			.append (sec)
			.append (":")
			.append (ms)
			.append ("::")
			.append (ns);
	};

	message.clear ();
	message.append ("[")
		.append (DUMP_STAGE_ACCUMULATED_RESULTS_TAG)
		.append ("] ")
		.append ("Accumulated performance results");
	line_writer (message);

	uint32_t sec, ms, ns;
	ns_to_time (total_assembly_load_time, sec, ms, ns);
	make_message (message, DUMP_STAGE_ACC_ASSEMBLY_LOAD_TAG, "Assembly load", sec, ms, ns);
	line_writer (message);

	ns_to_time (total_java_to_managed_time, sec, ms, ns);
	make_message (message, DUMP_STAGE_ACC_JAVA_TO_MANAGED_TAG, "Java to Managed lookup", sec, ms, ns);
	line_writer (message);

	ns_to_time (total_managed_to_java_time, sec, ms, ns);
	make_message (message, DUMP_STAGE_ACC_MANAGED_TO_JAVA_TAG, "Managed to Java lookup", sec, ms, ns);
	line_writer (message);
}

void
FastTiming::dump_to_logcat (size_t entries) noexcept
{
	log_info_nocheck (LOG_TIMING, "[%s] Performance measurement results", DUMP_STAGE_RESULTS_TAG.data ());
	if (no_events_logged (entries)) {
		return;
	}

	const size_t BufferSize = SharedConstants::MAX_LOGCAT_MESSAGE_LENGTH;
	using TBuffer = dynamic_local_string<BufferSize, char>;
	TBuffer message;

	auto event_writer = [](TimingEvent const& event, TBuffer& buffer, uint64_t& total_ns, bool indent) {
		format_and_log (event, buffer, total_ns, indent);
	};

	auto line_writer = [](TBuffer const& buffer) {
		log_info_nocheck (LOG_TIMING, buffer.get ());
	};

	dump<BufferSize, line_writer, event_writer> (entries, true /* indent */);
}

void
FastTiming::dump_to_file (size_t entries) noexcept
{
	if (no_events_logged (entries)) {
		return;
	}

	dynamic_local_string<SENSIBLE_PATH_MAX> timing_log_path;
	timing_log_path.assign_c (androidSystem.override_dirs[0]);
	timing_log_path.append (output_file_name == nullptr ? default_timing_file_name : *output_file_name);
	utils.create_directory (AndroidSystem::override_dirs [0], 0755);

	FILE *timing_log = utils.monodroid_fopen (timing_log_path.get (), "a");
	if (timing_log == nullptr) {
		log_error (LOG_TIMING, "[%s] Unable to create the performance measurements file '%s'", DUMP_STAGE_RESULTS_TAG.data (), timing_log_path.get ());
		return;
	}

	if (!utils.set_world_accessible (fileno (timing_log))) {
		log_warn (LOG_TIMING, "[%s] Failed to make performance measurements file '%s' world-readable", DUMP_STAGE_RESULTS_TAG.data (), timing_log_path.get ());
	}

	log_info (LOG_TIMING, "[%s] Performance measurement results logged to file: %s", DUMP_STAGE_RESULTS_TAG.data (), timing_log_path.get ());
	// TODO: implement
}

void
FastTiming::dump () noexcept
{
	// TODO: measure (average over, say, 10 calls) and log the following:
	//   * timing_profiler_state->add_sequence
	//   * timing_profiler_state->get_sequence
	//
	// This will allow to subtract these values from various measurements
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
