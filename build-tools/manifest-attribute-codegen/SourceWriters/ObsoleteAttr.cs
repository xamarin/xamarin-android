using Xamarin.SourceWriter;

namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

public class ObsoleteAttr : AttributeWriter
{
	public string Message { get; set; }

	public ObsoleteAttr (string message)
	{
		Message = message;
	}

	public override void WriteAttribute (CodeWriter writer)
	{
		writer.WriteLine ($"[Obsolete (\"{Message}\")]");
	}
}
