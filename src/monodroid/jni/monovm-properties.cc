#if defined (NET)
#include "monovm-properties.hh"

using namespace xamarin::android::internal;

MonoVMProperties::property_array MonoVMProperties::_property_keys {
	RUNTIME_IDENTIFIER_KEY,
	APP_CONTEXT_BASE_DIRECTORY_KEY,
	LOCAL_DATE_TIME_OFFSET_KEY,
};

MonoVMProperties::property_array MonoVMProperties::_property_values {
	SharedConstants::runtime_identifier,
	nullptr,
	nullptr,
};
#endif // def NET
