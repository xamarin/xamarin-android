#include <unistd.h>
#include <stdarg.h>
#include <mono/utils/mono-publib.h>
#include <mono/utils/mono-dl-fallback.h>

extern "C" {
#include "java_interop_api.h"
}

#include "globals.hh"
#include "monodroid-glue.hh"
#include "monodroid-glue-internal.hh"
#include "timing.hh"
#include "java-interop.h"
#include "cpu-arch.hh"

extern "C" {
	int _monodroid_getifaddrs (struct _monodroid_ifaddrs **ifap);
	void _monodroid_freeifaddrs (struct _monodroid_ifaddrs *ifa);
}

mono_bool _monodroid_get_network_interface_up_state (const char *ifname, mono_bool *is_up);
mono_bool _monodroid_get_network_interface_supports_multicast (const char *ifname, mono_bool *supports_multicast);
int _monodroid_get_dns_servers (void **dns_servers_array);

using namespace xamarin::android;
using namespace xamarin::android::internal;

static unsigned int
monodroid_get_log_categories ()
{
	return log_categories;
}

static int
monodroid_get_system_property (const char *name, char **value)
{
        return androidSystem.monodroid_get_system_property (name, value);
}

static int
monodroid_embedded_assemblies_set_assemblies_prefix (const char *prefix)
{
        embeddedAssemblies.set_assemblies_prefix (prefix);
        return 0;
}

static void
monodroid_log (LogLevel level, LogCategories category, const char *message)
{
	switch (level) {
		case LogLevel::Verbose:
		case LogLevel::Debug:
			log_debug_nocheck (category, message);
			break;

		case LogLevel::Info:
			log_info_nocheck (category, message);
			break;

		case LogLevel::Warn:
		case LogLevel::Silent: // warn is always printed
			log_warn (category, message);
			break;

		case LogLevel::Error:
			log_error (category, message);
			break;

		case LogLevel::Fatal:
			log_fatal (category, message);
			break;

		default:
		case LogLevel::Unknown:
		case LogLevel::Default:
			log_info_nocheck (category, message);
			break;
	}
}

static void
monodroid_free (void *ptr)
{
        free (ptr);
}

static int
_monodroid_max_gref_get ()
{
        return static_cast<int>(androidSystem.get_max_gref_count ());
}

static int
_monodroid_gref_get ()
{
	return osBridge.get_gc_gref_count ();
}


static void
_monodroid_gref_log (const char *message)
{
        osBridge._monodroid_gref_log (message);
}

static int
_monodroid_gref_log_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable)
{
        return osBridge._monodroid_gref_log_new (curHandle, curType, newHandle, newType, threadName, threadId, from, from_writable);
}

static void
_monodroid_gref_log_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
        osBridge._monodroid_gref_log_delete (handle, type, threadName, threadId, from, from_writable);
}

static int
_monodroid_weak_gref_get ()
{
	return osBridge.get_gc_weak_gref_count ();
}

static void
_monodroid_weak_gref_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable)
{
        osBridge._monodroid_weak_gref_new (curHandle, curType, newHandle, newType, threadName, threadId, from, from_writable);
}

static void
_monodroid_weak_gref_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
        osBridge._monodroid_weak_gref_delete (handle, type, threadName, threadId, from, from_writable);
}

static void
_monodroid_lref_log_new (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
        osBridge._monodroid_lref_log_new (lrefc, handle, type, threadName, threadId, from, from_writable);
}

static void
_monodroid_lref_log_delete (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
        osBridge._monodroid_lref_log_delete (lrefc, handle, type, threadName, threadId, from, from_writable);
}

static void
_monodroid_gc_wait_for_bridge_processing ()
{
        mono_gc_wait_for_bridge_processing ();
}

static int
_monodroid_get_android_api_level ()
{
        return monodroidRuntime.get_android_api_level ();
}

static void
monodroid_clear_gdb_wait ()
{
        monodroidRuntime.set_monodroid_gdb_wait (false);
}

static void*
_monodroid_get_identity_hash_code (JNIEnv *env, void *v)
{
        intptr_t rv = env->CallStaticIntMethod (monodroidRuntime.get_java_class_System (), monodroidRuntime.get_java_class_method_System_identityHashCode (), v);
        return (void*) rv;
}

static void*
_monodroid_timezone_get_default_id ()
{
	JNIEnv *env          = osBridge.ensure_jnienv ();
	jmethodID getDefault = env->GetStaticMethodID (monodroidRuntime.get_java_class_TimeZone (), "getDefault", "()Ljava/util/TimeZone;");
	jmethodID getID      = env->GetMethodID (monodroidRuntime.get_java_class_TimeZone (), "getID",      "()Ljava/lang/String;");
	jobject d            = env->CallStaticObjectMethod (monodroidRuntime.get_java_class_TimeZone (), getDefault);
	jstring id           = reinterpret_cast<jstring> (env->CallObjectMethod (d, getID));
	const char *mutf8    = env->GetStringUTFChars (id, nullptr);

	if (mutf8 == nullptr) {
		log_error (LOG_DEFAULT, "Failed to convert Java TimeZone ID to UTF8 (out of memory?)");
		env->DeleteLocalRef (id);
		env->DeleteLocalRef (d);
		return nullptr;
	}

	char *def_id         = strdup (mutf8);

	env->ReleaseStringUTFChars (id, mutf8);
	env->DeleteLocalRef (id);
	env->DeleteLocalRef (d);

	return def_id;
}

static void
_monodroid_counters_dump (const char *format, va_list args)
{
	monodroidRuntime.dump_counters_v (format, args);
}

static managed_timing_sequence*
monodroid_timing_start (const char *message)
{
	if (timing == nullptr)
		return nullptr;

	managed_timing_sequence *ret = timing->get_available_sequence ();
	if (message != nullptr) {
		log_info (LOG_TIMING, message);
	}
	ret->period.mark_start ();

	return ret;
}

static void
monodroid_timing_stop (managed_timing_sequence *sequence, const char *message)
{
	static constexpr const char DEFAULT_MESSAGE[] = "Managed Timing";

	if (sequence == nullptr)
		return;

	sequence->period.mark_end ();
	Timing::info (sequence->period, message == nullptr ? DEFAULT_MESSAGE : message);
	timing->release_sequence (sequence);
}

static char**
monodroid_strsplit (const char *str, const char *delimiter, size_t max_tokens)
{
	return utils.monodroid_strsplit (str, delimiter, max_tokens);
}

static void
monodroid_strfreev (char **str_array)
{
	utils.monodroid_strfreev (str_array);
}

static char*
monodroid_strdup_printf (const char *format, ...)
{
	va_list args;

	va_start (args, format);
	char *ret = utils.monodroid_strdup_vprintf (format, args);
	va_end (args);

	return ret;
}

static char*
monodroid_TypeManager_get_java_class_name (jclass klass)
{
	return monodroidRuntime.get_java_class_name_for_TypeManager (klass);
}

static void
monodroid_store_package_name (const char *name)
{
	utils.monodroid_store_package_name (name);
}

static int
monodroid_get_namespaced_system_property (const char *name, char **value)
{
	return static_cast<int>(androidSystem.monodroid_get_system_property (name, value));
}

static FILE*
monodroid_fopen (const char* filename, const char* mode)
{
	return utils.monodroid_fopen (filename, mode);
}

static int
send_uninterrupted (int fd, void *buf, int len)
{
	if (len < 0)
		len = 0;
	return utils.send_uninterrupted (fd, buf, static_cast<size_t>(len));
}

static int
recv_uninterrupted (int fd, void *buf, int len)
{
	if (len < 0)
		len = 0;
	return static_cast<int>(utils.recv_uninterrupted (fd, buf, static_cast<size_t>(len)));
}

static void
set_world_accessable (const char *path)
{
	utils.set_world_accessable (path);
}

static void
create_public_directory (const char *dir)
{
	utils.create_public_directory (dir);
}

static char*
path_combine (const char *path1, const char *path2)
{
	return utils.path_combine (path1, path2);
}

static void*
monodroid_dylib_mono_new ([[maybe_unused]] const char *libmono_path)
{
	return nullptr;
}

static void
monodroid_dylib_mono_free ([[maybe_unused]] void *mono_imports)
{
	// no-op
}

/*
  this function is used from JavaInterop and should be treated as public API
  https://github.com/xamarin/java.interop/blob/master/src/java-interop/java-interop-gc-bridge-mono.c#L266

  it should also accept libmono_path = nullptr parameter
*/
static int
monodroid_dylib_mono_init (void *mono_imports, [[maybe_unused]] const char *libmono_path)
{
	if (mono_imports == nullptr)
		return FALSE;
	return TRUE;
}

static void*
monodroid_get_dylib (void)
{
	return nullptr;
}

#define PINVOKE_SYMBOL(_sym_) { #_sym_, reinterpret_cast<void*>(&_sym_) }

MonodroidRuntime::pinvoke_api_map MonodroidRuntime::xa_pinvoke_map = {
	PINVOKE_SYMBOL (create_public_directory),
	PINVOKE_SYMBOL (java_interop_free),
	PINVOKE_SYMBOL (java_interop_jnienv_alloc_object),
	PINVOKE_SYMBOL (java_interop_jnienv_call_boolean_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_boolean_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_byte_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_byte_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_char_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_char_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_double_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_double_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_float_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_float_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_int_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_int_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_long_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_long_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_boolean_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_boolean_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_byte_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_byte_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_char_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_char_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_double_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_double_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_float_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_float_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_int_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_int_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_long_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_long_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_object_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_object_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_short_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_short_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_void_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_nonvirtual_void_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_object_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_object_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_short_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_short_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_boolean_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_boolean_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_byte_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_byte_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_char_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_char_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_double_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_double_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_float_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_float_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_int_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_int_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_long_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_long_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_object_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_object_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_short_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_short_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_void_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_static_void_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_call_void_method),
	PINVOKE_SYMBOL (java_interop_jnienv_call_void_method_a),
	PINVOKE_SYMBOL (java_interop_jnienv_define_class),
	PINVOKE_SYMBOL (java_interop_jnienv_delete_global_ref),
	PINVOKE_SYMBOL (java_interop_jnienv_delete_local_ref),
	PINVOKE_SYMBOL (java_interop_jnienv_delete_weak_global_ref),
	PINVOKE_SYMBOL (java_interop_jnienv_ensure_local_capacity),
	PINVOKE_SYMBOL (java_interop_jnienv_exception_check),
	PINVOKE_SYMBOL (java_interop_jnienv_exception_clear),
	PINVOKE_SYMBOL (java_interop_jnienv_exception_describe),
	PINVOKE_SYMBOL (java_interop_jnienv_exception_occurred),
	PINVOKE_SYMBOL (java_interop_jnienv_fatal_error),
	PINVOKE_SYMBOL (java_interop_jnienv_find_class),
	PINVOKE_SYMBOL (java_interop_jnienv_get_array_length),
	PINVOKE_SYMBOL (java_interop_jnienv_get_boolean_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_get_boolean_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_get_boolean_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_byte_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_get_byte_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_get_byte_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_char_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_get_char_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_get_char_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_direct_buffer_address),
	PINVOKE_SYMBOL (java_interop_jnienv_get_direct_buffer_capacity),
	PINVOKE_SYMBOL (java_interop_jnienv_get_double_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_get_double_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_get_double_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_field_id),
	PINVOKE_SYMBOL (java_interop_jnienv_get_float_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_get_float_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_get_float_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_int_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_get_int_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_get_int_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_java_vm),
	PINVOKE_SYMBOL (java_interop_jnienv_get_long_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_get_long_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_get_long_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_method_id),
	PINVOKE_SYMBOL (java_interop_jnienv_get_object_array_element),
	PINVOKE_SYMBOL (java_interop_jnienv_get_object_class),
	PINVOKE_SYMBOL (java_interop_jnienv_get_object_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_object_ref_type),
	PINVOKE_SYMBOL (java_interop_jnienv_get_primitive_array_critical),
	PINVOKE_SYMBOL (java_interop_jnienv_get_short_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_get_short_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_get_short_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_static_boolean_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_static_byte_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_static_char_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_static_double_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_static_field_id),
	PINVOKE_SYMBOL (java_interop_jnienv_get_static_float_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_static_int_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_static_long_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_static_method_id),
	PINVOKE_SYMBOL (java_interop_jnienv_get_static_object_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_static_short_field),
	PINVOKE_SYMBOL (java_interop_jnienv_get_string_chars),
	PINVOKE_SYMBOL (java_interop_jnienv_get_string_length),
	PINVOKE_SYMBOL (java_interop_jnienv_get_superclass),
	PINVOKE_SYMBOL (java_interop_jnienv_get_version),
	PINVOKE_SYMBOL (java_interop_jnienv_is_assignable_from),
	PINVOKE_SYMBOL (java_interop_jnienv_is_instance_of),
	PINVOKE_SYMBOL (java_interop_jnienv_is_same_object),
	PINVOKE_SYMBOL (java_interop_jnienv_monitor_enter),
	PINVOKE_SYMBOL (java_interop_jnienv_monitor_exit),
	PINVOKE_SYMBOL (java_interop_jnienv_new_boolean_array),
	PINVOKE_SYMBOL (java_interop_jnienv_new_byte_array),
	PINVOKE_SYMBOL (java_interop_jnienv_new_char_array),
	PINVOKE_SYMBOL (java_interop_jnienv_new_direct_byte_buffer),
	PINVOKE_SYMBOL (java_interop_jnienv_new_double_array),
	PINVOKE_SYMBOL (java_interop_jnienv_new_float_array),
	PINVOKE_SYMBOL (java_interop_jnienv_new_global_ref),
	PINVOKE_SYMBOL (java_interop_jnienv_new_int_array),
	PINVOKE_SYMBOL (java_interop_jnienv_new_local_ref),
	PINVOKE_SYMBOL (java_interop_jnienv_new_long_array),
	PINVOKE_SYMBOL (java_interop_jnienv_new_object),
	PINVOKE_SYMBOL (java_interop_jnienv_new_object_a),
	PINVOKE_SYMBOL (java_interop_jnienv_new_object_array),
	PINVOKE_SYMBOL (java_interop_jnienv_new_short_array),
	PINVOKE_SYMBOL (java_interop_jnienv_new_string),
	PINVOKE_SYMBOL (java_interop_jnienv_new_weak_global_ref),
	PINVOKE_SYMBOL (java_interop_jnienv_pop_local_frame),
	PINVOKE_SYMBOL (java_interop_jnienv_push_local_frame),
	PINVOKE_SYMBOL (java_interop_jnienv_register_natives),
	PINVOKE_SYMBOL (java_interop_jnienv_release_boolean_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_release_byte_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_release_char_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_release_double_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_release_float_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_release_int_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_release_long_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_release_primitive_array_critical),
	PINVOKE_SYMBOL (java_interop_jnienv_release_short_array_elements),
	PINVOKE_SYMBOL (java_interop_jnienv_release_string_chars),
	PINVOKE_SYMBOL (java_interop_jnienv_set_boolean_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_set_boolean_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_byte_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_set_byte_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_char_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_set_char_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_double_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_set_double_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_float_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_set_float_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_int_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_set_int_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_long_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_set_long_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_object_array_element),
	PINVOKE_SYMBOL (java_interop_jnienv_set_object_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_short_array_region),
	PINVOKE_SYMBOL (java_interop_jnienv_set_short_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_static_boolean_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_static_byte_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_static_char_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_static_double_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_static_float_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_static_int_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_static_long_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_static_object_field),
	PINVOKE_SYMBOL (java_interop_jnienv_set_static_short_field),
	PINVOKE_SYMBOL (java_interop_jnienv_throw),
	PINVOKE_SYMBOL (java_interop_jnienv_throw_new),
	PINVOKE_SYMBOL (java_interop_jnienv_to_reflected_field),
	PINVOKE_SYMBOL (java_interop_jnienv_to_reflected_method),
	PINVOKE_SYMBOL (java_interop_jnienv_unregister_natives),
	PINVOKE_SYMBOL (java_interop_strdup),
	PINVOKE_SYMBOL (monodroid_clear_gdb_wait),
	PINVOKE_SYMBOL (_monodroid_counters_dump),
	PINVOKE_SYMBOL (_monodroid_detect_cpu_and_architecture),
	PINVOKE_SYMBOL (monodroid_dylib_mono_free),
	PINVOKE_SYMBOL (monodroid_dylib_mono_init),
	PINVOKE_SYMBOL (monodroid_dylib_mono_new),
	PINVOKE_SYMBOL (monodroid_embedded_assemblies_set_assemblies_prefix),
	PINVOKE_SYMBOL (monodroid_fopen),
	PINVOKE_SYMBOL (monodroid_free),
	PINVOKE_SYMBOL (_monodroid_freeifaddrs),
	PINVOKE_SYMBOL (_monodroid_gc_wait_for_bridge_processing),
	PINVOKE_SYMBOL (_monodroid_get_android_api_level),
	PINVOKE_SYMBOL (_monodroid_get_dns_servers),
	PINVOKE_SYMBOL (monodroid_get_dylib),
	PINVOKE_SYMBOL (_monodroid_get_identity_hash_code),
	PINVOKE_SYMBOL (_monodroid_getifaddrs),
	PINVOKE_SYMBOL (monodroid_get_log_categories),
	PINVOKE_SYMBOL (monodroid_get_namespaced_system_property),
	PINVOKE_SYMBOL (_monodroid_get_network_interface_supports_multicast),
	PINVOKE_SYMBOL (_monodroid_get_network_interface_up_state),
	PINVOKE_SYMBOL (monodroid_get_system_property),
	PINVOKE_SYMBOL (_monodroid_gref_get),
	PINVOKE_SYMBOL (_monodroid_gref_log),
	PINVOKE_SYMBOL (_monodroid_gref_log_delete),
	PINVOKE_SYMBOL (_monodroid_gref_log_new),
	PINVOKE_SYMBOL (monodroid_log),
	PINVOKE_SYMBOL (_monodroid_lref_log_delete),
	PINVOKE_SYMBOL (_monodroid_lref_log_new),
	PINVOKE_SYMBOL (_monodroid_max_gref_get),
	PINVOKE_SYMBOL (monodroid_store_package_name),
	PINVOKE_SYMBOL (monodroid_strdup_printf),
	PINVOKE_SYMBOL (monodroid_strfreev),
	PINVOKE_SYMBOL (monodroid_strsplit),
	PINVOKE_SYMBOL (_monodroid_timezone_get_default_id),
	PINVOKE_SYMBOL (monodroid_timing_start),
	PINVOKE_SYMBOL (monodroid_timing_stop),
	PINVOKE_SYMBOL (monodroid_TypeManager_get_java_class_name),
	PINVOKE_SYMBOL (_monodroid_weak_gref_delete),
	PINVOKE_SYMBOL (_monodroid_weak_gref_get),
	PINVOKE_SYMBOL (_monodroid_weak_gref_new),
	PINVOKE_SYMBOL (path_combine),
	PINVOKE_SYMBOL (recv_uninterrupted),
	PINVOKE_SYMBOL (send_uninterrupted),
	PINVOKE_SYMBOL (set_world_accessable),
};

MonodroidRuntime::pinvoke_library_map MonodroidRuntime::other_pinvoke_map (MonodroidRuntime::LIBRARY_MAP_INITIAL_BUCKET_COUNT);

// `pinvoke_map_write_lock` MUST be held when calling this method
force_inline void*
MonodroidRuntime::load_library_entry (std::string const& library_name, std::string const& entrypoint_name, pinvoke_api_map_ptr api_map)
{
	// Make sure some other thread hasn't just added the entry
	auto iter = api_map->find (entrypoint_name);
	if (iter != api_map->end () && iter->second != nullptr) {
		return iter->second;
	}

	void *lib_handle = monodroid_dlopen (library_name.c_str (), MONO_DL_LOCAL, nullptr, nullptr);
	if (lib_handle == nullptr) {
		log_warn (LOG_ASSEMBLY, "Shared library '%s' not loaded, p/invoke '%s' may fail", library_name.c_str (), entrypoint_name.c_str ());
		return nullptr;
	}

	void *entry_handle = monodroid_dlsym (lib_handle, entrypoint_name.c_str (), nullptr, nullptr);
	if (entry_handle == nullptr) {
		log_warn (LOG_ASSEMBLY, "Symbol '%s' not found in shared library '%s', p/invoke may fail", entrypoint_name.c_str (), library_name.c_str ());
		return nullptr;
	}

	log_warn (LOG_ASSEMBLY, "Caching p/invoke entry %s @ %s", library_name.c_str (), entrypoint_name.c_str ());
	(*api_map)[entrypoint_name] = entry_handle;
	return entry_handle;
}

force_inline void*
MonodroidRuntime::fetch_or_create_pinvoke_map_entry (std::string const& library_name, std::string const& entrypoint_name, pinvoke_api_map_ptr api_map, bool need_lock)
{
	auto iter = api_map->find (entrypoint_name);
	if (iter != api_map->end () && iter->second != nullptr) {
		return iter->second;
	}

	if (!need_lock) {
		return load_library_entry (library_name, entrypoint_name, api_map);
	}

	std::lock_guard<std::mutex> lock (pinvoke_map_write_lock);
	return load_library_entry (library_name, entrypoint_name, api_map);
}

void*
MonodroidRuntime::monodroid_pinvoke_override (const char *library_name, const char *entrypoint_name)
{
	log_warn (LOG_DEFAULT, "MonodroidRuntime::monodroid_pinvoke_override (\"%s\", \"%s\")", library_name, entrypoint_name);

	timing_period total_time;
	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		total_time.mark_start ();
	}

	if (library_name == nullptr || *library_name == '\0' || entrypoint_name == nullptr || *entrypoint_name == '\0') {
		return nullptr;
	}

	bool is_internal;
	switch (library_name[0]) {
		case 'j':
			is_internal = strcmp ("java-interop", library_name) == 0;
			break;

		case 'x':
			is_internal = strcmp ("xa-internal-api", library_name) == 0;
			break;

		default:
			is_internal = false;
			break;
	}

	if (!is_internal) {
		std::string lib_name (library_name);
		std::string func_name (entrypoint_name);

		auto iter = other_pinvoke_map.find (lib_name);
		void *handle = nullptr;
		if (iter == other_pinvoke_map.end ()) {
			std::lock_guard<std::mutex> lock (pinvoke_map_write_lock);

			pinvoke_api_map_ptr lib_map;
			// Make sure some other thread hasn't just added the map
			iter = other_pinvoke_map.find (lib_name);
			if (iter == other_pinvoke_map.end () || iter->second == nullptr) {
				lib_map = new pinvoke_api_map (1);
				other_pinvoke_map[lib_name] = lib_map;
			} else {
				lib_map = iter->second;
			}

			handle = fetch_or_create_pinvoke_map_entry (lib_name, func_name, lib_map, /* need_lock */ false);
		} else {
			if (XA_UNLIKELY (iter->second == nullptr)) {
				log_warn (LOG_ASSEMBLY, "Internal error: null entry in p/invoke map for key '%s'", library_name);
				return nullptr; // fall back to `monodroid_dlopen`
			}

			handle = fetch_or_create_pinvoke_map_entry (lib_name, func_name, iter->second, /* need_lock */ true);
		}

		if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
			total_time.mark_end ();

			TIMING_LOG_INFO (total_time, "p/invoke override for '%s' (foreign)", entrypoint_name);
		}

		return handle;
	}

	timing_period lookup_time;
	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		lookup_time.mark_start ();
	}
	auto iter = xa_pinvoke_map.find (entrypoint_name);
	if (iter == xa_pinvoke_map.end ()) {
		log_fatal (LOG_ASSEMBLY, "Internal p/invoke symbol '%s @ %s' not found in compile-time map.", library_name, entrypoint_name);
		log_fatal (LOG_ASSEMBLY, "compile-time map contents:");
		for (iter = xa_pinvoke_map.begin (); iter != xa_pinvoke_map.end (); ++iter) {
			log_fatal (LOG_ASSEMBLY, "\t'%s'=%p", iter->first.c_str (), iter->second);
		}
		abort ();
		return nullptr;
	}

	if (XA_UNLIKELY (utils.should_log (LOG_TIMING))) {
		lookup_time.mark_end ();
		total_time.mark_end ();

		TIMING_LOG_INFO (lookup_time, "p/invoke cache lookup for '%s' (internal)", entrypoint_name);
		TIMING_LOG_INFO (total_time, "p/invoke override for '%s' (internal)", entrypoint_name);
	}
	log_warn (LOG_DEFAULT, "Found %s@%s in internal p/invoke map (%p)", library_name, entrypoint_name, iter->second);
	return iter->second;
}
