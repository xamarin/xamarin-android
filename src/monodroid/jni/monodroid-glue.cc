#include <array>
#include <cctype>
#include <cerrno>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>

#include <dirent.h>
#include <fcntl.h>
#include <pthread.h>
#include <strings.h>

#include <dlfcn.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <sys/syscall.h>
#include <sys/time.h>
#include <sys/types.h>
#include <sys/utsname.h>
#include <unistd.h>

#include <jni.h>
#include <android/dlext.h>

#include <mono/jit/jit.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/class.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/mono-config.h>
#include <mono/metadata/mono-debug.h>
#include <mono/metadata/object.h>
#include <mono/utils/mono-dl-fallback.h>
#include <mono/utils/mono-logger.h>

#include <mono/metadata/mono-private-unstable.h>

#include "mono_android_Runtime.h"

#if defined (DEBUG)
#include <arpa/inet.h>
#include <sys/socket.h>
#include <netinet/in.h>
#endif

//#include "java-interop-util.h"
#include "logger.hh"

//#include "monodroid.h"
#include "util.hh"
#include "debug.hh"
#include "embedded-assemblies.hh"
#include "monodroid-glue.hh"
#include "monodroid-glue-internal.hh"
#include "globals.hh"
#include "xamarin-app.hh"
#include "timing.hh"
//#include "xa-internal-api-impl.hh"
#include "build-info.hh"
#include "monovm-properties.hh"
#include "startup-aware-lock.hh"
#include "timing-internal.hh"
#include "search.hh"

//#include "xamarin_getifaddrs.h"

#include "cpp-util.hh"
#include "strings.hh"

#include "java-interop-dlfcn.h"

using namespace microsoft::java_interop;
using namespace xamarin::android;
using namespace xamarin::android::internal;

std::mutex MonodroidRuntime::pinvoke_map_write_lock;

MonoCoreRuntimeProperties MonodroidRuntime::monovm_core_properties = {
	.trusted_platform_assemblies = nullptr,
	.app_paths = nullptr,
	.native_dll_search_directories = nullptr,
	.pinvoke_override = &MonodroidRuntime::monodroid_pinvoke_override
};

std::mutex MonodroidRuntime::dso_handle_write_lock;
bool MonodroidRuntime::startup_in_progress = true;

void
MonodroidRuntime::thread_start ([[maybe_unused]] MonoProfiler *prof, [[maybe_unused]] uintptr_t tid)
{
	JNIEnv* env;
	int r = osBridge.get_jvm ()->AttachCurrentThread (&env, nullptr);

	if (r != JNI_OK) {
#if DEBUG
		log_fatal (LOG_DEFAULT, "ERROR: Unable to attach current thread to the Java VM!");
		Helpers::abort_application ();
#endif
	}
}

void
MonodroidRuntime::thread_end ([[maybe_unused]] MonoProfiler *prof, [[maybe_unused]] uintptr_t tid)
{
	int r = osBridge.get_jvm ()->DetachCurrentThread ();
	if (r != JNI_OK) {
#if DEBUG
		/*
		log_fatal (LOG_DEFAULT, "ERROR: Unable to detach current thread from the Java VM!");
		 */
#endif
	}
}

inline void
MonodroidRuntime::log_jit_event (MonoMethod *method, const char *event_name)
{
	jit_time.mark_end ();

	if (jit_log == nullptr)
		return;

	char* name = mono_method_full_name (method, 1);

	timing_diff diff (jit_time);
	fprintf (jit_log, "JIT method %6s: %s elapsed: %lis:%u::%u\n", event_name, name, static_cast<long>(diff.sec), diff.ms, diff.ns);

	free (name);
}

void
MonodroidRuntime::jit_begin ([[maybe_unused]] MonoProfiler *prof, MonoMethod *method)
{
	monodroidRuntime.log_jit_event (method, "begin");
}

void
MonodroidRuntime::jit_failed ([[maybe_unused]] MonoProfiler *prof, MonoMethod *method)
{
	monodroidRuntime.log_jit_event (method, "failed");
}

void
MonodroidRuntime::jit_done ([[maybe_unused]] MonoProfiler *prof, MonoMethod *method, [[maybe_unused]] MonoJitInfo* jinfo)
{
	monodroidRuntime.log_jit_event (method, "done");
}

#ifndef RELEASE
MonoAssembly*
MonodroidRuntime::open_from_update_dir (MonoAssemblyName *aname, [[maybe_unused]] char **assemblies_path, [[maybe_unused]] void *user_data)
{
	const char *culture = reinterpret_cast<const char*> (mono_assembly_name_get_culture (aname));
	const char *name    = reinterpret_cast<const char*> (mono_assembly_name_get_name (aname));
	size_t culture_len;

	if (culture != nullptr)
		culture_len = strlen (culture);
	else
		culture_len = 0;
	size_t name_len = strlen (name);

	static_local_string<SENSIBLE_PATH_MAX> pname (name_len + culture_len);
	if (culture_len > 0) {
		pname.append (culture, culture_len);
		pname.append ("/");
	}
	pname.append (name, name_len);

	bool is_dll = utils.ends_with (name, SharedConstants::DLL_EXTENSION);
	size_t file_name_len = pname.length () + 1;
	if (!is_dll)
		file_name_len += SharedConstants::DLL_EXTENSION.length ();

	MonoAssembly *result = nullptr;
	for (const char *override_dir : AndroidSystem::override_dirs) {
		if (override_dir == nullptr || !utils.directory_exists (override_dir)) {
			continue;
		}

		size_t override_dir_len = strlen (override_dir);
		static_local_string<SENSIBLE_PATH_MAX> fullpath (override_dir_len + file_name_len);
		utils.path_combine (fullpath, override_dir, override_dir_len, pname.get (), pname.length ());
		if (!is_dll) {
			fullpath.append (SharedConstants::DLL_EXTENSION);
		}

		log_debug (LOG_ASSEMBLY, "open_from_update_dir: trying to open assembly: %s\n", fullpath.get ());
		if (utils.file_exists (fullpath.get ())) {
			MonoImageOpenStatus status{};
			result = mono_assembly_open_full (fullpath.get (), &status, 0);
			if (result == nullptr || status != MonoImageOpenStatus::MONO_IMAGE_OK) {
				log_warn (LOG_ASSEMBLY, "Failed to load managed assembly '%s'. %s", fullpath.get (), mono_image_strerror (status));
			}
		} else {
			log_warn (LOG_ASSEMBLY, "open_from_update_dir: assembly file DOES NOT EXIST");
		}
		if (result != nullptr) {
			// TODO: register .mdb, .pdb file
			break;
		}
	}

	if (result != nullptr && utils.should_log (LOG_ASSEMBLY)) {
		log_info_nocheck (LOG_ASSEMBLY, "open_from_update_dir: loaded assembly: %p\n", result);
	}
	return result;
}
#endif

bool
MonodroidRuntime::should_register_file ([[maybe_unused]] const char *filename)
{
#ifndef RELEASE
	if (filename == nullptr) {
		return true;
	}

	size_t filename_len = strlen (filename) + 1; // includes space for path separator
	for (const char *odir : AndroidSystem::override_dirs) {
		if (odir == nullptr) {
			continue;
		}

		size_t odir_len = strlen (odir);
		static_local_string<SENSIBLE_PATH_MAX> p (odir_len + filename_len);
		utils.path_combine (p, odir, odir_len, filename, filename_len);
		bool  exists  = utils.file_exists (p.get ());

		if (exists) {
			return false;
		}
	}
#endif
	return true;
}

inline void
MonodroidRuntime::gather_bundled_assemblies (jstring_array_wrapper &runtimeApks, size_t *out_user_assemblies_count, bool have_split_apks)
{
#if defined(DEBUG)
	if (application_config.instant_run_enabled) {
		for (const char *od : AndroidSystem::override_dirs) {
			if (od == nullptr || !utils.directory_exists (od)) {
				continue;
			}

			// TODO: temporary hack for the location of typemaps, to be fixed
			dynamic_local_string<SENSIBLE_PATH_MAX> above { od };
			above.append ("/..");
			log_debug (LOG_ASSEMBLY, "Loading TypeMaps from %s", above.get());
			embeddedAssemblies.try_load_typemaps_from_directory (above.get());
		}
	}
#endif

	if (!androidSystem.is_embedded_dso_mode_enabled ()) {
		*out_user_assemblies_count = embeddedAssemblies.register_from_filesystem<should_register_file> ();
		return;
	}

	int64_t apk_count = static_cast<int64_t>(runtimeApks.get_length ());
	size_t prev_num_assemblies = 0;
	bool got_split_config_abi_apk = false;
	bool got_base_apk = false;

	for (int64_t i = 0; i < apk_count; i++) {
		jstring_wrapper &apk_file = runtimeApks [static_cast<size_t>(i)];

		if (have_split_apks) {
			bool scan_apk = false;

			// With split configs we need to scan only the abi apk, because both the assembly stores and the runtime
			// configuration blob are in `lib/{ARCH}`, which in turn lives in the split config APK
			if (!got_split_config_abi_apk && utils.ends_with (apk_file.get_cstr (), SharedConstants::split_config_abi_apk_name)) {
				got_split_config_abi_apk = scan_apk = true;
			} else if (!application_config.have_assembly_store && !got_base_apk && utils.ends_with (apk_file.get_cstr (), base_apk_name)) {
				got_base_apk = scan_apk = true;
			}

			if (!scan_apk) {
				continue;
			}
		}

		size_t cur_num_assemblies  = embeddedAssemblies.register_from_apk<should_register_file> (apk_file.get_cstr ());

		*out_user_assemblies_count += (cur_num_assemblies - prev_num_assemblies);
		prev_num_assemblies = cur_num_assemblies;

		if (!embeddedAssemblies.keep_scanning ()) {
			break;
		}
	}

	embeddedAssemblies.ensure_valid_assembly_stores ();
}

#if defined (DEBUG)
int
MonodroidRuntime::monodroid_debug_connect (int sock, struct sockaddr_in addr)
{
	long flags = fcntl (sock, F_GETFL, nullptr);
	flags |= O_NONBLOCK;
	fcntl (sock, F_SETFL, flags);

	int res = connect (sock, (struct sockaddr *) &addr, sizeof (addr));

	if (res < 0) {
		if (errno == EINPROGRESS) {
			while (true) {
				timeval tv;

				tv.tv_sec = 2;
				tv.tv_usec = 0;

				fd_set fds;
				FD_ZERO (&fds);
				FD_SET (sock, &fds);

				res = select (sock + 1, 0, &fds, 0, &tv);

				if (res <= 0 && errno != EINTR) return -5;

				socklen_t len = sizeof (int);
				int val = 0;
				if (getsockopt (sock, SOL_SOCKET, SO_ERROR, &val, &len) < 0) return -3;

				if (val) return -4;

				break;
			}
		} else {
			return -2;
		}
	}

	flags = fcntl (sock, F_GETFL, nullptr);
	flags &= (~O_NONBLOCK);
	fcntl (sock, F_SETFL, flags);

	return 1;
}

int
MonodroidRuntime::monodroid_debug_accept (int sock, struct sockaddr_in addr)
{
	ssize_t res = bind (sock, (struct sockaddr *) &addr, sizeof (addr));
	if (res < 0)
		return -1;

	res = listen (sock, 1);
	if (res < 0)
		return -2;

	int accepted = accept (sock, nullptr, nullptr);
	if (accepted < 0)
		return -3;

	constexpr const char handshake_msg [] = "MonoDroid-Handshake\n";
	constexpr size_t handshake_length = sizeof (handshake_msg) - 1;

	do {
		res = send (accepted, handshake_msg, handshake_length, 0);
	} while (res == -1 && errno == EINTR);
	if (res < 0)
		return -4;

	return accepted;
}
#endif

inline jint
MonodroidRuntime::Java_JNI_OnLoad (JavaVM *vm, [[maybe_unused]] void *reserved)
{
	JNIEnv *env;

	androidSystem.init_max_gref_count ();

	vm->GetEnv ((void**)&env, JNI_VERSION_1_6);
	osBridge.initialize_on_onload (vm, env);

	return JNI_VERSION_1_6;
}

void
MonodroidRuntime::parse_gdb_options ()
{
	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> val;

	if (!(androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_GDB_PROPERTY, val) > 0))
		return;

	constexpr std::string_view wait_param { "wait:" };
	if (val.starts_with (wait_param)) {
		/*
		 * The form of the property should be: 'wait:<timestamp>', where <timestamp> should be
		 * the output of date +%s in the android shell.
		 * If this property is set, wait for a native debugger to attach by spinning in a loop.
		 * The debugger can break the wait by setting 'monodroid_gdb_wait' to 0.
		 * If the current time is later than <timestamp> + 10s, the property is ignored.
		 */
		bool do_wait = true;

		long long v = atoll (val.get () + wait_param.length ());
		if (v > 100000) {
			time_t secs = time (nullptr);

			if (v + 10 < secs) {
				log_warn (LOG_DEFAULT, "Found stale %s property with value '%s', not waiting.", Debug::DEBUG_MONO_GDB_PROPERTY.data (), val.get ());
				do_wait = false;
			}
		}

		wait_for_gdb = do_wait;
	}
}

#if defined (DEBUG)
bool
MonodroidRuntime::parse_runtime_args (dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> &runtime_args, RuntimeOptions *options)
{
	if (runtime_args.length () == 0) {
		log_warn (LOG_DEFAULT, "runtime args empty");
		return true;
	}

	constexpr std::string_view ARG_DEBUG    { "debug" };
	constexpr std::string_view ARG_TIMEOUT  { "timeout=" };
	constexpr std::string_view ARG_SERVER   { "server=" };
	constexpr std::string_view ARG_LOGLEVEL { "loglevel=" };

	bool ret = true;
	string_segment token;
	while (runtime_args.next_token (',', token)) {
		if (token.starts_with (ARG_DEBUG)) {
			char *host = nullptr;
			int sdb_port = 1000, out_port = -1;

			options->debug = true;

			if (token.has_at ('=', ARG_DEBUG.length ())) {
				constexpr size_t arg_name_length = ARG_DEBUG.length () + 1; // Includes the '='

				static_local_string<SMALL_STRING_PARSE_BUFFER_LEN> hostport (token.length () - arg_name_length);
				hostport.assign (token.start () + arg_name_length, token.length () - arg_name_length);

				string_segment address;
				size_t field = 0;
				while (field < 3 && hostport.next_token (':', address)) {
					switch (field) {
						case 0: // host
							if (address.empty ()) {
								log_error (LOG_DEFAULT, "Invalid --debug argument for the host field (empty string)");
							} else {
								host = utils.strdup_new (address.start (), address.length ());
							}
							break;

						case 1: // sdb_port
							if (!address.to_integer (sdb_port)) {
								log_error (LOG_DEFAULT, "Invalid --debug argument for the sdb_port field");
							}
							break;

						case 2: // out_port
							if (!address.to_integer (out_port)) {
								log_error (LOG_DEFAULT, "Invalid --debug argument for the sdb_port field");
							}
							break;
					}
					field++;
				}
			} else if (!token.has_at ('\0', ARG_DEBUG.length ())) {
				log_error (LOG_DEFAULT, "Invalid --debug argument.");
				ret = false;
				continue;
			}

			if (sdb_port < 0 || sdb_port > std::numeric_limits<unsigned short>::max ()) {
				log_error (LOG_DEFAULT, "Invalid SDB port value %d", sdb_port);
				ret = false;
				continue;
			}

			if (out_port > std::numeric_limits<unsigned short>::max ()) {
				log_error (LOG_DEFAULT, "Invalid output port value %d", out_port);
				ret = false;
				continue;
			}

			if (host == nullptr)
				host = utils.strdup_new ("10.0.2.2");

			options->host = host;
			options->sdb_port = static_cast<uint16_t>(sdb_port);
			options->out_port = out_port == -1 ? 0 : static_cast<uint16_t>(out_port);
		} else if (token.starts_with (ARG_TIMEOUT)) {
			if (!token.to_integer (options->timeout_time, ARG_TIMEOUT.length ())) {
				log_error (LOG_DEFAULT, "Invalid --timeout argument.");
				ret = false;
			}
		} else if (token.starts_with (ARG_SERVER)) {
			options->server = token.has_at ('y', ARG_SERVER.length ()) || token.has_at ('Y', ARG_SERVER.length ());
		} else if (token.starts_with (ARG_LOGLEVEL)) {
			if (!token.to_integer (options->loglevel, ARG_LOGLEVEL.length ())) {
				log_error (LOG_DEFAULT, "Invalid --loglevel argument.");
				ret = false;
			}
		} else {
			static_local_string<SMALL_STRING_PARSE_BUFFER_LEN> arg (token);
			log_error (LOG_DEFAULT, "Unknown runtime argument: '%s'", arg.get ());
			ret = false;
		}
	}

	return ret;
}
#endif  // def DEBUG && !WINDOWS

inline void
MonodroidRuntime::set_debug_options (void)
{
	if (androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_DEBUG_PROPERTY, nullptr) == 0)
		return;

	embeddedAssemblies.set_register_debug_symbols (true);
	mono_debug_init (MONO_DEBUG_FORMAT_MONO);
}

void
MonodroidRuntime::prof_assembly_loading ([[maybe_unused]] MonoProfiler *prof, MonoAssembly *assembly) noexcept
{
	size_t seq = internal_timing->start_event (TimingEventKind::MonoAssemblyLoad);
	timing_profiler_state->add_sequence (assembly, seq);
}

void
MonodroidRuntime::prof_assembly_loaded ([[maybe_unused]] MonoProfiler *prof, MonoAssembly *assembly) noexcept
{
	size_t seq = timing_profiler_state->get_sequence (assembly);
	internal_timing->end_event (seq, true /* uses_more_info */);
	internal_timing->add_more_info (seq, mono_assembly_name_get_name (mono_assembly_get_name (assembly)));
}

void
MonodroidRuntime::prof_image_loading ([[maybe_unused]] MonoProfiler *prof, MonoImage *image) noexcept
{
	size_t seq = internal_timing->start_event (TimingEventKind::MonoImageLoad);
	timing_profiler_state->add_sequence (image, seq);
}

void
MonodroidRuntime::prof_image_loaded ([[maybe_unused]] MonoProfiler *prof, MonoImage *image) noexcept
{
	size_t seq = timing_profiler_state->get_sequence (image);
	internal_timing->end_event (seq, true /* uses_more_info */);
	internal_timing->add_more_info (seq, mono_image_get_name (image));
}

void
MonodroidRuntime::prof_class_loading ([[maybe_unused]] MonoProfiler *prof, MonoClass *klass) noexcept
{
	size_t seq = internal_timing->start_event (TimingEventKind::MonoClassLoad);
	timing_profiler_state->add_sequence (klass, seq);
}

void
MonodroidRuntime::prof_class_loaded ([[maybe_unused]] MonoProfiler *prof, MonoClass *klass) noexcept
{
	size_t seq = timing_profiler_state->get_sequence (klass);
	internal_timing->end_event (seq, true /* uses_more_info */);

	dynamic_local_string<SENSIBLE_TYPE_NAME_LENGTH> info;
	get_full_class_name (klass, info);
	internal_timing->add_more_info (seq, info);
}

void
MonodroidRuntime::prof_vtable_loading ([[maybe_unused]] MonoProfiler *prof, MonoVTable *vtable) noexcept
{
	size_t seq = internal_timing->start_event (TimingEventKind::MonoVTableLoad);
	timing_profiler_state->add_sequence (vtable, seq);
}

void
MonodroidRuntime::prof_vtable_loaded ([[maybe_unused]] MonoProfiler *prof, MonoVTable *vtable) noexcept
{
	size_t seq = timing_profiler_state->get_sequence (vtable);
	internal_timing->end_event (seq, true /* uses_more_info */);

	MonoClass *klass = mono_vtable_class (vtable);
	dynamic_local_string<SENSIBLE_TYPE_NAME_LENGTH> info;

	get_full_class_name (klass, info);
	internal_timing->add_more_info (seq, info);
}

void
MonodroidRuntime::prof_method_begin_invoke ([[maybe_unused]] MonoProfiler *prof, MonoMethod *method) noexcept
{
	size_t seq = internal_timing->start_event (TimingEventKind::MonoMethodInvoke);
	timing_profiler_state->add_sequence (method, seq);
}

void
MonodroidRuntime::prof_method_end_invoke ([[maybe_unused]] MonoProfiler *prof, MonoMethod *method) noexcept
{
	size_t seq = timing_profiler_state->get_sequence (method);
	internal_timing->end_event (seq, true /* uses_more_info */);

	MonoClass *klass = mono_method_get_class (method);
	dynamic_local_string<SENSIBLE_TYPE_NAME_LENGTH> info;

	get_full_class_name (klass, info);
	info.append (".");
	info.append (mono_method_get_name (method));
	internal_timing->add_more_info (seq, info);
}

void
MonodroidRuntime::prof_method_enter ([[maybe_unused]] MonoProfiler *prof, MonoMethod *method, [[maybe_unused]] MonoProfilerCallContext *context) noexcept
{
	size_t seq = internal_timing->start_event (TimingEventKind::MonoMethodDuration);
	timing_profiler_state->add_sequence (method, seq);
}

void
MonodroidRuntime::prof_method_leave ([[maybe_unused]] MonoProfiler *prof, MonoMethod *method, [[maybe_unused]] MonoProfilerCallContext *context) noexcept
{
	size_t seq = timing_profiler_state->get_sequence (method);
	internal_timing->end_event (seq, true /* uses_more_info */);

	MonoClass *klass = mono_method_get_class (method);
	dynamic_local_string<SENSIBLE_TYPE_NAME_LENGTH> info;

	get_full_class_name (klass, info);
	info.append (".");
	info.append (mono_method_get_name (method));
	internal_timing->add_more_info (seq, info);
}

void
MonodroidRuntime::prof_monitor_contention ([[maybe_unused]] MonoProfiler *prof, MonoObject *object) noexcept
{
	size_t seq = internal_timing->start_event (TimingEventKind::LockContention);
	timing_profiler_state->add_sequence (object, seq);
}

void
MonodroidRuntime::prof_monitor_acquired ([[maybe_unused]] MonoProfiler *prof, MonoObject *object) noexcept
{
	size_t seq = timing_profiler_state->get_sequence (object);
	internal_timing->end_event (seq, true /* uses_more_info */);

	MonoClass *klass = mono_object_get_class (object);
	dynamic_local_string<SENSIBLE_TYPE_NAME_LENGTH> info;

	get_full_class_name (klass, info);
	internal_timing->add_more_info (seq, info);
}

force_inline
void
MonodroidRuntime::timing_ensure_state () noexcept
{
	if (timing_profiler_state != nullptr) {
		return;
	}

	timing_profiler_state = new TimingProfilerState;
}
force_inline void
MonodroidRuntime::timing_init_extended () noexcept
{
	timing_ensure_state ();

	mono_profiler_set_assembly_loading_callback (profiler_handle, prof_assembly_loading);
	mono_profiler_set_assembly_loaded_callback (profiler_handle, prof_assembly_loaded);
	mono_profiler_set_image_loading_callback (profiler_handle, prof_image_loading);
	mono_profiler_set_image_loaded_callback (profiler_handle, prof_image_loaded);
}

force_inline void
MonodroidRuntime::timing_init_verbose () noexcept
{
	timing_ensure_state ();

	utils.create_directory (AndroidSystem::override_dirs [0], 0755);
	std::unique_ptr<char> jit_log_path {utils.path_combine (androidSystem.override_dirs[0], "methods.txt")};
	jit_log = utils.monodroid_fopen (jit_log_path.get (), "a");
	utils.set_world_accessable (jit_log_path.get ());
	jit_time.mark_start ();
	mono_profiler_set_jit_begin_callback (profiler_handle, jit_begin);
	mono_profiler_set_jit_done_callback (profiler_handle, jit_done);
	mono_profiler_set_jit_failed_callback (profiler_handle, jit_failed);
	mono_profiler_set_class_loading_callback (profiler_handle, prof_class_loading);
	mono_profiler_set_class_loaded_callback (profiler_handle, prof_class_loaded);
	mono_profiler_set_vtable_loading_callback (profiler_handle, prof_vtable_loading);
	mono_profiler_set_vtable_loaded_callback (profiler_handle, prof_vtable_loaded);
	mono_profiler_set_monitor_contention_callback (profiler_handle, prof_monitor_contention);
	mono_profiler_set_monitor_acquired_callback (profiler_handle, prof_monitor_acquired);
}

force_inline void
MonodroidRuntime::timing_init_extreme () noexcept
{
	timing_ensure_state ();

	mono_profiler_set_method_begin_invoke_callback (profiler_handle, prof_method_begin_invoke);
	mono_profiler_set_method_end_invoke_callback (profiler_handle, prof_method_end_invoke);
	mono_profiler_set_method_enter_callback (profiler_handle, prof_method_enter);
	mono_profiler_set_method_leave_callback (profiler_handle, prof_method_leave);
}

force_inline void
MonodroidRuntime::timing_init () noexcept
{
	if (!FastTiming::enabled () || FastTiming::mode () == TimingMode::Bare) {
		return;
	}

	// TODO: time this, so that we can subtract it from the cumulative results
	switch (FastTiming::mode ()) {
		case TimingMode::Extreme:
			timing_init_extreme ();
			[[fallthrough]];

		case TimingMode::Verbose:
			timing_init_verbose ();
			[[fallthrough]];

		case TimingMode::Extended:
			timing_init_extended ();
			break;

		default:
			// ignored
			break;
	}
}

void
MonodroidRuntime::mono_runtime_init ([[maybe_unused]] JNIEnv *env, [[maybe_unused]] dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN>& runtime_args)
{
#if defined (DEBUG)
	RuntimeOptions options{};
	int64_t cur_time;

	cur_time = time (nullptr);

	if (!parse_runtime_args (runtime_args, &options)) {
		log_error (LOG_DEFAULT, "Failed to parse runtime args: '%s'", runtime_args.get ());
	} else if (options.debug && cur_time > options.timeout_time) {
		log_warn (LOG_DEBUGGER, "Not starting the debugger as the timeout value has been reached; current-time: %lli  timeout: %lli", cur_time, options.timeout_time);
	} else if (options.debug && cur_time <= options.timeout_time) {
		embeddedAssemblies.set_register_debug_symbols (true);

		int loglevel;
		if (debug.have_debugger_log_level ())
			loglevel = debug.get_debugger_log_level ();
		else
			loglevel = options.loglevel;

		char *debug_arg = utils.monodroid_strdup_printf (
			"--debugger-agent=transport=dt_socket,loglevel=%d,address=%s:%d,%sembedding=1,timeout=%d",
			loglevel,
			options.host,
			options.sdb_port,
			options.server ? "server=y," : "",
			options.timeout_time
		);

		char *debug_options [2] = {
			debug_arg,
			nullptr
		};

		// this text is used in unit tests to check the debugger started
		// do not change it without updating the test.
		log_warn (LOG_DEBUGGER, "Trying to initialize the debugger with options: %s", debug_arg);

		if (options.out_port > 0) {
			int sock = socket (PF_INET, SOCK_STREAM, IPPROTO_TCP);
			if (sock < 0) {
				log_fatal (LOG_DEBUGGER, "Could not construct a socket for stdout and stderr; does your app have the android.permission.INTERNET permission? %s", strerror (errno));
				Helpers::abort_application ();
			}

			sockaddr_in addr;
			memset (&addr, 0, sizeof (addr));

			addr.sin_family = AF_INET;
			addr.sin_port = htons (options.out_port);

			int r;
			if ((r = inet_pton (AF_INET, options.host, &addr.sin_addr)) != 1) {
				log_error (LOG_DEBUGGER, "Could not setup a socket for stdout and stderr: %s",
						r == -1 ? strerror (errno) : "address not parseable in the specified address family");
				Helpers::abort_application ();
			}

			if (options.server) {
				int accepted = monodroid_debug_accept (sock, addr);
				log_warn (LOG_DEBUGGER, "Accepted stdout connection: %d", accepted);
				if (accepted < 0) {
					log_fatal (LOG_DEBUGGER, "Error accepting stdout and stderr (%s:%d): %s",
							     options.host, options.out_port, strerror (errno));
					Helpers::abort_application ();
				}

				dup2 (accepted, 1);
				dup2 (accepted, 2);
			} else {
				if (monodroid_debug_connect (sock, addr) != 1) {
					log_fatal (LOG_DEBUGGER, "Error connecting stdout and stderr (%s:%d): %s",
							     options.host, options.out_port, strerror (errno));
					Helpers::abort_application ();
				}

				dup2 (sock, 1);
				dup2 (sock, 2);
			}
		}

		if (debug.enable_soft_breakpoints ()) {
			constexpr std::string_view soft_breakpoints { "--soft-breakpoints" };
			debug_options[1] = const_cast<char*> (soft_breakpoints.data ());
			mono_jit_parse_options (2, debug_options);
		} else {
			mono_jit_parse_options (1, debug_options);
		}

		mono_debug_init (MONO_DEBUG_FORMAT_MONO);
	} else {
		set_debug_options ();
	}

	delete[] options.host;
#else
	set_debug_options ();
#endif

	// TESTING ASAN: use-after-free
	// char *x = new char[10]{};
	// delete[] x;
	// log_warn (LOG_DEFAULT, "x == %s", x);

	// TESTING UBSAN: integer overflow
	//log_warn (LOG_DEFAULT, "Let us have an overflow: %d", INT_MAX + 1);

	profiler_handle = mono_profiler_create (nullptr);
	mono_profiler_set_thread_started_callback (profiler_handle, thread_start);
	mono_profiler_set_thread_stopped_callback (profiler_handle, thread_end);

	timing_init ();
	parse_gdb_options ();

	if (wait_for_gdb) {
		log_warn (LOG_DEFAULT, "Waiting for gdb to attach...");
		while (monodroid_gdb_wait) {
			sleep (1);
		}
	}

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> prop_val;
	/* Additional runtime arguments passed to mono_jit_parse_options () */
	if (androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_RUNTIME_ARGS_PROPERTY, prop_val) > 0) {
		char **ptr;

		log_warn (LOG_DEBUGGER, "passing '%s' as extra arguments to the runtime.\n", prop_val.get ());

		char **args = utils.monodroid_strsplit (prop_val.get (), " ", 0);
		int argc = 0;

		for (ptr = args; *ptr; ptr++)
			argc ++;

		mono_jit_parse_options (argc, args);
	}

	// int argc = 5;
	// char* argv[] = { "-v", "-v", "-v", "-v", "-v" };
	// mono_jit_parse_options (argc, argv);

	mono_set_signal_chaining (1);
	mono_set_crash_chaining (1);

	osBridge.register_gc_hooks ();

	/*
	 * Assembly preload hooks are invoked in _reverse_ registration order.
	 * Looking for assemblies from the update dir takes precedence over
	 * everything else, and thus must go LAST.
	 */
	embeddedAssemblies.install_preload_hooks_for_appdomains ();
#ifndef RELEASE
	mono_install_assembly_preload_hook (open_from_update_dir, nullptr);
#endif

#if defined (RELEASE)
	if (application_config.marshal_methods_enabled) {
		xamarin_app_init (env, get_function_pointer_at_startup);
	}
#endif // def RELEASE && def ANDROID && def NET
}

void
MonodroidRuntime::cleanup_runtime_config (MonovmRuntimeConfigArguments *args, [[maybe_unused]] void *user_data)
{
	if (args == nullptr || args->kind != 1 || args->runtimeconfig.data.data == nullptr) {
		return;
	}

	munmap (static_cast<void*>(const_cast<char*>(args->runtimeconfig.data.data)), args->runtimeconfig.data.data_len);
}

MonoDomain*
MonodroidRuntime::create_domain (JNIEnv *env, jstring_array_wrapper &runtimeApks, bool is_root_domain, bool have_split_apks)
{
	size_t user_assemblies_count   = 0;

	gather_bundled_assemblies (runtimeApks, &user_assemblies_count, have_split_apks);

	if (embeddedAssemblies.have_runtime_config_blob ()) {
		size_t blob_time_index;
		if (FastTiming::enabled ()) [[unlikely]] {
			blob_time_index = internal_timing->start_event (TimingEventKind::RuntimeConfigBlob);
		}

		runtime_config_args.kind = 1;
		embeddedAssemblies.get_runtime_config_blob (runtime_config_args.runtimeconfig.data.data, runtime_config_args.runtimeconfig.data.data_len);
		monovm_runtimeconfig_initialize (&runtime_config_args, cleanup_runtime_config, nullptr);

		if (FastTiming::enabled ()) [[unlikely]] {
			internal_timing->end_event (blob_time_index);
		}
	}

	if (user_assemblies_count == 0 && androidSystem.count_override_assemblies () == 0 && !is_running_on_desktop) {
#if defined (DEBUG)
		log_fatal (LOG_DEFAULT, "No assemblies found in '%s' or '%s'. Assuming this is part of Fast Deployment. Exiting...",
		           AndroidSystem::override_dirs [0],
		           (AndroidSystem::override_dirs.size () > 1 && AndroidSystem::override_dirs [1] != nullptr) ? AndroidSystem::override_dirs [1] : "<unavailable>");
#else
		log_fatal (LOG_DEFAULT, "No assemblies (or assembly blobs) were found in the application APK file(s) or on the filesystem");
#endif
		constexpr const char *assemblies_prefix = EmbeddedAssemblies::get_assemblies_prefix ().data ();
		log_fatal (LOG_DEFAULT, "Make sure that all entries in the APK directory named `%s` are STORED (not compressed)", assemblies_prefix);
		log_fatal (LOG_DEFAULT, "If Android Gradle Plugin's minification feature is enabled, it is likely all the entries in `%s` are compressed", assemblies_prefix);

		Helpers::abort_application ();
	}

	MonoDomain *domain = mono_jit_init_version (const_cast<char*> ("RootDomain"), const_cast<char*> ("mobile"));
	if constexpr (is_running_on_desktop) {
		if (is_root_domain) {
			c_unique_ptr<char> corlib_error_message_guard {const_cast<char*>(mono_check_corlib_version ())};
			char *corlib_error_message = corlib_error_message_guard.get ();

			if (corlib_error_message == nullptr) {
				if (!androidSystem.monodroid_get_system_property ("xamarin.studio.fakefaultycorliberrormessage", &corlib_error_message)) {
					corlib_error_message = nullptr;
				}
			}
			if (corlib_error_message != nullptr) {
				jclass ex_klass = env->FindClass ("mono/android/MonoRuntimeException");
				env->ThrowNew (ex_klass, corlib_error_message);
				return nullptr;
			}

			// Load a basic environment for the RootDomain if run on desktop so that we can unload
			// and reload most assemblies including Mono.Android itself
			MonoAssemblyName *aname = mono_assembly_name_new ("System");
			mono_assembly_load_full (aname, nullptr, nullptr, 0);
			mono_assembly_name_free (aname);
		}
	}

	return domain;
}

inline int
MonodroidRuntime::LocalRefsAreIndirect (JNIEnv *env, jclass runtimeClass, int version)
{
	if (version < 14) {
		java_System = nullptr;
		java_System_identityHashCode = 0;
		return 0;
	}

	java_System = utils.get_class_from_runtime_field(env, runtimeClass, "java_lang_System", true);
	java_System_identityHashCode = env->GetStaticMethodID (java_System, "identityHashCode", "(Ljava/lang/Object;)I");

	return 1;
}

force_inline void
MonodroidRuntime::lookup_bridge_info (MonoClass *klass, const OSBridge::MonoJavaGCBridgeType *type, OSBridge::MonoJavaGCBridgeInfo *info)
{
	info->klass             = klass;
	info->handle            = mono_class_get_field_from_name (info->klass, const_cast<char*> ("handle"));
	info->handle_type       = mono_class_get_field_from_name (info->klass, const_cast<char*> ("handle_type"));
	info->refs_added        = mono_class_get_field_from_name (info->klass, const_cast<char*> ("refs_added"));
	info->weak_handle       = mono_class_get_field_from_name (info->klass, const_cast<char*> ("weak_handle"));
	if (info->klass == nullptr || info->handle == nullptr || info->handle_type == nullptr ||
			info->refs_added == nullptr || info->weak_handle == nullptr) {
		log_fatal (LOG_DEFAULT, "The type `%s.%s` is missing required instance fields! handle=%p handle_type=%p refs_added=%p weak_handle=%p",
				type->_namespace, type->_typename,
				info->handle,
				info->handle_type,
				info->refs_added,
				info->weak_handle);
		Helpers::abort_application ();
	}
}

force_inline void
MonodroidRuntime::lookup_bridge_info (MonoImage *image, const OSBridge::MonoJavaGCBridgeType *type, OSBridge::MonoJavaGCBridgeInfo *info)
{
	lookup_bridge_info (
		mono_class_from_name (image, type->_namespace, type->_typename),
		type,
		info
	);
}

void
MonodroidRuntime::monodroid_debugger_unhandled_exception (MonoException *ex)
{
	mono_debugger_agent_unhandled_exception (ex);
}

void
MonodroidRuntime::init_android_runtime (JNIEnv *env, jclass runtimeClass, jobject loader)
{
	constexpr std::string_view icall_typemap_java_to_managed { "Java.Interop.TypeManager::monodroid_typemap_java_to_managed" };
	constexpr std::string_view icall_typemap_managed_to_java { "Android.Runtime.JNIEnv::monodroid_typemap_managed_to_java" };

#if defined (RELEASE)
	// The reason for these using is that otherwise the compiler will complain about not being
	// able to cast overloaded methods to const void* pointers.
	using j2mFn = MonoReflectionType* (*)(MonoString *java_type);
	using m2jFn = const char* (*)(MonoReflectionType *type, const uint8_t *mvid);

	mono_add_internal_call (icall_typemap_java_to_managed.data (), reinterpret_cast<const void*>(static_cast<j2mFn>(EmbeddedAssemblies::typemap_java_to_managed)));
	mono_add_internal_call (icall_typemap_managed_to_java.data (), reinterpret_cast<const void*>(static_cast<m2jFn>(EmbeddedAssemblies::typemap_managed_to_java)));
#else
	mono_add_internal_call (icall_typemap_java_to_managed.data (), reinterpret_cast<const void*>(typemap_java_to_managed));
	mono_add_internal_call (icall_typemap_managed_to_java.data (), reinterpret_cast<const void*>(typemap_managed_to_java));
#endif // def RELEASE && def ANDROID

	mono_add_internal_call ("Android.Runtime.RuntimeNativeMethods::monodroid_debugger_unhandled_exception", reinterpret_cast<const void*> (monodroid_debugger_unhandled_exception));
	mono_add_internal_call ("Android.Runtime.RuntimeNativeMethods::monodroid_unhandled_exception", reinterpret_cast<const void*>(monodroid_unhandled_exception));

	struct JnienvInitializeArgs init = {};
	init.javaVm                 = osBridge.get_jvm ();
	init.env                    = env;
	init.logCategories          = log_categories;
	init.version                = env->GetVersion ();
	init.androidSdkVersion      = android_api_level;
	init.localRefsAreIndirect   = LocalRefsAreIndirect (env, runtimeClass, init.androidSdkVersion);
	init.isRunningOnDesktop     = is_running_on_desktop ? 1 : 0;
	init.brokenExceptionTransitions = application_config.broken_exception_transitions ? 1 : 0;
	init.packageNamingPolicy    = static_cast<int>(application_config.package_naming_policy);
	init.boundExceptionType     = application_config.bound_exception_type;
	init.jniAddNativeMethodRegistrationAttributePresent = application_config.jni_add_native_method_registration_attribute_present ? 1 : 0;
	init.jniRemappingInUse = application_config.jni_remapping_replacement_type_count > 0 || application_config.jni_remapping_replacement_method_index_entry_count > 0;
	init.marshalMethodsEnabled  = application_config.marshal_methods_enabled;

	// GC threshold is 90% of the max GREF count
	init.grefGcThreshold        = static_cast<int>(androidSystem.get_gref_gc_threshold ());

	log_info (LOG_GC, "GREF GC Threshold: %i", init.grefGcThreshold);

	init.grefClass = utils.get_class_from_runtime_field (env, runtimeClass, "java_lang_Class", true);
	Class_getName  = env->GetMethodID (init.grefClass, "getName", "()Ljava/lang/String;");
	init.Class_forName = env->GetStaticMethodID (init.grefClass, "forName", "(Ljava/lang/String;ZLjava/lang/ClassLoader;)Ljava/lang/Class;");

	MonoAssembly *mono_android_assembly;

	mono_android_assembly = utils.monodroid_load_assembly (default_alc, SharedConstants::MONO_ANDROID_ASSEMBLY_NAME.data ());
	MonoImage *mono_android_assembly_image = mono_assembly_get_image (mono_android_assembly);

	uint32_t i = 0;
	for ( ; i < OSBridge::NUM_XA_GC_BRIDGE_TYPES; ++i) {
		lookup_bridge_info (
			mono_android_assembly_image,
			&osBridge.get_java_gc_bridge_type (i),
			&osBridge.get_java_gc_bridge_info (i)
		);
	}

	MonoClass *runtime;
	MonoMethod *method;

	if constexpr (is_running_on_desktop) {
		runtime = mono_class_from_name (mono_android_assembly_image, SharedConstants::ANDROID_RUNTIME_NS_NAME.data (), SharedConstants::JNIENVINIT_CLASS_NAME.data ());
		method = mono_class_get_method_from_name (runtime, "Initialize", 1);
	} else {
		runtime = mono_class_get (mono_android_assembly_image, application_config.android_runtime_jnienv_class_token);
		method = mono_get_method (mono_android_assembly_image, application_config.jnienv_initialize_method_token, runtime);
	}

	abort_unless (runtime != nullptr, "INTERNAL ERROR: unable to find the Android.Runtime.JNIEnvInit class!");
	abort_unless (method != nullptr, "INTERNAL ERROR: Unable to find the Android.Runtime.JNIEnvInit.Initialize method!");

	MonoAssembly *ji_assm;
	ji_assm = utils.monodroid_load_assembly (default_alc, SharedConstants::JAVA_INTEROP_ASSEMBLY_NAME.data ());

	MonoImage       *ji_image   = mono_assembly_get_image  (ji_assm);
	for ( ; i < OSBridge::NUM_XA_GC_BRIDGE_TYPES + OSBridge::NUM_JI_GC_BRIDGE_TYPES; ++i) {
		lookup_bridge_info (
			ji_image,
			&osBridge.get_java_gc_bridge_type (i),
			&osBridge.get_java_gc_bridge_info (i)
		);
	}

	MonoError error;
	/* If running on desktop, we may be swapping in a new Mono.Android image when calling this
	 * so always make sure we have the freshest handle to the method.
	 */
	if (registerType == nullptr || is_running_on_desktop) {
		if constexpr (is_running_on_desktop) {
			registerType = mono_class_get_method_from_name (runtime, "RegisterJniNatives", 5);
		} else {
			registerType = mono_get_method (mono_android_assembly_image, application_config.jnienv_registerjninatives_method_token, runtime);
			jnienv_register_jni_natives = reinterpret_cast<jnienv_register_jni_natives_fn>(mono_method_get_unmanaged_callers_only_ftnptr (registerType, &error));
		}
	}
	abort_unless (registerType != nullptr, "INTERNAL ERROR: Unable to find Android.Runtime.JNIEnvInit.RegisterJniNatives! %s", mono_error_get_message (&error));

	jclass lrefLoaderClass = env->GetObjectClass (loader);
	init.Loader_loadClass     = env->GetMethodID (lrefLoaderClass, "loadClass", "(Ljava/lang/String;)Ljava/lang/Class;");
	env->DeleteLocalRef (lrefLoaderClass);

	init.grefLoader           = env->NewGlobalRef (loader);
	init.grefIGCUserPeer      = utils.get_class_from_runtime_field (env, runtimeClass, "mono_android_IGCUserPeer", true);

	osBridge.initialize_on_runtime_init (env, runtimeClass);

	log_debug (LOG_DEFAULT, "Calling into managed runtime init");

	size_t native_to_managed_index;
	if (FastTiming::enabled ()) [[unlikely]] {
		native_to_managed_index = internal_timing->start_event (TimingEventKind::NativeToManagedTransition);
	}

	auto initialize = reinterpret_cast<jnienv_initialize_fn> (mono_method_get_unmanaged_callers_only_ftnptr (method, &error));
	if (initialize == nullptr) {
		log_fatal (LOG_DEFAULT, "Failed to get pointer to Initialize. Mono error: %s", mono_error_get_message (&error));
	}

	abort_unless (
		initialize != nullptr,
		"Failed to obtain unmanaged-callers-only pointer to the Android.Runtime.JNIEnvInit.Initialize method. %s",
		mono_error_get_message (&error)
	);
	initialize (&init);

	if (FastTiming::enabled ()) [[unlikely]] {
		internal_timing->end_event (native_to_managed_index);
	}
}

MonoClass*
MonodroidRuntime::get_android_runtime_class ()
{
	MonoAssembly *assm = utils.monodroid_load_assembly (default_alc, SharedConstants::MONO_ANDROID_ASSEMBLY_NAME.data ());
	MonoImage *image   = mono_assembly_get_image (assm);
	return mono_class_from_name (image, SharedConstants::ANDROID_RUNTIME_NS_NAME.data (), SharedConstants::JNIENV_CLASS_NAME.data ());
}

inline void
MonodroidRuntime::propagate_uncaught_exception (JNIEnv *env, jobject javaThread, jthrowable javaException)
{
	MonoClass *runtime = get_android_runtime_class ();
	MonoMethod *method = mono_class_get_method_from_name (runtime, "PropagateUncaughtException", 3);

	void* args[] = {
		&env,
		&javaThread,
		&javaException,
	};
	mono_runtime_invoke (method, nullptr, args, nullptr);
}

#if DEBUG
static void
setup_gc_logging (void)
{
	gc_spew_enabled = androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_GC_PROPERTY, nullptr) > 0;
	if (gc_spew_enabled) {
		log_categories |= LOG_GC;
	}
}
#endif

force_inline unsigned int
MonodroidRuntime::convert_dl_flags (int flags)
{
	unsigned int lflags = (flags & static_cast<int> (MONO_DL_LOCAL))
		? JAVA_INTEROP_LIB_LOAD_LOCALLY
		: JAVA_INTEROP_LIB_LOAD_GLOBALLY;
	return lflags;
}

force_inline DSOCacheEntry*
MonodroidRuntime::find_dso_cache_entry (hash_t hash) noexcept
{
	auto equal = [](DSOCacheEntry const& entry, hash_t key) -> bool { return entry.hash == key; };
	auto less_than = [](DSOCacheEntry const& entry, hash_t key) -> bool { return entry.hash < key; };
	ssize_t idx = Search::binary_search<DSOCacheEntry, equal, less_than> (hash, dso_cache, application_config.number_of_dso_cache_entries);
	if (idx >= 0) {
		return &dso_cache[idx];
	}

	return nullptr;
}

force_inline void*
MonodroidRuntime::monodroid_dlopen_log_and_return (void *handle, char **err, const char *full_name, bool free_memory, [[maybe_unused]] bool need_api_init)
{
	if (handle == nullptr && err != nullptr) {
		const char *load_error = dlerror ();
		if (load_error == nullptr) {
			load_error = "Unknown error";
		}
		*err = utils.monodroid_strdup_printf ("Could not load library '%s'. %s", full_name, load_error);
	}

	if (free_memory) {
		delete[] full_name;
	}

	return handle;
}

force_inline void*
MonodroidRuntime::monodroid_dlopen_ignore_component_or_load ([[maybe_unused]] hash_t name_hash, const char *name, int flags, char **err) noexcept
{
	if (startup_in_progress) {
		auto ignore_component = [&](const char *label, MonoComponent component) -> bool {
			if ((application_config.mono_components_mask & component) != component) {
				log_info (LOG_ASSEMBLY, "Mono '%s' component requested but not packaged, ignoring", label);
				return true;
			}

			return false;
		};

		switch (name_hash) {
			case mono_component_debugger_hash:
				if (ignore_component ("Debugger", MonoComponent::Debugger)) {
					return nullptr;
				}
				break;

			case mono_component_hot_reload_hash:
				if (ignore_component ("Hot Reload", MonoComponent::HotReload)) {
					return nullptr;
				}
				break;

			case mono_component_diagnostics_tracing_hash:
				if (ignore_component ("Diagnostics Tracing", MonoComponent::Tracing)) {
					return nullptr;
				}
				break;
		}
	}

	unsigned int dl_flags = monodroidRuntime.convert_dl_flags (flags);
	void * handle = androidSystem.load_dso_from_any_directories (name, dl_flags);
	if (handle != nullptr) {
		return monodroid_dlopen_log_and_return (handle, err, name, false /* name_needs_free */);
	}

	handle = androidSystem.load_dso (name, dl_flags, false /* skip_existing_check */);
	return monodroid_dlopen_log_and_return (handle, err, name, false /* name_needs_free */);
}

force_inline void*
MonodroidRuntime::monodroid_dlopen (const char *name, int flags, char **err) noexcept
{
	hash_t name_hash = xxhash::hash (name, strlen (name));
	log_debug (LOG_ASSEMBLY, "monodroid_dlopen: hash for name '%s' is 0x%zx", name, name_hash);
	DSOCacheEntry *dso = find_dso_cache_entry (name_hash);
	log_debug (LOG_ASSEMBLY, "monodroid_dlopen: hash match %sfound, DSO name is '%s'", dso == nullptr ? "not " : "", dso == nullptr ? "<unknown>" : dso->name);

	if (dso == nullptr) {
		// DSO not known at build time, try to load it
		return monodroid_dlopen_ignore_component_or_load (name_hash, name, flags, err);
	} else if (dso->handle != nullptr) {
		return monodroid_dlopen_log_and_return (dso->handle, err, dso->name, false /* name_needs_free */);
	}

	if (dso->ignore) {
		log_info (LOG_ASSEMBLY, "Request to load '%s' ignored, it is known not to exist", dso->name);
		return nullptr;
	}

	StartupAwareLock lock (dso_handle_write_lock);
#if defined (RELEASE)
	if (androidSystem.is_embedded_dso_mode_enabled ()) {
		DSOApkEntry *apk_entry = dso_apk_entries;
		for (size_t i = 0; i < application_config.number_of_shared_libraries; i++) {
			if (apk_entry->name_hash != dso->real_name_hash) {
				apk_entry++;
				continue;
			}

			android_dlextinfo dli;
			dli.flags = ANDROID_DLEXT_USE_LIBRARY_FD | ANDROID_DLEXT_USE_LIBRARY_FD_OFFSET;
			dli.library_fd = apk_entry->fd;
			dli.library_fd_offset = apk_entry->offset;
			dso->handle = android_dlopen_ext (dso->name, flags, &dli);

			if (dso->handle != nullptr) {
				return monodroid_dlopen_log_and_return (dso->handle, err, dso->name, false /* name_needs_free */);
			}
			break;
		}
	}
#endif
	unsigned int dl_flags = monodroidRuntime.convert_dl_flags (flags);
	dso->handle = androidSystem.load_dso_from_any_directories (dso->name, dl_flags);

	if (dso->handle != nullptr) {
		return monodroid_dlopen_log_and_return (dso->handle, err, dso->name, false /* name_needs_free */);
	}

	dso->handle = androidSystem.load_dso_from_any_directories (name, dl_flags);
	return monodroid_dlopen_log_and_return (dso->handle, err, name, false /* name_needs_free */);
}

void*
MonodroidRuntime::monodroid_dlopen (const char *name, int flags, char **err, [[maybe_unused]] void *user_data) noexcept
{
	if (name == nullptr) {
		log_warn (LOG_ASSEMBLY, "monodroid_dlopen got a null name. This is not supported in NET+");
		return nullptr;
	}

	return monodroid_dlopen (name, flags, err);
}

void*
MonodroidRuntime::monodroid_dlsym (void *handle, const char *name, char **err, [[maybe_unused]] void *user_data)
{
	void *s;
	char *e = nullptr;

	s = java_interop_lib_symbol (handle, name, &e);

	if (!s && err) {
		*err = utils.monodroid_strdup_printf ("Could not find symbol '%s': %s", name, e);
	}
	if (e) {
		java_interop_free (e);
	}

	return s;
}

inline void
MonodroidRuntime::set_environment_variable_for_directory (const char *name, jstring_wrapper &value, bool createDirectory, mode_t mode)
{
	if (createDirectory) {
		int rv = utils.create_directory (value.get_cstr (), mode);
		if (rv < 0 && errno != EEXIST)
			log_warn (LOG_DEFAULT, "Failed to create directory for environment variable %s. %s", name, strerror (errno));
	}
	setenv (name, value.get_cstr (), 1);
}

inline void
MonodroidRuntime::create_xdg_directory (jstring_wrapper& home, size_t home_len, std::string_view const& relative_path, std::string_view const& environment_variable_name) noexcept
{
	static_local_string<SENSIBLE_PATH_MAX> dir (home_len + relative_path.length ());
	utils.path_combine (dir, home.get_cstr (), home_len, relative_path.data (), relative_path.length ());
	log_debug (LOG_DEFAULT, "Creating XDG directory: %s", dir.get ());
	int rv = utils.create_directory (dir.get (), DEFAULT_DIRECTORY_MODE);
	if (rv < 0 && errno != EEXIST)
		log_warn (LOG_DEFAULT, "Failed to create XDG directory %s. %s", dir.get (), strerror (errno));
	if (!environment_variable_name.empty ()) {
		setenv (environment_variable_name.data (), dir.get (), 1);
	}
}

inline void
MonodroidRuntime::create_xdg_directories_and_environment (jstring_wrapper &homeDir)
{
	size_t home_len = strlen (homeDir.get_cstr ());

	constexpr std::string_view XDG_DATA_HOME { "XDG_DATA_HOME" };
	constexpr std::string_view HOME_PATH { ".local/share" };
	create_xdg_directory (homeDir, home_len, HOME_PATH, XDG_DATA_HOME);

	constexpr std::string_view XDG_CONFIG_HOME { "XDG_CONFIG_HOME" };
	constexpr std::string_view CONFIG_PATH { ".config" };
	create_xdg_directory (homeDir, home_len, CONFIG_PATH, XDG_CONFIG_HOME);
}

#if DEBUG
void
MonodroidRuntime::set_debug_env_vars (void)
{
	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> value;
	if (androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_ENV_PROPERTY, value) == 0)
		return;

	auto log_envvar = [](const char *name, const char *v) {
		log_debug (LOG_DEFAULT, "Env variable '%s' set to '%s'.", name, v);
	};

	string_segment arg_token;
	while (value.next_token ('|', arg_token)) {
		static_local_string<SMALL_STRING_PARSE_BUFFER_LEN> arg (arg_token.length ());
		arg.assign (arg_token.start (), arg_token.length ());

		ssize_t idx = arg.index_of ('=');
		size_t index = static_cast<size_t>(idx);
		if (idx < 0 || index == arg.length () - 1) {
			// ’name’ or ’name=’
			constexpr std::string_view one { "1" };
			if (index == arg.length () - 1) {
				arg[index] = '\0';
			}
			setenv (arg.get (), one.data (), 1);
			log_envvar (arg.get (), one.data ());
		} else if (index == 0) {
			// ’=value’
			log_warn (LOG_DEFAULT, "Attempt to set environment variable without specifying name: '%s'", arg.get ());
		} else {
			// ’name=value’
			arg[index] = '\0';
			const char *v = arg.get () + idx + 1;
			setenv (arg.get (), v, 1);
			log_envvar (arg.get (), v);
		}
	}
}
#endif /* DEBUG */

inline void
MonodroidRuntime::set_trace_options (void)
{
	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> value;
	if (androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_TRACE_PROPERTY, value) == 0)
		return;

	mono_jit_set_trace_options (value.get ());
}

inline void
MonodroidRuntime::set_profile_options ()
{
	// We want to avoid dynamic allocation, thus let’s create a buffer that can take both the property value and a
	// path without allocation
	dynamic_local_string<SENSIBLE_PATH_MAX + PROPERTY_VALUE_BUFFER_LEN> value;
	{
		dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> prop_value;
		if (androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_PROFILE_PROPERTY, prop_value) == 0)
			return;

		value.assign (prop_value);
	}

	// NET+ supports only the AOT Mono profiler, if the prefix is absent or different than 'aot:' we consider the
	// property to contain value for the dotnet tracing profiler.
	constexpr std::string_view AOT_PREFIX { "aot:" };
	if (!value.starts_with (AOT_PREFIX)) {
		// setenv(3) makes copies of its arguments
		setenv ("DOTNET_DiagnosticPorts", value.get (), 1);
		return;
	}

	constexpr std::string_view OUTPUT_ARG { "output=" };
	constexpr size_t start_index = AOT_PREFIX.length () + 1; // one char past ':'

	dynamic_local_string<SENSIBLE_PATH_MAX> output_path;
	bool have_output_arg = false;
	string_segment param;

	while (value.next_token (start_index, ',', param)) {
		dynamic_local_string<SENSIBLE_PATH_MAX> temp;
		temp.assign (param.start (), param.length ());
		if (!param.starts_with (OUTPUT_ARG)) {
			continue;
		}

		output_path.assign (param.start () + OUTPUT_ARG.length (), param.length () - OUTPUT_ARG.length ());
		have_output_arg = true;
		break;
	}

	if (!have_output_arg) {
		constexpr std::string_view PROFILE_FILE_NAME_PREFIX { "profile." };
		constexpr std::string_view AOT_EXT { "aotprofile" };

		output_path
			.assign_c (AndroidSystem::override_dirs [0])
			.append ("/")
			.append (PROFILE_FILE_NAME_PREFIX)
			.append (AOT_EXT);

		value
			.append (",")
			.append (OUTPUT_ARG)
			.append (output_path.get (), output_path.length ());
	}
	if (utils.create_directory (AndroidSystem::override_dirs[0], 0) < 0) {
		log_warn (LOG_DEFAULT, "Failed to create directory '%s'. %s", AndroidSystem::override_dirs[0], std::strerror (errno));
	}

	log_warn (LOG_DEFAULT, "Initializing profiler with options: %s", value.get ());
	debug.monodroid_profiler_load (androidSystem.get_runtime_libdir (), value.get (), output_path.get ());
}

inline void
MonodroidRuntime::load_assembly (MonoAssemblyLoadContextGCHandle alc_handle, jstring_wrapper &assembly)
{
	size_t total_time_index;
	if (FastTiming::enabled ()) [[unlikely]] {
		total_time_index = internal_timing->start_event (TimingEventKind::AssemblyLoad);
	}

	const char *assm_name = assembly.get_cstr ();
	if (assm_name == nullptr) [[unlikely]] {
		log_warn (LOG_ASSEMBLY, "Unable to load assembly into ALC, name is null");
		return;
	}

	MonoAssemblyName *aname = mono_assembly_name_new (assm_name);

	MonoImageOpenStatus open_status;
	mono_assembly_load_full_alc (alc_handle, aname, nullptr /* basedir */, &open_status);

	mono_assembly_name_free (aname);

	if (FastTiming::enabled ()) [[unlikely]] {
		internal_timing->end_event (total_time_index, true /* uses_more_info */);

		constexpr std::string_view PREFIX { " (ALC): " };

		dynamic_local_string<SENSIBLE_PATH_MAX + PREFIX.length ()> more_info { PREFIX };
		more_info.append_c (assm_name);
		internal_timing->add_more_info (total_time_index, more_info);
	}
}

inline void
MonodroidRuntime::load_assembly (MonoDomain *domain, jstring_wrapper &assembly)
{
	size_t total_time_index;
	if (FastTiming::enabled ()) [[unlikely]] {
		total_time_index = internal_timing->start_event (TimingEventKind::AssemblyLoad);
	}

	const char *assm_name = assembly.get_cstr ();
	if (assm_name == nullptr) [[unlikely]] {
		log_warn (LOG_ASSEMBLY, "Unable to load assembly into AppDomain, name is null");
		return;
	}

	MonoAssemblyName *aname = mono_assembly_name_new (assm_name);
	MonoDomain *current = utils.get_current_domain ();
	if (domain != current) {
		mono_domain_set (domain, FALSE);
		mono_assembly_load_full (aname, NULL, NULL, 0);
		mono_domain_set (current, FALSE);
	} else {
		mono_assembly_load_full (aname, NULL, NULL, 0);
	}

	mono_assembly_name_free (aname);

	if (FastTiming::enabled ()) [[unlikely]] {
		internal_timing->end_event (total_time_index, true /* uses_more_info */);

		constexpr std::string_view PREFIX { " (domain): " };
		constexpr size_t PREFIX_SIZE = sizeof(PREFIX) - 1;

		dynamic_local_string<SENSIBLE_PATH_MAX + PREFIX_SIZE> more_info { PREFIX };
		more_info.append_c (assm_name);
		internal_timing->add_more_info (total_time_index, more_info);
	}
}

inline void
MonodroidRuntime::load_assemblies (load_assemblies_context_type ctx, bool preload, jstring_array_wrapper &assemblies)
{
	size_t total_time_index;
	if (FastTiming::enabled ()) [[unlikely]] {
		total_time_index = internal_timing->start_event (TimingEventKind::AssemblyPreload);
	}

	size_t i = 0;
	for (i = 0; i < assemblies.get_length (); ++i) {
		jstring_wrapper &assembly = assemblies [i];
		load_assembly (ctx, assembly);
		// only load the first "main" assembly if we are not preloading.
		if (!preload)
			break;
	}

	if (FastTiming::enabled ()) [[unlikely]] {
		internal_timing->end_event (total_time_index, true /* uses-more_info */);

		static_local_string<SharedConstants::INTEGER_BASE10_BUFFER_SIZE> more_info;
		more_info.append (static_cast<uint64_t>(i + 1));
		internal_timing->add_more_info (total_time_index, more_info);
	}
}

[[maybe_unused]] static void
monodroid_Mono_UnhandledException_internal ([[maybe_unused]] MonoException *ex)
{
	// Do nothing with it here, we let the exception naturally propagate on the managed side
}

MonoDomain*
MonodroidRuntime::create_and_initialize_domain (JNIEnv* env, jclass runtimeClass, jstring_array_wrapper &runtimeApks,
                                                jstring_array_wrapper &assemblies, [[maybe_unused]] jobjectArray assembliesBytes,
                                                [[maybe_unused]] jstring_array_wrapper &assembliesPaths, jobject loader, bool is_root_domain,
                                                bool force_preload_assemblies, bool have_split_apks)
{
	MonoDomain* domain = create_domain (env, runtimeApks, is_root_domain, have_split_apks);
	// Asserting this on desktop apparently breaks a Designer test
	abort_unless (domain != nullptr, "Failed to create AppDomain");

	// When running on desktop, the root domain is only a dummy so don't initialize it
	if constexpr (is_running_on_desktop) {
		if (is_root_domain) {
			return domain;
		}
	}

	default_alc = mono_alc_get_default_gchandle ();
	abort_unless (default_alc != nullptr, "Default AssemblyLoadContext not found");

	embeddedAssemblies.install_preload_hooks_for_alc ();
	log_debug (LOG_ASSEMBLY, "ALC hooks installed");

	bool preload = (androidSystem.is_assembly_preload_enabled () || (is_running_on_desktop && force_preload_assemblies));

	load_assemblies (default_alc, preload, assemblies);
	init_android_runtime (env, runtimeClass, loader);
	osBridge.add_monodroid_domain (domain);

	return domain;
}

void
MonodroidRuntime::monodroid_unhandled_exception (MonoObject *java_exception)
{
	mono_unhandled_exception (java_exception);
}

#if !defined (RELEASE)
MonoReflectionType*
MonodroidRuntime::typemap_java_to_managed (MonoString *java_type_name) noexcept
{
	return embeddedAssemblies.typemap_java_to_managed (java_type_name);
}

const char*
MonodroidRuntime::typemap_managed_to_java (MonoReflectionType *type, const uint8_t *mvid) noexcept
{
	return embeddedAssemblies.typemap_managed_to_java (type, mvid);
}
#endif // !def RELEASE

force_inline void
MonodroidRuntime::setup_mono_tracing (std::unique_ptr<char[]> const& mono_log_mask, bool have_log_assembly, bool have_log_gc)
{
	constexpr std::string_view MASK_ASM { "asm" };
	constexpr std::string_view MASK_DLL { "dll" };
	constexpr std::string_view MASK_GC  { "gc" };
	constexpr std::string_view COMMA    { "," };

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> log_mask;
	if (mono_log_mask == nullptr || *mono_log_mask.get () == '\0') {
		if (have_log_assembly) {
			log_mask.append (MASK_ASM);
			log_mask.append (COMMA);
			log_mask.append (MASK_DLL);
		}

		if (have_log_gc) {
			if (log_mask.length () > 0) {
				log_mask.append (COMMA);
			}
			log_mask.append (MASK_GC);
		}

		// empty string turns off all Mono VM tracing
		mono_trace_set_mask_string (log_mask.get ());
		return;
	}

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> input_log_mask;
	input_log_mask.assign_c (mono_log_mask.get ());
	input_log_mask.replace (':', ',');

	if (!have_log_assembly && !have_log_gc)  {
		mono_trace_set_mask_string (input_log_mask.get ());
		return;
	}

	bool need_asm = have_log_assembly, need_dll = have_log_assembly, need_gc = have_log_gc;

	string_segment token;
	while (input_log_mask.next_token (',', token)) {
		if (need_asm && token.equal (MASK_ASM)) {
			need_asm = false;
		} else if (need_dll && token.equal (MASK_DLL)) {
			need_dll = false;
		} else if (need_gc && token.equal (MASK_GC)) {
			need_gc = false;
		}

		if (!need_asm && !need_dll && !need_gc) {
			mono_trace_set_mask_string (input_log_mask.get ());
			return;
		}
	}

	if (need_asm) {
		input_log_mask.append (COMMA);
		input_log_mask.append (MASK_ASM);
	}

	if (need_dll) {
		input_log_mask.append (COMMA);
		input_log_mask.append (MASK_DLL);
	}

	if (need_gc) {
		input_log_mask.append (COMMA);
		input_log_mask.append (MASK_GC);
	}

	mono_trace_set_mask_string (input_log_mask.get ());
}

force_inline void
MonodroidRuntime::install_logging_handlers ()
{
	mono_trace_set_log_handler (mono_log_handler, nullptr);
	mono_trace_set_print_handler (mono_log_standard_streams_handler);
	mono_trace_set_printerr_handler (mono_log_standard_streams_handler);
}

inline void
MonodroidRuntime::Java_mono_android_Runtime_initInternal (JNIEnv *env, jclass klass, jstring lang, jobjectArray runtimeApksJava,
                                                          jstring runtimeNativeLibDir, jobjectArray appDirs, jint localDateTimeOffset,
                                                          jobject loader, jobjectArray assembliesJava, jint apiLevel, jboolean isEmulator,
                                                          jboolean haveSplitApks)
{
	char *mono_log_mask_raw = nullptr;
	char *mono_log_level_raw = nullptr;

	init_logging_categories (mono_log_mask_raw, mono_log_level_raw);

	std::unique_ptr<char[]> mono_log_mask (mono_log_mask_raw);
	std::unique_ptr<char[]> mono_log_level (mono_log_level_raw);

	// If fast logging is disabled, log messages immediately
	FastTiming::initialize ((log_timing_categories & LOG_TIMING_FAST_BARE) == 0);

	size_t total_time_index;
	if (FastTiming::enabled ()) [[unlikely]] {
		timing = new Timing ();
		total_time_index = internal_timing->start_event (TimingEventKind::TotalRuntimeInit);
	}

	jstring_array_wrapper applicationDirs (env, appDirs);
	jstring_wrapper &home = applicationDirs[SharedConstants::APP_DIRS_FILES_DIR_INDEX];

	mono_opt_aot_lazy_assembly_load = application_config.aot_lazy_load ? TRUE : FALSE;

	{
		MonoVMProperties monovm_props { home, localDateTimeOffset };

		// NOTE: the `const_cast` breaks the contract made to MonoVMProperties that the arrays it returns won't be
		// modified, but it's "ok" since Mono doesn't modify them and by using `const char* const*` in MonoVMProperties
		// we may get better code generated (since the methods returning the arrays are marked as `const`, thus not
		// modifying the class state, allowing the compiler to make some assumptions when optimizing) and the class
		// itself doesn't touch the arrays outside its constructor.
		monovm_initialize_preparsed (
			&monovm_core_properties,
			monovm_props.property_count (),
			const_cast<const char**>(monovm_props.property_keys ()),
			const_cast<const char**>(monovm_props.property_values ())
		);
	}

	android_api_level = apiLevel;
	androidSystem.detect_embedded_dso_mode (applicationDirs);
	androidSystem.set_running_in_emulator (isEmulator);

	java_TimeZone = utils.get_class_from_runtime_field (env, klass, "java_util_TimeZone", true);

	utils.monodroid_store_package_name (application_config.android_package_name);

	jstring_wrapper jstr (env, lang);
	set_environment_variable ("LANG", jstr);

	androidSystem.setup_environment ();

	set_environment_variable_for_directory ("TMPDIR", applicationDirs[SharedConstants::APP_DIRS_CACHE_DIR_INDEX]);
	set_environment_variable_for_directory ("HOME", home);
	create_xdg_directories_and_environment (home);
	androidSystem.set_primary_override_dir (home);

	jstring_array_wrapper runtimeApks (env, runtimeApksJava);
	androidSystem.setup_app_library_directories (runtimeApks, applicationDirs, haveSplitApks);

	init_reference_logging (androidSystem.get_primary_override_dir ());
	androidSystem.create_update_dir (androidSystem.get_primary_override_dir ());

#if DEBUG
	setup_gc_logging ();
	set_debug_env_vars ();
#endif

	bool have_log_assembly = (log_categories & LOG_ASSEMBLY) != 0;
	bool have_log_gc = (log_categories & LOG_GC) != 0;

	if (mono_log_level == nullptr || *mono_log_level.get () == '\0') {
		mono_trace_set_level_string ((have_log_assembly || have_log_gc) ? "info" : "error");
	} else {
		mono_trace_set_level_string (mono_log_level.get ());
	}

	setup_mono_tracing (mono_log_mask, have_log_assembly, have_log_gc);
	install_logging_handlers ();

	if (runtimeNativeLibDir != nullptr) {
		jstr = runtimeNativeLibDir;
		androidSystem.set_runtime_libdir (strdup (jstr.get_cstr ()));
		log_debug (LOG_DEFAULT, "Using runtime path: %s", androidSystem.get_runtime_libdir ());
	}

	androidSystem.setup_process_args (runtimeApks);
	mono_dl_fallback_register (monodroid_dlopen, monodroid_dlsym, nullptr, nullptr);

	set_profile_options ();

	set_trace_options ();

#if defined (DEBUG)
	debug.start_debugging_and_profiling ();
#endif

	log_debug (LOG_DEFAULT, "Probing for Mono AOT mode\n");

	MonoAotMode mode = MonoAotMode::MONO_AOT_MODE_NONE;
	if (androidSystem.is_mono_aot_enabled ()) {
		mode = androidSystem.get_mono_aot_mode ();
		if (mode != MonoAotMode::MONO_AOT_MODE_INTERP_ONLY) {
			log_debug (LOG_DEFAULT, "Enabling AOT mode in Mono");
		} else {
			log_debug (LOG_DEFAULT, "Enabling Mono Interpreter");
		}
	}
	mono_jit_set_aot_mode (mode);

	log_debug (LOG_DEFAULT, "Probing if we should use LLVM\n");

	if (androidSystem.is_mono_llvm_enabled ()) {
		char *args [1];
		args[0] = const_cast<char*> ("--llvm");
		log_debug (LOG_DEFAULT, "Enabling LLVM mode in Mono\n");
		mono_jit_parse_options (1,  args);
		mono_set_use_llvm (true);
	}

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> runtime_args;
	androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_EXTRA_PROPERTY, runtime_args);

	size_t mono_runtime_init_index;
	if (FastTiming::enabled ()) [[unlikely]] {
		mono_runtime_init_index = internal_timing->start_event (TimingEventKind::MonoRuntimeInit);
	}

	mono_runtime_init (env, runtime_args);

	if (FastTiming::enabled ()) [[unlikely]] {
		internal_timing->end_event (mono_runtime_init_index);
	}

	jstring_array_wrapper assemblies (env, assembliesJava);
	jstring_array_wrapper assembliesPaths (env);
	/* the first assembly is used to initialize the AppDomain name */
	create_and_initialize_domain (env, klass, runtimeApks, assemblies, nullptr, assembliesPaths, loader, /*is_root_domain:*/ true, /*force_preload_assemblies:*/ false, haveSplitApks);

	// Install our dummy exception handler on Desktop
	if constexpr (is_running_on_desktop) {
		mono_add_internal_call ("System.Diagnostics.Debugger::Mono_UnhandledException_internal(System.Exception)",
		                                 reinterpret_cast<const void*> (monodroid_Mono_UnhandledException_internal));
	}

	if (utils.should_log (LOG_DEFAULT)) [[unlikely]] {
		log_info_nocheck (
			LOG_DEFAULT,
			".NET Android version: %s (%s; %s); built on %s; NDK version: %s; API level: %s; MonoVM version: %s",
			BuildInfo::xa_version.data (),
			BuildInfo::architecture.data (),
			BuildInfo::kind.data (),
			BuildInfo::date.data (),
			BuildInfo::ndk_version.data (),
			BuildInfo::ndk_api_level.data (),
			mono_get_runtime_build_info ()
		);
	}

	if (FastTiming::enabled ()) [[unlikely]] {
		internal_timing->end_event (total_time_index);
	}

#if defined (RELEASE)
	if (application_config.marshal_methods_enabled) {
		xamarin_app_init (env, get_function_pointer_at_runtime);
	}
#endif // def RELEASE && def ANDROID && def NET
	startup_in_progress = false;
}

JNIEXPORT jint JNICALL
JNI_OnLoad (JavaVM *vm, void *reserved)
{
	return monodroidRuntime.Java_JNI_OnLoad (vm, reserved);
}

/* !DO NOT REMOVE! Used by the Android Designer */
JNIEXPORT void JNICALL
Java_mono_android_Runtime_init (JNIEnv *env, jclass klass, jstring lang, jobjectArray runtimeApksJava,
                                jstring runtimeNativeLibDir, jobjectArray appDirs, jobject loader,
                                [[maybe_unused]] jobjectArray externalStorageDirs, jobjectArray assembliesJava, [[maybe_unused]] jstring packageName,
                                jint apiLevel, [[maybe_unused]] jobjectArray environmentVariables)
{
	monodroidRuntime.Java_mono_android_Runtime_initInternal (
		env,
		klass,
		lang,
		runtimeApksJava,
		runtimeNativeLibDir,
		appDirs,
		0,
		loader,
		assembliesJava,
		apiLevel,
		/* isEmulator */ JNI_FALSE,
		/* haveSplitApks */ JNI_FALSE
	);
}

JNIEXPORT void JNICALL
Java_mono_android_Runtime_initInternal (JNIEnv *env, jclass klass, jstring lang, jobjectArray runtimeApksJava,
                                jstring runtimeNativeLibDir, jobjectArray appDirs, jint localDateTimeOffset, jobject loader,
                                jobjectArray assembliesJava, jint apiLevel, jboolean isEmulator,
                                jboolean haveSplitApks)
{
	monodroidRuntime.Java_mono_android_Runtime_initInternal (
		env,
		klass,
		lang,
		runtimeApksJava,
		runtimeNativeLibDir,
		appDirs,
		localDateTimeOffset,
		loader,
		assembliesJava,
		apiLevel,
		isEmulator,
		haveSplitApks
	);
}

force_inline void
MonodroidRuntime::Java_mono_android_Runtime_register (JNIEnv *env, jstring managedType, jclass nativeClass, jstring methods)
{
	size_t total_time_index;

	if (FastTiming::enabled ()) [[unlikely]] {
		total_time_index = internal_timing->start_event (TimingEventKind::RuntimeRegister);
	}

	jsize managedType_len = env->GetStringLength (managedType);
	const jchar *managedType_ptr = env->GetStringChars (managedType, nullptr);
	int methods_len = env->GetStringLength (methods);
	const jchar *methods_ptr = env->GetStringChars (methods, nullptr);

	mono_jit_thread_attach (nullptr); // There's just one domain in .net
	jnienv_register_jni_natives (managedType_ptr, managedType_len, nativeClass, methods_ptr, methods_len);

	env->ReleaseStringChars (methods, methods_ptr);
	env->ReleaseStringChars (managedType, managedType_ptr);

	if (FastTiming::enabled ()) [[unlikely]] {
		internal_timing->end_event (total_time_index, true /* uses_more_info */);

		dynamic_local_string<SENSIBLE_TYPE_NAME_LENGTH> type;
		const char *mt_ptr = env->GetStringUTFChars (managedType, nullptr);
		type.assign (mt_ptr, strlen (mt_ptr));
		env->ReleaseStringUTFChars (managedType, mt_ptr);

		internal_timing->add_more_info (total_time_index, type);
	}
}

JNIEXPORT void
JNICALL Java_mono_android_Runtime_dumpTimingData ([[maybe_unused]] JNIEnv *env, [[maybe_unused]] jclass klass)
{
	if (internal_timing == nullptr) {
		return;
	}

	internal_timing->dump ();
}

JNIEXPORT void
JNICALL Java_mono_android_Runtime_register (JNIEnv *env, [[maybe_unused]] jclass klass, jstring managedType, jclass nativeClass, jstring methods)
{
	monodroidRuntime.Java_mono_android_Runtime_register (env, managedType, nativeClass, methods);
}

char*
MonodroidRuntime::get_java_class_name_for_TypeManager (jclass klass)
{
	if (klass == nullptr || Class_getName == nullptr)
		return nullptr;

	JNIEnv *env = osBridge.ensure_jnienv ();
	jstring name = reinterpret_cast<jstring> (env->CallObjectMethod (klass, Class_getName));
	if (name == nullptr) {
		log_error (LOG_DEFAULT, "Failed to obtain Java class name for object at %p", klass);
		return nullptr;
	}

	const char *mutf8 = env->GetStringUTFChars (name, nullptr);
	if (mutf8 == nullptr) {
		log_error (LOG_DEFAULT, "Failed to convert Java class name to UTF8 (out of memory?)");
		env->DeleteLocalRef (name);
		return nullptr;
	}
	char *ret = strdup (mutf8);

	env->ReleaseStringUTFChars (name, mutf8);
	env->DeleteLocalRef (name);

	char *dot = strchr (ret, '.');
	while (dot != nullptr) {
		*dot = '/';
		dot = strchr (dot + 1, '.');
	}

	return ret;
}

JNIEnv*
get_jnienv (void)
{
	return osBridge.ensure_jnienv ();
}

JNIEXPORT void
JNICALL Java_mono_android_Runtime_propagateUncaughtException (JNIEnv *env, [[maybe_unused]] jclass klass, jobject javaThread, jthrowable javaException)
{
	monodroidRuntime.propagate_uncaught_exception (env, javaThread, javaException);
}
