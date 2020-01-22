using System;
using NUnit.Framework;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	[TestFixture]
	[NonParallelizable]
	public class JavaDaemonTests : BaseTest
	{
		XamarinAndroidApplicationProject CreateProject (bool isRelease = false)
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = isRelease,
				AndroidUseJavaDaemon = true,
				ManifestMerger = "manifestmerger.jar",
			};
			proj.OtherBuildItems.Add (new BuildItem ("AndroidJavaLibrary", "Hello (World).jar") {
				BinaryContent = () => Convert.FromBase64String (
@"UEsDBBQACAgIAMl8lUsAAAAAAAAAAAAAAAAJAAQATUVUQS1JTkYv/soAAAMAUEsHCAAAAAACAAAAA
AAAAFBLAwQUAAgICADJfJVLAAAAAAAAAAAAAAAAFAAAAE1FVEEtSU5GL01BTklGRVNULk1G803My0
xLLS7RDUstKs7Mz7NSMNQz4OVyLkpNLElN0XWqBAlY6BnEG5oaKWj4FyUm56QqOOcXFeQXJZYA1Wv
ycvFyAQBQSwcIbrokAkQAAABFAAAAUEsDBBQACAgIAIJ8lUsAAAAAAAAAAAAAAAASAAAAc2FtcGxl
L0hlbGxvLmNsYXNzO/Vv1z4GBgYTBkEuBhYGXg4GPnYGfnYGAUYGNpvMvMwSO0YGZg3NMEYGFuf8l
FRGBn6fzLxUv9LcpNSikMSkHKAIa3l+UU4KI4OIhqZPVmJZon5OYl66fnBJUWZeujUjA1dwfmlRcq
pbJkgtl0dqTk6+HkgZDwMrAxvQFrCIIiMDT3FibkFOqj6Yz8gggDDKPykrNbmEQZGBGehCEGBiYAR
pBpLsQJ4skGYE0qxa2xkYNwIZjAwcQJINIggkORm4oEqloUqZhZg2oClkB5LcYLN5AFBLBwjQMrpO
0wAAABMBAABQSwECFAAUAAgICADJfJVLAAAAAAIAAAAAAAAACQAEAAAAAAAAAAAAAAAAAAAATUVUQ
S1JTkYv/soAAFBLAQIUABQACAgIAMl8lUtuuiQCRAAAAEUAAAAUAAAAAAAAAAAAAAAAAD0AAABNRV
RBLUlORi9NQU5JRkVTVC5NRlBLAQIUABQACAgIAIJ8lUvQMrpO0wAAABMBAAASAAAAAAAAAAAAAAA
AAMMAAABzYW1wbGUvSGVsbG8uY2xhc3NQSwUGAAAAAAMAAwC9AAAA1gEAAAAA"
				)
			});
			if (isRelease) {
				var abis = new string [] { "armeabi-v7a", "x86" };
				proj.SetProperty (KnownProperties.AndroidSupportedAbis, string.Join (";", abis));
			}
			return proj;
		}

		void InstallOrBuild (XamarinAndroidApplicationProject proj)
		{
			using (var builder = CreateApkBuilder ()) {
				if (HasDevices) {
					try {
						Assert.IsTrue (builder.Install (proj), "Install should have succeeded.");
					} finally {
						try {
							string result = RunAdbCommand ($"uninstall {proj.PackageName}");
							TestContext.WriteLine ($"adb uninstall: {result}");
						} catch (Exception exc) {
							TestContext.WriteLine (exc);
						}
					}
				} else {
					Assert.IsTrue (builder.Build (proj), "Build should have succeeded.");
				}
				Assert.IsTrue (StringAssertEx.ContainsText (builder.LastBuildOutput, "java daemon already running"), "Java daemon should be reused!");
			}
		}

		[Test]
		public void BasicApp ([Values (false, true)] bool isRelease)
		{
			var proj = CreateProject (isRelease);
			InstallOrBuild (proj);
		}

		[Test]
		public void ProguardAndDX ()
		{
			var proj = CreateProject (isRelease: true);
			proj.DexTool = "dx";
			proj.LinkTool = "proguard";
			InstallOrBuild (proj);
		}

		[Test]
		public void MultiDexAndDX ()
		{
			var proj = CreateProject (isRelease: true);
			proj.DexTool = "dx";
			proj.SetProperty ("AndroidEnableMultiDex", "True");
			InstallOrBuild (proj);
		}

		[Test]
		public void R8 ()
		{
			var proj = CreateProject (isRelease: true);
			proj.LinkTool = "r8";
			InstallOrBuild (proj);
		}
	}
}
