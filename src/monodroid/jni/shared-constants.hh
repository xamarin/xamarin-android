#ifndef __SHARED_CONSTANTS_HH
#define __SHARED_CONSTANTS_HH

#include <string_view>
#include "cpp-util.hh"

namespace xamarin::android::internal
{
// _WIN32 is defined with _WIN64 so _WIN64 must be checked first.
#if __SIZEOF_POINTER__ == 8
#define __BITNESS__ "64bit"
#elif __SIZEOF_POINTER__ == 4
#define __BITNESS__ "32bit"
#else
#error Unknown pointer size for this platform
#endif

	class SharedConstants
	{
	public:
		// These three MUST be the same as like-named constants in src/Xamarin.Android.Build.Tasks/Utilities/MonoAndroidHelper.Basic.cs
		static constexpr std::string_view MANGLED_ASSEMBLY_NAME_EXT { ".so" };
		static constexpr std::string_view MANGLED_ASSEMBLY_REGULAR_ASSEMBLY_MARKER { "lib_" };
		static constexpr size_t REGULAR_ASSEMBLY_MARKER_INDEX = 3;              // this ☝️
		static constexpr char REGULAR_ASSEMBLY_MARKER_CHAR = MANGLED_ASSEMBLY_REGULAR_ASSEMBLY_MARKER[REGULAR_ASSEMBLY_MARKER_INDEX];
		static constexpr std::string_view MANGLED_ASSEMBLY_SATELLITE_ASSEMBLY_MARKER { "lib-" };
		static constexpr size_t SATELLITE_ASSEMBLY_MARKER_INDEX = 3;              // this ☝️
		static constexpr char SATELLITE_ASSEMBLY_MARKER_CHAR = MANGLED_ASSEMBLY_SATELLITE_ASSEMBLY_MARKER[SATELLITE_ASSEMBLY_MARKER_INDEX];

		static constexpr std::string_view MONO_ANDROID_RUNTIME_ASSEMBLY_NAME  { "Mono.Android.Runtime" };
		static constexpr std::string_view MONO_ANDROID_ASSEMBLY_NAME          { "Mono.Android" };
		static constexpr std::string_view JAVA_INTEROP_ASSEMBLY_NAME          { "Java.Interop" };
		static constexpr std::string_view ANDROID_RUNTIME_NS_NAME             { "Android.Runtime" };
		static constexpr std::string_view JNIENVINIT_CLASS_NAME               { "JNIEnvInit" };
		static constexpr std::string_view JNIENV_CLASS_NAME                   { "JNIEnv" };
		static constexpr std::string_view ANDROID_ENVIRONMENT_CLASS_NAME      { "AndroidEnvironment" };
		static constexpr std::string_view ANDROID_RUNTIME_INTERNAL_CLASS_NAME { "AndroidRuntimeInternal" };
		static constexpr std::string_view DLL_EXTENSION                       { ".dll" };
		static constexpr std::string_view PDB_EXTENSION                       { ".pdb" };

		static constexpr std::string_view RUNTIME_CONFIG_BLOB_BASE_NAME       { "libarc.bin" };
		static constexpr size_t runtime_config_blob_name_size                 = calc_size (RUNTIME_CONFIG_BLOB_BASE_NAME, MANGLED_ASSEMBLY_NAME_EXT);
		static constexpr auto RUNTIME_CONFIG_BLOB_NAME                        = concat_string_views<runtime_config_blob_name_size> (RUNTIME_CONFIG_BLOB_BASE_NAME, MANGLED_ASSEMBLY_NAME_EXT);

		static constexpr std::string_view MONO_SGEN_SO                        { "libmonosgen-2.0.so" };
		static constexpr std::string_view MONO_SGEN_ARCH_SO                   { "libmonosgen-" __BITNESS__ "-2.0.so" };
		static constexpr std::string_view OVERRIDE_DIRECTORY_NAME             { ".__override__" };

#if __arm__
		static constexpr std::string_view android_abi        { "armeabi_v7a" };
		static constexpr std::string_view android_lib_abi    { "armeabi-v7a" };
		static constexpr std::string_view runtime_identifier { "android-arm" };
#elif __aarch64__
		static constexpr std::string_view android_abi        { "arm64_v8a" };
		static constexpr std::string_view android_lib_abi    { "arm64-v8a" };
		static constexpr std::string_view runtime_identifier { "android-arm64" };
#elif __x86_64__
		static constexpr std::string_view android_abi        { "x86_64" };
		static constexpr std::string_view android_lib_abi    { "x86_64" };
		static constexpr std::string_view runtime_identifier { "android-x64" };
#elif __i386__
		static constexpr std::string_view android_abi        { "x86" };
		static constexpr std::string_view android_lib_abi    { "x86" };
		static constexpr std::string_view runtime_identifier { "android-x86" };
#endif

		static constexpr std::string_view split_config_prefix { "/split_config." };
		static constexpr std::string_view split_config_extension { ".apk" };

		static constexpr size_t split_config_abi_apk_name_size = calc_size (split_config_prefix, android_abi, split_config_extension);
		static constexpr auto split_config_abi_apk_name = concat_string_views<split_config_abi_apk_name_size> (split_config_prefix, android_abi, split_config_extension);

		//
		// Indexes must match these of trhe `appDirs` array in src/java-runtime/mono/android/MonoPackageManager.java
		//
		static constexpr size_t APP_DIRS_FILES_DIR_INDEX = 0;
		static constexpr size_t APP_DIRS_CACHE_DIR_INDEX = 1;
		static constexpr size_t APP_DIRS_DATA_DIR_INDEX = 2;

		// 64-bit unsigned or 64-bit signed with sign
		static constexpr size_t MAX_INTEGER_DIGIT_COUNT_BASE10 = 21;
		static constexpr size_t INTEGER_BASE10_BUFFER_SIZE = MAX_INTEGER_DIGIT_COUNT_BASE10 + 1;

		// Documented in NDK's <android/log.h> comments
		static constexpr size_t MAX_LOGCAT_MESSAGE_LENGTH = 1023;

		static constexpr char LOG_CATEGORY_NAME_NONE[] = "*none*";
		static constexpr char LOG_CATEGORY_NAME_MONODROID[] = "monodroid";
		static constexpr char LOG_CATEGORY_NAME_MONODROID_ASSEMBLY[] ="monodroid-assembly";
		static constexpr char LOG_CATEGORY_NAME_MONODROID_DEBUG[] = "monodroid-debug";
		static constexpr char LOG_CATEGORY_NAME_MONODROID_GC[] = "monodroid-gc";
		static constexpr char LOG_CATEGORY_NAME_MONODROID_GREF[] = "monodroid-gref";
		static constexpr char LOG_CATEGORY_NAME_MONODROID_LREF[] = "monodroid-lref";
		static constexpr char LOG_CATEGORY_NAME_MONODROID_TIMING[] = "monodroid-timing";
		static constexpr char LOG_CATEGORY_NAME_MONODROID_BUNDLE[] = "monodroid-bundle";
		static constexpr char LOG_CATEGORY_NAME_MONODROID_NETWORK[] = "monodroid-network";
		static constexpr char LOG_CATEGORY_NAME_MONODROID_NETLINK[] = "monodroid-netlink";
		static constexpr char LOG_CATEGORY_NAME_ERROR[] = "*error*";

#if defined (__aarch64__)
		static constexpr bool IsARM64 = true;
#else
		static constexpr bool IsARM64 = false;
#endif

#if defined (__arm__)
		static constexpr bool IsARM32 = true;
#else
		static constexpr bool IsARM32 = false;
#endif

#if defined (__i386__)
		static constexpr bool IsX86 = true;
#else
		static constexpr bool IsX86 = false;
#endif

#if defined (__x86_64__)
		static constexpr bool IsX64 = true;
#else
		static constexpr bool IsX64 = false;
#endif
	};
}
#endif // __SHARED_CONSTANTS_HH
