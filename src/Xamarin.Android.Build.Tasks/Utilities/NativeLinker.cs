using System;
using System.Collections.Generic;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks;

class NativeLinker
{
	readonly TaskLoggingHelper log;
	readonly string abi;

	public NativeLinker (TaskLoggingHelper log, string abi)
	{
		this.log = log;
		this.abi = abi;
	}

	public void Link (ITaskItem outputLibraryPath, List<ITaskItem> objectFiles, List<ITaskItem> archives)
	{
		log.LogDebugMessage ($"Linking: {outputLibraryPath}");
		EnsureCorrectAbi (outputLibraryPath);
		EnsureCorrectAbi (objectFiles);
		EnsureCorrectAbi (archives);
	}

	void EnsureCorrectAbi (ITaskItem item)
	{
		// The exception is just a precaution, since the items passed to us should have already been checked
		string itemAbi = item.GetMetadata ("Abi") ?? throw new InvalidOperationException ($"Internal error: 'Abi' metadata not found in item '{item}'");
		if (String.Compare (abi, itemAbi, StringComparison.OrdinalIgnoreCase) == 0) {
			return;
		}

		throw new InvalidOperationException ($"Internal error: '{item}' ABI ('{itemAbi}') doesn't have the expected value '{abi}'");
	}

	void EnsureCorrectAbi (List<ITaskItem> items)
	{
		foreach (ITaskItem item in items) {
			EnsureCorrectAbi (item);
		}
	}
}
