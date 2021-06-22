// Dear Emacs, this is a -*- C++ -*- header
#ifndef __OS_BRIDGE_H
#define __OS_BRIDGE_H

#include <jni.h>
#include <mono/metadata/sgen-bridge.h>

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
			const char       *_namespace;
			const char       *_typename;
			const uint32_t    class_token_id;
			const uint32_t    handle_token_id;
			const uint32_t    handle_type_token_id;
			const uint32_t    refs_added_token_id;
			const uint32_t    weak_handle_token_id;
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

		using MonodroidGCTakeRefFunc = mono_bool (OSBridge::*) (JNIEnv *env, MonoObject *obj);

		static const MonoJavaGCBridgeType empty_bridge_type;
		static const MonoJavaGCBridgeType mono_xa_gc_bridge_types[];
		static const MonoJavaGCBridgeType mono_ji_gc_bridge_types[];
		static MonoJavaGCBridgeInfo empty_bridge_info;
		static MonoJavaGCBridgeInfo mono_java_gc_bridge_info [];

	public:
		static const uint32_t NUM_XA_GC_BRIDGE_TYPES;
		static const uint32_t NUM_JI_GC_BRIDGE_TYPES;
		static const uint32_t NUM_GC_BRIDGE_TYPES;

	public:
		void clear_mono_java_gc_bridge_info ();
		jobject	lref_to_gref (JNIEnv *env, jobject lref);

		int get_gc_gref_count () const
		{
			return gc_gref_count;
		}

		const MonoJavaGCBridgeType& get_java_gc_bridge_type (uint32_t index)
		{
			if (index < NUM_XA_GC_BRIDGE_TYPES)
				return mono_xa_gc_bridge_types [index];

			index -= NUM_XA_GC_BRIDGE_TYPES;
			if (index < NUM_JI_GC_BRIDGE_TYPES)
				return mono_ji_gc_bridge_types [index];

			index -= NUM_JI_GC_BRIDGE_TYPES;
			return empty_bridge_type; // Not ideal...
		}

		MonoJavaGCBridgeInfo& get_java_gc_bridge_info (uint32_t index)
		{
			if (index >= NUM_GC_BRIDGE_TYPES)
				return empty_bridge_info; // Not ideal...

			return mono_java_gc_bridge_info [index];
		}

		JavaVM *get_jvm () const
		{
			return jvm;
		}

		void _monodroid_gref_log (const char *message);
		int _monodroid_gref_log_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable);
		void _monodroid_gref_log_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable);
		void _monodroid_weak_gref_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, const char *from, int from_writable);
		void _monodroid_weak_gref_delete (jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable);
		void _monodroid_lref_log_new (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable);
		void _monodroid_lref_log_delete (int lrefc, jobject handle, char type, const char *threadName, int threadId, const char *from, int from_writable);
		void monodroid_disable_gc_hooks ();
		void register_gc_hooks ();
		MonoGCBridgeObjectKind gc_bridge_class_kind (MonoClass *klass);
		mono_bool gc_is_bridge_object (MonoObject *object);
		void gc_cross_references (int num_sccs, MonoGCBridgeSCC **sccs, int num_xrefs, MonoGCBridgeXRef *xrefs);
		int get_gref_gc_threshold ();
		JNIEnv* ensure_jnienv ();
		void initialize_on_onload (JavaVM *vm, JNIEnv *env);
		void initialize_on_runtime_init (JNIEnv *env, jclass runtimeClass);
		void add_monodroid_domain (MonoDomain *domain, MonoClass* jnienv, MonoClassField* jnienv_bridge_processing_field);
		void remove_monodroid_domain (MonoDomain *domain);
		void on_destroy_contexts ();

	private:
		int get_gc_bridge_index (MonoClass *klass);
		MonoJavaGCBridgeInfo* get_gc_bridge_info_for_class (MonoClass *klass);
		MonoJavaGCBridgeInfo* get_gc_bridge_info_for_object (MonoObject *object);
		char get_object_ref_type (JNIEnv *env, void *handle);
		int _monodroid_gref_inc ();
		int _monodroid_gref_dec ();
		char* _get_stack_trace_line_end (char *m);
		void _write_stack_trace (FILE *to, const char *from);
		mono_bool take_global_ref_2_1_compat (JNIEnv *env, MonoObject *obj);
		mono_bool take_weak_global_ref_2_1_compat (JNIEnv *env, MonoObject *obj);
		mono_bool take_global_ref_jni (JNIEnv *env, MonoObject *obj);
		mono_bool take_weak_global_ref_jni (JNIEnv *env, MonoObject *obj);
		mono_bool add_reference_jobject (JNIEnv *env, jobject handle, jobject reffed_handle);
		mono_bool load_reference_target (AddReferenceTarget target, MonoJavaGCBridgeInfo** bridge_info, jobject *handle);
		mono_bool add_reference (JNIEnv *env, AddReferenceTarget target, AddReferenceTarget reffed_target);
		AddReferenceTarget target_from_mono_object (MonoObject *obj);
		AddReferenceTarget target_from_jobject (jobject jobj);
		int scc_get_stashed_index (MonoGCBridgeSCC *scc);
		void scc_set_stashed_index (MonoGCBridgeSCC *scc, int index);
		AddReferenceTarget target_from_scc (MonoGCBridgeSCC **sccs, int idx, JNIEnv *env, jobject temporary_peers);
		void target_release (JNIEnv *env, AddReferenceTarget target);
		mono_bool add_reference_mono_object (JNIEnv *env, MonoObject *obj, MonoObject *reffed_obj);
		void gc_prepare_for_java_collection (JNIEnv *env, int num_sccs, MonoGCBridgeSCC **sccs, int num_xrefs, MonoGCBridgeXRef *xrefs);
		void gc_cleanup_after_java_collection (JNIEnv *env, int num_sccs, MonoGCBridgeSCC **sccs);
		void java_gc (JNIEnv *env);
		void set_bridge_processing_field (MonodroidBridgeProcessingInfo *list, mono_bool value);
		int platform_supports_weak_refs ();

#if DEBUG
		char* describe_target (AddReferenceTarget target);
#endif
	private:
		int gc_gref_count = 0;
		int gc_weak_gref_count = 0;
		int gc_disabled = 0;

		MonodroidBridgeProcessingInfo *domains_list = nullptr;

		MonodroidGCTakeRefFunc take_global_ref = nullptr;
		MonodroidGCTakeRefFunc take_weak_global_ref = nullptr;

		JavaVM *jvm;
		jclass weakrefClass;
		jmethodID weakrefCtor;
		jmethodID weakrefGet;
		jobject    Runtime_instance;
		jmethodID  Runtime_gc;

		// These will be loaded as needed and persist between GCs
		// FIXME: This code assumes it is totally safe to hold onto these GREFs forever. Can
		// mono.android.jar ever be unloaded?
		jclass    ArrayList_class = nullptr;
		jclass    GCUserPeer_class;
		jmethodID ArrayList_ctor;
		jmethodID ArrayList_get;
		jmethodID ArrayList_add;
		jmethodID GCUserPeer_ctor;
	};
}
#endif // !__OS_BRIDGE_H
