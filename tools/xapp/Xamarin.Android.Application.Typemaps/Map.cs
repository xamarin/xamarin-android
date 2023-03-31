using System.Collections.Generic;

namespace Xamarin.Android.Application.Typemaps;

class Map
{
	public MapKind Kind { get; }
	public MapArchitecture Architecture { get; }
	public List<MapEntry> ManagedToJava { get; }
	public List<MapEntry> JavaToManaged { get; }
	public string FormatVersion { get; }

	public Map (MapKind kind, MapArchitecture architecture, List<MapEntry> managedToJava, List<MapEntry> javaToManaged, string formatVersion)
	{
		Kind = kind;
		Architecture = architecture;
		ManagedToJava = managedToJava;
		JavaToManaged = javaToManaged;
		FormatVersion = formatVersion;
	}
}
