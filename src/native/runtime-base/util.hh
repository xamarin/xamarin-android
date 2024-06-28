// This is a -*- C++ -*- header
#ifndef __MONODROID_UTIL_H__
#define __MONODROID_UTIL_H__

#ifndef TRUE
#ifdef __cplusplus
static inline constexpr int TRUE = 1;
#else
#define TRUE 1
#endif // __cplusplus
#endif

#ifndef FALSE
#ifdef __cplusplus
static inline constexpr int FALSE = 0;
#else
#define FALSE 0
#endif // __cplusplus
#endif

#include <array>
#include <cstdarg>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <optional>
#include <unistd.h>
#include <sys/stat.h>
#include <dirent.h>
#include <sys/time.h>

#include <jni.h>

#include <mono/metadata/assembly.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/class.h>
#include <mono/metadata/threads.h>
#include <mono/metadata/mono-private-unstable.h>
#include <mono/utils/mono-publib.h>

#include "jni-wrappers.hh"
#include "java-interop-util.h"
#include "logger.hh"
#include "strings.hh"

#ifdef __cplusplus
namespace xamarin::android
{
	class Util
	{
		static constexpr std::array<char, 16> hex_chars = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };
		static constexpr uint32_t ms_in_nsec = 1000000ULL;

	public:
		static void initialize () noexcept;

		static int monodroid_getpagesize () noexcept
		{
			return page_size;
		}

		static MonoAssembly    *monodroid_load_assembly (MonoDomain *domain, const char *basename);
		static MonoAssembly    *monodroid_load_assembly (MonoAssemblyLoadContextGCHandle alc_handle, const char *basename);
		static MonoClass       *monodroid_get_class_from_name (MonoDomain *domain, const char* assembly, const char *_namespace, const char *type);
		static int              send_uninterrupted (int fd, void *buf, size_t len);
		static ssize_t          recv_uninterrupted (int fd, void *buf, size_t len);
		static FILE            *monodroid_fopen (const char* filename, const char* mode);
		static int              monodroid_dirent_hasextension (dirent *e, const char *extension);
		static void             monodroid_strfreev (char **str_array);
		static char           **monodroid_strsplit (const char *str, const char *delimiter, size_t max_tokens);
		static char            *monodroid_strdup_printf (const char *format, ...);
		static char            *monodroid_strdup_vprintf (const char *format, va_list vargs);
		static char*            path_combine (const char *path1, const char *path2);
		static void             create_public_directory (const char *dir);
		static int              create_directory (const char *pathname, mode_t mode);
		static void             set_world_accessable (const char *path);
		static void             set_user_executable (const char *path);
		static bool             file_exists (const char *file);
		static bool             directory_exists (const char *directory);
		static bool             file_copy (const char *to, const char *from);

		static std::optional<size_t> get_file_size_at (int dirfd, const char *file_name) noexcept
		{
			struct stat sbuf;
			if (fstatat (dirfd, file_name, &sbuf, 0) == -1) {
				log_warn (LOG_ASSEMBLY, "Failed to stat file '%s': %s", file_name, std::strerror (errno));
				return std::nullopt;
			}

			return static_cast<size_t>(sbuf.st_size);
		}

		static std::optional<int> open_file_ro_at (int dirfd, const char *file_name) noexcept
		{
			int fd =  openat (dirfd, file_name, O_RDONLY);
			if (fd < 0) {
				log_error (LOG_ASSEMBLY, "Failed to open file '%s' for reading: %s", file_name, std::strerror (errno));
				return std::nullopt;
			}

			return fd;
		}

		// Make sure that `buf` has enough space! This is by design, the methods are supposed to be fast.
		template<size_t MaxStackSpace, typename TBuffer>
		static void path_combine (TBuffer& buf, const char* path1, const char* path2) noexcept
		{
			path_combine (buf, path1, path1 == nullptr ? 0 : strlen (path1), path2, path2 == nullptr ? 0 : strlen (path2));
		}

		// internal::static_local_string<MaxStackSpace>
		template<size_t MaxStackSpace, typename TBuffer>
		static void path_combine (TBuffer& buf, const char* path1, size_t path1_len, const char* path2, size_t path2_len) noexcept
		{
			abort_unless (path1 != nullptr || path2 != nullptr, "At least one path must be a valid pointer");

			if (path1 == nullptr) {
				buf.append_c (path2);
				return;
			}

			if (path2 == nullptr) {
				buf.append_c (path1);
				return;
			}

			buf.append (path1, path1_len);
			buf.append ("/");
			buf.append (path2, path2_len);
		}

		template<size_t MaxStackSpace>
		static void path_combine (internal::static_local_string<MaxStackSpace>& buf, const char* path1, const char* path2) noexcept
		{
			path_combine <MaxStackSpace, decltype(buf)> (buf, path1, path2);
		}

		template<size_t MaxStackSpace>
		static void path_combine (internal::static_local_string<MaxStackSpace>& buf, const char* path1, size_t path1_len, const char* path2, size_t path2_len) noexcept
		{
			path_combine <MaxStackSpace, decltype(buf)> (buf, path1, path1_len, path2, path2_len);
		}

		template<size_t MaxStackSpace>
		static void path_combine (internal::dynamic_local_string<MaxStackSpace>& buf, const char* path1, const char* path2) noexcept
		{
			path_combine <MaxStackSpace, decltype(buf)> (buf, path1, path2);
		}

		template<size_t MaxStackSpace>
		static void path_combine (internal::dynamic_local_string<MaxStackSpace>& buf, const char* path1, size_t path1_len, const char* path2, size_t path2_len) noexcept
		{
			path_combine <MaxStackSpace, decltype(buf)> (buf, path1, path1_len, path2, path2_len);
		}

		static char* path_combine (const char *path1, std::string_view const& path2) noexcept
		{
			return path_combine (path1, path2.data ());
		}

		static bool ends_with_slow (const char *str, const char *end) noexcept
		{
			char *p = const_cast<char*> (strstr (str, end));
			return p != nullptr && p [strlen (end)] == '\0';
		}

		template<size_t MaxStackSpace>
		static bool ends_with (internal::dynamic_local_string<MaxStackSpace> const& str, std::string_view const& sv) noexcept
		{
			if (str.length () < sv.length ()) {
				return false;
			}

			return memcmp (str.get () + str.length () - sv.length (), sv.data (), sv.length ()) == 0;
		}

		static bool ends_with (const char *str, std::string_view const& sv) noexcept
		{
			size_t len = strlen (str);
			if (len < sv.length ()) {
				return false;
			}

			return memcmp (str + len - sv.length (), sv.data (), sv.length ()) == 0;
		}

		template<size_t N>
		static bool ends_with (const char *str, const char (&end)[N])
		{
			char *p = const_cast<char*> (strstr (str, end));
			return p != nullptr && p [N - 1] == '\0';
		}

		template<size_t N>
		static bool ends_with (const char *str, std::array<char, N> const& end) noexcept
		{
			char *p = const_cast<char*> (strstr (str, end.data ()));
			return p != nullptr && p [N - 1] == '\0';
		}

		template<size_t N>
		static bool ends_with (const char *str, helper_char_array<N> const& end) noexcept
		{
			char *p = const_cast<char*> (strstr (str, end.data ()));
			return p != nullptr && p [N - 1] == '\0';
		}

		template<size_t N, size_t MaxStackSize, typename TStorage, typename TChar = char>
		static bool ends_with (internal::string_base<MaxStackSize, TStorage, TChar> const& str, const char (&end)[N]) noexcept
		{
			constexpr size_t end_length = N - 1;

			size_t len = str.length ();
			if (len < end_length) [[unlikely]] {
				return false;
			}

			return memcmp (str.get () + len - end_length, end, end_length) == 0;
		}

		template<size_t N, size_t MaxStackSize, typename TStorage, typename TChar = char>
		static bool ends_with (internal::string_base<MaxStackSize, TStorage, TChar> const& str, std::array<TChar, N> const& end) noexcept
		{
			constexpr size_t end_length = N - 1;

			size_t len = str.length ();
			if (len < end_length) [[unlikely]] {
				return false;
			}

			return memcmp (str.get () + len - end_length, end.data (), end_length) == 0;
		}

		template<size_t N, size_t MaxStackSize, typename TStorage, typename TChar = char>
		static bool ends_with (internal::string_base<MaxStackSize, TStorage, TChar> const& str, helper_char_array<N> const& end) noexcept
		{
			constexpr size_t end_length = N - 1;

			size_t len = str.length ();
			if (len < end_length) [[unlikely]] {
				return false;
			}

			return memcmp (str.get () + len - end_length, end.data (), end_length) == 0;
		}

		template<size_t MaxStackSize, typename TStorage, typename TChar = char>
		static const TChar* find_last (internal::string_base<MaxStackSize, TStorage, TChar> const& str, const char ch) noexcept
		{
			if (str.empty ()) {
				return nullptr;
			}

			for (size_t i = str.length (); i > 0; i--) {
				const size_t index = i - 1;
				if (str[index] == ch) {
					return str.get () + index;
				}
			}

			return nullptr;
		}

		static void *xmalloc (size_t size) noexcept
		{
			return ::xmalloc (size);
		}

		static void *xrealloc (void *ptr, size_t size) noexcept
		{
			return ::xrealloc (ptr, size);
		}

		static void *xcalloc (size_t nmemb, size_t size) noexcept
		{
			return ::xcalloc (nmemb, size);
		}

		static char *strdup_new (const char* s, size_t len) noexcept
		{
			if (len == 0 || s == nullptr) [[unlikely]] {
				return nullptr;
			}

			size_t alloc_size = Helpers::add_with_overflow_check<size_t> (len, 1);
			auto ret = new char[alloc_size];
			memcpy (ret, s, len);
			ret[len] = '\0';

			return ret;
		}

		static char *strdup_new (const char* s) noexcept
		{
			if (s == nullptr) [[unlikely]] {
				return nullptr;
			}

			return strdup_new (s, strlen (s));
		}

		template<size_t BufferSize>
		static char *strdup_new (internal::dynamic_local_string<BufferSize> const& buf) noexcept
		{
			return strdup_new (buf.get (), buf.length ());
		}

		static char *strdup_new (xamarin::android::internal::string_segment const& s, size_t from_index = 0) noexcept
		{
			if (from_index >= s.length ()) {
				return nullptr;
			}

			return strdup_new (s.start () + from_index, s.length () - from_index);
		}

		template<typename CharType = char, typename ...Strings>
		static char* string_concat (const char *s1, const CharType* s2, Strings... strings) noexcept
		{
			assert_char_type<CharType> ();

			size_t len = calculate_length (s1, s2, strings...);

			char *ret = new char [len + 1];
			*ret = '\0';

			concatenate_strings_into (len, ret, s1, s2, strings...);

			return ret;
		}

		static bool is_path_rooted (const char *path) noexcept;

		template<typename CharType = char>
		static size_t calculate_length (const CharType* s) noexcept
		{
			return strlen (s);
		}

		template<typename CharType = char, typename ...Strings>
		static size_t calculate_length (const CharType* s1, Strings... strings) noexcept
		{
			assert_char_type<CharType> ();

			return strlen (s1) + calculate_length (strings...);
		}

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

	protected:
		template<typename CharType = char, typename ...Strings>
		static constexpr void concatenate_strings_into ([[maybe_unused]] size_t len, [[maybe_unused]] char *dest) noexcept
		{}

		template<typename CharType = char, typename ...Strings>
		static constexpr void concatenate_strings_into (size_t len, char *dest, const CharType* s1, Strings... strings) noexcept
		{
			assert_char_type<CharType> ();

			strcat (dest, s1);
			concatenate_strings_into (len, dest, strings...);
		}

		static int make_directory (const char *path, [[maybe_unused]] mode_t mode) noexcept
		{
			return ::mkdir (path, mode);
		}

	private:
		template<typename CharType>
		static constexpr void assert_char_type ()
		{
			static_assert (std::is_same_v<CharType, char>, "CharType must be an 8-bit character type");
		}

	private:
		static inline int page_size;
	};
}
#endif // __cplusplus
#endif /* __MONODROID_UTIL_H__ */
