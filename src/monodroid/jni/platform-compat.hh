#ifndef __PLATFORM_COMPAT_HH
#define __PLATFORM_COMPAT_HH

#include <cstdint>

#if WINDOWS
constexpr char MONODROID_PATH_SEPARATOR[] = "\\";
constexpr char MONODROID_PATH_SEPARATOR_CHAR = '\\';
#else // !WINDOWS
constexpr char MONODROID_PATH_SEPARATOR[] = "/";
constexpr char MONODROID_PATH_SEPARATOR_CHAR = '/';
#endif // WINDOWS
constexpr size_t MONODROID_PATH_SEPARATOR_LENGTH = sizeof(MONODROID_PATH_SEPARATOR) - 1;

#if WINDOWS
typedef struct _stat monodroid_stat_t;
#define monodroid_dir_t _WDIR
typedef struct _wdirent monodroid_dirent_t;
#else // !WINDOWS
typedef struct stat monodroid_stat_t;
#define monodroid_dir_t DIR
typedef struct dirent monodroid_dirent_t;
#endif // WINDOWS

#define DEFAULT_DIRECTORY_MODE S_IRWXU | S_IRGRP | S_IXGRP | S_IROTH | S_IXOTH

#if defined (_MSC_VER)
#define inline __inline
#define force_inline __forceinline
#elif defined (__GNUC__)
#ifndef XA_LIKELY
#define XA_LIKELY(expr) (__builtin_expect ((expr) != 0, 1))
#endif

#ifndef XA_UNLIKELY
#define XA_UNLIKELY(expr) (__builtin_expect ((expr) != 0, 0))
#endif

#define force_inline inline __attribute__((always_inline))
#define never_inline __attribute__((noinline))

#if defined (__clang__)
#define UNINITIALIZED [[clang::uninitialized]]
#elif __GNUC__ >= 11  // def __clang
#define UNINITIALIZED [[gnu::uninitialized]]
#endif // ndef __clang
#endif // _MSV_VER

#ifndef force_inline
#define force_inline inline
#endif

#ifndef never_inline
#define never_inline
#endif

#ifndef inline
#define inline inline
#endif

#ifndef XA_LIKELY
#define XA_LIKELY(expr) (expr)
#endif

#ifndef XA_UNLIKELY
#define XA_UNLIKELY(expr) (expr)
#endif

#ifndef UNINITIALIZED
#define UNINITIALIZED
#endif

#endif // __PLATFORM_COMPAT_HH
