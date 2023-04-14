using System;
using System.Collections.Generic;

using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Framework;

namespace Xamarin.Android.Tasks
{
	public class DebugNativeCode : AndroidAsyncTask
	{
		public override string TaskPrefix => "DNC";

		[Required]
		public string AndroidNdkPath { get; set; }

		[Required]
		public string PackageName { get; set; }

		[Required]
		public string[] SupportedAbis { get; set; }

		[Required]
		public string AdbPath { get; set; }

		[Required]
		public string MainActivityName { get; set; }

		[Required]
		public string IntermediateOutputDir { get; set; }

		[Required]
		public ITaskItem[] NativeLibraries { get; set; }

		public string ActivityName { get; set; }
		public string TargetDeviceName { get; set; }

		public async override System.Threading.Tasks.Task RunTaskAsync ()
		{
			var nativeLibs = new Dictionary<string, List<string>> (StringComparer.OrdinalIgnoreCase);
			foreach (ITaskItem item in NativeLibraries) {
				string? abi = null;
				string? rid = item.GetMetadata ("RuntimeIdentifier");

				if (!String.IsNullOrEmpty (rid)) {
					abi = NdkHelper.RIDToABI (rid);
				}

				if (String.IsNullOrEmpty (abi)) {
					abi = item.GetMetadata ("abi");
				}

				if (String.IsNullOrEmpty (abi)) {
					Log.LogDebugMessage ($"Ignoring native library {item.ItemSpec} because it doesn't specify its ABI");
					continue;
				}

				if (!nativeLibs.TryGetValue (abi, out List<string> abiLibs)) {
					abiLibs = new List<string> ();
					nativeLibs.Add (abi, abiLibs);
				}
				abiLibs.Add (item.ItemSpec);
			}

			var prep = new NativeDebugPrep (Log);
			prep.Prepare (
				AdbPath,
				AndroidNdkPath,
				String.IsNullOrEmpty (ActivityName) ? MainActivityName : ActivityName,
				IntermediateOutputDir,
				PackageName,
				SupportedAbis,
				nativeLibs,
				TargetDeviceName
			);
		}
	}
}
