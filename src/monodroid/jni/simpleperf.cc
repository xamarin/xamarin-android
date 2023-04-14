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
// we do not abort the process if tracing fails - instead we log errors and continue running)
//
#include <array>
#include <cerrno>
#include <cstdio>
#include <ctime>
#include <unistd.h>
#include <sys/stat.h>
#include <sys/wait.h>

#include "android-system.hh"
#include "globals.hh"
#include "logger.hh"
#include "simpleperf.hh"
#include "strings.hh"

using namespace xamarin::android::internal;

std::string
RecordOptions::get_default_output_filename () noexcept
{
	time_t t = time (nullptr);

	struct tm tm;
	if (localtime_r (&t, &tm) != &tm) {
		return "perf.data";
	}

	char* buf = nullptr;

	// TODO: don't use asprintf
	asprintf (&buf, "perf-%02d-%02d-%02d-%02d-%02d.data", tm.tm_mon + 1, tm.tm_mday, tm.tm_hour, tm.tm_min, tm.tm_sec);

	std::string result = buf;
	free (buf);

	return result;
}

std::vector<std::string>
RecordOptions::to_record_args () noexcept
{
	std::vector<std::string> args;
	if (output_filename.empty ()) {
		output_filename = get_default_output_filename ();
	}

	args.insert (args.end (), {"-o", output_filename});
	args.insert (args.end (), {"-e", event});
	args.insert (args.end (), {"-f", std::to_string(freq)});

	if (duration_in_seconds != 0.0) {
		args.insert (args.end (), {"--duration", std::to_string (duration_in_seconds)});
	}

	if (threads.empty ()) {
		args.insert (args.end (), {"-p", std::to_string (getpid ())});
	} else {
		std::string threads_arg;

		for (auto const& thread_id : threads) {
			if (!threads_arg.empty ()) {
				threads_arg.append (",");
			}

			threads_arg.append (std::to_string (thread_id));
		}

		args.insert(args.end (), {"-t", threads_arg});
	}

	if (dwarf_callgraph) {
		args.push_back ("-g");
	} else if (fp_callgraph) {
		args.insert (args.end (), {"--call-graph", "fp"});
	}

	if (trace_offcpu) {
		args.push_back ("--trace-offcpu");
	}

	return args;
}

ProfileSession::ProfileSession () noexcept
{
	std::string input_file {"/proc/self/cmdline"};
	FILE* fp = fopen (input_file.c_str (), "r");
	if (fp == nullptr) {
		log_error (LOG_DEFAULT, "simpleperf: failed to open %s: %s", input_file.c_str (), strerror (errno));
		return;
	}

	std::string s = read_file (fp, input_file);
	for (size_t i = 0; i < s.size (); i++) {
		if (s[i] == '\0') {
			s = s.substr (0, i);
			break;
		}
	}

	std::string app_data_dir = "/data/data/" + s;
	uid_t uid = getuid ();
	if (uid >= AID_USER_OFFSET) {
		int user_id = uid / AID_USER_OFFSET;
		app_data_dir = "/data/user/" + std::to_string (user_id) + "/" + s;
	}

	session_valid = true;
}

std::string
ProfileSession::read_file (FILE* fp, std::string const& path) noexcept
{
	std::string s;
	if (fp == nullptr) {
		return s;
	}

	constexpr size_t BUF_SIZE = 200;
	std::array<char, BUF_SIZE> buf;

	while (true) {
		size_t n = fread (buf.data (), 1, buf.size (), fp);
		if (n < buf.size ()) {
			if (ferror (fp)) {
				log_warn (LOG_DEFAULT, "simpleperf: an error occurred while reading input file %s: %s", path.c_str (), strerror (errno));
			}

			break;
		}

		s.insert (s.end (), buf.data (), buf.data () + n);
	}

	fclose (fp);
	return s;
}

bool
ProfileSession::session_is_valid () const noexcept
{
	if (session_valid) {
		return true;
	}

	log_warn (LOG_DEFAULT, "simpleperf: profiling session object hasn't been initialized properly, profiling will NOT produce any results");
	return false;
}

std::string
ProfileSession::find_simpleperf_in_temp_dir () const noexcept
{
	const std::string path = "/data/local/tmp/simpleperf";
	if (!is_executable_file (path)) {
		return "";
	}
	// Copy it to app_dir to execute it.
	const std::string to_path = app_data_dir_ + "/simpleperf";
	if (!run_cmd ({"/system/bin/cp", path.c_str(), to_path.c_str()}, nullptr)) {
		return "";
	}

	// For apps with target sdk >= 29, executing app data file isn't allowed.
	// For android R, app context isn't allowed to use perf_event_open.
	// So test executing downloaded simpleperf.
	std::string s;
	if (!run_cmd ({to_path.c_str(), "list", "sw"}, &s)) {
		return "";
	}

	if (s.find ("cpu-clock") == std::string::npos) {
		return "";
	}

	return to_path;
}

bool
ProfileSession::run_cmd (std::vector<const char*> args, std::string* standard_output) noexcept
{
	std::array<int, 2> stdout_fd;
	if (pipe (stdout_fd.data ()) != 0) {
		return false;
	}

	args.push_back (nullptr);

	// Fork handlers (like gsl_library_close) may hang in a multi-thread environment.
	// So we use vfork instead of fork to avoid calling them.
	int pid = vfork ();
	if (pid == -1) {
		log_warn (LOG_DEFAULT, "simpleperf: `vfork` failed: %s", strerror (errno));
		return false;
	}

	if (pid == 0) {
		// child process
		close (stdout_fd[0]);
		dup2 (stdout_fd[1], 1);
		close (stdout_fd[1]);

		execvp (const_cast<char*>(args[0]), const_cast<char**>(args.data ()));

		log_error (LOG_DEFAULT, "simpleperf: failed to run %s: %s", args[0], strerror (errno));
		_exit (1);
	}

	// parent process
	close (stdout_fd[1]);

	int status;
	pid_t result = TEMP_FAILURE_RETRY (waitpid (pid, &status, 0));
	if (result == -1) {
		log_error (LOG_DEFAULT, "simpleperf: failed to call waitpid: %s", strerror (errno));
	}

	if (!WIFEXITED(status) || WEXITSTATUS(status) != 0) {
		return false;
	}

	if (standard_output == nullptr) {
		close (stdout_fd[0]);
	} else {
		*standard_output = read_file (fdopen (stdout_fd[0], "r"), "pipe");
	}

	return true;
}

bool
ProfileSession::is_executable_file (const std::string& path) noexcept
{
	struct stat st;

	if (stat (path.c_str (), &st) != 0) {
		return false;
	}

	return S_ISREG(st.st_mode) && ((st.st_mode & S_IXUSR) == S_IXUSR);
}

std::string
ProfileSession::find_simpleperf () const noexcept
{
	// 1. Try /data/local/tmp/simpleperf first. Probably it's newer than /system/bin/simpleperf.
	std::string simpleperf_path = find_simpleperf_in_temp_dir ();
	if (!simpleperf_path.empty()) {
		return simpleperf_path;
	}

	// 2. Try /system/bin/simpleperf, which is available on Android >= Q.
	simpleperf_path = "/system/bin/simpleperf";
	if (is_executable_file (simpleperf_path)) {
		return simpleperf_path;
	}

	log_error (LOG_DEFAULT, "simpleperf: can't find simpleperf on device. Please run api_profiler.py.");
	return "";
}

bool
ProfileSession::check_if_perf_enabled () noexcept
{
	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> prop;

	if (androidSystem.monodroid_get_system_property ("persist.simpleperf.profile_app_uid", prop) <= 0) {
		return false;
	}

	if (prop.get () == std::to_string (getuid ())) {
		prop.clear ();

		androidSystem.monodroid_get_system_property ("persist.simpleperf.profile_app_expiration_time", prop);
		if (!prop.empty ()) {
			errno = 0;
			long expiration_time = strtol (prop.get (), nullptr, 10);
			if (errno == 0 && expiration_time > time (nullptr)) {
				return true;
			}
		}
	}

	if (androidSystem.monodroid_get_system_property ("security.perf_harden", prop) <= 0 || prop.empty ()) {
		return true;
	}

	if (prop.get ()[0] == '1') {
		log_error (LOG_DEFAULT, "simpleperf: recording app isn't enabled on the device. Please run api_profiler.py.");
		return false;
	}

	return true;
}

bool
ProfileSession::create_simpleperf_data_dir () const noexcept
{
	struct stat st;
	if (stat (simpleperf_data_dir_.c_str (), &st) == 0 && S_ISDIR (st.st_mode)) {
		return true;
	}

	if (mkdir (simpleperf_data_dir_.c_str (), 0700) == -1) {
		log_error (LOG_DEFAULT, "simpleperf: failed to create simpleperf data dir %s: %s", simpleperf_data_dir_.c_str(), strerror (errno));
		return false;
	}

	return true;
}

bool
ProfileSession::create_simpleperf_process (std::string const& simpleperf_path, std::vector<std::string> const& record_args) noexcept
{
	// 1. Create control/reply pips.
	std::array<int, 2> control_fd { -1, -1 };
	std::array<int, 2> reply_fd { -1, -1 };

	if (pipe (control_fd.data ()) != 0 || pipe (reply_fd.data ()) != 0) {
		log_error (LOG_DEFAULT, "simpleperf: failed to call pipe: %s", strerror (errno));
		return false;
	}

	// 2. Prepare simpleperf arguments.
	std::vector<std::string> args;
	args.emplace_back (simpleperf_path);
	args.emplace_back ("record");
	args.emplace_back ("--log-to-android-buffer");
	args.insert (args.end (), {"--log", "debug"});
	args.emplace_back ("--stdio-controls-profiling");
	args.emplace_back ("--in-app");
	args.insert (args.end (), {"--tracepoint-events", "/data/local/tmp/tracepoint_events"});
	args.insert (args.end (), record_args.begin (), record_args.end ());

	char* argv[args.size () + 1];
	for (size_t i = 0; i < args.size (); ++i) {
		argv[i] = &args[i][0];
	}
	argv[args.size ()] = nullptr;

	// 3. Start simpleperf process.
	// Fork handlers (like gsl_library_close) may hang in a multi-thread environment.
	// So we use vfork instead of fork to avoid calling them.
	int pid = vfork ();
	if (pid == -1) {
		auto close_fds = [](std::array<int, 2> const& fds) {
			for (auto fd : fds) {
				close (fd);
			}
		};

		log_error (LOG_DEFAULT, "simpleperf: failed to fork: %s", strerror (errno));
		close_fds (control_fd);
		close_fds (reply_fd);

		return false;
	}

	if (pid == 0) {
		// child process
		close (control_fd[1]);
		dup2 (control_fd[0], 0);  // simpleperf read control cmd from fd 0.
		close (control_fd[0]);
		close (reply_fd[0]);
		dup2 (reply_fd[1], 1);  // simpleperf writes reply to fd 1.
		close (reply_fd[0]);
		chdir (simpleperf_data_dir_.c_str());
		execvp (argv[0], argv);

		log_fatal (LOG_DEFAULT, "simpleperf: failed to call exec: %s", strerror (errno));
	}

	// parent process
	close (control_fd[0]);
	control_fd_ = control_fd[1];
	close (reply_fd[1]);
	reply_fd_ = reply_fd[0];
	simpleperf_pid_ = pid;

	// 4. Wait until simpleperf starts recording.
	std::string start_flag = read_reply ();
	if (start_flag != "started") {
		log_error (LOG_DEFAULT, "simpleperf: failed to receive simpleperf start flag");
		return false;
	}

	return true;
}

void
ProfileSession::start_recording (std::vector<std::string> const& record_args) noexcept
{
	if (!session_is_valid () || !check_if_perf_enabled ()) {
		return;
	}

	std::lock_guard<std::mutex> guard {lock_};
	if (state_ != State::NOT_YET_STARTED) {
		log_error (LOG_DEFAULT, "simpleperf: start_recording: session in wrong state %d", state_);
	}

	for (auto const& arg : record_args) {
		if (arg == "--trace-offcpu") {
			trace_offcpu_ = true;
		}
	}

	std::string simpleperf_path = find_simpleperf ();

	if (!create_simpleperf_data_dir ()) {
		return;
	}

	if (!create_simpleperf_process (simpleperf_path, record_args)) {
		return;
	}

	state_ = State::STARTED;
}

void
ProfileSession::pause_recording () noexcept
{
	if (!session_is_valid ()) {
		return;
	}

	std::lock_guard<std::mutex> guard(lock_);
	if (state_ != State::STARTED) {
		log_error (LOG_DEFAULT, "simpleperf: pause_recording: session in wrong state %d", state_);
		return;
	}

	if (trace_offcpu_) {
		log_warn (LOG_DEFAULT, "simpleperf: --trace-offcpu doesn't work well with pause/resume recording");
	}

	if (!send_cmd ("pause")) {
		return;
	}

	state_ = State::PAUSED;
}

void
ProfileSession::resume_recording () noexcept
{
	if (!session_is_valid ()) {
		return;
	}

	std::lock_guard<std::mutex> guard {lock_};

	if (state_ != State::PAUSED) {
		log_error (LOG_DEFAULT, "simpleperf: resume_recording: session in wrong state %d", state_);
	}

	if (!send_cmd ("resume")) {
		return;
	}

	state_ = State::STARTED;
}

void
ProfileSession::stop_recording () noexcept
{
	if (!session_is_valid ()) {
		return;
	}

	std::lock_guard<std::mutex> guard {lock_};

	if (state_ != State::STARTED && state_ != State::PAUSED) {
		log_error (LOG_DEFAULT, "simpleperf: stop_recording: session in wrong state %d", state_);
		return;
	}

	// Send SIGINT to simpleperf to stop recording.
	if (kill (simpleperf_pid_, SIGINT) == -1) {
		log_error (LOG_DEFAULT, "simpleperf: failed to stop simpleperf: %s", strerror (errno));
		return;
	}

	int status;
	pid_t result = TEMP_FAILURE_RETRY(waitpid(simpleperf_pid_, &status, 0));
	if (result == -1) {
		log_error (LOG_DEFAULT, "simpleperf: failed to call waitpid: %s", strerror (errno));
		return;
	}

	if (!WIFEXITED (status) || WEXITSTATUS (status) != 0) {
		log_error (LOG_DEFAULT, "simpleperf: simpleperf exited with error, status = 0x%x", status);
		return;
	}

	state_ = State::STOPPED;
}

std::string
ProfileSession::read_reply () noexcept
{
	std::string s;
	while (true) {
		char c;
		ssize_t result = TEMP_FAILURE_RETRY (read (reply_fd_, &c, 1));
		if (result <= 0 || c == '\n') {
			break;
		}
		s.push_back(c);
	}

	return s;
}

bool
ProfileSession::send_cmd (std::string const& cmd) noexcept
{
	std::string data = cmd + "\n";

	if (TEMP_FAILURE_RETRY (write (control_fd_, &data[0], data.size())) != static_cast<ssize_t>(data.size ())) {
		log_error (LOG_DEFAULT, "simpleperf: failed to send cmd to simpleperf: %s", strerror (errno));
		return false;
	}

	if (read_reply () != "ok") {
		log_error (LOG_DEFAULT, "simpleperf: failed to run cmd in simpleperf: %s", cmd.c_str ());
		return false;
	}

	return true;
}
