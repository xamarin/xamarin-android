using System;

namespace Xamarin.Android.Tests
{
	class APKState
	{
		public string SdkVersion     { get; set; } = String.Empty;
		public string AdbTarget      { get; set; } = String.Empty;
		public int EmulatorProcessId { get; set; } = -1;
	}
}
