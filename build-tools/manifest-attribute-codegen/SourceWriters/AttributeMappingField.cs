using Xamarin.SourceWriter;

namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

class AttributeMappingField : FieldWriter
{
	public static AttributeMappingField Create (MetadataType type)
	{
		var field = new AttributeMappingField {
			Name = "mapping",
			IsStatic = true,
			Type = new TypeReferenceWriter ($"Xamarin.Android.Manifest.ManifestDocumentElement<{type.ManagedName}>"),
			Value = $"new (\"{type.Name}\")",
		};

		return field;
	}
}
