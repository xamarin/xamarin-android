using Mono.Options;
using Xamarin.SourceWriter;

namespace Xamarin.Android.Tools.ManifestAttributeCodeGenerator;

public class Program
{
	public static int Main (string [] args)
	{
		var show_help = false;
		string? sdk_path = null;
		string? base_dir = null;
		string? output_manifest = null;
		string? metadata_file = null;

		var options = new OptionSet {
			"Usage: manifest-attribute-codegen",
			"",
			"Generate manifest attributes from Android SDK platforms.",
			"",
			"Options:",
			{ "sdk-path=",
				"Path to Android SDK",
				v => sdk_path = v },
			{ "base-dir=",
				"xamarin-android repository root directory",
				v => base_dir = v },
			{ "metadata-file=",
				"metadata file used to drive the generation process",
				v => metadata_file = v },
			{ "output-manifest=",
				"Optional file to write merged manifest output",
				v => output_manifest = v },
			{ "h|?|help",
				"Show this help message and exit.",
				v => show_help = v != null },
		};

		try {
			options.Parse (args);
		} catch (Exception e) {
			Console.Error.WriteLine ("manifest-attribute-codegen: {0}", e);
			return 1;
		}

		if (show_help) {
			options.WriteOptionDescriptions (Console.Out);
			return 0;
		}

		sdk_path ??= Environment.GetEnvironmentVariable ("ANDROID_SDK_HOME");
		sdk_path ??= Environment.GetEnvironmentVariable ("ANDROID_SDK_PATH");

		if (sdk_path is null)
			throw new InvalidOperationException ("Pass Android SDK location as a command argument, or specify ANDROID_SDK_HOME or ANDROID_SDK_PATH environment variable");

		if (base_dir is null)
			throw new InvalidOperationException ("Pass xamarin-android repository root directory as a command argument");

		if (metadata_file is null)
			throw new InvalidOperationException ("'metadata-file' argument must be provided");

		// Create a merged manifest from the SDK levels
		var merged = ManifestDefinition.FromSdkDirectory (sdk_path);

		if (output_manifest is not null) {
			if (Path.GetDirectoryName (output_manifest) is string manifest_dir)
				Directory.CreateDirectory (manifest_dir);

			using var w = new StreamWriter (output_manifest);
			merged.WriteXml (w);
		}

		// Read metadata file
		var metadata = new MetadataSource (metadata_file);

		// Ensure everything in the Android SDK is accounted for.
		// This forces us to handle anything new that's been added to the SDK.
		metadata.EnsureAllElementsAccountedFor (merged.Elements);

		// Ensure there are no unused elements in the metadata file
		metadata.EnsureAllMetadataElementsExistInManifest (merged.Elements);

		// Generate manifest attributes C# code
		foreach (var type in metadata.Types.Values.Where (t => !t.Ignore)) {
			using var w = new StreamWriter (Path.Combine (base_dir, type.OutputFile));
			using var cw = new CodeWriter (w);
			var element = merged.Elements.First (_ => _.ActualElementName == type.Name);
			var writer = AttributeDataClass.Create (element, metadata, type);
			writer.Write (cw);
		}

		return 0;
	}
}
