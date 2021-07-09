using System;
using NUnit.Framework;
using Xamarin.ProjectTools;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Build.Framework;
using System.Text;
using Xamarin.Android.Tasks;
using Microsoft.Build.Utilities;

namespace Xamarin.Android.Build.Tests {
	[TestFixture]
	[Category ("Node-2")]
	[Parallelizable (ParallelScope.Children)]
	public class GetManifestVersionsTests : BaseTest {

#pragma warning disable 414
static object [] ReadManifestVersionsTestCases () => new object [] {
	new object[] {
		/* abi */                       "x86:arm64-v8a",
		/* versionCode */               "55555:66666",
		/* versionName */               "33.4:33.4",
		/* expectedResultsXml */       @"<?xml version='1.0'?>
<output>
	<result abi='x86' code='55555' name='33.4'/>
	<result abi='arm64-v8a' code='66666' name='33.4'/>
</output>",
	},
	new object[] {
		/* abi */                       "manifest:x86:arm64-v8a",
		/* versionCode */               "1:55555:66666",
		/* versionName */               "1.0:33.4:33.4",
		/* expectedResultsXml */        @"<?xml version='1.0'?>
<output>
	<result abi='all' code='1' name='1.0'/>
	<result abi='x86' code='55555' name='33.4'/>
	<result abi='arm64-v8a' code='66666' name='33.4'/>
</output>",
	},
	new object[] {
		/* abi */                       "manifest",
		/* versionCode */		"1",
		/* versionName */		"1.0",
		/* expectedResultsXml */        @"<?xml version='1.0'?>
<output>
	<result abi='all' code='1' name='1.0'/>
</output>",
	},
	new object[] {
		/* abi */                       "manifest:fake",
		/* versionCode */               "1:0",
		/* versionName */               "1.0:0",
		/* expectedResultsXml */        @"<?xml version='1.0'?>
<output>
	<result abi='all' code='1' name='1.0'/>
</output>",
	},
};
#pragma warning restore 414

		[Test]
		[TestCaseSource (nameof(ReadManifestVersionsTestCases))]
		public void ReadManifestVersions (string abi, string versionCode, string versionName, string expectedResultsXml)
		{
			char[] splitChars = new char[] {':'};
			var path = Path.Combine ("temp", TestName);
			Directory.CreateDirectory (Path.Combine (path));
			IBuildEngine engine = new MockBuildEngine (TestContext.Out);
			var task = new GetManifestVersions {
				BuildEngine = engine
			};
			string[] abis = abi.Split (splitChars, StringSplitOptions.RemoveEmptyEntries);
			string[] versionCodes = versionCode.Split (splitChars, StringSplitOptions.RemoveEmptyEntries);
			string[] versionNames = versionName.Split (splitChars, StringSplitOptions.RemoveEmptyEntries);
			Assert.AreEqual (versionCodes.Length, versionNames.Length);
			for (int i=0; i < abis.Length; i ++) {
				string versionBlock = $"android:versionCode='{versionCodes[i]}' android:versionName='{versionNames[i]}'";
				Directory.CreateDirectory (Path.Combine (path, abis[i]));
				File.WriteAllText (Path.Combine (path, abis[i], "AndroidManifest.xml"),
					$@"<?xml version='1.0' ?>
	<manifest xmlns:android='http://schemas.android.com/apk/res/android' {versionBlock} android:package='Foo.Foo' />");
			}
			task.ManifestPath = path;
			Assert.IsTrue (task.Execute ());
			var doc = XDocument.Parse (expectedResultsXml);
			IEnumerable<XElement> elements = doc.XPathSelectElements ("//output/result");
			Dictionary<string, (string code, string name)> results = new Dictionary<string, (string code, string name)> ();
			foreach (var element in elements) {
				string expectedAbi = element.Attribute("abi").Value;
				string code = element.Attribute("code").Value;
				string name = element.Attribute("name").Value;
				results.Add (expectedAbi, (code: code, name: name));
			}
			Assert.AreEqual (results.Count, task.ManifestVersions.Count ());
			foreach (var result in task.ManifestVersions) {
				var expected = results[result.GetMetadata ("Abi")];
				Assert.AreEqual (expected.code, result.GetMetadata ("VersionCode"));
				Assert.AreEqual (expected.name, result.GetMetadata ("VersionName"));
			}
		}
	}
}
