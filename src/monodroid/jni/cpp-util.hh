#ifndef __CPP_UTIL_HH
#define __CPP_UTIL_HH

#include <array>
#include <cstdarg>
#include <cstdlib>
#include <memory>
#include <type_traits>

#include <semaphore.h>

#include <android/log.h>

#include "cppcompat.hh"
#include "platform-compat.hh"
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
	if (XA_UNLIKELY (!(_condition_))) { \
		do_abort_unless ("%s:%d (%s): " _fmt_, __FILE__, __LINE__, __FUNCTION__, ## __VA_ARGS__); \
	}

#define abort_if_invalid_pointer_argument(_ptr_) abort_unless ((_ptr_) != nullptr, "Parameter '%s' must be a valid pointer", #_ptr_)
#define abort_if_negative_integer_argument(_arg_) abort_unless ((_arg_) > 0, "Parameter '%s' must be larger than 0", #_arg_)

namespace xamarin::android
{
	template <typename T>
	struct CDeleter final
	{
		void operator() (T* p)
		{
			std::free (p);
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
