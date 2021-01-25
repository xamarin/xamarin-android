//
// Code ported from build-tools/Xamarin.Android.Tools.BootstrapTasks/Xamarin.Android.Tools.BootstrapTasks/CreateAndroidEmulator.cs
//
using System;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class CreateAndroidEmulator : AppObject
	{
		public string SdkVersion          { get; set; } = String.Empty;
		public string AndroidAbi          { get; set; } = String.Empty;
		public string AndroidSdkHome      { get; set; } = String.Empty;
		public string JavaSdkHome         { get; set; } = String.Empty;
		public string TargetId            { get; set; } = String.Empty;
		public string ImageName           { get; set; } = String.Empty;
		public string DataPartitionSizeMB { get; set; } = String.Empty;
		public string RamSizeMB           { get; set; } = String.Empty;

		public async Task<bool> Run ()
		{
			EnsurePropertyValue (nameof (AndroidAbi), AndroidAbi);
			EnsurePropertyValue (nameof (DataPartitionSizeMB), DataPartitionSizeMB);
			EnsurePropertyValue (nameof (ImageName), ImageName);
			EnsurePropertyValue (nameof (RamSizeMB), RamSizeMB);
			EnsurePropertyValue (nameof (SdkVersion), SdkVersion);

			if (TargetId.Length == 0 && SdkVersion.Length > 0) {
				TargetId = $"system-images;android-{SdkVersion};default;{AndroidAbi}";
			}

			Log.DebugLine ($"Task {nameof (CreateAndroidEmulator)}");
			Log.DebugLine ($"  {nameof (AndroidAbi)}: {AndroidAbi}");
			Log.DebugLine ($"  {nameof (AndroidSdkHome)}: {AndroidSdkHome}");
			Log.DebugLine ($"  {nameof (JavaSdkHome)}: {JavaSdkHome}");
			Log.DebugLine ($"  {nameof (ImageName)}: {ImageName}");
			Log.DebugLine ($"  {nameof (SdkVersion)}: {SdkVersion}");
			Log.DebugLine ($"  {nameof (TargetId)}: {TargetId}");

			var avdmanager = new AvdmanagerRunner (Context, toolPath: Context.AvdManagerPath);
			if (!await avdmanager.Create (AndroidAbi, ImageName, TargetId)) {
				Log.ErrorLine ("AVD manager failed");
				return false;
			}

			string configPath = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.Personal), ".android", "avd", $"{ImageName}.avd", "config.ini");
			if (!File.Exists (configPath)) {
				Log.WarningLine ($"Config file for AVD '{ImageName}' not found at {configPath}");
				Log.WarningLine ($"AVD '{ImageName}' will use default emulator settings (memory and data partition size)");
				return false;
			}

			ulong diskSize;
			bool haveErrors = false;
			if (!UInt64.TryParse (DataPartitionSizeMB, out diskSize)) {
				Log.ErrorLine ($"Invalid data partition size '{DataPartitionSizeMB}' - must be a positive integer value expressing size in megabytes");
				haveErrors = true;
			}

			ulong ramSize;
			if (!UInt64.TryParse (RamSizeMB, out ramSize)) {
				Log.ErrorLine ($"Invalid RAM size '{RamSizeMB}' - must be a positive integer value expressing size in megabytes");
				haveErrors = true;
			}

			if (haveErrors) {
				return false;
			}

			File.AppendAllLines (configPath, new string[] {
				$"disk.dataPartition.size={diskSize}M",
				$"hw.ramSize={ramSize}"
			});

			return true;
		}
	}
}
