using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xamarin.Android.Tasks;
using Xamarin.Android.Tools;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests.Tasks {

	[TestFixture]
	public class NdkUtilTests : BaseTest {

		List<BuildErrorEventArgs> errors;
		List<BuildWarningEventArgs> warnings;
		MockBuildEngine engine;

		[SetUp]
		public void Setup ()
		{
			engine = new MockBuildEngine (TestContext.Out, errors = new List<BuildErrorEventArgs> (), warnings = new List<BuildWarningEventArgs> ());
		}

		[Test]
		public void TestNdkUtil ()
		{
			var log = new TaskLoggingHelper (engine, TestName);
			using (var builder = new Builder ()) {
				var ndkDir = AndroidNdkPath;
				var sdkDir = AndroidSdkPath;
				NdkTools ndk = NdkTools.Create (ndkDir, log: log);
				ndk.OSBinPath = TestEnvironment.OSBinDirectory;
				MonoAndroidHelper.AndroidSdk = new AndroidSdkInfo ((arg1, arg2) => { }, sdkDir, ndkDir, AndroidSdkResolver.GetJavaSdkPath ());
				var platforms = ndk.GetSupportedPlatforms ();
				Assert.AreNotEqual (0, platforms.Count (), "No platforms found");
				var arch = AndroidTargetArch.X86;
				Assert.IsTrue (ndk.ValidateNdkPlatform (arch, enableLLVM: false));
				Assert.AreEqual (0, errors.Count, "NdkTools.ValidateNdkPlatform should not have returned false.");
				int level = ndk.GetMinimumApiLevelFor (arch);
				int expected = 21;
				Assert.AreEqual (expected, level, $"Min Api Level for {arch} should be {expected}.");
				var compilerNoQuotes = ndk.GetToolPath (NdkToolKind.CompilerC, arch, level);
				Assert.AreEqual (0, errors.Count, "NdkTools.GetToolPath should not have errored.");
				Assert.NotNull (compilerNoQuotes, "NdkTools.GetToolPath returned null for NdkToolKind.CompilerC.");
				compilerNoQuotes = ndk.GetToolPath (NdkToolKind.CompilerCPlusPlus, arch, level);
				Assert.AreEqual (0, errors.Count, "NdkTools.GetToolPath should not have errored.");
				Assert.NotNull (compilerNoQuotes, "NdkTools.GetToolPath returned null for NdkToolKind.CompilerCPlusPlus.");
				var gas = ndk.GetToolPath (NdkToolKind.Assembler, arch, level);
				Assert.AreEqual (0, errors.Count, "NdkTools.GetToolPath should not have errored.");
				Assert.NotNull (gas, "NdkTools.GetToolPath returned null for NdkToolKind.Assembler.");
				var inc = ndk.GetDirectoryPath (NdkToolchainDir.PlatformInclude, arch, level);
				Assert.NotNull (inc, " NdkTools.GetToolPath should not return null for NdkToolchainDir.PlatformInclude");
				var libPath = ndk.GetDirectoryPath (NdkToolchainDir.PlatformLib, arch, level);
				Assert.NotNull (libPath, "NdkTools.GetDirectoryPath should not return null for NdkToolchainDir.PlatformLib");
				string ld = ndk.GetToolPath (NdkToolKind.Linker, arch, level);
				Assert.AreEqual (0, errors.Count, "NdkTools.GetToolPath should not have errored.");
				Assert.NotNull (ld, "NdkTools.GetToolPath returned null for NdkToolKind.Linker.");
			}
		}
	}
}
