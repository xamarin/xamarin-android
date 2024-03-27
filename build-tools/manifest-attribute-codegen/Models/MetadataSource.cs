using System.Text;
using System.Xml.Linq;

namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

class MetadataSource
{
	public Dictionary<string, MetadataType> Types { get; } = [];
	public Dictionary<string, MetadataAttribute> Elements { get; } = [];

	public MetadataSource (string filename)
	{
		var xml = XElement.Load (filename);

		foreach (var element in xml.Elements ("element")) {
			var me = new MetadataAttribute (element);
			Elements.Add (me.Path, me);
		}

		foreach (var element in xml.Elements ("type")) {
			var el = new MetadataType (element);
			Types.Add (el.Name, el);
		}
	}

	public MetadataAttribute GetMetadata (string path)
	{
		if (Elements.TryGetValue (path, out var element))
			return element;

		throw new InvalidOperationException ($"No MetadataElement found for path '{path}'.");
	}

	public void EnsureAllElementsAccountedFor (List<ElementDefinition> elements)
	{
		var missing = new List<string> ();

		foreach (var e in elements) {
			if (!Types.TryGetValue (e.ActualElementName, out var t)) {
				missing.Add ($"- Type: <{e.ActualElementName}>");
				continue;
			}

			if (t.Ignore)
				continue;

			foreach (var a in e.Attributes) {
				var name = $"{e.ActualElementName}.{a.Name}";

				if (!Elements.TryGetValue (name, out _))
					missing.Add ($"- Element: {name}");
			}
		}

		if (missing.Count == 0)
			return;

		var sb = new StringBuilder ();
		sb.AppendLine ("The following manifest elements are not specified in the metadata:");

		foreach (var m in missing)
			sb.AppendLine (m);

		throw new InvalidOperationException (sb.ToString ());
	}

	public void EnsureAllMetadataElementsExistInManifest (List<ElementDefinition> elements)
	{
		var missing = new List<string> ();

		foreach (var type in Types) {
			var type_def = elements.FirstOrDefault (e => e.ActualElementName == type.Key);

			if (type_def is null) {
				missing.Add ($"- Type: {type.Key}");
				continue;
			}
		}

		foreach (var type in Elements) {
			var type_name = type.Key.FirstSubset ('.');
			var elem_name = type.Key.LastSubset ('.');

			var type_def = elements.FirstOrDefault (e => e.ActualElementName == type_name);

			if (type_def is null) {
				missing.Add ($"- Element: {type.Key}");
				continue;
			}

			var elem_def = type_def.Attributes.FirstOrDefault (e => e.Name == elem_name);

			if (elem_def is null) {
				missing.Add ($"- Element: {type.Key}");
				continue;
			}
		}

		if (missing.Count == 0)
			return;

		var sb = new StringBuilder ();
		sb.AppendLine ("The following elements specified in the metadata were not found in the manifest:");

		foreach (var e in missing)
			sb.AppendLine (e);

		throw new InvalidOperationException (sb.ToString ());
	}
}

class MetadataAttribute
{
	public string Path { get; set; }
	public bool Visible { get; set; } = true;
	public string? Type { get; set; }
	public string? Name { get; set; }
	public string? Obsolete { get; set; }
	public bool ReadOnly { get; set; }
	public bool ManualMap { get; set; }

	public MetadataAttribute (XElement element)
	{
		Path = element.Attribute ("path")?.Value ?? throw new InvalidDataException ("Missing 'path' attribute.");

		if (!Path.Contains ('.'))
			throw new InvalidDataException ($"Invalid 'path' attribute value: {Path}");

		Visible = element.GetAttributeBoolOrDefault ("visible", true);
		Type = element.Attribute ("type")?.Value;
		Name = element.Attribute ("name")?.Value;
		Obsolete = element.Attribute ("obsolete")?.Value;
		ReadOnly = element.GetAttributeBoolOrDefault ("readonly", false);
		ManualMap = element.GetAttributeBoolOrDefault ("manualMap", false);
	}
}

public class MetadataType
{
	public string Name { get; set; }
	public string ManagedName { get; set; } = string.Empty;
	public string Namespace { get; set; } = string.Empty;
	public bool Ignore { get; set; }
	public string OutputFile { get; set; } = string.Empty;
	public string Usage { get; set; } = string.Empty;
	public bool AllowMultiple { get; set; }
	public bool IsJniNameProvider { get; set; }
	public bool HasDefaultConstructor { get; set; }
	public bool IsSealed { get; set; }
	public bool GenerateMapping { get; set; }

	public MetadataType (XElement element)
	{
		Name = element.GetRequiredAttributeString ("name");
		Ignore = element.GetAttributeBoolOrDefault ("ignore", false);

		if (Ignore)
			return;

		Namespace = element.GetRequiredAttributeString ("namespace");
		OutputFile = element.GetRequiredAttributeString ("outputFile");
		Usage = element.GetRequiredAttributeString ("usage");
		AllowMultiple = element.GetAttributeBoolOrDefault ("allowMultiple", false);
		IsJniNameProvider = element.GetAttributeBoolOrDefault ("jniNameProvider", false);
		HasDefaultConstructor = element.GetAttributeBoolOrDefault ("defaultConstructor", true);
		IsSealed = element.GetAttributeBoolOrDefault ("sealed", true);
		ManagedName = element.Attribute ("managedName")?.Value ?? Name.Unhyphenate ().Capitalize () + "Attribute";
		GenerateMapping = element.GetAttributeBoolOrDefault ("generateMapping", true);
	}
}
