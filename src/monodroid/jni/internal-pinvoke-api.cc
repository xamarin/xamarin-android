#if !defined (NET)
#include <unistd.h>
#include <stdarg.h>
#include <mono/utils/mono-publib.h>

#include "xa-internal-api.hh"

constexpr int TRUE = 1;
constexpr int FALSE = 0;

using namespace xamarin::android;

static MonoAndroidInternalCalls *internal_calls = nullptr;

MONO_API bool
_monodroid_init_internal_api (MonoAndroidInternalCalls *api)
{
	if (api == nullptr)
		return false;

	delete internal_calls;
	internal_calls = api;
	return true;
}

MONO_API void
_monodroid_shutdown_internal_api ()
{
	if (internal_calls == nullptr)
		return;

	delete internal_calls;
	internal_calls = nullptr;
}

MONO_API int
_monodroid_getifaddrs (struct _monodroid_ifaddrs **ifap)
{
	return internal_calls->monodroid_getifaddrs (ifap);
}

MONO_API void
_monodroid_freeifaddrs (struct _monodroid_ifaddrs *ifa)
{
	internal_calls->monodroid_freeifaddrs (ifa);
}

MONO_API void
_monodroid_detect_cpu_and_architecture (unsigned short *built_for_cpu, unsigned short *running_on_cpu, unsigned char *is64bit)
{
	internal_calls->monodroid_detect_cpu_and_architecture (built_for_cpu, running_on_cpu, is64bit);
}

// !DO NOT REMOVE! Used by Mono BCL (System.Net.NetworkInformation.NetworkInterface)
// https://github.com/mono/mono/blob/e59c1cd70f4a7171a0ff5e1f9f4937985d6a4d8d/mcs/class/System/System.Net.NetworkInformation/LinuxNetworkInterface.cs#L250-L261
MONO_API mono_bool
_monodroid_get_network_interface_up_state (const char *ifname, mono_bool *is_up)
{
	return internal_calls->monodroid_get_network_interface_up_state (ifname, is_up);
}

/* !DO NOT REMOVE! Used by Mono BCL (System.Net.NetworkInformation.NetworkInterface) */
MONO_API mono_bool
_monodroid_get_network_interface_supports_multicast (const char *ifname, mono_bool *supports_multicast)
{
	return internal_calls->monodroid_get_network_interface_supports_multicast (ifname, supports_multicast);
}

/* !DO NOT REMOVE! Used by Mono BCL (System.Net.NetworkInformation.UnixIPInterfaceProperties) */
MONO_API int
_monodroid_get_dns_servers (void **dns_servers_array)
{
	return internal_calls->monodroid_get_dns_servers (dns_servers_array);
}

//
// DO NOT REMOVE: used by Android.Runtime.Logger
//
MONO_API unsigned int
monodroid_get_log_categories ()
{
	return internal_calls->monodroid_get_log_categories ();
}

//
// DO NOT REMOVE: used by Android.Runtime.JNIEnv
//
MONO_API void
monodroid_log (LogLevel level, LogCategories category, const char *message)
{
	internal_calls->monodroid_log (level, category, message);
}

/* Invoked by:
   - System.Core.dll!System.TimeZoneInfo.Android.GetDefaultTimeZoneName
     https://github.com/mono/mono/blob/e59c1cd70f4a7171a0ff5e1f9f4937985d6a4d8d/mcs/class/corlib/System/TimeZoneInfo.Android.cs#L569-L594
   - Mono.Android.dll!Android.Runtime.AndroidEnvironment.GetDefaultTimeZone
*/

MONO_API void
monodroid_free (void *ptr)
{
	free (ptr);
}

// Used by https://github.com/mono/mono/blob/e59c1cd70f4a7171a0ff5e1f9f4937985d6a4d8d/mcs/class/corlib/System/TimeZoneInfo.Android.cs#L569-L594
MONO_API int
monodroid_get_system_property (const char *name, char **value)
{
	return internal_calls->monodroid_get_system_property (name, value);
}

// Used by Mono.Android.dll!Java.Interop.Runtime.MaxGlobalReferenceCount
MONO_API int
_monodroid_max_gref_get (void)
{
	return internal_calls->monodroid_max_gref_get ();
}

// Used by Mono.Android.dll!Java.Interop.Runtime.GlobalReferenceCount
MONO_API int
_monodroid_gref_get (void)
{
	return internal_calls->monodroid_gref_get ();
}

// Used by Mono.Android.dll!Java.Interop.Runtime.WeakGlobalReferenceCount
MONO_API int
_monodroid_weak_gref_get (void)
{
	return internal_calls->monodroid_weak_gref_get ();
}

// Used by Mono.Android.dll!Android.Runtime.JNIEnv
MONO_API void
_monodroid_gref_log (const char *message)
{
	internal_calls->monodroid_gref_log (message);
}

MONO_API int
_monodroid_gref_log_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable)
{
	return internal_calls->monodroid_gref_log_new (curHandle, curType, newHandle, newType, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_gref_log_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	internal_calls->monodroid_gref_log_delete (handle, type, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_weak_gref_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable)
{
	internal_calls->monodroid_weak_gref_new (curHandle, curType, newHandle, newType, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_weak_gref_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	internal_calls->monodroid_weak_gref_delete (handle, type, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_lref_log_new (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	internal_calls->monodroid_lref_log_new (lrefc, handle, type, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_lref_log_delete (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	internal_calls->monodroid_lref_log_delete (lrefc, handle, type, threadName, threadId, from, from_writable);
}

MONO_API void
_monodroid_gc_wait_for_bridge_processing (void)
{
	internal_calls->monodroid_gc_wait_for_bridge_processing ();
}

/* !DO NOT REMOVE! Used by Mono BCL */
MONO_API int
_monodroid_get_android_api_level (void)
{
	return internal_calls->monodroid_get_android_api_level ();
}

/* Can be called by a native debugger to break the wait on startup */
MONO_API void
monodroid_clear_gdb_wait (void)
{
	internal_calls->monodroid_clear_gdb_wait ();
}

MONO_API void*
_monodroid_get_identity_hash_code (JNIEnv *env, void *v)
{
	return internal_calls->monodroid_get_identity_hash_code (env, v);
}

MONO_API void*
_monodroid_timezone_get_default_id (void)
{
	return internal_calls->monodroid_timezone_get_default_id ();
}

MONO_API void
_monodroid_counters_dump (const char *format, ...)
{
	va_list args;
	va_start (args, format);
	internal_calls->dump_counters (format, args);
	va_end (args);
}

MONO_API int
monodroid_embedded_assemblies_set_assemblies_prefix (const char *prefix)
{
	return internal_calls->monodroid_embedded_assemblies_set_assemblies_prefix (prefix);
}

MONO_API managed_timing_sequence*
monodroid_timing_start (const char *message)
{
	return internal_calls->monodroid_timing_start (message);
}

MONO_API void
monodroid_timing_stop (managed_timing_sequence *sequence, const char *message)
{
	internal_calls->monodroid_timing_stop (sequence, message);
}

MONO_API char*
monodroid_strdup_printf (const char *format, ...)
{
	va_list args;

	va_start (args, format);
	char *ret = internal_calls->monodroid_strdup_printf (format, args);
	va_end (args);

	return ret;
}

MONO_API char*
monodroid_TypeManager_get_java_class_name (jclass klass)
{
	return internal_calls->monodroid_TypeManager_get_java_class_name (klass);
}

MONO_API void
monodroid_store_package_name (const char *name)
{
	internal_calls->monodroid_store_package_name (name);
}

MONO_API int
monodroid_get_namespaced_system_property (const char *name, char **value)
{
	return internal_calls->monodroid_get_namespaced_system_property (name, value);
}

MONO_API FILE*
monodroid_fopen (const char* filename, const char* mode)
{
	return internal_calls->monodroid_fopen (filename, mode);
}

MONO_API int
send_uninterrupted (int fd, void *buf, int len)
{
	return internal_calls->send_uninterrupted (fd, buf, len);
}

MONO_API int
recv_uninterrupted (int fd, void *buf, int len)
{
	return internal_calls->recv_uninterrupted (fd, buf, len);
}

MONO_API void
set_world_accessable (const char *path)
{
	internal_calls->set_world_accessable (path);
}

MONO_API void
create_public_directory (const char *dir)
{
	internal_calls->create_public_directory (dir);
}

MONO_API char*
path_combine (const char *path1, const char *path2)
{
	return internal_calls->path_combine (path1, path2);
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
#endif // ndef NET
