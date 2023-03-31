using System.IO;

namespace Xamarin.Android.Application.Typemaps
{
	interface ITypemap
	{
		string Description              { get; }
		string FormatVersion            { get; }
		string FullPath                 { get; }
		Map Map                         { get; }
		MapArchitecture MapArchitecture { get; }

		bool CanLoad (Stream stream, string filePath);
		bool Load (string outputDirectory, bool generateFiles);
	}
}
