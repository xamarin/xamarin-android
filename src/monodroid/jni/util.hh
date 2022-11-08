// This is a -*- C++ -*- header
#ifndef __MONODROID_UTIL_H__
#define __MONODROID_UTIL_H__

#ifndef TRUE
#ifdef __cplusplus
constexpr int TRUE = 1;
#else
#define TRUE 1
#endif // __cplusplus
#endif

#ifndef FALSE
#ifdef __cplusplus
constexpr int FALSE = 0;
#else
#define FALSE 0
#endif // __cplusplus
#endif

#include <stdlib.h>
#include <string.h>
#ifdef HAVE_BSD_STRING_H
#include <bsd/string.h>
#endif
#include <unistd.h>
#include <sys/stat.h>
#include <dirent.h>
#include <stdarg.h>
#include <time.h>
#include <sys/time.h>
#include <jni.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/threads.h>

#include "monodroid.h"
#include "jni-wrappers.hh"
#ifdef __cplusplus
#include "basic-utilities.hh"
#endif

#if defined (NET)
#include <mono/metadata/mono-private-unstable.h>
#endif // def NET

#include "java-interop-util.h"
#include "logger.hh"

#if !defined (NET)
#ifdef __cplusplus
extern "C" {
#endif // __cplusplus
	MONO_API  void    monodroid_strfreev (char **str_array);
	MONO_API  char  **monodroid_strsplit (const char *str, const char *delimiter, size_t max_tokens);
	MONO_API  char   *monodroid_strdup_printf (const char *format, ...);
	MONO_API  void    monodroid_store_package_name (const char *name);
	MONO_API  int     monodroid_get_namespaced_system_property (const char *name, char **value);
	MONO_API  FILE   *monodroid_fopen (const char* filename, const char* mode);

	MONO_API  int     send_uninterrupted (int fd, void *buf, int len);
	MONO_API  int     recv_uninterrupted (int fd, void *buf, int len);
	MONO_API  void    set_world_accessable (const char *path);
	MONO_API  void    create_public_directory (const char *dir);
	MONO_API  char   *path_combine (const char *path1, const char *path2);
#ifdef __cplusplus
}
#endif // __cplusplus
#endif // NET

#ifdef __cplusplus
namespace xamarin::android
{
	class Util : public BasicUtilities
	{
		static constexpr const char hex_chars [] = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

#if defined (ANDROID) || defined (__linux__) || defined (__linux)
		using timestruct = timespec;
#else
		using timestruct = timeval;
#endif
		static constexpr uint32_t ms_in_nsec = 1000000ULL;

	public:
		static void initialize () noexcept;

		static int monodroid_getpagesize () noexcept
		{
			return page_size;
		}

		static void             monodroid_store_package_name (const char *name) noexcept;
		static MonoAssembly    *monodroid_load_assembly (MonoDomain *domain, const char *basename) noexcept;
#if defined (NET)
		static MonoAssembly    *monodroid_load_assembly (MonoAssemblyLoadContextGCHandle alc_handle, const char *basename) noexcept;
#else // def NET
		static MonoObject      *monodroid_runtime_invoke (MonoDomain *domain, MonoMethod *method, void *obj, void **params, MonoObject **exc) noexcept;
#endif // ndef NET
		static MonoClass       *monodroid_get_class_from_name (MonoDomain *domain, const char* assembly, const char *_namespace, const char *type) noexcept;
#if !defined (NET)
		static MonoDomain      *monodroid_create_appdomain (MonoDomain *parent_domain, const char *friendly_name, int shadow_copy, const char *shadow_directories) noexcept;
		static MonoClass       *monodroid_get_class_from_image (MonoDomain *domain, MonoImage* image, const char *_namespace, const char *type) noexcept;
#endif
		static int              send_uninterrupted (int fd, void *buf, size_t len) noexcept;
		static ssize_t          recv_uninterrupted (int fd, void *buf, size_t len) noexcept;
		static jclass           get_class_from_runtime_field (JNIEnv *env, jclass runtime, const char *name, bool make_gref = false) noexcept;

		static bool should_log (LogCategories category) noexcept
		{
			return (log_categories & category) != 0;
		}

		static MonoDomain *get_current_domain (bool attach_thread_if_needed = true) noexcept
		{
			MonoDomain *ret = mono_domain_get ();
			if (ret != nullptr) {
				return ret;
			}

			// It's likely that we got a nullptr because the current thread isn't attached (see
			// https://github.com/xamarin/xamarin-android/issues/6211), so we need to attach the thread to the root
			// domain
			ret = mono_get_root_domain ();
			if (attach_thread_if_needed) {
				mono_thread_attach (ret);
			}

			return ret;
		}

	private:
		//char *monodroid_strdup_printf (const char *format, va_list vargs);
#if !defined (NET)
		static void  monodroid_property_set (MonoDomain *domain, MonoProperty *property, void *obj, void **params, MonoObject **exc) noexcept;
#endif // ndef NET

		template<typename IdxType>
		static void package_hash_to_hex (IdxType idx) noexcept;

		template<typename IdxType = size_t, typename ...Indices>
		static void package_hash_to_hex (uint32_t hash, IdxType idx, Indices... indices) noexcept;

	private:
		static inline char package_property_suffix[9];
		static inline int page_size;
	};
}
#endif // __cplusplus
#endif /* __MONODROID_UTIL_H__ */
