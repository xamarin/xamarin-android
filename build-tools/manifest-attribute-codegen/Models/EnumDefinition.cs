namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

class EnumDefinition
{
	public string ApiLevel { get; set; }
	public string Name { get; set; }
	public string Value { get; set; }

	public EnumDefinition (string apiLevel, string name, string value)
	{
		ApiLevel = apiLevel;
		Name = name;
		Value = value;
	}
}
