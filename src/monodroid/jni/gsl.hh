// Contains portions of the Guideline Support Library that Xamarin.Android uses:
//
//   https://isocpp.github.io/CppCoreGuidelines/CppCoreGuidelines
//
// Most code is taken from the (MIT-licensed) library at
//
//   https://github.com/Microsoft/GSL
//
// We use clang-tidy to check certain rules against the guidelines:
//
//   https://releases.llvm.org/14.0.0/tools/clang/tools/extra/docs/clang-tidy/checks/list.html
//
#if !defined (__GSL_HH)
#define __GSL_HH

#include <type_traits>

namespace gsl
{
	// https://isocpp.github.io/CppCoreGuidelines/CppCoreGuidelines#i11-never-transfer-ownership-by-a-raw-pointer-t-or-reference-t
	// https://releases.llvm.org/14.0.0/tools/clang/tools/extra/docs/clang-tidy/checks/cppcoreguidelines-owning-memory.html
	template <class T, class = std::enable_if_t<std::is_pointer<T>::value>>
	using owner = T;
}
#endif // ndef __GSL_HH
