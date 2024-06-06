using System;
using System.IO;
using System.Collections.Generic;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks;

public class GetNativeRuntimeComponents : AndroidTask
{
	public override string TaskPrefix => "GNRC";

	public ITaskItem[] MonoComponents { get; set; }

	[Required]
	public ITaskItem[] ResolvedNativeArchives { get; set; }

	[Output]
	public ITaskItem[] NativeArchives { get; set; }

	public override bool RunTask ()
	{
		var components = new NativeRuntimeComponents (MonoComponents);
		var archives = new List<ITaskItem> ();
		foreach (NativeRuntimeComponents.Archive archiveItem in components.KnownArchives) {
			if (!archiveItem.Include) {
				continue;
			}
			MakeItems (archiveItem, archives);
		}

		NativeArchives = archives.ToArray ();
		return !Log.HasLoggedErrors;
	}

	void MakeItems (NativeRuntimeComponents.Archive archive, List<ITaskItem> archives)
	{
		foreach (ITaskItem resolvedArchive in ResolvedNativeArchives) {
			string fileName = Path.GetFileName (resolvedArchive.ItemSpec);
			if (String.Compare (fileName, archive.Name, StringComparison.OrdinalIgnoreCase) == 0) {
				archives.Add (DoMakeItem (resolvedArchive));
			}
		}

		ITaskItem DoMakeItem (ITaskItem resolved)
		{
			var ret = new TaskItem (resolved.ItemSpec);
			string abi = MonoAndroidHelper.RidToAbi (resolved.GetRequiredMetadata ("_ResolvedNativeArchive", "RuntimeIdentifier", Log));
			ret.SetMetadata ("Abi", abi);

			return ret;
		}
	}
}
