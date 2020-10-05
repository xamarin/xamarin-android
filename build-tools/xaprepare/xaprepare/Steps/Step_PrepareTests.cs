using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml;

namespace Xamarin.Android.Prepare
{
	partial class Step_PrepareTests : Step
	{
		public Step_PrepareTests ()
			: base ("Generating test suite files")
		{}

		protected override async Task<bool> Execute (Context context)
		{
			var tests = new Tests ();

			bool retVal = true;
			var projitems = new Dictionary<TestAPK, string> ();
			var apkTests = new List<TestAPK> ();
			var hostUnitTests = new List<TestHostUnit> ();
			foreach (var kvp in tests.AllTests) {
				string testName = kvp.Key;
				XATest testInstance = kvp.Value;

				Log.StatusLine ($" {context.Characters.Bullet} {testName} ", $"({testInstance.KindName})");
				bool result = true;

				switch (testInstance) {
					case TestAPK apk:
						result = GenerateAPKTestFiles (apk, apkTests, context, projitems);
						break;

					case TestHostUnit unit:
						hostUnitTests.Add (unit);
						break;

					default:
						throw new NotSupportedException ($"Unsupported test type {testInstance} ({testInstance.KindName})");
				};

				if (!result) {
					Log.ErrorLine ($"Failed to generate files for {testInstance.KindName} test instance '{testName}'");
					retVal = false;
				}
			}

			if (!retVal) {
				return false;
			}

			if (!GenerateRunTestsAPKProjitems (apkTests, context)) {
				return false;
			}

			if (!GenerateRunApkTestsProjitems (apkTests, context, projitems)) {
				return false;
			}

			if (!GenerateRunTestsRunApkTestsTarget (context, tests)) {
				return false;
			}

			if (!GenerateHostUnitTestItems (hostUnitTests, context)) {
				return false;
			}

			return true;
		}

		bool GenerateHostUnitTestItems (List<TestHostUnit> hostUnitTests, Context context)
		{
			(XmlWriterSettings xmlSettings, string filePath, string fileDirectory) = PrepareStandardItems ("RunTests.HostUnit.projitems", context);

			using (var xw = XmlWriter.Create (filePath, xmlSettings)) {
				WriteMSBuildProjectHeader (xw);

				xw.WriteStartElement ("ItemGroup");

				foreach (TestHostUnit unit in hostUnitTests) {
					xw.WriteStartElement ("_TestAssembly");
					xw.WriteAttributeString ("Include", $"$(MSBuildThisFileDirectory){Utilities.GetRelativePath (fileDirectory, unit.TestAssemblyPath)}");
					xw.WriteEndElement (); // _TestAssembly
				}

				xw.WriteEndElement (); // ItemGroup

				WriteMSBuildProjectFooter (xw);
			}

			WriteOperationStatus (context, true);
			return true;
		}

		bool GenerateRunTestsRunApkTestsTarget (Context context, Tests tests)
		{
			(XmlWriterSettings xmlSettings, string filePath, string fileDirectory) = PrepareStandardItems ("RunTests.RunApkTests.proj", context);
			string runApkTestsTargetsPath = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "tests", "RunApkTests.targets");

			using (var xw = XmlWriter.Create (filePath, xmlSettings)) {
				WriteMSBuildProjectHeader (xw);

				xw.WriteStartElement ("Target");
				xw.WriteAttributeString ("Name", "RunApkTests");

				WriteApkTestBinlogItems (xw, "_ApkTestProject");
				WriteApkTestBuildCommands (xw, "_ApkTestProject", fileDirectory, runApkTestsTargetsPath);

				xw.WriteStartElement ("PropertyGroup");

				xw.WriteStartElement ("_HostOS");
				xw.WriteValue ("$(HostOS)");
				xw.WriteEndElement (); // _HostOS

				xw.WriteStartElement ("_ExeExtension");
				xw.WriteEndElement (); // _ExeExtension

				xw.WriteStartElement ("_HostOS");
				xw.WriteAttributeString ("Condition", " '$(_HostOS)' == 'Windows' ");
				xw.WriteEndElement (); // _HostOS

				xw.WriteStartElement ("_ExeExtension");
				xw.WriteAttributeString ("Condition", " '$(_HostOS)' == 'Windows' ");
				xw.WriteValue (".exe");
				xw.WriteEndElement (); // _ExeExtension

				string fakePath = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "bin", "$(Configuration)", "lib", "xamarin.android", "xbuild", "Xamarin", "Android", "$(_HostOS)", "cross-arm$(_ExeExtension)");
				xw.WriteStartElement ("_CrossCompilerAvailable");
				xw.WriteAttributeString ("Condition", $"Exists('$(MSBuildThisFileDirectory){Utilities.GetRelativePath (fileDirectory, fakePath)}')");
				xw.WriteValue ("True");
				xw.WriteEndElement (); // _CrossCompilerAvailable

				xw.WriteStartElement ("_CrossCompilerAvailable");
				xw.WriteAttributeString ("Condition", " '$(_CrossCompilerAvailable)' == '' ");
				xw.WriteValue ("False");
				xw.WriteEndElement(); // _CrossCompilerAvailable

				xw.WriteEndElement (); // PropertyGroup

				WriteApkTestBinlogItems (xw, "_ApkTestProjectAot", logTag: "AOT");
				WriteApkTestBuildCommands (
					xw, "_ApkTestProjectAot", fileDirectory, runApkTestsTargetsPath,
					condition: " '$(_CrossCompilerAvailable)' == 'True' ",
					tests.GetApkTestPhasePropertiesExec (ApkTestPhase.AOT),
					tests.GetApkTestPhasePropertiesMSBuild (ApkTestPhase.AOT)
				);

				WriteApkTestBinlogItems (xw, "_ApkTestProjectProfiledAot", logTag: "profiled-AOT");
				WriteApkTestBuildCommands (
					xw, "_ApkTestProjectProfiledAot", fileDirectory, runApkTestsTargetsPath,
					condition: " '$(_CrossCompilerAvailable)' == 'True' ",
					tests.GetApkTestPhasePropertiesExec (ApkTestPhase.ProfiledAOT),
					tests.GetApkTestPhasePropertiesMSBuild (ApkTestPhase.ProfiledAOT)
				);

				WriteApkTestBinlogItems (xw, "_ApkTestProjectBundle", logTag: "Bundle");
				WriteApkTestBuildCommands (
					xw, "_ApkTestProjectBundle", fileDirectory, runApkTestsTargetsPath,
					condition: " '$(_CrossCompilerAvailable)' == 'True' ",
					tests.GetApkTestPhasePropertiesExec (ApkTestPhase.MonoBundle),
					tests.GetApkTestPhasePropertiesMSBuild (ApkTestPhase.MonoBundle)
				);

				xw.WriteEndElement (); // Target:RunApkTests
				WriteMSBuildProjectFooter (xw);
			}

			WriteOperationStatus (context, true);
			return true;
		}

		void WriteApkTestBuildCommands (XmlWriter xw, string itemName, string fileDirectory, string runApkTestsTargetsPath, string? condition = null, List<string>? execProperties = null, List<string>? msbuildProperties = null)
		{
			condition = condition ?? String.Empty;
			string execPropertiesValue = String.Empty;
			if (execProperties != null && execProperties.Count > 0) {
				execPropertiesValue = " /p:" + String.Join (" /p:", execProperties);
			}

			string msbuildPropertiesValue = String.Empty;
			if (msbuildProperties != null && msbuildProperties.Count > 0) {
				msbuildPropertiesValue = String.Join (";", msbuildProperties);
			}

			xw.WriteStartElement ("Exec");
			xw.WriteAttributeString ("Command", $"$(_XABuild) %({itemName}.Identity) %({itemName}._BinLog) /t:SignAndroidPackage $(_XABuildProperties){execPropertiesValue}");
			if (!String.IsNullOrEmpty (condition)) {
				xw.WriteAttributeString ("Condition", condition);
			}
			xw.WriteEndElement (); // Exec

			xw.WriteStartElement ("MSBuild");
			xw.WriteAttributeString ("ContinueOnError", "ErrorAndContinue");
			xw.WriteAttributeString ("Projects", $"$(MSBuildThisFileDirectory){Utilities.GetRelativePath (fileDirectory, runApkTestsTargetsPath)}");
			xw.WriteAttributeString ("Targets", "$(RunApkTestsTarget)");

			if (condition.Length > 0) {
				xw.WriteAttributeString ("Condition", condition);
			}

			if (msbuildPropertiesValue.Length > 0) {
				xw.WriteAttributeString ("Properties", msbuildPropertiesValue);
			}

			xw.WriteEndElement (); // MSBuild
		}

		void WriteApkTestBinlogItems (XmlWriter xw, string itemName, string? logTag = null)
		{
			if (!String.IsNullOrEmpty (logTag)) {
				logTag = $"-{logTag}";
			} else {
				logTag = String.Empty;
			}

			xw.WriteStartElement ("ItemGroup");
			xw.WriteAttributeString ("Condition", " '$(USE_MSBUILD)' == '1' ");

			xw.WriteStartElement (itemName);

			xw.WriteStartElement ("_BinLog");
			xw.WriteValue ($"$(_XABinLogPrefix)-$([System.DateTime]::Now.ToString (\"yyyyMMddTHHmmss\"))-%(Filename){logTag}.binlog\"");
			xw.WriteEndElement (); // _BinLog

			xw.WriteEndElement (); // itemName

			xw.WriteEndElement (); // ItemGroup
		}

		//
		// For inclusion in tests/RunApkTests.targets
		//
		bool GenerateRunApkTestsProjitems (List<TestAPK> apkTests, Context context, Dictionary<TestAPK, string> projitems)
		{
			(XmlWriterSettings xmlSettings, string filePath, string fileDirectory) = PrepareStandardItems ("RunApkTests.APK.projitems", context);

			using (var xw = XmlWriter.Create (filePath, xmlSettings)) {
				WriteMSBuildProjectHeader (xw);

				foreach (TestAPK apk in apkTests) {
					if (!projitems.TryGetValue (apk, out string projitemsPath)) {
						continue;
					}

					string projitemsDirectory = Path.GetDirectoryName (projitemsPath);
					xw.WriteStartElement ("Import");
					xw.WriteAttributeString ("Project", $"$(MSBuildThisFileDirectory){Utilities.GetRelativePath (projitemsDirectory, projitemsPath)}");
					apk.WriteExtraXML (APKTestMSBuildContext.RunApkTestsImportProjitems, xw);
					xw.WriteEndElement ();
				}

				WriteMSBuildProjectFooter (xw);
			}

			WriteOperationStatus (context, true);
			return true;
		}

		//
		// For inclusion in build-tools/scripts/RunTests.targets
		//
		bool GenerateRunTestsAPKProjitems (List<TestAPK> apkTests, Context context)
		{
			(XmlWriterSettings xmlSettings, string filePath, string fileDirectory) = PrepareStandardItems ("RunTests.APK.projitems", context);

			using (var xw = XmlWriter.Create (filePath, xmlSettings)) {
				WriteMSBuildProjectHeader (xw);
				xw.WriteStartElement ("ItemGroup");

				foreach (TestAPK apk in apkTests) {
					string elementName = String.Empty;
					switch (apk.TestFlavor) {
						case APKTestFlavor.Plain:
							elementName = "_ApkTestProject";
							break;

						case APKTestFlavor.AOT:
							elementName = "_ApkTestProjectAot";
							break;

						case APKTestFlavor.MonoBundle:
							elementName = "_ApkTestProjectBundle";
							break;

						default:
							continue;
					};

					xw.WriteStartElement (elementName);
					xw.WriteAttributeString ("Include", $"$(MSBuildThisFileDirectory){Utilities.GetRelativePath (fileDirectory, apk.ProjectPath)}");
					xw.WriteEndElement (); // elementName
				}

				xw.WriteEndElement (); // ItemGroup
				WriteMSBuildProjectFooter (xw);
				xw.Flush ();
			}

			WriteOperationStatus (context, true);
			return true;
		}

		bool GenerateAPKTestFiles (TestAPK apk, List<TestAPK> apkTests, Context context, Dictionary<TestAPK, string> projitems)
		{
			apkTests.Add (apk);

			string projectBaseName = Path.GetFileNameWithoutExtension (apk.ProjectPath);

			(XmlWriterSettings xmlSettings, string filePath, string fileDirectory) = PrepareStandardItems ($"{projectBaseName}.projitems", context);
			projitems.Add (apk, filePath);

			bool success;
			string errorMessage = String.Empty;
			string fullProjectPath = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, apk.ProjectPath);

			if (!File.Exists (fullProjectPath)) {
				errorMessage = $"Project file '{fullProjectPath}' does not exist for test '{apk.Name}'";
				success = false;
			} else {
				success = apk.SkipProjitemsGeneration || GenerateAPKProjitemsFile (apk, xmlSettings, filePath, fileDirectory, context);
			}

			WriteOperationStatus (context, success, errorMessage);

			return success;
		}

		bool GenerateAPKProjitemsFile (TestAPK apk, XmlWriterSettings xmlSettings, string filePath, string fileDirectory, Context context)
		{
			using (var xw = XmlWriter.Create (filePath, xmlSettings)) {
				WriteMSBuildProjectHeader (xw);

				xw.WriteStartElement ("ItemGroup");
				if (apk.TestFlavor != APKTestFlavor.AndroidApplicationBundle) {
					xw.WriteStartElement ("TestApk");
				} else {
					xw.WriteStartElement ("TestAab");
				}
				xw.WriteAttributeString ("Include", $"$(MSBuildThisFileDirectory){Utilities.GetRelativePath (fileDirectory, apk.ApkPath)}");

				WriteRequiredElement (xw, apk, "Package", apk.AndroidPackageName);
				WriteOptionalElement (xw, "Activity", apk.Activity);
				WriteOptionalRelativePath (xw, "ResultsPath", fileDirectory, apk.ResultsFilePath);
				WriteOptionalRelativePath (xw, "TimingDefinitionsFilename", fileDirectory, apk.TimingDefinitionsFilename);
				WriteOptionalRelativePath (xw, "TimingResultsFilename", fileDirectory, apk.TimingResultsFilename);
				WriteOptionalRelativePath (xw, "ApkSizesDefinitionFilename", fileDirectory, apk.ApkSizesDefinitionFilename);
				WriteOptionalRelativePath (xw, "ApkSizesResultsFilename", fileDirectory, apk.ApkSizesResultsFilename);
				WriteOptionalElement (xw, "ApkSizesInputFilename", apk.ApkSizesResultsFilename);

				xw.WriteEndElement (); // Test{Apk,Aab}
				xw.WriteEndElement (); // ItemGroup

				if (apk.Instrumentations.Count > 0) {
					xw.WriteStartElement ("ItemGroup");

					foreach (TestAndroidInstrumentation ti in apk.Instrumentations) {
						xw.WriteStartElement ("TestApkInstrumentation");
						xw.WriteAttributeString ("Include", ti.TypeName);

						WriteRequiredElement (xw, apk, "Package", apk.AndroidPackageName);
						WriteOptionalRelativePath (xw, "ResultsPath", fileDirectory, ti.ResultsPath);
						WriteOptionalElement (xw, "LogcatFilenameDistincion", ti.LogcatFilenameDistincion);
						if (ti.TimeoutInMS > 0) {
							WriteOptionalElement (xw, "TimeoutInMS", $"{ti.TimeoutInMS}");
						}

						xw.WriteEndElement (); // TestApkInstrumentation
					}

					xw.WriteEndElement (); // ItemGroup
				}

				if (apk.AndroidPermissions.Count > 0) {
					xw.WriteStartElement ("ItemGroup");

					foreach (string permission in apk.AndroidPermissions) {
						xw.WriteStartElement ("TestApkPermission");
						xw.WriteAttributeString ("Include", permission);

						WriteRequiredElement (xw, apk, "Package", apk.AndroidPackageName);

						xw.WriteEndElement (); // TestApkPermission
					}

					xw.WriteEndElement (); // ItemGroup
				}

				WriteMSBuildProjectFooter (xw);
				xw.Flush ();
			}

			return true;
		}

		void WriteRequiredElement (XmlWriter xw, XATest test, string elementName, string value)
		{
			if (String.IsNullOrEmpty (value)) {
				throw new InvalidOperationException ($"Test instance '{test.Name}' does not have value for a required element '{elementName}'");
			}

			WriteSimpleElement (xw, elementName, value);
		}

		void WriteOptionalElement (XmlWriter xw, string elementName, string value)
		{
			if (String.IsNullOrEmpty (value)) {
				return;
			}

			WriteSimpleElement (xw, elementName, value);
		}

		void WriteSimpleElement (XmlWriter xw, string elementName, string value)
		{
			xw.WriteStartElement (elementName);
			xw.WriteValue (value);
			xw.WriteEndElement ();
		}

		void WriteOptionalRelativePath (XmlWriter xw, string elementName, string fileDirectory, string filePath)
		{
			if (String.IsNullOrEmpty (filePath)) {
				return;
			}

			xw.WriteStartElement (elementName);
			xw.WriteValue ($"$(MSBuildThisFileDirectory){Utilities.GetRelativePath (fileDirectory, filePath)}");
			xw.WriteEndElement ();
		}

		void WriteMSBuildProjectHeader (XmlWriter xw)
		{
			xw.WriteStartElement ("Project");
			xw.WriteAttributeString ("ToolsVersion", "4.0");
		}

		void WriteMSBuildProjectFooter (XmlWriter xw)
		{
			xw.WriteEndElement (); // Project
		}

		(XmlWriterSettings xmlSettings, string filePath, string fileDirectory) PrepareStandardItems (string xmlDocumentFileName, Context context)
		{
			var xmlSettings = new XmlWriterSettings {
				Indent = true,
				Encoding = Utilities.UTF8NoBOM,
				IndentChars = "\t",
				OmitXmlDeclaration = false,
			};

			string filePath = Path.Combine (Configurables.Paths.TestBinDir, xmlDocumentFileName);
			string fileDirectory = Path.GetDirectoryName (filePath);

			Directory.CreateDirectory (fileDirectory);
			Log.Status ($" {context.Characters.Bullet} ", Utilities.GetRelativePath (BuildPaths.XamarinAndroidSourceRoot, filePath));

			return (xmlSettings, filePath, fileDirectory);
		}

		void WriteOperationStatus (Context context, bool success, string? errorMessage = null)
		{
			if (success) {
				Log.StatusLine ($" {context.Characters.Success}", ConsoleColor.Green);
			} else {
				Log.StatusLine ($" {context.Characters.Failure}", Log.ErrorColor);
				if (!String.IsNullOrEmpty (errorMessage)) {
					Log.StatusLine (errorMessage, Log.ErrorColor);
				}
			}
		}
	}
}
