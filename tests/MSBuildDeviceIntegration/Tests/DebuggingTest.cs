using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using Mono.Debugging.Client;
using Mono.Debugging.Soft;
using NUnit.Framework;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	public class DebuggingTest : DeviceTest {
		[TearDown]
		public void ClearDebugProperties ()
		{
			ClearDebugProperty ();
		}

		[Test]
		[Retry (1)]
		public void ApplicationRunsWithoutDebugger ([Values (false, true)] bool isRelease)
		{
			if (!HasDevices) {
				Assert.Ignore ("Test needs a device attached.");
				return;
			}

			var proj = new XamarinFormsAndroidApplicationProject () {
				IsRelease = isRelease,
			};
			if (isRelease || !CommercialBuildAvailable) {
				var abis = new string [] { "armeabi-v7a", "x86" };
				proj.SetProperty (KnownProperties.AndroidSupportedAbis, string.Join (";", abis));
			}
			proj.SetDefaultTargetDevice ();
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				string apiLevel;
				proj.TargetFrameworkVersion = b.LatestTargetFrameworkVersion (out apiLevel);
				proj.AndroidManifest = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" android:versionCode=""1"" android:versionName=""1.0"" package=""UnnamedProject.UnnamedProject"">
	<uses-sdk android:minSdkVersion=""24"" android:targetSdkVersion=""{apiLevel}"" />
	<application android:label=""${{PROJECT_NAME}}"">
	</application >
</manifest>";
				b.Save (proj, saveProject: true);
				proj.NuGetRestore (Path.Combine (Root, b.ProjectDirectory), b.PackagesDirectory);
				Assert.True (b.Build (proj), "Project should have built.");
				Assert.True (b.Install (proj), "Project should have installed.");
				ClearAdbLogcat ();
				if (CommercialBuildAvailable)
					Assert.True (b.RunTarget (proj, "_Run"), "Project should have run.");
				else
					AdbStartActivity ($"{proj.PackageName}/{proj.JavaPackageName}.MainActivity");

				Assert.True (WaitForActivityToStart (proj.PackageName, "MainActivity",
					Path.Combine (Root, b.ProjectDirectory, "logcat.log"), 30), "Activity should have started.");
				Assert.True (b.Uninstall (proj), "Project should have uninstalled.");
			}
		}

#pragma warning disable 414
		static object [] DebuggerCustomAppTestCases = new object [] {
			new object[] {
				/* useSharedRuntime */   false,
				/* embedAssemblies */    true,
				/* fastDevType */        "Assemblies",
				/* profiledAot */        false,
				/* activityStarts */     true,
			},
			new object[] {
				/* useSharedRuntime */   false,
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies",
				/* profiledAot */        false,
				/* activityStarts */     true,
			},
			new object[] {
				/* useSharedRuntime */   true,
				/* embedAssemblies */    true,
				/* fastDevType */        "Assemblies",
				/* profiledAot */        false,
				/* activityStarts */     true,
			},
			new object[] {
				/* useSharedRuntime */   false,
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies",
				/* profiledAot */        true,
				/* activityStarts */     true,
			},
			new object[] {
				/* useSharedRuntime */   true,
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies",
				/* profiledAot */        true,
				/* activityStarts */     true,
			},
			new object[] {
				/* useSharedRuntime */   true,
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies",
				/* profiledAot */        false,
				/* activityStarts */     true,
			},
			new object[] {
				/* useSharedRuntime */   true,
				/* embedAssemblies */    true,
				/* fastDevType */        "Assemblies:Dexes",
				/* profiledAot */        false,
				/* activityStarts */     true,
			},
			new object[] {
				/* useSharedRuntime */   true,
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies:Dexes",
				/* profiledAot */        false,
				/* activityStarts */     false,
			},
		};
#pragma warning restore 414

		[Test]
		[TestCaseSource (nameof (DebuggerCustomAppTestCases))]
		[Retry (1)]
		public void CustomApplicationRunsWithDebuggerAndBreaks (bool useSharedRuntime, bool embedAssemblies, string fastDevType, bool profiledAot, bool activityStarts)
		{
			if (!CommercialBuildAvailable) {
				Assert.Ignore ("Test does not run on the Open Source Builds.");
				return;
			}
			if (!HasDevices) {
				Assert.Ignore ("Test needs a device attached.");
				return;
			}
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = false,
				AndroidFastDeploymentType = fastDevType,
				AndroidEnableProfiledAot = profiledAot,
			};
			var abis = new string [] { "armeabi-v7a", "x86" };
			proj.SetProperty (KnownProperties.AndroidSupportedAbis, string.Join (";", abis));
			proj.SetProperty (KnownProperties.AndroidUseSharedRuntime, useSharedRuntime.ToString ());
			proj.SetProperty ("EmbedAssembliesIntoApk", embedAssemblies.ToString ());
			proj.SetDefaultTargetDevice ();
			proj.Sources.Add (new BuildItem.Source ("MyApplication.cs") {
				TextContent = () => proj.ProcessSourceTemplate (@"using System;
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Widget;

namespace ${ROOT_NAMESPACE} {
	[Application]
	public class MyApplication : Application {
		public MyApplication (IntPtr handle, JniHandleOwnership jniHandle)
			: base (handle, jniHandle)
		{
		}

		public override void OnCreate ()
		{
			base.OnCreate ();
	 	}
	}
}
"),
			});
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				string apiLevel;
				proj.TargetFrameworkVersion = b.LatestTargetFrameworkVersion (out apiLevel);
				proj.AndroidManifest = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" android:versionCode=""1"" android:versionName=""1.0"" package=""UnnamedProject.UnnamedProject"">
	<uses-sdk android:minSdkVersion=""24"" android:targetSdkVersion=""{apiLevel}"" />
	<application android:label=""${{PROJECT_NAME}}"">
	</application >
</manifest>";
				b.Save (proj, saveProject: true);
				proj.NuGetRestore (Path.Combine (Root, b.ProjectDirectory), b.PackagesDirectory);
				Assert.True (b.Build (proj), "Project should have built.");
				Assert.True (b.Install (proj), "Project should have installed.");

				int breakcountHitCount = 0;
				ManualResetEvent resetEvent = new ManualResetEvent (false);
				var sw = new Stopwatch ();
				// setup the debugger
				var session = new SoftDebuggerSession ();
				session.Breakpoints = new BreakpointStore {
					{ Path.Combine (Root, b.ProjectDirectory, "MainActivity.cs"),  19 },
					{ Path.Combine (Root, b.ProjectDirectory, "MyApplication.cs"),  17 },
				};
				session.TargetHitBreakpoint += (sender, e) => {
					Console.WriteLine ($"BREAK {e.Type}");
					breakcountHitCount++;
					session.Continue ();
				};
				var rnd = new Random ();
				int port = rnd.Next (10000, 20000);
				TestContext.Out.WriteLine ($"{port}");
				var args = new SoftDebuggerConnectArgs ("", IPAddress.Loopback, port) {
					MaxConnectionAttempts = 10,
				};
				var startInfo = new SoftDebuggerStartInfo (args) {
					WorkingDirectory = Path.Combine (b.ProjectDirectory, proj.IntermediateOutputPath, "android", "assets"),
				};
				var options = new DebuggerSessionOptions () {
					EvaluationOptions = EvaluationOptions.DefaultOptions,
				};
				options.EvaluationOptions.UseExternalTypeResolver = true;
				ClearAdbLogcat ();
				Assert.True (b.RunTarget (proj, "_Run", parameters: new string [] {
					$"AndroidSdbTargetPort={port}",
					$"AndroidSdbHostPort={port}",
					"AndroidAttachDebugger=True",
				}), "Project should have run.");

				// do we expect the app to start?
				Assert.AreEqual (activityStarts, WaitForDebuggerToStart (Path.Combine (Root, b.ProjectDirectory, "logcat.log")), "Activity should have started");
				if (!activityStarts)
					return;
				// we need to give a bit of time for the debug server to start up.
				WaitFor (2000);
				session.LogWriter += (isStderr, text) => { Console.WriteLine (text); };
				session.OutputWriter += (isStderr, text) => { Console.WriteLine (text); };
				session.DebugWriter += (level, category, message) => { Console.WriteLine (message); };
				session.Run (startInfo, options);
				WaitFor (TimeSpan.FromSeconds (30), () => session.IsConnected);
				Assert.True (session.IsConnected, "Debugger should have connected but it did not.");
				// we need to wait here for a while to allow the breakpoints to hit
				// but we need to timeout
				TimeSpan timeout = TimeSpan.FromSeconds (60);
				while (session.IsConnected && breakcountHitCount < 2 && timeout >= TimeSpan.Zero) {
					Thread.Sleep (10);
					timeout = timeout.Subtract (TimeSpan.FromMilliseconds (10));
				}
				WaitFor (2000);
				int expected = 2;
				Assert.AreEqual (expected, breakcountHitCount, $"Should have hit {expected} breakpoints. Only hit {breakcountHitCount}");
				Assert.True (b.Uninstall (proj), "Project should have uninstalled.");
				session.Exit ();
			}
		}

#pragma warning disable 414
		static object [] DebuggerTestCases = new object [] {
			new object[] {
				/* useSharedRuntime */   false,
				/* embedAssemblies */    true,
				/* fastDevType */        "Assemblies",
			},
			new object[] {
				/* useSharedRuntime */   false,
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies",
			},
			new object[] {
				/* useSharedRuntime */   true,
				/* embedAssemblies */    true,
				/* fastDevType */        "Assemblies",
			},
			new object[] {
				/* useSharedRuntime */   true,
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies",
			},
			new object[] {
				/* useSharedRuntime */   true,
				/* embedAssemblies */    true,
				/* fastDevType */        "Assemblies:Dexes",
			},
			new object[] {
				/* useSharedRuntime */   true,
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies:Dexes",
			},
		};
#pragma warning restore 414

		[Test]
		[TestCaseSource (nameof(DebuggerTestCases))]
		[Retry (1)]
		public void ApplicationRunsWithDebuggerAndBreaks (bool useSharedRuntime, bool embedAssemblies, string fastDevType)
		{
			if (!CommercialBuildAvailable) {
				Assert.Ignore ("Test does not run on the Open Source Builds.");
				return;
			}
			if (!HasDevices) {
				Assert.Ignore ("Test needs a device attached.");
				return;
			}
			var proj = new XamarinFormsAndroidApplicationProject () {
				IsRelease = false,
				AndroidUseSharedRuntime = useSharedRuntime,
				EmbedAssembliesIntoApk = embedAssemblies,
				AndroidFastDeploymentType = fastDevType
			};
			var abis = new string [] { "armeabi-v7a", "x86" };
			proj.SetProperty (KnownProperties.AndroidSupportedAbis, string.Join (";", abis));
			proj.SetDefaultTargetDevice ();
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				string apiLevel;
				proj.TargetFrameworkVersion = b.LatestTargetFrameworkVersion (out apiLevel);
				proj.AndroidManifest = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" android:versionCode=""1"" android:versionName=""1.0"" package=""UnnamedProject.UnnamedProject"">
	<uses-sdk android:minSdkVersion=""24"" android:targetSdkVersion=""{apiLevel}"" />
	<application android:label=""${{PROJECT_NAME}}"">
	</application >
</manifest>";
				b.Save (proj, saveProject: true);
				proj.NuGetRestore (Path.Combine (Root, b.ProjectDirectory), b.PackagesDirectory);
				Assert.True (b.Build (proj), "Project should have built.");
				Assert.True (b.Install (proj), "Project should have installed.");

				int breakcountHitCount = 0;
				ManualResetEvent resetEvent = new ManualResetEvent (false);
				var sw = new Stopwatch ();
				// setup the debugger
				var session = new SoftDebuggerSession ();
				session.Breakpoints = new BreakpointStore {
					{ Path.Combine (Root, b.ProjectDirectory, "MainActivity.cs"),  19 },
					{ Path.Combine (Root, b.ProjectDirectory, "MainPage.xaml.cs"), 14 },
					{ Path.Combine (Root, b.ProjectDirectory, "MainPage.xaml.cs"), 19 },
					{ Path.Combine (Root, b.ProjectDirectory, "App.xaml.cs"), 12 },
				};
				session.TargetHitBreakpoint += (sender, e) => {
					Console.WriteLine ($"BREAK {e.Type}");
					breakcountHitCount++;
					session.Continue ();
				};
				var rnd = new Random ();
				int port = rnd.Next (10000, 20000);
				TestContext.Out.WriteLine ($"{port}");
				var args = new SoftDebuggerConnectArgs ("", IPAddress.Loopback, port) {
					MaxConnectionAttempts = 10,
				};
				var startInfo = new SoftDebuggerStartInfo (args) {
					WorkingDirectory = Path.Combine (b.ProjectDirectory, proj.IntermediateOutputPath, "android", "assets"),
				};
				var options = new DebuggerSessionOptions () {
					EvaluationOptions = EvaluationOptions.DefaultOptions,
				};
				options.EvaluationOptions.UseExternalTypeResolver = true;
				ClearAdbLogcat ();
				Assert.True (b.RunTarget (proj, "_Run", parameters: new string [] {
					$"AndroidSdbTargetPort={port}",
					$"AndroidSdbHostPort={port}",
					"AndroidAttachDebugger=True",
				}), "Project should have run.");

				Assert.IsTrue (WaitForDebuggerToStart (Path.Combine (Root, b.ProjectDirectory, "logcat.log")), "Activity should have started");
				// we need to give a bit of time for the debug server to start up.
				WaitFor (2000);
				session.LogWriter += (isStderr, text) => { Console.WriteLine (text); };
				session.OutputWriter += (isStderr, text) => { Console.WriteLine (text); };
				session.DebugWriter += (level, category, message) => { Console.WriteLine (message); };
				session.Run (startInfo, options);
				WaitFor (TimeSpan.FromSeconds (30), () => session.IsConnected);
				Assert.True (session.IsConnected, "Debugger should have connected but it did not.");
				// we need to wait here for a while to allow the breakpoints to hit
				// but we need to timeout
				TimeSpan timeout = TimeSpan.FromSeconds (60);
				int expected = 3;
				while (session.IsConnected && breakcountHitCount < 3 && timeout >= TimeSpan.Zero) {
					Thread.Sleep (10);
					timeout = timeout.Subtract (TimeSpan.FromMilliseconds (10));
				}
				WaitFor (2000);
				Assert.AreEqual (expected, breakcountHitCount, $"Should have hit {expected} breakpoints. Only hit {breakcountHitCount}");
				breakcountHitCount = 0;
				ClearAdbLogcat ();
				ClickButton (proj.PackageName, "myXFButton", "CLICK ME");
				while (session.IsConnected && breakcountHitCount < 1 && timeout >= TimeSpan.Zero) {
					Thread.Sleep (10);
					timeout = timeout.Subtract (TimeSpan.FromMilliseconds (10));
				}
				expected = 1;
				Assert.AreEqual (expected, breakcountHitCount, $"Should have hit {expected} breakpoints. Only hit {breakcountHitCount}");
				Assert.True (b.Uninstall (proj), "Project should have uninstalled.");
				session.Exit ();
			}
		}
	}
}
