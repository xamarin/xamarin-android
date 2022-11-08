#ifndef __BASIC_ANDROID_SYSTEM_HH
#define __BASIC_ANDROID_SYSTEM_HH

#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>

#include "cpu-arch.hh"
#include "jni-wrappers.hh"
#include "gsl.hh"

namespace xamarin::android::internal
{
	class BasicAndroidSystem
	{
	private:
#if defined (__clang__)
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wc99-designator"
#endif
		// Values correspond to the CPU_KIND_* macros
		static constexpr const char* android_abi_names[CPU_KIND_COUNT] = {
			[0]                 = "unknown",
			[CPU_KIND_ARM]      = "armeabi-v7a",
			[CPU_KIND_ARM64]    = "arm64-v8a",
			[CPU_KIND_MIPS]     = "mips",
			[CPU_KIND_X86]      = "x86",
			[CPU_KIND_X86_64]   = "x86_64",
		};
#if defined (__clang__)
#pragma clang diagnostic pop
#endif
		static constexpr size_t ANDROID_ABI_NAMES_SIZE = sizeof(android_abi_names) / sizeof (android_abi_names[0]);
		static constexpr size_t MAX_OVERRIDES = 1;

	protected:
		using ForEachApkHandler = void (*) (const char *apk, size_t index, size_t apk_count, void *user_data);
		using override_dirs_array = std::array<char*, MAX_OVERRIDES>;

	public:
#ifdef ANDROID64
		static constexpr char SYSTEM_LIB_PATH[] = "/system/lib64";
#elif ANDROID
		static constexpr char SYSTEM_LIB_PATH[] = "/system/lib";
#elif LINUX_FLATPAK
		static constexpr char SYSTEM_LIB_PATH[] = "/app/lib/mono";
#elif defined (__linux__) || defined (__linux)
		static constexpr char SYSTEM_LIB_PATH[] = "/usr/lib";
#elif APPLE_OS_X
		static constexpr char SYSTEM_LIB_PATH[] = "/Library/Frameworks/Xamarin.Android.framework/Versions/Current/lib/xamarin.android/xbuild/Xamarin/Android/lib/host-Darwin";
#elif WINDOWS
		static const char *SYSTEM_LIB_PATH;
#else
		static constexpr char SYSTEM_LIB_PATH[] = "";
#endif

		static const char* get_built_for_abi_name ();

	public:
		static void setup_app_library_directories (jstring_array_wrapper& runtimeApks, jstring_array_wrapper& appDirs, bool have_split_apks) noexcept;

		static const char* get_override_dir (size_t index) noexcept
		{
			if (index >= MAX_OVERRIDES)
				return nullptr;

			return _override_dirs [index];
		}

		static void set_override_dir (uint32_t index, const char* dir) noexcept
		{
			if (index >= MAX_OVERRIDES)
				return;

			_override_dirs [index] = const_cast <char*> (dir);
		}

		static bool is_embedded_dso_mode_enabled () noexcept
		{
			return embedded_dso_mode_enabled;
		}

		static void detect_embedded_dso_mode (jstring_array_wrapper& appDirs) noexcept;

		static char *get_runtime_libdir () noexcept
		{
			return runtime_libdir;
		}

		static void set_runtime_libdir (char *dir) noexcept
		{
			runtime_libdir = dir;
		}

		static char *get_primary_override_dir () noexcept
		{
			return primary_override_dir;
		}

		static void set_primary_override_dir (jstring_wrapper& home) noexcept
		{
			primary_override_dir = determine_primary_override_dir (home);
		}

		static override_dirs_array& override_dirs () noexcept
		{
			return _override_dirs;
		}

		static std::vector<char*>& app_lib_directories () noexcept
		{
			return _app_lib_directories;
		}

	protected:
		static void for_each_apk (jstring_array_wrapper &runtimeApks, ForEachApkHandler handler, void *user_data) noexcept;

	private:
		static void add_apk_libdir (const char *apk, size_t &index, const char *abi) noexcept;
		static void setup_apk_directories (unsigned short running_on_cpu, jstring_array_wrapper &runtimeApks, bool have_split_apks) noexcept;
		static gsl::owner<char*> determine_primary_override_dir (jstring_wrapper &home) noexcept;

		static void set_embedded_dso_mode_enabled (bool yesno) noexcept
		{
			embedded_dso_mode_enabled = yesno;
		}

	private:
		static inline bool embedded_dso_mode_enabled = false;
		static inline char *runtime_libdir = nullptr;
		static inline char *primary_override_dir = nullptr;
		static inline std::vector<char*> _app_lib_directories;
		static inline override_dirs_array _override_dirs;
		static inline const char* _built_for_abi_name = nullptr;
	};
}
#endif // !__BASIC_ANDROID_SYSTEM_HH
