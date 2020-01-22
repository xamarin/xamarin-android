using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Xamarin.Android.Tasks;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	[TestFixture]
	public class JavaDaemonClientTests : BaseTest
	{
		readonly List<int> processIds = new List<int> ();

		string MSBuildDirectory {
			get {
				using (var builder = new Builder ()) {
					return builder.AndroidMSBuildDirectory;
				}
			}
		}

		void AreEqualIgnoringLineEndings (string a, string b)
		{
			Assert.AreEqual (a.Replace ("\r\n", "\n").Trim (), b.Replace ("\r\n", "\n").Trim ());
		}

		JavaDaemonClient Connect ()
		{
			var java = Path.Combine (JavaSdkPath, "bin", "java");
			var java_daemon = Path.Combine (MSBuildDirectory, "java-daemon.jar");
			var classpath = new [] {
				Path.Combine (MSBuildDirectory, "r8.jar"),
				Path.Combine (MSBuildDirectory, "bundletool.jar"),
			};
			var client = new JavaDaemonClient { Log = Console.WriteLine };
			client.Connect (java, $"-jar \"{java_daemon}\" -classpath \"{string.Join (Path.DirectorySeparatorChar.ToString(), classpath)}\"");
			var processId = client.ProcessId;
			Assert.Greater (processId, 0, $"Invalid process id: {processId}");
			processIds.Add (processId.Value);
			return client;
		}

		string R8Path => Path.Combine (MSBuildDirectory, "r8.jar");

		string BundleToolPath => Path.Combine (MSBuildDirectory, "bundletool.jar");

		class Expected
		{
			public const string R8Version =
@"R8 1.5.68
build engineering";
			public const string D8Version =
@"D8 1.5.68
build engineering";
			public const string BundleToolVersion = "0.10.2";
		}

		[TearDown]
		public void TearDown ()
		{
			foreach (var id in processIds) {
				try {
					var p = Process.GetProcessById (id);
					Assert.IsTrue (p.HasExited, $"Process was not killed: {p.Id}");
				} catch (ArgumentException) {
					//Process not running, all good
				}
			}
		}

		[Test]
		public void R8Version ()
		{
			using (var client = Connect ()) {
				(int exitCode, string stdout, string stderr) = client.Invoke ("com.android.tools.r8.R8", R8Path, "--version");
				Assert.AreEqual (0, exitCode, $"Exited with {exitCode}: {stdout} {stderr}");
				AreEqualIgnoringLineEndings (Expected.R8Version, stdout);
				Assert.AreEqual ("", stderr.Trim ());
			}
		}

		[Test]
		public void BadExitCode ()
		{
			using (var client = Connect ()) {
				(int exitCode, string stdout, string stderr) = client.Invoke ("com.android.tools.r8.R8", R8Path, "");
				Assert.AreEqual (1, exitCode, $"Exited with {exitCode}: {stdout} {stderr}");
				Assert.AreEqual ("", stdout.Trim ());
				StringAssert.Contains ("Usage:", stderr);
			}
		}

		[Test]
		public void DifferentMainClass ()
		{
			using (var client = Connect ()) {
				(int exitCode, string stdout, string stderr) = client.Invoke ("com.android.tools.r8.R8", R8Path, "--version");
				Assert.AreEqual (0, exitCode, $"Exited with {exitCode}: {stdout} {stderr}");
				AreEqualIgnoringLineEndings (Expected.R8Version, stdout);
				Assert.AreEqual ("", stderr.Trim ());

				(exitCode, stdout, stderr) = client.Invoke ("com.android.tools.r8.D8", R8Path, "--version");
				Assert.AreEqual (0, exitCode, $"Exited with {exitCode}: {stdout} {stderr}");
				AreEqualIgnoringLineEndings (Expected.D8Version, stdout);
				Assert.AreEqual ("", stderr.Trim ());
			}
		}

		[Test]
		public void DifferentJar ()
		{
			using (var client = Connect ()) {
				(int exitCode, string stdout, string stderr) = client.Invoke ("com.android.tools.r8.R8", R8Path, "--version");
				Assert.AreEqual (0, exitCode, $"Exited with {exitCode}: {stdout} {stderr}");
				AreEqualIgnoringLineEndings (Expected.R8Version, stdout);

				(exitCode, stdout, stderr) = client.Invoke ("com.android.tools.build.bundletool.BundleToolMain", BundleToolPath, "version");
				Assert.AreEqual (0, exitCode, $"Exited with {exitCode}: {stdout} {stderr}");
				AreEqualIgnoringLineEndings (Expected.BundleToolVersion, stdout);
				Assert.AreEqual ("", stderr.Trim ());
			}
		}

		[Test]
		public void MultilineOutput ()
		{
			using (var client = Connect ()) {
				(int exitCode, string stdout, string stderr) = client.Invoke ("com.android.tools.r8.R8", R8Path, "--help");
				Assert.AreEqual (0, exitCode, $"Exited with {exitCode}: {stdout} {stderr}");
				Assert.AreEqual ("", stderr.Trim ());
				var lines = stdout.Split ('\n');
				Assert.Greater (lines.Length, 1, $"Should have a multi-line response: {stdout}");
			}
		}
	}
}
