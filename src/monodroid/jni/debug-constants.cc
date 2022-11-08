#include "debug.hh"

using namespace xamarin::android;

extern "C" const char *__get_debug_mono_log_property (void)
{
	return static_cast<const char*> (Debug::DEBUG_MONO_LOG_PROPERTY);
}
