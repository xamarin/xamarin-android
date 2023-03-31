namespace Xamarin.Android.Application.Typemaps;

class MapEntry
{
	public MapManagedType ManagedType { get; }
	public MapJavaType JavaType { get; }

	public MapEntry (MapManagedType managedType, MapJavaType javaType)
	{
		ManagedType = managedType;
		JavaType = javaType;
	}
}
