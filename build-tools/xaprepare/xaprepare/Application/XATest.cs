using System;

namespace Xamarin.Android.Prepare
{
	abstract class XATest : AppObject
	{
		/// <summary>
		///   Name of the test "kind" (e.g. "APK", "NUnit" etc)
		/// </summary>
		public abstract string KindName { get; }

		/// <summary>
		///   Display name for the test, does not need to be unique but must not be empty
		/// </summary>
		public string Name { get; }

		/// <summary>
		///   Path to the file with test run results. May be empty for some tests.
		/// </summary>
		public string ResultsFilePath { get; set; } = String.Empty;

		/// <summary>
		///   Each test uses some sort of input/container file (a .csproj, a .dll etc) but the exact meaning and
		///   treatment of the file depends on the actual test engine/kind/runner etc. However, since all of the tests
		///   have some sort of file, this base class has this generic, required, property to keep it. Derived classes
		///   should expose it to the world using a property name specific to the test kind (e.g. Assembly for unit
		///   tests, Project for APK tests etc)
		/// </summary>
		protected string TestFilePath { get; }

		protected XATest (string name, string testFilePath)
		{
			Name = EnsureNonEmptyArgument (name, nameof (name));
			TestFilePath = EnsureNonEmptyArgument (testFilePath, nameof (testFilePath));
		}

		protected string EnsureNonEmptyArgument (string value, string argumentName)
		{
			if (value.Length == 0)
				throw new ArgumentException ("must not be empty", argumentName);
			return value;
		}
	}
}
