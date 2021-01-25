#if MACOS
using System;

namespace Xamarin.Android.Tests
{
	partial class OS
	{
		public const string Name = "macOS";
		public const string NDKName = "darwin-x86_64";

		public static readonly StringComparer FilePathComparer = StringComparer.OrdinalIgnoreCase;
		public static readonly StringComparison FilePathComparison = StringComparison.OrdinalIgnoreCase;
	}
}
#endif
