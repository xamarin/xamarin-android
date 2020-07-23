#ifdef WINDOWS
#include <windef.h>
#include <winbase.h>
#include <shlobj.h>
#include <objbase.h>
#include <knownfolders.h>
#include <shlwapi.h>
#endif

#include "globals.hh"
#include "timing.hh"

using namespace xamarin::android;
using namespace xamarin::android::internal;

/* Invoked by System.Core.dll!System.IO.MemoryMappedFiles.MemoryMapImpl.getpagesize */
MONO_API int
monodroid_getpagesize (void)
{
#ifndef WINDOWS
	return getpagesize ();
#else
	SYSTEM_INFO info;
	GetSystemInfo (&info);
	return info.dwPageSize;
#endif
}

/* Invoked by:
   - System.Core.dll!System.TimeZoneInfo.Android.GetDefaultTimeZoneName
   - Mono.Android.dll!Android.Runtime.AndroidEnvironment.GetDefaultTimeZone
*/

MONO_API void
monodroid_free (void *ptr)
{
	free (ptr);
}

MONO_API int
monodroid_get_system_property (const char *name, char **value)
{
	return androidSystem.monodroid_get_system_property (name, value);
}

MONO_API int
_monodroid_max_gref_get (void)
{
	return static_cast<int>(androidSystem.get_max_gref_count ());
}

MONO_API int
_monodroid_gref_get (void)
{
	return osBridge.get_gc_gref_count ();
}

MONO_API void
_monodroid_gref_log (const char *message)
{
	osBridge._monodroid_gref_log (message);
}

MONO_API int
_monodroid_gref_log_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable)
{
	return osBridge._monodroid_gref_log_new (curHandle, curType, newHandle, newType, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_gref_log_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	osBridge._monodroid_gref_log_delete (handle, type, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_weak_gref_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable)
{
	osBridge._monodroid_weak_gref_new (curHandle, curType, newHandle, newType, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_weak_gref_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	osBridge._monodroid_weak_gref_delete (handle, type, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_lref_log_new (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	osBridge._monodroid_lref_log_new (lrefc, handle, type, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_lref_log_delete (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	osBridge._monodroid_lref_log_delete (lrefc, handle, type, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_gc_wait_for_bridge_processing (void)
{
	mono_gc_wait_for_bridge_processing ();
}

/* !DO NOT REMOVE! Used by Mono BCL */
MONO_API int
_monodroid_get_android_api_level (void)
{
	return monodroidRuntime.get_android_api_level ();
}

/* Can be called by a native debugger to break the wait on startup */
MONO_API void
monodroid_clear_gdb_wait (void)
{
	monodroidRuntime.set_monodroid_gdb_wait (false);
}

MONO_API void*
_monodroid_get_identity_hash_code (JNIEnv *env, void *v)
{
	log_warn (LOG_DEFAULT, "[3] %s", __PRETTY_FUNCTION__);
	log_warn (LOG_DEFAULT, "[3] env == %p; v == %p", env, v);
	intptr_t rv = env->CallStaticIntMethod (monodroidRuntime.get_java_class_System (), monodroidRuntime.get_java_class_method_System_identityHashCode (), v);
	return (void*) rv;
}

MONO_API void*
_monodroid_timezone_get_default_id (void)
{
	JNIEnv *env          = osBridge.ensure_jnienv ();
	jmethodID getDefault = env->GetStaticMethodID (monodroidRuntime.get_java_class_TimeZone (), "getDefault", "()Ljava/util/TimeZone;");
	jmethodID getID      = env->GetMethodID (monodroidRuntime.get_java_class_TimeZone (), "getID",      "()Ljava/lang/String;");
	jobject d            = env->CallStaticObjectMethod (monodroidRuntime.get_java_class_TimeZone (), getDefault);
	jstring id           = reinterpret_cast<jstring> (env->CallObjectMethod (d, getID));
	const char *mutf8    = env->GetStringUTFChars (id, nullptr);
	char *def_id         = strdup (mutf8);

	env->ReleaseStringUTFChars (id, mutf8);
	env->DeleteLocalRef (id);
	env->DeleteLocalRef (d);

	return def_id;
}

MONO_API void
_monodroid_counters_dump (const char *format, ...)
{
	va_list args;
	va_start (args, format);
	monodroidRuntime.dump_counters_v (format, args);
	va_end (args);
}

/* !DO NOT REMOVE! Used by libgdiplus.so */
MONO_API int
_monodroid_get_display_dpi (float *x_dpi, float *y_dpi)
{
	return monodroidRuntime.get_display_dpi (x_dpi, y_dpi);
}

MONO_API int monodroid_embedded_assemblies_set_assemblies_prefix (const char *prefix)
{
	embeddedAssemblies.set_assemblies_prefix (prefix);
	return 0;
}

MONO_API managed_timing_sequence*
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

MONO_API void
monodroid_timing_stop (managed_timing_sequence *sequence, const char *message)
{
	static constexpr const char DEFAULT_MESSAGE[] = "Managed Timing";

	if (sequence == nullptr)
		return;

	sequence->period.mark_end ();
	Timing::info (sequence->period, message == nullptr ? DEFAULT_MESSAGE : message);
	timing->release_sequence (sequence);
}

MONO_API void
monodroid_strfreev (char **str_array)
{
	utils.monodroid_strfreev (str_array);
}

MONO_API char**
monodroid_strsplit (const char *str, const char *delimiter, size_t max_tokens)
{
	return utils.monodroid_strsplit (str, delimiter, max_tokens);
}

MONO_API char*
monodroid_strdup_printf (const char *format, ...)
{
	va_list args;

	va_start (args, format);
	char *ret = utils.monodroid_strdup_vprintf (format, args);
	va_end (args);

	return ret;
}

MONO_API char*
monodroid_TypeManager_get_java_class_name (jclass klass)
{
	return monodroidRuntime.get_java_class_name_for_TypeManager (klass);
}

MONO_API void
monodroid_store_package_name (const char *name)
{
	utils.monodroid_store_package_name (name);
}

MONO_API int
monodroid_get_namespaced_system_property (const char *name, char **value)
{
	return static_cast<int>(androidSystem.monodroid_get_system_property (name, value));
}

MONO_API FILE*
monodroid_fopen (const char* filename, const char* mode)
{
	return utils.monodroid_fopen (filename, mode);
}

MONO_API int
send_uninterrupted (int fd, void *buf, int len)
{
	if (len < 0)
		len = 0;
	return utils.send_uninterrupted (fd, buf, static_cast<size_t>(len));
}

MONO_API int
recv_uninterrupted (int fd, void *buf, int len)
{
	if (len < 0)
		len = 0;
	return static_cast<int>(utils.recv_uninterrupted (fd, buf, static_cast<size_t>(len)));
}

MONO_API void
set_world_accessable (const char *path)
{
	utils.set_world_accessable (path);
}

MONO_API void
create_public_directory (const char *dir)
{
	utils.create_public_directory (dir);
}

MONO_API char*
path_combine (const char *path1, const char *path2)
{
	return utils.path_combine (path1, path2);
}

MONO_API void*
monodroid_dylib_mono_new ([[maybe_unused]] const char *libmono_path)
{
	return nullptr;
}

MONO_API void
monodroid_dylib_mono_free ([[maybe_unused]] void *mono_imports)
{
	// no-op
}

/*
  this function is used from JavaInterop and should be treated as public API
  https://github.com/xamarin/java.interop/blob/master/src/java-interop/java-interop-gc-bridge-mono.c#L266

  it should also accept libmono_path = nullptr parameter
*/
MONO_API int
monodroid_dylib_mono_init (void *mono_imports, [[maybe_unused]] const char *libmono_path)
{
	if (mono_imports == nullptr)
		return FALSE;
	return TRUE;
}

MONO_API void*
monodroid_get_dylib (void)
{
	return nullptr;
}
