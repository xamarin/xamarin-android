#include <cerrno>
#include <string>

#include <dlfcn.h>
#include <cstring>
#include <dlfcn.h>
#include <sys/types.h>
#include <sys/stat.h>
#include <unistd.h>
#ifdef ANDROID
#include <android/log.h>
#endif

#include "basic-android-system.hh"
#include "basic-utilities.hh"
#include "debug-app-helper.hh"
#include "shared-constants.hh"
#include "jni-wrappers.hh"

using namespace xamarin::android;
using namespace xamarin::android::internal;

#undef DO_LOG
#undef log_info

void log_info (LogCategories category, const char *format, ...);
void log_warn (LogCategories category, const char *format, ...);
void log_error (LogCategories category, const char *format, ...);
void log_fatal (LogCategories category, const char *format, ...);

static void copy_file_to_internal_location (char *to_dir, char *from_dir, char *file);
static void copy_native_libraries_to_internal_location ();
static const std::string get_libmonosgen_path ();

bool maybe_load_library (const char *path);

#ifndef ANDROID
#define ANDROID_LOG_INFO  1
#define ANDROID_LOG_WARN  2
#define ANDROID_LOG_ERROR 3
#define ANDROID_LOG_FATAL 4
#define ANDROID_LOG_DEBUG 5

static void
__android_log_vprint (int prio, const char* tag, const char* fmt, va_list ap)
{
	printf ("%d [%s] ", prio, tag);
	vprintf (fmt, ap);
	putchar ('\n');
	fflush (stdout);
}
#endif

static constexpr char TAG[] = "debug-app-helper";

unsigned int log_categories = LOG_DEFAULT | LOG_ASSEMBLY;
BasicUtilities utils;
BasicAndroidSystem androidSystem;

static
void DO_LOG (int prio, const char *tag, const char *fmt, va_list ap) noexcept
{
	__android_log_vprint (prio, tag, fmt, ap);
	va_end (ap);
}

JNIEXPORT jint JNICALL
JNI_OnLoad ([[maybe_unused]] JavaVM *vm, [[maybe_unused]] void *reserved)
{
	return JNI_VERSION_1_6;
}

JNIEXPORT void JNICALL
Java_mono_android_DebugRuntime_init (JNIEnv *env, [[maybe_unused]] jclass klass, jobjectArray runtimeApksJava,
                                     jstring runtimeNativeLibDir, jobjectArray appDirs, jboolean haveSplitApks)
{
	jstring_array_wrapper applicationDirs (env, appDirs);
	jstring_array_wrapper runtimeApks (env, runtimeApksJava);

	androidSystem.detect_embedded_dso_mode (applicationDirs);
	androidSystem.set_primary_override_dir (applicationDirs [0]);
	androidSystem.set_override_dir (0, androidSystem.get_primary_override_dir ());
	androidSystem.setup_app_library_directories (runtimeApks, applicationDirs, haveSplitApks);

	jstring_wrapper jstr (env);

	if (runtimeNativeLibDir != nullptr) {
		jstr = runtimeNativeLibDir;
		androidSystem.set_runtime_libdir (utils.strdup_new (jstr.get_cstr ()));
		log_warn (LOG_DEFAULT, "Using runtime path: %s", androidSystem.get_runtime_libdir ());
	}

	const std::string monosgen_path = get_libmonosgen_path ();
	void *monosgen = dlopen (monosgen_path.c_str (), RTLD_LAZY | RTLD_GLOBAL);
	if (monosgen == nullptr) {
		log_fatal (LOG_DEFAULT, "Failed to dlopen Mono runtime from %s: %s", monosgen_path.c_str (), dlerror ());
		exit (FATAL_EXIT_CANNOT_FIND_LIBMONOSGEN);
	}
}

#if defined (ANDROID)
static void
copy_file_to_internal_location (char *to_dir, char *from_dir, char *file)
{
	std::unique_ptr<char> from_file { utils.path_combine (from_dir, file) };

	do {
		if (!from_file || !utils.file_exists (from_file.get ()))
			break;

		log_warn (LOG_DEFAULT, "Copying file `%s` from external location `%s` to internal location `%s`",
				file, from_dir, to_dir);

		std::unique_ptr<char> to_file { utils.path_combine (to_dir, file) };
		if (!to_file)
			break;

		int r = unlink (to_file.get ());
		if (r < 0 && errno != ENOENT) {
			log_warn (LOG_DEFAULT, "Unable to delete file `%s`: %s", to_file.get (), strerror (errno));
			break;
		}

		if (!utils.file_copy (to_file.get (), from_file.get ())) {
			log_warn (LOG_DEFAULT, "Copy failed from `%s` to `%s`: %s", from_file.get (), to_file.get (), strerror (errno));
			break;
		}

		utils.set_user_executable (to_file.get ());
	} while (false);
}
#else  /* !defined (ANDROID) */
static void
copy_file_to_internal_location ([[maybe_unused]] char *to_dir, [[maybe_unused]] char *from_dir, [[maybe_unused]] char* file)
{
}
#endif /* defined (ANDROID) */

static void
copy_native_libraries_to_internal_location ()
{
	for (auto const& override_dir : androidSystem.override_dirs ()) {
		std::unique_ptr<char> dir_path { utils.path_combine (override_dir, "lib") };
		log_warn (LOG_DEFAULT, "checking directory: `%s`", dir_path.get ());

		if (dir_path == nullptr || !utils.directory_exists (dir_path.get ())) {
			log_warn (LOG_DEFAULT, "directory does not exist: `%s`", dir_path.get ());
			continue;
		}

		monodroid_dir_t *dir = utils.monodroid_opendir (dir_path.get ());
		if (dir == nullptr) {
			log_warn (LOG_DEFAULT, "could not open directory: `%s`", dir_path.get ());
			continue;
		}

		monodroid_dirent_t *e = nullptr;
		while ((e = readdir (dir)) != nullptr) {
			log_warn (LOG_DEFAULT, "checking file: `%s`", e->d_name);
			if (utils.monodroid_dirent_hasextension (e, ".so")) {
#if WINDOWS
				char *file_name = utils.utf16_to_utf8 (e->d_name);
#else   /* def WINDOWS */
				char *file_name = e->d_name;
#endif  /* ndef WINDOWS */
				copy_file_to_internal_location (androidSystem.get_primary_override_dir (), dir_path.get (), file_name);
#if WINDOWS
				free (file_name);
#endif  /* def WINDOWS */
			}
		}
		utils.monodroid_closedir (dir);
	}
}

force_inline std::string
combine_paths (const char *path1, const char *path2) noexcept
{
	std::string ret { path1 };
	ret.append (MONODROID_PATH_SEPARATOR);
	ret.append (path2);

	return ret;
}

force_inline bool
runtime_exists (const char *dir, std::string& libmonoso)
{
	if (dir == nullptr || *dir == '\0')
		return false;

	libmonoso = combine_paths (dir, SharedConstants::MONO_SGEN_SO);

	log_warn (LOG_DEFAULT, "Checking whether Mono runtime exists at: %s", libmonoso.c_str ());
	if (utils.file_exists (libmonoso.c_str ())) {
		log_info (LOG_DEFAULT, "Mono runtime found at: %s", libmonoso.c_str ());
		return true;
	}

	return false;
}

static const std::string
get_libmonosgen_path ()
{
	// Android 5 includes some restrictions on loading dynamic libraries via dlopen() from
	// external storage locations so we need to file copy the shared object to an internal
	// storage location before loading it.
	copy_native_libraries_to_internal_location ();

	if (androidSystem.is_embedded_dso_mode_enabled ()) {
		return SharedConstants::MONO_SGEN_SO;
	}

	std::string libmonoso;
	for (auto const& override_dir : androidSystem.override_dirs ()) {
		if (runtime_exists (override_dir, libmonoso)) {
			return libmonoso;
		}
	}

	for (auto const& dir : androidSystem.app_lib_directories ()) {
		if (runtime_exists (dir, libmonoso)) {
			return libmonoso;
		}
	}

	if (androidSystem.get_runtime_libdir () != nullptr) {
		libmonoso = combine_paths (androidSystem.get_runtime_libdir (), SharedConstants::MONO_SGEN_ARCH_SO);
	} else {
		libmonoso.clear ();
	}

	if (!libmonoso.empty () && utils.file_exists (libmonoso.c_str ())) {
		std::string links_dir = combine_paths (androidSystem.get_primary_override_dir (), "links");
		std::string link = combine_paths (links_dir.c_str (), SharedConstants::MONO_SGEN_SO);

		if (!utils.directory_exists (links_dir.c_str ())) {
			if (!utils.directory_exists (androidSystem.get_primary_override_dir ()))
				utils.create_public_directory (androidSystem.get_primary_override_dir ());
			utils.create_public_directory (links_dir.c_str ());
		}

		if (!utils.file_exists (link.c_str ())) {
			int result = symlink (libmonoso.c_str (), link.c_str ());
			if (result != 0 && errno == EEXIST) {
				log_warn (LOG_DEFAULT, "symlink exists, recreating: %s -> %s", link.c_str (), libmonoso.c_str ());
				unlink (link.c_str ());
				result = symlink (libmonoso.c_str (), link.c_str ());
			}
			if (result != 0)
				log_warn (LOG_DEFAULT, "symlink failed with errno=%i %s", errno, strerror (errno));
		}
		libmonoso = link;
	}

	log_warn (LOG_DEFAULT, "Trying to load sgen from: %s", !libmonoso.empty () ? libmonoso.c_str () : "<NULL>");
	if (!libmonoso.empty () && utils.file_exists (libmonoso.c_str ()))
		return libmonoso;

#ifdef WINDOWS
	if (runtime_exists (get_libmonoandroid_directory_path (), libmonoso))
		return libmonoso;
#endif

	if (runtime_exists (BasicAndroidSystem::SYSTEM_LIB_PATH, libmonoso))
		return libmonoso;
	log_fatal (LOG_DEFAULT, "Cannot find '%s'. Looked in the following locations:", SharedConstants::MONO_SGEN_SO);

	for (auto const& override_dir : androidSystem.override_dirs ()) {
		if (override_dir == nullptr)
			continue;
		log_fatal (LOG_DEFAULT, "  %s", override_dir);
	}

	for (auto const& dir : androidSystem.app_lib_directories ()) {
		log_fatal (LOG_DEFAULT, "  %s", dir);
	}

	log_fatal (LOG_DEFAULT, "Do you have a shared runtime build of your app with AndroidManifest.xml android:minSdkVersion < 10 while running on a 64-bit Android 5.0 target? This combination is not supported.");
	log_fatal (LOG_DEFAULT, "Please either set android:minSdkVersion >= 10 or use a build without the shared runtime (like default Release configuration).");
	abort ();

	return libmonoso;
}

void
log_debug_nocheck ([[maybe_unused]] LogCategories category, const char *format, ...)
{
	if ((log_categories & category) == 0)
		return;

	va_list args;
	va_start (args, format);

	DO_LOG (ANDROID_LOG_DEBUG, TAG, format, args);
}

void
log_info ([[maybe_unused]] LogCategories category, const char *format, ...)
{
	va_list args;
	va_start (args, format);

	DO_LOG (ANDROID_LOG_INFO, TAG, format, args);
}

void
log_info_nocheck ([[maybe_unused]] LogCategories category, const char *format, ...)
{
	if ((log_categories & category) == 0)
		return;

	va_list args;
	va_start (args, format);

	DO_LOG (ANDROID_LOG_INFO, TAG, format, args);
}

void log_error ([[maybe_unused]] LogCategories category, const char* format, ...)
{
	va_list args;
	va_start (args, format);

	DO_LOG (ANDROID_LOG_ERROR, TAG, format, args);
}

void log_fatal ([[maybe_unused]] LogCategories category, const char* format, ...)
{
	va_list args;
	va_start (args, format);

	DO_LOG (ANDROID_LOG_FATAL, TAG, format, args);
}

void log_warn ([[maybe_unused]] LogCategories category, const char* format, ...)
{
	va_list args;
	va_start (args, format);

	DO_LOG (ANDROID_LOG_WARN, TAG, format, args);
}
