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

namespace Android.Content;

[Serializable]
[AttributeUsage (AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public partial class GrantUriPermissionAttribute : Attribute {
	public GrantUriPermissionAttribute ()
	{
	}

	public string? Path { get; set; }

	public string? PathPattern { get; set; }

	public string? PathPrefix { get; set; }

#if XABT_MANIFEST_EXTENSIONS
	static Xamarin.Android.Manifest.ManifestDocumentElement<GrantUriPermissionAttribute> mapping = new ("grant-uri-permission");

	static GrantUriPermissionAttribute ()
	{
		mapping.Add (
			member: "Path",
			attributeName: "path",
			getter: self => self.Path,
			setter: (self, value) => self.Path = (string?) value
		);
		mapping.Add (
			member: "PathPattern",
			attributeName: "pathPattern",
			getter: self => self.PathPattern,
			setter: (self, value) => self.PathPattern = (string?) value
		);
		mapping.Add (
			member: "PathPrefix",
			attributeName: "pathPrefix",
			getter: self => self.PathPrefix,
			setter: (self, value) => self.PathPrefix = (string?) value
		);

		AddManualMapping ();
	}

	static partial void AddManualMapping ();
#endif
}
