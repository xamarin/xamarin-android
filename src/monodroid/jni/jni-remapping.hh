#if !defined (__JNI_REMAPPING_HH)
#define __JNI_REMAPPING_HH

#include <mono/utils/mono-publib.h>

#include "xamarin-app.hh"

namespace xamarin::android::internal
{
	class JniRemapping final
	{
	public:
		static const char* lookup_replacement_type (const char *jniSimpleReference) noexcept;
		static const JniRemappingReplacementMethod* lookup_replacement_method_info (const char *jniSourceType, const char *jniMethodName, const char *jniMethodSignature) noexcept;

	private:
		static bool equal (JniRemappingString const& left, const char *right, size_t right_len) noexcept;
	};
}
#endif
