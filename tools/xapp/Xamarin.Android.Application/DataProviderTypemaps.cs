using System.IO;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

class DataProviderTypemaps : DataProviderXamarinApp
{
	public DataProviderTypemaps (Stream inputStream, string? inputPath, ILogger log)
		: base (inputStream, inputPath, log)
	{
	}
}
