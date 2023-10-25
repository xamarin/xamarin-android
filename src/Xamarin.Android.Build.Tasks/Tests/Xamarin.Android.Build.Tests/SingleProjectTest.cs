using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;
using NUnit.Framework;
using Xamarin.Android.Tools;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	[Parallelizable (ParallelScope.Children)]
	public partial class SingleProjectTest : BaseTest
	{
		static readonly object [] AndroidManifestPropertiesSource = new object [] {
			new object [] {
				/* versionName */  "2.1",
				/* versionCode */  "42",
				/* errorMessage */ "",
			},
			new object [] {
				/* versionName */  "1.0.0",
				/* versionCode */  "1.0.0",
				/* errorMessage */ "XA0003",
			},
			new object [] {
				/* versionName */  "3.1.3a1",
				/* versionCode */  "42",
				/* errorMessage */ "",
			},
			new object [] {
				/* versionName */  "6.0-preview.7",
				/* versionCode */  "42",
				/* errorMessage */ "",
			},
		};

		[Test]
		[TestCaseSource (nameof (AndroidManifestPropertiesSource))]
		public void AndroidManifestProperties (string versionName, string versionCode, string errorMessage)
		{
			var packageName = "com.xamarin.singleproject";
			var applicationIcon = "@drawable/icon";
			var applicationRoundIcon = "@drawable/icon";
			var applicationLabel = "My Sweet App";
			var proj = new XamarinAndroidApplicationProject ();
			proj.AndroidManifest = proj.AndroidManifest
				.Replace ("package=\"${PACKAGENAME}\"", "")
				.Replace ("android:label=\"${PROJECT_NAME}\"", "")
				.Replace ("android:versionName=\"1.0\"", "")
				.Replace ("android:versionCode=\"1\"", "");
			if (Builder.UseDotNet) {
				proj.SetProperty ("ApplicationIcon", applicationIcon);
				proj.SetProperty ("ApplicationRoundIcon", applicationRoundIcon);
			} else {
				proj.SetProperty ("GenerateApplicationManifest", "true");
			}
			proj.SetProperty ("ApplicationId", packageName);
			proj.SetProperty ("ApplicationTitle", applicationLabel);
			proj.SetProperty ("ApplicationVersion", versionCode);
			proj.SetProperty ("ApplicationDisplayVersion", versionName);

			using (var b = CreateApkBuilder ()) {
				if (!string.IsNullOrEmpty (errorMessage)) {
					b.ThrowOnBuildFailure = false;
					Assert.IsFalse (b.Build (proj), "Build should have failed.");
					StringAssertEx.Contains (errorMessage, b.LastBuildOutput, $"Build should fail with message '{errorMessage}'");
					return;
				}

				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var manifest = b.Output.GetIntermediaryPath ("android/AndroidManifest.xml");
				FileAssert.Exists (manifest);

				using (var stream = File.OpenRead (manifest)) {
					var doc = XDocument.Load (stream);
					Assert.AreEqual (packageName, doc.Root.Attribute ("package")?.Value);
					Assert.AreEqual (versionName, doc.Root.Attribute (AndroidAppManifest.AndroidXNamespace + "versionName")?.Value);
					Assert.AreEqual (versionCode, doc.Root.Attribute (AndroidAppManifest.AndroidXNamespace + "versionCode")?.Value);
					var application = doc.Root.Element ("application");
					Assert.AreEqual (applicationLabel, application.Attribute (AndroidAppManifest.AndroidXNamespace + "label")?.Value);
					if (Builder.UseDotNet) {
						Assert.AreEqual (applicationIcon, application.Attribute (AndroidAppManifest.AndroidXNamespace + "icon")?.Value);
						Assert.AreEqual (applicationRoundIcon, application.Attribute (AndroidAppManifest.AndroidXNamespace + "roundIcon")?.Value);
					}
				}

				var apk = b.Output.GetIntermediaryPath ($"android/bin/{packageName}.apk");
				FileAssert.Exists (apk);

				// NOTE: the $(Version) setting is only implemented in .NET 6
				if (!Builder.UseDotNet)
					return;
				// If not valid version, skip
				if (!Version.TryParse (versionName, out _))
					return;

				int index = versionName.IndexOf ('-');
				var versionNumber = index == -1 ?
					$"{versionName}.0.0" :
					$"{versionName.Substring (0, index)}.0.0";
				var assemblyPath = b.Output.GetIntermediaryPath ($"android/assets/{proj.ProjectName}.dll");
				FileAssert.Exists (assemblyPath);
				using var assembly = AssemblyDefinition.ReadAssembly (assemblyPath);

				// System.Reflection.AssemblyVersion
				Assert.AreEqual (versionNumber, assembly.Name.Version.ToString ());

				// System.Reflection.AssemblyFileVersion
				var assemblyInfoVersion = assembly.CustomAttributes.FirstOrDefault (a => a.AttributeType.FullName == "System.Reflection.AssemblyInformationalVersionAttribute");
				Assert.IsNotNull (assemblyInfoVersion, "Should find AssemblyInformationalVersionAttribute!");
				Assert.AreEqual (versionName, assemblyInfoVersion.ConstructorArguments [0].Value);

				// System.Reflection.AssemblyInformationalVersion
				var assemblyFileVersion = assembly.CustomAttributes.FirstOrDefault (a => a.AttributeType.FullName == "System.Reflection.AssemblyFileVersionAttribute");
				Assert.IsNotNull (assemblyFileVersion, "Should find AssemblyFileVersionAttribute!");
				Assert.AreEqual (versionNumber, assemblyFileVersion.ConstructorArguments [0].Value);
			}
		}

		[Test]
		public void AndroidManifestValuesWin ()
		{
			var packageName = "com.xamarin.singleproject";
			var applicationIcon = "@drawable/icon";
			var applicationRoundIcon = "@drawable/icon";
			var applicationLabel = "My Sweet App";
			var versionName = "99.0";
			var versionCode = "99";
			var proj = new XamarinAndroidApplicationProject ();
			proj.AndroidManifest = proj.AndroidManifest
				.Replace ("package=\"${PACKAGENAME}\"", $"package=\"{packageName}\"")
				.Replace ("android:label=\"${PROJECT_NAME}\"", $"android:label=\"{applicationLabel}\" android:icon=\"{applicationIcon}\" android:roundIcon=\"{applicationRoundIcon}\"")
				.Replace ("android:versionName=\"1.0\"", $"android:versionName=\"{versionName}\"")
				.Replace ("android:versionCode=\"1\"", $"android:versionCode=\"{versionCode}\"");
			if (Builder.UseDotNet) {
				proj.SetProperty ("ApplicationIcon", "@drawable/shouldnotbeused");
				proj.SetProperty ("ApplicationRoundIcon", "@drawable/shouldnotbeused");
			} else {
				proj.SetProperty ("GenerateApplicationManifest", "true");
			}
			proj.SetProperty ("ApplicationId", "com.i.should.not.be.used");
			proj.SetProperty ("ApplicationTitle", "I should not be used");
			proj.SetProperty ("ApplicationVersion", "21");
			proj.SetProperty ("ApplicationDisplayVersion", "1.1.1.1");

			using var b = CreateApkBuilder ();
			Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			var manifest = b.Output.GetIntermediaryPath ("android/AndroidManifest.xml");
			FileAssert.Exists (manifest);

			using var stream = File.OpenRead (manifest);
			var doc = XDocument.Load (stream);
			Assert.AreEqual (packageName, doc.Root.Attribute ("package")?.Value);
			Assert.AreEqual (versionName, doc.Root.Attribute (AndroidAppManifest.AndroidXNamespace + "versionName")?.Value);
			Assert.AreEqual (versionCode, doc.Root.Attribute (AndroidAppManifest.AndroidXNamespace + "versionCode")?.Value);
			var application = doc.Root.Element ("application");
			Assert.AreEqual (applicationLabel, application.Attribute (AndroidAppManifest.AndroidXNamespace + "label")?.Value);
			if (Builder.UseDotNet) {
				Assert.AreEqual (applicationIcon, application.Attribute (AndroidAppManifest.AndroidXNamespace + "icon")?.Value);
				Assert.AreEqual (applicationRoundIcon, application.Attribute (AndroidAppManifest.AndroidXNamespace + "roundIcon")?.Value);
			}
		}
	}
}
