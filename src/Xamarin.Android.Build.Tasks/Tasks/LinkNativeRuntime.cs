using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Xamarin.Android.Tools;
using Xamarin.Build;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks;

public class LinkNativeRuntime : AndroidAsyncTask
{
	public override string TaskPrefix => "LNR";

	public ITaskItem[] MonoComponents { get; set; }

	[Required]
	public string AndroidBinUtilsDirectory { get; set; }

	[Required]
	public ITaskItem[] NativeArchives { get; set; }

	[Required]
	public ITaskItem[] NativeObjectFiles { get; set; }

	[Required]
	public ITaskItem[] OutputRuntimes { get; set; }

	[Required]
	public ITaskItem[] SupportedAbis { get; set; }

	public override System.Threading.Tasks.Task RunTaskAsync ()
	{
		return this.WhenAll (SupportedAbis, LinkRuntime);
	}

	void LinkRuntime (ITaskItem abiItem)
	{
		string abi = abiItem.ItemSpec;
		Log.LogDebugMessage ($"LinkRuntime ({abi})");
		var linker = new NativeLinker (Log, abi);
		linker.Link (
			GetFirstAbiItem (OutputRuntimes, "_UnifiedNativeRuntime", abi),
			GetAbiItems (NativeObjectFiles, "_NativeAssemblyTarget", abi),
			GetAbiItems (NativeArchives, "_SelectedNativeArchive", abi)
		);
	}

	List<ITaskItem> GetAbiItems (ITaskItem[] source, string itemName, string abi)
	{
		var ret = new List<ITaskItem> ();

		foreach (ITaskItem item in source) {
			if (AbiMatches (abi, item, itemName)) {
				ret.Add (item);
			}
		}

		return ret;
	}

	ITaskItem GetFirstAbiItem (ITaskItem[] source, string itemName, string abi)
	{
		foreach (ITaskItem item in source) {
			if (AbiMatches (abi, item, itemName)) {
				return item;
			}
		}

		throw new InvalidOperationException ($"Internal error: item '{itemName}' for ABI '{abi}' not found");
	}

	bool AbiMatches (string abi, ITaskItem item, string itemName)
	{
		return String.Compare (abi, item.GetRequiredMetadata (itemName, "Abi", Log), StringComparison.OrdinalIgnoreCase) == 0;
	}
}
