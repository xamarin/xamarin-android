using System;

namespace Xamarin.Android.Prepare
{
	class TestAndroidInstrumentation
	{
		public string TypeName { get; }

		public string ResultsPath { get; set; } = String.Empty;
		public string LogcatFilenameDistincion { get; set; } = String.Empty;
		public ulong TimeoutInMS { get; set; } = 0;

		public TestAndroidInstrumentation (string typeName)
		{
			if (typeName.Length == 0)
				throw new ArgumentException ("must not be empty", nameof (typeName));
			TypeName = typeName;
		}
	}
}
