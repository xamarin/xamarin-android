#ifndef __TIMING_HH
#define __TIMING_HH

#include <stdarg.h>
#include <cstring>
#include <cstdlib>
#include <sys/time.h>

#include <android/log.h>

#include "cppcompat.hh"
#include "logger.hh"

namespace xamarin::android
{
	struct timing_point
	{
		time_t sec = 0;
		uint64_t ns = 0;

		void mark ();

		void reset ()
		{
			sec = 0;
			ns = 0;
		}
	};

	struct timing_period
	{
		timing_point start;
		timing_point end;

		void mark_start ()
		{
			start.mark ();
		}

		void mark_end ()
		{
			end.mark ();
		}

		void reset ()
		{
			start.reset ();
			end.reset ();
		}
	};

	struct timing_diff
	{
		static constexpr uint32_t ms_in_nsec = 1000000ULL;

		time_t sec;
		uint32_t ms;
		uint32_t ns;

		timing_diff (const timing_period &period);
	};

	struct managed_timing_sequence
	{
		timing_period  period;
		bool           in_use;
		bool           dynamic;
	};

	// This class is intended to be used by the managed code. It can be used by the native code as
	// well, but the overhead it has (out of necessity) might not be desirable in native code.
#define TIMING_FORMAT "; elapsed: %lis:%lu::%lu"

	class Timing
	{
		static constexpr char MESSAGE_FORMAT[] = "%s" TIMING_FORMAT;

	public:
		static constexpr size_t DEFAULT_POOL_SIZE = 16;

	public:
		explicit Timing (size_t initial_pool_size = DEFAULT_POOL_SIZE) noexcept
			: sequence_pool_size (initial_pool_size)
		{
			sequence_pool = new managed_timing_sequence [initial_pool_size] ();
		}

		~Timing () noexcept
		{
			delete[] sequence_pool;
		}

		static void info (timing_period const &period, const char *message) noexcept
		{
			timing_diff diff (period);

			log_info_nocheck (LOG_TIMING, MESSAGE_FORMAT, message == nullptr ? "" : message, diff.sec, diff.ms, diff.ns);
		}

		static void warn (timing_period const &period, const char *message) noexcept
		{
			timing_diff diff (period);

			log_warn (LOG_TIMING, MESSAGE_FORMAT, message == nullptr ? "" : message, diff.sec, diff.ms, diff.ns);
		}

		managed_timing_sequence* get_available_sequence () noexcept
		{
			lock_guard<xamarin::android::mutex> lock (sequence_lock);

			managed_timing_sequence *ret;
			for (size_t i = 0; i < sequence_pool_size; i++) {
				if (sequence_pool[i].in_use) {
					continue;
				}

				ret = &sequence_pool[i];
				ret->in_use = true;
				ret->dynamic = false;

				return ret;
			}

			ret = new managed_timing_sequence ();
			ret->dynamic = true;

			return ret;
		}

		void release_sequence (managed_timing_sequence *sequence)
		{
			if (sequence == nullptr)
				return;

			lock_guard<xamarin::android::mutex> lock (sequence_lock);
			if (sequence->dynamic) {
				sequence->period.reset ();
				delete sequence;
				return;
			}

			sequence->in_use = false;
		}

	private:
		managed_timing_sequence  *sequence_pool;
		size_t                    sequence_pool_size;
		xamarin::android::mutex   sequence_lock;
	};

	// This is a hack to avoid having to allocate memory when rendering messages that use additional
	// format placeholders on the caller side. Memory allocation would be necessary since we append
	// the standard timing suffix to every message printed. Using a variadic macro allows us to
	// compose a call with all the elements present and make the composition compile-time.
	//
	// It could be done with template packs but that would result in extra code generated whenever a
	// call with a different set of parameters would be made, plus the code to implement that would
	// be a bit verbose and unwieldy, so we will stick to this simple method.
#define TIMING_DO_LOG(_level, _category_, ...) ::log_ ## _level ## _nocheck ((_category_), __VA_ARGS__)

#define TIMING_LOG_INFO(__period__, __format__, ...) {                \
		timing_diff diff ((__period__)); \
		TIMING_DO_LOG (info, LOG_TIMING, __format__ TIMING_FORMAT, __VA_ARGS__, diff.sec, diff.ms, diff.ns); \
	}
}
#endif // __TIMING_HH
