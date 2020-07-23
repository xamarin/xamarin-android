#include <stdlib.h>
#include <stdarg.h>
#include <jni.h>
#include <time.h>
#include <stdio.h>
#include <string.h>
#include <strings.h>
#include <ctype.h>
#include <assert.h>
#include <errno.h>
#include <limits.h>

#include <dlfcn.h>
#include <fcntl.h>
#include <unistd.h>
#include <stdint.h>

#include <sys/time.h>
#include <sys/types.h>

#include <mono/jit/jit.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/mono-config.h>
#include <mono/metadata/mono-debug.h>
#include <mono/utils/mono-dl-fallback.h>

#include "mono_android_Runtime.h"

#if defined (DEBUG) && !defined (WINDOWS)
#include <fcntl.h>
#include <arpa/inet.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <errno.h>
#endif

#if defined (LINUX)
#include <sys/syscall.h>
#endif

#ifndef WINDOWS
#include <sys/mman.h>
#include <sys/utsname.h>
#else
#include <windef.h>
#include <winbase.h>
#include <shlobj.h>
#include <objbase.h>
#include <knownfolders.h>
#include <shlwapi.h>
#endif

#include <sys/stat.h>
#include <unistd.h>
#include <dirent.h>
#include <pthread.h>

#include "java-interop-util.h"
#include "logger.hh"

#include "monodroid.h"
#include "util.hh"
#include "debug.hh"
#include "embedded-assemblies.hh"
#include "monodroid-glue.hh"
#include "mkbundle-api.h"
#include "monodroid-glue-internal.hh"
#include "globals.hh"
#include "xamarin-app.hh"
#include "timing.hh"

#ifndef WINDOWS
#include "xamarin_getifaddrs.h"
#endif

#include "cpp-util.hh"

using namespace xamarin::android;
using namespace xamarin::android::internal;

// This is below the above because we don't want to modify the header with our internal
// implementation details as it would prevent mkbundle from working
#include "mkbundle-api.h"

#include "config.include"
#include "machine.config.include"

#ifdef WINDOWS
static const char* get_xamarin_android_msbuild_path (void);
const char *BasicAndroidSystem::SYSTEM_LIB_PATH = get_xamarin_android_msbuild_path();
#endif

/* Set of Windows-specific utility/reimplementation of Unix functions */
#ifdef WINDOWS

static char *msbuild_folder_path = nullptr;

static const char*
get_xamarin_android_msbuild_path (void)
{
	const char *suffix = "MSBuild\\Xamarin\\Android";
	char *base_path = nullptr;
	wchar_t *buffer = nullptr;

	if (msbuild_folder_path != nullptr)
		return msbuild_folder_path;

	// Get the base path for 'Program Files' on Windows
	if (!SUCCEEDED (SHGetKnownFolderPath (FOLDERID_ProgramFilesX86, 0, nullptr, &buffer))) {
		if (buffer != nullptr)
			CoTaskMemFree (buffer);
		// returns current directory if a global one couldn't be found
		return ".";
	}

	// Compute the final path
	base_path = utils.utf16_to_utf8 (buffer);
	CoTaskMemFree (buffer);
	msbuild_folder_path = utils.path_combine (base_path, suffix);
	free (base_path);

	return msbuild_folder_path;
}

static int
setenv(const char *name, const char *value, int overwrite)
{
	return androidSystem.setenv (name, value, overwrite);
}
#endif // def WINDOWS

typedef void* (*mono_mkbundle_init_ptr) (void (*)(const MonoBundledAssembly **), void (*)(const char* assembly_name, const char* config_xml),void (*) (int mode));
mono_mkbundle_init_ptr mono_mkbundle_init;

typedef void (*mono_mkbundle_initialize_mono_api_ptr) (const BundleMonoAPI *info);
mono_mkbundle_initialize_mono_api_ptr mono_mkbundle_initialize_mono_api;

void
MonodroidRuntime::setup_bundled_app (const char *dso_name)
{
	if (!application_config.is_a_bundled_app)
		return;

	static int dlopen_flags = RTLD_LAZY;
	void *libapp = nullptr;

	if (androidSystem.is_embedded_dso_mode_enabled ()) {
		log_info (LOG_DEFAULT, "bundle app: embedded DSO mode");
		libapp = androidSystem.load_dso_from_any_directories (dso_name, dlopen_flags);
	} else {
		bool needs_free = false;
		log_info (LOG_DEFAULT, "bundle app: normal mode");
		char *bundle_path = androidSystem.get_full_dso_path_on_disk (dso_name, needs_free);
		log_info (LOG_DEFAULT, "bundle_path == %s", bundle_path ? bundle_path : "<nullptr>");
		if (bundle_path == nullptr)
			return;
		log_info (LOG_BUNDLE, "Attempting to load bundled app from %s", bundle_path);
		libapp = androidSystem.load_dso (bundle_path, dlopen_flags, true);
		if (needs_free)
			delete[] bundle_path;
	}

	if (libapp == nullptr) {
		log_info (LOG_DEFAULT, "No libapp!");
		if (!androidSystem.is_embedded_dso_mode_enabled ()) {
			log_fatal (LOG_BUNDLE, "bundled app initialization error");
			exit (FATAL_EXIT_CANNOT_LOAD_BUNDLE);
		} else {
			log_info (LOG_BUNDLE, "bundled app not found in the APK, ignoring.");
			return;
		}
	}

	mono_mkbundle_initialize_mono_api = reinterpret_cast<mono_mkbundle_initialize_mono_api_ptr> (dlsym (libapp, "initialize_mono_api"));
	if (!mono_mkbundle_initialize_mono_api)
		log_error (LOG_BUNDLE, "Missing initialize_mono_api in the application");

	mono_mkbundle_init = reinterpret_cast<mono_mkbundle_init_ptr> (dlsym (libapp, "mono_mkbundle_init"));
	if (!mono_mkbundle_init)
		log_error (LOG_BUNDLE, "Missing mono_mkbundle_init in the application");
	log_info (LOG_BUNDLE, "Bundled app loaded: %s", dso_name);
}

void
MonodroidRuntime::thread_start ([[maybe_unused]] MonoProfiler *prof, [[maybe_unused]] uintptr_t tid)
{
	JNIEnv* env;
	int r;
#ifdef PLATFORM_ANDROID
	r = osBridge.get_jvm ()->AttachCurrentThread (&env, nullptr);
#else   // ndef PLATFORM_ANDROID
	r = osBridge.get_jvm ()->AttachCurrentThread (reinterpret_cast<void**>(&env), nullptr);
#endif  // ndef PLATFORM_ANDROID
	if (r != JNI_OK) {
#if DEBUG
		log_fatal (LOG_DEFAULT, "ERROR: Unable to attach current thread to the Java VM!");
		exit (FATAL_EXIT_ATTACH_JVM_FAILED);
#endif
	}
}

void
MonodroidRuntime::thread_end ([[maybe_unused]] MonoProfiler *prof, [[maybe_unused]] uintptr_t tid)
{
	int r;
	r = osBridge.get_jvm ()->DetachCurrentThread ();
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
	fprintf (jit_log, "JIT method %6s: %s elapsed: %lis:%u::%u\n", event_name, name, static_cast<long int>(diff.sec), diff.ms, diff.ns);

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
	MonoAssembly *result = nullptr;

#ifndef ANDROID
	// First check if there are any in-memory assemblies
	if (designerAssemblies.has_assemblies ()) {
		MonoDomain *domain = mono_domain_get ();
		result = designerAssemblies.try_load_assembly (domain, aname);
		if (result != nullptr) {
			log_debug (LOG_ASSEMBLY, "Loaded assembly %s from memory in domain %d", mono_assembly_name_get_name (aname), mono_domain_get_id (domain));
			return result;
		}
		log_debug (LOG_ASSEMBLY, "No in-memory data found for assembly %s", mono_assembly_name_get_name (aname));
	} else {
		log_debug (LOG_ASSEMBLY, "No in-memory assemblies detected", mono_assembly_name_get_name (aname));
	}
#endif
	const char *override_dir;
	bool found = false;

	for (uint32_t oi = 0; oi < AndroidSystem::MAX_OVERRIDES; ++oi) {
		override_dir = androidSystem.get_override_dir (oi);
		if (override_dir != nullptr && utils.directory_exists (override_dir)) {
			found = true;
			break;
		}
	}
	if (!found)
		return nullptr;

	const char *culture = reinterpret_cast<const char*> (mono_assembly_name_get_culture (aname));
	const char *name    = reinterpret_cast<const char*> (mono_assembly_name_get_name (aname));
	char *pname_raw_ptr;

	if (culture != nullptr && *culture != '\0')
		pname_raw_ptr = utils.path_combine (culture, name);
	else
		pname_raw_ptr = utils.strdup_new (name);

	simple_pointer_guard<char[]> pname (pname_raw_ptr);

	constexpr const char *format_none = "%s" MONODROID_PATH_SEPARATOR "%s";
	constexpr const char *format_dll  = "%s" MONODROID_PATH_SEPARATOR "%s.dll";

	for (uint32_t oi = 0; oi < AndroidSystem::MAX_OVERRIDES; ++oi) {
		override_dir = androidSystem.get_override_dir (oi);
		if (override_dir == nullptr || !utils.directory_exists (override_dir))
			continue;

		const char *format = utils.ends_with (name, ".dll") ? format_none : format_dll;
		simple_pointer_guard<char, false> fullpath (utils.monodroid_strdup_printf (format, override_dir, pname.get ()));

		log_info (LOG_ASSEMBLY, "open_from_update_dir: trying to open assembly: %s\n", static_cast<const char*>(fullpath));
		if (utils.file_exists (fullpath))
			result = mono_assembly_open_full (fullpath, nullptr, 0);
		if (result != nullptr) {
			// TODO: register .mdb, .pdb file
			break;
		}
	}

	if (result && utils.should_log (LOG_ASSEMBLY)) {
		log_info_nocheck (LOG_ASSEMBLY, "open_from_update_dir: loaded assembly: %p\n", result);
	}
	return result;
}
#endif

bool
MonodroidRuntime::should_register_file ([[maybe_unused]] const char *filename)
{
#ifndef RELEASE
	for (size_t i = 0; i < AndroidSystem::MAX_OVERRIDES; ++i) {
		const char *odir = androidSystem.get_override_dir (i);
		if (odir == nullptr)
			continue;

		simple_pointer_guard<char[]> p (utils.path_combine (odir, filename));
		bool  exists  = utils.file_exists (p);

		if (exists) {
			log_info (LOG_ASSEMBLY, "should not register '%s' as it exists in the override directory '%s'", filename, odir);
			return !exists;
		}
	}
#endif
	return true;
}

inline void
MonodroidRuntime::gather_bundled_assemblies (jstring_array_wrapper &runtimeApks, size_t *out_user_assemblies_count)
{
#if defined(DEBUG) || !defined (ANDROID)
	if (application_config.instant_run_enabled) {
		for (size_t i = 0; i < AndroidSystem::MAX_OVERRIDES; ++i) {
			const char *p = androidSystem.get_override_dir (i);
			if (!utils.directory_exists (p))
				continue;
			log_info (LOG_ASSEMBLY, "Loading TypeMaps from %s", p);
			embeddedAssemblies.try_load_typemaps_from_directory (p);
		}
	}
#endif

	int64_t apk_count = static_cast<int64_t>(runtimeApks.get_length ());
	size_t prev_num_assemblies = 0;
	for (int64_t i = apk_count - 1; i >= 0; --i) {
		jstring_wrapper &apk_file = runtimeApks [static_cast<size_t>(i)];

		size_t cur_num_assemblies  = embeddedAssemblies.register_from<should_register_file> (apk_file.get_cstr ());

		if (strstr (apk_file.get_cstr (), "/Mono.Android.DebugRuntime") == nullptr &&
		    strstr (apk_file.get_cstr (), "/Mono.Android.Platform.ApiLevel_") == nullptr)
			*out_user_assemblies_count += (cur_num_assemblies - prev_num_assemblies);
		prev_num_assemblies = cur_num_assemblies;
	}
}

#if defined (DEBUG) && !defined (WINDOWS)
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
	char *val;

	if (!(androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_GDB_PROPERTY, &val) > 0))
		return;

	if (strstr (val, "wait:") == val) {
		/*
		 * The form of the property should be: 'wait:<timestamp>', where <timestamp> should be
		 * the output of date +%s in the android shell.
		 * If this property is set, wait for a native debugger to attach by spinning in a loop.
		 * The debugger can break the wait by setting 'monodroid_gdb_wait' to 0.
		 * If the current time is later than <timestamp> + 10s, the property is ignored.
		 */
		bool do_wait = true;

		long long v = atoll (val + strlen ("wait:"));
		if (v > 100000) {
			time_t secs = time (nullptr);

			if (v + 10 < secs) {
				log_warn (LOG_DEFAULT, "Found stale %s property with value '%s', not waiting.", Debug::DEBUG_MONO_GDB_PROPERTY, val);
				do_wait = false;
			}
		}

		wait_for_gdb = do_wait;
	}

	delete[] val;
}

#if defined (DEBUG) && !defined (WINDOWS)
int
MonodroidRuntime::parse_runtime_args (char *runtime_args, RuntimeOptions *options)
{
	char **args, **ptr;

	if (runtime_args == nullptr)
		return 1;

	options->timeout_time = 0;

	args = utils.monodroid_strsplit (runtime_args, ",", 0);

	for (ptr = args; ptr && *ptr; ptr++) {
		const char *arg = *ptr;

		if (!strncmp (arg, "debug", 5)) {
			char *host = nullptr;
			int sdb_port = 1000, out_port = -1;

			options->debug = 1;

			if (arg[5] == '=') {
				const char *sep, *endp;

				arg += 6;
				sep = strchr (arg, ':');
				if (sep != nullptr) {
					size_t arg_len = static_cast<size_t>(sep - arg);
					size_t alloc_size = ADD_WITH_OVERFLOW_CHECK (size_t, arg_len, 1);
					host = new char [alloc_size];
					memset (host, 0x00, alloc_size);
					strncpy (host, arg, arg_len);
					arg = sep+1;

					sdb_port = (int) strtol (arg, const_cast<char**> (&endp), 10);
					if (endp == arg) {
						log_error (LOG_DEFAULT, "Invalid --debug argument.");
						continue;
					} else if (*endp == ':') {
						arg = endp+1;
						out_port = (int) strtol (arg, const_cast<char**> (&endp), 10);
						if ((endp == arg) || (*endp != '\0')) {
							log_error (LOG_DEFAULT, "Invalid --debug argument.");
							continue;
						}
					} else if (*endp != '\0') {
						log_error (LOG_DEFAULT, "Invalid --debug argument.");
						continue;
					}
				}
			} else if (arg[5] != '\0') {
				log_error (LOG_DEFAULT, "Invalid --debug argument.");
				continue;
			}

			if (!host)
				host = utils.strdup_new ("10.0.2.2");

			if (sdb_port < 0 || sdb_port > USHRT_MAX) {
				log_error (LOG_DEFAULT, "Invalid SDB port value %d", sdb_port);
				continue;
			}

			if (out_port > USHRT_MAX) {
				log_error (LOG_DEFAULT, "Invalid output port value %d", out_port);
				continue;
			}

			options->host = host;
			options->sdb_port = static_cast<uint16_t>(sdb_port);
			options->out_port = out_port == -1 ? 0 : static_cast<uint16_t>(out_port);
		} else if (!strncmp (arg, "timeout=", 8)) {
			char *endp;

			arg += sizeof ("timeout");
			options->timeout_time = strtoll (arg, &endp, 10);
			if ((endp == arg) || (*endp != '\0'))
				log_error (LOG_DEFAULT, "Invalid --timeout argument.");
			continue;
		} else if (!strncmp (arg, "server=", 7)) {
			arg += sizeof ("server");
			options->server = *arg == 'y' || *arg == 'Y';
			continue;
		} else if (!strncmp (arg, "loglevel=", 9)) {
			char *endp;

			arg += sizeof ("loglevel");
			options->loglevel = static_cast<int>(strtol (arg, &endp, 10));
			if ((endp == arg) || (*endp != '\0'))
				log_error (LOG_DEFAULT, "Invalid --loglevel argument.");
		} else {
				log_error (LOG_DEFAULT, "Unknown runtime argument: '%s'", arg);
			continue;
		}
	}

	utils.monodroid_strfreev (args);
	return 1;
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
MonodroidRuntime::mono_runtime_init ([[maybe_unused]] char *runtime_args)
{
#if defined (DEBUG) && !defined (WINDOWS)
	RuntimeOptions options;
	int64_t cur_time;
	memset(&options, 0, sizeof (options));

	cur_time = time (nullptr);

	if (!parse_runtime_args (runtime_args, &options)) {
		log_error (LOG_DEFAULT, "Failed to parse runtime args: '%s'", runtime_args);
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
			"--debugger-agent=transport=dt_socket,loglevel=%d,address=%s:%d,%sembedding=1",
			loglevel,
			options.host,
			options.sdb_port,
			options.server ? "server=y," : ""
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
				exit (FATAL_EXIT_DEBUGGER_CONNECT);
			}

			sockaddr_in addr;
			memset (&addr, 0, sizeof (addr));

			addr.sin_family = AF_INET;
			addr.sin_port = htons (options.out_port);

			int r;
			if ((r = inet_pton (AF_INET, options.host, &addr.sin_addr)) != 1) {
				log_error (LOG_DEBUGGER, "Could not setup a socket for stdout and stderr: %s",
						r == -1 ? strerror (errno) : "address not parseable in the specified address family");
				exit (FATAL_EXIT_DEBUGGER_CONNECT);
			}

			if (options.server) {
				int accepted = monodroid_debug_accept (sock, addr);
				log_warn (LOG_DEBUGGER, "Accepted stdout connection: %d", accepted);
				if (accepted < 0) {
					log_fatal (LOG_DEBUGGER, "Error accepting stdout and stderr (%s:%d): %s",
							     options.host, options.out_port, strerror (errno));
					exit (FATAL_EXIT_DEBUGGER_CONNECT);
				}

				dup2 (accepted, 1);
				dup2 (accepted, 2);
			} else {
				if (monodroid_debug_connect (sock, addr) != 1) {
					log_fatal (LOG_DEBUGGER, "Error connecting stdout and stderr (%s:%d): %s",
							     options.host, options.out_port, strerror (errno));
					exit (FATAL_EXIT_DEBUGGER_CONNECT);
				}

				dup2 (sock, 1);
				dup2 (sock, 2);
			}
		}

		if (debug.enable_soft_breakpoints ()) {
			constexpr char soft_breakpoints[] = "--soft-breakpoints";
			debug_options[1] = const_cast<char*> (soft_breakpoints);
			mono_jit_parse_options (2, debug_options);
		} else {
			mono_jit_parse_options (1, debug_options);
		}

		mono_debug_init (MONO_DEBUG_FORMAT_MONO);
	} else {
		set_debug_options ();
	}
#else
	set_debug_options ();
#endif

	bool log_methods = utils.should_log (LOG_TIMING) && !(log_timing_categories & LOG_TIMING_BARE);
	if (XA_UNLIKELY (log_methods)) {
		simple_pointer_guard<char[]> jit_log_path = utils.path_combine (androidSystem.get_override_dir (0), "methods.txt");
		jit_log = utils.monodroid_fopen (jit_log_path, "a");
		utils.set_world_accessable (jit_log_path);
	}

	profiler_handle = mono_profiler_create (nullptr);
	mono_profiler_set_thread_started_callback (profiler_handle, thread_start);
	mono_profiler_set_thread_stopped_callback (profiler_handle, thread_end);

	if (XA_UNLIKELY (log_methods)) {
		jit_time.mark_start ();
		mono_profiler_set_jit_begin_callback (profiler_handle, jit_begin);
		mono_profiler_set_jit_done_callback (profiler_handle, jit_done);
		mono_profiler_set_jit_failed_callback (profiler_handle, jit_failed);
	}

	parse_gdb_options ();

	if (wait_for_gdb) {
		log_warn (LOG_DEFAULT, "Waiting for gdb to attach...");
		while (monodroid_gdb_wait) {
			sleep (1);
		}
	}

	char *prop_val;
	/* Additional runtime arguments passed to mono_jit_parse_options () */
	if (androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_RUNTIME_ARGS_PROPERTY, &prop_val) > 0) {
		char **ptr;

		log_warn (LOG_DEBUGGER, "passing '%s' as extra arguments to the runtime.\n", prop_val);

		char **args = utils.monodroid_strsplit (prop_val, " ", 0);
		int argc = 0;
		delete[] prop_val;

		for (ptr = args; *ptr; ptr++)
			argc ++;

		mono_jit_parse_options (argc, args);
	}

	mono_set_signal_chaining (1);
	mono_set_crash_chaining (1);

	osBridge.register_gc_hooks ();

	if (mono_mkbundle_initialize_mono_api) {
		BundleMonoAPI bundle_mono_api = {
			.mono_register_bundled_assemblies = mono_register_bundled_assemblies,
			.mono_register_config_for_assembly = mono_register_config_for_assembly,
			.mono_jit_set_aot_mode = reinterpret_cast<void (*)(int)>(mono_jit_set_aot_mode),
			.mono_aot_register_module = mono_aot_register_module,
			.mono_config_parse_memory = mono_config_parse_memory,
			.mono_register_machine_config = reinterpret_cast<void (*)(const char *)>(mono_register_machine_config),
		};

		/* The initialization function copies the struct */
		mono_mkbundle_initialize_mono_api (&bundle_mono_api);
	}

	if (mono_mkbundle_init)
		mono_mkbundle_init (mono_register_bundled_assemblies, mono_register_config_for_assembly, reinterpret_cast<void (*)(int)>(mono_jit_set_aot_mode));

	/*
	 * Assembly preload hooks are invoked in _reverse_ registration order.
	 * Looking for assemblies from the update dir takes precedence over
	 * everything else, and thus must go LAST.
	 */
	embeddedAssemblies.install_preload_hooks ();
#ifndef RELEASE
	mono_install_assembly_preload_hook (open_from_update_dir, nullptr);
#endif
}

MonoDomain*
MonodroidRuntime::create_domain (JNIEnv *env, jstring_array_wrapper &runtimeApks, bool is_root_domain)
{
	size_t user_assemblies_count   = 0;;

	gather_bundled_assemblies (runtimeApks, &user_assemblies_count);

	if (!mono_mkbundle_init && user_assemblies_count == 0 && androidSystem.count_override_assemblies () == 0 && !is_running_on_desktop) {
		log_fatal (LOG_DEFAULT, "No assemblies found in '%s' or '%s'. Assuming this is part of Fast Deployment. Exiting...",
		           androidSystem.get_override_dir (0),
		           (AndroidSystem::MAX_OVERRIDES > 1 && androidSystem.get_override_dir (1) != nullptr) ? androidSystem.get_override_dir (1) : "<unavailable>");
		exit (FATAL_EXIT_NO_ASSEMBLIES);
	}

	MonoDomain *domain;
	if (is_root_domain) {
		domain = mono_jit_init_version (const_cast<char*> ("RootDomain"), const_cast<char*> ("mobile"));
	} else {
		MonoDomain* root_domain = mono_get_root_domain ();
		simple_pointer_guard<char[], false> domain_name = utils.monodroid_strdup_printf ("MonoAndroidDomain%d", android_api_level);
		domain = utils.monodroid_create_appdomain (root_domain, domain_name, /*shadow_copy:*/ 1, /*shadow_directory:*/ androidSystem.get_override_dir (0));
	}

	if constexpr (is_running_on_desktop) {
		if (is_root_domain) {
			// Check that our corlib is coherent with the version of Mono we loaded otherwise
			// tell the IDE that the project likely need to be recompiled.
			simple_pointer_guard<char, false> corlib_error_message_guard = const_cast<char*>(mono_check_corlib_version ());
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
	log_warn (LOG_DEFAULT, "[1] %s", __PRETTY_FUNCTION__);

	if (version < 14) {
		log_warn (LOG_DEFAULT, "[1] running on API < 14, java_System will be null");
		java_System = nullptr;
		java_System_identityHashCode = 0;
		return 0;
	}

	java_System = utils.get_class_from_runtime_field(env, runtimeClass, "java_lang_System", true);
	log_warn (LOG_DEFAULT, "[1] java_System == %p", java_System);
	java_System_identityHashCode = env->GetStaticMethodID (java_System, "identityHashCode", "(Ljava/lang/Object;)I");

	return 1;
}

int
MonodroidRuntime::get_display_dpi (float *x_dpi, float *y_dpi)
{
	if (!x_dpi) {
		log_error (LOG_DEFAULT, "Internal error: x_dpi parameter missing in get_display_dpi");
		return -1;
	}

	if (!y_dpi) {
		log_error (LOG_DEFAULT, "Internal error: y_dpi parameter missing in get_display_dpi");
		return -1;
	}

	MonoDomain *domain = nullptr;
	if (runtime_GetDisplayDPI == nullptr) {
		domain = mono_get_root_domain ();
		MonoAssembly *assm = utils.monodroid_load_assembly (domain, "Mono.Android");;

		MonoImage *image = nullptr;
		if (assm != nullptr)
			image = mono_assembly_get_image  (assm);

		MonoClass *environment = nullptr;
		if (image != nullptr)
			environment = utils.monodroid_get_class_from_image (domain, image, "Android.Runtime", "AndroidEnvironment");

		if (environment != nullptr)
			runtime_GetDisplayDPI = mono_class_get_method_from_name (environment, "GetDisplayDPI", 2);
	}

	if (!runtime_GetDisplayDPI) {
		*x_dpi = DEFAULT_X_DPI;
		*y_dpi = DEFAULT_Y_DPI;
		return 0;
	}

	void* args [] = {
		x_dpi,
		y_dpi,
	};

	MonoObject *exc = nullptr;
	utils.monodroid_runtime_invoke (domain != nullptr ? domain : mono_get_root_domain (), runtime_GetDisplayDPI, nullptr, args, &exc);
	if (exc != nullptr) {
		*x_dpi = DEFAULT_X_DPI;
		*y_dpi = DEFAULT_Y_DPI;
	}

	return 0;
}

inline void
MonodroidRuntime::lookup_bridge_info (MonoDomain *domain, MonoImage *image, const OSBridge::MonoJavaGCBridgeType *type, OSBridge::MonoJavaGCBridgeInfo *info)
{
	info->klass             = utils.monodroid_get_class_from_image (domain, image, type->_namespace, type->_typename);
	info->handle            = mono_class_get_field_from_name (info->klass, const_cast<char*> ("handle"));
	info->handle_type       = mono_class_get_field_from_name (info->klass, const_cast<char*> ("handle_type"));
	info->refs_added        = mono_class_get_field_from_name (info->klass, const_cast<char*> ("refs_added"));
	info->weak_handle       = mono_class_get_field_from_name (info->klass, const_cast<char*> ("weak_handle"));
	if (info->klass == NULL || info->handle == NULL || info->handle_type == NULL ||
			info->refs_added == NULL || info->weak_handle == NULL) {
		log_fatal (LOG_DEFAULT, "The type `%s.%s` is missing required instance fields! handle=%p handle_type=%p refs_added=%p weak_handle=%p",
				type->_namespace, type->_typename,
				info->handle,
				info->handle_type,
				info->refs_added,
				info->weak_handle);
		exit (FATAL_EXIT_MONO_MISSING_SYMBOLS);
	}
}

void
MonodroidRuntime::init_android_runtime (MonoDomain *domain, JNIEnv *env, jclass runtimeClass, jobject loader)
{
	mono_add_internal_call ("Java.Interop.TypeManager::monodroid_typemap_java_to_managed", reinterpret_cast<const void*>(typemap_java_to_managed));
	mono_add_internal_call ("Android.Runtime.JNIEnv::monodroid_typemap_managed_to_java", reinterpret_cast<const void*>(typemap_managed_to_java));

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

	// GC threshold is 90% of the max GREF count
	init.grefGcThreshold        = static_cast<int>(androidSystem.get_gref_gc_threshold ());

	log_warn (LOG_GC, "GREF GC Threshold: %i", init.grefGcThreshold);

	init.grefClass = utils.get_class_from_runtime_field (env, runtimeClass, "java_lang_Class", true);
	Class_getName  = env->GetMethodID (init.grefClass, "getName", "()Ljava/lang/String;");
	init.Class_forName = env->GetStaticMethodID (init.grefClass, "forName", "(Ljava/lang/String;ZLjava/lang/ClassLoader;)Ljava/lang/Class;");

	MonoAssembly *assm = utils.monodroid_load_assembly (domain, "Mono.Android");
	MonoImage *image = mono_assembly_get_image (assm);

	uint32_t i = 0;

	for ( ; i < OSBridge::NUM_XA_GC_BRIDGE_TYPES; ++i) {
		lookup_bridge_info (domain, image, &osBridge.get_java_gc_bridge_type (i), &osBridge.get_java_gc_bridge_info (i));
	}

	// TODO: try looking up the method by its token
	MonoClass *runtime = utils.monodroid_get_class_from_image (domain, image, "Android.Runtime", "JNIEnv");
	MonoMethod *method = mono_class_get_method_from_name (runtime, "Initialize", 1);

	if (method == nullptr) {
		log_fatal (LOG_DEFAULT, "INTERNAL ERROR: Unable to find Android.Runtime.JNIEnv.Initialize!");
		exit (FATAL_EXIT_MISSING_INIT);
	}

	MonoAssembly    *ji_assm    = utils.monodroid_load_assembly (domain, "Java.Interop");
	MonoImage       *ji_image   = mono_assembly_get_image  (ji_assm);
	for ( ; i < OSBridge::NUM_XA_GC_BRIDGE_TYPES + OSBridge::NUM_JI_GC_BRIDGE_TYPES; ++i) {
		lookup_bridge_info (domain, ji_image, &osBridge.get_java_gc_bridge_type (i), &osBridge.get_java_gc_bridge_info (i));
	}

	/* If running on desktop, we may be swapping in a new Mono.Android image when calling this
	 * so always make sure we have the freshest handle to the method.
	 */
	if (registerType == nullptr || is_running_on_desktop) {
		registerType = mono_class_get_method_from_name (runtime, "RegisterJniNatives", 5);
	}
	if (registerType == nullptr) {
		log_fatal (LOG_DEFAULT, "INTERNAL ERROR: Unable to find Android.Runtime.JNIEnv.RegisterJniNatives!");
		exit (FATAL_EXIT_CANNOT_FIND_JNIENV);
	}
	MonoClass *android_runtime_jnienv = runtime;
	MonoClassField *bridge_processing_field = mono_class_get_field_from_name (runtime, const_cast<char*> ("BridgeProcessing"));
	if (android_runtime_jnienv ==nullptr || bridge_processing_field == nullptr) {
		log_fatal (LOG_DEFAULT, "INTERNAL_ERROR: Unable to find Android.Runtime.JNIEnv.BridgeProcessing");
		exit (FATAL_EXIT_CANNOT_FIND_JNIENV);
	}

	jclass lrefLoaderClass = env->GetObjectClass (loader);
	init.Loader_loadClass     = env->GetMethodID (lrefLoaderClass, "loadClass", "(Ljava/lang/String;)Ljava/lang/Class;");
	env->DeleteLocalRef (lrefLoaderClass);

	init.grefLoader           = env->NewGlobalRef (loader);
	init.grefIGCUserPeer      = utils.get_class_from_runtime_field (env, runtimeClass, "mono_android_IGCUserPeer", true);

	osBridge.initialize_on_runtime_init (env, runtimeClass);

	log_info (LOG_DEFAULT, "Calling into managed runtime init");

	timing_period partial_time;
	if (XA_UNLIKELY (utils.should_log (LOG_TIMING)))
		partial_time.mark_start ();

	void *args [] = {
		&init,
	};
	utils.monodroid_runtime_invoke (domain, method, nullptr, args, nullptr);

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		partial_time.mark_end ();
		Timing::info (partial_time, "Runtime.init: end native-to-managed transition");
	}
}

inline MonoClass*
MonodroidRuntime::get_android_runtime_class (MonoDomain *domain)
{
	MonoAssembly *assm = utils.monodroid_load_assembly (domain, "Mono.Android");
	MonoImage *image   = mono_assembly_get_image (assm);
	MonoClass *runtime = utils.monodroid_get_class_from_image (domain, image, "Android.Runtime", "JNIEnv");

	return runtime;
}

inline void
MonodroidRuntime::shutdown_android_runtime (MonoDomain *domain)
{
	MonoClass *runtime = get_android_runtime_class (domain);
	MonoMethod *method = mono_class_get_method_from_name (runtime, "Exit", 0);

	utils.monodroid_runtime_invoke (domain, method, nullptr, nullptr, nullptr);
}

inline void
MonodroidRuntime::propagate_uncaught_exception (MonoDomain *domain, JNIEnv *env, jobject javaThread, jthrowable javaException)
{
	MonoClass *runtime = get_android_runtime_class (domain);
	MonoMethod *method = mono_class_get_method_from_name (runtime, "PropagateUncaughtException", 3);

	void* args[] = {
		&env,
		&javaThread,
		&javaException,
	};
	utils.monodroid_runtime_invoke (domain, method, nullptr, args, nullptr);
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

inline int
MonodroidRuntime::convert_dl_flags (int flags)
{
	int lflags = flags & static_cast<int> (MONO_DL_LOCAL) ? 0: RTLD_GLOBAL;

	if (flags & static_cast<int> (MONO_DL_LAZY))
		lflags |= RTLD_LAZY;
	else
		lflags |= RTLD_NOW;
	return lflags;
}

inline void*
MonodroidRuntime::monodroid_dlopen_log_and_return (void *handle, char **err, const char *full_name, bool free_memory)
{
	if (handle == nullptr && err != nullptr) {
		*err = utils.monodroid_strdup_printf ("Could not load library: Library '%s' not found.", full_name);
	}

	if (free_memory) {
		delete[] full_name;
	}

	return handle;
}

void*
MonodroidRuntime::monodroid_dlopen (const char *name, int flags, char **err, [[maybe_unused]] void *user_data)
{
	int dl_flags = monodroidRuntime.convert_dl_flags (flags);
	bool libmonodroid_fallback = false;

	/* name is nullptr when we're P/Invoking __Internal, so remap to libmonodroid */
	if (name == nullptr) {
		name = "libmonodroid.so";
		libmonodroid_fallback = TRUE;
	}

	void *h = androidSystem.load_dso_from_any_directories (name, dl_flags);
	if (h != nullptr) {
		return monodroid_dlopen_log_and_return (h, err, name, false);
	}

	if (libmonodroid_fallback) {
		char *full_name = utils.path_combine (AndroidSystem::SYSTEM_LIB_PATH, "libmonodroid.so");
		h = androidSystem.load_dso (full_name, dl_flags, false);
		return monodroid_dlopen_log_and_return (h, err, full_name, true);
	}

	if (!utils.ends_with (name, ".dll.so") && !utils.ends_with (name, ".exe.so")) {
		return monodroid_dlopen_log_and_return (h, err, name, false);
	}

	char *basename_part = const_cast<char*> (strrchr (name, '/'));
	if (basename_part != nullptr)
		basename_part++;
	else
		basename_part = (char*)name;

	simple_pointer_guard<char[]> basename = utils.string_concat ("libaot-", basename_part);
	h = androidSystem.load_dso_from_any_directories (basename, dl_flags);

	if (h != nullptr && XA_UNLIKELY (utils.should_log (LOG_ASSEMBLY)))
		log_info_nocheck (LOG_ASSEMBLY, "Loaded AOT image '%s'", static_cast<const char*>(basename));

	return h;
}

void*
MonodroidRuntime::monodroid_dlsym (void *handle, const char *name, char **err, [[maybe_unused]] void *user_data)
{
	void *s;

	s = dlsym (handle, name);

	if (!s && err) {
		*err = utils.monodroid_strdup_printf ("Could not find symbol '%s'.", name);
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
MonodroidRuntime::create_xdg_directory (jstring_wrapper& home, const char *relativePath, const char *environmentVariableName)
{
	simple_pointer_guard<char> dir = utils.path_combine (home.get_cstr (), relativePath);
	log_info (LOG_DEFAULT, "Creating XDG directory: %s", static_cast<char*>(dir));
	int rv = utils.create_directory (dir, DEFAULT_DIRECTORY_MODE);
	if (rv < 0 && errno != EEXIST)
		log_warn (LOG_DEFAULT, "Failed to create XDG directory %s. %s", static_cast<char*>(dir), strerror (errno));
	if (environmentVariableName)
		setenv (environmentVariableName, dir, 1);
}

inline void
MonodroidRuntime::create_xdg_directories_and_environment (jstring_wrapper &homeDir)
{
	create_xdg_directory (homeDir, ".local/share", "XDG_DATA_HOME");
	create_xdg_directory (homeDir, ".config", "XDG_CONFIG_HOME");
}

#if DEBUG
void
MonodroidRuntime::set_debug_env_vars (void)
{
	char *value;

	if (androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_ENV_PROPERTY, &value) == 0)
		return;

	char **args = utils.monodroid_strsplit (value, "|", 0);
	delete[] value;

	for (char **ptr = args; ptr && *ptr; ptr++) {
		char *arg = *ptr;
		char *v = strchr (arg, '=');
		if (v) {
			*v = '\0';
			++v;
		} else {
			constexpr char one[] = "1";
			v = const_cast<char*> (one);
		}
		setenv (arg, v, 1);
		log_info (LOG_DEFAULT, "Env variable '%s' set to '%s'.", arg, v);
	}
	utils.monodroid_strfreev (args);
}
#endif /* DEBUG */

inline void
MonodroidRuntime::set_trace_options (void)
{
	char *value;

	if (androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_TRACE_PROPERTY, &value) == 0)
		return;

	mono_jit_set_trace_options (value);
	delete[] value;
}

inline void
MonodroidRuntime::set_profile_options ()
{
	constexpr const char mlpd_ext[] = "mlpd";
	constexpr const char aot_ext[] = "aotprofile";

	constexpr const char output_arg[] = "output=";
	constexpr const size_t output_arg_len = sizeof(output_arg) - 1;

	char *value;
	if (androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_PROFILE_PROPERTY, &value) == 0)
		return;

	char *output = nullptr;
	char *delimiter = strchr (value, ',');
	while (delimiter != nullptr) {
		char *arg = delimiter + 1;
		if (*arg == '\0') {
			break;
		}

		if (strncmp (arg, output_arg, output_arg_len) != 0) {
			delimiter = strchr (arg, ',');
			continue;
		}

		arg += output_arg_len;
		if (*arg == '\0') {
			break; // empty...
		}

		delimiter = strchr (arg, ',');
		if (delimiter == nullptr) {
			output = utils.strdup_new (arg);
		} else {
			output = utils.strdup_new (arg, static_cast<size_t>(delimiter - arg));
		}
		break;
	}

	if (output == nullptr) {
		const char* col = strchr (value, ':');
		char *extension;
		bool  extension_needs_free = false;

		if ((col != nullptr && strncmp (value, "log:", 4) == 0) || strcmp (value, "log") == 0)
			extension = const_cast<char*>(mlpd_ext);
		else if ((col != nullptr && !strncmp (value, "aot:", 4)) || !strcmp (value, "aot"))
			extension = const_cast<char*>(aot_ext);
		else if ((col != nullptr && strncmp (value, "default:", 8) == 0) || strcmp (value, "default") == 0)
			extension = const_cast<char*>(mlpd_ext);
		else {
			size_t len = col != nullptr ? static_cast<size_t>(col - value) : strlen (value);
			size_t alloc_size = ADD_WITH_OVERFLOW_CHECK (size_t, len, 1);
			extension = new char [alloc_size];
			strncpy (extension, value, len);
			extension [len] = '\0';
			extension_needs_free = true;
		}

		output = utils.string_concat (androidSystem.get_override_dir (0), MONODROID_PATH_SEPARATOR, "profile.", extension);
		char *ovalue = utils.string_concat (value, col == nullptr ? ":" : ",", output_arg, output);

		if (extension_needs_free)
			delete[] extension;
		delete[] value;
		value = ovalue;
	}

	/*
	 * libmono-profiler-log.so profiler won't overwrite existing files.
	 * Remove it For Great Justice^H^H^H to preserve my sanity!
	 */
	unlink (output);

	log_warn (LOG_DEFAULT, "Initializing profiler with options: %s", value);
	debug.monodroid_profiler_load (androidSystem.get_runtime_libdir (), value, output);

	delete[] value;
	delete[] output;
}

/*
Disable LLVM signal handlers.

This happens when RenderScript needs to be compiled. See https://bugzilla.xamarin.com/show_bug.cgi?id=18016

This happens only on first run of the app. LLVM is used to compiled the RenderScript scripts. LLVM, been
a nice and smart library installs a ton of signal handlers and don't chain at all, completely breaking us.

This is a hack to set llvm::DisablePrettyStackTrace to true and avoid this source of signal handlers.

*/
void
MonodroidRuntime::disable_external_signal_handlers (void)
{
	if (!androidSystem.is_mono_llvm_enabled ())
		return;

	void *llvm  = androidSystem.load_dso ("libLLVM.so", RTLD_LAZY, TRUE);
	if (llvm) {
		bool *disable_signals = reinterpret_cast<bool*> (dlsym (llvm, "_ZN4llvm23DisablePrettyStackTraceE"));
		if (disable_signals) {
			*disable_signals = true;
			log_info (LOG_DEFAULT, "Disabled LLVM signal trapping");
		}
		//MUST NOT dlclose to ensure we don't lose the hack
	}
}

inline void
MonodroidRuntime::load_assembly (MonoDomain *domain, jstring_wrapper &assembly)
{
	timing_period total_time;
	if (XA_UNLIKELY (utils.should_log (LOG_TIMING)))
		total_time.mark_start ();

	const char *assm_name = assembly.get_cstr ();
	MonoAssemblyName *aname;

	aname = mono_assembly_name_new (assm_name);

#ifndef ANDROID
	if (designerAssemblies.has_assemblies () && designerAssemblies.try_load_assembly (domain, aname) != nullptr) {
		log_debug (LOG_ASSEMBLY, "Dynamically opened assembly %s", mono_assembly_name_get_name (aname));
	} else
#endif
	if (domain != mono_domain_get ()) {
		MonoDomain *current = mono_domain_get ();
		mono_domain_set (domain, FALSE);
		mono_assembly_load_full (aname, NULL, NULL, 0);
		mono_domain_set (current, FALSE);
	} else {
		mono_assembly_load_full (aname, NULL, NULL, 0);
	}

	mono_assembly_name_free (aname);

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		total_time.mark_end ();
		TIMING_LOG_INFO (total_time, "Assembly load: %s preloaded", assm_name);
	}
}

inline void
MonodroidRuntime::load_assemblies (MonoDomain *domain, jstring_array_wrapper &assemblies)
{
	timing_period total_time;
	if (XA_UNLIKELY (utils.should_log (LOG_TIMING)))
		total_time.mark_start ();

	for (size_t i = 0; i < assemblies.get_length (); ++i) {
		jstring_wrapper &assembly = assemblies [i];
		load_assembly (domain, assembly);
	}

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		total_time.mark_end ();

		TIMING_LOG_INFO (total_time, "Finished loading assemblies: preloaded %u assemblies", assemblies.get_length ());
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
												bool force_preload_assemblies)
{
	MonoDomain* domain = create_domain (env, runtimeApks, is_root_domain);

	// When running on desktop, the root domain is only a dummy so don't initialize it
	if constexpr (is_running_on_desktop) {
		if (is_root_domain) {
			return domain;
		}
	}

#ifndef ANDROID
	if (assembliesBytes != nullptr)
		designerAssemblies.add_or_update_from_java (domain, env, assemblies, assembliesBytes, assembliesPaths);
#endif
	if (androidSystem.is_assembly_preload_enabled () || (is_running_on_desktop && force_preload_assemblies))
		load_assemblies (domain, assemblies);
	init_android_runtime (domain, env, runtimeClass, loader);

	osBridge.add_monodroid_domain (domain);

	return domain;
}

MonoReflectionType*
MonodroidRuntime::typemap_java_to_managed (MonoString *java_type_name)
{
	return embeddedAssemblies.typemap_java_to_managed (java_type_name);
}

const char*
MonodroidRuntime::typemap_managed_to_java (MonoReflectionType *type, const uint8_t *mvid)
{
	return embeddedAssemblies.typemap_managed_to_java (type, mvid);
}

inline void
MonodroidRuntime::Java_mono_android_Runtime_initInternal (JNIEnv *env, jclass klass, jstring lang, jobjectArray runtimeApksJava,
                                                          jstring runtimeNativeLibDir, jobjectArray appDirs, jobject loader,
                                                          [[maybe_unused]] jobjectArray externalStorageDirs, jobjectArray assembliesJava,
                                                          jint apiLevel, jboolean embeddedDSOsEnabled, jboolean isEmulator)
{
	init_logging_categories ();

	timing_period total_time;
	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		timing = new Timing ();
		total_time.mark_start ();
	}

	android_api_level = apiLevel;
	androidSystem.set_embedded_dso_mode_enabled ((bool) embeddedDSOsEnabled);
	androidSystem.set_running_in_emulator (isEmulator);

	java_TimeZone = utils.get_class_from_runtime_field (env, klass, "java_util_TimeZone", true);

	utils.monodroid_store_package_name (application_config.android_package_name);

	jstring_wrapper jstr (env, lang);
	set_environment_variable ("LANG", jstr);

	androidSystem.setup_environment ();

	jstring_array_wrapper applicationDirs (env, appDirs);
	jstring_wrapper &home = applicationDirs[0];
	set_environment_variable_for_directory ("TMPDIR", applicationDirs[1]);
	set_environment_variable_for_directory ("HOME", home);
	create_xdg_directories_and_environment (home);
	androidSystem.set_primary_override_dir (home);

	disable_external_signal_handlers ();

	jstring_array_wrapper runtimeApks (env, runtimeApksJava);
	androidSystem.setup_app_library_directories (runtimeApks, applicationDirs, apiLevel);

	init_reference_logging (androidSystem.get_primary_override_dir ());
	androidSystem.create_update_dir (androidSystem.get_primary_override_dir ());

#if DEBUG
	setup_gc_logging ();
	set_debug_env_vars ();
#endif

#ifndef RELEASE
	jstr = env->GetObjectArrayElement (externalStorageDirs, 0);
	androidSystem.set_override_dir (1, utils.strdup_new (jstr.get_cstr ()));

	jstr = env->GetObjectArrayElement (externalStorageDirs, 1);
	androidSystem.set_override_dir (2, utils.strdup_new (jstr.get_cstr ()));

	for (uint32_t i = 0; i < AndroidSystem::MAX_OVERRIDES; ++i) {
		const char *p = androidSystem.get_override_dir (i);
		if (!utils.directory_exists (p))
			continue;
		log_warn (LOG_DEFAULT, "Using override path: %s", p);
	}
#endif
	setup_bundled_app ("libmonodroid_bundle_app.so");

	if (runtimeNativeLibDir != nullptr) {
		jstr = runtimeNativeLibDir;
		androidSystem.set_runtime_libdir (strdup (jstr.get_cstr ()));
		log_warn (LOG_DEFAULT, "Using runtime path: %s", androidSystem.get_runtime_libdir ());
	}

	androidSystem.setup_process_args (runtimeApks);

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING)) && !(log_timing_categories & LOG_TIMING_BARE)) {
		mono_counters_enable (static_cast<int>(XA_LOG_COUNTERS));

		simple_pointer_guard<char[]> counters_path (utils.path_combine (androidSystem.get_override_dir (0), "counters.txt"));
		log_info_nocheck (LOG_TIMING, "counters path: %s", counters_path.get ());
		counters = utils.monodroid_fopen (counters_path, "a");
		utils.set_world_accessable (counters_path);
	}

	mono_dl_fallback_register (monodroid_dlopen, monodroid_dlsym, nullptr, nullptr);

	set_profile_options ();

	set_trace_options ();

#if defined (DEBUG) && !defined (WINDOWS)
	debug.start_debugging_and_profiling ();
#endif

	mono_config_parse_memory (reinterpret_cast<const char*> (monodroid_config));
	mono_register_machine_config (reinterpret_cast<const char*> (monodroid_machine_config));

	log_info (LOG_DEFAULT, "Probing for Mono AOT mode\n");

	MonoAotMode mode = MonoAotMode::MONO_AOT_MODE_NONE;
	if (androidSystem.is_mono_aot_enabled ()) {
		mode = androidSystem.get_mono_aot_mode ();
		if (mode == MonoAotMode::MONO_AOT_MODE_LAST) {
			// Hack. See comments in android-system.hh
			if (!androidSystem.is_interpreter_enabled ()) {
				mode = MonoAotMode::MONO_AOT_MODE_NONE;
			}
		}

		if (mode != MonoAotMode::MONO_AOT_MODE_NONE) {
			if (mode != MonoAotMode::MONO_AOT_MODE_LAST) {
				log_info (LOG_DEFAULT, "Enabling AOT mode in Mono");
			} else {
				log_info (LOG_DEFAULT, "Enabling Mono Interpreter");
			}
		}
	}
	mono_jit_set_aot_mode (mode);

	log_info (LOG_DEFAULT, "Probing if we should use LLVM\n");

	if (androidSystem.is_mono_llvm_enabled ()) {
		char *args [1];
		args[0] = const_cast<char*> ("--llvm");
		log_info (LOG_DEFAULT, "Enabling LLVM mode in Mono\n");
		mono_jit_parse_options (1,  args);
		mono_set_use_llvm (true);
	}

	char *runtime_args = nullptr;
	androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_EXTRA_PROPERTY, &runtime_args);

#if TRACE
	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "debug.mono.extra=%s", runtime_args);
#endif

	timing_period partial_time;
	if (XA_UNLIKELY (utils.should_log (LOG_TIMING)))
		partial_time.mark_start ();

	mono_runtime_init (runtime_args);

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		partial_time.mark_end ();

		Timing::info (partial_time, "Runtime.init: Mono runtime init");
	}

	jstring_array_wrapper assemblies (env, assembliesJava);
	jstring_array_wrapper assembliesPaths (env);
	/* the first assembly is used to initialize the AppDomain name */
	create_and_initialize_domain (env, klass, runtimeApks, assemblies, nullptr, assembliesPaths, loader, /*is_root_domain:*/ true, /*force_preload_assemblies:*/ false);

	delete[] runtime_args;

	// Install our dummy exception handler on Desktop
	if constexpr (is_running_on_desktop) {
		mono_add_internal_call ("System.Diagnostics.Debugger::Mono_UnhandledException_internal(System.Exception)",
		                                 reinterpret_cast<const void*> (monodroid_Mono_UnhandledException_internal));
	}

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		total_time.mark_end ();

		Timing::info (total_time, "Runtime.init: end, total time");
		dump_counters ("## Runtime.init: end");
	}
}

void
MonodroidRuntime::dump_counters (const char *format, ...)
{
	if (counters == nullptr)
		return;

	va_list args;
	va_start (args, format);
	dump_counters_v (format, args);
	va_end (args);
}

void
MonodroidRuntime::dump_counters_v (const char *format, va_list args)
{
	if (counters == nullptr)
		return;

	fprintf (counters, "\n");
	vfprintf (counters, format, args);
	fprintf (counters, "\n");

	mono_counters_dump (MonodroidRuntime::XA_LOG_COUNTERS, counters);
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
                                jobjectArray externalStorageDirs, jobjectArray assembliesJava, [[maybe_unused]] jstring packageName,
                                jint apiLevel, [[maybe_unused]] jobjectArray environmentVariables)
{
	monodroidRuntime.Java_mono_android_Runtime_initInternal (
		env,
		klass,
		lang,
		runtimeApksJava,
		runtimeNativeLibDir,
		appDirs,
		loader,
		externalStorageDirs,
		assembliesJava,
		apiLevel,
		/* embeddedDSOsEnabled */ JNI_FALSE,
		/* isEmulator */ JNI_FALSE
	);
}

JNIEXPORT void JNICALL
Java_mono_android_Runtime_initInternal (JNIEnv *env, jclass klass, jstring lang, jobjectArray runtimeApksJava,
                                jstring runtimeNativeLibDir, jobjectArray appDirs, jobject loader,
                                jobjectArray externalStorageDirs, jobjectArray assembliesJava,
                                jint apiLevel, jboolean embeddedDSOsEnabled, jboolean isEmulator)
{
	monodroidRuntime.Java_mono_android_Runtime_initInternal (
		env,
		klass,
		lang,
		runtimeApksJava,
		runtimeNativeLibDir,
		appDirs,
		loader,
		externalStorageDirs,
		assembliesJava,
		apiLevel,
		embeddedDSOsEnabled,
		isEmulator
	);
}

inline void
MonodroidRuntime::Java_mono_android_Runtime_register (JNIEnv *env, jstring managedType, jclass nativeClass, jstring methods)
{
	timing_period total_time;

	char *type = nullptr;

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING)))
		total_time.mark_start ();

	int managedType_len = env->GetStringLength (managedType);
	const jchar *managedType_ptr = env->GetStringChars (managedType, nullptr);

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		const char *mt_ptr = env->GetStringUTFChars (managedType, nullptr);
		type = utils.strdup_new (mt_ptr);
		env->ReleaseStringUTFChars (managedType, mt_ptr);

		log_info_nocheck (LOG_TIMING, "Runtime.register: registering type `%s`", type);
	}

	int methods_len = env->GetStringLength (methods);
	const jchar *methods_ptr = env->GetStringChars (methods, nullptr);

	void *args[] = {
		&managedType_ptr,
		&managedType_len,
		&nativeClass,
		&methods_ptr,
		&methods_len,
	};

	MonoDomain *domain = mono_domain_get ();
	mono_jit_thread_attach (domain);
	// Refresh current domain as it might have been modified by the above call
	domain = mono_domain_get ();

	MonoMethod *register_jni_natives = registerType;
	if constexpr (is_running_on_desktop) {
		MonoClass *runtime = utils.monodroid_get_class_from_name (domain, "Mono.Android", "Android.Runtime", "JNIEnv");
		register_jni_natives = mono_class_get_method_from_name (runtime, "RegisterJniNatives", 5);
	}
	utils.monodroid_runtime_invoke (domain, register_jni_natives, nullptr, args, nullptr);

	env->ReleaseStringChars (methods, methods_ptr);
	env->ReleaseStringChars (managedType, managedType_ptr);

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		total_time.mark_end ();

		Timing::info (total_time, "Runtime.register: end time");

		dump_counters ("## Runtime.register: type=%s\n", type);
		delete[] type;
	}
}

JNIEXPORT void
JNICALL Java_mono_android_Runtime_register (JNIEnv *env, [[maybe_unused]] jclass klass, jstring managedType, jclass nativeClass, jstring methods)
{
	monodroidRuntime.Java_mono_android_Runtime_register (env, managedType, nativeClass, methods);
}

// DO NOT USE ON NORMAL X.A
// This function only works with the custom TypeManager embedded with the designer process.
static void
reinitialize_android_runtime_type_manager (JNIEnv *env)
{
	jclass typeManager = env->FindClass ("mono/android/TypeManager");
	env->UnregisterNatives (typeManager);

	jmethodID resetRegistration = env->GetStaticMethodID (typeManager, "resetRegistration", "()V");
	env->CallStaticVoidMethod (typeManager, resetRegistration);

	env->DeleteLocalRef (typeManager);
}

inline jint
MonodroidRuntime::Java_mono_android_Runtime_createNewContextWithData (JNIEnv *env, jclass klass, jobjectArray runtimeApksJava, jobjectArray assembliesJava,
                                                                      jobjectArray assembliesBytes, jobjectArray assembliesPaths, jobject loader, jboolean force_preload_assemblies)
{
	log_info (LOG_DEFAULT, "CREATING NEW CONTEXT");
	reinitialize_android_runtime_type_manager (env);
	MonoDomain *root_domain = mono_get_root_domain ();
	mono_jit_thread_attach (root_domain);

	jstring_array_wrapper runtimeApks (env, runtimeApksJava);
	jstring_array_wrapper assemblies (env, assembliesJava);
	jstring_array_wrapper assembliePaths (env, assembliesPaths);
	MonoDomain *domain = create_and_initialize_domain (env, klass, runtimeApks, assemblies, assembliesBytes, assembliePaths, loader, /*is_root_domain:*/ false, force_preload_assemblies);
	mono_domain_set (domain, FALSE);
	int domain_id = mono_domain_get_id (domain);
	current_context_id = domain_id;
	log_info (LOG_DEFAULT, "Created new context with id %d\n", domain_id);
	return domain_id;
}

inline void
MonodroidRuntime::Java_mono_android_Runtime_switchToContext (JNIEnv *env, jint contextID)
{
	log_info (LOG_DEFAULT, "SWITCHING CONTEXT");
	MonoDomain *domain = mono_domain_get_by_id ((int)contextID);
	if (current_context_id != (int)contextID) {
		mono_domain_set (domain, TRUE);
		// Reinitialize TypeManager so that its JNI handle goes into the right domain
		reinitialize_android_runtime_type_manager (env);
	}
	current_context_id = (int)contextID;
}

inline void
MonodroidRuntime::Java_mono_android_Runtime_destroyContexts (JNIEnv *env, jintArray array)
{
	MonoDomain *root_domain = mono_get_root_domain ();
	mono_jit_thread_attach (root_domain);
	current_context_id = -1;

	jint *contextIDs = env->GetIntArrayElements (array, nullptr);
	jsize count = env->GetArrayLength (array);

	log_info (LOG_DEFAULT, "Cleaning %d domains", count);

	for (jsize i = 0; i < count; i++) {
		int domain_id = contextIDs[i];
		MonoDomain *domain = mono_domain_get_by_id (domain_id);

		if (domain == nullptr)
			continue;
		log_info (LOG_DEFAULT, "Shutting down domain `%d'", contextIDs[i]);
		shutdown_android_runtime (domain);
		osBridge.remove_monodroid_domain (domain);
#ifndef ANDROID
		designerAssemblies.clear_for_domain (domain);
#endif
	}
	osBridge.on_destroy_contexts ();
#ifndef ANDROID
	for (jsize i = 0; i < count; i++) {
		int domain_id = contextIDs[i];
		MonoDomain *domain = mono_domain_get_by_id (domain_id);

		if (domain == nullptr)
			continue;
		log_info (LOG_DEFAULT, "Unloading domain `%d'", contextIDs[i]);
		mono_domain_unload (domain);
	}
#endif  // !defined(ANDROID)
	env->ReleaseIntArrayElements (array, contextIDs, JNI_ABORT);

	reinitialize_android_runtime_type_manager (env);

	log_info (LOG_DEFAULT, "All domain cleaned up");
}

char*
MonodroidRuntime::get_java_class_name_for_TypeManager (jclass klass)
{
	if (klass == nullptr || Class_getName == nullptr)
		return nullptr;

	JNIEnv *env = osBridge.ensure_jnienv ();
	jstring name = reinterpret_cast<jstring> (env->CallObjectMethod (klass, Class_getName));
	const char *mutf8 = env->GetStringUTFChars (name, nullptr);
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

JNIEXPORT jint
JNICALL Java_mono_android_Runtime_createNewContextWithData (JNIEnv *env, jclass klass, jobjectArray runtimeApksJava, jobjectArray assembliesJava, jobjectArray assembliesBytes, jobjectArray assembliesPaths, jobject loader, jboolean force_preload_assemblies)
{
	return monodroidRuntime.Java_mono_android_Runtime_createNewContextWithData (
		env,
		klass,
		runtimeApksJava,
		assembliesJava,
		assembliesBytes,
		assembliesPaths,
		loader,
		force_preload_assemblies
	);
}

/* !DO NOT REMOVE! Used by older versions of the Android Designer (pre-16.4) */
JNIEXPORT jint
JNICALL Java_mono_android_Runtime_createNewContext (JNIEnv *env, jclass klass, jobjectArray runtimeApksJava, jobjectArray assembliesJava, jobject loader)
{
	return monodroidRuntime.Java_mono_android_Runtime_createNewContextWithData (
		env,
		klass,
		runtimeApksJava,
		assembliesJava,
		nullptr, // assembliesBytes
		nullptr, // assembliesPaths
		loader,
		false    // force_preload_assemblies
	);
}

JNIEXPORT void
JNICALL Java_mono_android_Runtime_switchToContext (JNIEnv *env, [[maybe_unused]] jclass klass, jint contextID)
{
	monodroidRuntime.Java_mono_android_Runtime_switchToContext (env, contextID);
}

JNIEXPORT void
JNICALL Java_mono_android_Runtime_destroyContexts (JNIEnv *env, [[maybe_unused]] jclass klass, jintArray array)
{
	monodroidRuntime.Java_mono_android_Runtime_destroyContexts (env, array);
}

JNIEXPORT void
JNICALL Java_mono_android_Runtime_propagateUncaughtException (JNIEnv *env, [[maybe_unused]] jclass klass, jobject javaThread, jthrowable javaException)
{
	MonoDomain *domain = mono_domain_get ();
	monodroidRuntime.propagate_uncaught_exception (domain, env, javaThread, javaException);
}
