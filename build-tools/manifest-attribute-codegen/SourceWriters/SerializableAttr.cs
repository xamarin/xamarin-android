using Xamarin.SourceWriter;

namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

public class SerializableAttr : AttributeWriter
{
	public override void WriteAttribute (CodeWriter writer)
	{
		writer.WriteLine ("[Serializable]");
	}
}
