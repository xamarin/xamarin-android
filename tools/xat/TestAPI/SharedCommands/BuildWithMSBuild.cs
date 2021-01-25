using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.Shared
{
	class BuildWithMSBuild : SharedTestCommand
	{
		/// <summary>
		///   MSBuild target to invoke instead of the standard build one
		/// </summary>
		public string Target { get; set; } = String.Empty;

		/// <summary>
		///   If specified, it overrides the project file path as specified in the test suite for which this command is
		///   executed.  Allows to e.g. build MSBuild projects which aren't part of a suite but are required by some
		///   suite to run (e.g. Java.Interop native library)
		/// </summary>
		public string ProjectFilePath { get; set; } = String.Empty;

		public BuildWithMSBuild ()
			: base (nameof (BuildWithMSBuild), "Build a test suite using MSBuild/xabuild")
		{}

		protected override async Task<bool> Execute (XATest suite)
		{
			var msbuild = new MSBuildRunner (Context, msbuildPath: Context.MSBuildBinary);

			var arguments = new List<string> {
				"/r"
			};

			if (Target.Length > 0) {
				arguments.Add ($"/t:{Target}");
			}

			string buildingWhat;
			string projectPath;
			string projectName;
			if (ProjectFilePath.Length > 0) {
				buildingWhat = "project";
				projectPath = ProjectFilePath;
				projectName = Path.GetFileName (ProjectFilePath);
			} else {
				buildingWhat = "suite";
				projectPath = suite.TestProjectFilePath;
				projectName = suite.Name;
			}

			CustomizeBuildArguments (arguments);
			Log.InfoLine ($"Building {buildingWhat} '{projectName}'");
			string logTag = $"build_test_{suite.ID}";
			return await msbuild.Run (
				projectPath,
				logTag,
				arguments: arguments,
				binlogName: logTag,
				workingDirectory: Path.GetDirectoryName (projectPath)
			);
		}

		protected virtual void CustomizeBuildArguments (List<string> arguments)
		{}
	}
}
