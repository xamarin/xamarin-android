namespace Xamarin.Android.Application.Typemaps;

class MapJavaType
{
	public string Name { get; }
	public string SourceFile { get; }

	public MapJavaType (string name, string sourceFile = "")
	{
		Name = name;
		SourceFile = sourceFile;
	}
}
