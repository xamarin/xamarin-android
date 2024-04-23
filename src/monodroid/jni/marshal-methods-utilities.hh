#if !defined (__MARSHAL_METHODS_UTILITIES_HH)
#define __MARSHAL_METHODS_UTILITIES_HH

#include <cstdint>

#include "xamarin-app.hh"

#if defined (ANDROID) && defined (RELEASE)
namespace xamarin::android::internal
{
	class MarshalMethodsUtilities
	{
		static constexpr char Unknown[] = "Unknown";

	public:
		static const char* get_method_name (uint64_t id) noexcept
		{
			size_t i = 0;
			while (mm_method_names[i].id != 0) {
				if (mm_method_names[i].id == id) {
					return mm_method_names[i].name;
				}
				i++;
			}

			return Unknown;
		}

		static const char* get_class_name (uint32_t class_index) noexcept
		{
			if (class_index >= marshal_methods_number_of_classes) {
				return Unknown;
			}

			return mm_class_names[class_index];
		}

		static uint64_t get_method_id (uint32_t mono_image_index, uint32_t method_token) noexcept
		{
			return (static_cast<uint64_t>(mono_image_index) << 32) | method_token;
		}
	};
}
#endif // def ANDROID && def RELEASE
#endif // ndef __MARSHAL_METHODS_UTILITIES_HH
