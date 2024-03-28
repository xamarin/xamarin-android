#include <array>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <jni.h>
#include <limits>
#include <memory>
#include <string>
#include <type_traits>

#include <android/log.h>

#include "marshal-methods-tracing.hh"
#include "marshal-methods-utilities.hh"
#include "cpp-util.hh"
#include "helpers.hh"
#include "native-tracing.hh"

using namespace xamarin::android;
using namespace xamarin::android::internal;

constexpr int PRIORITY = ANDROID_LOG_INFO;
constexpr char LEAD[] = "MM: ";
constexpr char BOOL_TRUE[] = "true";
constexpr char BOOL_FALSE[] = "false";
constexpr char MISSING_ENV[] = "<missing env>";
constexpr char NULL_PARAM[] = "<NULL>";
constexpr char INTERNAL_ERROR[] = "<ERR>";
constexpr char UNKNOWN[] = "<?>";

static jclass java_lang_Class;
static jmethodID java_lang_Class_getName;

void _mm_trace_init (JNIEnv *env) noexcept
{
	if (env == nullptr || java_lang_Class != nullptr) {
		return;
	}

	java_lang_Class = to_gref (env, env->FindClass ("java/lang/Class"));
	java_lang_Class_getName = env->GetMethodID (java_lang_Class, "getName", "()Ljava/lang/String");

	if (env->ExceptionOccurred ()) {
		env->ExceptionDescribe ();
		env->ExceptionClear ();
		xamarin::android::Helpers::abort_application ();
	}

	bool all_found = assert_valid_jni_pointer (java_lang_Class, "class", "java.lang.Class");
	all_found &= assert_valid_jni_pointer (java_lang_Class_getName, "method", "java.lang.Class.getName ()");

	if (!all_found) {
		xamarin::android::Helpers::abort_application ();
	}
}

static void _mm_trace_func_leave_enter (JNIEnv *env, int32_t tracing_mode, uint32_t mono_image_index, uint32_t class_index, uint32_t method_token,
                                        const char* which, const char* native_method_name, bool need_trace, const char* method_extra) noexcept
{
	uint64_t method_id = MarshalMethodsUtilities::get_method_id (mono_image_index, method_token);
	const char *managed_method_name = MarshalMethodsUtilities::get_method_name (method_id);
	const char *class_name = MarshalMethodsUtilities::get_class_name (class_index);

	if (need_trace && tracing_mode == TracingModeFull) {
		std::string trace { LEAD };
		trace.append (which);
		trace.append (": ");
		trace.append (native_method_name);
		if (method_extra != nullptr) {
			trace.append (" ");
			trace.append (method_extra);
		}
		trace.append (" {");
		trace.append (managed_method_name);
		trace.append ("} in class ");
		trace.append (class_name);

		trace.append ("\n  Native stack trace:\n");
		c_unique_ptr<const char> native_trace { xa_get_native_backtrace () };
		trace.append (native_trace.get ());
		trace.append ("\n");

		trace.append ("\n  Java stack trace:\n");
		c_unique_ptr<const char> java_trace { xa_get_java_backtrace (env) };
		trace.append (java_trace.get ());
		trace.append ("\n");

		trace.append ("\n  Installed signal handlers:\n");
		c_unique_ptr<const char> signal_handlers { xa_get_interesting_signal_handlers () };
		trace.append (signal_handlers.get ());

		__android_log_write (PRIORITY, SharedConstants::LOG_CATEGORY_NAME_MONODROID_ASSEMBLY, trace.c_str ());
	} else {
		__android_log_print (
			PRIORITY,
			SharedConstants::LOG_CATEGORY_NAME_MONODROID_ASSEMBLY,
			"%s%s: %s%s%s {%s} in class %s",
			LEAD,
			which,
			native_method_name,
			method_extra == nullptr ? "" : " ",
			method_extra == nullptr ? "" : method_extra,
			managed_method_name,
			class_name
		);
	}
}

void _mm_trace_func_enter (JNIEnv *env, int32_t tracing_mode, uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, const char* native_method_name, const char* method_params) noexcept
{
	constexpr char ENTER[] = "ENTER";
	_mm_trace_func_leave_enter (env, tracing_mode, mono_image_index, class_index, method_token, ENTER, native_method_name, true /* need_trace */, method_params);
}

void _mm_trace_func_leave (JNIEnv *env, int32_t tracing_mode, uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, const char* native_method_name, const char* method_return_value) noexcept
{
	constexpr char LEAVE[] = "LEAVE";
	_mm_trace_func_leave_enter (env, tracing_mode, mono_image_index, class_index, method_token, LEAVE, native_method_name, false /* need_trace */, method_return_value);
}

const char* _mm_trace_get_boolean_string (uint8_t v) noexcept
{
	return v ? BOOL_TRUE : BOOL_FALSE;
}

char* _mm_trace_get_c_string (JNIEnv *env, jstring v) noexcept
{
	if (env == nullptr) {
		return strdup (MISSING_ENV);
	}

	if (v == nullptr) {
		return strdup (NULL_PARAM);
	}

	const char *s = env->GetStringUTFChars (v, nullptr);
	char *ret = strdup (s);
	env->ReleaseStringUTFChars (v, s);

	return ret;
}

[[gnu::always_inline]]
static char* get_class_name (JNIEnv *env, jclass klass) noexcept
{
	if (java_lang_Class == nullptr || java_lang_Class_getName == nullptr) {
		return strdup (INTERNAL_ERROR);
	}

	auto className = static_cast<jstring>(env->CallObjectMethod (klass, java_lang_Class_getName));
	if (className == nullptr) {
		return strdup (UNKNOWN);
	}

	return _mm_trace_get_c_string (env, className);
}

char* _mm_trace_get_class_name (JNIEnv *env, jclass v) noexcept
{
	if (env == nullptr) {
		return strdup (MISSING_ENV);
	}

	if (v == nullptr) {
		return strdup (NULL_PARAM);
	}

	return get_class_name (env, v);
}

char* _mm_trace_get_object_class_name (JNIEnv *env, jobject v) noexcept
{
	if (env == nullptr) {
		return strdup (MISSING_ENV);
	}

	if (v == nullptr) {
		return strdup (NULL_PARAM);
	}

	return get_class_name (env, env->GetObjectClass (v));
}
