#ifndef __CPP_UTIL_HH
#define __CPP_UTIL_HH

#include <array>
#include <cstdarg>
#include <cstdlib>
#include <memory>
#include <string_view>
#include <type_traits>

#include <semaphore.h>
#include <android/log.h>

#include "helpers.hh"

static inline void
do_abort_unless (const char* fmt, ...)
{
	va_list ap;

	va_start (ap, fmt);
	__android_log_vprint (ANDROID_LOG_FATAL, "monodroid", fmt, ap);
	va_end (ap);

	xamarin::android::Helpers::abort_application ();
}

#define abort_unless(_condition_, _fmt_, ...) \
	if (!(_condition_)) [[unlikely]] { \
		do_abort_unless ("%s:%d (%s): " _fmt_, __FILE__, __LINE__, __FUNCTION__, ## __VA_ARGS__); \
	}

#define abort_if_invalid_pointer_argument(_ptr_) abort_unless ((_ptr_) != nullptr, "Parameter '%s' must be a valid pointer", #_ptr_)
#define abort_if_negative_integer_argument(_arg_) abort_unless ((_arg_) > 0, "Parameter '%s' must be larger than 0", #_arg_)

// Helpers to use in "printf debugging". Normally not used in code anywhere. No code should be shipped with any
// of the macros present.
#define PD_LOG_LOCATION() log_info_nocheck (LOG_DEFAULT, "loc: %s:%d (%s)", __FILE__, __LINE__, __FUNCTION__)
#define PD_LOG_FUNCTION() log_info_nocheck (LOG_DEFAULT, "%s [%s:%d]", __PRETTY_FUNCTION__, __FILE__, __LINE__)

namespace xamarin::android
{
	template <typename T>
	struct CDeleter final
	{
		using UnderlyingType = std::remove_cv_t<T>;

		void operator() (T* p)
		{
			UnderlyingType *ptr;

			if constexpr (std::is_const_v<T>) {
				ptr = const_cast<std::remove_const_t<T>*> (p);
			} else {
				ptr = p;
			}

			std::free (reinterpret_cast<void*>(ptr));
		}
	};

	template <typename T>
	using c_unique_ptr = std::unique_ptr<T, CDeleter<T>>;

	template<size_t Size>
	struct helper_char_array final
	{
		constexpr char* data () noexcept
		{
			return _elems;
		}

		constexpr const char* data () const noexcept
		{
			return _elems;
		}

		constexpr char const& operator[] (size_t n) const noexcept
		{
			return _elems[n];
		}

		constexpr char& operator[] (size_t n) noexcept
		{
			return _elems[n];
		}

		char _elems[Size]{};
	};

	template<size_t Size>
	using char_array = std::array<char, Size>;

	template<size_t ...Length>
	constexpr auto concat_const (const char (&...parts)[Length])
	{
		// `parts` being constant string arrays, Length for each of them includes the trailing NUL byte, thus the
		// `sizeof... (Length)` part which subtracts the number of template parameters - the amount of NUL bytes so that
		// we don't waste space.
		constexpr size_t total_length = (... + Length) - sizeof... (Length);
		char_array<total_length + 1> ret; // lgtm [cpp/paddingbyteinformationdisclosure] the buffer is filled in the loop below
		ret[total_length] = 0;

		size_t i = 0;
		for (char const* from : {parts...}) {
			for (; *from != '\0'; i++) {
				ret[i] = *from++;
			}
		}

		return ret;
	};

	template<class T>
	concept StringViewPart = std::is_same_v<T, std::string_view>;

	template<size_t TotalLength, StringViewPart ...T>
	consteval auto concat_string_views (T const&... parts)
	{
		std::array<char, TotalLength + 1> ret; // lgtm [cpp/paddingbyteinformationdisclosure] the buffer is filled in the loop below
		ret[TotalLength] = 0;

		size_t i = 0;
		for (std::string_view const& sv : {parts...}) {
			for (const char ch : sv) {
				ret[i] = ch;
				i++;
			}
		}

		return ret;
	}

	consteval size_t calc_size (std::string_view const& sv1) noexcept
	{
		return sv1.size ();
	}

	template<StringViewPart ...T>
	consteval size_t calc_size (std::string_view const& sv1, T const&... other_svs) noexcept
	{
		return sv1.size () + calc_size (other_svs...);
	}

	template <typename TEnum, std::enable_if_t<std::is_enum_v<TEnum>, int> = 0>
	constexpr TEnum operator & (TEnum l, TEnum r) noexcept
	{
		using etype = std::underlying_type_t<TEnum>;
		return static_cast<TEnum>(static_cast<etype>(l) & static_cast<etype>(r));
	}

	template <typename TEnum, std::enable_if_t<std::is_enum_v<TEnum>, int> = 0>
	constexpr TEnum& operator &= (TEnum& l, TEnum r) noexcept
	{
		return l = (l & r);
	}

	template <typename TEnum, std::enable_if_t<std::is_enum_v<TEnum>, int> = 0>
	constexpr TEnum operator | (TEnum l, TEnum r) noexcept
	{
		using etype = std::underlying_type_t<TEnum>;
		return static_cast<TEnum>(static_cast<etype>(l) | static_cast<etype>(r));
	}

	template <typename TEnum, std::enable_if_t<std::is_enum_v<TEnum>, int> = 0>
	constexpr TEnum& operator |= (TEnum& l, TEnum r) noexcept
	{
		return l = (l | r);
	}

	template <typename TEnum, std::enable_if_t<std::is_enum_v<TEnum>, int> = 0>
	constexpr TEnum operator ~ (TEnum r) noexcept
	{
		using etype = std::underlying_type_t<TEnum>;
		return static_cast<TEnum> (~static_cast<etype>(r));
	}

	template <typename TEnum, std::enable_if_t<std::is_enum_v<TEnum>, int> = 0>
	constexpr TEnum operator ^ (TEnum l, TEnum r) noexcept
	{
		using etype = std::underlying_type_t<TEnum>;
		return static_cast<TEnum>(static_cast<etype>(l) ^ static_cast<etype>(r));
	}

	template <typename TEnum, std::enable_if_t<std::is_enum_v<TEnum>, int> = 0>
	constexpr TEnum& operator ^= (TEnum& l, TEnum r) noexcept
	{
		return l = (l ^ r);
	}
}
#endif // !def __CPP_UTIL_HH
