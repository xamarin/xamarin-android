#include "helpers.hh"

using namespace xamarin::android;

[[noreturn]] void
Helpers::abort_application () noexcept
{
	std::abort ();
}
