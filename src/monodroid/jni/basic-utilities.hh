#ifndef __BASIC_UTILITIES_HH
#define __BASIC_UTILITIES_HH

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstdio>
#include <cstring>
#include <type_traits>

#include <unistd.h>

#include <sys/stat.h>
#include <sys/types.h>
#include <dirent.h>

#include "java-interop-util.h"
#include "helpers.hh"
#include "cpp-util.hh"
#include "platform-compat.hh"
#include "strings.hh"

namespace xamarin::android
{
	class BasicUtilities
	{
	public:
		FILE            *monodroid_fopen (const char* filename, const char* mode);
		int              monodroid_stat (const char *path, monodroid_stat_t *s);
		monodroid_dir_t *monodroid_opendir (const char *filename);
		int              monodroid_closedir (monodroid_dir_t *dirp);
		int              monodroid_dirent_hasextension (monodroid_dirent_t *e, const char *extension);
		void             monodroid_strfreev (char **str_array);
		char           **monodroid_strsplit (const char *str, const char *delimiter, size_t max_tokens);
		char            *monodroid_strdup_printf (const char *format, ...);
		char            *monodroid_strdup_vprintf (const char *format, va_list vargs);
		char*            path_combine (const char *path1, const char *path2);
		void             create_public_directory (const char *dir);
		int              create_directory (const char *pathname, mode_t mode);
		void             set_world_accessable (const char *path);
		void             set_user_executable (const char *path);
		bool             file_exists (const char *file);
		bool             directory_exists (const char *directory);
		bool             file_copy (const char *to, const char *from);


		// Make sure that `buf` has enough space! This is by design, the methods are supposed to be fast.
		template<size_t MaxStackSpace, typename TBuffer>
		void path_combine (TBuffer& buf, const char* path1, const char* path2) noexcept
		{
			path_combine (buf, path1, path1 == nullptr ? 0 : strlen (path1), path2, path2 == nullptr ? 0 : strlen (path2));
		}

		// internal::static_local_string<MaxStackSpace>
		template<size_t MaxStackSpace, typename TBuffer>
		void path_combine (TBuffer& buf, const char* path1, size_t path1_len, const char* path2, size_t path2_len) noexcept
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
			buf.append (MONODROID_PATH_SEPARATOR);
			buf.append (path2, path2_len);
		}

		template<size_t MaxStackSpace>
		void path_combine (internal::static_local_string<MaxStackSpace>& buf, const char* path1, const char* path2) noexcept
		{
			path_combine <MaxStackSpace, decltype(buf)> (buf, path1, path2);
		}

		template<size_t MaxStackSpace>
		void path_combine (internal::static_local_string<MaxStackSpace>& buf, const char* path1, size_t path1_len, const char* path2, size_t path2_len) noexcept
		{
			path_combine <MaxStackSpace, decltype(buf)> (buf, path1, path1_len, path2, path2_len);
		}

		template<size_t MaxStackSpace>
		void path_combine (internal::dynamic_local_string<MaxStackSpace>& buf, const char* path1, const char* path2) noexcept
		{
			path_combine <MaxStackSpace, decltype(buf)> (buf, path1, path2);
		}

		template<size_t MaxStackSpace>
		void path_combine (internal::dynamic_local_string<MaxStackSpace>& buf, const char* path1, size_t path1_len, const char* path2, size_t path2_len) noexcept
		{
			path_combine <MaxStackSpace, decltype(buf)> (buf, path1, path1_len, path2, path2_len);
		}

		bool ends_with_slow (const char *str, const char *end)
		{
			char *p = const_cast<char*> (strstr (str, end));
			return p != nullptr && p [strlen (end)] == '\0';
		}

		template<size_t N>
		bool ends_with (const char *str, const char (&end)[N])
		{
			char *p = const_cast<char*> (strstr (str, end));
			return p != nullptr && p [N - 1] == '\0';
		}

		template<size_t N>
		bool ends_with (const char *str, std::array<char, N> const& end) const noexcept
		{
			char *p = const_cast<char*> (strstr (str, end.data ()));
			return p != nullptr && p [N - 1] == '\0';
		}

		template<size_t N>
		bool ends_with (const char *str, helper_char_array<N> const& end) const noexcept
		{
			char *p = const_cast<char*> (strstr (str, end.data ()));
			return p != nullptr && p [N - 1] == '\0';
		}

		template<size_t N, size_t MaxStackSize, typename TStorage, typename TChar = char>
		bool ends_with (internal::string_base<MaxStackSize, TStorage, TChar> const& str, const char (&end)[N]) const noexcept
		{
			constexpr size_t end_length = N - 1;

			size_t len = str.length ();
			if (XA_UNLIKELY (len < end_length)) {
				return false;
			}

			return memcmp (str.get () + len - end_length, end, end_length) == 0;
		}

		template<size_t N, size_t MaxStackSize, typename TStorage, typename TChar = char>
		bool ends_with (internal::string_base<MaxStackSize, TStorage, TChar> const& str, std::array<TChar, N> const& end) const noexcept
		{
			constexpr size_t end_length = N - 1;

			size_t len = str.length ();
			if (XA_UNLIKELY (len < end_length)) {
				return false;
			}

			return memcmp (str.get () + len - end_length, end.data (), end_length) == 0;
		}

		template<size_t N, size_t MaxStackSize, typename TStorage, typename TChar = char>
		bool ends_with (internal::string_base<MaxStackSize, TStorage, TChar> const& str, helper_char_array<N> const& end) const noexcept
		{
			constexpr size_t end_length = N - 1;

			size_t len = str.length ();
			if (XA_UNLIKELY (len < end_length)) {
				return false;
			}

			return memcmp (str.get () + len - end_length, end.data (), end_length) == 0;
		}

		template<size_t MaxStackSize, typename TStorage, typename TChar = char>
		const TChar* find_last (internal::string_base<MaxStackSize, TStorage, TChar> const& str, const char ch) const noexcept
		{
			if (str.empty ()) {
				return nullptr;
			}

			for (size_t i = str.length () - 1; i >= 0; i--) {
				if (str[i] == ch) {
					return str.get () + i;
				}
			}

			return nullptr;
		}

		void *xmalloc (size_t size)
		{
			return ::xmalloc (size);
		}

		void *xrealloc (void *ptr, size_t size)
		{
			return ::xrealloc (ptr, size);
		}

		void *xcalloc (size_t nmemb, size_t size)
		{
			return ::xcalloc (nmemb, size);
		}

		char *strdup_new (const char* s, size_t len)
		{
			if (XA_UNLIKELY (len == 0 || s == nullptr)) {
				return nullptr;
			}

			size_t alloc_size = ADD_WITH_OVERFLOW_CHECK (size_t, len, 1);
			auto ret = new char[alloc_size];
			memcpy (ret, s, len);
			ret[len] = '\0';

			return ret;
		}

		char *strdup_new (const char* s)
		{
			if (XA_UNLIKELY (s == nullptr)) {
				return nullptr;
			}

			return strdup_new (s, strlen (s));
		}

		char *strdup_new (xamarin::android::internal::string_segment const& s, size_t from_index = 0) noexcept
		{
			if (from_index >= s.length ()) {
				return nullptr;
			}

			return strdup_new (s.start () + from_index, s.length () - from_index);
		}

		template<typename CharType = char, typename ...Strings>
		char* string_concat (const char *s1, const CharType* s2, Strings... strings)
		{
			assert_char_type<CharType> ();

			size_t len = calculate_length (s1, s2, strings...);

			char *ret = new char [len + 1];
			*ret = '\0';

			concatenate_strings_into (len, ret, s1, s2, strings...);

			return ret;
		}

		bool            is_path_rooted (const char *path);

		template<typename CharType = char>
		size_t calculate_length (const CharType* s)
		{
			return strlen (s);
		}

		template<typename CharType = char, typename ...Strings>
		size_t calculate_length (const CharType* s1, Strings... strings)
		{
			assert_char_type<CharType> ();

			return strlen (s1) + calculate_length (strings...);
		}

	protected:
		template<typename CharType = char, typename ...Strings>
		void concatenate_strings_into ([[maybe_unused]] size_t len, [[maybe_unused]] char *dest)
		{}

		template<typename CharType = char, typename ...Strings>
		void concatenate_strings_into (size_t len, char *dest, const CharType* s1, Strings... strings)
		{
			assert_char_type<CharType> ();

			strcat (dest, s1);
			concatenate_strings_into (len, dest, strings...);
		}

		int make_directory (const char *path, [[maybe_unused]] mode_t mode)
		{
			return mkdir (path, mode);
		}

	private:
		template<typename CharType>
		static constexpr void assert_char_type ()
		{
			static_assert (std::is_same_v<CharType, char>, "CharType must be an 8-bit character type");
		}
	};
}
#endif // !__BASIC_UTILITIES_HH
