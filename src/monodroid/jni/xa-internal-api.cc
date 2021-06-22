#ifdef WINDOWS
#include <windef.h>
#include <winbase.h>
#include <shlobj.h>
#include <objbase.h>
#include <knownfolders.h>
#include <shlwapi.h>
#endif  // defined(WINDOWS)

#include "globals.hh"
#include "xa-internal-api-impl.hh"

#if defined (WINDOWS)
#define WINDOWS_UNUSED_ARG [[maybe_unused]]
#else
#define WINDOWS_UNUSED_ARG
#endif

using namespace xamarin::android;
using namespace xamarin::android::internal;

void _monodroid_detect_cpu_and_architecture (unsigned short *built_for_cpu, unsigned short *running_on_cpu, unsigned char *is64bit);
mono_bool _monodroid_get_network_interface_up_state (const char *ifname, mono_bool *is_up);
mono_bool _monodroid_get_network_interface_supports_multicast (const char *ifname, mono_bool *supports_multicast);
int _monodroid_get_dns_servers (void **dns_servers_array);
int  _monodroid_getifaddrs (struct _monodroid_ifaddrs **ifap);
void _monodroid_freeifaddrs (struct _monodroid_ifaddrs *ifa);

mono_bool
MonoAndroidInternalCalls_Impl::monodroid_get_network_interface_up_state (WINDOWS_UNUSED_ARG const char *ifname, WINDOWS_UNUSED_ARG mono_bool *is_up)
{
#ifdef WINDOWS
	return FALSE;
#else  // !defined(WINDOWS)
	return ::_monodroid_get_network_interface_up_state (ifname, is_up);
#endif // defined(WINDOWS)
}

mono_bool
MonoAndroidInternalCalls_Impl::monodroid_get_network_interface_supports_multicast (WINDOWS_UNUSED_ARG const char *ifname, WINDOWS_UNUSED_ARG mono_bool *supports_multicast)
{
#ifdef WINDOWS
	return FALSE;
#else  // !defined(WINDOWS)
	return ::_monodroid_get_network_interface_supports_multicast (ifname, supports_multicast);
#endif // defined(WINDOWS)
}

int
MonoAndroidInternalCalls_Impl::monodroid_get_dns_servers (WINDOWS_UNUSED_ARG void **dns_servers_array)
{
#ifdef WINDOWS
	return FALSE;
#else  // !defined(WINDOWS)
	return ::_monodroid_get_dns_servers (dns_servers_array);
#endif // defined(WINDOWS)
}

int
MonoAndroidInternalCalls_Impl::monodroid_getifaddrs (WINDOWS_UNUSED_ARG struct _monodroid_ifaddrs **ifap)
{
#ifdef WINDOWS
	return -1;
#else  // !defined(WINDOWS)
	return ::_monodroid_getifaddrs (ifap);
#endif // defined(WINDOWS)
}

void
MonoAndroidInternalCalls_Impl::monodroid_freeifaddrs (WINDOWS_UNUSED_ARG struct _monodroid_ifaddrs *ifa)
{
#ifndef WINDOWS
	::_monodroid_freeifaddrs (ifa);
#endif // defined(WINDOWS)
}

void
MonoAndroidInternalCalls_Impl::monodroid_detect_cpu_and_architecture (unsigned short *built_for_cpu, unsigned short *running_on_cpu, unsigned char *is64bit)
{
	::_monodroid_detect_cpu_and_architecture (built_for_cpu, running_on_cpu, is64bit);
}

unsigned int
MonoAndroidInternalCalls_Impl::monodroid_get_log_categories ()
{
	return log_categories;
}

void
MonoAndroidInternalCalls_Impl::monodroid_log (LogLevel level, LogCategories category, const char *message)
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

int
MonoAndroidInternalCalls_Impl::monodroid_get_system_property (const char *name, char **value)
{
	return androidSystem.monodroid_get_system_property (name, value);
}

int
MonoAndroidInternalCalls_Impl::monodroid_max_gref_get ()
{
	return static_cast<int>(androidSystem.get_max_gref_count ());
}

int
MonoAndroidInternalCalls_Impl::monodroid_gref_get ()
{
	return osBridge.get_gc_gref_count ();
}

void
MonoAndroidInternalCalls_Impl::monodroid_gref_log (const char *message)
{
	osBridge._monodroid_gref_log (message);
}

int
MonoAndroidInternalCalls_Impl::monodroid_gref_log_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable)
{
	return osBridge._monodroid_gref_log_new (curHandle, curType, newHandle, newType, threadName, threadId, from, from_writable);
}

void
MonoAndroidInternalCalls_Impl::monodroid_gref_log_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	osBridge._monodroid_gref_log_delete (handle, type, threadName, threadId, from, from_writable);
}

void
MonoAndroidInternalCalls_Impl::monodroid_weak_gref_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable)
{
	osBridge._monodroid_weak_gref_new (curHandle, curType, newHandle, newType, threadName, threadId, from, from_writable);
}

void
MonoAndroidInternalCalls_Impl::monodroid_weak_gref_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	osBridge._monodroid_weak_gref_delete (handle, type, threadName, threadId, from, from_writable);
}

void
MonoAndroidInternalCalls_Impl::monodroid_lref_log_new (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	osBridge._monodroid_lref_log_new (lrefc, handle, type, threadName, threadId, from, from_writable);
}

void
MonoAndroidInternalCalls_Impl::monodroid_lref_log_delete (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable)
{
	osBridge._monodroid_lref_log_delete (lrefc, handle, type, threadName, threadId, from, from_writable);
}

void
MonoAndroidInternalCalls_Impl::monodroid_gc_wait_for_bridge_processing ()
{
	mono_gc_wait_for_bridge_processing ();
}

int
MonoAndroidInternalCalls_Impl::monodroid_get_android_api_level ()
{
	return monodroidRuntime.get_android_api_level ();
}

void
MonoAndroidInternalCalls_Impl::monodroid_clear_gdb_wait ()
{
	monodroidRuntime.set_monodroid_gdb_wait (false);
}

void*
MonoAndroidInternalCalls_Impl::monodroid_get_identity_hash_code (JNIEnv *env, void *v)
{
	intptr_t rv = env->CallStaticIntMethod (monodroidRuntime.get_java_class_System (), monodroidRuntime.get_java_class_method_System_identityHashCode (), v);
	return (void*) rv;
}

void*
MonoAndroidInternalCalls_Impl::monodroid_timezone_get_default_id ()
{
	log_warn (LOG_ASSEMBLY, "%s", __PRETTY_FUNCTION__);

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

void
MonoAndroidInternalCalls_Impl::dump_counters (const char *format, va_list args)
{
	monodroidRuntime.dump_counters_v (format, args);
}

int
MonoAndroidInternalCalls_Impl::monodroid_embedded_assemblies_set_assemblies_prefix (const char *prefix)
{
	embeddedAssemblies.set_assemblies_prefix (prefix);
	return 0;
}

managed_timing_sequence*
MonoAndroidInternalCalls_Impl::monodroid_timing_start (const char *message)
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

void
MonoAndroidInternalCalls_Impl::monodroid_timing_stop (managed_timing_sequence *sequence, const char *message)
{
	static constexpr const char DEFAULT_MESSAGE[] = "Managed Timing";

	if (sequence == nullptr)
		return;

	sequence->period.mark_end ();
	Timing::info (sequence->period, message == nullptr ? DEFAULT_MESSAGE : message);
	timing->release_sequence (sequence);
}

void
MonoAndroidInternalCalls_Impl::monodroid_strfreev (char **str_array)
{
	utils.monodroid_strfreev (str_array);
}

char**
MonoAndroidInternalCalls_Impl::monodroid_strsplit (const char *str, const char *delimiter, size_t max_tokens)
{
	return utils.monodroid_strsplit (str, delimiter, max_tokens);
}

char*
MonoAndroidInternalCalls_Impl::monodroid_strdup_printf (const char *format, va_list args)
{
	return utils.monodroid_strdup_vprintf (format, args);
}

char*
MonoAndroidInternalCalls_Impl::monodroid_TypeManager_get_java_class_name (jclass klass)
{
	return monodroidRuntime.get_java_class_name_for_TypeManager (klass);
}

void
MonoAndroidInternalCalls_Impl::monodroid_store_package_name (const char *name)
{
	utils.monodroid_store_package_name (name);
}

int
MonoAndroidInternalCalls_Impl::monodroid_get_namespaced_system_property (const char *name, char **value)
{
	return static_cast<int>(androidSystem.monodroid_get_system_property (name, value));
}

FILE*
MonoAndroidInternalCalls_Impl::monodroid_fopen (const char* filename, const char* mode)
{
	return utils.monodroid_fopen (filename, mode);
}

int
MonoAndroidInternalCalls_Impl::send_uninterrupted (int fd, void *buf, int len)
{
	if (len < 0)
		len = 0;
	return utils.send_uninterrupted (fd, buf, static_cast<size_t>(len));
}

int
MonoAndroidInternalCalls_Impl::recv_uninterrupted (int fd, void *buf, int len)
{
	if (len < 0)
		len = 0;
	return static_cast<int>(utils.recv_uninterrupted (fd, buf, static_cast<size_t>(len)));
}

void
MonoAndroidInternalCalls_Impl::set_world_accessable (const char *path)
{
	utils.set_world_accessable (path);
}

void
MonoAndroidInternalCalls_Impl::create_public_directory (const char *dir)
{
	utils.create_public_directory (dir);
}

char*
MonoAndroidInternalCalls_Impl::path_combine (const char *path1, const char *path2)
{
	return utils.path_combine (path1, path2);
}
