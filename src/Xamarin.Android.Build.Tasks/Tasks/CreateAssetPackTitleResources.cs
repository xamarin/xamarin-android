using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;

using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class CreateAssetPackTitleResources : AndroidTask
	{
        public override string TaskPrefix => "CAPTR";

        [Required]
        public ITaskItem[] Assets { get; set; }
        [Required]
		public ITaskItem OutputFile { get; set; }
        public override bool RunTask ()
		{
			XDocument doc = new XDocument ();
            XElement resources = new XElement ("resources");
            foreach (var asset in Assets) {
                var pack = asset.GetMetadata ("AssetPack");
                var title = asset.GetMetadata ("TitleResource");
                if (string.IsNullOrEmpty (pack))
                    continue;
                resources.Add (new XElement ("string",
                    new XAttribute ("name", title),
                    pack
                ));
            }
            doc.Add (resources);
			doc.Save (OutputFile.ItemSpec);
			return !Log.HasLoggedErrors;
		}
    }
}