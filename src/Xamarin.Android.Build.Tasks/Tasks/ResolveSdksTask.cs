//
// ResolveSdksTask.cs
//
// Author:
//       Michael Hutchinson <mhutchinson@novell.com>
//       Jonathan Pryor <jonp@xamarin.com>
//
// Copyright (c) 2010 Novell, Inc.
// Copyright (c) 2013 Xamarin Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.IO;
using System.Linq;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	/// <summary>
	/// ResolveSdks' job is to call RefreshAndroidSdk and setup static members of MonoAndroidHelper
	/// </summary>
	public class ResolveSdks : AndroidTask
	{
		public override string TaskPrefix => "RSD";

		public string [] ReferenceAssemblyPaths { get; set; }

		public string CommandLineToolsVersion { get; set; }

		[Required]
		public string MinimumSupportedJavaVersion   { get; set; }

		[Required]
		public string LatestSupportedJavaVersion    { get; set; }

		[Output]
		public string CommandLineToolsPath { get; set; }

		[Output]
		public string AndroidNdkPath { get; set; }

		[Output]
		public string AndroidSdkPath { get; set; }

		[Output]
		public string JavaSdkPath { get; set; }

		[Output]
		public string MonoAndroidToolsPath { get; set; }

		[Output]
		public string MonoAndroidBinPath { get; set; }

		[Output]
		public string MonoAndroidLibPath { get; set; }

		[Output]
		public string AndroidBinUtilsPath { get; set; }

		public override bool RunTask ()
		{
			// OS X:    $prefix/lib/xamarin.android/xbuild/Xamarin/Android
			// Windows: %ProgramFiles(x86)%\MSBuild\Xamarin\Android
			if (string.IsNullOrEmpty (MonoAndroidToolsPath)) {
				MonoAndroidToolsPath  = Path.GetDirectoryName (typeof (ResolveSdks).Assembly.Location);
			}
			MonoAndroidBinPath  = MonoAndroidHelper.GetOSBinPath () + Path.DirectorySeparatorChar;
			MonoAndroidLibPath  = MonoAndroidHelper.GetOSLibPath () + Path.DirectorySeparatorChar;
			AndroidBinUtilsPath = MonoAndroidBinPath + "binutils" + Path.DirectorySeparatorChar;

			var minVersion      = Version.Parse (MinimumSupportedJavaVersion);
			var maxVersion      = Version.Parse (LatestSupportedJavaVersion);

			JavaSdkPath         = MonoAndroidHelper.GetJdkInfo (this.CreateTaskLogger (), JavaSdkPath, minVersion, maxVersion)?.HomePath;

			MonoAndroidHelper.RefreshSupportedVersions (ReferenceAssemblyPaths);

			try {
				MonoAndroidHelper.RefreshAndroidSdk (AndroidSdkPath, AndroidNdkPath, JavaSdkPath, Log);
			}
			catch (InvalidOperationException e) {
				if (e.Message.Contains (" Android ")) {
					Log.LogCodedError ("XA5300", Properties.Resources.XA5300_Android_SDK);
				}
				if (e.Message.Contains (" Java ")) {
					Log.LogCodedError ("XA5300", Properties.Resources.XA5300_Java_SDK);
				}
				return false;
			}

			AndroidNdkPath = MonoAndroidHelper.AndroidSdk.AndroidNdkPath;
			AndroidSdkPath = MonoAndroidHelper.AndroidSdk.AndroidSdkPath;
			JavaSdkPath    = MonoAndroidHelper.AndroidSdk.JavaSdkPath;

			CommandLineToolsPath    = MonoAndroidHelper.AndroidSdk.GetCommandLineToolsPaths (CommandLineToolsVersion)
				.FirstOrDefault () ??
				Path.Combine (AndroidSdkPath, "tools");

			if (string.IsNullOrEmpty (AndroidSdkPath)) {
				Log.LogCodedError ("XA5300", Properties.Resources.XA5300_Android_SDK);
				return false;
			}
			if (string.IsNullOrEmpty (JavaSdkPath)) {
				Log.LogCodedError ("XA5300", Properties.Resources.XA5300_Java_SDK);
				return false;
			}

			MonoAndroidHelper.TargetFrameworkDirectories = ReferenceAssemblyPaths;

			Log.LogDebugMessage ($"{nameof (ResolveSdks)} Outputs:");
			Log.LogDebugMessage ($"  {nameof (AndroidSdkPath)}: {AndroidSdkPath}");
			Log.LogDebugMessage ($"  {nameof (AndroidNdkPath)}: {AndroidNdkPath}");
			Log.LogDebugMessage ($"  {nameof (JavaSdkPath)}: {JavaSdkPath}");
			Log.LogDebugMessage ($"  {nameof (MonoAndroidBinPath)}: {MonoAndroidBinPath}");
			Log.LogDebugMessage ($"  {nameof (MonoAndroidToolsPath)}: {MonoAndroidToolsPath}");
			Log.LogDebugMessage ($"  {nameof (AndroidBinUtilsPath)}: {AndroidBinUtilsPath}");

			//note: this task does not error out if it doesn't find all things. that's the job of the targets
			return !Log.HasLoggedErrors;
		}
	}
}
