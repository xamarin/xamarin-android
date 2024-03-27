using System.Xml.Linq;

namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

class ManifestDefinition
{
	public string ApiLevel { get; set; } = "0";
	public List<ElementDefinition> Elements { get; } = new List<ElementDefinition> ();

	// Creates a new ManifestDefinition for a single Android API from the given file path
	public static ManifestDefinition FromFile (string filePath)
	{
		var dir_name = new FileInfo (filePath).Directory?.Parent?.Parent?.Parent?.Name;

		if (dir_name is null)
			throw new InvalidOperationException ($"Could not determine API level from {filePath}");

		var manifest = new ManifestDefinition () {
			ApiLevel = dir_name.Substring (dir_name.IndexOf ('-') + 1)
		};

		var elements = XDocument.Load (filePath).Root?.Elements ("declare-styleable")
			.Select (e => ElementDefinition.FromElement (manifest.ApiLevel, e))
			.ToList ();

		if (elements is not null)
			manifest.Elements.AddRange (elements);

		return manifest;
	}

	public static ManifestDefinition FromSdkDirectory (string sdkPath)
	{
		// Load all the attrs_manifest.xml files from the Android SDK
		var manifests = Directory.GetDirectories (Path.Combine (sdkPath, "platforms"), "android-*")
			.Select (d => Path.Combine (d, "data", "res", "values", "attrs_manifest.xml"))
			.Where (File.Exists)
			.Order ()
			.Select (FromFile)
			.ToList ();

		// Merge all the manifests into a single one
		var merged = new ManifestDefinition ();

		foreach (var def in manifests) {
			foreach (var el in def.Elements) {
				var element = merged.Elements.FirstOrDefault (_ => _.ActualElementName == el.ActualElementName);
				if (element == null)
					merged.Elements.Add (element = new ElementDefinition (
						el.ApiLevel,
						el.Name,
						(string []?) el.Parents?.Clone ()
					));
				foreach (var at in el.Attributes) {
					var attribute = element.Attributes.FirstOrDefault (_ => _.Name == at.Name);
					if (attribute == null)
						element.Attributes.Add (attribute = new AttributeDefinition (
							at.ApiLevel,
							at.Name,
							at.Format
						));
					foreach (var en in at.Enums) {
						var enumeration = at.Enums.FirstOrDefault (_ => _.Name == en.Name);
						if (enumeration == null)
							attribute.Enums.Add (new EnumDefinition (
								en.ApiLevel,
								en.Name,
								en.Value
							));
					}
				}
			}
		}

		return merged;
	}

	public void WriteXml (TextWriter w)
	{
		w.WriteLine ("<m>");

		foreach (var e in Elements)
			e.WriteXml (w);

		w.WriteLine ("</m>");
	}
}
