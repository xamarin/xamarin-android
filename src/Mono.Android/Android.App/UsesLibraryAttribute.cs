//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by 'manifest-attribute-codegen'.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

#nullable enable

using System;

namespace Android.App;

[Serializable]
[AttributeUsage (AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed partial class UsesLibraryAttribute : Attribute {
	public UsesLibraryAttribute ()
	{
	}

	public string? Name { get; set; }

	public bool Required { get; set; }

#if XABT_MANIFEST_EXTENSIONS
	static Xamarin.Android.Manifest.ManifestDocumentElement<UsesLibraryAttribute> mapping = new ("uses-library");

	static UsesLibraryAttribute ()
	{
		mapping.Add (
			member: "Name",
			attributeName: "name",
			getter: self => self.Name,
			setter: (self, value) => self.Name = (string?) value
		);
		mapping.Add (
			member: "Required",
			attributeName: "required",
			getter: self => self.Required,
			setter: (self, value) => self.Required = (bool) value
		);

		AddManualMapping ();
	}

	static partial void AddManualMapping ();
#endif
}
