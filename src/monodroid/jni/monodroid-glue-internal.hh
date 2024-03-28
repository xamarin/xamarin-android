// Dear Emacs, this is a -*- C++ -*- header
#ifndef __MONODROID_GLUE_INTERNAL_H
#define __MONODROID_GLUE_INTERNAL_H

#include <cstdint>
#include <string>
#include <string_view>

#include <jni.h>
#include "android-system.hh"
#include "osbridge.hh"
#include "timing.hh"
#include "cpp-util.hh"
#include "xxhash.hh"

#include <mono/utils/mono-counters.h>
#include <mono/metadata/profiler.h>

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

//#include <mono/utils/mono-publib.h>
#include <mono/jit/mono-private-unstable.h>
#include <mono/metadata/mono-private-unstable.h>

// See https://github.com/dotnet/runtime/pull/67024
// See https://github.com/xamarin/xamarin-android/issues/6935
extern mono_bool mono_opt_aot_lazy_assembly_load;

namespace xamarin::android::internal
{
	struct PinvokeEntry
	{
		hash_t      hash;
		const char *name;
		void       *func;
	};

	struct string_hash
	{
		force_inline xamarin::android::hash_t operator() (std::string const& s) const noexcept
		{
			return xamarin::android::xxhash::hash (s.c_str (), s.length ());
		}
	};

	// Values must be identical to those in src/Mono.Android/Android.Runtime/RuntimeNativeMethods.cs
	enum class TraceKind : uint32_t
	{
		Java    = 0x01,
		Managed = 0x02,
		Native  = 0x04,
		Signals = 0x08,
	};

	class MonodroidRuntime
	{
		using pinvoke_api_map = tsl::robin_map<
			std::string,
			void*,
			string_hash,
			std::equal_to<std::string>,
			std::allocator<std::pair<std::string, void*>>,
			true
		>;

		using pinvoke_api_map_ptr = pinvoke_api_map*;
		using pinvoke_library_map = tsl::robin_map<
			std::string,
			pinvoke_api_map_ptr,
			string_hash,
			std::equal_to<std::string>,
			std::allocator<std::pair<std::string, pinvoke_api_map_ptr>>,
			true
		>;

		using load_assemblies_context_type = MonoAssemblyLoadContextGCHandle;
		static constexpr pinvoke_library_map::size_type LIBRARY_MAP_INITIAL_BUCKET_COUNT = 1;

#if defined (DEBUG)
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
			bool            jniRemappingInUse;
			bool            marshalMethodsEnabled;
		};

		using jnienv_initialize_fn = void (*) (JnienvInitializeArgs*);
		using jnienv_register_jni_natives_fn = void (*)(const jchar *typeName_ptr, int32_t typeName_len, jclass jniClass, const jchar *methods_ptr, int32_t methods_len);

	private:
		static constexpr std::string_view base_apk_name { "/base.apk" };
		static constexpr size_t SMALL_STRING_PARSE_BUFFER_LEN = 50;
		static constexpr bool is_running_on_desktop = false;

		static constexpr std::string_view mono_component_debugger_name  { "libmono-component-debugger.so" };
		static constexpr hash_t mono_component_debugger_hash            = xxhash::hash (mono_component_debugger_name);

		static constexpr std::string_view mono_component_hot_reload_name { "libmono-component-hot_reload.so" };
		static constexpr hash_t mono_component_hot_reload_hash          = xxhash::hash (mono_component_hot_reload_name);

		static constexpr std::string_view mono_component_diagnostics_tracing_name { "libmono-component-diagnostics_tracing.so" };
		static constexpr hash_t mono_component_diagnostics_tracing_hash = xxhash::hash (mono_component_diagnostics_tracing_name);

		static constexpr char xamarin_native_tracing_name[]             = "libxamarin-native-tracing.so";

	public:
		static constexpr int XA_LOG_COUNTERS = MONO_COUNTER_JIT | MONO_COUNTER_METADATA | MONO_COUNTER_GC | MONO_COUNTER_GENERICS | MONO_COUNTER_INTERP;

	public:
		void Java_mono_android_Runtime_register (JNIEnv *env, jstring managedType, jclass nativeClass, jstring methods);
		void Java_mono_android_Runtime_initInternal (JNIEnv *env, jclass klass, jstring lang, jobjectArray runtimeApksJava,
		                                             jstring runtimeNativeLibDir, jobjectArray appDirs, jint localDateTimeOffset,
		                                             jobject loader, jobjectArray assembliesJava, jint apiLevel, jboolean isEmulator,
		                                             jboolean haveSplitApks);

		jint Java_JNI_OnLoad (JavaVM *vm, void *reserved);

		static bool is_startup_in_progress () noexcept
		{
			return startup_in_progress;
		}

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

		jclass get_java_class_TimeZone () const
		{
			return java_TimeZone;
		}

		void set_monodroid_gdb_wait (bool yes_no)
		{
			monodroid_gdb_wait = yes_no;
		}

		void propagate_uncaught_exception (JNIEnv *env, jobject javaThread, jthrowable javaException);
		char*	get_java_class_name_for_TypeManager (jclass klass);
		void log_traces (JNIEnv *env, TraceKind kind, const char *first_line) noexcept;

	private:
		static void mono_log_handler (const char *log_domain, const char *log_level, const char *message, mono_bool fatal, void *user_data);
		static void mono_log_standard_streams_handler (const char *str, mono_bool is_stdout);

		// A reference to unique_ptr is not the best practice ever, but it's faster this way
		void setup_mono_tracing (std::unique_ptr<char[]> const& mono_log_mask, bool have_log_assembly, bool have_log_gc);
		void install_logging_handlers ();

		unsigned int convert_dl_flags (int flags);

		static void  cleanup_runtime_config (MonovmRuntimeConfigArguments *args, void *user_data);
		static void* load_library_symbol (const char *library_name, const char *symbol_name, void **dso_handle = nullptr) noexcept;
		static void* load_library_entry (std::string const& library_name, std::string const& entrypoint_name, pinvoke_api_map_ptr api_map) noexcept;
		static void  load_library_entry (const char *library_name, const char *entrypoint_name, PinvokeEntry &entry, void **dso_handle) noexcept;
		static void* fetch_or_create_pinvoke_map_entry (std::string const& library_name, std::string const& entrypoint_name, hash_t entrypoint_name_hash, pinvoke_api_map_ptr api_map, bool need_lock) noexcept;
		static PinvokeEntry* find_pinvoke_address (hash_t hash, const PinvokeEntry *entries, size_t entry_count) noexcept;
		static void* handle_other_pinvoke_request (const char *library_name, hash_t library_name_hash, const char *entrypoint_name, hash_t entrypoint_name_hash) noexcept;
		static void* monodroid_pinvoke_override (const char *library_name, const char *entrypoint_name);

		template<typename TFunc>
		static void load_symbol (void *handle, const char *name, TFunc*& fnptr) noexcept
		{
			char *err = nullptr;
			void *symptr = monodroid_dlsym (handle, name, &err, nullptr);

			if (symptr == nullptr) {
				log_warn (LOG_DEFAULT, "Failed to load symbol '%s' library with handle %p. %s", name, handle, err == nullptr ? "Unknown error" : err);
				fnptr = nullptr;
				return;
			}

			fnptr = reinterpret_cast<TFunc*>(symptr);
		}

		static void* monodroid_dlopen_ignore_component_or_load (hash_t hash, const char *name, int flags, char **err) noexcept;
		static void* monodroid_dlopen (const char *name, int flags, char **err) noexcept;
		static void* monodroid_dlopen (const char *name, int flags, char **err, void *user_data) noexcept;
		static void* monodroid_dlsym (void *handle, const char *name, char **err, void *user_data);
		static void* monodroid_dlopen_log_and_return (void *handle, char **err, const char *full_name, bool free_memory, bool need_api_init = false);
		static DSOCacheEntry* find_dso_cache_entry (hash_t hash) noexcept;

		int LocalRefsAreIndirect (JNIEnv *env, jclass runtimeClass, int version);
		void create_xdg_directory (jstring_wrapper& home, size_t home_len, std::string_view const& relative_path, std::string_view const& environment_variable_name) noexcept;
		void create_xdg_directories_and_environment (jstring_wrapper &homeDir);
		void lookup_bridge_info (MonoClass *klass, const OSBridge::MonoJavaGCBridgeType *type, OSBridge::MonoJavaGCBridgeInfo *info);
		void lookup_bridge_info (MonoImage *image, const OSBridge::MonoJavaGCBridgeType *type, OSBridge::MonoJavaGCBridgeInfo *info);
		void load_assembly (MonoDomain *domain, jstring_wrapper &assembly);
		void load_assembly (MonoAssemblyLoadContextGCHandle alc_handle, jstring_wrapper &assembly);
		void load_assemblies (load_assemblies_context_type ctx, bool preload, jstring_array_wrapper &assemblies);

		void set_debug_options ();
		void parse_gdb_options ();
		void mono_runtime_init (JNIEnv *env, dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN>& runtime_args);

		void init_android_runtime (JNIEnv *env, jclass runtimeClass, jobject loader);
		void set_environment_variable_for_directory (const char *name, jstring_wrapper &value, bool createDirectory, mode_t mode);

		void set_environment_variable_for_directory (const char *name, jstring_wrapper &value)
		{
			set_environment_variable_for_directory (name, value, true, DEFAULT_DIRECTORY_MODE);
		}

		void set_environment_variable (const char *name, jstring_wrapper &value)
		{
			set_environment_variable_for_directory (name, value, false, 0);
		}

		static void monodroid_unhandled_exception (MonoObject *java_exception);
		MonoClass* get_android_runtime_class ();

		MonoDomain*	create_domain (JNIEnv *env, jstring_array_wrapper &runtimeApks, bool is_root_domain, bool have_split_apks);
		MonoDomain* create_and_initialize_domain (JNIEnv* env, jclass runtimeClass, jstring_array_wrapper &runtimeApks,
		                                          jstring_array_wrapper &assemblies, jobjectArray assembliesBytes, jstring_array_wrapper &assembliesPaths,
		                                          jobject loader, bool is_root_domain, bool force_preload_assemblies,
		                                          bool have_split_apks);

		void gather_bundled_assemblies (jstring_array_wrapper &runtimeApks, size_t *out_user_assemblies_count, bool have_split_apks);
		static bool should_register_file (const char *filename);
		void set_trace_options ();
		void set_profile_options ();

		void log_jit_event (MonoMethod *method, const char *event_name);
		static void jit_begin (MonoProfiler *prof, MonoMethod *method);
		static void jit_failed (MonoProfiler *prof, MonoMethod *method);
		static void jit_done (MonoProfiler *prof, MonoMethod *method, MonoJitInfo* jinfo);
		static void thread_start (MonoProfiler *prof, uintptr_t tid);
		static void thread_end (MonoProfiler *prof, uintptr_t tid);

#if !defined (RELEASE)
		static MonoReflectionType* typemap_java_to_managed (MonoString *java_type_name) noexcept;
		static const char* typemap_managed_to_java (MonoReflectionType *type, const uint8_t *mvid) noexcept;
#endif // !def RELEASE

		static void monodroid_debugger_unhandled_exception (MonoException *ex);

#if defined (RELEASE)
		static const char* get_method_name (uint32_t mono_image_index, uint32_t method_token) noexcept;

		template<bool NeedsLocking>
		static void get_function_pointer (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr) noexcept;
		static void get_function_pointer_at_startup (uint32_t mono_image_index, uint32_t class_token, uint32_t method_token, void*& target_ptr) noexcept;
		static void get_function_pointer_at_runtime (uint32_t mono_image_index, uint32_t class_token, uint32_t method_token, void*& target_ptr) noexcept;
#endif // def RELEASE

#if defined (DEBUG)
		void set_debug_env_vars (void);
		bool parse_runtime_args (dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> &runtime_args, RuntimeOptions *options);
		int monodroid_debug_connect (int sock, struct sockaddr_in addr);
		int monodroid_debug_accept (int sock, struct sockaddr_in addr);
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
		jclass              java_TimeZone;
		timing_period       jit_time;
		FILE               *jit_log;
		MonoProfilerHandle  profiler_handle;

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
		static bool         startup_in_progress;

		jnienv_register_jni_natives_fn jnienv_register_jni_natives = nullptr;
		MonoAssemblyLoadContextGCHandle default_alc = nullptr;

		static std::mutex             pinvoke_map_write_lock;
		static pinvoke_library_map    other_pinvoke_map;
		static MonoCoreRuntimeProperties monovm_core_properties;
		MonovmRuntimeConfigArguments  runtime_config_args;

		static void *system_native_library_handle;
		static void *system_security_cryptography_native_android_library_handle;
		static void *system_io_compression_native_library_handle;
		static std::mutex   dso_handle_write_lock;
	};
}
#endif
