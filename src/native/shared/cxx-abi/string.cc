//
// Defining the macro will make the the explicit instantations below truely hidden
//
#define _LIBCPP_DISABLE_VISIBILITY_ANNOTATIONS

#include <string>

_LIBCPP_BEGIN_NAMESPACE_STD

template class __attribute__ ((__visibility__("hidden"))) basic_string<char>;

_LIBCPP_END_NAMESPACE_STD
