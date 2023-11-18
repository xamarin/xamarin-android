using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Xamarin.Android.BuildTools.PrepTasks;

namespace Xamarin.Android.Tools.BootstrapTasks
{
	public class StartAndroidEmulator : Task
	{
		[Output]
		public                  string          AdbTarget       {get; set;}

		[Output]
		public                  int             EmulatorProcessId       {get; set;}

		/// <summary>
		/// Specifies $ANDROID_HOME and $ANDROID_SDK_ROOT. This is the path to the Android SDK.
		/// </summary>
		[Required]
		public                  string          AndroidSdkDirectory     {get; set;}

		/// <summary>
		/// Specifies $ANDROID_PREFS_ROOT. This is not the path to the Android SDK, but a root folder that contains the `.android` folder.
		/// </summary>
		[Required]
		public                  string          AvdManagerHome  {get; set;}
		public                  string          Port            {get; set;}
		public                  string          ImageName       {get; set;} = "XamarinAndroidTestRunner64";
		public                  string          Arguments       {get; set;}
		public                  string          ToolPath        {get; set;}
		public                  string          ToolExe         {get; set;}
		public                  string          LogcatFile      {get; set;}

		public override bool Execute ()
		{
			Run (GetEmulatorPath ());

			if (!string.IsNullOrEmpty (Port)) {
				AdbTarget   = $"-s emulator-{Port}";
			}

			return !Log.HasLoggedErrors;
		}

		string GetEmulatorPath ()
		{
			if (string.IsNullOrEmpty (ToolExe))
				ToolExe = "emulator";

			var dirs = string.IsNullOrEmpty (ToolPath)
				? null
				: new [] { ToolPath };
			string filename;
			var path = Which.GetProgramLocation (ToolExe, out filename, dirs);
			if (path == null) {
				Log.LogError ($"Could not find `emulator`. Please set the `{nameof (StartAndroidEmulator)}.{nameof (ToolExe)}` property appropriately.");
				return null;
			}
			return path;
		}

		void Run (string emulator)
		{
			if (emulator == null)
				return;

			var port = string.IsNullOrEmpty (Port) ? "" : $"-port {Port}";
			var arguments = $"{Arguments ?? string.Empty} -verbose -no-hidpi-scaling -wipe-data -detect-image-hang -logcat-output \"{LogcatFile}\" -no-audio -no-snapshot -cache-size 512 -change-locale en-US -timezone \"Etc/UTC\" {port} -avd {ImageName}";
			Log.LogMessage ($"Tool {emulator} execution started with arguments: {arguments}");
			var psi = new ProcessStartInfo () {
				FileName                = emulator,
				Arguments               = arguments,
				UseShellExecute         = false,
				CreateNoWindow          = true,
				RedirectStandardOutput  = true,
				RedirectStandardError   = true,
				WindowStyle             = ProcessWindowStyle.Hidden,
			};

			Log.LogMessage (MessageImportance.Low, $"Environment variables being passed to the tool:");
			var p = new Process () {
				StartInfo = psi,
			};
			psi.EnvironmentVariables ["ANDROID_HOME"]       = AndroidSdkDirectory;
			psi.EnvironmentVariables ["ANDROID_PREFS_ROOT"] = AvdManagerHome;
			psi.EnvironmentVariables ["ANDROID_SDK_ROOT"]   = AndroidSdkDirectory;
			Log.LogMessage (MessageImportance.Low, $"\tANDROID_HOME=\"{psi.EnvironmentVariables ["ANDROID_HOME"]}\"");
			Log.LogMessage (MessageImportance.Low, $"\tANDROID_PREFS_ROOT=\"{psi.EnvironmentVariables ["ANDROID_PREFS_ROOT"]}\"");
			Log.LogMessage (MessageImportance.Low, $"\tANDROID_SDK_ROOT=\"{psi.EnvironmentVariables ["ANDROID_SDK_ROOT"]}\"");

			var sawError        = new AutoResetEvent (false);

			DataReceivedEventHandler output = null;
			output = (o, e) => {
				Log.LogMessage (MessageImportance.Low, $"[emulator stdout] {e.Data}");
				if (string.IsNullOrWhiteSpace (e.Data))
					return;
				if (e.Data.StartsWith ("Hax ram_size", StringComparison.Ordinal) &&
						e.Data.EndsWith (" 0x0", StringComparison.Ordinal)) {
					Log.LogError ("Emulator failed to start: ram_size is 0MB! Please re-install HAXM.");
					sawError.Set ();
				}
				if (e.Data.IndexOf ("ERROR:", StringComparison.Ordinal) >= 0) {
					Log.LogError ($"Emulator failed to start: {e.Data}");
					sawError.Set ();
				}
			};
			DataReceivedEventHandler error = null;
			error = (o, e) => {
				Log.LogMessage (MessageImportance.Low, $"[emulator stderr] {e.Data}");
				if (string.IsNullOrWhiteSpace (e.Data))
					return;
				if (e.Data.StartsWith ("Failed to sync", StringComparison.Ordinal) ||
						e.Data.Contains ("Internal error")) {
					Log.LogError ($"Emulator failed to start: {e.Data}");
					Log.LogError ($"Do you have another VM running on the machine? If so, please try exiting the VM and try again.");
					sawError.Set ();
				}
				if (e.Data.StartsWith ("Unknown hax vcpu return", StringComparison.Ordinal)) {
					Log.LogError ($"Emulator failed to start: `{e.Data}`. Please try again?");
					sawError.Set ();
				}
				// The following may not be fatal:
				// [emulator stderr] eglMakeCurrent failed in binding subwindow!
				if (e.Data.IndexOf ("ERROR:", StringComparison.Ordinal) >= 0 ||
						(e.Data.IndexOf (" failed ", StringComparison.Ordinal) >= 0 && e.Data.IndexOf ("eglMakeCurrent", StringComparison.Ordinal) == -1)) {
					Log.LogError ($"Emulator failed to start: {e.Data}");
					sawError.Set ();
				}
			};

			p.OutputDataReceived  += output;
			p.ErrorDataReceived   += error;

			p.Start ();
			p.BeginOutputReadLine ();
			p.BeginErrorReadLine ();

			const int Timeout = 20*1000;
			int i = WaitHandle.WaitAny (new[]{sawError}, millisecondsTimeout: Timeout);
			if (i == 0 || Log.HasLoggedErrors) {
				p.Kill ();
				return;
			}

			p.CancelOutputRead ();
			p.CancelErrorRead ();

			p.OutputDataReceived  -= output;
			p.ErrorDataReceived   -= error;

			p.OutputDataReceived  += WriteProcessOutputMessage;
			p.ErrorDataReceived   += WriteProcessErrorMessage;

			p.BeginOutputReadLine ();
			p.BeginErrorReadLine ();

			EmulatorProcessId = p.Id;
		}

		static void WriteProcessOutputMessage (object sender, DataReceivedEventArgs e)
		{
			Console.WriteLine (e.Data);
		}

		static void WriteProcessErrorMessage (object sender, DataReceivedEventArgs e)
		{
			Console.Error.WriteLine (e.Data);
		}
	}
}
