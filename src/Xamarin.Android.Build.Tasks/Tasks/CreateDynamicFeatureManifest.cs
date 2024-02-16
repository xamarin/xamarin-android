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
	public class CreateDynamicFeatureManifest : AndroidTask
	{
		readonly XNamespace androidNS = "http://schemas.android.com/apk/res/android";
		readonly XNamespace distNS = "http://schemas.android.com/apk/distribution";
		readonly XNamespace toolsNS = "http://schemas.android.com/tools";

		public override string TaskPrefix => "CDFM";

		[Required]
		public string FeatureSplitName { get; set; }

		[Required]
		public string FeatureDeliveryType { get; set; }

		[Required]
		public string FeatureType { get; set; }

		[Required]
		public string PackageName { get; set; }

		[Required]
		public ITaskItem OutputFile { get; set; }

		public string FeatureTitleResource { get; set; }

		public string MinSdkVersion { get; set; }

		public string TargetSdkVersion { get; set; }

		public bool IsFeatureSplit { get; set; } = false;
		public bool IsInstant { get; set; } = false;
		public bool HasCode { get; set; } = false;

		public override bool RunTask ()
		{
			XDocument doc = new XDocument ();
			switch (FeatureType) {
				case "AssetPack":
					GenerateAssetPackManifest (doc);
					break;
				default:
					GenerateFeatureManifest (doc);
					break;
			}
			doc.Save (OutputFile.ItemSpec);
			return !Log.HasLoggedErrors;
		}

		void GenerateFeatureManifest (XDocument doc) {
			XAttribute featureTitleResource = null;
			if (!string.IsNullOrEmpty (FeatureTitleResource))
				featureTitleResource = new XAttribute (distNS + "title", FeatureTitleResource);
			XElement usesSdk = new XElement ("uses-sdk");
			if (!string.IsNullOrEmpty (MinSdkVersion))
				usesSdk.Add (new XAttribute (androidNS + "minSdkVersion", MinSdkVersion));
			if (!string.IsNullOrEmpty (MinSdkVersion))
				usesSdk.Add (new XAttribute (androidNS + "targetSdkVersion", TargetSdkVersion));
			doc.Add (new XElement ("manifest",
					new XAttribute(XNamespace.Xmlns + "android", androidNS),
					new XAttribute(XNamespace.Xmlns + "tools", toolsNS),
					new XAttribute(XNamespace.Xmlns + "dist", distNS),
					new XAttribute (androidNS + "versionCode", "1"),
					new XAttribute (androidNS + "versionName", "1.0"),
					new XAttribute ("package", PackageName),
					new XAttribute ("featureSplit", FeatureSplitName),
					new XAttribute (androidNS + "isFeatureSplit", IsFeatureSplit),
					new XElement (distNS + "module",
						featureTitleResource,
						new XAttribute (distNS + "instant", IsInstant),
						new XElement (distNS + "delivery",
							GetDistribution ()
						),
						new XElement (distNS + "fusing",
							new XAttribute (distNS + "include", true)
						)
					),
					usesSdk,
					new XElement ("application",
						new XAttribute (androidNS + "hasCode", HasCode),
						new XAttribute (toolsNS + "replace", "android:hasCode")
					)
				)
			);
		}

		void GenerateAssetPackManifest (XDocument doc) {
			doc.Add (new XElement ("manifest",
					new XAttribute (XNamespace.Xmlns + "android", androidNS),
					new XAttribute (XNamespace.Xmlns + "tools", toolsNS),
					new XAttribute (XNamespace.Xmlns + "dist", distNS),
					new XAttribute ("package", PackageName),
					new XAttribute ("split", FeatureSplitName),
					new XElement (distNS + "module",
						new XAttribute (distNS + "type", "asset-pack"),
						new XElement (distNS + "delivery",
							GetDistribution ()
						),
						new XElement (distNS + "fusing",
							new XAttribute (distNS + "include", true)
						)
					)
				)
			);
		}

		XElement GetDistribution ()
		{
			XElement distribution;
			switch (FeatureDeliveryType)
			{
				case "OnDemand":
					distribution = new XElement (distNS + "on-demand");
					break;
				case "FastFollow":
					distribution = new XElement (distNS + "fast-follow");
					break;
				case "InstallTime":
				default:
					distribution = new XElement (distNS + "install-time");
					break;
			}
			return distribution;
		}
	}
}
