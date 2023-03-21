#ifndef __BASIC_ANDROID_SYSTEM_HH
#define __BASIC_ANDROID_SYSTEM_HH

#include <cstddef>
#include <cstdint>

#include "cpu-arch.hh"
#include "jni-wrappers.hh"

namespace xamarin::android::internal
{
	class BasicAndroidSystem
	{
	protected:
		using ForEachApkHandler = void (BasicAndroidSystem::*) (const char *apk, size_t index, size_t apk_count, void *user_data);

	private:
#if defined (__clang__)
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wc99-designator"
#endif
		// Values correspond to the CPU_KIND_* macros
		static constexpr const char* android_abi_names[CPU_KIND_X86_64+1] = {
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
		static const char* built_for_abi_name;

	public:
#ifdef ANDROID64
		static constexpr char SYSTEM_LIB_PATH[] = "/system/lib64";
#elif ANDROID
		static constexpr char SYSTEM_LIB_PATH[] = "/system/lib";
#else
		static constexpr char SYSTEM_LIB_PATH[] = "";
#endif

		static constexpr size_t MAX_OVERRIDES = 1;
		static char* override_dirs [MAX_OVERRIDES];
		static const char **app_lib_directories;
		static size_t app_lib_directories_size;
		static const char* get_built_for_abi_name ();

	public:
		void setup_app_library_directories (jstring_array_wrapper& runtimeApks, jstring_array_wrapper& appDirs, bool have_split_apks);

		const char* get_override_dir (size_t index) const
		{
			if (index >= MAX_OVERRIDES)
				return nullptr;

			return override_dirs [index];
		}

		void set_override_dir (uint32_t index, const char* dir)
		{
			if (index >= MAX_OVERRIDES)
				return;

			override_dirs [index] = const_cast <char*> (dir);
		}

		bool is_embedded_dso_mode_enabled () const
		{
			return embedded_dso_mode_enabled;
		}

		void detect_embedded_dso_mode (jstring_array_wrapper& appDirs) noexcept;

		char *get_runtime_libdir () const
		{
			return runtime_libdir;
		}

		void set_runtime_libdir (char *dir)
		{
			runtime_libdir = dir;
		}

		char *get_primary_override_dir () const
		{
			return primary_override_dir;
		}

		void set_primary_override_dir (jstring_wrapper& home)
		{
			primary_override_dir = determine_primary_override_dir (home);
		}

	protected:
		void  for_each_apk (jstring_array_wrapper &runtimeApks, ForEachApkHandler handler, void *user_data);

	private:
		void add_apk_libdir (const char *apk, size_t &index, const char *abi) noexcept;
		void setup_apk_directories (unsigned short running_on_cpu, jstring_array_wrapper &runtimeApks, bool have_split_apks) noexcept;
		char* determine_primary_override_dir (jstring_wrapper &home);

		void set_embedded_dso_mode_enabled (bool yesno) noexcept
		{
			embedded_dso_mode_enabled = yesno;
		}

	private:
		bool  embedded_dso_mode_enabled = false;
		char *runtime_libdir = nullptr;
		char *primary_override_dir = nullptr;
	};
}
#endif // !__BASIC_ANDROID_SYSTEM_HH
