using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NUnit.Framework;
using Xamarin.Android.Tasks;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Build.Tests {
	[TestFixture]
	[Category ("Node-2")]
	[Parallelizable (ParallelScope.Self)]
	public class AndroidComputeResPathsTests : BaseTest {

		List<BuildErrorEventArgs> errors;
		List<BuildWarningEventArgs> warnings;
		List<BuildMessageEventArgs> messages;
		MockBuildEngine engine;

		[SetUp]
		public void Setup ()
		{
			engine = new MockBuildEngine (TestContext.Out,
				errors: errors = new List<BuildErrorEventArgs> (),
				warnings: warnings = new List<BuildWarningEventArgs> (),
				messages: messages = new List<BuildMessageEventArgs> ());
		}

#pragma warning disable 414
		static object [] AndroidComputeResPathsChecks () => new object [] {
			new object[] {
				/* Identity */	                Path.Combine ("Assets", "asset1.txt"),
				/* LogicalName */               Path.Combine ("Assets", "subfolder", "asset1.txt"),
				/* expectedIntermediateFile */  Path.Combine ("subfolder", "asset1.txt"),
				/* expectedResultFile */        Path.Combine ("Assets", "asset1.txt"),
			},
			new object[] {
				/* Identity */	                Path.Combine ("Assets", "asset1.txt"),
				/* LogicalName */               null,
				/* expectedIntermediateFile */  Path.Combine ("asset1.txt"),
				/* expectedResultFile */        Path.Combine ("Assets", "asset1.txt"),
			},
			new object[] {
				/* Identity */	                Path.Combine ("Assets", "subfolder", "asset1.txt"),
				/* LogicalName */               null,
				/* expectedIntermediateFile */  Path.Combine ("subfolder", "asset1.txt"),
				/* expectedResultFile */        Path.Combine ("Assets", "subfolder", "asset1.txt"),
			},
			new object[] {
				/* Identity */	                Path.Combine ("Assets", "asset1.txt"),
				/* LogicalName */               Path.Combine ("%FOO%Assets", "subfolder", "asset1.txt"),
				/* expectedIntermediateFile */  Path.Combine ("subfolder", "asset1.txt"),
				/* expectedResultFile */        Path.Combine ("Assets", "asset1.txt"),
			},
			new object[] {
				/* Identity */	                Path.Combine ("Assets", "asset1.txt"),
				/* LogicalName */               Path.Combine ("%FOO%Assets", "subfolder", "..", "..", "asset1.txt"),
				/* expectedIntermediateFile */  Path.Combine ("asset1.txt"),
				/* expectedResultFile */        Path.Combine ("Assets", "asset1.txt"),
			},
		};
#pragma warning restore 414

		[Test]
		[TestCaseSource (nameof (AndroidComputeResPathsChecks))]
		public void AndroidComputeResPathsTest (string identity, string logicalName, string expectedIntermediateFile, string expectedResultFile)
		{
			var hash = Files.HashString (identity + logicalName + expectedIntermediateFile + expectedResultFile);
			var projectDir = Path.Combine (Root, "temp", $"{nameof (AndroidComputeResPathsTest)}_{hash}");
			TestOutputDirectories [TestContext.CurrentContext.Test.ID] = projectDir;
			var intermdiateDir = Path.Combine (projectDir, "assets");

			var metaData = new Dictionary<string, string> ();
			if (!string.IsNullOrEmpty (logicalName))
				metaData.Add ("LogicalName", logicalName.Replace ("%FOO%", "\\"));

			// The file needs to exist on disk
			var path = Path.Combine (projectDir, identity);
			Directory.CreateDirectory (Path.GetDirectoryName (path));
			File.WriteAllText (path, contents: "");

			var assets = new List<ITaskItem> () {
				new TaskItem (identity, metaData),
			};

			string previousWorking = Environment.CurrentDirectory;
			try {
				Environment.CurrentDirectory = projectDir;
				var task = new AndroidComputeResPaths () {
					BuildEngine = engine,
					IntermediateDir = intermdiateDir,
					ProjectDir = projectDir,
					Prefixes = "Assets",
					ResourceFiles = assets.ToArray (),
				};
				Assert.True (task.Execute (), "task should have succeeded.");
				Assert.AreEqual (1, task.IntermediateFiles.Length, "Expected 1 entry in the IntermediateFiles Output");
				Assert.AreEqual (1, task.ResolvedResourceFiles.Length, "Expected 1 entry in the ResolvedResourceFiles Output");
				var expected = Path.GetFullPath (Path.Combine (intermdiateDir, expectedIntermediateFile));
				StringAssert.AreEqualIgnoringCase (
					expected,
					task.IntermediateFiles [0].ItemSpec,
					$"Expected {expected} in task.IntermediateFiles, but got {task.IntermediateFiles [0].ItemSpec}"
				);
				StringAssert.AreEqualIgnoringCase (
					expectedResultFile,
					task.ResolvedResourceFiles [0].ItemSpec,
					$"Expected {expectedResultFile} in task.ResolvedResourceFiles, but got {task.ResolvedResourceFiles [0].ItemSpec}"
				);
			} finally {
				Environment.CurrentDirectory = previousWorking;
			}
		}

		static readonly object [] XA1029DuplicateFileLinksSource = new object [] {
			new object [] {
				/* identity */  Path.Combine ("Resources", "layout", "layout1.xml"),
				/* link1 */     Path.Combine ("Resources", "layout", "layout1.xml"),
				/* link2 */     null,
				/* resources */ true,
			},
			new object [] {
				/* identity */  Path.Combine ("Resources", "layout", "layout1.xml"),
				/* link1 */     Path.Combine ("Resources", "layout", "Layout1.xml"),
				/* link2 */     null,
				/* resources */ true,
			},
			new object [] {
				/* identity */  Path.Combine ("Resources", "layout", "layout1.xml"),
				/* link1 */     Path.Combine ("Resources", "layout-xhdpi", "layout1.xml"),
				/* link2 */     Path.Combine ("Resources", "layout-xhdpi", "layout1.axml"),
				/* resources */ true,
			},
			new object [] {
				/* identity */  Path.Combine ("Assets", "asset1.txt"),
				/* link1 */     Path.Combine ("Assets", "asset2.txt"),
				/* link2 */     Path.Combine ("Assets", "Asset2.txt"),
				/* resources */ false,
			},
		};

		[Test]
		[TestCaseSource (nameof (XA1029DuplicateFileLinksSource))]
		public void XA1029DuplicateFileLinks (string identity, string link1, string link2, bool resources)
		{
			var hash = Tools.Files.HashString (identity + link1 + link2 + resources);
			var projectDir = Path.Combine (Root, "temp", $"{nameof (XA1029DuplicateFileLinks)}_{hash}");
			TestOutputDirectories [TestContext.CurrentContext.Test.ID] = projectDir;

			var path = Path.Combine (projectDir, identity);
			Directory.CreateDirectory (Path.GetDirectoryName (path));
			File.WriteAllText (path, contents: "");

			ITaskItem resource1 = new TaskItem (identity);
			resource1.SetMetadata ("Link", link1);
			ITaskItem resource2 = new TaskItem (identity);
			resource2.SetMetadata ("Link", link2);

			var intermediateDir = Path.Combine (projectDir, "obj", "Debug", resources ? "res" : "assets");
			Directory.CreateDirectory (intermediateDir);

			string previousWorking = Environment.CurrentDirectory;
			try {
				Environment.CurrentDirectory = projectDir;

				var task = new AndroidComputeResPaths {
					BuildEngine = engine,
					ResourceFiles = new ITaskItem [] { new TaskItem (resource1), new TaskItem (resource2) },
					IntermediateDir = intermediateDir,
					LowercaseFilenames = resources
				};

				Assert.IsTrue (task.Execute (), "Task should succeed.");
				Assert.IsTrue (warnings.Count > 0, "Task should produce a warning.");
				BuildWarningEventArgs warning = warnings [0];
				Assert.AreEqual ("XA1029", warning.Code);
				StringAssert.Contains (resource1.GetMetadata ("Link"), warning.Message);
				StringAssert.Contains (resource2.GetMetadata ("Link"), warning.Message);
			} finally {
				Environment.CurrentDirectory = previousWorking;
			}
		}

		static readonly object [] XA1030ConflictingResourceFilesSource = new object [] {
			new object [] {
				/* resource1 */ Path.Combine ("Resources", "layout", "layout1.axml"),
				/* resource2 */ Path.Combine ("Resources", "layout", "layout1.xml"),
			},
			new object [] {

				/* resource1 */ Path.Combine ("Resources", "layout", "layout1.xml"),
				/* resource2 */ Path.Combine ("Resources", "layout", "layout1.axml"),
			},
		};

		[Test]
		[TestCaseSource (nameof (XA1030ConflictingResourceFilesSource))]
		public void XA1030ConflictingResourceFiles (string resource1, string resource2)
		{
			var hash = Tools.Files.HashString (resource1 + resource2);
			var projectDir = Path.Combine (Root, "temp", $"{nameof (XA1030ConflictingResourceFiles)}_{hash}");
			TestOutputDirectories [TestContext.CurrentContext.Test.ID] = projectDir;

			var path = Path.Combine (projectDir, resource1);
			Directory.CreateDirectory (Path.GetDirectoryName (path));
			File.WriteAllText (path, contents: "");
			path = Path.Combine (projectDir, resource2);
			Directory.CreateDirectory (Path.GetDirectoryName (path));
			File.WriteAllText (path, contents: "");

			var intermediateDir = Path.Combine (projectDir, "obj", "Debug", "res");
			Directory.CreateDirectory (intermediateDir);

			string previousWorking = Environment.CurrentDirectory;
			try {
				Environment.CurrentDirectory = projectDir;
				var task = new AndroidComputeResPaths {
					BuildEngine = engine,
					ResourceFiles = new ITaskItem [] { new TaskItem (resource1), new TaskItem (resource2) },
					IntermediateDir = intermediateDir,
					LowercaseFilenames = true
				};

				Assert.IsFalse (task.Execute (), "Task should fail.");
				Assert.IsTrue (errors.Count > 0, "Task should produce an error.");
				BuildErrorEventArgs error = errors [0];
				Assert.AreEqual ("XA1030", error.Code);
				StringAssert.Contains (resource1, error.Message);
				StringAssert.Contains (resource2, error.Message);
			} finally {
				Environment.CurrentDirectory = previousWorking;
			}
		}

		static readonly object [] XA1031ConflictingFileLinksSource = new object [] {
			new object [] {
				/* identity1 */ Path.Combine ("Resources", "layout", "layout1.xml"),
				/* identity2 */ Path.Combine ("Resources", "layout-xhdpi", "layout1.xml"),
				/* link1 */     Path.Combine ("Resources", "layout", "layout2.xml"),
				/* link2 */     Path.Combine ("Resources", "layout-xhdpi", "layout1.xml"),
				/* resources */ true,
			},
			new object [] {
				/* identity1 */ Path.Combine ("Resources", "layout", "layout1.xml"),
				/* identity2 */ null,
				/* link1 */     Path.Combine ("Resources", "layout", "layout2.xml"),
				/* link2 */     Path.Combine ("Resources", "layout", "Layout1.xml"),
				/* resources */ true,
			},
			new object [] {
				/* identity1 */ Path.Combine ("Resources", "layout", "layout1.xml"),
				/* identity2 */ Path.Combine ("Resources", "layout-xhdpi", "layout1.xml"),
				/* link1 */     Path.Combine ("Resources", "layout", "layout2.xml"),
				/* link2 */     Path.Combine ("Resources", "layout-xhdpi", "Layout1.xml"),
				/* resources */ true,
			},
			new object [] {
				/* identity1 */ Path.Combine ("Assets", "asset1.txt"),
				/* identity2 */ Path.Combine ("Assets", "asset3.txt"),
				/* link1 */     Path.Combine ("Assets", "asset2.txt"),
				/* link2 */     Path.Combine ("Assets", "Asset3.txt"),
				/* resources */ false,
			},
		};

		[Test]
		[TestCaseSource (nameof (XA1031ConflictingFileLinksSource))]
		public void XA1031ConflictingFileLinks (string identity1, string link1, string identity2, string link2, bool resources)
		{
			var hash = Tools.Files.HashString (identity1 + link1 + identity2 + link2 + resources);
			var projectDir = Path.Combine (Root, "temp", $"{nameof (XA1031ConflictingFileLinks)}_{hash}");
			TestOutputDirectories [TestContext.CurrentContext.Test.ID] = projectDir;

			var path = Path.Combine (projectDir, identity1);
			Directory.CreateDirectory (Path.GetDirectoryName (path));
			File.WriteAllText (path, contents: "");
			path = Path.Combine (projectDir, identity2);
			Directory.CreateDirectory (Path.GetDirectoryName (path));
			File.WriteAllText (path, contents: "");

			ITaskItem resource1 = new TaskItem (identity1);
			resource1.SetMetadata ("Link", link1);
			ITaskItem resource2 = new TaskItem (identity2);
			resource2.SetMetadata ("Link", link2);

			var intermediateDir = Path.Combine (projectDir, "obj", "Debug", resources ? "res" : "assets");
			Directory.CreateDirectory (intermediateDir);

			string previousWorking = Environment.CurrentDirectory;
			try {
				Environment.CurrentDirectory = projectDir;

				var task = new AndroidComputeResPaths {
					BuildEngine = engine,
					ResourceFiles = new ITaskItem [] { new TaskItem (resource1), new TaskItem (resource2) },
					IntermediateDir = intermediateDir,
					LowercaseFilenames = resources
				};

				Assert.IsFalse (task.Execute (), "Task should fail.");
				Assert.IsTrue (errors.Count > 0, "Task should produce an error.");
				BuildErrorEventArgs error = errors [0];
				Assert.AreEqual ("XA1031", error.Code);
				StringAssert.Contains (resource1.ItemSpec, error.Message);
				StringAssert.Contains (resource2.ItemSpec, error.Message);
				StringAssert.Contains (resource1.GetMetadata ("Link"), error.Message);
				StringAssert.Contains (resource2.GetMetadata ("Link"), error.Message);
			} finally {
				Environment.CurrentDirectory = previousWorking;
			}
		}
	}
}
