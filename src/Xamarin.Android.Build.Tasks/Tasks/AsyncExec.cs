#nullable enable
using System.Diagnostics;
using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class AsyncExec : Exec
	{
		[Output]
		public int ProcessId { get; set; }

		protected override int ExecuteTool (string pathToTool, string responseFileCommands, string commandLineCommands)
		{
			var startInfo = new ProcessStartInfo (pathToTool, commandLineCommands) {
				WindowStyle = ProcessWindowStyle.Hidden,
				CreateNoWindow = true,
				UseShellExecute = true,
			};
			var workingDirectory = GetWorkingDirectory ();
			if (!string.IsNullOrEmpty (workingDirectory)) {
				startInfo.WorkingDirectory = workingDirectory;
			}
			if (EnvironmentVariables != null) {
				foreach (var text in EnvironmentVariables) {
					var index = text.IndexOf ('=');
					if (index != -1) {
						var key = text.Substring (0, index);
						var value = text.Substring (index + 1);
						startInfo.EnvironmentVariables [key] = value;
					} else {
						Log.LogDebugMessage ($"Skipping value in EnvironmentVariables: {text}");
					}
				}
			}
			using var process = new Process {
				StartInfo = startInfo,
			};
			process.Start ();
			ProcessId = process.Id;
			return 0;
		}
	}
}
