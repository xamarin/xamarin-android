// Dear Emacs, this is a -*- C++ -*- header
#ifndef __ANDROID_SYSTEM_H
#define __ANDROID_SYSTEM_H

#include <stdint.h>
#include <stdlib.h>
#include <pthread.h>
#include <jni.h>

#ifdef ANDROID
#include <sys/system_properties.h>
#endif

#include "util.hh"
#include "cppcompat.hh"
#include "xamarin-app.hh"
#include "shared-constants.hh"
#include "basic-android-system.hh"
#include "strings.hh"
#include "gsl.hh"

#include <mono/jit/jit.h>

#if !defined (ANDROID)
constexpr uint32_t PROP_NAME_MAX = 32;
constexpr uint32_t PROP_VALUE_MAX = 92;
#endif

constexpr size_t PROPERTY_VALUE_BUFFER_LEN = PROP_VALUE_MAX + 1;

namespace xamarin::android {
	class jstring_wrapper;
	class jstring_array_wrapper;
}

namespace xamarin::android::internal
{
#if defined (DEBUG) || !defined (ANDROID)
	struct BundledProperty;
#endif

	class AndroidSystem : public BasicAndroidSystem
	{
	private:
#if defined (DEBUG) || !defined (ANDROID)
		static constexpr char OVERRIDE_ENVIRONMENT_FILE_NAME[] = "environment";
		static constexpr uint32_t OVERRIDE_ENVIRONMENT_FILE_HEADER_SIZE = 22;
		static BundledProperty *bundled_properties;
#endif
#if defined (WINDOWS)
		static std::mutex readdir_mutex;
		static char *libmonoandroid_directory_path;
#endif

	public:
		static void setup_environment () noexcept;
		static void setup_process_args (jstring_array_wrapper &runtimeApks) noexcept;
		static void create_update_dir (char *override_dir) noexcept;

		template<size_t N>
		static bool monodroid_system_property_exists (const char (&name)[N]) noexcept
		{
			return monodroid_system_property_exists_impl (static_cast<const char*>(name));
		}

		static int monodroid_get_system_property (const char *name, gsl::owner<char**> value) noexcept;

		template<size_t N>
		static int monodroid_get_system_property (const char (&name)[N], dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN>& value) noexcept
		{
			return fetch_system_property (static_cast<const char*>(name), value);
		}

		static size_t monodroid_get_system_property_from_overrides (const char *name, char ** value) noexcept;
		static char* get_bundled_app (JNIEnv *env, jstring dir) noexcept;
		static int   count_override_assemblies () noexcept;
		static long  get_gref_gc_threshold () noexcept;
		static void* load_dso (const char *path, unsigned int dl_flags, bool skip_exists_check) noexcept;
		static void* load_dso_from_any_directories (const char *name, unsigned int dl_flags) noexcept;
		static bool get_full_dso_path_on_disk (const char *dso_name, dynamic_local_string<SENSIBLE_PATH_MAX>& path) noexcept;
		static monodroid_dirent_t* readdir (monodroid_dir_t *dir) noexcept;

		static long get_max_gref_count () noexcept
		{
			return max_gref_count;
		}

		static void init_max_gref_count () noexcept
		{
			max_gref_count = get_max_gref_count_from_system ();
		}

#if defined (WINDOWS)
		static int setenv (const char *name, const char *value, int overwrite) noexcept;
#endif
		static bool is_assembly_preload_enabled () noexcept
		{
			return application_config.uses_assembly_preload;
		}

		static bool is_mono_llvm_enabled () noexcept
		{
			return application_config.uses_mono_llvm;
		}

		static bool is_mono_aot_enabled () noexcept
		{
			return application_config.uses_mono_aot;
		}

		static MonoAotMode get_mono_aot_mode () noexcept
		{
			return aotMode;
		}

		static bool is_interpreter_enabled () noexcept
		{
#if !defined (NET)
			// HACK! See below
			return get_mono_aot_mode () == MonoAotMode::MONO_AOT_MODE_LAST && is_aot_mode_last_really_interpreter_mode ();
#else   // defined (NET)
			return get_mono_aot_mode () == MonoAotMode::MONO_AOT_MODE_INTERP_ONLY;
#endif  // !defined (NET)
		}

		// Hack, see comment for `aot_mode_last_is_interpreter` at the bottom of the class declaration
		static bool is_aot_mode_last_really_interpreter_mode () noexcept
		{
#if !defined(NET)
			return aot_mode_last_is_interpreter;
#else   // defined (NET)
			return false;
#endif  // !defined (NET)
		}

		static void set_running_in_emulator (bool yesno) noexcept
		{
			running_in_emulator = yesno;
		}

	private:
#if defined (DEBUG) || !defined (ANDROID)
		static void add_system_property (const char *name, const char *value) noexcept;
		static void setup_environment (const char *name, const char *value) noexcept;
		static void setup_environment_from_override_file (const char *path) noexcept;
		static BundledProperty* lookup_system_property (const char *name) noexcept;
#endif
		static const char* lookup_system_property (const char *name, size_t &value_len) noexcept;
		static long  get_max_gref_count_from_system () noexcept;
		static void setup_process_args_apk (const char *apk, size_t index, size_t apk_count, void *user_data) noexcept;
		static int  _monodroid__system_property_get (const char *name, char *sp_value, const size_t sp_value_len) noexcept;
#if defined (DEBUG) || !defined (ANDROID)
		static size_t _monodroid_get_system_property_from_file (const char *path, char **value) noexcept;
#endif
		static bool get_full_dso_path (const char *base_dir, const char *dso_path, dynamic_local_string<SENSIBLE_PATH_MAX>& path) noexcept;

		template<class Container>
		force_inline
		static void* load_dso_from_dirs (Container const& directories, const char *dso_name, unsigned int dl_flags) noexcept
		{
			LOG_FUNC_ENTER ();

			if (dso_name == nullptr) {
				LOG_FUNC_LEAVE ();
				return nullptr;
			}

			for (auto const& dir : directories) {
				void *handle = load_dso_from_directory (dir, dso_name, dl_flags);
				if (handle != nullptr) {
					LOG_FUNC_LEAVE ();
					return handle;
				}
			}

			LOG_FUNC_LEAVE ();
			return nullptr;
		}

		static void* load_dso_from_specified_dirs (std::vector<char*> const& directories, const char *dso_name, unsigned int dl_flags) noexcept
		{
			return load_dso_from_dirs (directories, dso_name, dl_flags);
		}

		static void* load_dso_from_specified_dirs (override_dirs_array const& directories, const char *dso_name, unsigned int dl_flags) noexcept
		{
			return load_dso_from_dirs (directories, dso_name, dl_flags);
		}

		static void* load_dso_from_directory (const char *directory, const char *dso_name, unsigned int dl_flags) noexcept;

		static void* load_dso_from_app_lib_dirs (const char *name, unsigned int dl_flags) noexcept;
		static void* load_dso_from_override_dirs (const char *name, unsigned int dl_flags) noexcept;
		static bool get_existing_dso_path_on_disk (const char *base_dir, const char *dso_name, dynamic_local_string<SENSIBLE_PATH_MAX>& path) noexcept;

		static int fetch_system_property (const char *name, dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN>& value) noexcept;
		static bool monodroid_system_property_exists_impl (const char *name) noexcept;
#if defined (WINDOWS)
		static struct _wdirent* readdir_windows (_WDIR *dirp) noexcept;
		static char* get_libmonoandroid_directory_path () noexcept;
		static int symlink (const char *target, const char *linkpath) noexcept;

#endif // WINDOWS

#if !defined (ANDROID)
		static void monodroid_strreplace (char *buffer, char old_char, char new_char) noexcept;
#endif // !ANDROID
	private:
		static inline long max_gref_count = 0;
		static inline MonoAotMode aotMode = MonoAotMode::MONO_AOT_MODE_NONE;
		static inline bool running_in_emulator = false;

#if !defined (NET)
		// This is a hack because of the way Mono currently switches the full interpreter (no JIT) mode. In Mono
		// **internal** headers there's an AOT mode macro, `MONO_EE_MODE_INTERP`, whose value is exactly the same as
		// MonoAotMode::MONO_AOT_MODE_LAST.  However, we use `MonoAotMode::MONO_AOT_MODE_LAST` as a sentinel to indicate
		// that we want to use the default Mono AOT/JIT mode and so we can't "overload" it to mean something else for
		// the sake of using Mono's internal functionality.  Until Mono makes `MONO_EE_MODE_INTERP` part of the public
		// `MonoAotMode` enum and its value is not in conflict with the sentinel, we will use this hack.
		//
		// See also: https://github.com/mono/mono/issues/18893
		//
		static inline bool aot_mode_last_is_interpreter = false;
#endif  // !defined (NET)
	};
}
#endif // !__ANDROID_SYSTEM_H
