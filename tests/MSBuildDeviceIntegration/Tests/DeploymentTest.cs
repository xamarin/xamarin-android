using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Xamarin.ProjectTools;
[assembly: NonParallelizable]

namespace Xamarin.Android.Build.Tests
{
	[SingleThreaded]
	public class DeploymentTest : DeviceTest {

		static ProjectBuilder builder;
		static XamarinFormsAndroidApplicationProject proj;

		[OneTimeSetUp]
		public void BeforeDeploymentTests ()
		{
			proj = new XamarinFormsAndroidApplicationProject ();
			proj.SetProperty (KnownProperties.AndroidSupportedAbis, "armeabi-v7a;x86");
			var mainPage = proj.Sources.First (x => x.Include () == "MainPage.xaml.cs");
			var source = mainPage.TextContent ().Replace ("InitializeComponent ();", @"InitializeComponent ();
			Console.WriteLine ($""TimeZoneInfo={TimeZoneInfo.Local.DisplayName}"");
");
			mainPage.TextContent = () => source;
			builder = CreateApkBuilder (Path.Combine ("temp", "DeploymentTests"));
			string apiLevel;
			proj.TargetFrameworkVersion = builder.LatestTargetFrameworkVersion (out apiLevel);
			proj.PackageName = "Xamarin.TimeZoneTest";
			proj.AndroidManifest = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" android:versionCode=""1"" android:versionName=""1.0"" package=""Xamarin.TimeZoneTest"">
	<uses-sdk android:minSdkVersion=""24"" android:targetSdkVersion=""{apiLevel}"" />
	<application android:label=""${{PROJECT_NAME}}"">
	</application >
</manifest> ";
			Assert.IsTrue (builder.Build (proj), "Build should have succeeded.");
			Assert.IsTrue (builder.Install (proj), "Install should have succeeded.");
		}

		[OneTimeTearDown]
		public void AfterDeploymentTests ()
		{
			RunAdbCommand ($"uninstall {proj.PackageName}");
			if (TestContext.CurrentContext.Result.FailCount == 0 && Directory.Exists (builder.ProjectDirectory))
			    Directory.Delete (builder.ProjectDirectory, recursive: true);
		}


		[Test]
		public void CheckXamarinFormsAppDeploysAndAButtonWorks ()
		{
			if (!HasDevices)
				Assert.Ignore ("Skipping Test. No devices available.");
			AdbStartActivity ($"{proj.PackageName}/md52d9cf6333b8e95e8683a477bc589eda5.MainActivity");
			string activityStartLogcatPath = Path.Combine (XABuildPaths.TestOutputDirectory, builder.ProjectDirectory, "startup-logcat.log");
			bool didActivityStart = WaitForActivityToStart (proj.PackageName, "MainActivity", output: out string output, timeout: 15);
			File.WriteAllText (activityStartLogcatPath, output);
			Assert.IsTrue (didActivityStart, "Activity should have started");

			ClearAdbLogcat ();
			ClickButton (proj.PackageName, "myXFButton", "CLICK ME");
			string buttonClickedLogcatPath = Path.Combine (XABuildPaths.TestOutputDirectory, builder.ProjectDirectory, "button-logcat.log");
			var buttonEventLogcat = new StringBuilder ();
			bool didButtonEventFire = MonitorAdbLogcat ((line) => {
				buttonEventLogcat.AppendLine (line);
				return line.Contains ("Button was Clicked!");
			});
			File.WriteAllText (buttonClickedLogcatPath, buttonEventLogcat.ToString ());
			Assert.IsTrue (didButtonEventFire, "Button Should have been Clicked.");
		}

		static object [] GetTimeZoneTestCases ()
		{
			List<object> tests = new List<object> ();
			var ignore = new string [] {
				"Asia/Qostanay",
				"US/Pacific-New"
			};
			
			foreach (var tz in NodaTime.DateTimeZoneProviders.Tzdb.Ids) {
				if (ignore.Contains (tz))
					continue;
				tests.Add (new object [] {
					tz,
				});
			}
			return tests.ToArray ();
		}

		[Test]
		[TestCaseSource (nameof (GetTimeZoneTestCases))]
		[Retry (1)]
		public void CheckTimeZoneInfoIsCorrect (string timeZone)
		{
			if (!HasDevices)
				Assert.Ignore ("Skipping Test. No devices available.");

			RunAdbCommand ($"shell su root setprop persist.sys.timezone \"{timeZone}\"");
			ClearAdbLogcat ();
			AdbStartActivity ($"{proj.PackageName}/md52d9cf6333b8e95e8683a477bc589eda5.MainActivity");
			string activityStartLogcatPath = Path.Combine (XABuildPaths.TestOutputDirectory, builder.ProjectDirectory, "startup-logcat.log");
			bool didActivityStart = WaitForActivityToStart (proj.PackageName, "MainActivity", output: out string activityStartLogcat);
			File.WriteAllText (activityStartLogcatPath, activityStartLogcat);
			Assert.IsTrue (didActivityStart, "Activity should have started");

			string tzLogcatPath = Path.Combine (XABuildPaths.TestOutputDirectory, builder.ProjectDirectory, "timezone-logcat.log");
			var tzLogcat = new StringBuilder ();
			bool didContainTimezone = MonitorAdbLogcat ((l)=> {
				tzLogcat.AppendLine (l);
				return l.Contains ($"TimeZoneInfo={timeZone}");
			});
			File.WriteAllText (tzLogcatPath, tzLogcat.ToString ());
			Assert.IsTrue (didContainTimezone, $"TimeZone should have been {timeZone}");
		}
	}
}