using NUnit.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Xamarin.Android.Build;
using Xamarin.Android.Tasks;
using Xamarin.ProjectTools;
using Microsoft.Android.Build.Tasks;
using System.Runtime.CompilerServices;

namespace Xamarin.Android.Build.Tests
{
	public class BaseTest
	{
		public static ConcurrentDictionary<string, string> TestOutputDirectories = new ConcurrentDictionary<string, string> ();
		public static ConcurrentDictionary<string, string> TestPackageNames = new ConcurrentDictionary<string, string> ();

		[SetUpFixture]
		public class SetUp {
			const string BinutilsSubdir = "ndk";

			public static string DeviceAbi {
				get;
				private set;
			}

			public static int DeviceSdkVersion {
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
#if NETCOREAPP
				Builder.UseDotNet = true;
#else
				Builder.UseDotNet = false;
#endif

				using (var builder = new Builder ()) {
					CommercialBuildAvailable = File.Exists (Path.Combine (builder.AndroidMSBuildDirectory, "Xamarin.Android.Common.Debugging.targets"));
					AndroidMSBuildDirectory = builder.AndroidMSBuildDirectory;
				}
			}

			[OneTimeSetUp]
			public void BeforeAllTests ()
			{
				try {
					DeviceSdkVersion = GetSdkVersion ();
					if (DeviceSdkVersion != -1) {
						if (DeviceSdkVersion >= 21)
							DeviceAbi = RunAdbCommand ("shell getprop ro.product.cpu.abilist64").Trim ();

						if (string.IsNullOrEmpty (DeviceAbi))
							DeviceAbi = RunAdbCommand ("shell getprop ro.product.cpu.abi") ?? RunAdbCommand ("shell getprop ro.product.cpu.abi2");

						if (DeviceAbi.Contains (",")) {
							DeviceAbi = DeviceAbi.Split (',')[0];
						}
					}
				} catch (Exception ex) {
					Console.Error.WriteLine ("Failed to determine whether there is Android target emulator or not: " + ex);
				}

				string hostNdkDir = GetHostBinutilsDir ();
				string xabtDir = Path.GetDirectoryName (typeof (BuildApk).Assembly.Location);
				string symlinkDir = Path.Combine (xabtDir, hostNdkDir);

				// If directory exists then it's either a valid symlink or a real dir, don't touch anything in such instance
				if (Directory.Exists (symlinkDir)) {
					return;
				}

				// If a file exists and is a symlink, then it's probably a dangling link - recreate it
				if (File.Exists (symlinkDir)) {
					if (!SymbolicLink.IsPathSymlink (symlinkDir)) {
						throw new InvalidOperationException ($"File named '{symlinkDir}' exists where a symbolic link to a directory should be.");
					}

					File.Delete (symlinkDir);
				}

				string binutilsDirFullPath = Path.Combine (XABuildPaths.PrefixDirectory, "lib", "xamarin.android", "xbuild", "Xamarin", "Android", hostNdkDir);
				if (!SymbolicLink.Create (symlinkDir, binutilsDirFullPath)) {
					throw new InvalidOperationException ($"Failed to create a symbolic link from '{symlinkDir}' to '{binutilsDirFullPath}'");
				}
			}

			string GetHostBinutilsDir ()
			{
				if (RuntimeInformation.IsOSPlatform (OSPlatform.Windows)) {
					return BinutilsSubdir;
				}

				if (RuntimeInformation.IsOSPlatform (OSPlatform.Linux)) {
					return Path.Combine ("Linux", BinutilsSubdir);
				}

				if (RuntimeInformation.IsOSPlatform (OSPlatform.OSX)) {
					return Path.Combine ("Darwin", BinutilsSubdir);
				}

				throw new InvalidOperationException ($"Unsupported OS");
			}

			int GetSdkVersion ()
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

		/// <summary>
		/// Checks if a commercial Xamarin.Android is available
		/// * Defaults to Assert.Ignore ()
		/// </summary>
		public void AssertCommercialBuild (bool fail = false)
		{
			if (!CommercialBuildAvailable) {
				var message = "This test requires a commercial build of Xamarin.Android.";
				if (fail) {
					Assert.Fail (message);
				} else {
					Assert.Ignore (message);
				}
			}
		}

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

		public static string AndroidSdkPath {
			get {
				return AndroidSdkResolver.GetAndroidSdkPath ();
			}
		}

		public static string AndroidNdkPath {
			get {
				return AndroidSdkResolver.GetAndroidNdkPath ();
			}
		}

		/// <summary>
		/// Windows can only create a file of 255 characters: This type of path is composed of components separated by backslashes, each up to the value returned in the lpMaximumComponentLength parameter of the GetVolumeInformation function (this value is commonly 255 characters).
		/// See: https://docs.microsoft.com/en-us/windows/win32/fileio/naming-a-file#maximum-path-length-limitation
		/// </summary>
		public const int MaxFileName = 255;

		protected static void AssertDexToolSupported (string dexTool)
		{
			if (Builder.UseDotNet && dexTool == "dx") {
				Assert.Ignore ("dx is not supported in .NET 5+");
			}
		}

		protected static void AssertAaptSupported (bool useAapt2)
		{
			if (Builder.UseDotNet && !useAapt2) {
				Assert.Ignore ("aapt(1) is not supported in .NET 5+");
			}
		}

		protected static void WaitFor(int milliseconds)
		{
			var pause = new ManualResetEvent(false);
			pause.WaitOne(milliseconds);
		}

		protected static void WaitFor (TimeSpan timeSpan, Func<bool> func, int intervalInMS = 10)
		{
			var pause = new ManualResetEvent (false);
			TimeSpan total = timeSpan;
			TimeSpan interval = TimeSpan.FromMilliseconds (intervalInMS);
			while (total.TotalMilliseconds > 0) {
				pause.WaitOne (interval);
				total = total.Subtract (interval);
				if (func ()) {
					break;
				}
			}
		}

		protected static string RunAdbCommand (string command, bool ignoreErrors = true, int timeout = 30)
		{
			string ext = Environment.OSVersion.Platform != PlatformID.Unix ? ".exe" : "";
			string adb = Path.Combine (AndroidSdkPath, "platform-tools", "adb" + ext);
			string adbTarget = Environment.GetEnvironmentVariable ("ADB_TARGET");
			return RunProcess (adb, $"{adbTarget} {command}");
		}

		protected static (int code, string stdOutput, string stdError) RunApkDiffCommand (string args)
		{
			string ext = Environment.OSVersion.Platform != PlatformID.Unix ? ".exe" : "";

			try {
				return RunProcessWithExitCode ("apkdiff" + ext, args);
			} catch (System.ComponentModel.Win32Exception) {
				// apkdiff's location might not be in the $PATH, try known location
				var profileDir = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);

				return RunProcessWithExitCode (Path.Combine (profileDir, ".dotnet", "tools", "apkdiff" + ext), args);
			}
		}

		protected static string RunProcess (string exe, string args)
		{
			var (_, stdOutput, stdError) = RunProcessWithExitCode (exe, args);

			return stdOutput + stdError;
		}

		protected static (int code, string stdOutput, string stdError) RunProcessWithExitCode (string exe, string args, int timeoutInSeconds = 30)
		{
			TestContext.Out.WriteLine ($"{nameof(RunProcess)}: {exe} {args}");
			var info = new ProcessStartInfo (exe, args) {
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
			};
			using (var proc = new Process ()) {
				StringBuilder standardOutput = new StringBuilder (), errorOutput = new StringBuilder ();
				proc.StartInfo = info;
				proc.OutputDataReceived += new DataReceivedEventHandler ((sender, e) => {
					if (!string.IsNullOrEmpty (e.Data))
						standardOutput.AppendLine (e.Data);
				});
				proc.ErrorDataReceived += new DataReceivedEventHandler ((sender, e) => {
					if (!string.IsNullOrEmpty (e.Data))
						errorOutput.AppendLine (e.Data);
				});

				proc.Start ();
				proc.BeginOutputReadLine ();
				proc.BeginErrorReadLine ();

				if (!proc.WaitForExit ((int)TimeSpan.FromSeconds (timeoutInSeconds).TotalMilliseconds)) {
					proc.Kill ();
					TestContext.Out.WriteLine ($"{nameof (RunProcess)} timed out: {exe} {args}");
					return (-1, null, null); //Don't try to read stdout/stderr
				}

				proc.WaitForExit ();

				return (proc.ExitCode, standardOutput.ToString ().Trim (), errorOutput.ToString ().Trim ());
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
			javaExe = IsWindows ? "java.cmd" : "java";
			javacExe  = IsWindows ? "javac.cmd" : "javac";

			var javaPath = Path.Combine (Root, path);

			CreateFauxJdk (javaPath, javaVersion, javaVersion, javaVersion);

			var jarSigner = IsWindows ? "jarsigner.exe" : "jarsigner";
			var javaBinPath = Path.Combine (javaPath, "bin");
			File.WriteAllText (Path.Combine (javaBinPath, jarSigner), "");

			return javaPath;
		}

		// https://github.com/xamarin/xamarin-android-tools/blob/683f37508b56c76c24b3287a5687743438625341/tests/Xamarin.Android.Tools.AndroidSdk-Tests/JdkInfoTests.cs#L60-L100
		void CreateFauxJdk (string dir, string releaseVersion, string releaseBuildNumber, string javaVersion)
		{
			Directory.CreateDirectory (dir);

			using (var release = new StreamWriter (Path.Combine (dir, "release"))) {
				release.WriteLine ($"JAVA_VERSION=\"{releaseVersion}\"");
				release.WriteLine ($"BUILD_NUMBER={releaseBuildNumber}");
				release.WriteLine ($"JUST_A_KEY");
			}

			var bin = Path.Combine (dir, "bin");
			var inc = Path.Combine (dir, "include");
			var jre = Path.Combine (dir, "jre");
			var jli = Path.Combine (jre, "lib", "jli");

			Directory.CreateDirectory (bin);
			Directory.CreateDirectory (inc);
			Directory.CreateDirectory (jli);
			Directory.CreateDirectory (jre);

			string quote = IsWindows ? "" : "\"";
			string java = IsWindows
				? $"echo java version \"{javaVersion}\"{Environment.NewLine}"
				: $"echo java version '\"{javaVersion}\"'{Environment.NewLine}";
			java = java +
				$"echo Property settings:{Environment.NewLine}" +
				$"echo {quote}    java.home = {dir}{quote}{Environment.NewLine}" +
				$"echo {quote}    java.vendor = Xamarin.Android Unit Tests{quote}{Environment.NewLine}" +
				$"echo {quote}    java.version = {javaVersion}{quote}{Environment.NewLine}" +
				$"echo {quote}    xamarin.multi-line = line the first{quote}{Environment.NewLine}" +
				$"echo {quote}        line the second{quote}{Environment.NewLine}" +
				$"echo {quote}        .{quote}{Environment.NewLine}";

			string javac =
				$"echo javac {javaVersion}{Environment.NewLine}";

			CreateShellScript (Path.Combine (bin, "jar"), "");
			CreateShellScript (Path.Combine (bin, "java"), java);
			CreateShellScript (Path.Combine (bin, "javac"), javac);
			CreateShellScript (Path.Combine (jli, "libjli.dylib"), "");
			CreateShellScript (Path.Combine (jre, "libjvm.so"), "");
			CreateShellScript (Path.Combine (jre, "jvm.dll"), "");
		}

		// https://github.com/xamarin/xamarin-android-tools/blob/683f37508b56c76c24b3287a5687743438625341/tests/Xamarin.Android.Tools.AndroidSdk-Tests/JdkInfoTests.cs#L108-L132
		void CreateShellScript (string path, string contents)
		{
			if (IsWindows && string.Compare (Path.GetExtension (path), ".dll", true) != 0)
				path += ".cmd";
			using (var script = new StreamWriter (path)) {
				if (IsWindows) {
					script.WriteLine ("@echo off");
				}
				else {
					script.WriteLine ("#!/bin/sh");
				}
				script.WriteLine (contents);
			}
			if (IsWindows)
				return;
			var chmod = new ProcessStartInfo {
				FileName                    = "chmod",
				Arguments                   = $"+x \"{path}\"",
				UseShellExecute             = false,
				RedirectStandardInput       = false,
				RedirectStandardOutput      = true,
				RedirectStandardError       = true,
				CreateNoWindow              = true,
				WindowStyle                 = ProcessWindowStyle.Hidden,
			};
			var p = Process.Start (chmod);
			p.WaitForExit ();
		}

		void CreateFauxExecutable (string exeFullPath, StringBuilder sb) {
			File.WriteAllText (exeFullPath, sb.ToString ());
			if (!IsWindows) {
				RunProcess ("chmod", $"u+x {exeFullPath}");
			}
		}

		protected ProjectBuilder CreateApkBuilder (string directory = null, bool cleanupAfterSuccessfulBuild = false, bool cleanupOnDispose = false, [CallerMemberName] string packageName = "")
		{
			if (string.IsNullOrEmpty (directory))
				directory = Path.Combine ("temp", TestName);
			TestOutputDirectories [TestContext.CurrentContext.Test.ID] = Path.Combine (Root, directory);
			TestPackageNames [packageName] = $"com.xamarin.{packageName}";
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
			string hash1 = Files.HashBytes (ReadAllBytesIgnoringLineEndings (stream1));
			string hash2 = Files.HashBytes (ReadAllBytesIgnoringLineEndings (stream2));
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
			foreach (var dir in Directory.GetDirectories (path, "*", SearchOption.TopDirectoryOnly).OrderByDescending (x => Path.GetFileName (x))) {
				TestContext.Out.WriteLine ($"Found build tools version: {dir}.");
				var aapt2 = Path.Combine (dir, exe);
				if (File.Exists (aapt2))
					return dir;
			}
			return Path.Combine (path, "25.0.2");
		}

		protected string GetPathToZipAlign()
		{
			var exe = IsWindows ? "zipalign.exe" : "zipalign";
			return GetPathToLatestBuildTools (exe);
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

		protected string GetResourceDesignerPath (ProjectBuilder builder, XamarinAndroidProject project)
		{
			string path;
			if (Builder.UseDotNet) {
				path = Path.Combine (Root, builder.ProjectDirectory, project.IntermediateOutputPath);
			} else {
				path = Path.Combine (Root, builder.ProjectDirectory, "Resources");
			}
			return Path.Combine (path, "Resource.designer" + project.Language.DefaultDesignerExtension);
		}

		/// <summary>
		/// Asserts that a AndroidManifest.xml file contains the expected //application/@android:extractNativeLibs value.
		/// </summary>
		protected void AssertExtractNativeLibs (string manifest, bool extractNativeLibs)
		{
			FileAssert.Exists (manifest);
			using (var stream = File.OpenRead (manifest)) {
				var doc = XDocument.Load (stream);
				var element = doc.Root.Element ("application");
				Assert.IsNotNull (element, $"application element not found in {manifest}");
				var ns = (XNamespace) "http://schemas.android.com/apk/res/android";
				var attribute = element.Attribute (ns + "extractNativeLibs");
				Assert.IsNotNull (attribute, $"android:extractNativeLibs attribute not found in {manifest}");
				Assert.AreEqual (extractNativeLibs ? "true" : "false", attribute.Value, $"Unexpected android:extractNativeLibs value found in {manifest}");
			}
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
				try {
					Directory.Delete (output, recursive: true);
				} catch (IOException ex) {
					// This happens on CI occasionally, let's not fail the test
					TestContext.Out.WriteLine ($"Failed to delete '{output}': {ex}");
				}
			} else {
				foreach (var file in Directory.GetFiles (Path.Combine (output), "*.log", SearchOption.AllDirectories)) {
					TestContext.AddTestAttachment (file, Path.GetFileName (output));
				}
			}
		}
	}
}
