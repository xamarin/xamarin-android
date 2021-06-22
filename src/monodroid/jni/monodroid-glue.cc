#include <array>
#include <cstdlib>
#include <cstdarg>

#include <jni.h>
#include <ctime>
#include <cstdio>
#include <cstring>
#include <strings.h>
#include <cctype>
#include <cerrno>

#if defined (APPLE_OS_X)
#include <dlfcn.h>
#endif  // def APPLE_OX_X

#include <fcntl.h>
#include <unistd.h>
#include <cstdint>

#include <sys/time.h>
#include <sys/types.h>

#include <mono/jit/jit.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/loader.h>
#include <mono/metadata/mono-config.h>
#include <mono/metadata/mono-debug.h>
#include <mono/utils/mono-dl-fallback.h>
#include <mono/utils/mono-logger.h>

#include "mono_android_Runtime.h"

#if defined (DEBUG) && !defined (WINDOWS)
#include <fcntl.h>
#include <arpa/inet.h>
#include <sys/socket.h>
#include <netinet/in.h>
#endif

#if defined (__linux__) || defined (__linux)
#include <sys/syscall.h>
#endif

#if defined (APPLE_OS_X)
#include <libgen.h>
#endif  // defined(APPLE_OS_X)

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
#include "xa-internal-api-impl.hh"
#include "build-info.hh"
#include "monovm-properties.hh"

#ifndef WINDOWS
#include "xamarin_getifaddrs.h"
#endif

#include "cpp-util.hh"
#include "strings.hh"

#include "java-interop-dlfcn.h"

using namespace microsoft::java_interop;
using namespace xamarin::android;
using namespace xamarin::android::internal;

// This is below the above because we don't want to modify the header with our internal
// implementation details as it would prevent mkbundle from working
#include "mkbundle-api.h"

std::mutex MonodroidRuntime::jni_lookup_lock;
#if !defined (NET6)
#include "config.include"
#include "machine.config.include"

std::mutex MonodroidRuntime::api_init_lock;
void *MonodroidRuntime::api_dso_handle = nullptr;
#else // ndef NET6
std::mutex MonodroidRuntime::pinvoke_map_write_lock;

// The pragmas will go away once we switch to C++20, where the designator becomes part of the language standard. Both
// gcc and clang support it now as an extension, though.
#if defined (__clang__)
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wc99-designator"
#endif

MonoCoreRuntimeProperties MonodroidRuntime::monovm_core_properties = {
	.trusted_platform_assemblies = nullptr,
	.app_paths = nullptr,
	.native_dll_search_directories = nullptr,
	.pinvoke_override = &MonodroidRuntime::monodroid_pinvoke_override
};

#if defined (__clang__)
#pragma clang diagnostic pop
#endif
#endif // def NET6

#ifdef WINDOWS
static const char* get_xamarin_android_msbuild_path (void);
const char *BasicAndroidSystem::SYSTEM_LIB_PATH = get_xamarin_android_msbuild_path();

/* Set of Windows-specific utility/reimplementation of Unix functions */

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

#if !defined (NET6)
typedef void* (*mono_mkbundle_init_ptr) (void (*)(const MonoBundledAssembly **), void (*)(const char* assembly_name, const char* config_xml),void (*) (int mode));
mono_mkbundle_init_ptr mono_mkbundle_init;

typedef void (*mono_mkbundle_initialize_mono_api_ptr) (const BundleMonoAPI *info);
mono_mkbundle_initialize_mono_api_ptr mono_mkbundle_initialize_mono_api;

void
MonodroidRuntime::setup_bundled_app (const char *dso_name)
{
	if (!application_config.is_a_bundled_app)
		return;

	static unsigned int dlopen_flags = JAVA_INTEROP_LIB_LOAD_LOCALLY;
	void *libapp = nullptr;

	if (androidSystem.is_embedded_dso_mode_enabled ()) {
		log_info (LOG_DEFAULT, "bundle app: embedded DSO mode");
		libapp = androidSystem.load_dso_from_any_directories (dso_name, dlopen_flags);
	} else {
		log_info (LOG_DEFAULT, "bundle app: normal mode");
		dynamic_local_string<SENSIBLE_PATH_MAX> bundle_path;
		if (!androidSystem.get_full_dso_path_on_disk (dso_name, bundle_path)) {
			log_info (LOG_DEFAULT, "bundle %s not found on filesystem", dso_name);
			return;
		}
		log_info (LOG_BUNDLE, "Attempting to load bundled app from %s", bundle_path.get ());
		libapp = androidSystem.load_dso (bundle_path.get (), dlopen_flags, true);
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

	mono_mkbundle_initialize_mono_api = reinterpret_cast<mono_mkbundle_initialize_mono_api_ptr> (java_interop_lib_symbol (libapp, "initialize_mono_api", nullptr));
	if (mono_mkbundle_initialize_mono_api == nullptr)
		log_error (LOG_BUNDLE, "Missing initialize_mono_api in the application");

	mono_mkbundle_init = reinterpret_cast<mono_mkbundle_init_ptr> (java_interop_lib_symbol (libapp, "mono_mkbundle_init", nullptr));
	if (mono_mkbundle_init == nullptr)
		log_error (LOG_BUNDLE, "Missing mono_mkbundle_init in the application");
	log_info (LOG_BUNDLE, "Bundled app loaded: %s", dso_name);
}
#endif

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
	size_t culture_len;

	if (culture != nullptr)
		culture_len = strlen (culture);
	else
		culture_len = 0;
	size_t name_len = strlen (name);

	static_local_string<SENSIBLE_PATH_MAX> pname (name_len + culture_len);
	if (culture_len > 0)
		pname.append (culture, culture_len);
	pname.append (name, name_len);

	constexpr char dll_extension[] = ".dll";
	constexpr size_t dll_extension_len = sizeof(dll_extension) - 1;

	bool is_dll = utils.ends_with (name, dll_extension);
	size_t file_name_len = pname.length () + 1;
	if (!is_dll)
		file_name_len += dll_extension_len;

	for (uint32_t oi = 0; oi < AndroidSystem::MAX_OVERRIDES; ++oi) {
		override_dir = androidSystem.get_override_dir (oi);
		if (override_dir == nullptr || !utils.directory_exists (override_dir))
			continue;

		size_t override_dir_len = strlen (override_dir);
		static_local_string<SENSIBLE_PATH_MAX> fullpath (override_dir_len + file_name_len);
		utils.path_combine (fullpath, override_dir, override_dir_len, pname.get (), pname.length ());
		if (!is_dll) {
			fullpath.append (dll_extension, dll_extension_len);
		}

		log_info (LOG_ASSEMBLY, "open_from_update_dir: trying to open assembly: %s\n", fullpath.get ());
		if (utils.file_exists (fullpath.get ()))
			result = mono_assembly_open_full (fullpath.get (), nullptr, 0);
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
	size_t filename_len = strlen (filename) + 1; // includes space for path separator
	for (size_t i = 0; i < AndroidSystem::MAX_OVERRIDES; ++i) {
		const char *odir = androidSystem.get_override_dir (i);
		if (odir == nullptr)
			continue;

		size_t odir_len = strlen (odir);
		static_local_string<SENSIBLE_PATH_MAX> p (odir_len + filename_len);
		utils.path_combine (p, odir, odir_len, filename, filename_len);
		bool  exists  = utils.file_exists (p.get ());

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
bool
MonodroidRuntime::parse_runtime_args (dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> &runtime_args, RuntimeOptions *options)
{
	if (runtime_args.length () == 0) {
		log_warn (LOG_DEFAULT, "runtime args empty");
		return true;
	}

	constexpr char   ARG_DEBUG[]                   = "debug";
	constexpr size_t ARG_DEBUG_LENGTH              = sizeof(ARG_DEBUG) - 1;
	constexpr char   ARG_TIMEOUT[]                 = "timeout=";
	constexpr size_t ARG_TIMEOUT_LENGTH            = sizeof(ARG_TIMEOUT) - 1;
	constexpr char   ARG_SERVER[]                  = "server=";
	constexpr size_t ARG_SERVER_LENGTH             = sizeof(ARG_SERVER) - 1;
	constexpr char   ARG_LOGLEVEL[]                = "loglevel=";
	constexpr size_t ARG_LOGLEVEL_LENGTH           = sizeof(ARG_LOGLEVEL) - 1;

	bool ret = true;
	string_segment token;
	while (runtime_args.next_token (',', token)) {
		if (token.starts_with (ARG_DEBUG, ARG_DEBUG_LENGTH)) {
			char *host = nullptr;
			int sdb_port = 1000, out_port = -1;

			options->debug = true;

			if (token.has_at ('=', ARG_DEBUG_LENGTH)) {
				constexpr size_t arg_name_length = ARG_DEBUG_LENGTH + 1; // Includes the '='

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
			} else if (!token.has_at ('\0', ARG_DEBUG_LENGTH)) {
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
		} else if (token.starts_with (ARG_TIMEOUT, ARG_TIMEOUT_LENGTH)) {
			if (!token.to_integer (options->timeout_time, ARG_TIMEOUT_LENGTH)) {
				log_error (LOG_DEFAULT, "Invalid --timeout argument.");
				ret = false;
			}
		} else if (token.starts_with (ARG_SERVER, ARG_SERVER_LENGTH)) {
			options->server = token.has_at ('y', ARG_SERVER_LENGTH) || token.has_at ('Y', ARG_SERVER_LENGTH);
		} else if (token.starts_with (ARG_LOGLEVEL, ARG_LOGLEVEL_LENGTH)) {
			if (!token.to_integer (options->loglevel, ARG_LOGLEVEL_LENGTH)) {
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
MonodroidRuntime::mono_runtime_init ([[maybe_unused]] dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN>& runtime_args)
{
#if defined (DEBUG) && !defined (WINDOWS)
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

	mono_set_signal_chaining (1);
	mono_set_crash_chaining (1);

	osBridge.register_gc_hooks ();

#if !defined (NET6)
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
#endif // ndef NET6
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

#if defined (NET6)
void
MonodroidRuntime::cleanup_runtime_config (MonovmRuntimeConfigArguments *args, [[maybe_unused]] void *user_data)
{
	if (args == nullptr || args->kind != 1 || args->runtimeconfig.data.data == nullptr) {
		return;
	}

#if !defined (WINDOWS)
	munmap (static_cast<void*>(const_cast<char*>(args->runtimeconfig.data.data)), args->runtimeconfig.data.data_len);
#endif // ndef WINDOWS
}
#endif // def NET6

MonoDomain*
MonodroidRuntime::create_domain (JNIEnv *env, jstring_array_wrapper &runtimeApks, bool is_root_domain)
{
	size_t user_assemblies_count   = 0;
#if defined (NET6)
	constexpr bool have_mono_mkbundle_init = false;
#else // def NET6
	bool have_mono_mkbundle_init = mono_mkbundle_init != nullptr;
#endif // ndef NET6

	gather_bundled_assemblies (runtimeApks, &user_assemblies_count);

#if defined (NET6)
	timing_period blob_time;
	if (XA_UNLIKELY (utils.should_log (LOG_TIMING)))
		blob_time.mark_start ();

	if (embeddedAssemblies.have_runtime_config_blob ()) {
		runtime_config_args.kind = 1;
		embeddedAssemblies.get_runtime_config_blob (runtime_config_args.runtimeconfig.data.data, runtime_config_args.runtimeconfig.data.data_len);
		monovm_runtimeconfig_initialize (&runtime_config_args, cleanup_runtime_config, nullptr);
	}

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		blob_time.mark_end ();
		Timing::info (blob_time, "Register runtimeconfig binary blob");
	}
#endif // def NET6

	if (!have_mono_mkbundle_init && user_assemblies_count == 0 && androidSystem.count_override_assemblies () == 0 && !Util::is_running_on_desktop) {
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

		constexpr char DOMAIN_NAME[] = "MonoAndroidDomain";
		constexpr size_t DOMAIN_NAME_LENGTH = sizeof(DOMAIN_NAME) - 1;
		constexpr size_t DOMAIN_NAME_TOTAL_SIZE = DOMAIN_NAME_LENGTH + MAX_INTEGER_DIGIT_COUNT_BASE10;

		static_local_string<DOMAIN_NAME_TOTAL_SIZE + 1> domain_name (DOMAIN_NAME_TOTAL_SIZE);
		domain_name.append (DOMAIN_NAME);
		domain_name.append (android_api_level);

		domain = utils.monodroid_create_appdomain (root_domain, domain_name.get (), /*shadow_copy:*/ 1, /*shadow_directory:*/ androidSystem.get_override_dir (0));
	}

	if constexpr (Util::is_running_on_desktop) {
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
	if (version < 14) {
		java_System = nullptr;
		java_System_identityHashCode = 0;
		return 0;
	}

	java_System = utils.get_class_from_runtime_field(env, runtimeClass, "java_lang_System", true);
	java_System_identityHashCode = env->GetStaticMethodID (java_System, "identityHashCode", "(Ljava/lang/Object;)I");

	return 1;
}

inline void
MonodroidRuntime::lookup_bridge_info (MonoDomain *domain, MonoImage *image, const OSBridge::MonoJavaGCBridgeType *type, OSBridge::MonoJavaGCBridgeInfo *info)
{
	info->klass             = utils.monodroid_get_required_class (domain, image, type->class_token_id, type->_namespace, type->_typename);
	info->handle            = utils.monodroid_get_class_field (info->klass, type->handle_token_id, "handle");
	info->handle_type       = utils.monodroid_get_class_field (info->klass, type->handle_type_token_id, "handle_type");
	info->refs_added        = utils.monodroid_get_class_field (info->klass, type->refs_added_token_id, "refs_added");
	info->weak_handle       = utils.monodroid_get_class_field (info->klass, type->weak_handle_token_id, "weak_handle");
	if (XA_UNLIKELY (info->klass == nullptr || info->handle == nullptr || info->handle_type == nullptr || info->refs_added == nullptr || info->weak_handle == nullptr)) {
		log_fatal (LOG_DEFAULT, "The type `%s.%s` is missing required instance fields! handle=%p handle_type=%p refs_added=%p weak_handle=%p",
				type->_namespace, type->_typename,
				info->handle,
				info->handle_type,
				info->refs_added,
				info->weak_handle);
		abort ();
	}
}

force_inline MonoMethod*
MonodroidRuntime::monodroid_get_required_method (MonoImage *image, MonoClass *klass, uint32_t tokenID, int param_count, char const *type_name, char const *method_name)
{
	MonoMethod *method;

	if constexpr (Util::is_running_on_desktop) {
		method = mono_class_get_method_from_name (klass, method_name, param_count);
	} else {
		method = mono_get_method (image, tokenID, klass);
	}
	abort_unless (method != nullptr, "INTERNAL ERROR: Unable to find method `%s.%s`", type_name, method_name);

	return method;
}

void
MonodroidRuntime::init_android_runtime (MonoDomain *domain, JNIEnv *env, jclass runtimeClass, jobject loader, MonoClass*& jnienv, MonoClassField*& jnienv_bridge_processing_field)
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
	init.isRunningOnDesktop     = Util::is_running_on_desktop ? 1 : 0;
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

	MonoClass *runtime = utils.monodroid_get_required_class (domain, image, managed_token_ids.android_runtime_jnienv, "Android.Runtime", "JNIEnv");
	jnienv = runtime;

	MonoMethod      *method = monodroid_get_required_method (image, runtime, managed_token_ids.android_runtime_jnienv_initialize, 1, "Android.Runtime.JNIEnv", "Initialize");
	MonoAssembly    *ji_assm    = utils.monodroid_load_assembly (domain, "Java.Interop");
	MonoImage       *ji_image   = mono_assembly_get_image  (ji_assm);
	for ( ; i < OSBridge::NUM_XA_GC_BRIDGE_TYPES + OSBridge::NUM_JI_GC_BRIDGE_TYPES; ++i) {
		lookup_bridge_info (domain, ji_image, &osBridge.get_java_gc_bridge_type (i), &osBridge.get_java_gc_bridge_info (i));
	}

	/* If running on desktop, we may be swapping in a new Mono.Android image when calling this
	 * so always make sure we have the freshest handle to the method.
	 */
	if (registerType == nullptr || Util::is_running_on_desktop) {
		registerType = monodroid_get_required_method (image, runtime, managed_token_ids.android_runtime_jnienv_registerjninatives, 5, "Android.Runtime.JNIEnv", "RegisterJniNatives");
	}

	// Android.Runtime.JNIEnv.BridgeProcessing
	MonoClassField *bridge_processing_field = utils.monodroid_get_class_field (runtime, managed_token_ids.android_runtime_jnienv_bridgeprocessing, "BridgeProcessing");
	abort_unless (bridge_processing_field != nullptr, "INTERNAL_ERROR: Unable to find Android.Runtime.JNIEnv.BridgeProcessing");
	jnienv_bridge_processing_field = bridge_processing_field;

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

MonoClass*
MonodroidRuntime::get_android_runtime_class (MonoDomain *domain)
{
	MonoAssembly *assm = utils.monodroid_load_assembly (domain, "Mono.Android");
	MonoImage *image   = mono_assembly_get_image (assm);
	MonoClass *runtime = utils.monodroid_get_required_class (domain, image, managed_token_ids.android_runtime_jnienv, "Android.Runtime", "JNIEnv");

	return runtime;
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

force_inline unsigned int
MonodroidRuntime::convert_dl_flags (int flags)
{
	unsigned int lflags = (flags & static_cast<int> (MONO_DL_LOCAL))
		? JAVA_INTEROP_LIB_LOAD_LOCALLY
		: JAVA_INTEROP_LIB_LOAD_GLOBALLY;
	return lflags;
}

#if !defined (NET6)
force_inline void
MonodroidRuntime::init_internal_api_dso (void *handle)
{
	if (handle == nullptr) {
		log_fatal (LOG_DEFAULT, "Internal API library is required");
		exit (FATAL_EXIT_MONO_MISSING_SYMBOLS);
	}

	// There's a very, very small chance of a race condition here, but it should be acceptable and we can save some time
	// by not acquiring the lock on Android systems which don't have the dlopen bug we worked around in
	// https://github.com/xamarin/xamarin-android/pull/4914
	//
	// The race condition will exist only on systems with the above dynamic loader bug and would become a problem only
	// if an application were loading managed assemblies with p/invokes very quickly from different threads. All in all,
	// not a very likely scenario.
	//
	if (handle == api_dso_handle) {
		log_debug (LOG_DEFAULT, "Internal API library already loaded, initialization not necessary");
		return;
	}

	std::lock_guard<std::mutex> lock (api_init_lock);
	if (api_dso_handle != nullptr) {
		auto api_shutdown = reinterpret_cast<external_api_shutdown_fn> (java_interop_lib_symbol (api_dso_handle, MonoAndroidInternalCalls::SHUTDOWN_FUNCTION_NAME, nullptr));
		if (api_shutdown == nullptr) {
			// We COULD ignore this situation, but if the function is missing it means we messed something up and thus
			// it *is* a fatal error.
			log_fatal (LOG_DEFAULT, "Unable to properly close Internal API library, shutdown function '%s' not found in the module", MonoAndroidInternalCalls::SHUTDOWN_FUNCTION_NAME);
			exit (FATAL_EXIT_MONO_MISSING_SYMBOLS);
		}
		api_shutdown ();
	}

	api_dso_handle = handle;
	auto api = new MonoAndroidInternalCalls_Impl ();
	auto api_init = reinterpret_cast<external_api_init_fn>(java_interop_lib_symbol (handle, MonoAndroidInternalCalls::INIT_FUNCTION_NAME, nullptr));
	if (api_init == nullptr) {
		log_fatal (LOG_DEFAULT, "Unable to initialize Internal API library, init function '%s' not found in the module", MonoAndroidInternalCalls::INIT_FUNCTION_NAME);
		exit (FATAL_EXIT_MONO_MISSING_SYMBOLS);
	}

	log_debug (LOG_DEFAULT, "Initializing Internal API library %p", handle);
	if (!api_init (api)) {
		log_fatal (LOG_DEFAULT, "Failed to initialize Internal API library");
		exit (FATAL_EXIT_MONO_MISSING_SYMBOLS);
	}
}
#endif // ndef NET6

force_inline void*
MonodroidRuntime::monodroid_dlopen_log_and_return (void *handle, char **err, const char *full_name, bool free_memory, [[maybe_unused]] bool need_api_init)
{
	if (handle == nullptr && err != nullptr) {
		*err = utils.monodroid_strdup_printf ("Could not load library: Library '%s' not found.", full_name);
	}

	if (free_memory) {
		delete[] full_name;
	}

#if !defined (NET6)
	if (!need_api_init)
		return handle;
	init_internal_api_dso (handle);
#endif // ndef NET6

	return handle;
}

#if defined (NET6) && defined (ANDROID)
force_inline void*
MonodroidRuntime::monodroid_dlopen (const char *name, int flags, char **err)
{
	unsigned int dl_flags = monodroidRuntime.convert_dl_flags (flags);
	void *h = androidSystem.load_dso_from_any_directories (name, dl_flags);
	if (h != nullptr) {
		return monodroid_dlopen_log_and_return (h, err, name, false /* name_needs_free */);
	}

	if (!utils.ends_with (name, ".dll.so")) {
		return monodroid_dlopen_log_and_return (h, err, name, false /* name_needs_free */);
	}

	char *basename_part = const_cast<char*> (strrchr (name, '/'));
	if (basename_part != nullptr) {
		basename_part++;
	} else {
		basename_part = (char*)name;
	}

	constexpr char LIBAOT_PREFIX[] = "libaot-";
	constexpr size_t LIBAOT_PREFIX_SIZE = sizeof(LIBAOT_PREFIX);

	size_t base_len = strlen (basename_part);
	size_t len = base_len + LIBAOT_PREFIX_SIZE; // includes the trailing \0
	static_local_string<PATH_MAX> basename (len);

	basename.append (LIBAOT_PREFIX).append (basename_part);

	h = androidSystem.load_dso_from_any_directories (basename.get (), dl_flags);

	if (h != nullptr && XA_UNLIKELY (utils.should_log (LOG_ASSEMBLY))) {
		log_info_nocheck (LOG_ASSEMBLY, "Loaded AOT image '%s'", basename.get ());
	}

	return h;
}

void*
MonodroidRuntime::monodroid_dlopen (const char *name, int flags, char **err, [[maybe_unused]] void *user_data)
{
	log_warn (LOG_DEFAULT, "MonodroidRuntime::monodroid_dlopen (\"%s\", 0x%x, %p, %p)", name, flags, err, user_data);
	if (name == nullptr) {
		log_warn (LOG_ASSEMBLY, "monodroid_dlopen got a null name. This is not supported in NET6+");
		return nullptr;
	}

	constexpr char DSO_EXTENSION[] = ".so";
	constexpr size_t DSO_EXTENSION_SIZE = sizeof(DSO_EXTENSION);

	if (utils.ends_with (name, DSO_EXTENSION)) {
		return monodroid_dlopen (name, flags, err);
	}

	size_t len = ADD_WITH_OVERFLOW_CHECK (size_t, strlen (name), DSO_EXTENSION_SIZE); // includes the trailing \0
	static_local_string<PATH_MAX> full_name (len);

	full_name.append (name).append (DSO_EXTENSION);
	return monodroid_dlopen (full_name.get (), flags, err);
}
#else // def NET6 && def ANDROID
void*
MonodroidRuntime::monodroid_dlopen (const char *name, int flags, char **err, [[maybe_unused]] void *user_data)
{
	unsigned int dl_flags = monodroidRuntime.convert_dl_flags (flags);
	bool libmonodroid_fallback = false;
	bool name_is_full_path = false;
	bool name_needs_free = false;
	/* name is nullptr when we're P/Invoking __Internal, so remap to libxa-internal-api */
	if (name == nullptr || strstr (name, "xa-internal-api") != nullptr) {
#if defined (WINDOWS)
		char *tmp_name = nullptr;

		auto probe_dll_at = [&](const char *the_path) -> bool {
			if (the_path == nullptr) {
				return false;
			}

			const char *last_sep = strrchr (the_path, MONODROID_PATH_SEPARATOR_CHAR);
			if (last_sep != nullptr) {
				char *dir = utils.strdup_new (the_path, last_sep - the_path);
				tmp_name = utils.string_concat (dir, MONODROID_PATH_SEPARATOR, API_DSO_NAME);
				delete[] dir;
				if (!utils.file_exists (tmp_name)) {
					delete[] tmp_name;
					tmp_name = nullptr;
					return false;
				}

				return true;
			}

			return false;
		};

		//
		// First try to see if it exist at the path pointed to by `name`. With p/invokes, currently (Sep 2020), we can't
		// really trust the path since it consists of *some* directory path + p/invoke library name and it does not
		// point to the location where the native library is. However, we still need to check the location first, should
		// it point to the right place in the future.
		//
		// Context: https://github.com/mono/mono/issues/20295#issuecomment-679271621
		//
		bool found = probe_dll_at (name);
		if (!found) {
			// Next lets try the location of the XA runtime DLL, libxa-internal-api.dll should be next to it.
			const char *path = get_my_location (false);
			found = probe_dll_at (path);
			if (path != nullptr) {
				free (reinterpret_cast<void*>(const_cast<char*>(path)));
			}

			if (!found) {
				log_warn (LOG_DEFAULT, "Failed to locate %s, using file name without the path", API_DSO_NAME);
				name = API_DSO_NAME;
			} else {
				name = tmp_name;
				name_is_full_path = true;
				name_needs_free = true;
			}
		}
#else // ndef WINDOWS
		name = API_DSO_NAME;
#endif // WINDOWS
		libmonodroid_fallback = true;
	}

	void *h = nullptr;
	if (!name_is_full_path)
		h = androidSystem.load_dso_from_any_directories (name, dl_flags);

	if (h != nullptr) {
		return monodroid_dlopen_log_and_return (h, err, name, name_needs_free, libmonodroid_fallback);
	}

	if (libmonodroid_fallback) {
		const char *full_name;
		if (name_is_full_path) {
			full_name = name;
		} else {
			if (name_needs_free) {
				delete[] name;
			}
			full_name = utils.path_combine (AndroidSystem::SYSTEM_LIB_PATH, API_DSO_NAME);
			name_needs_free = true;
		}
		h = androidSystem.load_dso (full_name, dl_flags, false);
		return monodroid_dlopen_log_and_return (h, err, full_name, name_needs_free, true);
	}

	if (!utils.ends_with (name, ".dll.so") && !utils.ends_with (name, ".exe.so")) {
		return monodroid_dlopen_log_and_return (h, err, name, name_needs_free);
	}

	char *basename_part = const_cast<char*> (strrchr (name, '/'));
	if (basename_part != nullptr)
		basename_part++;
	else
		basename_part = (char*)name;

	constexpr char LIBAOT_PREFIX[] = "libaot-";
	constexpr size_t LIBAOT_PREFIX_SIZE = sizeof(LIBAOT_PREFIX);

	size_t base_len = strlen (basename_part);
	size_t len = base_len + LIBAOT_PREFIX_SIZE; // includes the trailing \0
	static_local_string<PATH_MAX> basename (len);

	basename.append (LIBAOT_PREFIX).append (basename_part);

	h = androidSystem.load_dso_from_any_directories (basename.get (), dl_flags);

	if (h != nullptr && XA_UNLIKELY (utils.should_log (LOG_ASSEMBLY)))
		log_info_nocheck (LOG_ASSEMBLY, "Loaded AOT image '%s'", basename.get ());

	if (name_needs_free) {
		delete[] name;
	}

	return h;
}
#endif // !(def NET6 && def ANDROID)

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
MonodroidRuntime::create_xdg_directory (jstring_wrapper& home, size_t home_len, const char *relativePath, size_t relative_path_len, const char *environmentVariableName)
{
	static_local_string<SENSIBLE_PATH_MAX> dir (home_len + relative_path_len);
	utils.path_combine (dir, home.get_cstr (), home_len, relativePath, relative_path_len);
	log_info (LOG_DEFAULT, "Creating XDG directory: %s", dir.get ());
	int rv = utils.create_directory (dir.get (), DEFAULT_DIRECTORY_MODE);
	if (rv < 0 && errno != EEXIST)
		log_warn (LOG_DEFAULT, "Failed to create XDG directory %s. %s", dir.get (), strerror (errno));
	if (environmentVariableName)
		setenv (environmentVariableName, dir.get (), 1);
}

inline void
MonodroidRuntime::create_xdg_directories_and_environment (jstring_wrapper &homeDir)
{
	size_t home_len = strlen (homeDir.get_cstr ());

	constexpr char XDG_DATA_HOME[] = "XDG_DATA_HOME";
	constexpr char HOME_PATH[] = ".local/share";
	constexpr size_t HOME_PATH_LEN = sizeof(HOME_PATH) - 1;
	create_xdg_directory (homeDir, home_len, HOME_PATH, HOME_PATH_LEN, XDG_DATA_HOME);

	constexpr char XDG_CONFIG_HOME[] = "XDG_CONFIG_HOME";
	constexpr char CONFIG_PATH[] = ".config";
	constexpr size_t CONFIG_PATH_LEN = sizeof(CONFIG_PATH) - 1;
	create_xdg_directory (homeDir, home_len, CONFIG_PATH, CONFIG_PATH_LEN, XDG_CONFIG_HOME);
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
			constexpr char one[] = "1";
			if (index == arg.length () - 1) {
				arg[index] = '\0';
			}
			setenv (arg.get (), one, 1);
			log_envvar (arg.get (), one);
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

#if defined (NET6)
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

		value.assign (prop_value.get (), prop_value.length ());
	}

	// setenv(3) makes copies of its arguments
	setenv ("DOTNET_DiagnosticPorts", value.get (), 1);
}
#else // def NET6
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

		value.assign (prop_value.get (), prop_value.length ());
	}

	constexpr char OUTPUT_ARG[] = "output=";
	constexpr size_t OUTPUT_ARG_LEN = sizeof(OUTPUT_ARG) - 1;

	ssize_t colon_idx = value.index_of (':');
	size_t start_index = colon_idx < 0 ? 0 : static_cast<size_t>(colon_idx + 1);
	dynamic_local_string<SENSIBLE_PATH_MAX> output_path;
	bool have_output_arg = false;
	string_segment param;

	while (value.next_token (start_index, ',', param)) {
		dynamic_local_string<SENSIBLE_PATH_MAX> temp;
		temp.assign (param.start (), param.length ());
		if (!param.starts_with (OUTPUT_ARG, OUTPUT_ARG_LEN) || param.length () == OUTPUT_ARG_LEN) {
			continue;
		}

		output_path.assign (param.start () + OUTPUT_ARG_LEN, param.length () - OUTPUT_ARG_LEN);
		have_output_arg = true;
		break;
	}

	if (!have_output_arg) {
		constexpr char   MLPD_EXT[] = "mlpd";
		constexpr size_t MLPD_EXT_LEN = sizeof(MLPD_EXT) - 1;
		constexpr char   AOT_EXT[] = "aotprofile";
		constexpr size_t AOT_EXT_LEN = sizeof(AOT_EXT) - 1;
		constexpr char   COV_EXT[] = "xml";
		constexpr size_t COV_EXT_LEN = sizeof(COV_EXT) - 1;
		constexpr char   LOG_PREFIX[] = "log:";
		constexpr size_t LOG_PREFIX_LENGTH = sizeof(LOG_PREFIX) - 1;
		constexpr char   AOT_PREFIX[] = "aot:";
		constexpr size_t AOT_PREFIX_LENGTH = sizeof(AOT_PREFIX) - 1;
		constexpr char   DEFAULT_PREFIX[] = "default:";
		constexpr size_t DEFAULT_PREFIX_LENGTH = sizeof(DEFAULT_PREFIX) - 1;
		constexpr char   COVERAGE_PREFIX[] = "coverage:";
		constexpr size_t COVERAGE_PREFIX_LENGTH = sizeof(COVERAGE_PREFIX) - 1;
		constexpr char   PROFILE_FILE_NAME_PREFIX[] = "profile.";
		constexpr size_t PROFILE_FILE_NAME_PREFIX_LEN = sizeof(PROFILE_FILE_NAME_PREFIX) - 1;

		size_t length_adjust = colon_idx >= 1 ? 0 : 1;

		output_path
			.assign (androidSystem.get_override_dir (0))
			.append (MONODROID_PATH_SEPARATOR)
			.append (PROFILE_FILE_NAME_PREFIX, PROFILE_FILE_NAME_PREFIX_LEN);

		if (value.starts_with (LOG_PREFIX, LOG_PREFIX_LENGTH - length_adjust)) {
			output_path.append (MLPD_EXT, MLPD_EXT_LEN);
		} else if (value.starts_with (AOT_PREFIX, AOT_PREFIX_LENGTH - length_adjust)) {
			output_path.append (AOT_EXT, AOT_EXT_LEN);
		} else if (value.starts_with (DEFAULT_PREFIX, DEFAULT_PREFIX_LENGTH - length_adjust)) {
			output_path.append (MLPD_EXT, MLPD_EXT_LEN);
		} else if (value.starts_with (COVERAGE_PREFIX, COVERAGE_PREFIX_LENGTH - length_adjust)) {
			output_path.append (COV_EXT, COV_EXT_LEN);
		} else {
			size_t len = colon_idx < 0 ? value.length () : static_cast<size_t>(colon_idx + 1);
			output_path.append (value.get (), len);
		}

		if (colon_idx < 0)
			value.append (":");
		else
			value.append (",");
		value
			.append (OUTPUT_ARG, OUTPUT_ARG_LEN)
			.append (output_path.get (), output_path.length ());
	}

	/*
	 * libmono-profiler-log.so profiler won't overwrite existing files.
	 * Remove it For Great Justice^H^H^H to preserve my sanity!
	 */
	unlink (output_path.get ());

	log_warn (LOG_DEFAULT, "Initializing profiler with options: %s", value.get ());
	debug.monodroid_profiler_load (androidSystem.get_runtime_libdir (), value.get (), output_path.get ());
}
#endif // ndef NET6

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

	void *llvm  = androidSystem.load_dso ("libLLVM.so", JAVA_INTEROP_LIB_LOAD_GLOBALLY, TRUE);
	if (llvm) {
		bool *disable_signals = reinterpret_cast<bool*> (java_interop_lib_symbol (llvm, "_ZN4llvm23DisablePrettyStackTraceE", nullptr));
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
MonodroidRuntime::load_assemblies (MonoDomain *domain, bool preload, jstring_array_wrapper &assemblies)
{
	timing_period total_time;
	if (XA_UNLIKELY (utils.should_log (LOG_TIMING)))
		total_time.mark_start ();

	for (size_t i = 0; i < assemblies.get_length (); ++i) {
		jstring_wrapper &assembly = assemblies [i];
		load_assembly (domain, assembly);
		// only load the first "main" assembly if we are not preloading.
		if (!preload)
			break;
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
	if constexpr (Util::is_running_on_desktop) {
		if (is_root_domain) {
			return domain;
		}
	}

#ifndef ANDROID
	if (assembliesBytes != nullptr)
		designerAssemblies.add_or_update_from_java (domain, env, assemblies, assembliesBytes, assembliesPaths);
#endif
	bool preload = (androidSystem.is_assembly_preload_enabled () || (Util::is_running_on_desktop && force_preload_assemblies));
	load_assemblies (domain, preload, assemblies);

	MonoClass* jnienv = nullptr;
	MonoClassField* jnienv_bridge_processing_field = nullptr;
	init_android_runtime (domain, env, runtimeClass, loader, jnienv, jnienv_bridge_processing_field);

	osBridge.add_monodroid_domain (domain, jnienv, jnienv_bridge_processing_field);

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

#if defined (WINDOWS)
const char*
MonodroidRuntime::get_my_location (bool remove_file_name)
{
	HMODULE hm = NULL;

	DWORD handle_flags = GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT;
	if (GetModuleHandleExW (handle_flags, (LPCWSTR) &get_xamarin_android_msbuild_path, &hm) == 0) {
		int ret = GetLastError ();
		log_warn (LOG_DEFAULT, "Unable to get HANDLE to `libmono-android.debug.dll`; GetModuleHandleExW returned %d\n", ret);
		return nullptr;
	}

	WCHAR path[MAX_PATH * 2];
	if (GetModuleFileNameW (hm, path, sizeof(path)) == 0) {
		int ret = GetLastError ();
		log_warn (LOG_DEFAULT, "Unable to get filename to `libmono-android.debug.dll`; GetModuleFileNameW returned %d\n", ret);
		return nullptr;
	}

	if (remove_file_name)
		PathRemoveFileSpecW (path);

	return utils.utf16_to_utf8 (path);
}
#elif defined (APPLE_OS_X)
const char*
MonodroidRuntime::get_my_location (bool remove_file_name)
{
	Dl_info info;
	if (dladdr (reinterpret_cast<const void*>(&MonodroidRuntime::get_my_location), &info) == 0) {
		log_warn (LOG_DEFAULT, "Could not lookup library containing `MonodroidRuntime::get_my_location()`; dladdr failed: %s", dlerror ());
		return nullptr;
	}

	if (remove_file_name)
		return utils.strdup_new (dirname (const_cast<char*>(info.dli_fname)));
	return utils.strdup_new (info.dli_fname);
}
#endif  // defined(WINDOWS)

#if defined (ANDROID)
force_inline void
MonodroidRuntime::setup_mono_tracing (char const* const& mono_log_mask)
{
	bool have_log_assembly = (log_categories & LOG_ASSEMBLY) != 0;
	bool have_log_gc = (log_categories & LOG_GC) != 0;

	constexpr char   MASK_ASM[] = "asm";
	constexpr size_t MASK_ASM_LEN = sizeof(MASK_ASM) - 1;
	constexpr char   MASK_DLL[] = "dll";
	constexpr size_t MASK_DLL_LEN = sizeof(MASK_DLL) - 1;
	constexpr char   MASK_GC[] = "gc";
	constexpr size_t MASK_GC_LEN = sizeof(MASK_GC) - 1;

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> log_mask;
	if (mono_log_mask == nullptr || *mono_log_mask == '\0') {
		if (have_log_assembly) {
			log_mask.append (MASK_ASM);
			log_mask.append (",");
			log_mask.append (MASK_DLL);
		}

		if (have_log_gc) {
			if (log_mask.length () > 0) {
				log_mask.append (",");
			}
			log_mask.append (MASK_GC);
		}

		// empty string turns off all Mono VM tracing
		mono_trace_set_mask_string (log_mask.get ());
		return;
	}

	if (!have_log_assembly && !have_log_gc)  {
		mono_trace_set_mask_string (mono_log_mask);
		return;
	}

	bool need_asm = have_log_assembly, need_dll = have_log_assembly, need_gc = have_log_gc;
	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> input_log_mask;
	input_log_mask.assign (mono_log_mask);

	string_segment token;
	while (input_log_mask.next_token (',', token)) {
		if (need_asm && token.equal (MASK_ASM, MASK_ASM_LEN)) {
			need_asm = false;
		} else if (need_dll && token.equal (MASK_DLL, MASK_DLL_LEN)) {
			need_dll = false;
		} else if (need_gc && token.equal (MASK_GC, MASK_GC_LEN)) {
			need_gc = false;
		}

		if (!need_asm && !need_dll && !need_gc) {
			mono_trace_set_mask_string (mono_log_mask);
			return;
		}
	}

	if (need_asm) {
		input_log_mask.append (",");
		input_log_mask.append (MASK_ASM);
	}

	if (need_dll) {
		input_log_mask.append (",");
		input_log_mask.append (MASK_DLL);
	}

	if (need_gc) {
		input_log_mask.append (",");
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

#endif // def ANDROID

void
MonodroidRuntime::lookup_java_timezone ()
{
	std::lock_guard<std::mutex> lock (jni_lookup_lock);
	java_TimeZone = utils.get_class_from_runtime_field (osBridge.ensure_jnienv (), java_mono_Runtime, "java_util_TimeZone", true);
}

inline void
MonodroidRuntime::Java_mono_android_Runtime_initInternal (JNIEnv *env, jclass klass, jstring lang, jobjectArray runtimeApksJava,
                                                          jstring runtimeNativeLibDir, jobjectArray appDirs, jobject loader,
                                                          jobjectArray assembliesJava, jint apiLevel, jboolean isEmulator)
{
	char *mono_log_mask = nullptr;
	char *mono_log_level = nullptr;

	java_mono_Runtime = klass;
	init_logging_categories (mono_log_mask, mono_log_level);

	timing_period total_time;
	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		timing = new Timing ();
		total_time.mark_start ();
	}

#if defined (NET6)
	{
		MonoVMProperties monovm_props;

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
#endif // def NET6

	jstring_array_wrapper applicationDirs (env, appDirs);

	android_api_level = apiLevel;
	androidSystem.detect_embedded_dso_mode (applicationDirs);
	androidSystem.set_running_in_emulator (isEmulator);

	utils.monodroid_store_package_name (application_config.android_package_name);

	jstring_wrapper jstr (env, lang);
	set_environment_variable ("LANG", jstr);

	androidSystem.setup_environment ();

	jstring_wrapper &home = applicationDirs[0];
	set_environment_variable_for_directory ("TMPDIR", applicationDirs[1]);
	set_environment_variable_for_directory ("HOME", home);
	create_xdg_directories_and_environment (home);
	androidSystem.set_primary_override_dir (home);

	disable_external_signal_handlers ();

	jstring_array_wrapper runtimeApks (env, runtimeApksJava);
	androidSystem.setup_app_library_directories (runtimeApks, applicationDirs);

	init_reference_logging (androidSystem.get_primary_override_dir ());
	androidSystem.create_update_dir (androidSystem.get_primary_override_dir ());

#if DEBUG
	setup_gc_logging ();
	set_debug_env_vars ();
#endif

#if !defined (NET6)
	setup_bundled_app ("libmonodroid_bundle_app.so");
#endif

#if defined (ANDROID)
	if (mono_log_level == nullptr || *mono_log_level == '\0') {
		mono_trace_set_level_string ("error");
	} else {
		// `mono_log_level` cannot be freed here, Mono VM stores it internally
		mono_trace_set_level_string (mono_log_level);
	}

	setup_mono_tracing (mono_log_mask);

#if defined (NET6)
	install_logging_handlers ();
#endif // def NET6
#endif

	if (runtimeNativeLibDir != nullptr) {
		jstr = runtimeNativeLibDir;
		androidSystem.set_runtime_libdir (strdup (jstr.get_cstr ()));
		log_info (LOG_DEFAULT, "Using runtime path: %s", androidSystem.get_runtime_libdir ());
	}

	androidSystem.setup_process_args (runtimeApks);

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING)) && !(log_timing_categories & LOG_TIMING_BARE)) {
		mono_counters_enable (static_cast<int>(XA_LOG_COUNTERS));

		dynamic_local_string<SENSIBLE_PATH_MAX> counters_path;
		utils.path_combine (counters_path, androidSystem.get_override_dir (0), "counters.txt");
		log_info_nocheck (LOG_TIMING, "counters path: %s", counters_path.get ());
		counters = utils.monodroid_fopen (counters_path.get (), "a");
		utils.set_world_accessable (counters_path.get ());
	}

	void *dso_handle = nullptr;
#if defined (WINDOWS) || defined (APPLE_OS_X)
	const char *my_location = get_my_location ();
	if (my_location != nullptr) {
		simple_pointer_guard<char, false> dso_path (utils.path_combine (my_location, API_DSO_NAME));
		log_info (LOG_DEFAULT, "Attempting to load %s", dso_path.get ());
		dso_handle = java_interop_lib_load (dso_path.get (), JAVA_INTEROP_LIB_LOAD_GLOBALLY, nullptr);
#if defined (APPLE_OS_X)
		delete[] my_location;
#else   // !defined(APPLE_OS_X)
		free (static_cast<void*>(const_cast<char*>(my_location))); // JI allocates with `calloc`
#endif  // defined(APPLE_OS_X)
	}

	if (dso_handle == nullptr) {
		log_info (LOG_DEFAULT, "Attempting to load %s with \"bare\" dlopen", API_DSO_NAME);
		dso_handle = java_interop_lib_load (API_DSO_NAME, JAVA_INTEROP_LIB_LOAD_GLOBALLY, nullptr);
	}
#endif  // defined(WINDOWS) || defined(APPLE_OS_X)
	if (dso_handle == nullptr)
		dso_handle = androidSystem.load_dso_from_any_directories (API_DSO_NAME, JAVA_INTEROP_LIB_LOAD_GLOBALLY);
#if !defined (NET6)
	init_internal_api_dso (dso_handle);
#endif // ndef NET6
	mono_dl_fallback_register (monodroid_dlopen, monodroid_dlsym, nullptr, nullptr);

	set_profile_options ();

	set_trace_options ();

#if defined (DEBUG) && !defined (WINDOWS)
	debug.start_debugging_and_profiling ();
#endif

#if !defined (NET6)
	mono_config_parse_memory (reinterpret_cast<const char*> (monodroid_config));
	mono_register_machine_config (reinterpret_cast<const char*> (monodroid_machine_config));
#endif // ndef NET6
	log_info (LOG_DEFAULT, "Probing for Mono AOT mode\n");

	MonoAotMode mode = MonoAotMode::MONO_AOT_MODE_NONE;
	if (androidSystem.is_mono_aot_enabled ()) {
		mode = androidSystem.get_mono_aot_mode ();
#if !defined (NET6)
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
#else   // defined (NET6)
		if (mode != MonoAotMode::MONO_AOT_MODE_INTERP_ONLY) {
			log_info (LOG_DEFAULT, "Enabling AOT mode in Mono");
		} else {
			log_info (LOG_DEFAULT, "Enabling Mono Interpreter");
		}
#endif  // !defined (NET6)
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

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> runtime_args;
	androidSystem.monodroid_get_system_property (Debug::DEBUG_MONO_EXTRA_PROPERTY, runtime_args);

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

#if defined (ANDROID) && !defined (NET6)
	// Mono from mono/mono has a bug which requires us to install the handlers after `mono_init_jit_version` is called
	install_logging_handlers ();
#endif // def ANDROID && ndef NET6

	// Install our dummy exception handler on Desktop
	if constexpr (Util::is_running_on_desktop) {
		mono_add_internal_call ("System.Diagnostics.Debugger::Mono_UnhandledException_internal(System.Exception)",
		                                 reinterpret_cast<const void*> (monodroid_Mono_UnhandledException_internal));
	}

	if (XA_UNLIKELY (utils.should_log (LOG_DEFAULT))) {
		log_info_nocheck (
			LOG_DEFAULT,
			"Xamarin.Android version: %s (%s; %s); built on %s",
			BuildInfo::xa_version,
			BuildInfo::architecture,
			BuildInfo::kind,
			BuildInfo::date
		);

#if defined (ANDROID)
		log_info_nocheck (LOG_DEFAULT, "NDK version: %s; API level: %s", BuildInfo::ndk_version, BuildInfo::ndk_api_level);
		log_info_nocheck (LOG_DEFAULT, "MonoVM version: %s", mono_get_runtime_build_info ());
#endif // def ANDROID
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
		loader,
		assembliesJava,
		apiLevel,
		/* isEmulator */ JNI_FALSE
	);
}

JNIEXPORT void JNICALL
Java_mono_android_Runtime_initInternal (JNIEnv *env, jclass klass, jstring lang, jobjectArray runtimeApksJava,
                                jstring runtimeNativeLibDir, jobjectArray appDirs, jobject loader,
                                jobjectArray assembliesJava, jint apiLevel, jboolean isEmulator)
{
	monodroidRuntime.Java_mono_android_Runtime_initInternal (
		env,
		klass,
		lang,
		runtimeApksJava,
		runtimeNativeLibDir,
		appDirs,
		loader,
		assembliesJava,
		apiLevel,
		isEmulator
	);
}

inline void
MonodroidRuntime::Java_mono_android_Runtime_register (JNIEnv *env, jstring managedType, jclass nativeClass, jstring methods)
{
	timing_period total_time;

	dynamic_local_string<SENSIBLE_TYPE_NAME_LENGTH> type;

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING)))
		total_time.mark_start ();

	int managedType_len = env->GetStringLength (managedType);
	const jchar *managedType_ptr = env->GetStringChars (managedType, nullptr);

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		const char *mt_ptr = env->GetStringUTFChars (managedType, nullptr);
		type.assign (mt_ptr, strlen (mt_ptr));
		env->ReleaseStringUTFChars (managedType, mt_ptr);

		log_info_nocheck (LOG_TIMING, "Runtime.register: registering type `%s`", type.get ());
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
	if constexpr (Util::is_running_on_desktop) {
		MonoClass *runtime = utils.monodroid_get_class (domain, "Mono.Android", "Android.Runtime", "JNIEnv");
		register_jni_natives = mono_class_get_method_from_name (runtime, "RegisterJniNatives", 5);
	}
	utils.monodroid_runtime_invoke (domain, register_jni_natives, nullptr, args, nullptr);

	env->ReleaseStringChars (methods, methods_ptr);
	env->ReleaseStringChars (managedType, managedType_ptr);

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		total_time.mark_end ();

		Timing::info (total_time, "Runtime.register: end time");

		dump_counters ("## Runtime.register: type=%s\n", type.get ());
	}
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
	MonoDomain *domain = mono_domain_get ();
	monodroidRuntime.propagate_uncaught_exception (domain, env, javaThread, javaException);
}
