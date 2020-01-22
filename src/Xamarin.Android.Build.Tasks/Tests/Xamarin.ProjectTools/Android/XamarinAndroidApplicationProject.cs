using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Build.Construction;

namespace Xamarin.ProjectTools
{
	public class XamarinAndroidApplicationProject : XamarinAndroidCommonProject
	{
		const string default_strings_xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<resources>
	<string name=""hello"">Hello World, Click Me!</string>
	<string name=""app_name"">${PROJECT_NAME}</string>
</resources>
";

		static readonly string default_layout_main;
		static readonly string default_main_activity_cs, default_main_activity_fs;
		static readonly string default_android_manifest;

		static XamarinAndroidApplicationProject ()
		{
			using (var sr = new StreamReader (typeof(XamarinAndroidApplicationProject).Assembly.GetManifestResourceStream ("Xamarin.ProjectTools.Resources.Base.MainActivity.cs")))
				default_main_activity_cs = sr.ReadToEnd ();
			using (var sr = new StreamReader (typeof(XamarinAndroidApplicationProject).Assembly.GetManifestResourceStream ("Xamarin.ProjectTools.Resources.Base.MainActivity.fs")))
				default_main_activity_fs = sr.ReadToEnd ();
			using (var sr = new StreamReader (typeof(XamarinAndroidApplicationProject).Assembly.GetManifestResourceStream ("Xamarin.ProjectTools.Resources.Base.LayoutMain.axml")))
				default_layout_main = sr.ReadToEnd ();
			using (var sr = new StreamReader (typeof(XamarinAndroidApplicationProject).Assembly.GetManifestResourceStream ("Xamarin.ProjectTools.Resources.Base.AndroidManifest.xml")))
				default_android_manifest = sr.ReadToEnd ();

		}

		public XamarinAndroidApplicationProject (string debugConfigurationName = "Debug", string releaseConfigurationName = "Release")
			: base (debugConfigurationName, releaseConfigurationName)
		{
			SetProperty ("AndroidApplication", "True");

			SetProperty ("AndroidResgenClass", "Resource");
			SetProperty ("AndroidResgenFile", () => "Resources\\Resource.designer" + Language.DefaultDesignerExtension);
			SetProperty ("AndroidManifest", "Properties\\AndroidManifest.xml");
			SetProperty (DebugProperties, "AndroidLinkMode", "None");
			SetProperty (ReleaseProperties, "AndroidLinkMode", "SdkOnly");
			SetProperty (DebugProperties, KnownProperties.EmbedAssembliesIntoApk, "False", "'$(EmbedAssembliesIntoApk)' == ''");
			SetProperty (ReleaseProperties, KnownProperties.EmbedAssembliesIntoApk, "True", "'$(EmbedAssembliesIntoApk)' == ''");

			AndroidManifest = default_android_manifest;
			LayoutMain = default_layout_main;
			StringsXml = default_strings_xml;
			PackageName = PackageName ?? string.Format ("{0}.{0}", ProjectName);
			JavaPackageName = JavaPackageName ?? PackageName.ToLowerInvariant ();

			OtherBuildItems.Add (new BuildItem.NoActionResource ("Properties\\AndroidManifest.xml") { TextContent = () => 
					AndroidManifest.Replace("${PROJECT_NAME}", ProjectName).
					Replace ("${PACKAGENAME}", PackageName) });
			AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\layout\\Main.axml") { TextContent = () => LayoutMain });
			AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\values\\Strings.xml") { TextContent = () => StringsXml.Replace ("${PROJECT_NAME}", ProjectName) });

			Sources.Add (new BuildItem.Source (() => "MainActivity" + Language.DefaultExtension) { TextContent = () => ProcessSourceTemplate (MainActivity ?? DefaultMainActivity) });
		}

		// it is exposed as public because we may want to slightly modify this.
		public string DefaultMainActivity {
			get { return Language == XamarinAndroidProjectLanguage.FSharp ? default_main_activity_fs : default_main_activity_cs; }
		}

		public bool BundleAssemblies {
			get { return string.Equals (GetProperty (KnownProperties.BundleAssemblies), "True", StringComparison.OrdinalIgnoreCase); }
			set { SetProperty (KnownProperties.BundleAssemblies, value.ToString ()); }
		}

		public bool AotAssemblies {
			get { return string.Equals (GetProperty (KnownProperties.AotAssemblies), "True", StringComparison.OrdinalIgnoreCase); }
			set { SetProperty (KnownProperties.AotAssemblies, value.ToString ()); }
		}

		public bool AndroidEnableProfiledAot {
			get { return string.Equals (GetProperty (KnownProperties.AndroidEnableProfiledAot), "True", StringComparison.OrdinalIgnoreCase); }
			set { SetProperty (KnownProperties.AndroidEnableProfiledAot, value.ToString ()); }
		}

		public bool EnableProguard {
			get { return string.Equals (GetProperty (KnownProperties.EnableProguard), "True", StringComparison.OrdinalIgnoreCase); }
			set { SetProperty (KnownProperties.EnableProguard, value.ToString ()); }
		}

		public bool EnableDesugar {
			get { return string.Equals (GetProperty (KnownProperties.AndroidEnableDesugar), "True", StringComparison.OrdinalIgnoreCase); }
			set { SetProperty (KnownProperties.AndroidEnableDesugar, value.ToString ()); }
		}

		public bool Deterministic {
			get { return string.Equals (GetProperty (KnownProperties.Deterministic), "True", StringComparison.OrdinalIgnoreCase); }
			set { SetProperty (KnownProperties.Deterministic, value.ToString ()); }
		}

		public bool AndroidUseSharedRuntime {
			get { return string.Equals (GetProperty (KnownProperties.AndroidUseSharedRuntime), "True", StringComparison.OrdinalIgnoreCase); }
			set { SetProperty (KnownProperties.AndroidUseSharedRuntime, value.ToString ()); }
		}

		public bool EmbedAssembliesIntoApk {
			get { return string.Equals (GetProperty (KnownProperties.EmbedAssembliesIntoApk), "True", StringComparison.OrdinalIgnoreCase); }
			set { SetProperty (KnownProperties.EmbedAssembliesIntoApk, value.ToString ()); }
		}

		public bool AndroidUseJavaDaemon {
			get { return string.Equals (GetProperty (KnownProperties.AndroidUseJavaDaemon), "True", StringComparison.OrdinalIgnoreCase); }
			set { SetProperty (KnownProperties.AndroidUseJavaDaemon, value.ToString ()); }
		}

		public string DexTool {
			get { return GetProperty (KnownProperties.AndroidDexTool); }
			set { SetProperty (KnownProperties.AndroidDexTool, value); }
		}

		public string ManifestMerger {
			get { return GetProperty (KnownProperties.AndroidManifestMerger); }
			set { SetProperty (KnownProperties.AndroidManifestMerger, value); }
		}

		public string LinkTool {
			get { return GetProperty (KnownProperties.AndroidLinkTool); }
			set { SetProperty (KnownProperties.AndroidLinkTool, value); }
		}

		public string AndroidFastDeploymentType {
			get { return GetProperty (KnownProperties.AndroidFastDeploymentType); }
			set { SetProperty (KnownProperties.AndroidFastDeploymentType, value); }
		}

		public bool UseJackAndJill {
			get { return string.Equals (GetProperty (KnownProperties.UseJackAndJill), "True", StringComparison.OrdinalIgnoreCase); }
			set { SetProperty (KnownProperties.UseJackAndJill, value.ToString ()); }
		}

		public bool AndroidExplicitCrunch {
			get { return string.Equals (GetProperty (KnownProperties.AndroidExplicitCrunch), "True", StringComparison.OrdinalIgnoreCase); }
			set { SetProperty (KnownProperties.AndroidExplicitCrunch, value.ToString ()); }
		}

		public AndroidLinkMode AndroidLinkModeDebug {
			get {
				AndroidLinkMode m;
				return Enum.TryParse<AndroidLinkMode> (GetProperty (DebugProperties, KnownProperties.AndroidLinkMode), out m) ? m : AndroidLinkMode.None;
			}
			set { SetProperty (DebugProperties, KnownProperties.AndroidLinkMode, value.ToString ()); }
		}

		public AndroidLinkMode AndroidLinkModeRelease {
			get {
				AndroidLinkMode m;
				return Enum.TryParse<AndroidLinkMode> (GetProperty (ReleaseProperties, KnownProperties.AndroidLinkMode), out m) ? m : AndroidLinkMode.None;
			}
			set { SetProperty (ReleaseProperties, KnownProperties.AndroidLinkMode, value.ToString ()); }
		}

		public string AndroidManifest { get; set; }
		public string LayoutMain { get; set; }
		public string MainActivity { get; set; }
		public string StringsXml { get; set; }
		public string PackageName { get; set; }
		public string JavaPackageName { get; set; }

		public override BuildOutput CreateBuildOutput (ProjectBuilder builder)
		{
			return new AndroidApplicationBuildOutput (this) { Builder = builder };
		}

		public void SetDefaultTargetDevice ()
		{
			SetProperty ("AdbTarget", Environment.GetEnvironmentVariable ("ADB_TARGET"));
		}

		public override string ProcessSourceTemplate (string source)
		{
			return source.Replace ("${ROOT_NAMESPACE}", RootNamespace ?? ProjectName)
				.Replace ("${PROJECT_NAME}", ProjectName)
				.Replace ("${PACKAGENAME}", PackageName)
				.Replace ("${JAVA_PACKAGENAME}", JavaPackageName);
		}
	}
}
