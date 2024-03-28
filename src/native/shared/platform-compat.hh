#ifndef __PLATFORM_COMPAT_HH
#define __PLATFORM_COMPAT_HH

#include <fcntl.h>

static inline constexpr int DEFAULT_DIRECTORY_MODE = S_IRWXU | S_IRGRP | S_IXGRP | S_IROTH | S_IXOTH;

#if defined(NO_INLINE)
#define force_inline [[gnu::noinline]]
#define inline_calls [[gnu::flatten]]
#else
#define force_inline [[gnu::always_inline]]
#define inline_calls
#endif

#endif // __PLATFORM_COMPAT_HH
