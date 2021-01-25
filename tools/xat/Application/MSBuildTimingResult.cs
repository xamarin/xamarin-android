using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	sealed class MSBuildTimingResult : AppObject
	{
		public string ProjectName { get; }
		public string OutputFilePath { get; }

		public MSBuildTimingResult (string projectName, string outputFilePath)
		{
			ProjectName = EnsureParameterValue (nameof (projectName), projectName);
			OutputFilePath = EnsureParameterValue (nameof (outputFilePath), outputFilePath);
		}
	}
}
