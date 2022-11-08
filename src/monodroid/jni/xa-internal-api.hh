// Dear Emacs, this is a -*- C++ -*- header
#ifndef __XA_INTERNAL_API_HH
#define __XA_INTERNAL_API_HH

#include <stdio.h>
#include <cstdarg>
#include <jni.h>

#include "monodroid.h"
#include "logger.hh"
#include "timing.hh"
#include "xamarin_getifaddrs.h"

namespace xamarin::android
{
	class MonoAndroidInternalCalls
	{
	public:
		static constexpr char INIT_FUNCTION_NAME[] = "_monodroid_init_internal_api";
		static constexpr char SHUTDOWN_FUNCTION_NAME[] = "_monodroid_shutdown_internal_api";

		// To shush compiler warnings
		virtual ~MonoAndroidInternalCalls ()
		{}

	public:
		virtual mono_bool monodroid_get_network_interface_up_state (const char *ifname, mono_bool *is_up) = 0;
		virtual mono_bool monodroid_get_network_interface_supports_multicast (const char *ifname, mono_bool *supports_multicast) = 0;
		virtual int monodroid_get_dns_servers (void **dns_servers_array) = 0;
		virtual int monodroid_getifaddrs (_monodroid_ifaddrs **ifap) = 0;
		virtual void monodroid_freeifaddrs (_monodroid_ifaddrs *ifa) = 0;
		virtual void monodroid_detect_cpu_and_architecture (unsigned short *built_for_cpu, unsigned short *running_on_cpu, unsigned char *is64bit) = 0;
		virtual unsigned int monodroid_get_log_categories () = 0;
		virtual void monodroid_log (LogLevel level, LogCategories category, const char *message) = 0;
		virtual int monodroid_get_system_property (const char *name, char **value) = 0;
		virtual int monodroid_max_gref_get () = 0;
		virtual int monodroid_gref_get () = 0;
		virtual void monodroid_gref_log (const char *message) = 0;
		virtual int monodroid_gref_log_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable) = 0;
		virtual void monodroid_gref_log_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) = 0;
		virtual int monodroid_weak_gref_get () = 0;
		virtual void monodroid_weak_gref_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable) = 0;
		virtual void monodroid_weak_gref_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) = 0;
		virtual void monodroid_lref_log_new (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) = 0;
		virtual void monodroid_lref_log_delete (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) = 0;
		virtual void monodroid_gc_wait_for_bridge_processing () = 0;
		virtual int monodroid_get_android_api_level () = 0;
		virtual void monodroid_clear_gdb_wait () = 0;
		virtual void* monodroid_get_identity_hash_code (JNIEnv *env, void *v) = 0;
		virtual void* monodroid_timezone_get_default_id () = 0;
		virtual void dump_counters (const char *format, va_list args) = 0;
		virtual int monodroid_embedded_assemblies_set_assemblies_prefix (const char *prefix) = 0;
		virtual managed_timing_sequence* monodroid_timing_start (const char *message) = 0;
		virtual void monodroid_timing_stop (managed_timing_sequence *sequence, const char *message) = 0;
		virtual char* monodroid_strdup_printf (const char *format, va_list args) = 0;
		virtual char* monodroid_TypeManager_get_java_class_name (jclass klass) = 0;
		virtual void monodroid_store_package_name (const char *name) = 0;
		virtual int monodroid_get_namespaced_system_property (const char *name, char **value) = 0;
		virtual FILE* monodroid_fopen (const char* filename, const char* mode) = 0;
		virtual int send_uninterrupted (int fd, void *buf, int len) = 0;
		virtual int recv_uninterrupted (int fd, void *buf, int len) = 0;
		virtual void set_world_accessable (const char *path) = 0;
		virtual void create_public_directory (const char *dir) = 0;
		virtual char* path_combine (const char *path1, const char *path2) = 0;
	};

	typedef bool (*external_api_init_fn) (MonoAndroidInternalCalls *api);
	typedef void (*external_api_shutdown_fn) ();
}
#endif // __XA_INTERNAL_API_HH
