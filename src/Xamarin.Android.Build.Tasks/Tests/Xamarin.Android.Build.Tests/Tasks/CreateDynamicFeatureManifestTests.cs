using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Net.Http;
using System.Threading.Tasks;
using Xamarin.Android.Tasks;
using Xamarin.Tools.Zip;
using TaskItem = Microsoft.Build.Utilities.TaskItem;

namespace Xamarin.Android.Build.Tests
{
	[TestFixture]
	[Category ("Node-2")]
	public class CreateDynamicFeatureManifestTests : BaseTest
	{
		[Test]
		[TestCase ("OnDemand", "", "")]
		[TestCase ("OnDemand", "21", "")]
		[TestCase ("OnDemand", "21", "30")]
		[TestCase ("InstallTime", "", "")]
		[TestCase ("InstallTime", "21", "")]
		[TestCase ("InstallTime", "21", "30")]
		[TestCase ("", "", "")]
		public void CreateDynamicFeatureManifestIsCreated (string deliveryType, string minSdk, string targetSdk)
		{
			string path = Path.Combine (Root, "temp", TestName);
			Directory.CreateDirectory(path);
			string manifest = Path.Combine(path, "AndroidManifest.xml");
			var task = new CreateDynamicFeatureManifest {
				BuildEngine = new MockBuildEngine (TestContext.Out),
				FeatureDeliveryType = deliveryType,
				FeatureSplitName = "MyFeature",
				FeatureTitleResource = "@strings/myfeature",
				PackageName = "com.feature.test",
				OutputFile = new TaskItem (manifest),
				MinSdkVersion = minSdk,
				TargetSdkVersion = targetSdk,
			};
			Assert.IsTrue (task.Execute (), "task.Execute() should have succeeded.");
			XDocument doc = XDocument.Load (manifest);
			var nsResolver = new XmlNamespaceManager (new NameTable ());
			nsResolver.AddNamespace ("android", "http://schemas.android.com/apk/res/android");
			nsResolver.AddNamespace ("dist", "http://schemas.android.com/apk/distribution");
			bool hasMinSdk = !string.IsNullOrEmpty (minSdk);
			Assert.AreEqual (hasMinSdk, doc.XPathSelectElements ($"//manifest/uses-sdk[@android:minSdkVersion='{minSdk}']", nsResolver).Any (),
				$"minSdkVersion should {(hasMinSdk ? "" : "not")} be set.");
			bool hasTargetSdk = !string.IsNullOrEmpty (targetSdk);
			Assert.AreEqual (hasMinSdk, doc.XPathSelectElements ($"//manifest/uses-sdk[@android:targetSdkVersion='{targetSdk}']", nsResolver).Any (),
				$"minSdkVersion should {(hasTargetSdk ? "" : "not")} be set.");
			string expected = "";
			switch (deliveryType) {
				case "OnDemand":
					expected = "dist:on-demand";
					break;
				case "InstallTime":
				default:
					expected = "dist:install-time";
					break;
			}
			Assert.IsTrue (doc.XPathSelectElements ($"//manifest/dist:module/dist:delivery/{expected}", nsResolver).Any (), $"Delivery type should be set to {expected}");
			Directory.Delete (path, recursive: true);
		}
	}
}
