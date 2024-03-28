using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	/// <summary>
	/// Processes .so files coming from @(ResolvedFileToPublish).
	/// * Checks if ABI is valid
	/// * Fixes up libmonodroid.so based on $(AndroidIncludeDebugSymbols)
	/// </summary>
	public class ProcessNativeLibraries : AndroidTask
	{
		const string MonoComponentPrefix = "libmono-component-";

		public override string TaskPrefix => "PRNL";

		static readonly HashSet<string> DebugNativeLibraries = new HashSet<string> (StringComparer.OrdinalIgnoreCase) {
			"libxamarin-debug-app-helper",
		};

		/// <summary>
		/// Assumed to be .so files only
		/// </summary>
		public ITaskItem [] InputLibraries { get; set; }
		public ITaskItem [] Components { get; set; }
		public string [] ExcludedLibraries { get; set; }

		public bool IncludeDebugSymbols { get; set; }
		public bool IncludeNativeTracing { get; set; }

		[Output]
		public ITaskItem [] OutputLibraries { get; set; }

		public override bool RunTask ()
		{
			if (InputLibraries == null || InputLibraries.Length == 0)
				return true;

			var wantedComponents = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
			if (Components != null && Components.Length > 0) {
				foreach (ITaskItem item in Components) { ;
					wantedComponents.Add ($"{MonoComponentPrefix}{item.ItemSpec}");
				}
			}

			var output = new List<ITaskItem> (InputLibraries.Length);

			foreach (var library in InputLibraries) {
				var abi = AndroidRidAbiHelper.GetNativeLibraryAbi (library);
				if (string.IsNullOrEmpty (abi)) {
					var packageId = library.GetMetadata ("NuGetPackageId");
					if (!string.IsNullOrEmpty (packageId)) {
						Log.LogCodedWarning ("XA4301", library.ItemSpec, 0, Properties.Resources.XA4301_ABI_NuGet, library.ItemSpec, packageId);
					} else {
						Log.LogCodedWarning ("XA4301", library.ItemSpec, 0, Properties.Resources.XA4301_ABI, library.ItemSpec);
					}
					continue;
				}
				// Both libmono-android.debug.so and libmono-android.release.so are in InputLibraries.
				// Use IncludeDebugSymbols to determine which one to include.
				// We may eventually have files such as `libmono-android-checked+asan.release.so` as well.
				var fileName = Path.GetFileNameWithoutExtension (library.ItemSpec);
				if (fileName.StartsWith ("libmono-android", StringComparison.Ordinal)) {
					if (fileName.EndsWith (".debug", StringComparison.Ordinal)) {
						if (!IncludeDebugSymbols)
							continue;
						library.SetMetadata ("ArchiveFileName", "libmonodroid.so");
					} else if (fileName.EndsWith (".release", StringComparison.Ordinal)) {
						if (IncludeDebugSymbols)
							continue;
						library.SetMetadata ("ArchiveFileName", "libmonodroid.so");
					}
				} else if (DebugNativeLibraries.Contains (fileName)) {
					if (!IncludeDebugSymbols) {
						Log.LogDebugMessage ($"Excluding '{library.ItemSpec}' for release builds.");
						continue;
					}
				} else if (fileName.StartsWith (MonoComponentPrefix, StringComparison.OrdinalIgnoreCase)) {
					if (!wantedComponents.Contains (fileName)) {
						continue;
					}
				} else if (!IncludeNativeTracing && String.Compare ("libxamarin-native-tracing", fileName, StringComparison.Ordinal) == 0) {
					Log.LogDebugMessage ($"Excluding '{library.ItemSpec}' because native stack traces support is disabled");
					continue;
				} else if (ExcludedLibraries != null && ExcludedLibraries.Contains (fileName, StringComparer.OrdinalIgnoreCase)) {
					Log.LogDebugMessage ($"Excluding '{library.ItemSpec}'");
					continue;
				}

				output.Add (library);
			}

			OutputLibraries = output.ToArray ();

			return !Log.HasLoggedErrors;
		}
	}
}
