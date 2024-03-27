using Xamarin.SourceWriter;

namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

class AttributeMappingManualInitializer : MethodWriter
{
	public static AttributeMappingManualInitializer Create ()
	{
		var method = new AttributeMappingManualInitializer {
			Name = "AddManualMapping",
			IsStatic = true,
			IsPartial = true,
			IsDeclaration = true,
			ReturnType = TypeReferenceWriter.Void,
		};

		return method;
	}
}
