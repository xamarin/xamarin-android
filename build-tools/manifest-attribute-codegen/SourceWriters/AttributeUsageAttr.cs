using Xamarin.SourceWriter;

namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

public class AttributeUsageAttr : AttributeWriter
{
	public string Usage { get; set; }
	public bool AllowMultiple { get; set; }

	public AttributeUsageAttr (MetadataType type)
	{
		Usage = type.Usage;
		AllowMultiple = type.AllowMultiple;
	}

	public override void WriteAttribute (CodeWriter writer)
	{
		writer.WriteLine ($"[AttributeUsage ({Usage}, AllowMultiple = {(AllowMultiple ? "true" : "false")}, Inherited = false)]");
	}
}
