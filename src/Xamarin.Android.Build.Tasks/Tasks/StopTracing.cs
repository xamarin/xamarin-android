using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Framework;
using Mono.Unix.Native;

namespace Xamarin.Android.Tasks
{
	public class StopTracing : AndroidTask
	{
		const int WaitTimeout = 3000;

		public override string TaskPrefix => "STR";

		[Required]
		public string DotnetDSRouterPidFile { get; set; }

		[Required]
		public string DotnetTracePidFile { get; set; }

		public string MonoAndroidToolsDirectory { get; set; }

		/// <summary>
		/// Path to the folder that contains dotnet / dotnet.exe.
		/// </summary>
		public string NetCoreRoot { get; set; }

		public bool KillDotnetTrace { get; set; } = false;

		public override bool RunTask ()
		{
			StopProcess (DotnetTracePidFile, kill: KillDotnetTrace);
			StopProcess (DotnetDSRouterPidFile, kill: true);
			return !Log.HasLoggedErrors;
		}

		void StopProcess(string pidFile, bool kill)
		{
			if (!File.Exists (pidFile)) {
				Log.LogDebugMessage ($"File does not exist: {pidFile}");
				return;
			}

			var text = File.ReadAllText (pidFile).Trim ();
			if (!int.TryParse (text, out int pid)) {
				Log.LogDebugMessage ($"Failed to get pid from: {pidFile}, {text}");
				return;
			}

			Log.LogDebugMessage ($"Process Id: {pid}");
			Process process;
			try {
				process = Process.GetProcessById (pid);
			} catch (ArgumentException exc) {
				// thrown if process is not running
				Log.LogDebugMessage ($"Failed to find process: {exc}");
				return;
			}

			if (kill) {
				process.Kill ();
				process.WaitForExit ();
			} else if (RuntimeInformation.IsOSPlatform (OSPlatform.Windows)) {
				RunConsoleCtrl (pid);
				WaitForExitWithTimeout (process);
			} else {
				int ret = Syscall.kill (pid, Signum.SIGINT);
				Log.LogDebugMessage ($"Syscall.kill() returned: {ret}");
				WaitForExitWithTimeout (process);
			}
		}

		void RunConsoleCtrl (int pid)
		{
			var dotnet_path = MonoAndroidHelper.FindDotnet (NetCoreRoot);
			var console_ctrl_path = Path.Combine (MonoAndroidToolsDirectory, "console-ctrl.dll");
			var console_ctrl = new Process () {
				StartInfo = new ProcessStartInfo {
					FileName = dotnet_path,
					Arguments = $"\"{console_ctrl_path}\" {pid}",
					WindowStyle = ProcessWindowStyle.Hidden,
					CreateNoWindow = true,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
				},
			};
			console_ctrl.Start ();
			console_ctrl.WaitForExit ();

			var stderr = console_ctrl.StandardError.ReadToEnd ();
			if (!string.IsNullOrEmpty (stderr)) {
				Log.LogDebugMessage ($"[console-ctrl stderr] {stderr}");
			}
			var stdout = console_ctrl.StandardOutput.ReadToEnd ();
			if (!string.IsNullOrEmpty (stdout)) {
				Log.LogDebugMessage ($"[console-ctrl stdout] {stdout}");
			}
			Log.LogDebugMessage ($"{console_ctrl_path} exited with {console_ctrl.ExitCode}");
		}

		void WaitForExitWithTimeout(Process process)
		{
			if (!process.WaitForExit (WaitTimeout)) {
				process.Kill ();
			}
		}
	}
}
