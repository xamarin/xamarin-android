using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.APK
{
	/// <summary>
	///   Replace existing copies of the test on target device with a fresh copy.
	/// </summary>
	class Deploy : APKTestCommand
	{
		public Deploy ()
			: base ("Deploy", "Deploy APK/AAB test to device, replacing previous versions of it")
		{}

		protected override async Task<bool> Run (TestAPK test)
		{
			string apkName = Path.GetFileName (test.ApkPath);

			if (test.TestFlavor != APKTestFlavor.AndroidApplicationBundle) {
				var adb = new AdbRunner (Context, toolPath: Context.AdbPath) {
					AdbTarget = State!.AdbTarget,
				};

				Log.DebugLine ($"Uninstalling package {test.AndroidPackageName}");
				if (!await adb.UninstallAPK (test.AndroidPackageName)) {
					Log.DebugLine ($"Package {test.AndroidPackageName} uninstall either failed or the package wasn't installed.");
				}

				Log.InfoLine ($"Deploying package: ", apkName);
				return await adb.InstallAPK (test.ApkPath, traceAdb: true);
			}

			var bundleTool = new BundleToolRunner (Context, toolPath: Context.JavaPath) {
				AdbTarget = State!.AdbTarget,
			};

			Log.InfoLine ("Building APKs from bundle: ", apkName);
			if (!await bundleTool.BuildApks (test.ApkPath)) {
				Log.WarningLine ($"Failed to build APK packages from the {test.ApkPath} bundle");
				return false;
			}

			Log.InfoLine ("Installing APKs built from bundle: ", apkName);
			return await bundleTool.InstallApks (test.ApkPath);
		}
	}
}
