#include <cstdlib>
#include <cstdarg>
#include <cstdio>
#include <cerrno>
#ifndef WINDOWS
#include <sys/socket.h>
#else
#include <winsock2.h>
#include <shlwapi.h>
#endif
#include <sys/stat.h>
#include <sys/types.h>
#include <cstring>
#ifdef HAVE_BSD_STRING_H
#include <bsd/string.h>
#endif

#ifdef WINDOWS
#include <direct.h>
#endif

#include <mono/metadata/appdomain.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/class.h>

#include "java-interop-util.h"

#include "monodroid.h"
#include "util.hh"
#include "globals.hh"
#include "monodroid-glue.hh"
#include "cpp-util.hh"
#include "timing-internal.hh"

using namespace xamarin::android;
using namespace xamarin::android::internal;

void timing_point::mark ()
{
	FastTiming::get_time (sec, ns);
}

timing_diff::timing_diff (const timing_period &period)
{
	FastTiming::calculate_interval (period.start, period.end, *this);
}

void
Util::initialize () noexcept
{
#ifndef WINDOWS
	page_size = getpagesize ();
#else   // defined(WINDOWS)
	SYSTEM_INFO info;
	GetSystemInfo (&info);
	page_size = info.dwPageSize;
#endif  // defined(WINDOWS)
}

int
Util::send_uninterrupted (int fd, void *buf, size_t len) noexcept
{
	ssize_t res;
#ifdef WINDOWS
	const char *buffer = static_cast<const char*> (buf);
#else
	void *buffer = buf;
#endif
	do {
		res = send (
			fd,
			buffer,
#ifdef WINDOWS
			static_cast<int>(len),
#else
			len,
#endif
			0
		);
	} while (res == -1 && errno == EINTR);

	return static_cast<size_t>(res) == len;
}

ssize_t
Util::recv_uninterrupted (int fd, void *buf, size_t len) noexcept
{
#if defined (WINDOWS)
	using nbytes_type = int;
#else
	using nbytes_type = size_t;
#endif
	ssize_t res;
	size_t total = 0;
	int flags = 0;
	nbytes_type nbytes;

	do {
		nbytes = static_cast<nbytes_type>(len - total);
		res = recv (fd, (char *) buf + total, nbytes, flags);

		if (res > 0)
			total += static_cast<size_t>(res);
	} while ((res > 0 && total < len) || (res == -1 && errno == EINTR));

	return static_cast<ssize_t>(total);
}

template<typename IdxType>
inline void
Util::package_hash_to_hex (IdxType /* idx */) noexcept
{
	package_property_suffix[sizeof (package_property_suffix) / sizeof (char) - 1] = 0x00;
}

template<typename IdxType, typename ...Indices>
inline void
Util::package_hash_to_hex (uint32_t hash, IdxType idx, Indices... indices) noexcept
{
	package_property_suffix [idx] = hex_chars [(hash & (0xF0000000 >> idx * 4)) >> ((7 - idx) * 4)];
	package_hash_to_hex <IdxType> (hash, indices...);
}

void
Util::monodroid_store_package_name (const char *name) noexcept
{
	if (!name || *name == '\0')
		return;

	/* Android properties can be at most 32 bytes long (!) and so we mustn't append the package name
	 * as-is since it will most likely generate conflicts (packages tend to be named
	 * com.mycompany.app), so we simply generate a hash code and use that instead. We treat the name
	 * as a stream of bytes assumming it's an ASCII string using a simplified version of the hash
	 * algorithm used by BCL's String.GetHashCode ()
	 */
	const char *ch = name;
	uint32_t hash = 0;
	while (*ch)
		hash = (hash << 5) - (hash + static_cast<uint32_t>(*ch++));

	// In C++14 or newer we could use std::index_sequence, but in C++11 it's a bit too much ado
	// for this simple case, so a manual sequence it is.
	//
	// And yes, I know it could be done in a simple loop or in even simpler 8 lines of code, but
	// that would be boring, wouldn't it? :)
	package_hash_to_hex (hash, 0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u);
	log_info (LOG_DEFAULT, "Generated hash 0x%s for package name %s", package_property_suffix, name);
}

#if defined (NET)
MonoAssembly*
Util::monodroid_load_assembly (MonoAssemblyLoadContextGCHandle alc_handle, const char *basename) noexcept
{
	MonoImageOpenStatus  status;
	MonoAssemblyName    *aname = mono_assembly_name_new (basename);
	MonoAssembly        *assm = mono_assembly_load_full_alc (alc_handle, aname, nullptr, &status);

	mono_assembly_name_free (aname);

	if (assm == nullptr || status != MonoImageOpenStatus::MONO_IMAGE_OK) {
		log_fatal (LOG_DEFAULT, "Unable to find assembly '%s'.", basename);
		exit (FATAL_EXIT_MISSING_ASSEMBLY);
	}
	return assm;
}
#endif // def NET

MonoAssembly *
Util::monodroid_load_assembly (MonoDomain *domain, const char *basename) noexcept
{
	MonoAssembly         *assm;
	MonoAssemblyName     *aname;
	MonoImageOpenStatus   status;

	aname = mono_assembly_name_new (basename);
	MonoDomain *current = get_current_domain ();

	if (domain != current) {
		mono_domain_set (domain, FALSE);
		assm  = mono_assembly_load_full (aname, nullptr, &status, 0);
		mono_domain_set (current, FALSE);
	} else {
		assm  = mono_assembly_load_full (aname, nullptr, &status, 0);
	}

	mono_assembly_name_free (aname);

	if (!assm) {
		log_fatal (LOG_DEFAULT, "Unable to find assembly '%s'.", basename);
		exit (FATAL_EXIT_MISSING_ASSEMBLY);
	}
	return assm;
}

#if !defined (NET)
MonoObject *
Util::monodroid_runtime_invoke (MonoDomain *domain, MonoMethod *method, void *obj, void **params, MonoObject **exc) noexcept
{
	MonoDomain *current = get_current_domain ();
	if (domain == current) {
		return mono_runtime_invoke (method, obj, params, exc);
	}

	mono_domain_set (domain, FALSE);
	MonoObject *r = mono_runtime_invoke (method, obj, params, exc);
	mono_domain_set (current, FALSE);
	return r;
}

void
Util::monodroid_property_set (MonoDomain *domain, MonoProperty *property, void *obj, void **params, MonoObject **exc) noexcept
{
	MonoDomain *current = get_current_domain ();
	if (domain == current) {
		mono_property_set_value (property, obj, params, exc);
		return;
	}

	mono_domain_set (domain, FALSE);
	mono_property_set_value (property, obj, params, exc);
	mono_domain_set (current, FALSE);
}

MonoDomain*
Util::monodroid_create_appdomain (MonoDomain *parent_domain, const char *friendly_name, int shadow_copy, const char *shadow_directories) noexcept
{
	MonoClass *appdomain_setup_klass = monodroid_get_class_from_name (parent_domain, "mscorlib", "System", "AppDomainSetup");
	MonoClass *appdomain_klass = monodroid_get_class_from_name (parent_domain, "mscorlib", "System", "AppDomain");
	MonoMethod *create_domain = mono_class_get_method_from_name (appdomain_klass, "CreateDomain", 3);
	MonoProperty *shadow_copy_prop = mono_class_get_property_from_name (appdomain_setup_klass, "ShadowCopyFiles");
	MonoProperty *shadow_copy_dirs_prop = mono_class_get_property_from_name (appdomain_setup_klass, "ShadowCopyDirectories");

	MonoObject *setup = mono_object_new (parent_domain, appdomain_setup_klass);
	MonoString *mono_friendly_name = mono_string_new (parent_domain, friendly_name);
	MonoString *mono_shadow_copy = mono_string_new (parent_domain, shadow_copy ? "true" : "false");
	MonoString *mono_shadow_copy_dirs = shadow_directories == nullptr ? nullptr : mono_string_new (parent_domain, shadow_directories);

	monodroid_property_set (parent_domain, shadow_copy_prop, setup, reinterpret_cast<void**> (&mono_shadow_copy), nullptr);
	if (mono_shadow_copy_dirs != nullptr)
		monodroid_property_set (parent_domain, shadow_copy_dirs_prop, setup, reinterpret_cast<void**> (&mono_shadow_copy_dirs), nullptr);

	void *args[3] = { mono_friendly_name, nullptr, setup };
	auto appdomain = reinterpret_cast<MonoAppDomain*>(monodroid_runtime_invoke (parent_domain, create_domain, nullptr, args, nullptr));
	if (appdomain == nullptr)
		return nullptr;

	return mono_domain_from_appdomain (appdomain);
}
#endif // ndef NET

MonoClass*
Util::monodroid_get_class_from_name ([[maybe_unused]] MonoDomain *domain, const char* assembly, const char *_namespace, const char *type) noexcept
{
#if !defined (NET)
	MonoDomain *current = get_current_domain ();

	if (domain != current)
		mono_domain_set (domain, FALSE);
#endif // ndef NET

	MonoClass *result;
	MonoAssemblyName *aname = mono_assembly_name_new (assembly);
	MonoAssembly *assm = mono_assembly_loaded (aname);
	if (assm != nullptr) {
		MonoImage *image = mono_assembly_get_image (assm);
		result = mono_class_from_name (image, _namespace, type);
	} else
		result = nullptr;

#if !defined (NET)
	if (domain != current)
		mono_domain_set (current, FALSE);
#endif // ndef NET

	mono_assembly_name_free (aname);

	return result;
}

#if !defined (NET)
MonoClass*
Util::monodroid_get_class_from_image (MonoDomain *domain, MonoImage *image, const char *_namespace, const char *type) noexcept
{
	MonoDomain *current = get_current_domain ();

	if (domain != current)
		mono_domain_set (domain, FALSE);

	MonoClass *result = mono_class_from_name (image, _namespace, type);

	if (domain != current)
		mono_domain_set (current, FALSE);

	return result;
}
#endif // ndef NET

jclass
Util::get_class_from_runtime_field (JNIEnv *env, jclass runtime, const char *name, bool make_gref) noexcept
{
	static constexpr char java_lang_class_sig[] = "Ljava/lang/Class;";

	jfieldID fieldID = env->GetStaticFieldID (runtime, name, java_lang_class_sig);
	if (fieldID == nullptr)
		return nullptr;

	jobject field = env->GetStaticObjectField (runtime, fieldID);
	if (field == nullptr)
		return nullptr;

	return reinterpret_cast<jclass> (make_gref ? OSBridge::lref_to_gref (env, field) : field);
}
