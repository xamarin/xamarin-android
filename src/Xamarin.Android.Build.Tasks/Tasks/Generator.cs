// Copyright (C) 2012 Xamarin, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class BindingsGenerator : AndroidDotnetToolTask
	{
		public override string TaskPrefix => "BGN";

		public bool OnlyRunXmlAdjuster { get; set; }

		public string XmlAdjusterOutput { get; set; }

		[Required]
		public string OutputDirectory { get; set; }

		public string EnumDirectory { get; set; }

		public string EnumMetadataDirectory { get; set; }

		[Required]
		public string AndroidApiLevel { get; set; }

		[Required]
		public string ApiXmlInput { get; set; }

		public string AssemblyName { get; set; }

		public string CodegenTarget { get; set; }

		public bool NoStdlib { get; set; }

		public string TypeMappingReportFile { get; set; }

		public bool UseShortFileNames { get; set; }

		// apart from ReferencedManagedLibraries we need it to find mscorlib.dll.
		[Required]
		public string MonoAndroidFrameworkDirectories { get; set; }

		public string LangVersion { get; set; }

		public bool EmitLegacyInterfaceInvokers { get; set; }

		public bool EnableBindingStaticAndDefaultInterfaceMethods { get; set; }
		public bool EnableBindingNestedInterfaceTypes { get; set; }
		public bool EnableBindingInterfaceConstants { get; set; }
		public string EnableRestrictToAttributes { get; set; }
		public bool EnableObsoleteOverrideInheritance { get; set; }
		public string Nullable { get; set; }

		public ITaskItem[] TransformFiles { get; set; }
		public ITaskItem[] ReferencedManagedLibraries { get; set; }
		public ITaskItem[] AnnotationsZipFiles { get; set; }
		public ITaskItem[] NamespaceTransforms { get; set; }

		public ITaskItem[] JavadocXml { get; set; }
		public string JavadocVerbosity { get; set; }

		public bool UseJavaLegacyResolver { get; set; }

		private List<Tuple<string, string>> transform_files = new List<Tuple<string,string>> ();

		public override bool RunTask ()
		{
			Directory.CreateDirectory (OutputDirectory);

			// We need to do this validation in Execute rather than GenerateCommandLineCommands
			// because we can't terminate the build nicely in GenerateCommandLineCommands.
			if (TransformFiles != null)
				foreach (var fixup in TransformFiles) {
					try {
						var doc = XDocument.Load (fixup.ItemSpec);

						switch (doc.Root.Name.LocalName) {
							case "metadata":
								Log.LogDebugMessage ("Adding transform file {0} as metadata.", fixup.ItemSpec);
								transform_files.Add (new Tuple<string, string> (fixup.ItemSpec, "fixup"));
								break;
							case "enum-field-mappings":
								Log.LogDebugMessage ("Adding transform file {0} as enumfields.", fixup.ItemSpec);
								transform_files.Add (new Tuple<string, string> (fixup.ItemSpec, "enumfields"));
								break;
							case "enum-method-mappings":
								Log.LogDebugMessage ("Adding transform file {0} as enummethods.", fixup.ItemSpec);
								transform_files.Add (new Tuple<string, string> (fixup.ItemSpec, "enummethods"));
								break;
							default:
								Log.LogCodedError (
									code: "XA4229",
									file: fixup.ItemSpec,
									lineNumber: 0,
									message: Properties.Resources.XA4229,
									messageArgs: new [] {
										doc.Root.Name.LocalName
									}
								);
								return false;
						}
					} catch (Exception ex) {
						Log.LogCodedError (
							code: "XA4230",
							file: fixup.ItemSpec,
							lineNumber: 0,
							message: Properties.Resources.XA4230,
							messageArgs: new [] {
								ex
							}
						);
						return false;
					}
				}


			if (NamespaceTransforms?.Any () == true)
				foreach (var nt in NamespaceTransforms) {
					if (!nt.MetadataNames.OfType<string> ().Contains ("Replacement", StringComparer.OrdinalIgnoreCase))
						Log.LogCodedError (
							code: "XA4233",
							message: Properties.Resources.XA4233,
							messageArgs: new [] { nt.ItemSpec }
						);
				}

			if (Log.HasLoggedErrors)
				return false;

			return base.RunTask ();
		}

		void WriteLine (StreamWriter sw, string line)
		{
			sw.WriteLine (line);
			Log.LogDebugMessage (line);
		}

		protected override string GenerateCommandLineCommands ()
		{
			var cmd = GetCommandLineBuilder ();

			if (NamespaceTransforms?.Any () == true) {
				// ex: obj/Debug/generated/msbuild-metadata.xml
				var transform_file = Path.Combine (OutputDirectory, "..", "msbuild-metadata.xml");

				var root = new XElement ("metadata");

				foreach (var nt in NamespaceTransforms)
					root.Add (new XElement ("ns-replace", new XAttribute ("source", nt.ItemSpec), new XAttribute ("replacement", nt.GetMetadata ("replacement"))));

				using (var xml_writer = XmlWriter.Create (transform_file, new XmlWriterSettings { Indent = true }))
					root.WriteTo (xml_writer);

				transform_files.Add (new Tuple<string, string> (transform_file, "fixup"));
			}

			string responseFile = Path.Combine (OutputDirectory, "generator.rsp");
			Log.LogDebugMessage ("[Generator] response file: {0}", responseFile);
			using (var sw = new StreamWriter (responseFile, append: false, encoding: Files.UTF8withoutBOM)) {

				if (OnlyRunXmlAdjuster)
					WriteLine (sw, "--only-xml-adjuster");
				if (!string.IsNullOrEmpty (XmlAdjusterOutput))
					WriteLine (sw, $"--xml-adjuster-output=\"{XmlAdjusterOutput}\"");

				if (!string.IsNullOrEmpty (CodegenTarget))
					WriteLine (sw, $"--codegen-target={CodegenTarget}");
				if (!string.IsNullOrEmpty (OutputDirectory))
					WriteLine (sw, $"--csdir=\"{OutputDirectory}\"");
				if (!string.IsNullOrEmpty (EnumDirectory))
					WriteLine (sw, $"--enumdir=\"{EnumDirectory}\"");
				if (!string.IsNullOrEmpty (EnumMetadataDirectory))
					WriteLine (sw, $"--enummetadata=\"{EnumMetadataDirectory}\"");
				if (!string.IsNullOrEmpty (AssemblyName))
					WriteLine (sw, $"--assembly=\"{AssemblyName}\"");

				if (!NoStdlib) {
					string fxpath = MonoAndroidFrameworkDirectories.Split (';').First (p => new DirectoryInfo (p).GetFiles ("mscorlib.dll").Any ());
					WriteLine (sw, $"--ref=\"{Path.Combine (Path.GetFullPath (fxpath), "mscorlib.dll")}\"");
				}

				if (ReferencedManagedLibraries != null)
					foreach (var lib in ReferencedManagedLibraries)
						WriteLine (sw, $"--ref=\"{Path.GetFullPath (lib.ItemSpec)}\"");
				if (AnnotationsZipFiles != null)
					foreach (var zip in AnnotationsZipFiles)
						WriteLine (sw, $"--annotations=\"{Path.GetFullPath (zip.ItemSpec)}\"");

				foreach (var tf in transform_files)
					WriteLine (sw, $"\"--{tf.Item2}={tf.Item1}\"");

				if (!string.IsNullOrEmpty (AndroidApiLevel))
					WriteLine (sw, $"--api-level={AndroidApiLevel}");

				if (!string.IsNullOrEmpty (TypeMappingReportFile))
					WriteLine (sw, $"--type-map-report=\"{TypeMappingReportFile}\"");

				WriteLine (sw, "--global");
				WriteLine (sw, "--public");

				if (UseShortFileNames)
					WriteLine (sw, "--use-short-file-names");

				if (SupportsCSharp8) {
					var features = new List<string> ();

					if (EmitLegacyInterfaceInvokers) {
						features.Add ("emit-legacy-interface-invokers");
					}

					if (EnableBindingInterfaceConstants)
						features.Add ("interface-constants");

					if (EnableBindingNestedInterfaceTypes)
						features.Add ("nested-interface-types");

					if (EnableBindingStaticAndDefaultInterfaceMethods)
						features.Add ("default-interface-methods");

					if (!EnableObsoleteOverrideInheritance)
						features.Add ("do-not-fix-obsolete-overrides");

					if (string.Equals (EnableRestrictToAttributes, "obsolete", StringComparison.OrdinalIgnoreCase))
						features.Add ("restrict-to-attributes");

					if (string.Equals (Nullable, "enable", StringComparison.OrdinalIgnoreCase))
						features.Add ("nullable-reference-types");

					if (features.Any ())
						WriteLine (sw, $"--lang-features={string.Join (",", features)}");
				}

				if (!string.IsNullOrEmpty (JavadocVerbosity))
					WriteLine (sw, $"\"--doc-comment-verbosity={JavadocVerbosity}\"");

				if (JavadocXml != null) {
					foreach (var xml in JavadocXml) {
						WriteLine (sw, $"\"--with-javadoc-xml={Path.GetFullPath (xml.ItemSpec)}\"");
					}
				}

				if (UseJavaLegacyResolver)
					WriteLine (sw, "--use-legacy-java-resolver=true");
			}

			cmd.AppendSwitch ($"\"{ApiXmlInput}\"");
			cmd.AppendSwitch ($"\"@{responseFile}\"");
			return cmd.ToString ();
		}

		protected override void LogEventsFromTextOutput (string singleLine, MessageImportance messageImportance)
		{
			base.LogEventsFromTextOutput (singleLine, messageImportance);

			if (messageImportance != StandardErrorLoggingImportance)
				return;

			Log.LogFromStandardError ("BG0000", singleLine);
		}

		bool SupportsCSharp8 {
			get {
				// These are the values that pre-date C# 8.  We assume any
				// new value we encounter is something that supports it.
				switch (LangVersion) {
					case "7.3":
					case "7.2":
					case "7.1":
					case "7":
					case "6":
					case "5":
					case "4":
					case "3":
					case "ISO-2":
					case "ISO-1":
						return false;
				}

				return true;
			}
		}
	}
}
