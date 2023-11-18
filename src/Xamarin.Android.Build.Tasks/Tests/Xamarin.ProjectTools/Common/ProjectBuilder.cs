using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using Microsoft.Build.Framework;
using System.Xml.Linq;
using Xamarin.Android.Tools;

namespace Xamarin.ProjectTools
{
	public class ProjectBuilder : Builder
	{
		public ProjectBuilder (string projectDirectory)
		{
			ProjectDirectory = projectDirectory;
			Target = "Build";
			ThrowOnBuildFailure = true;
			BuildLogFile = "build.log";
		}

		public bool CleanupOnDispose { get; set; }
		public bool CleanupAfterSuccessfulBuild { get; set; }
		public string ProjectDirectory { get; set; }
		public string Target { get; set; }

		public ILogger Logger { get; set; }

		protected override void Dispose (bool disposing)
		{
			if (disposing)
				if (CleanupOnDispose)
					Cleanup ();
		}

		bool built_before;
		bool last_build_result;

		public BuildOutput Output { get; private set; }

		public void Save (XamarinProject project, bool doNotCleanupOnUpdate = false, bool saveProject = true)
		{
			var files = project.Save (saveProject);

			if (!built_before) {
				if (project.ShouldPopulate) {
					if (Directory.Exists (ProjectDirectory)) {
						FileSystemUtils.SetDirectoryWriteable (ProjectDirectory);
						Directory.Delete (ProjectDirectory, true);
					}
					project.Populate (ProjectDirectory, files);
				}

				project.CopyNuGetConfig (ProjectDirectory);
			}
			else
				project.UpdateProjectFiles (ProjectDirectory, files, doNotCleanupOnUpdate);
		}

		public bool Build (XamarinProject project, bool doNotCleanupOnUpdate = false, string [] parameters = null, bool saveProject = true, Dictionary<string, string> environmentVariables = null)
		{
			Save (project, doNotCleanupOnUpdate, saveProject);

			Output = project.CreateBuildOutput (this);

			bool result = BuildInternal (Path.Combine (ProjectDirectory, project.ProjectFilePath), Target, parameters, environmentVariables, restore: project.ShouldRestorePackageReferences, binlogName: Path.GetFileNameWithoutExtension (BuildLogFile));
			built_before = true;

			if (CleanupAfterSuccessfulBuild)
				Cleanup ();
			last_build_result = result;
			return result;
		}

		public bool Install (XamarinProject project, bool doNotCleanupOnUpdate = false, string [] parameters = null, bool saveProject = true, Dictionary<string, string> environmentVariables = null)
		{
			//NOTE: since $(BuildingInsideVisualStudio) is set, Build will not happen by default
			return RunTarget (project, "Build,Install", doNotCleanupOnUpdate, parameters, saveProject: saveProject, environmentVariables: environmentVariables);
		}

		public bool Uninstall (XamarinProject project, bool doNotCleanupOnUpdate = false, bool saveProject = true)
		{
			return RunTarget (project, "Uninstall", doNotCleanupOnUpdate);
		}

		public bool Restore (XamarinProject project, bool doNotCleanupOnUpdate = false, string [] parameters = null)
		{
			return RunTarget (project, "Restore", doNotCleanupOnUpdate, parameters);
		}

		public bool Clean (XamarinProject project, bool doNotCleanupOnUpdate = false)
		{
			return RunTarget (project, "Clean", doNotCleanupOnUpdate);
		}

		public bool UpdateAndroidResources (XamarinProject project, bool doNotCleanupOnUpdate = false, string [] parameters = null, Dictionary<string, string> environmentVariables = null)
		{
			return RunTarget (project, "UpdateAndroidResources", doNotCleanupOnUpdate, parameters, environmentVariables);
		}

		public bool DesignTimeBuild (XamarinProject project, string target = "Compile", bool doNotCleanupOnUpdate = false, string [] parameters = null)
		{
			if (parameters == null) {
				return RunTarget (project, target, doNotCleanupOnUpdate, parameters: new string [] { "DesignTimeBuild=True" });
			} else {
				var designTimeParameters = new string [parameters.Length + 1];
				parameters.CopyTo (designTimeParameters, 0);
				designTimeParameters [parameters.Length] = "DesignTimeBuild=True";
				return RunTarget (project, target, doNotCleanupOnUpdate, parameters: designTimeParameters);
			}
		}

		public bool RunTarget (XamarinProject project, string target, bool doNotCleanupOnUpdate = false, string [] parameters = null, Dictionary<string, string> environmentVariables = null, bool saveProject = true)
		{
			var oldTarget = Target;
			Target = target;
			try {
				return Build (project, doNotCleanupOnUpdate: doNotCleanupOnUpdate, parameters: parameters, saveProject: saveProject, environmentVariables: environmentVariables);
			} finally {
				Target = oldTarget;
			}
		}

		public void Cleanup ()
		{
			// don't clean up if we failed so we can get the
			//logs
			if (!last_build_result)
				return;
			built_before = false;

			var projectDirectory = Path.Combine (XABuildPaths.TestOutputDirectory, ProjectDirectory);
			if (Directory.Exists (projectDirectory)) {
				FileSystemUtils.SetDirectoryWriteable (projectDirectory);
				Directory.Delete (projectDirectory, true);
			}
		}

		public struct RuntimeInfo
		{
			public string Name;
			public string Runtime;
			public string Abi;
			public int Size;
		}

		public RuntimeInfo [] GetSupportedRuntimes ()
		{
			var runtimeInfo = new List<RuntimeInfo> ();
			var runtimeDirs = new HashSet<string> ();
			var rootRuntimeDirs = Directory.GetDirectories (TestEnvironment.DotNetPreviewPacksDirectory, $"Microsoft.Android.Runtime.{XABuildConfig.AndroidDefaultTargetDotnetApiLevel}.*");
			foreach (var dir in rootRuntimeDirs) {
				runtimeDirs.Add (Directory.GetDirectories (dir).LastOrDefault ());
			}

			foreach (var runtimeDir in runtimeDirs) {
				foreach (var file in Directory.EnumerateFiles (runtimeDir, "libmono-android.*.so", SearchOption.AllDirectories)) {
					string fullFilePath = Path.GetFullPath (file);
					DirectoryInfo parentDir = Directory.GetParent (fullFilePath);
					if (parentDir == null)
						continue;
					string[] items = Path.GetFileName (fullFilePath).Split ('.' );
					if (items.Length != 3)
						continue;
					var fi = new FileInfo (fullFilePath);
					runtimeInfo.Add (new RuntimeInfo () {
						Name = "libmonodroid.so",
						Runtime = items [1], // release|debug
						Abi = parentDir.Name, // armaebi|x86|arm64-v8a
						Size = (int)fi.Length, // int
					});
				}
			}
			return runtimeInfo.ToArray ();
		}
	}
}
