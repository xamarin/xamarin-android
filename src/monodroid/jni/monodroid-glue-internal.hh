// Dear Emacs, this is a -*- C++ -*- header
#ifndef __MONODROID_GLUE_INTERNAL_H
#define __MONODROID_GLUE_INTERNAL_H

#include <string>
#include <jni.h>
#include "android-system.hh"
#include "osbridge.hh"
#include "timing.hh"

#include <mono/utils/mono-counters.h>
#include <mono/metadata/profiler.h>

#if defined (NET6)
// NDEBUG causes robin_map.h not to include <iostream> which, in turn, prevents indirect inclusion of <mutex>. <mutex>
// conflicts with our std::mutex definition in cppcompat.hh
#if !defined (NDEBUG)
#define NDEBUG
#define NDEBUG_UNDEFINE
#endif

// hush some compiler warnings
#if defined (__clang__)
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wunused-parameter"
#endif // __clang__

#include <tsl/robin_map.h>

#if defined (__clang__)
#pragma clang diagnostic pop
#endif // __clang__

#if defined (NDEBUG_UNDEFINE)
#undef NDEBUG
#undef NDEBUG_UNDEFINE
#endif

#include <mono/jit/mono-private-unstable.h>
#include <mono/metadata/mono-private-unstable.h>
#endif

namespace xamarin::android::internal
{
	class MonodroidRuntime
	{
#if defined (NET6)
		using pinvoke_api_map = tsl::robin_map<
			std::string,
			void*,
			std::hash<std::string>,
			std::equal_to<std::string>,
			std::allocator<std::pair<std::string, void*>>,
			true
		>;

		using pinvoke_api_map_ptr = pinvoke_api_map*;
		using pinvoke_library_map = tsl::robin_map<
			std::string,
			pinvoke_api_map_ptr,
			std::hash<std::string>,
			std::equal_to<std::string>,
			std::allocator<std::pair<std::string, pinvoke_api_map_ptr>>,
			true
		>;

		static constexpr pinvoke_library_map::size_type LIBRARY_MAP_INITIAL_BUCKET_COUNT = 1;
#endif // def NET6

#if defined (DEBUG) && !defined (WINDOWS)
		struct RuntimeOptions {
			bool debug = false;
			int loglevel = 0;
			int64_t timeout_time = 0;
			char *host = nullptr;
			uint16_t sdb_port = 0;
			uint16_t out_port = 0;
			bool server = false;
		};
#endif

		// Keep the enum values in sync with those in src/Mono.Android/AndroidRuntime/BoundExceptionType.cs
		enum class BoundExceptionType : uint8_t
		{
			System = 0x00,
			Java   = 0x01,
		};

		struct JnienvInitializeArgs {
			JavaVM         *javaVm;
			JNIEnv         *env;
			jobject         grefLoader;
			jmethodID       Loader_loadClass;
			jclass          grefClass;
			jmethodID       Class_forName;
			unsigned int    logCategories;
			int             version;
			int             androidSdkVersion;
			int             localRefsAreIndirect;
			int             grefGcThreshold;
			jobject         grefIGCUserPeer;
			int             isRunningOnDesktop;
			uint8_t         brokenExceptionTransitions;
			int             packageNamingPolicy;
			uint8_t         boundExceptionType;
			int             jniAddNativeMethodRegistrationAttributePresent;
		};

	private:
		static constexpr size_t SMALL_STRING_PARSE_BUFFER_LEN = 50;

#define MAKE_API_DSO_NAME(_ext_) "libxa-internal-api." # _ext_
#if defined (WINDOWS)
		static constexpr char API_DSO_NAME[] = MAKE_API_DSO_NAME (dll);
#elif defined (APPLE_OS_X)
		static constexpr char API_DSO_NAME[] = MAKE_API_DSO_NAME (dylib);
#else   // !defined(WINDOWS) && !defined(APPLE_OS_X)
		static constexpr char API_DSO_NAME[] = MAKE_API_DSO_NAME (so);
#endif  // defined(WINDOWS)
	public:
		static constexpr int XA_LOG_COUNTERS = MONO_COUNTER_JIT | MONO_COUNTER_METADATA | MONO_COUNTER_GC | MONO_COUNTER_GENERICS | MONO_COUNTER_INTERP;

	public:
		void Java_mono_android_Runtime_register (JNIEnv *env, jstring managedType, jclass nativeClass, jstring methods);
		void Java_mono_android_Runtime_initInternal (JNIEnv *env, jclass klass, jstring lang, jobjectArray runtimeApksJava,
		                                             jstring runtimeNativeLibDir, jobjectArray appDirs, jobject loader,
		                                             jobjectArray assembliesJava, jint apiLevel, jboolean isEmulator);
#if !defined (ANDROID)
		jint Java_mono_android_Runtime_createNewContextWithData (JNIEnv *env, jclass klass, jobjectArray runtimeApksJava, jobjectArray assembliesJava,
		                                                         jobjectArray assembliesBytes, jobjectArray assembliesPaths, jobject loader, jboolean force_preload_assemblies);
		void Java_mono_android_Runtime_switchToContext (JNIEnv *env, jint contextID);
		void Java_mono_android_Runtime_destroyContexts (JNIEnv *env, jintArray array);
		void shutdown_android_runtime (MonoDomain *domain);
#endif
		jint Java_JNI_OnLoad (JavaVM *vm, void *reserved);

		int get_android_api_level () const
		{
			return android_api_level;
		}

		jclass get_java_class_System () const
		{
			return java_System;
		}

		jmethodID get_java_class_method_System_identityHashCode () const
		{
			return java_System_identityHashCode;
		}

		jclass get_java_class_TimeZone ()
		{
			if (XA_UNLIKELY (java_TimeZone == nullptr)) {
				lookup_java_timezone ();
			}

			return java_TimeZone;
		}

		void set_monodroid_gdb_wait (bool yes_no)
		{
			monodroid_gdb_wait = yes_no;
		}

		FILE *get_counters () const
		{
			return counters;
		}

		void propagate_uncaught_exception (MonoDomain *domain, JNIEnv *env, jobject javaThread, jthrowable javaException);

		// The reason we don't use the C++ overload feature here is that there appears to be an issue in clang++ that
		// comes with the Android NDK. The issue is that for calls like:
		//
		//   char *s = "something";
		//   dump_counters ("My string: %s", s);
		//
		// the compiler will resolve the overload taking `va_list` instead of the one with the ellipsis, thus causing
		// `vfprintf` to segfault while trying to perform a `strlen` on `args`.
		//
		// The issue appears to stem from the fact that `va_list` in the NDK is a typedef to `__builtin_va_list` which
		// in turn is internally defined by clang to be `char*`
		//
		// Desktop builds (using both clang++ and g++) do NOT appear to have this issue, so it might be a problem in the
		// NDK. More investigation would be required, but for now lets work around the issue by not overloading the
		// function
		void dump_counters (const char *format, ...);
		void dump_counters_v (const char *format, va_list args);
		char*	get_java_class_name_for_TypeManager (jclass klass);

	private:
		void lookup_java_timezone ();
		static MonoMethod* monodroid_get_required_method (MonoImage *image, MonoClass *klass, uint32_t tokenID, int param_count, char const *type_name, char const *method_name);

#if defined (ANDROID)
		static void mono_log_handler (const char *log_domain, const char *log_level, const char *message, mono_bool fatal, void *user_data);
		static void mono_log_standard_streams_handler (const char *str, mono_bool is_stdout);
		void setup_mono_tracing (char const* const& mono_log_mask);
		void install_logging_handlers ();
#endif // def ANDROID

		unsigned int convert_dl_flags (int flags);
#if defined (WINDOWS) || defined (APPLE_OS_X)
		static const char* get_my_location (bool remove_file_name = true);
#endif  // defined(WINDOWS) || defined(APPLE_OS_X)
#if defined (NET6)
		static void  cleanup_runtime_config (MonovmRuntimeConfigArguments *args, void *user_data);
		static void* load_library_entry (std::string const& library_name, std::string const& entrypoint_name, pinvoke_api_map_ptr api_map);
		static void* fetch_or_create_pinvoke_map_entry (std::string const& library_name, std::string const& entrypoint_name, pinvoke_api_map_ptr api_map, bool need_lock);
		static void* monodroid_pinvoke_override (const char *library_name, const char *entrypoint_name);
		static void* monodroid_dlopen (const char *name, int flags, char **err);
#endif // def NET6
		static void* monodroid_dlopen (const char *name, int flags, char **err, void *user_data);
		static void* monodroid_dlsym (void *handle, const char *name, char **err, void *user_data);
		static void* monodroid_dlopen_log_and_return (void *handle, char **err, const char *full_name, bool free_memory, bool need_api_init = false);
#if !defined (NET6)
		static void  init_internal_api_dso (void *handle);
#endif // ndef NET6
		int LocalRefsAreIndirect (JNIEnv *env, jclass runtimeClass, int version);
		void create_xdg_directory (jstring_wrapper& home, size_t home_len, const char *relativePath, size_t relative_path_len, const char *environmentVariableName);
		void create_xdg_directories_and_environment (jstring_wrapper &homeDir);
		void disable_external_signal_handlers ();
		void lookup_bridge_info (MonoDomain *domain, MonoImage *image, const OSBridge::MonoJavaGCBridgeType *type, OSBridge::MonoJavaGCBridgeInfo *info);
		void load_assembly (MonoDomain *domain, jstring_wrapper &assembly);
		void load_assemblies (MonoDomain *domain, bool preload, jstring_array_wrapper &assemblies);
		void set_debug_options ();
		void parse_gdb_options ();
		void mono_runtime_init (dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN>& runtime_args);
#if !defined (NET6)
		void setup_bundled_app (const char *dso_name);
#endif // ndef NET6
		void init_android_runtime (MonoDomain *domain, JNIEnv *env, jclass runtimeClass, jobject loader, MonoClass*& jnienv, MonoClassField*& jnienv_bridge_processing_field);
		void set_environment_variable_for_directory (const char *name, jstring_wrapper &value, bool createDirectory, mode_t mode);

		void set_environment_variable_for_directory (const char *name, jstring_wrapper &value)
		{
			set_environment_variable_for_directory (name, value, true, DEFAULT_DIRECTORY_MODE);
		}

		void set_environment_variable (const char *name, jstring_wrapper &value)
		{
			set_environment_variable_for_directory (name, value, false, 0);
		}

		MonoClass* get_android_runtime_class (MonoDomain *domain);
		MonoDomain*	create_domain (JNIEnv *env, jstring_array_wrapper &runtimeApks, bool is_root_domain);
		MonoDomain* create_and_initialize_domain (JNIEnv* env, jclass runtimeClass, jstring_array_wrapper &runtimeApks,
		                                          jstring_array_wrapper &assemblies, jobjectArray assembliesBytes, jstring_array_wrapper &assembliesPaths,
		                                          jobject loader, bool is_root_domain, bool force_preload_assemblies);

		void gather_bundled_assemblies (jstring_array_wrapper &runtimeApks, size_t *out_user_assemblies_count);
		static bool should_register_file (const char *filename);
		void set_trace_options ();
		void set_profile_options ();

		void log_jit_event (MonoMethod *method, const char *event_name);
		static void jit_begin (MonoProfiler *prof, MonoMethod *method);
		static void jit_failed (MonoProfiler *prof, MonoMethod *method);
		static void jit_done (MonoProfiler *prof, MonoMethod *method, MonoJitInfo* jinfo);
		static void thread_start (MonoProfiler *prof, uintptr_t tid);
		static void thread_end (MonoProfiler *prof, uintptr_t tid);
		static MonoReflectionType* typemap_java_to_managed (MonoString *java_type_name);

		static const char* typemap_managed_to_java (MonoReflectionType *type, const uint8_t *mvid);

#if defined (DEBUG)
		void set_debug_env_vars (void);

#if !defined (WINDOWS)
		bool parse_runtime_args (dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> &runtime_args, RuntimeOptions *options);
		int monodroid_debug_connect (int sock, struct sockaddr_in addr);
		int monodroid_debug_accept (int sock, struct sockaddr_in addr);
#endif // !WINDOWS
#endif // DEBUG

#if !defined (RELEASE)
		static MonoAssembly* open_from_update_dir (MonoAssemblyName *aname, char **assemblies_path, void *user_data);
#endif
	private:
		MonoMethod         *registerType          = nullptr;
		int                 android_api_level     = 0;
		volatile bool       monodroid_gdb_wait    = true;
		jclass              java_System;
		jmethodID           java_System_identityHashCode;
		jmethodID           Class_getName;
		jclass              java_TimeZone = nullptr;
		jclass              java_mono_Runtime = nullptr;
		timing_period       jit_time;
		FILE               *jit_log;
		MonoProfilerHandle  profiler_handle;
		FILE               *counters;
		/*
		 * If set, monodroid will spin in a loop until the debugger breaks the wait by
		 * clearing monodroid_gdb_wait.
		 */
		bool                wait_for_gdb;

		/* The context (mapping to a Mono AppDomain) that is currently selected as the
		 * active context from the point of view of Java. We cannot rely on the value
		 * of `mono_domain_get` for this as it's stored per-thread and we want to be
		 * able to switch our different contexts from different threads.
		 */
		int                 current_context_id = -1;

#if defined (NET6)
		static std::mutex             pinvoke_map_write_lock;
		static pinvoke_api_map        xa_pinvoke_map;
		static pinvoke_library_map    other_pinvoke_map;
		static MonoCoreRuntimeProperties monovm_core_properties;
		MonovmRuntimeConfigArguments  runtime_config_args;
#else // def NET6
		static std::mutex   api_init_lock;
		static void        *api_dso_handle;
#endif // !def NET6
		static std::mutex   jni_lookup_lock;
	};
}
#endif
