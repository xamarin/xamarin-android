#include <compare>
#include <unistd.h>
#include <stdarg.h>
#include <mono/utils/mono-publib.h>
#include <mono/utils/mono-dl-fallback.h>

#include "globals.hh"
#include "monodroid-glue.hh"
#include "monodroid-glue-internal.hh"
#include "timing.hh"
#include "java-interop.h"
#include "cpu-arch.hh"
#include "xxhash.hh"
#include "startup-aware-lock.hh"
#include "jni-remapping.hh"

extern "C" {
	int _monodroid_getifaddrs (struct _monodroid_ifaddrs **ifap);
	void _monodroid_freeifaddrs (struct _monodroid_ifaddrs *ifa);
}

mono_bool _monodroid_get_network_interface_up_state (const char *ifname, mono_bool *is_up);
mono_bool _monodroid_get_network_interface_supports_multicast (const char *ifname, mono_bool *supports_multicast);
int _monodroid_get_dns_servers (void **dns_servers_array);

using namespace xamarin::android;
using namespace xamarin::android::internal;

void* MonodroidRuntime::system_native_library_handle = nullptr;
void* MonodroidRuntime::system_security_cryptography_native_android_library_handle = nullptr;
void* MonodroidRuntime::system_io_compression_native_library_handle = nullptr;

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
_monodroid_counters_dump ([[maybe_unused]] const char *format, [[maybe_unused]] va_list args)
{
#if !defined (NET)
	monodroidRuntime.dump_counters_v (format, args);
#endif // ndef NET
}

static managed_timing_sequence*
monodroid_timing_start (const char *message)
{
	if (timing == nullptr)
		return nullptr;

	managed_timing_sequence *ret = timing->get_available_sequence ();
	if (message != nullptr) {
		log_write (LOG_TIMING, LogLevel::Info, message);
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

static const char*
_monodroid_lookup_replacement_type (const char *jniSimpleReference)
{
	return JniRemapping::lookup_replacement_type (jniSimpleReference);
}

static const JniRemappingReplacementMethod*
_monodroid_lookup_replacement_method_info (const char *jniSourceType, const char *jniMethodName, const char *jniMethodSignature)
{
	return JniRemapping::lookup_replacement_method_info (jniSourceType, jniMethodName, jniMethodSignature);
}

static void
monodroid_log_traces (uint32_t kind, const char *first_line)
{
	JNIEnv *env = osBridge.ensure_jnienv ();
	auto tk = static_cast<TraceKind>(kind);

	monodroidRuntime.log_traces (env, tk, first_line);
}

#include "pinvoke-tables.include"

MonodroidRuntime::pinvoke_library_map MonodroidRuntime::other_pinvoke_map (MonodroidRuntime::LIBRARY_MAP_INITIAL_BUCKET_COUNT);

force_inline void*
MonodroidRuntime::load_library_symbol (const char *library_name, const char *symbol_name, void **dso_handle) noexcept
{
	void *lib_handle = dso_handle == nullptr ? nullptr : *dso_handle;

	if (lib_handle == nullptr) {
		lib_handle = monodroid_dlopen (library_name, MONO_DL_LOCAL, nullptr, nullptr);
		if (lib_handle == nullptr) {
			log_warn (LOG_ASSEMBLY, "Shared library '%s' not loaded, p/invoke '%s' may fail", library_name, symbol_name);
			return nullptr;
		}

		if (dso_handle != nullptr) {
			void *expected_null = nullptr;
			if (!__atomic_compare_exchange (dso_handle, &expected_null, &lib_handle, false /* weak */, __ATOMIC_ACQUIRE /* success_memorder */, __ATOMIC_RELAXED /* xxxfailure_memorder */)) {
				log_debug (LOG_ASSEMBLY, "Library '%s' handle already cached by another thread", library_name);
			}
		}
	}

	void *entry_handle = monodroid_dlsym (lib_handle, symbol_name, nullptr, nullptr);
	if (entry_handle == nullptr) {
		log_warn (LOG_ASSEMBLY, "Symbol '%s' not found in shared library '%s', p/invoke may fail", symbol_name, library_name);
		return nullptr;
	}

	return entry_handle;
}

// `pinvoke_map_write_lock` MUST be held when calling this method
force_inline void*
MonodroidRuntime::load_library_entry (std::string const& library_name, std::string const& entrypoint_name, pinvoke_api_map_ptr api_map) noexcept
{
	// Make sure some other thread hasn't just added the entry
	auto iter = api_map->find (entrypoint_name);
	if (iter != api_map->end () && iter->second != nullptr) {
		return iter->second;
	}

	void *entry_handle = load_library_symbol (library_name.c_str (), entrypoint_name.c_str ());
	if (entry_handle == nullptr) {
		// error already logged
		return nullptr;
	}

	log_debug (LOG_ASSEMBLY, "Caching p/invoke entry %s @ %s", library_name.c_str (), entrypoint_name.c_str ());
	(*api_map)[entrypoint_name] = entry_handle;
	return entry_handle;
}

force_inline void
MonodroidRuntime::load_library_entry (const char *library_name, const char *entrypoint_name, PinvokeEntry &entry, void **dso_handle) noexcept
{
	void *entry_handle = load_library_symbol (library_name, entrypoint_name, dso_handle);
	void *expected_null = nullptr;

	bool already_loaded = !__atomic_compare_exchange (
		/* ptr */              &entry.func,
		/* expected */         &expected_null,
		/* desired */          &entry_handle,
		/* weak */              false,
		/* success_memorder */  __ATOMIC_ACQUIRE,
		/* failure_memorder */  __ATOMIC_RELAXED
	);

	if (already_loaded) {
		log_debug (LOG_ASSEMBLY, "Entry '%s' from library '%s' already loaded by another thread", entrypoint_name, library_name);
	}
}

force_inline void*
MonodroidRuntime::fetch_or_create_pinvoke_map_entry (std::string const& library_name, std::string const& entrypoint_name, hash_t entrypoint_name_hash, pinvoke_api_map_ptr api_map, bool need_lock) noexcept
{
	auto iter = api_map->find (entrypoint_name, entrypoint_name_hash);
	if (iter != api_map->end () && iter->second != nullptr) {
		return iter->second;
	}

	if (!need_lock) {
		return load_library_entry (library_name, entrypoint_name, api_map);
	}

	StartupAwareLock lock (pinvoke_map_write_lock);
	return load_library_entry (library_name, entrypoint_name, api_map);
}

force_inline PinvokeEntry*
MonodroidRuntime::find_pinvoke_address (hash_t hash, const PinvokeEntry *entries, size_t entry_count) noexcept
{
	while (entry_count > 0) {
		const PinvokeEntry *ret = entries + (entry_count / 2);

		std::strong_ordering result = hash <=> ret->hash;
		if (result < 0) {
			entry_count /= 2;
		} else if (result > 0) {
			entries = ret + 1;
			entry_count -= entry_count / 2 + 1;
		} else {
			return const_cast<PinvokeEntry*>(ret);
		}
	}

	return nullptr;
}

force_inline void*
MonodroidRuntime::handle_other_pinvoke_request (const char *library_name, hash_t library_name_hash, const char *entrypoint_name, hash_t entrypoint_name_hash) noexcept
{
	std::string lib_name {library_name};
	std::string entry_name {entrypoint_name};

	auto iter = other_pinvoke_map.find (lib_name, library_name_hash);
	void *handle = nullptr;
	if (iter == other_pinvoke_map.end ()) {
		StartupAwareLock lock (pinvoke_map_write_lock);

		pinvoke_api_map_ptr lib_map;
		// Make sure some other thread hasn't just added the map
		iter = other_pinvoke_map.find (lib_name, library_name_hash);
		if (iter == other_pinvoke_map.end () || iter->second == nullptr) {
			lib_map = new pinvoke_api_map (1);
			other_pinvoke_map[lib_name] = lib_map;
		} else {
			lib_map = iter->second;
		}

		handle = fetch_or_create_pinvoke_map_entry (lib_name, entry_name, entrypoint_name_hash, lib_map, /* need_lock */ false);
	} else {
		if (iter->second == nullptr) [[unlikely]] {
			log_warn (LOG_ASSEMBLY, "Internal error: null entry in p/invoke map for key '%s'", library_name);
			return nullptr; // fall back to `monodroid_dlopen`
		}

		handle = fetch_or_create_pinvoke_map_entry (lib_name, entry_name, entrypoint_name_hash, iter->second, /* need_lock */ true);
	}

	return handle;
}

void*
MonodroidRuntime::monodroid_pinvoke_override (const char *library_name, const char *entrypoint_name)
{
	if (library_name == nullptr || entrypoint_name == nullptr) {
		return nullptr;
	}

	hash_t library_name_hash = xxhash::hash (library_name, strlen (library_name));
	hash_t entrypoint_hash = xxhash::hash (entrypoint_name, strlen (entrypoint_name));

	if (library_name_hash == java_interop_library_hash || library_name_hash == xa_internal_api_library_hash) {
		PinvokeEntry *entry = find_pinvoke_address (entrypoint_hash, internal_pinvokes, internal_pinvokes_count);

		if (entry == nullptr) [[unlikely]] {
			log_fatal (LOG_ASSEMBLY, "Internal p/invoke symbol '%s @ %s' (hash: 0x%zx) not found in compile-time map.", library_name, entrypoint_name, entrypoint_hash);
			log_fatal (LOG_ASSEMBLY, "compile-time map contents:");
			for (size_t i = 0; i < internal_pinvokes_count; i++) {
				PinvokeEntry const& e = internal_pinvokes[i];
				log_fatal (LOG_ASSEMBLY, "\t'%s'=%p (hash: 0x%zx)", e.name, e.func, e.hash);
			}
			Helpers::abort_application ();
		}

		return entry->func;
	}

	// The order of statements below should be kept in the descending probability of occurrence order (as much as
	// possible, of course). `libSystem.Native` is requested during early startup for each MAUI app, so its
	// probability is higher, just as it's more likely that `libSystem.Security.Cryptography.Android` will be used
	// in an app rather than `libSystem.IO.Compression.Native`
	void **dotnet_dso_handle; // Set to a non-null value only for dotnet shared libraries
	if (library_name_hash == system_native_library_hash) {
		dotnet_dso_handle = &system_native_library_handle;
	} else if (library_name_hash == system_security_cryptography_native_android_library_hash) {
		dotnet_dso_handle = &system_security_cryptography_native_android_library_handle;
	} else if (library_name_hash == system_io_compression_native_library_hash) {
		dotnet_dso_handle = &system_io_compression_native_library_handle;
	} else {
		dotnet_dso_handle = nullptr;
	}

	if (dotnet_dso_handle != nullptr) {
		PinvokeEntry *entry = find_pinvoke_address (entrypoint_hash, dotnet_pinvokes, dotnet_pinvokes_count);
		if (entry != nullptr) {
			if (entry->func != nullptr) {
				return entry->func;
			}

			load_library_entry (library_name, entrypoint_name, *entry, dotnet_dso_handle);
			if (entry->func == nullptr) {
				log_fatal (LOG_ASSEMBLY, "Failed to load symbol '%s' from shared library '%s'", entrypoint_name, library_name);
				return nullptr; // let Mono deal with the fallout
			}

			return entry->func;
		}

		// It's possible we don't have an entry for some `dotnet` p/invoke, fall back to the slow path below
		log_debug (LOG_ASSEMBLY, "Symbol '%s' in library '%s' not found in the generated tables, falling back to slow path", entrypoint_name, library_name);
	}

	return handle_other_pinvoke_request (library_name, library_name_hash, entrypoint_name, entrypoint_hash);
}
