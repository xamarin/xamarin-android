/// Dear Emacs, this is a -*- C++ -*- header
#ifndef __OS_BRIDGE_H
#define __OS_BRIDGE_H

#include <array>

#include <jni.h>
#include <mono/metadata/sgen-bridge.h>
#include <mono/metadata/appdomain.h>

#include "logger.hh"

namespace xamarin::android::internal
{
	class OSBridge
	{
	private:
		struct MonodroidBridgeProcessingInfo {
			MonoDomain *domain;
			MonoClassField *bridge_processing_field;
			MonoVTable *jnienv_vtable;

			MonodroidBridgeProcessingInfo* next;
		};

	public:
		struct MonoJavaGCBridgeType
		{
			const char *_namespace;
			const char *_typename;
		};

		/* `mono_java_gc_bridge_info` stores shared global data about the last Monodroid assembly loaded.
		 * Specifically it stores data about the `mono_java_gc_bridge_types` types.
		 * In order for this to work, two rules must be followed.
		 *   1. Only one Monodroid appdomain can be loaded at a time.
		 *   2. Since the Monodroid appdomain unload clears `mono_java_gc_bridge_info`, anything which
		 *      could run at the same time as the domain unload (like gc_bridge_class_kind) must tolerate
		 *      the structure fields being set to zero during run
		 */
		struct MonoJavaGCBridgeInfo
		{
			MonoClass       *klass;
			MonoClassField  *handle;
			MonoClassField  *handle_type;
			MonoClassField  *refs_added;
			MonoClassField  *weak_handle;
		};

		// add_reference can work with objects which are either MonoObjects with java peers, or raw jobjects
		struct AddReferenceTarget
		{
			mono_bool is_mono_object;
			union {
				MonoObject *obj;
				jobject jobj;
			};
		};

		using MonodroidGCTakeRefFunc = mono_bool (*) (JNIEnv *env, MonoObject *obj);

		inline static const MonoJavaGCBridgeType empty_bridge_type = {
			"",
			"",
		};

		static constexpr std::array<MonoJavaGCBridgeType, 2> mono_xa_gc_bridge_types {{
			{ "Java.Lang",  "Object" },
			{ "Java.Lang",  "Throwable" },
		}};

		static constexpr std::array<MonoJavaGCBridgeType, 2> mono_ji_gc_bridge_types {{
			{ "Java.Interop",       "JavaObject" },
			{ "Java.Interop",       "JavaException" },
		}};

		static inline MonoJavaGCBridgeInfo empty_bridge_info = {
			nullptr,
			nullptr,
			nullptr,
			nullptr,
			nullptr
		};

		static constexpr uint32_t NUM_GC_BRIDGE_TYPES = mono_xa_gc_bridge_types.size () + mono_ji_gc_bridge_types.size ();
		static inline std::array<MonoJavaGCBridgeInfo, NUM_GC_BRIDGE_TYPES> mono_java_gc_bridge_info;

	public:
		static void clear_mono_java_gc_bridge_info () noexcept;
		static jobject lref_to_gref (JNIEnv *env, jobject lref) noexcept;

		static int get_gc_gref_count () noexcept
		{
			return gc_gref_count;
		}

		static int get_gc_weak_gref_count () noexcept
		{
			return gc_weak_gref_count;
		}

		static const MonoJavaGCBridgeType& get_java_gc_bridge_type (size_t index) noexcept
		{
			if (index < mono_xa_gc_bridge_types.size ())
				return mono_xa_gc_bridge_types [index];

			index -= mono_xa_gc_bridge_types.size ();
			if (index < mono_ji_gc_bridge_types.size ())
				return mono_ji_gc_bridge_types [index];

			return empty_bridge_type; // Not ideal...
		}

		static MonoJavaGCBridgeInfo& get_java_gc_bridge_info (size_t index) noexcept
		{
			if (index >= mono_java_gc_bridge_info.size ())
				return empty_bridge_info; // Not ideal...

			return mono_java_gc_bridge_info [index];
		}

		static JavaVM *get_jvm () noexcept
		{
			return jvm;
		}

		static void _monodroid_gref_log (const char *message) noexcept;
		static int _monodroid_gref_log_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable) noexcept;
		static void _monodroid_gref_log_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) noexcept;
		static void _monodroid_weak_gref_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable) noexcept;
		static void _monodroid_weak_gref_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) noexcept;
		static void _monodroid_lref_log_new (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) noexcept;
		static void _monodroid_lref_log_delete (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable) noexcept;

		static void monodroid_disable_gc_hooks () noexcept
		{
			gc_disabled = true;
		}

		static void register_gc_hooks () noexcept;
		static MonoGCBridgeObjectKind gc_bridge_class_kind (MonoClass *klass) noexcept;
		static mono_bool gc_is_bridge_object (MonoObject *object) noexcept;
		static void gc_cross_references (int num_sccs, MonoGCBridgeSCC **sccs, int num_xrefs, MonoGCBridgeXRef *xrefs) noexcept;
		static int get_gref_gc_threshold () noexcept;
		static JNIEnv* ensure_jnienv () noexcept;
		static void initialize_on_onload (JavaVM *vm, JNIEnv *env) noexcept;
		static void initialize_on_runtime_init (JNIEnv *env, jclass runtimeClass) noexcept;
		static void add_monodroid_domain (MonoDomain *domain) noexcept;
#if !defined (NET) && !defined (ANDROID)
		static void remove_monodroid_domain (MonoDomain *domain) noexcept;
		static void on_destroy_contexts () noexcept;
#endif // ndef NET

	private:
		static int get_gc_bridge_index (MonoClass *klass) noexcept;
		static MonoJavaGCBridgeInfo* get_gc_bridge_info_for_class (MonoClass *klass) noexcept;
		static MonoJavaGCBridgeInfo* get_gc_bridge_info_for_object (MonoObject *object) noexcept;
		static char get_object_ref_type (JNIEnv *env, void *handle) noexcept;

		static int _monodroid_gref_inc () noexcept
		{
			return __sync_add_and_fetch (&gc_gref_count, 1);
		}

		static int _monodroid_gref_dec () noexcept
		{
			return __sync_sub_and_fetch (&gc_gref_count, 1);
		}

		static char* _get_stack_trace_line_end (char *m) noexcept;
		static void _write_stack_trace (FILE *to, char *from, LogCategories = LOG_NONE) noexcept;
		static mono_bool take_global_ref_2_1_compat (JNIEnv *env, MonoObject *obj) noexcept;
		static mono_bool take_weak_global_ref_2_1_compat (JNIEnv *env, MonoObject *obj) noexcept;
		static mono_bool take_global_ref_jni (JNIEnv *env, MonoObject *obj) noexcept;
		static mono_bool take_weak_global_ref_jni (JNIEnv *env, MonoObject *obj) noexcept;
		static mono_bool add_reference_jobject (JNIEnv *env, jobject handle, jobject reffed_handle) noexcept;
		static mono_bool load_reference_target (AddReferenceTarget target, MonoJavaGCBridgeInfo** bridge_info, jobject *handle) noexcept;
		static mono_bool add_reference (JNIEnv *env, AddReferenceTarget target, AddReferenceTarget reffed_target) noexcept;
		static AddReferenceTarget target_from_mono_object (MonoObject *obj) noexcept;
		static AddReferenceTarget target_from_jobject (jobject jobj) noexcept;
		static int scc_get_stashed_index (MonoGCBridgeSCC *scc) noexcept;
		static void scc_set_stashed_index (MonoGCBridgeSCC *scc, int index) noexcept;
		static AddReferenceTarget target_from_scc (MonoGCBridgeSCC **sccs, int idx, JNIEnv *env, jobject temporary_peers) noexcept;
		static void target_release (JNIEnv *env, AddReferenceTarget target) noexcept;
		static mono_bool add_reference_mono_object (JNIEnv *env, MonoObject *obj, MonoObject *reffed_obj) noexcept;
		static void gc_prepare_for_java_collection (JNIEnv *env, int num_sccs, MonoGCBridgeSCC **sccs, int num_xrefs, MonoGCBridgeXRef *xrefs) noexcept;
		static void gc_cleanup_after_java_collection (JNIEnv *env, int num_sccs, MonoGCBridgeSCC **sccs) noexcept;

		static void java_gc (JNIEnv *env) noexcept
		{
			env->CallVoidMethod (Runtime_instance, Runtime_gc);
		}

		static void set_bridge_processing_field (MonodroidBridgeProcessingInfo *list, mono_bool value) noexcept;
		static int platform_supports_weak_refs () noexcept;

#if DEBUG
		static char* describe_target (AddReferenceTarget target) noexcept;
#endif
	private:
		static inline int gc_gref_count = 0;
		static inline int gc_weak_gref_count = 0;
		static inline bool gc_disabled = false;

		static inline MonodroidBridgeProcessingInfo *domains_list = nullptr;

		static inline MonodroidGCTakeRefFunc take_global_ref = nullptr;
		static inline MonodroidGCTakeRefFunc take_weak_global_ref = nullptr;

		static inline JavaVM *jvm;
		static inline jclass weakrefClass;
		static inline jmethodID weakrefCtor;
		static inline jmethodID weakrefGet;
		static inline jobject    Runtime_instance;
		static inline jmethodID  Runtime_gc;

		// These will be loaded as needed and persist between GCs
		// FIXME: This code assumes it is totally safe to hold onto these GREFs forever. Can
		// mono.android.jar ever be unloaded?
		static inline jclass    ArrayList_class = nullptr;
		static inline jclass    GCUserPeer_class;
		static inline jmethodID ArrayList_ctor;
		static inline jmethodID ArrayList_get;
		static inline jmethodID ArrayList_add;
		static inline jmethodID GCUserPeer_ctor;
	};
}
#endif // !__OS_BRIDGE_H
