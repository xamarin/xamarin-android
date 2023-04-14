using System;
using System.IO;

using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Framework;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	public class ProfileNativeCode : AndroidAsyncTask
	{
		const string PythonName = "python3";

		public override string TaskPrefix => "PNC";

		string[]? pathExt;

		public string DeviceSdkVersion      { get; set; }
		public bool DeviceIsEmulator        { get; set; }
		public string[] DeviceSupportedAbis { get; set; }
		public string DevicePrimaryABI      { get; set; }

		[Required]
		public string SimplePerfDirectory   { get; set; }
		public string NdkPythonDirectory    { get; set; }

		public async override System.Threading.Tasks.Task RunTaskAsync ()
		{
			string? pythonPath = null;

			if (!String.IsNullOrEmpty (NdkPythonDirectory)) {
				pythonPath = Path.Combine (NdkPythonDirectory, "bin", PythonName);
				if (!File.Exists (pythonPath)) {
					pythonPath = null;
				}
			}

			if (String.IsNullOrEmpty (pythonPath)) {
				Log.LogWarning ($"NDK {PythonName} not found, will attempt to find one in a system location");
				pythonPath = FindPython ();
				if (String.IsNullOrEmpty (pythonPath)) {
					Log.LogWarning ($"System {PythonName} not found, will attempt to use executable name without path");
					pythonPath = PythonName;
				}
			}

			string? appProfilerScript = Path.Combine (SimplePerfDirectory, "app_profiler.py");
			if (!File.Exists (appProfilerScript)) {
				Log.LogError ($"Profiling script {appProfilerScript} not found");
				return;
			}

			// TODO: prepare a directory with unstripped native libraries (for use with the profiler's -lib argument)
			Console.WriteLine ($"python3 path: {pythonPath}");
			Console.WriteLine ($"profiler script path: {appProfilerScript}");

			var python = new PythonRunner (Log, pythonPath);

			// TODO: params
			bool success = await python.RunScript (appProfilerScript);
		}

		string? FindPython ()
		{
			// TODO: might be a good idea to try to look for `python` and check its version, if python3 isn't found
			if (OS.IsWindows) {
				string? envvar = Environment.GetEnvironmentVariable ("PATHEXT");
				if (String.IsNullOrEmpty (envvar)) {
					pathExt = new string[] { ".exe", ".bat", ".cmd" };
				} else {
					pathExt = envvar.Split (Path.PathSeparator);
				}
			}

			string? pathVar = Environment.GetEnvironmentVariable ("PATH")?.Trim ();
			if (String.IsNullOrEmpty (pathVar)) {
				return null;
			}

			foreach (string dir in pathVar.Split (Path.PathSeparator)) {
				string? exe = GetExecutablePath (dir, PythonName);
				if (!String.IsNullOrEmpty (exe)) {
					return exe;
				}
			}

			return null;
		}

		string? GetExecutablePath (string dir, string baseExeName)
		{
			string exePath = Path.Combine (dir, baseExeName);
			if (!OS.IsWindows) {
				if (File.Exists (exePath)) {
					return exePath;
				}

				return null;
			}

			foreach (string ext in pathExt) {
				string exe = $"{exePath}{ext}";
				if (File.Exists (exe)) {
					return exe;
				}
			}

			return null;
		}
	}
}
