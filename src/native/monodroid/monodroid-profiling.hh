#if !defined(MONODROID_PROFILING_HH)
#define MONODROID_PROFILING_HH

namespace xamarin::android {
	// Keep the values ordered in the order of increasing verbosity
	enum class ProfilingMode
	{
		Bare,
		Extended,
		Verbose,
		Extreme,
	};
}
#endif // MONODROID_PROFILING_HH
