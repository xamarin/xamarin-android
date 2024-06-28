// This is a -*- C++ -*- header
#ifndef __CPP_COMPAT_H
#define __CPP_COMPAT_H

#if defined(USES_LIBSTDCPP)
#include <mutex>
#else // def USES_LIBSTDCPP
#include <pthread.h>

// We can't use <mutex> on Android because it requires linking libc++ into the rutime, see below.
//
// Android NDK currently provides a build of libc++ which we cannot link into .NET for Android runtime because it would
// expose libc++ symbols which would conflict with a version of libc++ potentially included in a mixed
// native/Xamarin.Android application.
//
// Until we can figure out a way to take full advantage of the STL, this header will
// contain implementations of certain C++ standard functions, classes
// etc we want to use despite lack of the STL.
//
// When/if we have any STL implementation available on standard Android
// we can remove this file.
namespace std
{
	template<typename TMutex>
	class lock_guard
	{
	public:
		using mutex_type = TMutex;

	public:
		lock_guard (const lock_guard&) = delete;

		explicit lock_guard (mutex_type& _mutex)
			: _mutex (_mutex)
		{
			_mutex.lock ();
		}

		~lock_guard ()
		{
			_mutex.unlock ();
		}

		lock_guard& operator= (const lock_guard&) = delete;

	private:
		mutex_type &_mutex;
	};

	class mutex
	{
	public:
		mutex () noexcept = default;
		~mutex () noexcept = default;

		void lock () noexcept
		{
			pthread_mutex_lock (&_pmutex);
		}

		void unlock () noexcept
		{
			pthread_mutex_unlock (&_pmutex);
		}
	private:
		pthread_mutex_t _pmutex = PTHREAD_MUTEX_INITIALIZER;
	};
}
#endif // ndef USES_LIBSTDCPP

#endif
