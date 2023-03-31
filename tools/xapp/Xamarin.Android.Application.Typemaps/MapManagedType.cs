using System;

namespace Xamarin.Android.Application.Typemaps;

class MapManagedType
{
	public Guid MVID { get; } = Guid.Empty;
	public uint TokenID { get; } = 0;
	public string TypeName { get; set; } = String.Empty;
	public string AssemblyName { get; set; } = String.Empty;
	public bool IsDuplicate { get; set; }
	public bool IsGeneric { get; set; }
	public string SourceFile { get; } = String.Empty;

	public MapManagedType (Guid mvid, uint tokenID, string assemblyName, string sourceFile)
	{
		MVID = mvid;
		TokenID = tokenID;
		AssemblyName = assemblyName;
		SourceFile = sourceFile;
	}

	public MapManagedType (string name, string sourceFile = "")
	{
		TypeName = name;
		SourceFile = sourceFile;
	}
}
