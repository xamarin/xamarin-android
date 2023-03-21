#include <cerrno>

#include <dlfcn.h>
#include <string.h>
#include <dlfcn.h>
#include <sys/types.h>
#include <sys/stat.h>
#include <unistd.h>
#include <android/log.h>

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
static const char* get_libmonosgen_path ();

bool maybe_load_library (const char *path);

#define DO_LOG(_level_,_tag_,_format_,_args_)                      \
	va_start ((_args_), (_format_)); \
	__android_log_vprint ((_level_), (_tag_), (_format_), (_args_)); \
	va_end ((_args_));

static constexpr char TAG[] = "debug-app-helper";

unsigned int log_categories = LOG_DEFAULT | LOG_ASSEMBLY;
BasicUtilities utils;
BasicAndroidSystem androidSystem;

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

	const char *monosgen_path = get_libmonosgen_path ();
	void *monosgen = dlopen (monosgen_path, RTLD_LAZY | RTLD_GLOBAL);
	if (monosgen == nullptr) {
		log_fatal (LOG_DEFAULT, "Failed to dlopen Mono runtime from %s: %s", monosgen_path, dlerror ());
		Helpers::abort_application ();
	}
}

static void
copy_file_to_internal_location (char *to_dir, char *from_dir, char *file)
{
	char *from_file = utils.path_combine (from_dir, file);
	char *to_file   = nullptr;

	do {
		if (!from_file || !utils.file_exists (from_file))
			break;

		log_warn (LOG_DEFAULT, "Copying file `%s` from external location `%s` to internal location `%s`",
				file, from_dir, to_dir);

		to_file = utils.path_combine (to_dir, file);
		if (!to_file)
			break;

		int r = unlink (to_file);
		if (r < 0 && errno != ENOENT) {
			log_warn (LOG_DEFAULT, "Unable to delete file `%s`: %s", to_file, strerror (errno));
			break;
		}

		if (!utils.file_copy (to_file, from_file)) {
			log_warn (LOG_DEFAULT, "Copy failed from `%s` to `%s`: %s", from_file, to_file, strerror (errno));
			break;
		}

		utils.set_user_executable (to_file);
	} while (0);

	delete[] from_file;
	delete[] to_file;
}

static void
copy_native_libraries_to_internal_location ()
{
	for (size_t i = 0; i < BasicAndroidSystem::MAX_OVERRIDES; ++i) {
		monodroid_dir_t *dir;
		monodroid_dirent_t *e;

		char *dir_path = utils.path_combine (BasicAndroidSystem::override_dirs [i], "lib");
		log_warn (LOG_DEFAULT, "checking directory: `%s`", dir_path);

		if (dir_path == nullptr || !utils.directory_exists (dir_path)) {
			log_warn (LOG_DEFAULT, "directory does not exist: `%s`", dir_path);
			delete[] dir_path;
			continue;
		}

		if ((dir = utils.monodroid_opendir (dir_path)) == nullptr) {
			log_warn (LOG_DEFAULT, "could not open directory: `%s`", dir_path);
			delete[] dir_path;
			continue;
		}

		while ((e = readdir (dir)) != nullptr) {
			log_warn (LOG_DEFAULT, "checking file: `%s`", e->d_name);
			if (utils.monodroid_dirent_hasextension (e, ".so")) {
				char *file_name = e->d_name;
				copy_file_to_internal_location (androidSystem.get_primary_override_dir (), dir_path, file_name);
			}
		}
		utils.monodroid_closedir (dir);
		delete[] dir_path;
	}
}

static inline bool
runtime_exists (const char *dir, char*& libmonoso)
{
	if (dir == nullptr || *dir == '\0')
		return false;

	libmonoso = utils.path_combine (dir, SharedConstants::MONO_SGEN_SO);
	log_warn (LOG_DEFAULT, "Checking whether Mono runtime exists at: %s", libmonoso);
	if (utils.file_exists (libmonoso)) {
		log_info (LOG_DEFAULT, "Mono runtime found at: %s", libmonoso);
		return true;
	}
	delete[] libmonoso;
	libmonoso = nullptr;

	return false;
}

static const char*
get_libmonosgen_path ()
{
	char *libmonoso;

	// Android 5 includes some restrictions on loading dynamic libraries via dlopen() from
	// external storage locations so we need to file copy the shared object to an internal
	// storage location before loading it.
	copy_native_libraries_to_internal_location ();

	if (androidSystem.is_embedded_dso_mode_enabled ()) {
		return SharedConstants::MONO_SGEN_SO;
	}

	for (size_t i = 0; i < BasicAndroidSystem::MAX_OVERRIDES; ++i) {
		if (runtime_exists (BasicAndroidSystem::override_dirs [i], libmonoso)) {
			return libmonoso;
		}
	}

	for (size_t i = 0; i < BasicAndroidSystem::app_lib_directories_size; i++) {
		if (runtime_exists (BasicAndroidSystem::app_lib_directories [i], libmonoso)) {
			return libmonoso;
		}
	}

	if (androidSystem.get_runtime_libdir () != nullptr) {
		libmonoso = utils.path_combine (androidSystem.get_runtime_libdir (), SharedConstants::MONO_SGEN_ARCH_SO);
	} else
		libmonoso = nullptr;

	if (libmonoso != nullptr && utils.file_exists (libmonoso)) {
		char* links_dir = utils.path_combine (androidSystem.get_primary_override_dir (), "links");
		char* link = utils.path_combine (links_dir, SharedConstants::MONO_SGEN_SO);
		if (!utils.directory_exists (links_dir)) {
			if (!utils.directory_exists (androidSystem.get_primary_override_dir ()))
				utils.create_public_directory (androidSystem.get_primary_override_dir ());
			utils.create_public_directory (links_dir);
		}
		delete[] links_dir;
		if (!utils.file_exists (link)) {
			int result = symlink (libmonoso, link);
			if (result != 0 && errno == EEXIST) {
				log_warn (LOG_DEFAULT, "symlink exists, recreating: %s -> %s", link, libmonoso);
				unlink (link);
				result = symlink (libmonoso, link);
			}
			if (result != 0)
				log_warn (LOG_DEFAULT, "symlink failed with errno=%i %s", errno, strerror (errno));
		}
		delete[] libmonoso;
		libmonoso = link;
	}

	log_warn (LOG_DEFAULT, "Trying to load sgen from: %s", libmonoso != nullptr ? libmonoso : "<NULL>");
	if (libmonoso != nullptr && utils.file_exists (libmonoso))
		return libmonoso;
	delete[] libmonoso;

	if (runtime_exists (BasicAndroidSystem::SYSTEM_LIB_PATH, libmonoso))
		return libmonoso;
	log_fatal (LOG_DEFAULT, "Cannot find '%s'. Looked in the following locations:", SharedConstants::MONO_SGEN_SO);

	for (size_t i = 0; i < BasicAndroidSystem::MAX_OVERRIDES; ++i) {
		if (BasicAndroidSystem::override_dirs [i] == nullptr)
			continue;
		log_fatal (LOG_DEFAULT, "  %s", BasicAndroidSystem::override_dirs [i]);
	}

	for (size_t i = 0; i < BasicAndroidSystem::app_lib_directories_size; i++) {
		log_fatal (LOG_DEFAULT, "  %s", BasicAndroidSystem::app_lib_directories [i]);
	}

	log_fatal (LOG_DEFAULT, "Do you have a shared runtime build of your app with AndroidManifest.xml android:minSdkVersion < 10 while running on a 64-bit Android 5.0 target? This combination is not supported.");
	log_fatal (LOG_DEFAULT, "Please either set android:minSdkVersion >= 10 or use a build without the shared runtime (like default Release configuration).");
	Helpers::abort_application ();

	return libmonoso;
}

void
log_debug_nocheck ([[maybe_unused]] LogCategories category, const char *format, ...)
{
	va_list args;

	if ((log_categories & category) == 0)
		return;

	DO_LOG (ANDROID_LOG_DEBUG, TAG, format, args);
}

void
log_info ([[maybe_unused]] LogCategories category, const char *format, ...)
{
	va_list args;

	DO_LOG (ANDROID_LOG_INFO, TAG, format, args);
}

void
log_info_nocheck ([[maybe_unused]] LogCategories category, const char *format, ...)
{
	va_list args;

	if ((log_categories & category) == 0)
		return;

	DO_LOG (ANDROID_LOG_INFO, TAG, format, args);
}

void log_error ([[maybe_unused]] LogCategories category, const char* format, ...)
{
	va_list args;

	DO_LOG (ANDROID_LOG_ERROR, TAG, format, args);
}

void log_fatal ([[maybe_unused]] LogCategories category, const char* format, ...)
{
	va_list args;

	DO_LOG (ANDROID_LOG_FATAL, TAG, format, args);
}

void log_warn ([[maybe_unused]] LogCategories category, const char* format, ...)
{
	va_list args;

	DO_LOG (ANDROID_LOG_WARN, TAG, format, args);
}
