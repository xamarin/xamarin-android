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
using System.Collections.Generic;

namespace Xamarin.Android.Build.Tests
{
	[TestFixture]
	[Category ("UsesDevice")]
	public class DebuggingTest : DeviceTest {
		[TearDown]
		public void ClearDebugProperties ()
		{
			ClearDebugProperty ();
		}

		void SetTargetFrameworkAndManifest(XamarinAndroidApplicationProject proj, Builder builder)
		{
			string apiLevel;
			proj.TargetFrameworkVersion = builder.LatestTargetFrameworkVersion (out apiLevel);

			// TODO: We aren't sure how to support preview bindings in .NET6 yet.
			if (Builder.UseDotNet && apiLevel == "31") {
				apiLevel = "30";
				proj.TargetFrameworkVersion = "v11.0";
			}

			AssertTargetFrameworkVersionSupported (proj.TargetFrameworkVersion);

			proj.AndroidManifest = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android"" android:versionCode=""1"" android:versionName=""1.0"" package=""{proj.PackageName}"">
	<uses-sdk android:minSdkVersion=""24"" android:targetSdkVersion=""{apiLevel}"" />
	<application android:label=""${{PROJECT_NAME}}"">
	</application >
</manifest>";
		}

		[Test]
		public void ApplicationRunsWithoutDebugger ([Values (false, true)] bool isRelease, [Values (false, true)] bool extractNativeLibs, [Values (false, true)] bool useEmbeddedDex)
		{
			AssertHasDevices ();

			var proj = new XamarinFormsAndroidApplicationProject () {
				IsRelease = isRelease,
			};
			if (isRelease || !CommercialBuildAvailable) {
				proj.SetAndroidSupportedAbis ("armeabi-v7a", "x86");
			}
			proj.SetDefaultTargetDevice ();
			using (var b = CreateApkBuilder (Path.Combine ("temp", TestName))) {
				SetTargetFrameworkAndManifest (proj, b);
				proj.AndroidManifest = proj.AndroidManifest.Replace ("<application ", $"<application android:extractNativeLibs=\"{extractNativeLibs.ToString ().ToLowerInvariant ()}\" android:useEmbeddedDex=\"{useEmbeddedDex.ToString ().ToLowerInvariant ()}\" ");
				Assert.True (b.Install (proj), "Project should have installed.");
				var manifest = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, "android", "AndroidManifest.xml");
				AssertExtractNativeLibs (manifest, extractNativeLibs);
				ClearAdbLogcat ();
				b.BuildLogFile = "run.log";
				if (CommercialBuildAvailable)
					Assert.True (b.RunTarget (proj, "_Run"), "Project should have run.");
				else
					AdbStartActivity ($"{proj.PackageName}/{proj.JavaPackageName}.MainActivity");

				Assert.True (WaitForActivityToStart (proj.PackageName, "MainActivity",
					Path.Combine (Root, b.ProjectDirectory, "logcat.log"), 30), "Activity should have started.");
				b.BuildLogFile = "uninstall.log";
				Assert.True (b.Uninstall (proj), "Project should have uninstalled.");
			}
		}

		[Test]
		public void ClassLibraryMainLauncherRuns ([Values (true, false)] bool preloadAssemblies)
		{
			AssertHasDevices ();

			var path = Path.Combine ("temp", TestName);

			var app = new XamarinAndroidApplicationProject {
				ProjectName = "MyApp",
			};
			if (!CommercialBuildAvailable) {
				app.SetAndroidSupportedAbis ("armeabi-v7a", "x86");
			}
			app.SetDefaultTargetDevice ();
			app.SetProperty ("AndroidEnablePreloadAssemblies", preloadAssemblies.ToString ());

			var lib = new XamarinAndroidLibraryProject {
				ProjectName = "MyLibrary"
			};
			lib.Sources.Add (new BuildItem.Source ("MainActivity.cs") {
				TextContent = () => lib.ProcessSourceTemplate (app.DefaultMainActivity).Replace ("${JAVA_PACKAGENAME}", app.JavaPackageName),
			});
			lib.AndroidResources.Clear ();
			foreach (var resource in app.AndroidResources) {
				lib.AndroidResources.Add (resource);
			}
			var reference = $"..\\{lib.ProjectName}\\{lib.ProjectName}.csproj";
			app.References.Add (new BuildItem.ProjectReference (reference, lib.ProjectName, lib.ProjectGuid));

			// Remove the default MainActivity.cs & AndroidResources
			app.AndroidResources.Clear ();
			app.AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\layout\\foo.xml") {
				TextContent = () =>
@"<?xml version=""1.0"" encoding=""utf-8""?>
<LinearLayout
  xmlns:android=""http://schemas.android.com/apk/res/android""
  android:layout_width=""fill_parent""
  android:layout_height=""wrap_content""
/>"
			});
			app.Sources.Remove (app.GetItem ("MainActivity.cs"));

			using (var libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName)))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
				SetTargetFrameworkAndManifest (app, appBuilder);
				Assert.IsTrue (libBuilder.Build (lib), "library build should have succeeded.");
				Assert.True (appBuilder.Install (app), "app should have installed.");
				ClearAdbLogcat ();
				appBuilder.BuildLogFile = "run.log";
				if (CommercialBuildAvailable)
					Assert.True (appBuilder.RunTarget (app, "_Run"), "Project should have run.");
				else
					AdbStartActivity ($"{app.PackageName}/{app.JavaPackageName}.MainActivity");

				Assert.True (WaitForActivityToStart (app.PackageName, "MainActivity",
					Path.Combine (Root, appBuilder.ProjectDirectory, "logcat.log"), 30), "Activity should have started.");
			}
		}

#pragma warning disable 414
		static object [] DebuggerCustomAppTestCases = new object [] {
			new object[] {
				/* embedAssemblies */    true,
				/* fastDevType */        "Assemblies",
				/* activityStarts */     true,
			},
			new object[] {
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies",
				/* activityStarts */     true,
			},
			new object[] {
				/* embedAssemblies */    true,
				/* fastDevType */        "Assemblies:Dexes",
				/* activityStarts */     true,
			},
			new object[] {
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies:Dexes",
				/* activityStarts */     false,
			},
		};
#pragma warning restore 414

		[Test, Category ("Debugger")]
		[TestCaseSource (nameof (DebuggerCustomAppTestCases))]
		public void CustomApplicationRunsWithDebuggerAndBreaks (bool embedAssemblies, string fastDevType, bool activityStarts)
		{
			AssertCommercialBuild ();
			AssertHasDevices ();
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = false,
				AndroidFastDeploymentType = fastDevType,
			};
			proj.SetAndroidSupportedAbis ("armeabi-v7a", "x86");
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
				SetTargetFrameworkAndManifest (proj, b);
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
					TestContext.WriteLine ($"BREAK {e.Type}, {e.Backtrace.GetFrame (0)}");
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
				b.BuildLogFile = "run.log";
				Assert.True (b.RunTarget (proj, "_Run", doNotCleanupOnUpdate: true, parameters: new string [] {
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
				var expectedTime = TimeSpan.FromSeconds (1);
				var actualTime = ProfileFor (() => session.IsConnected);
				TestContext.Out.WriteLine ($"Debugger connected in {actualTime}");
				Assert.LessOrEqual (actualTime, expectedTime, $"Debugger should have connected within {expectedTime} but it took {actualTime}.");
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
				b.BuildLogFile = "uninstall.log";
				Assert.True (b.Uninstall (proj), "Project should have uninstalled.");
				session.Exit ();
			}
		}

#pragma warning disable 414
		static object [] DebuggerTestCases = new object [] {
			new object[] {
				/* embedAssemblies */    true,
				/* fastDevType */        "Assemblies",
				/* allowDeltaInstall */  false,
				/* user */		 null,
			},
			new object[] {
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies",
				/* allowDeltaInstall */  false,
				/* user */		 null,
			},
			new object[] {
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies",
				/* allowDeltaInstall */  true,
				/* user */		 null,
			},
			new object[] {
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies:Dexes",
				/* allowDeltaInstall */  false,
				/* user */		 null,
			},
			new object[] {
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies:Dexes",
				/* allowDeltaInstall */  true,
				/* user */		 null,
			},
			new object[] {
				/* embedAssemblies */    true,
				/* fastDevType */        "Assemblies",
				/* allowDeltaInstall */  false,
				/* user */		 DeviceTest.GuestUserName,
			},
			new object[] {
				/* embedAssemblies */    false,
				/* fastDevType */        "Assemblies",
				/* allowDeltaInstall */  false,
				/* user */		 DeviceTest.GuestUserName,
			},
		};
#pragma warning restore 414

		[Test, Category ("SmokeTests"), Category ("Debugger")]
		[TestCaseSource (nameof(DebuggerTestCases))]
		public void ApplicationRunsWithDebuggerAndBreaks (bool embedAssemblies, string fastDevType, bool allowDeltaInstall, string username)
		{
			AssertCommercialBuild ();
			AssertHasDevices ();

			var path = Path.Combine ("temp", TestName);
			int userId = GetUserId (username);
			List<string> parameters = new List<string> ();
			if (userId >= 0)
				parameters.Add ($"AndroidDeviceUserId={userId}");
			if (SwitchUser (username)) {
				WaitFor (5);
				ClickButton ("", "android:id/button1", "Yes continue");
			}

			var lib = new XamarinAndroidLibraryProject {
				ProjectName = "Library1",
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () =>
@"public class Foo
{
	public Foo ()
	{
	}
}"
					},
				},
			};

			var app = new XamarinFormsAndroidApplicationProject {
				ProjectName = "App",
				IsRelease = false,
				EmbedAssembliesIntoApk = embedAssemblies,
				AndroidFastDeploymentType = fastDevType
			};
			app.MainPage = app.MainPage.Replace ("InitializeComponent ();", "InitializeComponent (); new Foo ();");
			app.AddReference (lib);
			app.SetAndroidSupportedAbis ("armeabi-v7a", "x86");
			app.SetProperty (KnownProperties._AndroidAllowDeltaInstall, allowDeltaInstall.ToString ());
			app.SetDefaultTargetDevice ();
			using (var libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName)))
			using (var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName))) {
				Assert.True (libBuilder.Build (lib), "Library should have built.");

				SetTargetFrameworkAndManifest (app, appBuilder);
				Assert.True (appBuilder.Install (app, parameters: parameters.ToArray ()), "App should have installed.");

				if (!embedAssemblies) {
					// Check that we deployed .pdb files
					StringAssertEx.ContainsRegex ($@"NotifySync CopyFile.+{app.ProjectName}\.pdb", appBuilder.LastBuildOutput,
						$"{app.ProjectName}.pdb should be deployed!");
					StringAssertEx.ContainsRegex ($@"NotifySync CopyFile.+{lib.ProjectName}\.pdb", appBuilder.LastBuildOutput,
						$"{lib.ProjectName}.pdb should be deployed!");
				}

				int breakcountHitCount = 0;
				ManualResetEvent resetEvent = new ManualResetEvent (false);
				var sw = new Stopwatch ();
				// setup the debugger
				var session = new SoftDebuggerSession ();
				session.Breakpoints = new BreakpointStore {
					{ Path.Combine (Root, appBuilder.ProjectDirectory, "MainActivity.cs"),  20 },
					{ Path.Combine (Root, appBuilder.ProjectDirectory, "MainPage.xaml.cs"), 14 },
					{ Path.Combine (Root, appBuilder.ProjectDirectory, "MainPage.xaml.cs"), 19 },
					{ Path.Combine (Root, appBuilder.ProjectDirectory, "App.xaml.cs"), 12 },
					{ Path.Combine (Root, libBuilder.ProjectDirectory, "Foo.cs"), 4 },
				};
				session.TargetHitBreakpoint += (sender, e) => {
					TestContext.WriteLine ($"BREAK {e.Type}, {e.Backtrace.GetFrame (0)}");
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
					WorkingDirectory = Path.Combine (appBuilder.ProjectDirectory, app.IntermediateOutputPath, "android", "assets"),
				};
				var options = new DebuggerSessionOptions () {
					EvaluationOptions = EvaluationOptions.DefaultOptions,
				};
				options.EvaluationOptions.UseExternalTypeResolver = true;
				ClearAdbLogcat ();
				appBuilder.BuildLogFile = "run.log";

				parameters.Add ($"AndroidSdbTargetPort={port}");
				parameters.Add ($"AndroidSdbHostPort={port}");
				parameters.Add ("AndroidAttachDebugger=True");

				Assert.True (appBuilder.RunTarget (app, "_Run", doNotCleanupOnUpdate: true,
					parameters: parameters.ToArray ()), "Project should have run.");

				Assert.IsTrue (WaitForDebuggerToStart (Path.Combine (Root, appBuilder.ProjectDirectory, "logcat.log")), "Activity should have started");
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
				int expected = 4;
				while (session.IsConnected && breakcountHitCount < 3 && timeout >= TimeSpan.Zero) {
					Thread.Sleep (10);
					timeout = timeout.Subtract (TimeSpan.FromMilliseconds (10));
				}
				WaitFor (2000);
				Assert.AreEqual (expected, breakcountHitCount, $"Should have hit {expected} breakpoints. Only hit {breakcountHitCount}");
				breakcountHitCount = 0;
				ClearAdbLogcat ();
				ClickButton (app.PackageName, "myXFButton", "CLICK ME");
				while (session.IsConnected && breakcountHitCount < 1 && timeout >= TimeSpan.Zero) {
					Thread.Sleep (10);
					timeout = timeout.Subtract (TimeSpan.FromMilliseconds (10));
				}
				expected = 1;
				Assert.AreEqual (expected, breakcountHitCount, $"Should have hit {expected} breakpoints. Only hit {breakcountHitCount}");
				appBuilder.BuildLogFile = "uninstall.log";
				Assert.True (appBuilder.Uninstall (app), "Project should have uninstalled.");
				session.Exit ();
			}
		}
	}
}
