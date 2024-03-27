using System.Xml.Linq;
using Xamarin.SourceWriter;

namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

class AttributeDefinition
{
	public string ApiLevel { get; }
	public string Name { get; }
	public string Format { get; }
	public List<EnumDefinition> Enums { get; } = new List<EnumDefinition> ();

	public AttributeDefinition (string apiLevel, string name, string format)
	{
		ApiLevel = apiLevel;
		Name = name;
		Format = format;
	}

	public string GetAttributeType ()
	{
		return Format switch {
			"boolean" => "bool",
			"integer" => "int",
			"string" => "string?",
			_ => "string?",
		};
	}

	public static AttributeDefinition FromElement (string api, XElement e)
	{
		var name = e.GetAttributeStringOrEmpty ("name");
		var format = e.GetAttributeStringOrEmpty ("format");

		var def = new AttributeDefinition (api, name, format);

		var enums = e.Elements ("enum")
			     .Select (n => new EnumDefinition (api, n.GetAttributeStringOrEmpty ("name"), n.GetAttributeStringOrEmpty ("value")));

		def.Enums.AddRange (enums);

		return def;
	}

	public void WriteXml (TextWriter w)
	{
		var format = Format.HasValue () ? $" format='{Format}'" : string.Empty;
		var api_level = int.TryParse (ApiLevel, out var level) && level <= 10 ? string.Empty : $" api-level='{ApiLevel}'";

		w.Write ($"        <a name='{Name}'{format}{api_level}");

		if (Enums.Count > 0) {
			w.WriteLine (">");
			foreach (var e in Enums)
				w.WriteLine ($"            <enum-definition name='{e.Name}' value='{e.Value}' api-level='{e.ApiLevel}' />");
			w.WriteLine ("        </a>");
		} else
			w.WriteLine (" />");
	}
}
