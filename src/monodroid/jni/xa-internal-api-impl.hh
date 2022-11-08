// Dear Emacs, this is a -*- C++ -*- header
#ifndef __XA_INTERNAL_API_IMPL_HH
#define __XA_INTERNAL_API_IMPL_HH

#include "xa-internal-api.hh"

namespace xamarin::android::internal
{
	class MonoAndroidInternalCalls_Impl final : public MonoAndroidInternalCalls
	{
	public:
		virtual mono_bool monodroid_get_network_interface_up_state (const char *ifname, mono_bool *is_up) final override;
		virtual mono_bool monodroid_get_network_interface_supports_multicast (const char *ifname, mono_bool *supports_multicast) final override;
		virtual int monodroid_get_dns_servers (void **dns_servers_array) final override;
		virtual int monodroid_getifaddrs (_monodroid_ifaddrs **ifap) final override;
		virtual void monodroid_freeifaddrs (_monodroid_ifaddrs *ifa) final override;
		virtual void monodroid_detect_cpu_and_architecture (unsigned short *built_for_cpu, unsigned short *running_on_cpu, unsigned char *is64bit) final override;
		virtual unsigned int monodroid_get_log_categories () final override;
		virtual void monodroid_log (LogLevel level, LogCategories category, const char *message) final override;
		virtual int monodroid_get_system_property (const char *name, char **value) final override;
		virtual int monodroid_max_gref_get () final override;
		virtual int monodroid_gref_get () final override;
		virtual void monodroid_gref_log (const char *message) final override;
		virtual int monodroid_gref_log_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable) final override;
		virtual void monodroid_gref_log_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) final override;
		virtual int monodroid_weak_gref_get () final override;
		virtual void monodroid_weak_gref_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable) final override;
		virtual void monodroid_weak_gref_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) final override;
		virtual void monodroid_lref_log_new (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) final override;
		virtual void monodroid_lref_log_delete (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) final override;
		virtual void monodroid_gc_wait_for_bridge_processing () final override;
		virtual int monodroid_get_android_api_level () final override;
		virtual void monodroid_clear_gdb_wait () final override;
		virtual void* monodroid_get_identity_hash_code (JNIEnv *env, void *v) final override;
		virtual void* monodroid_timezone_get_default_id () final override;
		virtual void dump_counters (const char *format, va_list args) final override;
		virtual int monodroid_embedded_assemblies_set_assemblies_prefix (const char *prefix) final override;
		virtual managed_timing_sequence* monodroid_timing_start (const char *message) final override;
		virtual void monodroid_timing_stop (managed_timing_sequence *sequence, const char *message) final override;
		virtual char* monodroid_strdup_printf (const char *format, va_list args) final override;
		virtual char* monodroid_TypeManager_get_java_class_name (jclass klass) final override;
		virtual void monodroid_store_package_name (const char *name) final override;
		virtual int monodroid_get_namespaced_system_property (const char *name, char **value) final override;
		virtual FILE* monodroid_fopen (const char* filename, const char* mode) final override;
		virtual int send_uninterrupted (int fd, void *buf, int len) final override;
		virtual int recv_uninterrupted (int fd, void *buf, int len) final override;
		virtual void set_world_accessable (const char *path) final override;
		virtual void create_public_directory (const char *dir) final override;
		virtual char* path_combine (const char *path1, const char *path2) final override;
	};
}
#endif // __XA_INTERNAL_API_IMPL_HH
