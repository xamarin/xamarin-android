using System;
using System.Collections.Generic;

namespace Xamarin.Android.Prepare
{
	class BuildAndroidPlatforms
	{
		public const string AndroidNdkVersion = "21";
		public const string AndroidNdkPkgRevision = "21.0.6113669";

		public static readonly List<AndroidPlatform> AllPlatforms = new List<AndroidPlatform> {
			new AndroidPlatform (apiName: "",                       apiLevel: 1,  platformID: "1"),
			new AndroidPlatform (apiName: "",                       apiLevel: 2,  platformID: "2"),
			new AndroidPlatform (apiName: "",                       apiLevel: 3,  platformID: "3"),
			new AndroidPlatform (apiName: "Donut",                  apiLevel: 4,  platformID: "4",   include: "v1.6"),
			new AndroidPlatform (apiName: "Eclair",                 apiLevel: 5,  platformID: "5",   include: "v2.0"),
			new AndroidPlatform (apiName: "Eclair",                 apiLevel: 6,  platformID: "6",   include: "v2.0.1"),
			new AndroidPlatform (apiName: "Eclair",                 apiLevel: 7,  platformID: "7",   include: "v2.1"),
			new AndroidPlatform (apiName: "Froyo",                  apiLevel: 8,  platformID: "8",   include: "v2.2"),
			new AndroidPlatform (apiName: "",                       apiLevel: 9,  platformID: "9"),
			new AndroidPlatform (apiName: "Gingerbread",            apiLevel: 10, platformID: "10",  include: "v2.3"),
			new AndroidPlatform (apiName: "Honeycomb",              apiLevel: 11, platformID: "11",  include: "v3.0"),
			new AndroidPlatform (apiName: "Honeycomb",              apiLevel: 12, platformID: "12",  include: "v3.1"),
			new AndroidPlatform (apiName: "Honeycomb",              apiLevel: 13, platformID: "13",  include: "v3.2"),
			new AndroidPlatform (apiName: "Ice Cream Sandwich",     apiLevel: 14, platformID: "14",  include: "v4.0"),
			new AndroidPlatform (apiName: "Ice Cream Sandwich",     apiLevel: 15, platformID: "15",  include: "v4.0.3"),
			new AndroidPlatform (apiName: "Jelly Bean",             apiLevel: 16, platformID: "16",  include: "v4.1"),
			new AndroidPlatform (apiName: "Jelly Bean",             apiLevel: 17, platformID: "17",  include: "v4.2"),
			new AndroidPlatform (apiName: "Jelly Bean",             apiLevel: 18, platformID: "18",  include: "v4.3"),
			new AndroidPlatform (apiName: "Kit Kat",                apiLevel: 19, platformID: "19",  include: "v4.4",    framework: "v4.4"),
			new AndroidPlatform (apiName: "Kit Kat + Wear support", apiLevel: 20, platformID: "20",  include: "v4.4.87", framework: "v4.4.87"),
			new AndroidPlatform (apiName: "Lollipop",               apiLevel: 21, platformID: "21",  include: "v5.0",    framework: "v5.0"),
			new AndroidPlatform (apiName: "Lollipop",               apiLevel: 22, platformID: "22",  include: "v5.1",    framework: "v5.1"),
			new AndroidPlatform (apiName: "Marshmallow",            apiLevel: 23, platformID: "23",  include: "v6.0",    framework: "v6.0"),
			new AndroidPlatform (apiName: "Nougat",                 apiLevel: 24, platformID: "24",  include: "v7.0",    framework: "v7.0"),
			new AndroidPlatform (apiName: "Nougat",                 apiLevel: 25, platformID: "25",  include: "v7.1",    framework: "v7.1"),
			new AndroidPlatform (apiName: "Oreo",                   apiLevel: 26, platformID: "26",  include: "v8.0",    framework: "v8.0"),
			new AndroidPlatform (apiName: "Oreo",                   apiLevel: 27, platformID: "27",  include: "v8.1",    framework: "v8.1"),
			new AndroidPlatform (apiName: "Pie",                    apiLevel: 28, platformID: "28",  include: "v9.0",    framework: "v9.0"),
			new AndroidPlatform (apiName: "",                       apiLevel: 29, platformID: "29",  include: "v10.0",   framework: "v10.0"),
		};

		public static readonly Dictionary<string, uint> NdkMinimumAPI = new Dictionary<string, uint> {
			{ AbiNames.TargetJit.AndroidArmV7a, 16 },
			{ AbiNames.TargetJit.AndroidArmV8a, 21 },
			{ AbiNames.TargetJit.AndroidX86,    16 },
			{ AbiNames.TargetJit.AndroidX86_64, 21 },
		};
	}
}
