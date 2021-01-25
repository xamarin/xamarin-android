using System;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class AndroidState : AppObject
	{
		public int EmulatorProcessId             { get; set; } = -1;
		public string AdbTarget                  { get; set; } = String.Empty;
	}
}
