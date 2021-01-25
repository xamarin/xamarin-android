using System;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class TestAndroidInstrumentation : AppObject
	{
		public string TypeName                 { get; }

		public string ResultsPath              { get; set; } = String.Empty;
		public string LogcatFilenameDistincion { get; set; } = String.Empty;
		public long TimeoutInMS                { get; set; } = -1;

		public TestAndroidInstrumentation (string typeName)
		{
			EnsureParameterValue (nameof (typeName), typeName);
			TypeName = typeName;
		}
	}
}
