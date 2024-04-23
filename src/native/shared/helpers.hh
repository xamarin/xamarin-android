#ifndef __HELPERS_HH
#define __HELPERS_HH

#include <cstdint>
#include <cstdlib>

#include "java-interop-util.h"
#include "platform-compat.hh"

namespace xamarin::android
{
#define ADD_WITH_OVERFLOW_CHECK(__ret_type__, __a__, __b__) xamarin::android::Helpers::add_with_overflow_check<__ret_type__>(__FILE__, __LINE__, (__a__), (__b__))
#define MULTIPLY_WITH_OVERFLOW_CHECK(__ret_type__, __a__, __b__) xamarin::android::Helpers::multiply_with_overflow_check<__ret_type__>(__FILE__, __LINE__, (__a__), (__b__))

	class [[gnu::visibility("hidden")]] Helpers
	{
	public:
		template<typename Ret, typename P1, typename P2>
		force_inline static Ret add_with_overflow_check (const char *file, uint32_t line, P1 a, P2 b) noexcept
		{
			Ret ret;

			if (__builtin_add_overflow (a, b, &ret)) [[unlikely]] {
				log_fatal (LOG_DEFAULT, "Integer overflow on addition at %s:%u", file, line);
				abort_application ();
			}

			return ret;
		}

		// Can't use templates as above with add_with_oveflow because of a bug in the clang compiler
		// shipped with the NDK:
		//
		//   https://github.com/android-ndk/ndk/issues/294
		//   https://github.com/android-ndk/ndk/issues/295
		//   https://bugs.llvm.org/show_bug.cgi?id=16404
		//
		// Using templated parameter types for `a` and `b` would make clang generate that tries to
		// use 128-bit integers and thus output code that calls `__muloti4` and so linking would
		// fail
		//
		template<typename Ret>
		force_inline static Ret multiply_with_overflow_check (const char *file, uint32_t line, size_t a, size_t b) noexcept
		{
			Ret ret;

			if (__builtin_mul_overflow (a, b, &ret)) [[unlikely]] {
				log_fatal (LOG_DEFAULT, "Integer overflow on multiplication at %s:%u", file, line);
				abort_application ();
			}

			return ret;
		}

		[[noreturn]] static void abort_application () noexcept;
	};
}
#endif // __HELPERS_HH
