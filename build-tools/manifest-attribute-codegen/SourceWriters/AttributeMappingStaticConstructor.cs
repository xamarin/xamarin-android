using Xamarin.SourceWriter;

namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

class AttributeMappingStaticConstructor : ConstructorWriter
{
	public static AttributeMappingStaticConstructor Create (ElementDefinition attr, MetadataSource metadata, MetadataType type)
	{
		var ctor = new AttributeMappingStaticConstructor {
			Name = type.ManagedName,
			IsStatic = true,
		};

		// mapping.Add (
		// 	member: "Name",
		// 	attributeName: "name",
		// 	getter: self => self.Name,
		// 	setter: (self, value) => self.Name = (string?) value
		// );
		foreach (var a in attr.Attributes.OrderBy (a => a.Name)) {
			var attr_metadata = metadata.GetMetadata ($"{attr.ActualElementName}.{a.Name}");

			if (!attr_metadata.Visible || attr_metadata.ManualMap)
				continue;

			var name = (attr_metadata.Name ?? a.Name).Capitalize ();
			var setter = $"(self, value) => self.{name} = ({attr_metadata.Type ?? a.GetAttributeType ()}) value";

			if (attr_metadata.ReadOnly)
				setter = "null";

			ctor.Body.Add ($"mapping.Add (");
			ctor.Body.Add ($"	member: \"{name}\",");
			ctor.Body.Add ($"	attributeName: \"{a.Name}\",");
			ctor.Body.Add ($"	getter: self => self.{name},");
			ctor.Body.Add ($"	setter: {setter}");
			ctor.Body.Add ($");");
		}

		ctor.Body.Add (string.Empty);
		ctor.Body.Add ($"AddManualMapping ();");

		return ctor;
	}
}
