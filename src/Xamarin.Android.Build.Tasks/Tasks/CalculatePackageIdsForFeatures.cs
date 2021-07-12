using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.XPath;

using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;

using Xamarin.Build;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class CalculatePackageIdsForFeatures : AndroidTask
	{
		public override string TaskPrefix => "CPI";

		[Required]
		public ITaskItem[] FeatureProjects { get; set; }

		[Output]
		public ITaskItem[] Output { get; set; }

		public override bool RunTask ()
		{
			List<ITaskItem> output = new List<ITaskItem> ();
			byte packageId = 0x7c;
			foreach (var feature in FeatureProjects) {
				var item = new TaskItem (feature.ItemSpec);
				item.SetMetadata ("AdditionalProperties", $"FeaturePackageId=0x{packageId.ToString ("X")}");
				output.Add (item);
				packageId++;
			}
			Output = output.ToArray ();
			return !Log.HasLoggedErrors;
		}
	}
}
