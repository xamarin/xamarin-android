#include <cerrno>

#include "basic-android-system.hh"
#include "cpp-util.hh"
#include "shared-constants.hh"
#include "util.hh"

using namespace xamarin::android;
using namespace xamarin::android::internal;

void
BasicAndroidSystem::detect_embedded_dso_mode (jstring_array_wrapper& appDirs) noexcept
{
	LOG_FUNC_ENTER ();

	// appDirs[SharedConstants::APP_DIRS_DATA_DIR_INDEX] points to the native library directory
	std::unique_ptr<char> libmonodroid_path {Util::path_combine (appDirs[SharedConstants::APP_DIRS_DATA_DIR_INDEX].get_cstr (), "libmonodroid.so")};
	log_debug (LOG_ASSEMBLY, "Checking if libmonodroid was unpacked to %s", libmonodroid_path.get ());
	if (!Util::file_exists (libmonodroid_path.get ())) {
		log_debug (LOG_ASSEMBLY, "%s not found, assuming application/android:extractNativeLibs == false", libmonodroid_path.get ());
		set_embedded_dso_mode_enabled (true);
	} else {
		log_debug (LOG_ASSEMBLY, "Native libs extracted to %s, assuming application/android:extractNativeLibs == true", appDirs[SharedConstants::APP_DIRS_DATA_DIR_INDEX].get_cstr ());
		set_embedded_dso_mode_enabled (false);
	}

	LOG_FUNC_LEAVE ();
}

void
BasicAndroidSystem::setup_app_library_directories (jstring_array_wrapper& runtimeApks, jstring_array_wrapper& appDirs, bool have_split_apks) noexcept
{
	LOG_FUNC_ENTER ();

	if (!is_embedded_dso_mode_enabled ()) {
		log_info (LOG_DEFAULT, "Setting up for DSO lookup in app data directories");
		app_lib_directories ().push_back (Util::strdup_new (appDirs[SharedConstants::APP_DIRS_DATA_DIR_INDEX].get_cstr ()));
		log_debug (LOG_ASSEMBLY, "Added filesystem DSO lookup location: %s", appDirs[SharedConstants::APP_DIRS_DATA_DIR_INDEX].get_cstr ());
	} else {
		log_info (LOG_DEFAULT, "Setting up for DSO lookup directly in the APK");
		app_lib_directories ().reserve (runtimeApks.get_length ());

		unsigned short running_on_cpu = 0;
		_monodroid_detect_running_cpu (&running_on_cpu);
		setup_apk_directories (running_on_cpu, runtimeApks, have_split_apks);
	}

	LOG_FUNC_LEAVE ();
}

void
BasicAndroidSystem::for_each_apk (jstring_array_wrapper &runtimeApks, ForEachApkHandler handler, void *user_data) noexcept
{
	LOG_FUNC_ENTER ();

	size_t apksLength = runtimeApks.get_length ();
	for (size_t i = 0; i < apksLength; ++i) {
		jstring_wrapper &e = runtimeApks [i];

		(*handler) (e.get_cstr (), i, apksLength, user_data);
	}

	LOG_FUNC_LEAVE ();
}

force_inline void
BasicAndroidSystem::add_apk_libdir (const char *apk, size_t &index, const char *abi) noexcept
{
	LOG_FUNC_ENTER ();

	app_lib_directories ().push_back (Util::string_concat (apk, "!/lib/", abi));
	log_debug (LOG_ASSEMBLY, "Added APK DSO lookup location: %s", app_lib_directories ()[index]);
	index++;

	LOG_FUNC_LEAVE ();
}

force_inline void
BasicAndroidSystem::setup_apk_directories (unsigned short running_on_cpu, jstring_array_wrapper &runtimeApks, bool have_split_apks) noexcept
{
	LOG_FUNC_ENTER ();

	const char *abi = android_abi_names [running_on_cpu];
	size_t number_of_added_directories = 0;

	for (size_t i = 0; i < runtimeApks.get_length (); ++i) {
		jstring_wrapper &e = runtimeApks [i];
		const char *apk = e.get_cstr ();

		if (have_split_apks) {
			if (Util::ends_with (apk, SharedConstants::split_config_abi_apk_name)) {
				add_apk_libdir (apk, number_of_added_directories, abi);
				break;
			}
		} else {
			add_apk_libdir (apk, number_of_added_directories, abi);
		}
	}

	LOG_FUNC_LEAVE ();
}

gsl::owner<char*>
BasicAndroidSystem::determine_primary_override_dir (jstring_wrapper &home) noexcept
{
	LOG_FUNC_ENTER ();
	LOG_FUNC_LEAVE ();
	return Util::path_combine (home.get_cstr (), ".__override__");
}

const char*
BasicAndroidSystem::get_built_for_abi_name ()
{
	LOG_FUNC_ENTER ();

	if (_built_for_abi_name == nullptr) {
		_built_for_abi_name = android_abi_names [BuiltForCpu::cpu ()];
	}

	LOG_FUNC_LEAVE ();
	return _built_for_abi_name;
}
