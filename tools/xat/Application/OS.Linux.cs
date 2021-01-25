#if LINUX
using System;

namespace Xamarin.Android.Tests
{
	partial class OS
	{
		public const string Name = "Linux";
		public const string NDKName = "linux-x86_64";

		public static readonly StringComparer FilePathComparer = StringComparer.Ordinal;
		public static readonly StringComparison FilePathComparison = StringComparison.Ordinal;
	}
}
#endif
