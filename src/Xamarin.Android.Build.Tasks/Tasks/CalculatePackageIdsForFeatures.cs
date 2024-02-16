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
			int packageId = 0x7f; // default package Id for the main app.
			// we now decrement the package id and will use that for
			// each "feature". This is so resources do not clash.
			foreach (var feature in FeatureProjects) {
				packageId--;
				var item = new TaskItem (feature.ItemSpec);
				bool isFeature = feature.GetMetadata("FeatureType") == "Feature";
				string apkFileIntermediate = feature.GetMetadata ("_BaseZipIntermediate");
				item.SetMetadata ("AdditionalProperties", $"AndroidApplication={isFeature.ToString ()};_BaseZipIntermediate={apkFileIntermediate};EmbedAssembliesIntoApk=true;FeaturePackageId=0x{packageId.ToString ("X")}");
				output.Add (item);
			}
			Output = output.ToArray ();
			return !Log.HasLoggedErrors;
		}
	}
}
