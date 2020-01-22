using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xamarin.Android.Tasks;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	public class BaseTest
	{
		public static ConcurrentDictionary<string, string> TestOutputDirectories = new ConcurrentDictionary<string, string> ();

		[SetUpFixture]
		public class SetUp {
			public static bool HasDevices {
				get;
				private set;
			}

			public static string DeviceAbi {
				get;
				private set;
			}

			public static bool CommercialBuildAvailable {
				get;
				private set;
			}

			public static string AndroidMSBuildDirectory {
				get;
				private set;
			}

			static SetUp ()
			{
				using (var builder = new Builder ()) {
					CommercialBuildAvailable = File.Exists (Path.Combine (builder.AndroidMSBuildDirectory, "Xamarin.Android.Common.Debugging.targets"));
					AndroidMSBuildDirectory = builder.AndroidMSBuildDirectory;
				}
			}

			[OneTimeSetUp]
			public void BeforeAllTests ()
			{
				try {
					var adbTarget = Environment.GetEnvironmentVariable ("ADB_TARGET");
					int sdkVersion = -1;
					var result = RunAdbCommand ($"{adbTarget} shell getprop ro.build.version.sdk");
					if (result.Contains ("*")) {
						//NOTE: We may get a result of:
						//
						//27* daemon not running; starting now at tcp:5037
						//* daemon started successfully
						result = result.Split ('*').First ().Trim ();
					}
					HasDevices = int.TryParse (result, out sdkVersion) && sdkVersion != -1;
					if (HasDevices) {
						if (sdkVersion >= 21)
							DeviceAbi = RunAdbCommand ("shell getprop ro.product.cpu.abilist64").Trim ();

						if (string.IsNullOrEmpty (DeviceAbi))
							DeviceAbi = RunAdbCommand ("shell getprop ro.product.cpu.abi") ?? RunAdbCommand ("shell getprop ro.product.cpu.abi2");
					}
				} catch (Exception ex) {
					Console.Error.WriteLine ("Failed to determine whether there is Android target emulator or not: " + ex);
				}
			}

			[OneTimeTearDown]
			public void AfterAllTests ()
			{
				if (System.Diagnostics.Debugger.IsAttached)
					return;

				//NOTE: adb.exe can cause a couple issues on Windows
				//	1) it holds a lock on ~/android-toolchain, so a future build that needs to delete/recreate would fail
				//	2) the MSBuild <Exec /> task *can* hang until adb.exe exits

				try {
					RunAdbCommand ("kill-server", true);
				} catch (Exception ex) {
					Console.Error.WriteLine ("Failed to run adb kill-server: " + ex);
				}

				//NOTE: in case `adb kill-server` fails, kill the process as a last resort
				foreach (var p in Process.GetProcessesByName ("adb.exe"))
					p.Kill ();
			}

		}

		protected bool HasDevices => SetUp.HasDevices;

		protected string DeviceAbi => SetUp.DeviceAbi;

		protected bool IsWindows => TestEnvironment.IsWindows;

		protected bool IsMacOS => TestEnvironment.IsMacOS;

		protected bool IsLinux => TestEnvironment.IsLinux;

		public string StagingPath {
			get { return Environment.GetFolderPath (Environment.SpecialFolder.MyDocuments); }
		}

		public string Root {
			get {
				return Path.GetFullPath (XABuildPaths.TestOutputDirectory);
			}
		}

		public static bool CommercialBuildAvailable => SetUp.CommercialBuildAvailable;

		public static string AndroidMSBuildDirectory => SetUp.AndroidMSBuildDirectory;

		char [] invalidChars = { '{', '}', '(', ')', '$', ':', ';', '\"', '\'', ',', '=' };

		public string TestName {
			get {
				var result = TestContext.CurrentContext.Test.Name;
				foreach (var c in invalidChars.Concat (Path.GetInvalidPathChars ().Concat (Path.GetInvalidFileNameChars ()))) {
					result = result.Replace (c, '_');
				}
				return result.Replace ("_", string.Empty);
			}
		}

		public static string JavaSdkPath => AndroidSdkResolver.GetJavaSdkPath ();

		public static string AndroidSdkPath => AndroidSdkResolver.GetAndroidSdkPath ();

		public static string AndroidNdkPath => AndroidSdkResolver.GetAndroidNdkPath();

		/// <summary>
		/// Windows can only create a file of 255 characters: This type of path is composed of components separated by backslashes, each up to the value returned in the lpMaximumComponentLength parameter of the GetVolumeInformation function (this value is commonly 255 characters).
		/// See: https://docs.microsoft.com/en-us/windows/win32/fileio/naming-a-file#maximum-path-length-limitation
		/// </summary>
		public const int MaxFileName = 255;

		static Lazy<bool> longPaths = new Lazy<bool> (() => {
			if (!TestEnvironment.IsWindows) {
				return true;
			}
			var path = Path.Combine (Path.GetTempPath (), "foo".PadRight (MaxFileName, 'N'));
			try {
				File.WriteAllText (path, "");
				return true;
			} catch {
				return false;
			} finally {
				// If the file exists, we should be able to delete it
				if (File.Exists (path)) {
					File.Delete (path);
				}
			}
		});

		public static bool LongPathsSupported => longPaths.Value;

		protected static void WaitFor(int milliseconds)
		{
			var pause = new ManualResetEvent(false);
			pause.WaitOne(milliseconds);
		}

		protected static string RunAdbCommand (string command, bool ignoreErrors = true, int timeout = 30)
		{
			string ext = Environment.OSVersion.Platform != PlatformID.Unix ? ".exe" : "";
			string adb = Path.Combine (AndroidSdkPath, "platform-tools", "adb" + ext);
			string adbTarget = Environment.GetEnvironmentVariable ("ADB_TARGET");
			return RunProcess (adb, $"{adbTarget} {command}");
		}

		protected static string RunProcess (string exe, string args)
		{
			TestContext.Out.WriteLine ($"{nameof(RunProcess)}: {exe} {args}");
			var info = new ProcessStartInfo (exe, args) {
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
			};
			using (var proc = Process.Start (info)) {
				if (!proc.WaitForExit ((int)TimeSpan.FromSeconds (30).TotalMilliseconds)) {
					proc.Kill ();
					TestContext.Out.WriteLine ($"{nameof (RunProcess)} timed out: {exe} {args}");
					return null; //Don't try to read stdout/stderr
				}
				var result = proc.StandardOutput.ReadToEnd ().Trim () + proc.StandardError.ReadToEnd ().Trim ();
				return result;
			}
		}

		protected string CreateFauxAndroidNdkDirectory (string path)
		{
			var androidNdkDirectory = Path.Combine (Root, path);
			Directory.CreateDirectory (androidNdkDirectory);
			Directory.CreateDirectory (Path.Combine (androidNdkDirectory, "toolchains"));
			var sb  = new StringBuilder ();
			if (IsWindows) {
				sb.AppendLine ("@echo off");
				sb.AppendLine ($"echo GNU Make 3.81");
			} else {
				sb.AppendLine ("#!/bin/bash");
				sb.AppendLine ($"echo \"GNU Make 3.81\"");
			}
			CreateFauxExecutable (Path.Combine (androidNdkDirectory, IsWindows ? "ndk-build.cmd" : "ndk-build"), sb);
			sb.Clear();
			if (IsWindows) {
				sb.AppendLine("@echo off");
			} else {
				sb.AppendLine("#!/bin/bash");
			}
			CreateFauxExecutable (Path.Combine (androidNdkDirectory, IsWindows ? "ndk-stack.cmd" : "ndk-stack"), sb);
			return androidNdkDirectory;
		}

		protected string CreateFauxAndroidSdkDirectory (string path, string buildToolsVersion, ApiInfo [] apiLevels = null)
		{
			var androidSdkDirectory = Path.Combine (Root, path);
			var androidSdkToolsPath = Path.Combine (androidSdkDirectory, "tools");
			var androidSdkBinPath = Path.Combine (androidSdkToolsPath, "bin");
			var androidSdkPlatformToolsPath = Path.Combine (androidSdkDirectory, "platform-tools");
			var androidSdkPlatformsPath = Path.Combine (androidSdkDirectory, "platforms");
			var androidSdkBuildToolsPath = Path.Combine (androidSdkDirectory, "build-tools", buildToolsVersion ?? string.Empty);
			Directory.CreateDirectory (androidSdkDirectory);
			Directory.CreateDirectory (androidSdkToolsPath);
			Directory.CreateDirectory (androidSdkBinPath);
			Directory.CreateDirectory (androidSdkPlatformToolsPath);
			Directory.CreateDirectory (androidSdkPlatformsPath);
			Directory.CreateDirectory (androidSdkBuildToolsPath);

			File.WriteAllText (Path.Combine (androidSdkPlatformToolsPath, IsWindows ? "adb.exe" : "adb"), "");
			if (!string.IsNullOrEmpty (buildToolsVersion)) {
				File.WriteAllText (Path.Combine (androidSdkBuildToolsPath, IsWindows ? "zipalign.exe" : "zipalign"), "");
				File.WriteAllText (Path.Combine (androidSdkBuildToolsPath, IsWindows ? "aapt.exe" : "aapt"), "");
			}
			File.WriteAllText (Path.Combine (androidSdkToolsPath, IsWindows ? "lint.bat" : "lint"), "");

			List<ApiInfo> defaults = new List<ApiInfo> ();
			for (int i = 10; i < 26; i++) {
				defaults.Add (new ApiInfo () {
					Id = i.ToString (),
				});
			}
			foreach (var level in apiLevels ?? defaults.ToArray ()) {
				var dir = Path.Combine (androidSdkPlatformsPath, $"android-{level.Id}");
				Directory.CreateDirectory(dir);
				File.WriteAllText (Path.Combine (dir, "android.jar"), "");
			}
			return androidSdkDirectory;
		}

		public struct ApiInfo {
			public string Id;
			public int Level;
			public string Name;
			public string FrameworkVersion;
			public bool Stable;
		}

		protected string CreateFauxReferencesDirectory (string path, ApiInfo [] versions)
		{
			string referencesDirectory = Path.Combine (Root, path);
			Directory.CreateDirectory (referencesDirectory);
			Directory.CreateDirectory (Path.Combine (referencesDirectory, "MonoAndroid", "v1.0", "RedistList"));
			File.WriteAllText (Path.Combine (referencesDirectory, "MonoAndroid", "v1.0", "mscorlib.dll"), "");
			File.WriteAllText (Path.Combine (referencesDirectory, "MonoAndroid", "v1.0", "RedistList", "FrameworkList.xml"),
				$"<FileList Redist=\"MonoAndroid\" Name=\"Xamarin.Android Base Class Libraries\"></FileList>");
			foreach (var v in versions) {
				Directory.CreateDirectory (Path.Combine (referencesDirectory, "MonoAndroid", v.FrameworkVersion));
				Directory.CreateDirectory (Path.Combine (referencesDirectory, "MonoAndroid", v.FrameworkVersion, "RedistList"));
				File.WriteAllText (Path.Combine (referencesDirectory, "MonoAndroid", v.FrameworkVersion, "MonoAndroid.dll"), "");
				File.WriteAllText (Path.Combine (referencesDirectory, "MonoAndroid", v.FrameworkVersion, "AndroidApiInfo.xml"),
					$"<AndroidApiInfo>\n<Id>{v.Id}</Id>\n<Level>{v.Level}</Level>\n<Name>{v.Name}</Name>\n<Version>{v.FrameworkVersion}</Version>\n<Stable>{v.Stable}</Stable>\n</AndroidApiInfo>");
				File.WriteAllText (Path.Combine (referencesDirectory, "MonoAndroid", v.FrameworkVersion, "RedistList", "FrameworkList.xml"),
					$"<FileList Redist=\"MonoAndroid\" Name=\"Xamarin.Android {v.FrameworkVersion} Support\" IncludeFramework=\"v1.0\"></FileList>");
			}
			return referencesDirectory;
		}

		protected string CreateFauxJavaSdkDirectory (string path, string javaVersion, out string javaExe, out string javacExe)
		{
			javaExe = IsWindows ? "Java.cmd" : "java.bash";
			javacExe  = IsWindows ? "Javac.cmd" : "javac.bash";
			var jarSigner = IsWindows ? "jarsigner.exe" : "jarsigner";
			var javaPath = Path.Combine (Root, path);
			var javaBinPath = Path.Combine (javaPath, "bin");
			Directory.CreateDirectory (javaBinPath);

			CreateFauxJavaExe (Path.Combine (javaBinPath, javaExe), javaVersion);
			CreateFauxJavacExe (Path.Combine (javaBinPath, javacExe), javaVersion);

			File.WriteAllText (Path.Combine (javaBinPath, jarSigner), "");
			return javaPath;
		}

		void CreateFauxJavaExe (string javaExeFullPath, string version)
		{
			var sb  = new StringBuilder ();
			if (IsWindows) {
				sb.AppendLine ("@echo off");
				sb.AppendLine ($"echo java version \"{version}\"");
				sb.AppendLine ($"echo Java(TM) SE Runtime Environment (build {version}-b13)");
				sb.AppendLine ($"echo Java HotSpot(TM) 64-Bit Server VM (build 25.101-b13, mixed mode)");
			} else {
				sb.AppendLine ("#!/bin/bash");
				sb.AppendLine ($"echo \"java version \\\"{version}\\\"\"");
				sb.AppendLine ($"echo \"Java(TM) SE Runtime Environment (build {version}-b13)\"");
				sb.AppendLine ($"echo \"Java HotSpot(TM) 64-Bit Server VM (build 25.101-b13, mixed mode)\"");
			}
			CreateFauxExecutable (javaExeFullPath, sb);
		}

		void CreateFauxJavacExe (string javacExeFullPath, string version)
		{
			var sb  = new StringBuilder ();
			if (IsWindows) {
				sb.AppendLine ("@echo off");
				sb.AppendLine ($"echo javac {version}");
			} else {
				sb.AppendLine ("#!/bin/bash");
				sb.AppendLine ($"echo \"javac {version}\"");
			}
			CreateFauxExecutable (javacExeFullPath, sb);
		}

		void CreateFauxExecutable (string exeFullPath, StringBuilder sb) {
			File.WriteAllText (exeFullPath, sb.ToString ());
			if (!IsWindows) {
				RunProcess ("chmod", $"u+x {exeFullPath}");
			}
		}

		protected ProjectBuilder CreateApkBuilder (string directory = null, bool cleanupAfterSuccessfulBuild = false, bool cleanupOnDispose = false)
		{
			if (string.IsNullOrEmpty (directory))
				directory = Path.Combine ("temp", TestName);
			TestOutputDirectories [TestContext.CurrentContext.Test.ID] = Path.Combine (Root, directory);
			return BuildHelper.CreateApkBuilder (directory, cleanupAfterSuccessfulBuild, cleanupOnDispose);
		}

		protected ProjectBuilder CreateDllBuilder (string directory = null, bool cleanupAfterSuccessfulBuild = false, bool cleanupOnDispose = false)
		{
			if (string.IsNullOrEmpty (directory))
				directory = Path.Combine ("temp", TestName);
			TestOutputDirectories [TestContext.CurrentContext.Test.ID] = Path.Combine (Root, directory);
			return BuildHelper.CreateDllBuilder (directory, cleanupAfterSuccessfulBuild, cleanupOnDispose);
		}

		protected void AssertFileContentsMatch (string file1, string file2)
		{
			if (!FileCompare (file1, file2)) {
				TestContext.AddTestAttachment (file1, Path.GetFileName (file1));
				TestContext.AddTestAttachment (file2, Path.GetFileName (file2));
				Assert.Fail ($"{file1} and {file2} do not match.");
			}
		}

		protected bool FileCompare (string file1, string file2)
		{
			FileAssert.Exists (file1);
			FileAssert.Exists (file2);
			using (var stream1 = File.OpenRead (file1))
			using (var stream2 = File.OpenRead (file2)) {
				return StreamCompare (stream1, stream2);
			}
		}

		protected bool StreamCompare (Stream stream1, Stream stream2)
		{
			Assert.IsNotNull (stream1, "stream1 of StreamCompare should not be null");
			Assert.IsNotNull (stream2, "stream2 of StreamCompare should not be null");
			string hash1 = MonoAndroidHelper.HashBytes (ReadAllBytesIgnoringLineEndings (stream1));
			string hash2 = MonoAndroidHelper.HashBytes (ReadAllBytesIgnoringLineEndings (stream2));
			return hash1 == hash2;
		}

		protected byte [] ReadAllBytesIgnoringLineEndings (Stream stream)
		{
			using (var memoryStream = new MemoryStream ()) {
				int readByte;
				while ((readByte = stream.ReadByte()) != -1) {
					byte b = (byte)readByte;
					if (b != '\r' && b != '\n') {
						memoryStream.WriteByte (b);
					}
				}
 				return memoryStream.ToArray ();
			}
		}

		protected string GetPathToLatestBuildTools (string exe)
		{
			var path = Path.Combine (AndroidSdkPath, "build-tools");
			foreach (var dir in Directory.GetDirectories (path, "*", SearchOption.TopDirectoryOnly).OrderByDescending (x => new Version (Path.GetFileName (x)))) {
				var aapt2 = Path.Combine (dir, exe);
				if (File.Exists (aapt2))
					return dir;
			}
			return Path.Combine (path, "25.0.2");
		}

		protected string GetPathToAapt2 ()
		{
			var exe = IsWindows ? "aapt2.exe" : "aapt2";
			var path = Path.Combine (AndroidMSBuildDirectory, IsWindows ? "" : (IsMacOS ? "Darwin" : "Linux"));
			if (File.Exists (Path.Combine (path, exe)))
				return path;
			return GetPathToLatestBuildTools (exe);
		}

		protected string GetPathToAapt ()
		{
			var exe = IsWindows ? "aapt.exe" : "aapt";
			return GetPathToLatestBuildTools (exe);
		}

		[SetUp]
		public void TestSetup ()
		{
			TestContext.Out.WriteLine ($"[TESTLOG] Test {TestName} Starting");
			TestContext.Out.Flush ();
		}

		[TearDown]
		protected virtual void CleanupTest ()
		{
			TestContext.Out.WriteLine ($"[TESTLOG] Test {TestName} Complete");
			TestContext.Out.WriteLine ($"[TESTLOG] Test {TestName} Outcome={TestContext.CurrentContext.Result.Outcome.Status}");
			TestContext.Out.Flush ();
			string outputDir = null;
			if (!TestOutputDirectories.TryGetValue (TestContext.CurrentContext.Test.ID, out outputDir))
				return;
			if (System.Diagnostics.Debugger.IsAttached || string.IsNullOrEmpty (outputDir))
					return;
			// find the "root" directory just below "temp" and clean from there because
			// some tests create multiple subdirectories
			var output = Path.GetFullPath (outputDir);
			while (!Directory.GetParent (output).Name.EndsWith ("temp", StringComparison.OrdinalIgnoreCase)) {
					output = Directory.GetParent (output).FullName;
			}
			if (!Directory.Exists (output))
				return;
			if (TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Passed || 
			    TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Skipped) {
				FileSystemUtils.SetDirectoryWriteable (output);
				Directory.Delete (output, recursive: true);
			} else {
				foreach (var file in Directory.GetFiles (Path.Combine (output), "*.log", SearchOption.AllDirectories)) {
					TestContext.AddTestAttachment (file, Path.GetFileName (output));
				}
			}
		}
	}
}

