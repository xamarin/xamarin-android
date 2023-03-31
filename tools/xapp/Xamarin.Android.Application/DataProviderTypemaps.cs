using System.IO;

using Xamarin.Android.Application.Utilities;

namespace Xamarin.Android.Application;

interface IDataProviderTypemaps : IDataProvider
{
}

abstract class DataProviderTypemapsXamarinApp : DataProviderXamarinApp, IDataProviderTypemaps
{
	public DataProviderTypemapsXamarinApp (Stream inputStream, string? inputPath, ILogger log)
		: base (inputStream, inputPath, log)
	{
	}

	public static IDataProviderTypemaps? Create (Stream inputStream, string? inputPath, ILogger log)
	{
		// TODO: load ELF here, for detection
		return null;

		// switch (format_tag) {
		// 	case Constants.FormatTag_V1:
		// 		return null;

		// 	case Constants.FormatTag_V2:
		// 		return null;

		// 	default:
		// 		//WarnNotSupported (elf, format_tag);
		// 		return null;
		// }
	}
}

class DataProviderTypemapsFastDev : DataProvider, IDataProviderTypemaps
{
	public DataProviderTypemapsFastDev (Stream inputStream, string? inputPath, ILogger log)
		: base (inputStream, inputPath, log)
	{
	}
}
