namespace Xamarin.Android.Application;

sealed class Constants
{
	// Symbols in libxamarin-app.so
	public sealed class SymbolNames
	{
		public const string ApplicationConfig             = "application_config";
		public const string DSOCache                      = "dso_cache";
		public const string EnvironmentVariables          = "app_environment_variables";
		public const string FormatTag                     = "format_tag";
		public const string MarshalMethodsClassCache      = "marshal_methods_class_cache";
		public const string MarshalMethodsClassNames      = "mm_class_names";
		public const string MarshalMethodsMethodNames     = "mm_method_names";
		public const string MarshalMethodsNumberOfClasses = "marshal_methods_number_of_classes";
		public const string MarshalMethodsXamarinAppInit  = "xamarin_app_init";
		public const string MonoAotModeName               = "mono_aot_mode_name";
		public const string SystemProperties              = "app_system_properties";
	}

	// Correspond to the `FORMAT_TAG` constant in src/monodroid/xamarin-app.hh
	public const ulong FormatTag_V1 = 0x015E6972616D58;
	public const ulong FormatTag_V2 = 0x00026E69726D6158;

	public const uint CompressedDataMagicInt = 0x5A4C4158; // 'XALZ', little-endian
	public static readonly byte[] CompressedDataMagic = { 0x58, 0x41, 0x4c, 0x5a }; // 'XALZ', little-endian

	public const string UnableToLoadDataForPointer = "[unable to load data a pointer indicates]";
	public const string ItemUnsupported = "unsupported";
}
