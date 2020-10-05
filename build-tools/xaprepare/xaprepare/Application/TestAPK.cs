using System;
using System.Collections.Generic;
using System.Xml;

namespace Xamarin.Android.Prepare
{
	enum APKTestFlavor
	{
		Plain,
		AOT,
		AndroidApplicationBundle,
		MonoBundle,
		CheckedASAN,
		CheckedUBSAN,
	}

	/// <summary>
	///   Contexts in which the <see also="WriteExtraXML" /> method will call the registered handlers when MSBuild
	///   fragments are generated.
	/// </summary>
	enum APKTestMSBuildContext
	{
		/// <summary>
		///   Used when generating *.projitems &lt;ItemGroup/&gt; for inclusion in the RunApkTests.targets file.
		/// </summary>
		RunApkTestsImportProjitems,
	}

	class TestAPK : XATest
	{
		// Ugly...
		Dictionary<APKTestMSBuildContext, List<Action<XmlWriter, TestAPK>>>? extraXMLWriters;

		public override string KindName => "APK";
		public string ApkPath { get; }
		public string AndroidPackageName { get; }
		public bool IsApk { get; }
		public APKTestFlavor TestFlavor { get; }
		public string ProjectPath => TestFilePath;

		public bool SkipProjitemsGeneration { get; set; }

		/// <summary>
		///   Name of the Activity via which the test is launched. May be empty, in which case Instrumentations must not
		///   be an empty collection.
		/// </summary>
		public string Activity { get; set; } = String.Empty;

		public List<TestAndroidInstrumentation> Instrumentations { get; } = new List<TestAndroidInstrumentation> ();

		public List<string> AndroidPermissions { get; } = new List<string> ();

		public string TimingDefinitionsFilename { get; set; } = String.Empty;
		public string TimingResultsFilename { get; set; } = String.Empty;
		public string ApkSizesInputFilename { get; set; } = String.Empty;
		public string ApkSizesDefinitionFilename { get; set; } = String.Empty;
		public string ApkSizesResultsFilename { get; set; } = String.Empty;

		public TestAPK (string apkPath, string androidPackageName, string name, string projectFilePath, APKTestFlavor testFlavor = APKTestFlavor.Plain)
			: base (name, projectFilePath)
		{
			ApkPath = EnsureNonEmptyArgument (apkPath, nameof (apkPath));
			AndroidPackageName = EnsureNonEmptyArgument (androidPackageName, nameof (androidPackageName));
			TestFlavor = testFlavor;
		}

		/// <summary>
		///  <para>
		///   This is definitely clunky but allows us to add occasional context-sensitive (sorta kinda...) elements when
		///   generating the MSBuild fragments without having to derive new classes. This capability is used so
		///   infrequently that I think it's OK for it to be clunky while remaining simple.
		///  </para>
		///  <para>
		///    This method will always be called between xw.WriteStartElement and xw.WriteEndElement and after the
		///    generator has added its own attributes.
		///  </para>
		/// </summary>
		public void WriteExtraXML (APKTestMSBuildContext msbuildContext, XmlWriter xw)
		{
			if (extraXMLWriters == null || extraXMLWriters.Count == 0) {
				return;
			}

			if (!extraXMLWriters.TryGetValue (msbuildContext, out List<Action<XmlWriter, TestAPK>> actions)) {
				return;
			}

			foreach (var action in actions) {
				action (xw, this);
			}
		}

		/// <summary>
		///   Register an action to write extra attributes (or content, as needed) when generating MSBuild XML in the
		///   specified context. The action will always be called between xw.WriteStartElement and xw.WriteEndElement and after the
		///   generator has added its own attributes.
		/// </summary>
		public void AddExtraXMLWriter (APKTestMSBuildContext msbuildContext, Action<XmlWriter, TestAPK> action)
		{
			if (extraXMLWriters == null) {
				extraXMLWriters = new Dictionary<APKTestMSBuildContext, List<Action<XmlWriter, TestAPK>>> ();
			}

			if (!extraXMLWriters.TryGetValue (msbuildContext, out List<Action<XmlWriter, TestAPK>> actions)) {
				actions = new List<Action<XmlWriter, TestAPK>> ();
				extraXMLWriters.Add (msbuildContext, actions);
			}

			actions.Add (action);
		}
	}
}
