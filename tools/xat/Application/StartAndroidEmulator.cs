//
// Code ported from build-tools/Xamarin.Android.Tools.BootstrapTasks/Xamarin.Android.Tools.BootstrapTasks/StartAndroidEmulator.cs
//
using System;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class StartAndroidEmulator : AppObject
	{
		// Output properties
		public string AdbTarget         { get; private set; } = String.Empty;
		public int    EmulatorProcessId { get; private set; } = -1;

		// Input properties
		public string AndroidSdkHome    { get; set; } = String.Empty;
		public ushort Port              { get; set; } = 0;
		public string ImageName         { get; set; } = String.Empty;

		public async Task<bool> Run ()
		{
			Log.DebugLine ($"Task {nameof (StartAndroidEmulator)}");
			Log.DebugLine ($"  {nameof (AndroidSdkHome)}: {AndroidSdkHome}");
			Log.DebugLine ($"  {nameof (ImageName)}: {ImageName}");
			Log.DebugLine ($"  {nameof (Port)}: {Port}");

			if (Port > 0) {
				AdbTarget   = $"emulator-{Port}";
			}

			EmulatorProcessId = -1;

			var emulator = new EmulatorRunner (Context.Instance, toolPath: Context.EmulatorPath);
			int pid = await emulator.Start (ImageName, AndroidSdkHome, Port);
			if (pid == -1) {
				return false;
			}

			EmulatorProcessId = pid;
			return true;
		}
	}
}
