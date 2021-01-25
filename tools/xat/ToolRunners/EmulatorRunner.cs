using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class EmulatorRunner : ToolRunner
	{
		sealed class WatchForErrors : ProcessStandardStreamWrapper
		{
			Action callback;

			Log Log => Log.Instance;

			public WatchForErrors (Action errorCallback)
			{
				callback = errorCallback;
			}

			bool IsFalseNegativeError (string message)
			{
				// False negatives here:
				//
				//  emulator: ERROR: VkCommonOperations.cpp:496: Failed to create Vulkan instance.
				//  [45219:45436:1116/142133.147236:ERROR:nss_util.cc(748)] After loading Root Certs, loaded==false: NSS error code: -8018
				//

				if (message.IndexOf ("Vulkan instance", StringComparison.Ordinal) <= 0 ||
				    message.IndexOf ("Root Certs", StringComparison.Ordinal) <= 0) {
					return true;
				}

				return false;
			}

			protected override string PreprocessMessage (string message, ref bool writeLine, out bool ignoreLine)
			{
				ignoreLine = false;

				if (String.IsNullOrWhiteSpace (message)) {
					return message;
				}

				bool haveError = true;
				if (message.StartsWith ("Hax ram_size", StringComparison.Ordinal) && message.EndsWith (" 0x0", StringComparison.Ordinal)) {
					Log.ErrorLine ("Emulator failed to start: ram_size is 0MB! Please re-install HAXM.");
				} else if (message.IndexOf ("ERROR:", StringComparison.Ordinal) >= 0 && !IsFalseNegativeError (message)) {
					Log.ErrorLine ($"Emulator failed to start: {message}");
				} else if (message.StartsWith ("Failed to sync", StringComparison.Ordinal) || message.Contains ("Internal error")) {
					Log.ErrorLine ($"Emulator failed to start: {message}");
					Log.ErrorLine ($"Do you have another VM running on the machine? If so, please try exiting the VM and try again.");
				} else if (message.StartsWith ("Unknown hax vcpu return", StringComparison.Ordinal)) {
					Log.ErrorLine ($"Emulator failed to start: `{message}`. Please try again?");
				} else if (message.IndexOf (" failed ", StringComparison.Ordinal) >= 0) {
					Log.ErrorLine ($"Emulator failed to start: {message}");
				} else if (message.IndexOf ("PANIC:", StringComparison.Ordinal) >= 0) {
					Log.ErrorLine ($"Emulator panicked: {message}");
				} else {
					haveError = false;
				}

				if (haveError) {
					callback ();
				}

				return message;
			}
		}

		protected override string DefaultToolExecutableName => "emulator";
		protected override string ToolName                  => "Emulator";

		public EmulatorRunner (Context context, Log? log = null, string? toolPath = null)
			: base (context, log, toolPath)
		{
			ProcessTimeout = TimeSpan.FromSeconds (20);
			EchoStandardOutput = true;
			EchoStandardError = true;
		}

		public async Task<int> Start (string avdName, string androidSdkHome = "", ushort emulatorPort = Configurables.Defaults.AdbEmulatorPort, uint cacheSize = 512, bool verbose = true)
		{
			EnsureParameterValue (nameof (avdName), avdName);

			ProcessRunner runner = CreateProcessRunner ();
			if (verbose) {
				runner.AddArgument ("-verbose");
			}

			if (cacheSize > 0) {
				runner
					.AddArgument ("-cache-size")
					.AddArgument (cacheSize.ToString ());
			}

			if (emulatorPort > 0) {
				runner
					.AddArgument ("-port")
					.AddArgument (emulatorPort.ToString ());
			}

			runner
				.AddArgument ("-avd")
				.AddQuotedArgument ($"{avdName}");

			if (androidSdkHome.Length > 0) {
				runner.Environment ["ANDROID_HOME"] = androidSdkHome;
			}

			int processId = -1;
			bool success = await RunTool (() => {
				bool gotError = false;
				ProcessStandardStreamWrapper errorHandler = new WatchForErrors (() => gotError = true);
				runner.StandardOutputEchoWrapper = errorHandler;
				runner.StandardErrorEchoWrapper = errorHandler;

				bool started = runner.Run (fireAndForget: true);
				if (started) {
					if (runner.Process == null) {
						Log.WarningLine ("Emulator was started but no process instance found?");
					} else if (!gotError) {
						processId = runner.Process.Id;
						Log.DebugLine ($"Emulator PID: {processId}");
					}
				} else {
					Log.ErrorLine ("Emulator could not be started");
				}

				return started;
			});

			return processId;
		}
	}
}
