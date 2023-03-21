#if !defined (__BUILD_INFO_HH)
#define __BUILD_INFO_HH

#include <android/ndk-version.h>

namespace xamarin::android::internal
{
#define STRINGIFY(_val_) #_val_
#define API_STRING(_api_) STRINGIFY(_api_)
#define VERSION_STRING(_major_, _minor_, _build_) STRINGIFY(_major_) "." STRINGIFY(_minor_) "." STRINGIFY(_build_)

	class BuildInfo final
	{
	public:
		static constexpr char xa_version[] = XA_VERSION;
		static constexpr char date[] = __DATE__;

		static constexpr char kind[] =
#if defined (DEBUG)
			"Debug";
#else // ndef DEBUG
			"Release";
#endif

		static constexpr char architecture[] =
#if defined (__aarch64__)
			"ARM64";
#elif defined (__arm__)
			"ARM32";
#elif defined (__amd64__) || defined (__x86_64__)
			"X86_64";
#elif defined (__i386__)
			"X86";
#endif

		static constexpr char ndk_version[] =
#if defined (ANDROID)
			VERSION_STRING (__NDK_MAJOR__, __NDK_MINOR__, __NDK_BUILD__);
#else // def ANDROID
			"";
#endif // // ndef ANDROID

		static constexpr char ndk_api_level[] =
#if defined (__ANDROID_API__)
			API_STRING(__ANDROID_API__);
#else // def __ANDROID_API__
			"";
#endif // ndef __ANDROID_API__
	};
}
#endif // ndef __BUILD_INFO_HH
