#ifndef __CPP_UTIL_HH
#define __CPP_UTIL_HH

#include <array>
#include <cstdarg>
#include <cstdlib>
#include <memory>
#include <type_traits>

#include <semaphore.h>

#if defined (ANDROID)
#include <android/log.h>
#else
#include <cstdio>
#endif

#include "cppcompat.hh"
#include "platform-compat.hh"

static inline void
do_abort_unless (const char* fmt, ...)
{
	va_list ap;

	va_start (ap, fmt);
#if defined (ANDROID)
	__android_log_vprint (ANDROID_LOG_FATAL, "monodroid", fmt, ap);
#else // def ANDROID
	vfprintf (stderr, fmt, ap);
	fprintf (stderr, "\n");
#endif // ndef ANDROID
	va_end (ap);

	std::abort ();
}

#define abort_unless(_condition_, _fmt_, ...) \
	if (XA_UNLIKELY (!(_condition_))) { \
		do_abort_unless ("%s:%d (%s): " _fmt_, __FILE__, __LINE__, static_cast<const char*>(__FUNCTION__), ## __VA_ARGS__); \
	}

#define abort_if_invalid_pointer_argument(_ptr_) abort_unless ((_ptr_) != nullptr, "Parameter '%s' must be a valid pointer", #_ptr_)
#define abort_if_negative_integer_argument(_arg_) abort_unless ((_arg_) > 0, "Parameter '%s' must be larger than 0", #_arg_)

#if defined (__GNUC__) && !defined (__clang__)

// g++ gets confused about the various timing `*_index` variables being uninitialized, it's analyzer isn't good enough
// to see that we  initialize them when the `if` condition is the same as used here. In theory the diagnostic is correct
// as the value returned from `FastTiming::enabled ()` can change between the call above and the one below, but even if
// we cache result of the first call above in a variable, the warnning is still issued.
#define DEAR_GCC_THIS_VARIABLE_IS_INITIALIZED_START \
	_Pragma ("GCC diagnostic push") \
	_Pragma ("GCC diagnostic ignored \"-Wmaybe-uninitialized\"")

#define DEAR_GCC_THIS_VARIABLE_IS_INITIALIZED_END _Pragma ("GCC diagnostic pop")
#else
#define DEAR_GCC_THIS_VARIABLE_IS_INITIALIZED_START
#define DEAR_GCC_THIS_VARIABLE_IS_INITIALIZED_END
#endif

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

	// MinGW 9 on the CI build bots has a bug in the gcc compiler which causes builds to fail with:
	//
	//  error G713F753E: ‘constexpr auto xamarin::android::concat_const(const char (&)[Length]...) [with long long unsigned int ...Length = {15, 7, 5}]’ called in a constant expression
	//  ...
	//  /usr/lib/gcc/x86_64-w64-mingw32/9.3-win32/include/c++/array:94:12: note: ‘struct std::array<char, 17>’ has no user-provided default constructor
	// struct array
	// ^~~~~
	// /usr/lib/gcc/x86_64-w64-mingw32/9.3-win32/include/c++/array:110:56: note: and the implicitly-defined constructor does not initialize ‘char std::array<char, 17>::_M_elems [17]’
	//  typename _AT_Type::_Type                         _M_elems;
	//                                                   ^~~~~~~~
	//
	// thus we need to use this workaround here
	//
#if defined (__MINGW32__) && __GNUC__ < 10
	template<size_t Size>
	using char_array = helper_char_array<Size>;
#else
	template<size_t Size>
	using char_array = std::array<char, Size>;
#endif

	template<size_t ...Length>
	constexpr auto concat_const (const char (&...parts)[Length])
	{
		// `parts` being constant string arrays, Length for each of them includes the trailing NUL byte, thus the
		// `sizeof... (Length)` part which subtracts the number of template parameters - the amount of NUL bytes so that
		// we don't waste space.
		constexpr size_t total_length = (... + Length) - sizeof... (Length);
		char_array<total_length + 1> ret;
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
