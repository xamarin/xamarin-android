using System;
using System.Linq;
using NUnit.Framework;
using Xamarin.ProjectTools;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Xamarin.Tools.Zip;
using System.Collections.Generic;

namespace Xamarin.Android.Build.Tests
{
	[Parallelizable (ParallelScope.Children)]
	public partial class ManifestTest : BaseTest
	{
		readonly string TargetSdkManifest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" android:versionCode=""1"" android:versionName=""1.0"" package=""Bug12935.Bug12935"">
	<uses-sdk android:targetSdkVersion=""{0}""/>
	<application android:label=""Bug12935"">
	</application>
</manifest>";

		readonly string ElementOrderManifest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" android:versionCode=""1"" android:versionName=""1.0"" package=""CheckElementReOrdering.CheckElementReOrdering"">
	<uses-sdk android:targetSdkVersion=""21""/>
	<application android:label=""CheckElementReOrdering"">
	</application>
	<permission android:name=""com.xamarin.test.TEST"" android:label=""Test Permission"" />
	<permission-tree android:name=""com.xamarin.test"" />
	<permission-group android:name=""group1"" />
	<uses-feature android:name=""android.hardware.camera"" />
	<supports-gl-texture android:name=""GL_OES_compressed_ETC1_RGB8_texture"" />
	<uses-permission android:name=""android.permission.CAMERA"" />
</manifest>";

		readonly string ScreenOrientationActivity = @"
using System;

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;

namespace Bug12935
{
	[Activity (Label = ""Bug12935"",
		MainLauncher = true,
		Icon = ""@drawable/icon"",
		ScreenOrientation = ScreenOrientation.SensorPortrait
	)]
	public class MainActivity : Activity
	{
		int count = 1;

		protected override void OnCreate (Bundle bundle)
		{
			base.OnCreate (bundle);
		}
	}
}
";

		[Test]
		public void Bug12935 ([Values (true, false)] bool useAapt2)
		{
			AssertAaptSupported (useAapt2);
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.MainActivity = ScreenOrientationActivity;
			proj.AndroidUseAapt2 = useAapt2;
			var directory = $"temp/Bug12935_{useAapt2}";
			using (var builder = CreateApkBuilder (directory)) {

				proj.TargetFrameworkVersion = "v4.2";
				proj.AndroidManifest = string.Format (TargetSdkManifest, "17");
				Assert.IsTrue (builder.Build (proj), "Build for TargetFrameworkVersion 17 should have succeeded");
				var manifestFile = Path.Combine (Root, builder.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");

				XDocument doc = XDocument.Load (manifestFile);
				var ns = doc.Root.GetNamespaceOfPrefix ("android");
				var screenOrientationXName = XName.Get ("screenOrientation", ns.NamespaceName);
				var targetSdkXName = XName.Get ("targetSdkVersion", ns.NamespaceName);

				var usesSdk = doc.XPathSelectElement ("/manifest/uses-sdk");
				Assert.IsNotNull (usesSdk, "Failed to read the uses-sdk element");
				var targetSdk = usesSdk.Attribute (targetSdkXName);
				Assert.AreEqual ("17", targetSdk.Value, "targetSdkVersion should have been 17");

				var activityElement = doc.XPathSelectElement ("/manifest/application/activity");
				Assert.IsNotNull (activityElement, "Failed to read the activity element");
				var screenOrientation = activityElement.Attribute (screenOrientationXName);
				Assert.IsNotNull (screenOrientation, "activity element did not contain a android:screenOrientation attribute");
				Assert.AreEqual ("sensorPortrait", screenOrientation.Value, "screenOrientation should have been sensorPortrait");

				builder.Cleanup ();
				proj.TargetFrameworkVersion = "v4.1";
				proj.AndroidManifest = string.Format (TargetSdkManifest, "16");
				Assert.IsTrue (builder.Build (proj), "Build for TargetFrameworkVersion 16 should have succeeded");

				doc = XDocument.Load (manifestFile);
				usesSdk = doc.XPathSelectElement ("/manifest/uses-sdk");
				Assert.IsNotNull (usesSdk, "Failed to read the uses-sdk element");
				targetSdk = usesSdk.Attribute (targetSdkXName);
				Assert.AreEqual ("16", targetSdk.Value, "targetSdkVersion should have been 16");
				activityElement = doc.XPathSelectElement ("/manifest/application/activity");
				Assert.IsNotNull (activityElement, "Failed to read the activity element");
				screenOrientation = activityElement.Attribute (screenOrientationXName);
				Assert.AreEqual ("sensorPortrait", screenOrientation.Value, "screenOrientation for targetSdkVersion 16 should have been sensorPortrait");

				builder.Cleanup ();
				builder.ThrowOnBuildFailure = false;
				proj.TargetFrameworkVersion = "v4.0.3";
				proj.AndroidManifest = string.Format (TargetSdkManifest, "15");
				Assert.IsFalse (builder.Build (proj), "Build for TargetFrameworkVersion 15 should have failed");
				StringAssertEx.Contains (useAapt2 ? "APT2259: " : "APT1134: ", builder.LastBuildOutput);
				StringAssertEx.Contains (useAapt2 ? "APT2067" : "", builder.LastBuildOutput);
				StringAssertEx.Contains (Path.Combine ("Properties", "AndroidManifest.xml"), builder.LastBuildOutput);
				StringAssertEx.Contains ($"{(useAapt2 ? "2" : "1")} Error(s)", builder.LastBuildOutput);
			}
		}

		[Test]
		public void CheckElementReOrdering ([Values (true, false)] bool useAapt2)
		{
			AssertAaptSupported (useAapt2);
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.MainActivity = ScreenOrientationActivity;
			proj.AndroidUseAapt2 = useAapt2;
			using (var builder = CreateApkBuilder ()) {
				proj.AndroidManifest = ElementOrderManifest;
				Assert.IsTrue (builder.Build (proj), "first build should have succeeded");
				var manifestFile = Path.Combine (Root, builder.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				XDocument doc = XDocument.Load (manifestFile);
				var ns = doc.Root.GetNamespaceOfPrefix ("android");
				var manifest = GetElement (doc, "manifest");
				var app = GetElement (manifest, "application");
				Assert.AreEqual (0, app.ElementsAfterSelf ().Count (),
					"There should be no elements after the application element");
				var activity = GetElement (app, "activity");
				AssertAttribute (activity, ns + "exported", "true");
				var intent_filter = GetElement (activity, "intent-filter");
				var action = GetElement (intent_filter, "action");
				AssertAttribute (action, ns + "name", "android.intent.action.MAIN");
				var category = GetElement (intent_filter, "category");
				AssertAttribute (category, ns + "name", "android.intent.category.LAUNCHER");

				// Add Exported=true and build again
				proj.MainActivity = proj.MainActivity.Replace ("MainLauncher = true,", "MainLauncher = true, Exported = true,");
				proj.Touch ("MainActivity.cs");
				Assert.IsTrue (builder.Build (proj), "second build should have succeeded");
			}

			static XElement GetElement (XContainer parent, XName name)
			{
				var e = parent.Element (name);
				Assert.IsNotNull (e, $"{name} element should not be null.");
				return e;
			}

			static void AssertAttribute (XElement parent, XName name, string expected)
			{
				var a = parent.Attribute (name);
				Assert.IsNotNull (a, $"{name} attribute should not be null.");
				Assert.AreEqual (expected, a.Value, $"{name} attribute value did not match.");
			}
		}

		[Test]
		public void OverlayManifestTest ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
				ManifestMerger = "manifestmerger.jar",
			};
			proj.AndroidManifest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" xmlns:tools=""http://schemas.android.com/tools"" android:versionCode=""1"" android:versionName=""1.0"" package=""foo.foo"">
	<application android:label=""foo"">
	</application>
</manifest>";
			proj.OtherBuildItems.Add (new BuildItem ("AndroidManifestOverlay", "ManifestOverlay.xml") {
				TextContent = () => @"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"">
	<uses-permission android:name=""android.permission.CAMERA"" />
</manifest>
"
			});
			using (var b = CreateApkBuilder ("temp/OverlayManifestTest", cleanupAfterSuccessfulBuild: true, cleanupOnDispose: false)) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var manifestFile = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				var text = File.ReadAllText (manifestFile);
				StringAssert.Contains ("android.permission.CAMERA", text, $"{manifestFile} should contain 'android.permission.CAMERA'");
			}
		}

		[Test]
		public void RemovePermissionTest ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
				PackageReferences = {
					KnownPackages.ZXing_Net_Mobile,
				},
				ManifestMerger = "manifestmerger.jar",
			};
			proj.AndroidManifest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" xmlns:tools=""http://schemas.android.com/tools"" android:versionCode=""1"" android:versionName=""1.0"" package=""foo.foo"">
	<uses-sdk android:targetSdkVersion=""29""/>
	<uses-permission android:name=""android.permission.CAMERA"" tools:node=""remove"" />
	<application android:label=""foo"">
	</application>
</manifest>";
			using (var b = CreateApkBuilder ("temp/RemovePermissionTest", cleanupAfterSuccessfulBuild: true, cleanupOnDispose: false)) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var manifestFile = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				var text = File.ReadAllText (manifestFile);
				StringAssert.DoesNotContain ("android.permission.CAMERA", text, $"{manifestFile} should not contain 'android.permission.CAMERA'");
			}
		}

		[Test]
		public void IntentFilterData ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			string declHead = "public class MainActivity";
			string intentFilter = @"[IntentFilter (new string [] {""action1""},
				DataPath = ""foo"",
				DataPathPattern = ""foo*"",
				DataPathPrefix = ""foo"",
				Label = ""testTarget""
				)]
				";
			proj.MainActivity = proj.DefaultMainActivity.Replace (declHead, intentFilter + declHead);
			using (var b = CreateApkBuilder ("temp/IntentFilterData", true, false)) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var manifest = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				var doc = XDocument.Load (manifest);
				var nsResolver = new XmlNamespaceManager (new NameTable ());
				nsResolver.AddNamespace ("android", "http://schemas.android.com/apk/res/android");
				Assert.AreEqual (3, doc.XPathSelectElements ("//intent-filter[@android:label='testTarget']/data", nsResolver).Count (), "intent-filter/data count mismatch.");
			}
		}

		[Test]
		public void IntentFilterDataLists ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			string declHead = "public class MainActivity";
			string intentFilter = @"[IntentFilter (new string [] {""action1""},
				DataPaths = new string [] {""foo"", ""bar""},
				DataPathPatterns = new string [] {""foo*"", ""bar*""},
				DataPathPrefixes = new string [] {""foo"", ""bar""},
				DataHosts = new string [] {""foo.com"", ""bar.com""},
				DataPorts = new string [] {""10000"", ""20000""},
				DataSchemes = new string [] {""http"", ""ftp""},
				DataMimeTypes = new string [] {""text/html"", ""text/xml""},
				Label = ""testTarget""
				)]
				";
			proj.MainActivity = proj.DefaultMainActivity.Replace (declHead, intentFilter + declHead);
			using (var b = CreateApkBuilder ("temp/IntentFilterDataLists", true, false)) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var manifest = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				var doc = XDocument.Load (manifest);
				var nsResolver = new XmlNamespaceManager (new NameTable ());
				nsResolver.AddNamespace ("android", "http://schemas.android.com/apk/res/android");
				Assert.AreEqual (14, doc.XPathSelectElements ("//intent-filter[@android:label='testTarget']/data", nsResolver).Count (), "intent-filter/data count mismatch.");
			}
		}

		[Test]
		public void IntentFilterMultipleItems ()
		{
			// somehow the tests above passed but this example failed.
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.MainActivity = proj.DefaultMainActivity.Replace (
				"public class MainActivity ",
				@"[IntentFilter (new string []{""foo""}, DataSchemes=new string []{""http"", ""https""})] public class MainActivity ");
			var b = CreateApkBuilder ("temp/IntentFilterMultipleItems");
			Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
			var appsrc = File.ReadAllText (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml"));
			Assert.IsTrue (appsrc.Contains ("<data android:scheme=\"http\""), "schemes:http");
			Assert.IsTrue (appsrc.Contains ("<data android:scheme=\"https\""), "schemes:https");
			b.Dispose ();
		}

		[Test]
		public void LayoutAttributeElement ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				TargetFrameworkVersion = "v7.0",
				IsRelease = true,
			};
			string declHead = "public class MainActivity";
			string layout = @"[Layout (DefaultWidth=""500dp"", DefaultHeight=""600dp"", Gravity=""center"", MinWidth=""300dp"", MinHeight=""400dp"")]";
			proj.MainActivity = proj.DefaultMainActivity.Replace (declHead, layout + declHead);
			using (var b = CreateApkBuilder ("temp/LayoutAttributeElement", true, false)) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var manifest = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				var doc = XDocument.Load (manifest);
				var nsResolver = new XmlNamespaceManager (new NameTable ());
				nsResolver.AddNamespace ("android", "http://schemas.android.com/apk/res/android");
				var le = doc.XPathSelectElement ("//layout") as XElement;
				Assert.IsNotNull (le, "no layout element found");
				Assert.IsTrue (doc.XPathSelectElements ("//layout[@android:defaultWidth='500dp' and @android:defaultHeight='600dp' and @android:gravity='center' and @android:minWidth='300dp' and @android:minHeight='400dp']", nsResolver).Any (),
							   "'layout' element is not generated as expected.");
			}
		}

		[Test]
		public void DirectBootAwareAttribute ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				TargetFrameworkVersion = "v7.0",
				IsRelease = true,
			};
			string attrHead = ", Activity (";
			string attr = @", Activity (DirectBootAware=true, ";
			proj.MainActivity = proj.DefaultMainActivity.Replace (attrHead, attr);
			proj.OtherBuildItems.Add (new BuildItem (BuildActions.Compile, "MyService.cs") {
				TextContent = () => "using Android.App; [Service (DirectBootAware = true)] public class MyService : Service { public override Android.OS.IBinder OnBind (Android.Content.Intent intent) { return null; } }"
			});
			proj.OtherBuildItems.Add (new BuildItem (BuildActions.Compile, "MyApplication.cs") {
				TextContent = () => "using Android.App; [Application (DirectBootAware = true)] public class MyApplication : Application {}"
			});
			using (var b = CreateApkBuilder ("temp/DirectBootAwareAttribute", true, false)) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var manifest = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				var doc = XDocument.Load (manifest);
				var nsResolver = new XmlNamespaceManager (new NameTable ());
				nsResolver.AddNamespace ("android", "http://schemas.android.com/apk/res/android");
				var le = doc.XPathSelectElement ("//activity") as XElement;
				Assert.IsNotNull (le, "no activity element found");
				Assert.IsTrue (doc.XPathSelectElements ("//activity[@android:directBootAware='true']", nsResolver).Any (),
						   "'activity' element is not generated as expected.");
				Assert.IsTrue (doc.XPathSelectElements ("//provider[@android:name='mono.MonoRuntimeProvider' and @android:directBootAware='true']", nsResolver).Any (),
						   "'provider' element is not generated as expected.");
			}
		}

		static object [] VersionCodeTestSource = new object [] {
			new object[] {
				/* seperateApk */ false,
				/* abis */ "armeabi-v7a",
				/* versionCode */ "123",
				/* useLagacy */ true,
				/* pattern */ null,
				/* props */ null,
				/* shouldBuild */ true,
				/* expected */ "123",
			},
			new object[] {
				/* seperateApk */ false,
				/* abis */ "armeabi-v7a",
				/* versionCode */ "123",
				/* useLagacy */ false,
				/* pattern */ null,
				/* props */ null,
				/* shouldBuild */ true,
				/* expected */ "123",
			},
			new object[] {
				/* seperateApk */ false,
				/* abis */ "armeabi-v7a",
				/* versionCode */ "123",
				/* useLagacy */ false,
				/* pattern */ "{abi}{versionCode}",
				/* props */ null,
				/* shouldBuild */ true,
				/* expected */ "123",
			},
			new object[] {
				/* seperateApk */ false,
				/* abis */ "armeabi-v7a",
				/* versionCode */ "1",
				/* useLagacy */ false,
				/* pattern */ "{abi}{versionCode}",
				/* props */ "versionCode=123",
				/* shouldBuild */ true,
				/* expected */ "123",
			},
			new object[] {
				/* seperateApk */ false,
				/* abis */ "armeabi-v7a;x86",
				/* versionCode */ "123",
				/* useLagacy */ false,
				/* pattern */ "{abi}{versionCode}",
				/* props */ null,
				/* shouldBuild */ true,
				/* expected */ "123",
			},
			new object[] {
				/* seperateApk */ true,
				/* abis */ "armeabi-v7a;x86",
				/* versionCode */ "123",
				/* useLagacy */ true,
				/* pattern */ null,
				/* props */ null,
				/* shouldBuild */ true,
				/* expected */ "131195;196731",
			},
			new object[] {
				/* seperateApk */ true,
				/* abis */ "armeabi-v7a;x86",
				/* versionCode */ "123",
				/* useLagacy */ false,
				/* pattern */ null,
				/* props */ null,
				/* shouldBuild */ true,
				/* expected */ "200123;300123",
			},
			new object[] {
				/* seperateApk */ true,
				/* abis */ "armeabi-v7a;x86",
				/* versionCode */ "123",
				/* useLagacy */ false,
				/* pattern */ "{abi}{versionCode}",
				/* props */ null,
				/* shouldBuild */ true,
				/* expected */ "2123;3123",
			},
			new object[] {
				/* seperateApk */ true,
				/* abis */ "armeabi-v7a;x86",
				/* versionCode */ "12",
				/* useLagacy */ false,
				/* pattern */ "{abi}{minSDK:00}{versionCode:000}",
				/* props */ null,
				/* shouldBuild */ true,
				/* expected */ "219012;319012",
			},
			new object[] {
				/* seperateApk */ true,
				/* abis */ "armeabi-v7a;x86",
				/* versionCode */ "12",
				/* useLagacy */ false,
				/* pattern */ "{abi}{minSDK:00}{screen}{versionCode:000}",
				/* props */ "screen=24",
				/* shouldBuild */ true,
				/* expected */ "21924012;31924012",
			},
			new object[] {
				/* seperateApk */ true,
				/* abis */ "armeabi-v7a;x86",
				/* versionCode */ "12",
				/* useLagacy */ false,
				/* pattern */ "{abi}{minSDK:00}{screen}{foo:0}{versionCode:000}",
				/* props */ "screen=24;foo=$(Foo)",
				/* shouldBuild */ true,
				/* expected */ "219241012;319241012",
			},
			new object[] {
				/* seperateApk */ true,
				/* abis */ "armeabi-v7a;x86",
				/* versionCode */ "12",
				/* useLagacy */ false,
				/* pattern */ "{abi}{minSDK:00}{screen}{foo:00}{versionCode:000}",
				/* props */ "screen=24;foo=$(Foo)",
				/* shouldBuild */ false,
				/* expected */ "2192401012;3192401012",
			},
		};

		[Test]
		[TestCaseSource(nameof (VersionCodeTestSource))]
		public void VersionCodeTests (bool seperateApk, string abis, string versionCode, bool useLegacy, string versionCodePattern, string versionCodeProperties, bool shouldBuild, string expectedVersionCode)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
				MinSdkVersion = null,
			};
			proj.SetProperty ("Foo", "1");
			proj.SetProperty ("GenerateApplicationManifest", "false"); // Disable $(AndroidVersionCode) support
			proj.SetProperty (proj.ReleaseProperties, KnownProperties.AndroidCreatePackagePerAbi, seperateApk);
			if (!string.IsNullOrEmpty (abis))
				proj.SetAndroidSupportedAbis (abis);
			if (!string.IsNullOrEmpty (versionCodePattern))
				proj.SetProperty (proj.ReleaseProperties, "AndroidVersionCodePattern", versionCodePattern);
			else
				proj.RemoveProperty (proj.ReleaseProperties, "AndroidVersionCodePattern");
			if (!string.IsNullOrEmpty (versionCodeProperties))
				proj.SetProperty (proj.ReleaseProperties, "AndroidVersionCodeProperties", versionCodeProperties);
			else
				proj.RemoveProperty (proj.ReleaseProperties, "AndroidVersionCodeProperties");
			if (useLegacy)
				proj.SetProperty (proj.ReleaseProperties, "AndroidUseLegacyVersionCode", true);
			proj.AndroidManifest = proj.AndroidManifest.Replace ("android:versionCode=\"1\"", $"android:versionCode=\"{versionCode}\"");
			using (var builder = CreateApkBuilder (Path.Combine ("temp", TestName), false, false)) {
				builder.ThrowOnBuildFailure = false;
				Assert.AreEqual (shouldBuild, builder.Build (proj), shouldBuild ? "Build should have succeeded." : "Build should have failed.");
				if (!shouldBuild)
					return;
				var abiItems = seperateApk ? abis.Split (';') : new string[1];
				var expectedItems = expectedVersionCode.Split (';');
				XNamespace aNS = "http://schemas.android.com/apk/res/android";
				Assert.AreEqual (abiItems.Length, expectedItems.Length, "abis parameter should have matching elements for expected");
				for (int i = 0; i < abiItems.Length; i++) {
					var path = seperateApk ? Path.Combine ("android", abiItems[i], "AndroidManifest.xml") : Path.Combine ("android", "manifest", "AndroidManifest.xml");
					var manifest = builder.Output.GetIntermediaryAsText (Root, path);
					var doc = XDocument.Parse (manifest);
					var nsResolver = new XmlNamespaceManager (new NameTable ());
					nsResolver.AddNamespace ("android", "http://schemas.android.com/apk/res/android");
					var m = doc.XPathSelectElement ("/manifest") as XElement;
					Assert.IsNotNull (m, "no manifest element found");
					var vc = m.Attribute (aNS + "versionCode");
					Assert.IsNotNull (vc, "no versionCode attribute found");
					StringAssert.AreEqualIgnoringCase (expectedItems[i], vc.Value,
						$"Version Code is incorrect. Found {vc.Value} expect {expectedItems[i]}");
				}
			}
		}

		[Test]
		[TestCase ("1", false, "manifest=1")]
		[TestCase ("1", true, "x86_64=500001;arm64-v8a=400001")]
		[TestCase ("2", false, "manifest=2")]
		[TestCase ("2", true, "x86_64=500002;arm64-v8a=400002")]
		[TestCase ("999", false, "manifest=999")]
		[TestCase ("999", true, "x86_64=500999;arm64-v8a=400999")]
		public void ApplicationVersionTests (string applicationVersion, bool seperateApk, string expected)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
				MinSdkVersion = null,
			};
			proj.SetProperty (proj.ReleaseProperties, "ApplicationVersion", applicationVersion);
			proj.SetProperty (proj.ReleaseProperties, "ApplicationDisplayVersion", applicationVersion);
			proj.SetProperty (proj.ReleaseProperties, KnownProperties.AndroidCreatePackagePerAbi, seperateApk);
			proj.AndroidManifest = proj.AndroidManifest
				.Replace ("android:versionCode=\"1\"", string.Empty)
				.Replace ("android:versionName=\"1.0\"", string.Empty);
			using (var builder = CreateApkBuilder ()) {
				Assert.True (builder.Build (proj), "Build should have succeeded.");
				XNamespace aNS = "http://schemas.android.com/apk/res/android";

				var expectedItems = expected.Split (';');
				foreach (var item in expectedItems) {
					var items = item.Split ('=');
					var path = Path.Combine ("android", items [0], "AndroidManifest.xml");
					var manifest = builder.Output.GetIntermediaryAsText (Root, path);
					var doc = XDocument.Parse (manifest);
					var m = doc.XPathSelectElement ("/manifest") as XElement;
					Assert.IsNotNull (m, "no manifest element found");
					var vc = m.Attribute (aNS + "versionCode");
					Assert.IsNotNull (vc, "no versionCode attribute found");
					StringAssert.AreEqualIgnoringCase (items [1], vc.Value,
						$"Version Code is incorrect. Found {vc.Value} expect {items [1]}");
				}

			}
		}

		[Test]
		public void ManifestPlaceholders ([Values ("legacy", "manifestmerger.jar")] string manifestMerger)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
				ManifestMerger = manifestMerger,
				JavaPackageName = "com.foo.bar",
			};
			proj.AndroidManifest = proj.AndroidManifest.
				Replace ("application android:label=\"${PROJECT_NAME}\"", "application android:label=\"${ph1}\" x='${ph2}' ").
				Replace ("package=\"${PACKAGENAME}\"", "package=\"${Package}\"");
			proj.SetProperty ("AndroidManifestPlaceholders", "ph2=a=b\\c;ph1=val1;Package=com.foo.bar");
			proj.SetProperty ("AndroidManifestMergerExtraArgs", "--log VERBOSE");
			using (var builder = CreateApkBuilder ()) {
				builder.Build (proj);
				var manifest = builder.Output.GetIntermediaryAsText (Root, Path.Combine ("android", "AndroidManifest.xml"));
				Assert.IsTrue (manifest.Contains (" android:label=\"val1\""), "#1");
				Assert.IsTrue (manifest.Contains (" x=\"a=b\\c\"".Replace ('\\', Path.DirectorySeparatorChar)), "#2");
				Assert.IsTrue (manifest.Contains ("package=\"com.foo.bar\""), "PackageName should have been replaced with 'com.foo.bar'");
				var apk = Path.Combine (Root, builder.ProjectDirectory, proj.OutputPath, $"com.foo.bar-Signed.apk");
				FileAssert.Exists (apk, $"'{apk}' should have been created.");
			}
		}

		[Test]
		public void ManifestPlaceHolders2 ()
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			var s = proj.AndroidManifest.Replace ("<application android:label=\"${PROJECT_NAME}\"", "<application android:label=\"${FOOBARNAME}\"");
			Assert.AreNotEqual (proj.AndroidManifest, s, "#0");
			proj.SetProperty ("AndroidManifestPlaceholders", "FOOBARNAME=AAAAAAAA");
			proj.AndroidManifest = s;
			using (var builder = CreateApkBuilder (Path.Combine ("temp", TestContext.CurrentContext.Test.Name), false, false)) {
				Assert.IsTrue (builder.Build (proj), "Build should have succeeded.");
				var manifest = builder.Output.GetIntermediaryAsText (Root, "android/AndroidManifest.xml");
				Assert.IsTrue (manifest.Contains ("AAAAAAAA"), "#1");
			}
		}

		[Test]
		public void ManifestPlaceHoldersXA1010 ([Values ("legacy", "manifestmerger.jar")] string manifestMerger)
		{
			var proj = new XamarinAndroidApplicationProject () {
				ManifestMerger = manifestMerger
			};
			proj.AndroidManifest = proj.AndroidManifest.Replace ("application android:label=\"${PROJECT_NAME}\"", "application android:label=\"${ph1}\" x='${ph2}' ");
			proj.SetProperty ("AndroidManifestPlaceholders", "ph2=a=b\\c;ph1");
			using (var builder = CreateApkBuilder ()) {
				IEnumerable<string> messages;
				if (string.CompareOrdinal (manifestMerger, "legacy") == 0) {
					Assert.IsTrue (builder.Build (proj), "Build should have succeeded.");
					messages = builder.LastBuildOutput.SkipWhile (x => !x.StartsWith ("Build succeeded.", StringComparison.Ordinal));
				}
				else {
					builder.ThrowOnBuildFailure = false;
					Assert.IsFalse (builder.Build (proj), "Build should have failed.");
					messages = builder.LastBuildOutput.SkipWhile (x => !x.StartsWith ("Build FAILED.", StringComparison.Ordinal));
				}
				string warning = messages.FirstOrDefault (x => x.Contains ("warning XA1010:"));
				Assert.IsNotNull (warning, "Warning should be XA1010");
				StringAssert.Contains ("AndroidManifestPlaceholders", warning, "Warning should mention AndroidManifestPlaceholders");
				StringAssert.Contains ("ph1", warning, "Warning should mention invalid placeholder");
			}
		}

		[Test]
		[TestCaseSource (nameof (DebuggerAttributeCases))]
		public void DebuggerAttribute (string debugType, bool isRelease, bool expected)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = isRelease,
			};
			proj.SetProperty (isRelease ? proj.ReleaseProperties : proj.DebugProperties, "DebugType", debugType);
			using (var builder = CreateApkBuilder (Path.Combine ("temp", $"DebuggerAttribute_{debugType}_{isRelease}_{expected}"), false, false)) {
				Assert.IsTrue (builder.Build (proj), "Build should have succeeded");
				var manifest = builder.Output.GetIntermediaryAsText (Root, Path.Combine ("android", "AndroidManifest.xml"));
				Assert.AreEqual (expected, manifest.Contains ("android:debuggable=\"true\""), $"Manifest  {(expected ? "should" : "should not")} contain the andorid:debuggable attribute");
			}
		}

		[Test]
		public void ModifyManifest ([Values (true, false)] bool isRelease)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = isRelease,
				Imports = {
					new Import ("foo.targets") {
				TextContent = () => @"<?xml version=""1.0"" encoding=""utf-16""?>
<Project ToolsVersion=""4.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
<PropertyGroup>
	<AfterGenerateAndroidManifest>
		$(AfterGenerateAndroidManifest);
		_Foo;
	</AfterGenerateAndroidManifest>
	<Namespace>
	    <Namespace Prefix=""android"" Uri=""http://schemas.android.com/apk/res/android"" />
	</Namespace>
</PropertyGroup>
<ItemGroup>
	<_Permissions Include=""&lt;uses-permission android:name=&quot;android.permission.READ_CONTACTS&quot; /&gt;"" />
</ItemGroup>
<Target Name=""_Foo"">
	<XmlPoke
		XmlInputPath=""$(IntermediateOutputPath)android\AndroidManifest.xml""
		Value=""@(_Permissions)""
		Query=""/manifest""
		Namespaces=""$(Namespace)""
	/>
</Target>
</Project>
"
					},
				},
			};
			using (var builder = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (builder.Build (proj), "Build should have succeeded");
				var manifest = builder.Output.GetIntermediaryAsText (Root, Path.Combine ("android", "AndroidManifest.xml"));
				Assert.IsTrue (manifest.Contains ("READ_CONTACTS"), $"Manifest should contain the READ_CONTACTS");
			}
		}

		[Test]
		public void EmptyUsesSdkElementBuildsOK ([Values (false, true)] bool addReference)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			if (addReference)
				proj.PackageReferences.Add (KnownPackages.AndroidXSecurityCrypto);
			proj.AndroidManifest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" xmlns:tools=""http://schemas.android.com/tools"" android:versionCode=""1"" android:versionName=""1.0"" package=""foo.foo"">
	<uses-sdk />
	<application android:label=""foo"">
	</application>
</manifest>";
			using (var b = CreateApkBuilder ($"temp/{TestName}", cleanupAfterSuccessfulBuild: true, cleanupOnDispose: false)) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var manifestFile = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				var doc = XDocument.Load (manifestFile);
				var ns = XNamespace.Get ("http://schemas.android.com/apk/res/android");
				var usesSdk = doc.Element ("manifest")?.Element ("uses-sdk");
				Assert.IsNotNull (usesSdk, "Should have found a uses-sdk element.");
				var expected = addReference ? "23" : "19";
				Assert.AreEqual (expected, usesSdk.Attribute (ns + "minSdkVersion")?.Value, $"minSdkVersion should have been '{expected}' but was '{usesSdk.Attribute ("minSdkVersion")?.Value}'");

			}
		}

		[Test]
		public void MergeLibraryManifest ()
		{
			byte [] classesJar;
			using (var stream = typeof (XamarinAndroidCommonProject).Assembly.GetManifestResourceStream ("Xamarin.ProjectTools.Resources.Base.classes.jar")) {
				classesJar = new byte [stream.Length];
				stream.Read (classesJar, 0, (int)stream.Length);
			}
			byte [] data;
			using (var ms = new MemoryStream ()) {
				using (var zip = ZipArchive.Create (ms)) {
					zip.AddEntry ("AndroidManifest.xml", @"<?xml version='1.0'?>
<manifest xmlns:android='http://schemas.android.com/apk/res/android' package='com.xamarin.test'>
    <uses-sdk android:minSdkVersion='16'/>
    <permission android:name='${applicationId}.permission.C2D_MESSAGE' android:protectionLevel='signature' />
    <application>
        <activity android:name='.signin.internal.SignInHubActivity' />
        <provider
            android:authorities='${applicationId}.FacebookInitProvider'
            android:name='.internal.FacebookInitProvider'
            android:exported='false' />
        <meta-data android:name='android.support.VERSION' android:value='25.4.0' />
        <meta-data android:name='android.support.VERSION' android:value='25.4.0' />
    </application>
</manifest>
", encoding: System.Text.Encoding.UTF8);
					zip.CreateDirectory ("res");
					zip.AddEntry (classesJar, "classes.jar");
					zip.AddEntry ("R.txt", " ", encoding: System.Text.Encoding.UTF8);
				}
				data = ms.ToArray ();
			}
			var path = Path.Combine ("temp", TestContext.CurrentContext.Test.Name);
			var lib = new XamarinAndroidBindingProject () {
				ProjectName = "Binding1",
				AndroidClassParser = "class-parse",
				Jars = {
					new AndroidItem.LibraryProjectZip ("Jars\\foo.aar") {
						BinaryContent = () => data,
					}
				},
			};
			var proj = new XamarinAndroidApplicationProject () {
				PackageName = "com.xamarin.manifest",
				References = {
					new BuildItem.ProjectReference ("..\\Binding1\\Binding1.csproj", lib.ProjectGuid)
				},
				PackageReferences = {
					KnownPackages.SupportMediaCompat_27_0_2_1,
					KnownPackages.SupportFragment_27_0_2_1,
					KnownPackages.SupportCoreUtils_27_0_2_1,
					KnownPackages.SupportCoreUI_27_0_2_1,
					KnownPackages.SupportCompat_27_0_2_1,
					KnownPackages.AndroidSupportV4_27_0_2_1,
					KnownPackages.SupportV7AppCompat_27_0_2_1,
				},
			};
			proj.Sources.Add (new BuildItem.Source ("TestActivity1.cs") {
				TextContent = () => @"using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Support.V4.App;
using Android.Util;
[Activity (Label = ""TestActivity1"")]
[IntentFilter (new[]{Intent.ActionMain}, Categories = new[]{ ""com.xamarin.sample"" })]
public class TestActivity1 : FragmentActivity {
}
				",
			});
			proj.Sources.Add (new BuildItem.Source ("TestActivity2.cs") {
				TextContent = () => @"using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Support.V4.App;
using Android.Util;
[Activity (Label = ""TestActivity2"")]
[IntentFilter (new[]{Intent.ActionMain}, Categories = new[]{ ""com.xamarin.sample"" })]
public class TestActivity2 : FragmentActivity {
}
				",
			});
			using (var libbuilder = CreateDllBuilder (Path.Combine (path, "Binding1"))) {
				Assert.IsTrue (libbuilder.Build (lib), "Build should have succeeded.");
				using (var builder = CreateApkBuilder (Path.Combine (path, "App1"))) {
					Assert.IsTrue (builder.Build (proj), "Build should have succeeded.");
					var manifest = builder.Output.GetIntermediaryAsText (Root, "android/AndroidManifest.xml");
					Assert.IsTrue (manifest.Contains ("com.xamarin.manifest.permission.C2D_MESSAGE"),
						"${applicationId}.permission.C2D_MESSAGE was not replaced with com.xamarin.manifest.permission.C2D_MESSAGE");
					Assert.IsTrue (manifest.Contains ("com.xamarin.test.signin.internal.SignInHubActivity"),
						".signin.internal.SignInHubActivity was not replaced with com.xamarin.test.signin.internal.SignInHubActivity");
					Assert.IsTrue (manifest.Contains ("com.xamarin.manifest.FacebookInitProvider"),
						"${applicationId}.FacebookInitProvider was not replaced with com.xamarin.manifest.FacebookInitProvider");
					Assert.IsTrue (manifest.Contains ("com.xamarin.test.internal.FacebookInitProvider"),
						".internal.FacebookInitProvider was not replaced with com.xamarin.test.internal.FacebookInitProvider");
					Assert.AreEqual (manifest.IndexOf ("meta-data", StringComparison.OrdinalIgnoreCase),
					                 manifest.LastIndexOf ("meta-data", StringComparison.OrdinalIgnoreCase), "There should be only one meta-data element");

					var doc = XDocument.Parse (manifest);
					var ns = XNamespace.Get ("http://schemas.android.com/apk/res/android");

					var activities = doc.Element ("manifest")?.Element ("application")?.Elements ("activity");
					var e = activities.FirstOrDefault (x => x.Attribute (ns.GetName ("label"))?.Value == "TestActivity2");
					Assert.IsNotNull (e, "Manifest should contain an activity for TestActivity2");
					Assert.IsNotNull (e.Element ("intent-filter"), "TestActivity2 should have an intent-filter");
					Assert.IsNotNull (e.Element ("intent-filter").Element ("action"), "TestActivity2 should have an intent-filter/action");
				}
			}
		}

		[Test]
		public void AllActivityAttributeProperties ([Values ("legacy", "manifestmerger.jar")] string manifestMerger)
		{
			var proj = new XamarinAndroidApplicationProject {
				ManifestMerger = manifestMerger,
			};

			string expectedOutput = manifestMerger == "legacy" ?
				$"android:allowEmbedded=\"true\" android:allowTaskReparenting=\"true\" android:alwaysRetainTaskState=\"true\" android:autoRemoveFromRecents=\"true\" android:banner=\"@drawable/icon\" android:clearTaskOnLaunch=\"true\" android:colorMode=\"hdr\" android:configChanges=\"mcc\" android:description=\"@string/app_name\" android:directBootAware=\"true\" android:documentLaunchMode=\"never\" android:enabled=\"true\" android:enableVrMode=\"foo\" android:excludeFromRecents=\"true\" android:exported=\"true\" android:finishOnCloseSystemDialogs=\"true\" android:finishOnTaskLaunch=\"true\" android:hardwareAccelerated=\"true\" android:icon=\"@drawable/icon\" android:immersive=\"true\" android:label=\"TestActivity\" android:launchMode=\"singleTop\" android:lockTaskMode=\"normal\" android:logo=\"@drawable/icon\" android:maxAspectRatio=\"1.2\" android:maxRecents=\"1\" android:multiprocess=\"true\" android:name=\"com.contoso.TestActivity\" android:noHistory=\"true\" android:parentActivityName=\"{proj.PackageName}.MainActivity\" android:permission=\"com.contoso.permission.TEST_ACTIVITY\" android:persistableMode=\"persistNever\" android:process=\"com.contoso.process.testactivity_process\" android:recreateOnConfigChanges=\"mcc\" android:relinquishTaskIdentity=\"true\" android:resizeableActivity=\"true\" android:resumeWhilePausing=\"true\" android:rotationAnimation=\"crossfade\" android:roundIcon=\"@drawable/icon\" android:screenOrientation=\"portrait\" android:showForAllUsers=\"true\" android:showOnLockScreen=\"true\" android:showWhenLocked=\"true\" android:singleUser=\"true\" android:stateNotNeeded=\"true\" android:supportsPictureInPicture=\"true\" android:taskAffinity=\"com.contoso\" android:theme=\"@android:style/Theme.Light\" android:turnScreenOn=\"true\" android:uiOptions=\"splitActionBarWhenNarrow\" android:visibleToInstantApps=\"true\" android:windowSoftInputMode=\"stateUnchanged|adjustUnspecified\"" :
				$"android:name=\"com.contoso.TestActivity\" android:allowEmbedded=\"true\" android:allowTaskReparenting=\"true\" android:alwaysRetainTaskState=\"true\" android:autoRemoveFromRecents=\"true\" android:banner=\"@drawable/icon\" android:clearTaskOnLaunch=\"true\" android:colorMode=\"hdr\" android:configChanges=\"mcc\" android:description=\"@string/app_name\" android:directBootAware=\"true\" android:documentLaunchMode=\"never\" android:enableVrMode=\"foo\" android:enabled=\"true\" android:excludeFromRecents=\"true\" android:exported=\"true\" android:finishOnCloseSystemDialogs=\"true\" android:finishOnTaskLaunch=\"true\" android:hardwareAccelerated=\"true\" android:icon=\"@drawable/icon\" android:immersive=\"true\" android:label=\"TestActivity\" android:launchMode=\"singleTop\" android:lockTaskMode=\"normal\" android:logo=\"@drawable/icon\" android:maxAspectRatio=\"1.2\" android:maxRecents=\"1\" android:multiprocess=\"true\" android:noHistory=\"true\" android:parentActivityName=\"{proj.PackageName}.MainActivity\" android:permission=\"com.contoso.permission.TEST_ACTIVITY\" android:persistableMode=\"persistNever\" android:process=\"com.contoso.process.testactivity_process\" android:recreateOnConfigChanges=\"mcc\" android:relinquishTaskIdentity=\"true\" android:resizeableActivity=\"true\" android:resumeWhilePausing=\"true\" android:rotationAnimation=\"crossfade\" android:roundIcon=\"@drawable/icon\" android:screenOrientation=\"portrait\" android:showForAllUsers=\"true\" android:showOnLockScreen=\"true\" android:showWhenLocked=\"true\" android:singleUser=\"true\" android:stateNotNeeded=\"true\" android:supportsPictureInPicture=\"true\" android:taskAffinity=\"com.contoso\" android:theme=\"@android:style/Theme.Light\" android:turnScreenOn=\"true\" android:uiOptions=\"splitActionBarWhenNarrow\" android:visibleToInstantApps=\"true\" android:windowSoftInputMode=\"stateUnchanged|adjustUnspecified\"";

			proj.Sources.Add (new BuildItem.Source ("TestActivity.cs") {
				TextContent = () => @"using Android.App;
using Android.Content.PM;
using Android.Views;
[Activity (
	AllowEmbedded              = true,
	AllowTaskReparenting       = true,
	AlwaysRetainTaskState      = true,
	AutoRemoveFromRecents      = true,
	Banner                     = ""@drawable/icon"",
	ClearTaskOnLaunch          = true,
	ColorMode                  = ""hdr"",
	ConfigurationChanges       = ConfigChanges.Mcc,
	Description                = ""@string/app_name"",
	DirectBootAware            = true,
	DocumentLaunchMode         = DocumentLaunchMode.Never,
	Enabled                    = true,
	EnableVrMode               = ""foo"",
	ExcludeFromRecents         = true,
	Exported                   = true,
	FinishOnCloseSystemDialogs = true,
	FinishOnTaskLaunch         = true,
	HardwareAccelerated        = true,
	Icon                       = ""@drawable/icon"",
	Immersive                  = true,
	Label                      = ""TestActivity"",
	LaunchMode                 = LaunchMode.SingleTop,
	LockTaskMode               = ""normal"",
	Logo                       = ""@drawable/icon"",
	MaxAspectRatio             = 1.2F,
	MaxRecents                 = 1,
	MultiProcess               = true,
	Name                       = ""com.contoso.TestActivity"",
	NoHistory                  = true,
	ParentActivity             = typeof (UnnamedProject.MainActivity),
	Permission                 = ""com.contoso.permission.TEST_ACTIVITY"",
	PersistableMode            = ActivityPersistableMode.Never,
	Process                    = ""com.contoso.process.testactivity_process"",
	RecreateOnConfigChanges    = ConfigChanges.Mcc,
	RelinquishTaskIdentity     = true,
	ResizeableActivity         = true,
	ResumeWhilePausing         = true,
	RotationAnimation          = WindowRotationAnimation.Crossfade,
	RoundIcon                  = ""@drawable/icon"",
	ScreenOrientation          = ScreenOrientation.Portrait,
	ShowForAllUsers            = true,
	ShowOnLockScreen           = true,
	ShowWhenLocked             = true,
	SingleUser                 = true,
	StateNotNeeded             = true,
	SupportsPictureInPicture   = true,
	TaskAffinity               = ""com.contoso"",
	Theme                      = ""@android:style/Theme.Light"",
	TurnScreenOn               = true,
	UiOptions                  = UiOptions.SplitActionBarWhenNarrow,
	VisibleToInstantApps       = true,
	WindowSoftInputMode        = Android.Views.SoftInput.StateUnchanged)]
class TestActivity : Activity { }"
			});

			using (ProjectBuilder builder = CreateDllBuilder (Path.Combine ("temp", TestName))) {
				Assert.IsTrue (builder.Build (proj), "Build should have succeeded");

				string manifest = builder.Output.GetIntermediaryAsText (Path.Combine ("android", "AndroidManifest.xml"));
				var doc = XDocument.Parse (manifest);
				var ns = XNamespace.Get ("http://schemas.android.com/apk/res/android");
				IEnumerable<XElement> activities = doc.Element ("manifest")?.Element ("application")?.Elements ("activity");
				XElement e = activities.FirstOrDefault (x => x.Attribute (ns.GetName ("label"))?.Value == "TestActivity");
				Assert.IsNotNull (e, "Manifest should contain an activity labeled TestActivity");
				Assert.AreEqual (expectedOutput, string.Join (" ", e.Attributes ()));
			}
		}

		[Test]
 		public void AllServiceAttributeProperties ([Values ("legacy", "manifestmerger.jar")] string manifestMerger)
 		{
 			string expectedOutput = manifestMerger == "legacy" ?
				"android:directBootAware=\"true\" android:enabled=\"true\" android:exported=\"true\" android:foregroundServiceType=\"connectedDevice\" android:icon=\"@drawable/icon\" android:isolatedProcess=\"true\" android:label=\"TestActivity\" android:name=\"com.contoso.TestActivity\" android:permission=\"com.contoso.permission.TEST_ACTIVITY\" android:process=\"com.contoso.process.testactivity_process\" android:roundIcon=\"@drawable/icon\"" :
				"android:name=\"com.contoso.TestActivity\" android:directBootAware=\"true\" android:enabled=\"true\" android:exported=\"true\" android:foregroundServiceType=\"connectedDevice\" android:icon=\"@drawable/icon\" android:isolatedProcess=\"true\" android:label=\"TestActivity\" android:permission=\"com.contoso.permission.TEST_ACTIVITY\" android:process=\"com.contoso.process.testactivity_process\" android:roundIcon=\"@drawable/icon\"";

			var proj = new XamarinAndroidApplicationProject {
				ManifestMerger = manifestMerger
			};

 			proj.Sources.Add (new BuildItem.Source ("TestActivity.cs") {
 				TextContent = () => @"using Android.App;
 using Android.Content.PM;
 using Android.Views;
 [Service (
 	DirectBootAware            = true,
 	Enabled                    = true,
 	Exported                   = true,
	ForegroundServiceType      = ForegroundService.TypeConnectedDevice,
 	Icon                       = ""@drawable/icon"",
 	IsolatedProcess            = true,
 	Label                      = ""TestActivity"",
 	Name                       = ""com.contoso.TestActivity"",
 	Permission                 = ""com.contoso.permission.TEST_ACTIVITY"",
 	Process                    = ""com.contoso.process.testactivity_process"",
 	RoundIcon                  = ""@drawable/icon"")]
 class TestService : Service { public override Android.OS.IBinder OnBind (Android.Content.Intent intent) { return null; } }"
 			});

 			using (ProjectBuilder builder = CreateDllBuilder (Path.Combine ("temp", TestName))) {
 				Assert.IsTrue (builder.Build (proj), "Build should have succeeded");

 				string manifest = builder.Output.GetIntermediaryAsText (Path.Combine ("android", "AndroidManifest.xml"));
 				var doc = XDocument.Parse (manifest);
 				var ns = XNamespace.Get ("http://schemas.android.com/apk/res/android");
 				IEnumerable<XElement> activities = doc.Element ("manifest")?.Element ("application")?.Elements ("service");
 				XElement e = activities.FirstOrDefault (x => x.Attribute (ns.GetName ("label"))?.Value == "TestActivity");
 				Assert.IsNotNull (e, "Manifest should contain an activity labeled TestActivity");
 				Assert.AreEqual (expectedOutput, string.Join (" ", e.Attributes ()));
 			}
 		}

		/// <summary>
		/// Based on missing [Service(Exported=true)] here:
		/// https://github.com/microsoft/dotnet-podcasts/blob/09b733b406ecb128f026645ef4c7e69c773f8a4b/src/Mobile/Platforms/Android/Services/MediaPlayerService.cs#L15-L16
		/// </summary>
		[Test]
		public void ExportedErrorMessage ()
		{
			var proj = new XamarinAndroidApplicationProject {
				ManifestMerger = "manifestmerger.jar"
			};

			proj.Sources.Add (new BuildItem.Source ("TestActivity.cs") {
				TextContent = () => $@"using Android.App;
 using Android.Content.PM;
 using Android.Views;
 [Service]
[IntentFilter(new[] {{ ""{proj.PackageName}.PLAY"" }})]
 class TestService : Service {{ public override Android.OS.IBinder OnBind (Android.Content.Intent intent) {{ return null; }} }}"
			});

			using var b = CreateDllBuilder ();
			b.ThrowOnBuildFailure = false;
			Assert.IsFalse (b.Build (proj), "Build should have failed");
			var extension = IsWindows ? ".exe" : "";
			Assert.IsTrue (b.LastBuildOutput.ContainsText ($"AndroidManifest.xml(12,5): java{extension} error AMM0000:"), "Should recieve AMM0000 error");
			Assert.IsTrue (b.LastBuildOutput.ContainsText ("Apps targeting Android 12 and higher are required to specify an explicit value for `android:exported`"), "Should recieve AMM0000 error");
		}
	}
}
