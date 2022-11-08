#ifndef __SHARED_CONSTANTS_HH
#define __SHARED_CONSTANTS_HH

#include "cpp-util.hh"

namespace xamarin::android::internal
{
// _WIN32 is defined with _WIN64 so _WIN64 must be checked first.
#if __SIZEOF_POINTER__ == 8 || defined (_WIN64)
#define __BITNESS__ "64bit"
#elif __SIZEOF_POINTER__ == 4 || defined (_WIN32)
#define __BITNESS__ "32bit"
#else
#error Unknown pointer size for this platform
#endif

	class SharedConstants
	{
	public:
#if defined (NET)
		static constexpr char MONO_ANDROID_RUNTIME_ASSEMBLY_NAME[] = "Mono.Android.Runtime";
#endif
		static constexpr char MONO_ANDROID_ASSEMBLY_NAME[] = "Mono.Android";
		static constexpr char JAVA_INTEROP_ASSEMBLY_NAME[] = "Java.Interop";

		static constexpr char ANDROID_RUNTIME_NS_NAME[] = "Android.Runtime";
		static constexpr char JNIENVINIT_CLASS_NAME[] = "JNIEnvInit";
		static constexpr char JNIENV_CLASS_NAME[] = "JNIEnv";
		static constexpr char ANDROID_ENVIRONMENT_CLASS_NAME[] = "AndroidEnvironment";
		static constexpr char ANDROID_RUNTIME_INTERNAL_CLASS_NAME[] = "AndroidRuntimeInternal";

		static constexpr char DLL_EXTENSION[] = ".dll";

#if defined (NET)
		static constexpr char RUNTIME_CONFIG_BLOB_NAME[] = "rc.bin";
#endif // def NET

#if defined (ANDROID) || defined (__linux__) || defined (__linux)
		static constexpr char MONO_SGEN_SO[]      = "libmonosgen-2.0.so";
		static constexpr char MONO_SGEN_ARCH_SO[] = "libmonosgen-" __BITNESS__ "-2.0.so";
#elif APPLE_OS_X
		static constexpr char MONO_SGEN_SO[]      = "libmonosgen-2.0.dylib";
		static constexpr char MONO_SGEN_ARCH_SO[] = "libmonosgen-" __BITNESS__ "-2.0.dylib";
#elif WINDOWS
		static constexpr char MONO_SGEN_SO[]      = "libmonosgen-2.0.dll";
		static constexpr char MONO_SGEN_ARCH_SO[] = "libmonosgen-" __BITNESS__ "-2.0.dll";
#else
		static constexpr char MONO_SGEN_SO[]      = "monosgen-2.0";
		static constexpr char MONO_SGEN_ARCH_SO[] = "monosgen-" __BITNESS__ "-2.0";
#endif

#if __arm__
		static constexpr char android_abi[] = "armeabi_v7a";
		static constexpr char runtime_identifier[] = "android-arm";
#elif __aarch64__
		static constexpr char android_abi[] = "arm64_v8a";
		static constexpr char runtime_identifier[] = "android-arm64";
#elif __x86_64__
		static constexpr char android_abi[] = "x86_64";
		static constexpr char runtime_identifier[] = "android-x64";
#elif __i386__
		static constexpr char android_abi[] = "x86";
		static constexpr char runtime_identifier[] = "android-x86";
#endif

		static constexpr auto split_config_abi_apk_name = concat_const ("/split_config.", android_abi, ".apk");

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

		// in bytes, including the terminating \0
		static constexpr size_t SENSIBLE_LOG_LEVEL_ENVVAR_SIZE = 64;
	};
}
#endif // __SHARED_CONSTANTS_HH
