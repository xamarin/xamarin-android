using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	// We have a list of files, we want to get the
	// ones that actually exist on disk.
	public class GetAssetPacks : AndroidTask
	{
		public override string TaskPrefix => "GAP";

		[Required]
		public ITaskItem[] Assets { get; set; }

        public string[] MetadataToCopy { get; set; } = { "DeliveryType", "IsFeatureSplit"};

		[Output]
		public ITaskItem[] AssetPacks { get; set; }

		public override bool RunTask ()
		{
            Dictionary<string, ITaskItem> assetPacks = new Dictionary<string, ITaskItem> ();
			foreach (var asset in Assets) {
                var assetPack = asset.GetMetadata ("AssetPack");
                if (string.IsNullOrEmpty (assetPack))
                    continue;
                if (!assetPacks.ContainsKey (assetPack)) {
                    assetPacks[assetPack] = new TaskItem (assetPack);
                    assetPacks[assetPack].SetMetadata ("AssetPack", assetPack);
                }
                var item = assetPacks[assetPack];
                foreach (var metadata in MetadataToCopy)
                    if (string.IsNullOrEmpty (item.GetMetadata (metadata)))
                        item.SetMetadata (metadata, asset.GetMetadata (metadata));
            }

            AssetPacks = assetPacks.Values.ToArray ();

			return true;
		}
	}
}