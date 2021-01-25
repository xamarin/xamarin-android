using System.Collections.Generic;

namespace Xamarin.Android.Tests
{
	class MSBuildTimingState : AndroidState
	{
		public List<MSBuildTimingResult> Results { get; } = new List<MSBuildTimingResult> ();
	}
}
