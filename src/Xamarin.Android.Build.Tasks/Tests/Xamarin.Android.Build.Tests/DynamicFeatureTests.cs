using NUnit.Framework;
using System.IO;
using System.Text;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	[Category ("Node-3")]
	[Parallelizable (ParallelScope.Children)]
	public class DynamicFeatureTests : BaseTest
	{
		[Test]
		[Category ("SmokeTests")]
		public void BuildDynamicFeature ([Values (true, false)] bool isRelease) {

			if (!Builder.UseDotNet)
				Assert.Ignore ("Dynamic Features not supported on Legacy Projects.");
			var path = Path.Combine ("temp", TestName);
			var feature1 = new XamarinAndroidLibraryProject () {
				ProjectName = "Feature1",
				IsRelease = isRelease,
				OtherBuildItems = {
					new AndroidItem.AndroidAsset ("Assets\\asset3.txt") {
						TextContent = () => "Asset3",
						Encoding = Encoding.ASCII,
					},
				}
			};
			// we don't need any of this stuff!
			feature1.Sources.Clear ();
			feature1.AndroidResources.Clear ();
			feature1.SetProperty ("FeatureTitleResource", "@string/feature1");
			var app = new XamarinAndroidApplicationProject {
				ProjectName = "MyApp",
				IsRelease = isRelease,
				OtherBuildItems = {
					new AndroidItem.AndroidResource (() => "Resources\\values\\string1.xml") {
						TextContent = () => @"<resources>
					<string name=""feature1"">Feature1</string>
				</resources>",
					},
				}
			};
			app.SetProperty ("AndroidPackageFormat", "aab");
			var reference = new BuildItem.ProjectReference ($"..\\{feature1.ProjectName}\\{feature1.ProjectName}.csproj",
				feature1.ProjectName);
			reference.Metadata.Add ("ReferenceOutputAssembly", "False");
			reference.Metadata.Add ("AndroidDynamicFeature", "True");
			app.References.Add (reference);
			using (var libBuilder = CreateDllBuilder (Path.Combine (path, feature1.ProjectName))) {
				libBuilder.Save (feature1);
				using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
					Assert.IsTrue (appBuilder.Build (app), $"{app.ProjectName} should succeed");
				}
			}
		}
	}
}
