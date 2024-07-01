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

	[Output]
	public ITaskItem[] RequiredLibraries { get; set; }

	public override bool RunTask ()
	{
		var components = new NativeRuntimeComponents (MonoComponents);
		var uniqueAbis = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		var archives = new List<ITaskItem> ();
		foreach (NativeRuntimeComponents.Archive archiveItem in components.KnownArchives) {
			if (!archiveItem.Include) {
				continue;
			}
			MakeItems (archiveItem, archives, uniqueAbis);
		}
		NativeArchives = archives.ToArray ();

		var libraries = new List<ITaskItem> ();
		foreach (string lib in components.NativeLibraries) {
			MakeLibraryItems (lib, libraries, uniqueAbis);
		}
		RequiredLibraries = libraries.ToArray ();

		return !Log.HasLoggedErrors;
	}

	void MakeLibraryItems (string libName, List<ITaskItem> libraries, HashSet<string> uniqueAbis)
	{
		foreach (string abi in uniqueAbis) {
			var item = new TaskItem (libName);
			item.SetMetadata (KnownMetadata.Abi, abi);
			libraries.Add (item);
		}
	}

	void MakeItems (NativeRuntimeComponents.Archive archive, List<ITaskItem> archives, HashSet<string> uniqueAbis)
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
			uniqueAbis.Add (abi);
			ret.SetMetadata (KnownMetadata.Abi, abi);
			ret.SetMetadata (KnownMetadata.LinkWholeArchive, archive.WholeArchive.ToString ());

			return ret;
		}
	}
}
