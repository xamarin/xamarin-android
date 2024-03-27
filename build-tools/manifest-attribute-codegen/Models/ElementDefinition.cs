using System.Xml.Linq;

namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

class ElementDefinition
{
	static readonly char [] sep = [' '];

	public string ApiLevel { get; }
	public string Name { get; }
	public string[]? Parents { get;}
	public List<AttributeDefinition> Attributes { get; } = new List<AttributeDefinition> ();

	public string ActualElementName => Name.ToActualName ();

	public ElementDefinition (string apiLevel, string name, string []? parents)
	{
		ApiLevel = apiLevel;
		Name = name;
		Parents = parents;
	}

	public static ElementDefinition FromElement (string api, XElement e)
	{
		var name = e.GetAttributeStringOrEmpty ("name");
		var parents = e.Attribute ("parent")?.Value?.Split (sep, StringSplitOptions.RemoveEmptyEntries);
		var def = new ElementDefinition (api, name, parents);

		var attrs = e.Elements ("attr")
			     .Select (a => AttributeDefinition.FromElement (api, a));

		def.Attributes.AddRange (attrs);

		return def;
	}

	public void WriteXml (TextWriter w)
	{
		w.WriteLine ($"    <e name='{ActualElementName}' api-level='{ApiLevel}'>");

		if (Parents?.Any () == true)
			foreach (var p in Parents)
				w.WriteLine ($"        <parent>{p.ToActualName ()}</parent>");

		foreach (var a in Attributes)
			a.WriteXml (w);

		w.WriteLine ("    </e>");
	}
}
