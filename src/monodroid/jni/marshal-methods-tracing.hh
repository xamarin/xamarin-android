#if !defined (__MARSHAL_METHODS_TRACING_HH)
#define __MARSHAL_METHODS_TRACING_HH

#include "monodroid-glue-internal.hh"

extern "C" void _mm_trace (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, const char *message);
extern "C" void _mm_trace_func_enter (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token);
extern "C" void _mm_trace_func_leave (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token);

#endif // ndef __MARSHAL_METHODS_TRACING_HH