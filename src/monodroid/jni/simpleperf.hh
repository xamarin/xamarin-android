//
// SPDX-License-Identifier: MIT
// SPDX-License-Identifier: Apache-2.0
//
// Support for managing simpleperf session from within the runtime
//
// Heavily based on https://android.googlesource.com/platform/system/extras/+/refs/tags/android-13.0.0_r11/simpleperf/app_api/cpp/simpleperf.cpp
//
// Because the original code is licensed under the `Apache-2.0` license, this file is dual-licensed under the `MIT` and
// `Apache-2.0` licenses
//
// We can't use the original source because of the C++ stdlib features it uses (I/O streams which we can't use because
// we don't reference libc++)
//
// The API is very similar to the original, with occasional stylistic changes and some behavioral changes (for instance,
// we do not abort the process if tracing fails - instead we log errors and continue running).  Portions are
// reimplemented in a way that better fits Xamarin.Android's purposes. We also don't split the classes into interface
// and implementation bits - there's no need since we use this API entirely internally.
//
// Stylistic changes include indentation and renaming of all methods to use lower-case words separated by underscores instead of
// camel case (because that's the style Xamarin.Android sources use).  Names are otherwise the same as in the original
// code, for easier porting of potential changes.
//
#if !defined (__SIMPLEPERF_HH)
#define __SIMPLEPERF_HH

#include <string>
#include <vector>

#include "cppcompat.hh"

namespace xamarin::android::internal
{
	enum class RecordCmd
	{
		CMD_PAUSE_RECORDING = 1,
		CMD_RESUME_RECORDING,
	};

	/**
	 * RecordOptions sets record options used by ProfileSession. The options are
	 * converted to a string list in toRecordArgs(), which is then passed to
	 * `simpleperf record` cmd. Run `simpleperf record -h` or
	 * `run_simpleperf_on_device.py record -h` for help messages.
	 *
	 * Example:
	 *   RecordOptions options;
	 *   options.set_duration (3).record_dwarf_call_graph ().set_output_filename ("perf.data");
	 *   ProfileSession session;
	 *   session.start_recording (options);
	 */
	class RecordOptions final
	{
	public:
		/**
		 * Set output filename. Default is perf-<month>-<day>-<hour>-<minute>-<second>.data.
		 * The file will be generated under simpleperf_data/.
		 */
		RecordOptions& set_output_filename (std::string const& filename) noexcept
		{
			output_filename = filename;
			return *this;
		}

		/**
		 * Set event to record. Default is cpu-cycles. See `simpleperf list` for all available events.
		 */
		RecordOptions& set_event (std::string const& wanted_event) noexcept
		{
			event = wanted_event;
			return *this;
		}

		/**
		 * Set how many samples to generate each second running. Default is 4000.
		 */
		RecordOptions& set_sample_frequency (size_t wanted_freq) noexcept
		{
			freq = wanted_freq;
			return *this;
		}

		/**
		 * Set record duration. The record stops after `durationInSecond` seconds. By default,
		 * record stops only when stopRecording() is called.
		 */
		RecordOptions& set_duration (double wanted_duration_in_seconds) noexcept
		{
			duration_in_seconds = wanted_duration_in_seconds;
			return *this;
		}

		/**
		 * Record some threads in the app process. By default, record all threads in the process.
		 */
		RecordOptions& set_sample_threads (std::vector<pid_t> const& wanted_threads) noexcept
		{
			threads = wanted_threads;
			return *this;
		}

		/**
		 * Record dwarf based call graph. It is needed to get Java callstacks.
		 */
		RecordOptions& record_dwarf_call_graph () noexcept
		{
			dwarf_callgraph = true;
			fp_callgraph = false;
			return *this;
		}

		/**
		 * Record frame pointer based call graph. It is suitable to get C++ callstacks on 64bit devices.
		 */
		RecordOptions& record_frame_pointer_call_graph () noexcept
		{
			fp_callgraph = true;
			dwarf_callgraph = false;
			return *this;
		}

		/**
		 * Trace context switch info to show where threads spend time off cpu.
		 */
		RecordOptions& trace_off_cpu () noexcept
		{
			trace_offcpu = true;
			return *this;
		}

		/**
		 * Translate record options into arguments for `simpleperf record` cmd.
		 */
		std::vector<std::string> to_record_args () noexcept;

	private:
		static std::string get_default_output_filename () noexcept;

	private:
		std::string        output_filename;
		std::string        event               = "cpu-cycles";
		size_t             freq                = 4000;
		double             duration_in_seconds = 0.0;
		std::vector<pid_t> threads;
		bool               dwarf_callgraph     = false;
		bool               fp_callgraph        = false;
		bool               trace_offcpu        = false;
	};


	enum class State
	{
		NOT_YET_STARTED,
		STARTED,
		PAUSED,
		STOPPED,
	};

	/**
	 * ProfileSession uses `simpleperf record` cmd to generate a recording file.
	 * It allows users to start recording with some options, pause/resume recording
	 * to only profile interested code, and stop recording.
	 *
	 * Example:
	 *   RecordOptions options;
	 *   options.set_dwarf_call_graph ();
	 *   ProfileSession session;
	 *   session.start_recording (options);
	 *   sleep(1);
	 *   session.pause_recording ();
	 *   sleep(1);
	 *   session.resume_recording ();
	 *   sleep(1);
	 *   session.stop_recording ();
	 *
	 * It logs when error happens, does not abort the process. To read error messages of simpleperf record
	 * process, filter logcat with `simpleperf`.
	 */
	class ProfileSession final
	{
	private:
		static constexpr uid_t AID_USER_OFFSET = 100000;

	public:
		ProfileSession () noexcept;

		/**
		 * Start recording.
		 * @param options RecordOptions
		 */
		void start_recording (RecordOptions& options) noexcept
		{
			start_recording (options.to_record_args ());
		}

		/**
		 * Start recording.
		 * @param args arguments for `simpleperf record` cmd.
		 */
		void start_recording (std::vector<std::string> const& record_args) noexcept;

		/**
		 * Pause recording. No samples are generated in paused state.
		 */
		void pause_recording () noexcept;

		/**
		 * Resume a paused session.
		 */
		void resume_recording () noexcept;

		/**
		 * Stop recording and generate a recording file under appDataDir/simpleperf_data/.
		 */
		void stop_recording () noexcept;

	private:
		bool session_is_valid () const noexcept;

		std::string find_simpleperf_in_temp_dir () const noexcept;
		std::string find_simpleperf () const noexcept;
		bool create_simpleperf_data_dir () const noexcept;
		bool create_simpleperf_process (std::string const& simpleperf_path, std::vector<std::string> const& record_args) noexcept;
		std::string read_reply () noexcept;
		bool send_cmd (std::string const& cmd) noexcept;

		static std::string read_file (FILE* fp, std::string const& path) noexcept;
		static bool is_executable_file (const std::string& path) noexcept;
		static bool run_cmd (std::vector<const char*> args, std::string* standard_output) noexcept;
		static bool check_if_perf_enabled () noexcept;

	private:
		// Clunky, but we want error in initialization to be non-fatal to the app
		bool session_valid = false;

		const std::string app_data_dir_;
		const std::string simpleperf_data_dir_;
		std::mutex        lock_;  // Protect all members below.
		State             state_ = State::NOT_YET_STARTED;
		pid_t             simpleperf_pid_ = -1;
		int               control_fd_ = -1;
		int               reply_fd_ = -1;
		bool              trace_offcpu_ = false;
	};
}
#endif // ndef __SIMPLEPERF_HH
