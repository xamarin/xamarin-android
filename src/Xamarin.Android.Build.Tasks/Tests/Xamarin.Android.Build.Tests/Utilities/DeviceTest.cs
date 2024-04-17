using NUnit.Framework;
using NUnit.Framework.Interfaces;
using System;
using System.Globalization;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using Xamarin.ProjectTools;
using System.Runtime.InteropServices;

namespace Xamarin.Android.Build.Tests
{
	public class DeviceTest: BaseTest
	{
		public const string GuestUserName = "guest1";

		protected string DeviceAbi { get; private set; }

		protected int DeviceSdkVersion { get; private set;}

		static string _shellEchoOutput = null;
		protected static bool IsDeviceAttached (bool refreshCachedValue = false)
		{
			if (string.IsNullOrEmpty (_shellEchoOutput) || refreshCachedValue) {
				// run this twice as sometimes the first time returns the
				// device as "offline".
				RunAdbCommand ("devices");
				var devices = RunAdbCommand ("devices");
				TestContext.Out.WriteLine ($"LOG adb devices: {devices}");
				_shellEchoOutput = RunAdbCommand ("shell echo OK", timeout: 15);
			}
			return _shellEchoOutput.Contains ("OK");
		}

		/// <summary>
		/// Checks if there is a device available
		/// * Defaults to Assert.Fail ()
		/// </summary>
		public void AssertHasDevices (bool fail = true)
		{
			if (!IsDeviceAttached (refreshCachedValue: true)) {
				var message = "This test requires an attached device or emulator.";
				if (fail) {
					Assert.Fail (message);
				} else {
					Assert.Ignore (message);
				}
			}
		}

		[OneTimeSetUp]
		public void DeviceSetup ()
		{
			TestContext.Out.WriteLine ($"LOG DeviceSetup: Enter!!!");
			Console.WriteLine ($"LOG DeviceSetup: Enter Console!!!");
			Console.ReadLine ();
			if (!string.IsNullOrEmpty (DeviceAbi))
				return;
			if (IsDeviceAttached ()) {
				try {
					DeviceSdkVersion = GetSdkVersion ();
					TestContext.Out.WriteLine ($"LOG DeviceSetup: {DeviceSdkVersion}");
					if (DeviceSdkVersion != -1) {
						if (DeviceSdkVersion >= 21)
							DeviceAbi = RunAdbCommand ("shell getprop ro.product.cpu.abilist64").Trim ();

						if (string.IsNullOrEmpty (DeviceAbi))
							DeviceAbi = (RunAdbCommand ("shell getprop ro.product.cpu.abi") ?? RunAdbCommand ("shell getprop ro.product.cpu.abi2")) ?? "x86_64";

						if (DeviceAbi.Contains (",")) {
							DeviceAbi = DeviceAbi.Split (',')[0];
						}
					} else {
						TestContext.Out.WriteLine ($"LOG GetSdkVersion: {DeviceSdkVersion}");
						DeviceAbi = "x86_64";
					}
				} catch (Exception ex) {
					Console.Error.WriteLine ("Failed to determine whether there is Android target emulator or not: " + ex);
				}
				SetAdbLogcatBufferSize (128);
				CreateGuestUser (GuestUserName);
				return;
			}
			TestContext.Out.WriteLine ($"LOG DeviceSetup: No Device!!!!");
			DeviceAbi = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64-v8a" : "x86_64";
		}

		[OneTimeTearDown]
		protected virtual void DeviceTearDown ()
		{
			if (IsDeviceAttached ()) {
				// make sure we are not on a guest user anymore.
				SwitchUser ();
				DeleteGuestUser (GuestUserName);

				var packages = TestPackageNames.Values.ToArray ();
				TestPackageNames.Clear ();
				foreach (var package in packages) {
					RunAdbCommand ($"uninstall {package}");
				}
			}
		}

		[SetUp]
		public virtual void SetupTest ()
		{
			if (!IsDeviceAttached (refreshCachedValue: true)) {
				// something went wrong with the emulator.
				// lets restart it.
				RestartDevice ();
				AssertHasDevices ();
			}

			// We want to start with a clean logcat buffer for each test
			ClearAdbLogcat ();
		}

		[TearDown]
		protected override void CleanupTest ()
		{
			if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed && IsDeviceAttached () &&
					TestOutputDirectories.TryGetValue (TestContext.CurrentContext.Test.ID, out string outputDir)) {
				Directory.CreateDirectory (outputDir);
				string local = Path.Combine (outputDir, "screenshot.png");
				string deviceLog = Path.Combine (outputDir, "logcat-failed.log");
				string remote = "/data/local/tmp/screenshot.png";
				string localUi = Path.Combine (outputDir, "ui.xml");
				RunAdbCommand ($"shell screencap {remote}");

				// Give the app a chance to finish logging
				WaitFor (1000);
				var output = RunAdbCommand ($"logcat -d");
				File.WriteAllText (deviceLog, output);
				RunAdbCommand ($"pull {remote} \"{local}\"");
				RunAdbCommand ($"shell rm {remote}");
				if (File.Exists (local)) {
					TestContext.AddTestAttachment (local);
				} else {
					TestContext.WriteLine ($"{local} did not exist!");
				}
				var ui = GetUI (timeoutInSeconds: 0);
				ui.Save (localUi);
				if (File.Exists (localUi)) {
					TestContext.AddTestAttachment (localUi);
				} else {
					TestContext.WriteLine ($"{localUi} did not exist!");
				}
			}

			base.CleanupTest ();
		}

		protected int GetSdkVersion ()
		{
			var command = $"shell getprop ro.build.version.sdk";
			var result = RunAdbCommand (command);
			if (result.Contains ("*")) {
				// Run the command again, we likely got:
				// * daemon not running; starting now at tcp:5037
				// * daemon started successfully
				// adb.exe: device offline
				TestContext.WriteLine ($"Retrying:\n{command}\n{result}");
				result = RunAdbCommand (command);
			}
			if (!int.TryParse (result, out var sdkVersion)) {
				sdkVersion = -1;
			}
			return sdkVersion;
		}

		public static void RestartDevice ()
		{
			TestContext.Out.WriteLine ($"Trying to restart Emulator");
			// shell out to msbuild and start the emulator again
			var dotnet = new DotNetCLI (Path.Combine (XABuildPaths.TopDirectory, "src", "Xamarin.Android.Build.Tasks", "Tests", "Xamarin.Android.Build.Tests", "Emulator.csproj"));
			dotnet.ProjectDirectory = XABuildPaths.TestAssemblyOutputDirectory;
			Assert.IsTrue (dotnet.Build ("AcquireAndroidTarget", parameters: new string[] { "TestAvdForceCreation=false", $"Configuration={XABuildPaths.Configuration}" }), "Failed to acquire emulator.");
			WaitFor ((int)TimeSpan.FromSeconds (5).TotalMilliseconds);
		}

		protected static void RunAdbInput (string command, params object [] args)
		{
			RunAdbCommand ($"shell {command} {string.Join (" ", args)}");
		}

		protected static string SetAdbLogcatBufferSize (int sizeInMeg)
		{
			return RunAdbCommand ($"logcat -G {sizeInMeg}M");
		}

		protected static string ClearAdbLogcat ()
		{
			return RunAdbCommand ("logcat -c");
		}

		protected static string ClearDebugProperty ()
		{
			return ClearShellProp ("debug.mono.extra");
		}

		protected static string ClearShellProp (string propName)
		{
			return RunAdbCommand ($"shell setprop {propName} \"''\"");
		}

		protected static string AdbStartActivity (string activity)
		{
			return RunAdbCommand ($"shell am start -S -n \"{activity}\"");
		}

		protected static void RunProjectAndAssert (XamarinAndroidApplicationProject proj, ProjectBuilder builder, string logName = "run.log", bool doNotCleanupOnUpdate = false, string [] parameters = null)
		{
			builder.BuildLogFile = logName;
			Assert.True (builder.RunTarget (proj, "Run", doNotCleanupOnUpdate: doNotCleanupOnUpdate, parameters: parameters), "Project should have run.");
		}

		protected static void StartActivityAndAssert (XamarinAndroidApplicationProject proj)
		{
			var result = AdbStartActivity ($"{proj.PackageName}/{proj.JavaPackageName}.MainActivity");
			Assert.IsTrue (result.Contains ("Starting: Intent { cmp="), $"Attempt to start activity failed with:\n{result}");
		}

		protected TimeSpan ProfileFor (Func<bool> func, TimeSpan? timeout = null)
		{
			var stopwatch = new Stopwatch ();
			stopwatch.Start ();
			WaitFor (timeout ?? TimeSpan.FromMinutes (1), func);
			stopwatch.Stop ();
			return stopwatch.Elapsed;
		}

		protected static bool MonitorAdbLogcat (Func<string, bool> action, string logcatFilePath, int timeout = 15)
		{
			string ext = Environment.OSVersion.Platform != PlatformID.Unix ? ".exe" : "";
			string adb = Path.Combine (AndroidSdkPath, "platform-tools", "adb" + ext);
			var info = new ProcessStartInfo (adb, "logcat") {
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
			};

			bool didActionSucceed = false;
			ManualResetEventSlim stdout_done = new ManualResetEventSlim ();
			using (var sw = File.CreateText (logcatFilePath)) {
				using (var proc = Process.Start (info)) {
					proc.OutputDataReceived += (sender, e) => {
						if (e.Data != null) {
							sw.WriteLine (e.Data);
							if (action (e.Data)) {
								didActionSucceed = true;
							}
						} else {
							stdout_done.Set ();
						}
					};
					proc.BeginOutputReadLine ();
					TimeSpan time = TimeSpan.FromSeconds (timeout);
					while (!stdout_done.IsSet && !didActionSucceed && time.TotalMilliseconds > 0) {
						proc.WaitForExit (10);
						time -= TimeSpan.FromMilliseconds (10);
					}
					proc.Kill ();
					proc.WaitForExit ();
					stdout_done.Wait ();
					sw.Flush ();
					return didActionSucceed;
				}
			}
		}

		protected static bool WaitForDebuggerToStart (string logcatFilePath, int timeout = 120)
		{
			bool result = MonitorAdbLogcat ((line) => {
				return line.IndexOf ("Trying to initialize the debugger with options:", StringComparison.OrdinalIgnoreCase) > 0;
			}, logcatFilePath, timeout);
			return result;
		}

		static Regex regex = new Regex (@"\s*(\++)(?<seconds>\d+)s(?<milliseconds>\d+)ms", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

		protected static bool WaitForPermissionActivity (string logcatFilePath, int timeout = 5)
		{
			string activityNamespace = "com.android.permissioncontroller";
			string activityName = "com.android.packageinstaller.permission.ui.ReviewPermissionsActivity";
			bool result = WaitForActivityToStart (activityNamespace, activityName, logcatFilePath, timeout);
			if (result)
				ClickButton ("", "com.android.permissioncontroller:id/continue_button", "CONTINUE");
			return result;
		}

		protected static void ClearBlockingDialogs ()
		{
			ClickButton ("", "android:id/aerr_wait", "Wait");
			WaitFor ((int)TimeSpan.FromSeconds (2).TotalMilliseconds);
		}

		protected static bool WaitForAppBuiltForOlderAndroidWarning (string packageName, string logcatFilePath, int timeout = 5)
		{
			bool result = MonitorAdbLogcat ((line) => {
				return line.Contains ($"ActivityTaskManager: Showing SDK deprecation warning for package {packageName}");
			}, logcatFilePath, timeout);
			if (result)
				ClickButton ("", "android:id/button1", "OK");
			return result;
		}

		protected static bool WaitForActivityToStart (string activityNamespace, string activityName, string logcatFilePath, int timeout = 120)
		{
			return WaitForActivityToStart (activityNamespace, activityName, logcatFilePath, out TimeSpan time, timeout);
		}

		protected static bool WaitForActivityToStart (string activityNamespace, string activityName, string logcatFilePath, out TimeSpan startupTime, int timeout = 120)
		{
			startupTime = TimeSpan.Zero;
			string capturedLine = string.Empty;
			bool result = MonitorAdbLogcat ((line) => {
				capturedLine = line;
				var idx1 = line.IndexOf ("ActivityManager: Displayed", StringComparison.OrdinalIgnoreCase);
				var idx2 = idx1 > 0 ? 0 : line.IndexOf ("ActivityTaskManager: Displayed", StringComparison.OrdinalIgnoreCase);
				return (idx1 > 0 || idx2 > 0) && line.Contains (activityNamespace) && line.Contains (activityName);
			}, logcatFilePath, timeout);
			var match = regex.Match (capturedLine);
			if (match.Success) {
				startupTime = new TimeSpan (0, 0, 0,
					int.Parse (match.Groups ["seconds"].Value, CultureInfo.InvariantCulture),
					int.Parse (match.Groups ["milliseconds"].Value, CultureInfo.InvariantCulture));
			}
			return result;
		}

		protected static XDocument GetUI (int timeoutInSeconds = 120)
		{
			var ui = RunAdbCommand ("exec-out uiautomator dump /dev/tty");
			int time = 0;
			while (ui.Contains ("ERROR:")) {
				ui = RunAdbCommand ("exec-out uiautomator dump /dev/tty");
				WaitFor (1);
				time += 1;
				if (time * 1000 > timeoutInSeconds)
					break;
			}
			ui = ui.Replace ("UI hierchary dumped to: /dev/tty", string.Empty).Trim ();
			try {
				return XDocument.Parse (ui);
			} catch {
				return XDocument.Parse ("<node />");
			}
		}

		protected static (int x, int y, int w, int h)? GetControlBounds (string packageName, string uiElement, string text, int timeoutInSeconds = 120)
		{
			var regex = new Regex (@"[(0-9)]\d*", RegexOptions.Compiled);
			var uiDoc = GetUI (timeoutInSeconds);
			var node = uiDoc.XPathSelectElement ($"//node[contains(@resource-id,'{uiElement}')]");
			if (node == null)
				node = uiDoc.XPathSelectElement ($"//node[contains(@content-desc,'{uiElement}')]");
			if (node == null)
				node = uiDoc.XPathSelectElement ($"//node[contains(@text,'{text}')]");
			if (node == null)
				return null;
			var bounds = node.Attribute ("bounds");
			var matches = regex.Matches (bounds.Value);
			int.TryParse (matches [0].Value, out int x);
			int.TryParse (matches [1].Value, out int y);
			int.TryParse (matches [2].Value, out int w);
			int.TryParse (matches [3].Value, out int h);
			return (x: x, y: y, w: w, h: h);
		}

		protected static bool ClickButton (string packageName, string buttonName, string buttonText, int timeoutInSeconds = 30)
		{
			var bounds = GetControlBounds (packageName, buttonName, buttonText, timeoutInSeconds);
			if (!bounds.HasValue)
				return false;
			RunAdbInput ("input tap", bounds.Value.x + ((bounds.Value.w - bounds.Value.x) / 2), bounds.Value.y + ((bounds.Value.h - bounds.Value.y) / 2));
			return true;
		}

		/// <summary>
		/// Returns the first device listed via `adb devices`
		///
		/// Output is:
		/// > adb devices
		/// List of devices attached
		/// 89RY0AEFA device
		/// </summary>
		/// <returns></returns>
		protected static string GetAttachedDeviceSerial ()
		{
			var text = RunAdbCommand ("devices");
			var lines = text.Split ('\n');
			if (lines.Length < 2) {
				Assert.Fail ($"Unexpected `adb devices` output: {text}");
			}
			var serial = lines [1];
			var index = serial.IndexOf ('\t');
			if (index != -1) {
				serial = serial.Substring (0, index);
			}
			return serial.Trim ();
		}

		protected static string [] GetOverrideDirectoryPaths (string packageName, string abi)
		{
			return new string [] {
				$"/data/data/{packageName}/files/.__override__/{abi}",
				$"/storage/emulated/0/Android/data/{packageName}/files/.__override__/{abi}",
				$"/mnt/shell/emulated/0/Android/data/{packageName}/files/.__override__/{abi}",
				$"/storage/sdcard/Android/data/{packageName}/files/.__override__/{abi}",
			};
		}

		protected static void CreateGuestUser (string username)
		{
			if (GetUserId (username) == -1)
				RunAdbCommand ($"shell pm create-user --guest {username}");
		}

		protected static void DeleteGuestUser (string username)
		{
			int userId = GetUserId (username);
			if (userId > 0)
				RunAdbCommand ($"shell pm remove-user --guest {userId}");
		}

		protected static int GetUserId (string username)
		{
			string output = RunAdbCommand ($"shell pm list users");
			if (string.IsNullOrEmpty (username))
				username = "Owner";
			Regex regex = new Regex ($@"UserInfo{{(?<userId>\d+):{username}", RegexOptions.Compiled);
			Console.WriteLine (output);
			var match = regex.Match (output);
			if (match.Success) {
				return int.Parse (match.Groups ["userId"].Value, CultureInfo.InvariantCulture);
			}
			return -1;
		}

		protected static bool SwitchUser (string username = "")
		{
			int userId = GetUserId (username);
			if (userId == -1)
				userId = 0;
			if (userId >= 0) {
				RunAdbCommand ($"shell am switch-user {userId}");
				return true;
			}
			return false;
		}
	}
}
