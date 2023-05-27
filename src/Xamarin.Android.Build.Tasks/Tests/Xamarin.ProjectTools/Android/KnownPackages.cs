using System;

namespace Xamarin.ProjectTools
{
	public static class KnownPackages
	{
		public static Package AndroidSupportV4_27_0_2_1 = new Package () {
			Id = "Xamarin.Android.Support.v4",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.v4") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.v4.27.0.2.1\\lib\\MonoAndroid70\\Xamarin.Android.Support.v4.dll" }
			}
		};
		public static Package AndroidWear_2_2_0 = new Package () {
			Id = "Xamarin.Android.Wear",
			Version = "2.2.0",
			TargetFramework = "MonoAndroid80",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Wearable") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Wear.2.2.0\\lib\\MonoAndroid80\\Xamarin.Android.Wear.dll" }
				}
		};
		public static Package SupportV7RecyclerView_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.v7.RecyclerView",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.V7.RecyclerView") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.v7.RecyclerView.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.v7.RecyclerView.dll"
				}
			}
		};
		public static Package SupportV7CardView_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.v7.Cardview",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.v7.CardView") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.v7.CardView.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.v7.CardView.dll" }
			}
		};
		public static Package SupportV7AppCompat_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.v7.AppCompat",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.v7.AppCompat") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.v7.AppCompat.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.v7.AppCompat.dll" }
			}
		};
		public static Package SupportCompat_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.Compat",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.Compat") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.Compat.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.Compat.dll" }
			}
		};
		public static Package SupportCoreUI_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.Core.UI",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.Core.UI") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.Core.UI.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.Core.UI.dll" }
			}
		};
		public static Package SupportCoreUtils_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.Core.Utils",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.Core.Utils") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.Core.Utils.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.Core.Utils.dll" }
			}
		};
		public static Package SupportFragment_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.Fragment",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.Fragment") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.Fragment.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.Fragment.dll" }
			}
		};
		public static Package SupportMediaCompat_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.Media.Compat",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.Media.Compat") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.Media.Compat.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.Media.Compat.dll" }
			}
		};
		public static Package SupportPercent_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.Percent",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.Percent") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.Percent.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.Percent.dll" }
			}
		};
		public static Package SupportV7MediaRouter_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.v7.MediaRouter",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.v7.MediaRouter") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.v7.MediaRouter.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.v7.MediaRouter.dll" }
			}
		};
		public static Package SupportConstraintLayout_1_0_2_2 = new Package {
			Id = "Xamarin.Android.Support.Constraint.Layout",
			Version = "1.0.2.2",
			TargetFramework = "MonoAndroid70",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.Constraint.Layout") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.Constraint.Layout.1.0.2.2\\lib\\MonoAndroid70\\Xamarin.Android.Support.Constraint.Layout.dll"
				}
			}
		};
		public static Package VectorDrawable_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.Vector.Drawable",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.Vector.Drawable") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.Vector.Drawable.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.Vector.Drawable.dll" }
			},
		};
		public static Package SupportDesign_27_0_2_1 = new Package {
			Id = "Xamarin.Android.Support.Design",
			Version = "27.0.2.1",
			TargetFramework = "MonoAndroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.Support.Design") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.Design.27.0.2.1\\lib\\MonoAndroid81\\Xamarin.Android.Support.Design.dll" }
				}
		};
		public static Package XamarinFormsPCL_2_3_4_231 = new Package {
			Id = "Xamarin.Forms",
			Version = "2.3.4.231",
			TargetFramework = "portable-net45+win+wp80+MonoAndroid10+xamarinios10+MonoTouch10",
			References = {
				new BuildItem.Reference ("Xamarin.Forms.Core") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.2.3.4.231\\lib\\portable-win+net45+wp80+win81+wpa81+MonoAndroid10+MonoTouch10+Xamarin.iOS10\\Xamarin.Forms.Core.dll"
				},
				new BuildItem.Reference ("Xamarin.Forms.Xaml") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.2.3.4.231\\lib\\portable-win+net45+wp80+win81+wpa81+MonoAndroid10+MonoTouch10+Xamarin.iOS10\\Xamarin.Forms.Xaml.dll"
				},
			}
		};
		public static Package XamarinForms_2_3_4_231 = new Package {
			Id = "Xamarin.Forms",
			Version = "2.3.4.231",
			TargetFramework = "MonoAndroid44",
			References =  {
				new BuildItem.Reference ("Xamarin.Forms.Platform.Android") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.2.3.4.231\\lib\\MonoAndroid10\\Xamarin.Forms.Platform.Android.dll"
				},
				new BuildItem.Reference ("FormsViewGroup") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.2.3.4.231\\lib\\MonoAndroid10\\FormsViewGroup.dll"
				},
				new BuildItem.Reference ("Xamarin.Forms.Core") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.2.3.4.231\\lib\\MonoAndroid10\\Xamarin.Forms.Core.dll"
				},
				new BuildItem.Reference ("Xamarin.Forms.Xaml") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.2.3.4.231\\lib\\MonoAndroid10\\Xamarin.Forms.Xaml.dll"
				},
				new BuildItem.Reference ("Xamarin.Forms.Platform") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.2.3.4.231\\lib\\MonoAndroid10\\Xamarin.Forms.Platform.dll"
				},
			}
		};
		public static Package XamarinForms_4_0_0_425677 = new Package {
			Id = "Xamarin.Forms",
			Version = "4.0.0.425677",
			TargetFramework = "MonoAndroid90",
			References =  {
				new BuildItem.Reference ("Xamarin.Forms.Platform.Android") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.4.0.0.425677\\lib\\MonoAndroid90\\Xamarin.Forms.Platform.Android.dll"
				},
				new BuildItem.Reference ("FormsViewGroup") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.4.0.0.425677\\lib\\MonoAndroid90\\FormsViewGroup.dll"
				},
				new BuildItem.Reference ("Xamarin.Forms.Core") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.4.0.0.425677\\lib\\MonoAndroid90\\Xamarin.Forms.Core.dll"
				},
				new BuildItem.Reference ("Xamarin.Forms.Xaml") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.4.0.0.425677\\lib\\MonoAndroid90\\Xamarin.Forms.Xaml.dll"
				},
				new BuildItem.Reference ("Xamarin.Forms.Platform") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.4.0.0.425677\\lib\\MonoAndroid90\\Xamarin.Forms.Platform.dll"
				},
			}
		};
		public static Package XamarinForms_4_7_0_1142 = new Package {
			Id = "Xamarin.Forms",
			Version = "4.7.0.1142",
			TargetFramework = "MonoAndroid10.0",
		};
		public static Package XamarinForms_5_0_0_2515 = new Package {
			Id = "Xamarin.Forms",
			Version = "5.0.0.2515",
			TargetFramework = "MonoAndroid10.0",
		};
		public static Package XamarinFormsMaps_4_7_0_1142 = new Package {
			Id = "Xamarin.Forms.Maps",
			Version = "4.7.0.1142",
			TargetFramework = "MonoAndroid10.0",
		};
		public static Package XamarinFormsMaps_5_0_0_2515 = new Package {
			Id = "Xamarin.Forms.Maps",
			Version = "5.0.0.2515",
			TargetFramework = "MonoAndroid10.0",
		};
		public static Package XamarinFormsMaps_4_0_0_425677 = new Package {
			Id = "Xamarin.Forms.Maps",
			Version = "4.0.0.425677",
			TargetFramework = "MonoAndroid90",
			References =  {
				new BuildItem.Reference ("Xamarin.Forms.Maps.Android") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.Maps.4.0.0.425677\\lib\\MonoAndroid90\\Xamarin.Forms.Maps.Android.dll"
				},
				new BuildItem.Reference ("Xamarin.Forms.Maps") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.Maps.4.0.0.425677\\lib\\MonoAndroid90\\Xamarin.Forms.Maps.dll"
				},
			}
		};
		public static Package XamarinForms_4_4_0_991265 = new Package {
			Id = "Xamarin.Forms",
			Version = "4.4.0.991265",
			TargetFramework = "MonoAndroid90",
			References =  {
				new BuildItem.Reference ("Xamarin.Forms.Platform.Android") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.4.4.0.991265\\lib\\MonoAndroid90\\Xamarin.Forms.Platform.Android.dll"
				},
				new BuildItem.Reference ("FormsViewGroup") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.4.4.0.991265\\lib\\MonoAndroid90\\FormsViewGroup.dll"
				},
				new BuildItem.Reference ("Xamarin.Forms.Core") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.4.4.0.991265\\lib\\MonoAndroid90\\Xamarin.Forms.Core.dll"
				},
				new BuildItem.Reference ("Xamarin.Forms.Xaml") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.4.4.0.991265\\lib\\MonoAndroid90\\Xamarin.Forms.Xaml.dll"
				},
				new BuildItem.Reference ("Xamarin.Forms.Platform") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Forms.4.4.0.991265\\lib\\MonoAndroid90\\Xamarin.Forms.Platform.dll"
				},
			}
		};
		public static Package AndroidXMigration = new Package {
			Id = "Xamarin.AndroidX.Migration",
			Version = "1.0.6.1",
			TargetFramework = "MonoAndroid10",
		};
		public static Package AndroidXAppCompat = new Package {
			Id = "Xamarin.AndroidX.AppCompat",
			Version = "1.1.0.1",
			TargetFramework = "MonoAndroid10",
		};
		public static Package AndroidXAppCompat_1_6_0_1 = new Package {
			Id = "Xamarin.AndroidX.AppCompat",
			Version = "1.6.0.1",
			TargetFramework = "MonoAndroid10",
		};
		public static Package AndroidXBrowser = new Package {
			Id = "Xamarin.AndroidX.Browser",
			Version = "1.2.0.1",
			TargetFramework = "MonoAndroid90",
			References =  {
				new BuildItem.Reference ("Xamarin.AndroidX.Browser") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.AndroidX.Browser.1.0.0\\lib\\MonoAndroid90\\Xamarin.AndroidX.Browser.dll"
				},
			}
		};
		public static Package AndroidXMediaRouter = new Package {
			Id = "Xamarin.AndroidX.MediaRouter",
			Version = "1.1.0.1",
			TargetFramework = "MonoAndroid90",
			References =  {
				new BuildItem.Reference ("Xamarin.AndroidX.MediaRouter") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.AndroidX.MediaRouter.1.1.0\\lib\\MonoAndroid90\\Xamarin.AndroidX.MediaRouter.dll"
				},
			}
		};
		public static Package AndroidXLegacySupportV4 = new Package {
			Id = "Xamarin.AndroidX.Legacy.Support.V4",
			Version = "1.0.0.1",
			TargetFramework = "MonoAndroid90",
			References =  {
				new BuildItem.Reference ("Xamarin.AndroidX.Legacy.Support.V4") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.AndroidX.Legacy.Support.V4.1.0.0\\lib\\MonoAndroid90\\Xamarin.AndroidX.Legacy.Support.V4.dll"
				},
			}
		};
		public static Package AndroidXLifecycleLiveData = new Package {
			Id = "Xamarin.AndroidX.Lifecycle.LiveData",
			Version = "2.2.0.1",
			TargetFramework = "MonoAndroid90",
			References =  {
				new BuildItem.Reference ("Xamarin.AndroidX.Lifecycle.LiveData") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.AndroidX.Lifecycle.LiveData.2.1.0\\lib\\MonoAndroid90\\Xamarin.AndroidX.Lifecycle.LiveData.dll"
				},
			}
		};
		public static Package AndroidXAppCompatResources = new Package {
			Id = "Xamarin.AndroidX.AppCompat.AppCompatResources",
			Version = "1.1.0.1",
			TargetFramework = "MonoAndroid90",
			References =  {
				new BuildItem.Reference ("Xamarin.AndroidX.AppCompat.AppCompatResources") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.AndroidX.AppCompat.AppCompatResources.1.1.0.1\\lib\\MonoAndroid90\\Xamarin.AndroidX.AppCompat.AppCompatResources.dll"
				},
			}
		};
		public static Package AndroidXWorkRuntime = new Package {
			Id = "Xamarin.AndroidX.Work.Runtime",
			Version = "2.3.4.3",
			TargetFramework = "MonoAndroid90",
			References = {
				new BuildItem.Reference("Xamarin.AndroidX.Work.Runtime") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.AndroidX.Work.Runtime.2.3.4.3\\lib\\MonoAndroid90\\Xamarin.AndroidX.Work.Runtime.dll"
				}
			}
		};
		public static Package AndroidXSecurityCrypto = new Package {
			Id = "Xamarin.AndroidX.Security.SecurityCrypto",
			Version = "1.0.0.9",
			TargetFramework = "MonoAndroid90",
			References = {
				new BuildItem.Reference("Xamarin.AndroidX.Security.SecurityCrypto") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.AndroidX.Security.SecurityCrypto.1.0.0.9\\lib\\MonoAndroid90\\Xamarin.AndroidX.Security.SecurityCrypto.dll"
				}
			}
		};
		public static Package XamarinGoogleAndroidMaterial = new Package {
			Id = "Xamarin.Google.Android.Material",
			Version = "1.0.0.1",
			TargetFramework = "MonoAndroid90",
			References =  {
				new BuildItem.Reference ("Xamarin.Google.Android.Material") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Google.Android.Material.1.0.0\\lib\\MonoAndroid90\\Xamarin.Google.Android.Material.dll"
				},
			}
		};
		public static Package CocosSharp_PCL_Shared_1_5_0_0 = new Package {
			Id = "CocosSharp.PCL.Shared",
			Version = "1.5.0.0",
			TargetFramework ="MonoAndroid10",
			References =  {
				new BuildItem.Reference ("box2d") {
					MetadataValues = "HintPath=..\\packages\\CocosSharp.PCL.Shared.1.5.0.0\\lib\\MonoAndroid10\\box2d.dll"
				},
				new BuildItem.Reference ("CocosSharp") {
					MetadataValues = "HintPath=..\\packages\\CocosSharp.PCL.Shared.1.5.0.0\\lib\\MonoAndroid10\\CocosSharp.dll"
				},
				new BuildItem.Reference ("MonoGame.Framework") {
					MetadataValues = "HintPath=..\\packages\\CocosSharp.PCL.Shared.1.5.0.0\\lib\\MonoAndroid10\\MonoGame.Framework.dll"
				},
			}
		};
		public static Package MonoGame_Framework_Android_3_4_0_459 = new Package {
			Id = "MonoGame.Framework.Android",
			Version = "3.4.0.459",
			TargetFramework = "MonoAndroid10",
			References = {
				new BuildItem.Reference ("MonoGame.Framework") {
					MetadataValues = "HintPath=..\\packages\\MonoGame.Framework.Android.3.4.0.459\\lib\\MonoAndroid\\MonoGame.Framework.dll"
				},
			}
		};
		public static Package Xamarin_Android_Support_v8_RenderScript_28_0_0_3 = new Package {
			Id = "Xamarin.Android.Support.v8.RenderScript",
			Version = "28.0.0.3",
			TargetFramework = "MonoAndroid90",
			References = {
				new BuildItem.Reference ("MonoGame.Framework") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Support.v8.RenderScript.28.0.0.3\\lib\\MonoAndroid90\\Xamarin.Android.Support.v8.RenderScript.dll"
				},
			}
		};
		public static Package FSharp_Core_Latest = new Package {
			Id = "FSharp.Core",
			Version = "4.7.1",
			TargetFramework = "netstandard2.0",
			References = {
				new BuildItem.Reference ("mscorlib"),
				new BuildItem.Reference ("FSharp.Core") {
					MetadataValues = "HintPath=..\\packages\\FSharp.Core.4.7.1\\lib\\netstandard2.0\\FSharp.Core.dll"
				},
			}
		};
		public static Package Xamarin_Android_FSharp_ResourceProvider = new Package {
			Id = "Xamarin.Android.FSharp.ResourceProvider",
			Version = "1.0.1",
			TargetFramework = "monoandroid81",
			References = {
				new BuildItem.Reference ("Xamarin.Android.FSharp.ResourceProvider.Runtime") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.FSharp.ResourceProvider.1.0.1\\lib\\monoandroid81\\Xamarin.Android.FSharp.ResourceProvider.Runtime.dll"
				},
			}
		};
		public static Package SQLitePCLRaw_Core = new Package {
			Id = "SQLitePCLRaw.core",
			Version = "1.1.8",
			TargetFramework = "monoandroid71",
			References = {
				new BuildItem.Reference("SQLitePCL") {
					MetadataValues = "HintPath=..\\packages\\SQLitePCLRaw.core.1.1.8\\lib\\MonoAndroid\\SQLitePCLRaw.core.dll"
				}
			}
		};
		public static Package Microsoft_Azure_EventHubs = new Package {
			Id = "Microsoft.Azure.EventHubs",
			Version = "2.2.1",
			TargetFramework = "netstandard2.0",
			References = {
				new BuildItem.Reference("Microsoft.Azure.EventHubs") {
					MetadataValues = "HintPath=..\\packages\\Microsoft.Azure.EventHubs.2.2.1\\lib\\netstandard2.0\\Microsoft.Azure.EventHubs.dll"
				}
			}
		};
		public static Package PCLCrypto_Alpha = new Package {
			Id = "PCLCrypto",
			Version = "2.1.17-alpha-g5b1e8dff8c",
			TargetFramework = "monoandroid23",
			References = {
				new BuildItem.Reference("PCLCrypto") {
					MetadataValues = "HintPath=..\\packages\\PCLCrypto.2.1.17-alpha-g5b1e8dff8c\\lib\\monoandroid23\\PCLCrypto.dll"
				}
			}
		};
		public static Package Android_Arch_Core_Common_26_1_0 = new Package {
			Id = "Xamarin.Android.Arch.Core.Common",
			Version = "26.1.0",
			TargetFramework = "MonoAndroid80",
			References = {
				new BuildItem.Reference("Xamarin.Android.Arch.Core.Common") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Arch.Core.Common.26.1.0\\lib\\MonoAndroid80\\Xamarin.Android.Arch.Core.Common.dll"
				}
			}
		};
		public static Package Android_Arch_Lifecycle_Common_26_1_0 = new Package {
			Id = "Xamarin.Android.Arch.Lifecycle.Common",
			Version = "26.1.0",
			TargetFramework = "MonoAndroid80",
			References = {
				new BuildItem.Reference("Xamarin.Android.Arch.Lifecycle.Common") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Arch.Lifecycle.Common.26.1.0\\lib\\MonoAndroid80\\Xamarin.Android.Arch.Lifecycle.Common.dll"
				}
			}
		};
		public static Package Android_Arch_Lifecycle_Runtime_26_1_0 = new Package {
			Id = "Xamarin.Android.Arch.Lifecycle.Runtime",
			Version = "26.1.0",
			TargetFramework = "MonoAndroid80",
			References = {
				new BuildItem.Reference("Xamarin.Android.Arch.Lifecycle.Runtime") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Arch.Lifecycle.Runtime.26.1.0\\lib\\MonoAndroid80\\Xamarin.Android.Arch.Lifecycle.Runtime.dll"
				}
			}
		};
		public static Package Android_Arch_Work_Runtime = new Package {
			Id = "Xamarin.Android.Arch.Work.Runtime",
			Version = "1.0.0",
			TargetFramework = "MonoAndroid90",
			References = {
				new BuildItem.Reference("Xamarin.Android.Arch.Work.Runtime") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Arch.Work.Runtime.1.0.0\\lib\\MonoAndroid90\\Xamarin.Android.Arch.Work.Runtime.dll"
				}
			}
		};
		public static Package Xamarin_Android_Crashlytics = new Package {
			Id = "Xamarin.Android.Crashlytics",
			Version = "2.9.4.4",
			TargetFramework = "MonoAndroid60",
			References = {
				new BuildItem.Reference("Xamarin.Android.Crashlytics") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Crashlytics.2.9.4.4\\lib\\MonoAndroid60\\Xamarin.Android.Crashlytics.dll"
				}
			}
		};
		public static Package Xamarin_Android_Fabric = new Package {
			Id = "Xamarin.Android.Fabric",
			Version = "1.4.3.4",
			TargetFramework = "MonoAndroid60",
			References = {
				new BuildItem.Reference("Xamarin.Android.Fabric") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.Android.Fabric.1.4.3.4\\lib\\MonoAndroid60\\Xamarin.Android.Fabric.dll"
				}
			}
		};
		public static Package Xamarin_Build_Download = new Package {
			Id = "Xamarin.Build.Download",
			Version = "0.11.2",
		};

		public static Package Xamarin_Build_Download_0_11_3 = new Package {
			Id = "Xamarin.Build.Download",
			Version = "0.11.3",
		};
		// NOTE: old version required for some tests
		public static Package Xamarin_Build_Download_0_4_11 = new Package {
			Id = "Xamarin.Build.Download",
			Version = "0.4.11",
		};
		public static Package NuGet_Build_Packaging = new Package {
			Id = "NuGet.Build.Packaging",
			Version = "0.2.2",
		};
		public static Package Xamarin_GooglePlayServices_Base = new Package {
			Id = "Xamarin.GooglePlayServices.Base",
			Version = "117.6.0.2",
		};
		public static Package Xamarin_GooglePlayServices_Basement = new Package {
			Id = "Xamarin.GooglePlayServices.Basement",
			Version = "117.6.0.3",
		};
		public static Package Xamarin_GooglePlayServices_Tasks = new Package {
			Id = "Xamarin.GooglePlayServices.Tasks",
			Version = "117.2.1.2",
		};
		public static Package Xamarin_GooglePlayServices_Maps = new Package {
			Id = "Xamarin.GooglePlayServices.Maps",
			Version = "117.0.1.2",
		};
		public static Package Xamarin_Kotlin_StdLib_Common = new Package {
			Id = "Xamarin.Kotlin.Stdlib.Common",
			Version = "1.6.20.1"
		};
		public static Package Acr_UserDialogs = new Package {
			Id = "Acr.UserDialogs",
			Version = "6.5.1",
			TargetFramework = "MonoAndroid10",
			References = {
				new BuildItem.Reference ("Acr.UserDialogs") {
					MetadataValues = "HintPath=..\\packages\\Acr.UserDialogs.6.5.1\\lib\\MonoAndroid10\\Acr.UserDialogs.dll"
				},
				new BuildItem.Reference ("Acr.UserDialogs.Interfaces") {
					MetadataValues = "HintPath=..\\packages\\Acr.UserDialogs.6.5.1\\lib\\MonoAndroid10\\Acr.UserDialogs.Interfaces.dll"
				}
			},
		};
		public static Package CircleImageView = new Package {
			Id = "Refractored.Controls.CircleImageView",
			Version = "1.0.1",
			TargetFramework = "MonoAndroid10",
			References = {
				new BuildItem.Reference ("Refractored.Controls.CircleImageView") {
					MetadataValues = "HintPath=..\\packages\\Refractored.Controls.CircleImageView.1.0.1\\lib\\MonoAndroid10\\Refractored.Controls.CircleImageView.dll"
				}
			},
		};
		public static Package Microsoft_Extensions_Http = new Package {
			Id = "Microsoft.Extensions.Http",
			Version = "2.2.0",
			TargetFramework = "netstandard2.0",
			References = {
				new BuildItem.Reference ("Microsoft.Extensions.Http") {
					MetadataValues = "HintPath=..\\packages\\Microsoft.Extensions.Http.2.2.0\\lib\\netstandard2.0\\Microsoft.Extensions.Http.dll"
				}
			},
		};
		public static Package Akavache = new Package {
			Id = "akavache",
			Version = "6.0.30",
			TargetFramework = "netstandard2.0",
		};
		/// <summary>
		/// A NuGet package that has an EmbeddedResource in PdfViewBinding.dll:
		/// __AndroidLibraryProjects__.zip\library_project_imports\res\.gitignore
		/// </summary>
		public static Package Xamarin_PdfView_Android = new Package {
			Id = "Xamarin.PdfView.Android",
			Version = "1.0.4",
			References = {
				new BuildItem.Reference ("Xamarin.PdfView.Android") {
					MetadataValues = "HintPath=..\\packages\\Xamarin.PdfView.Android.1.0.4\\lib\\PdfViewBinding.dll"
				}
			},
		};
		public static Package ZXing_Net_Mobile = new Package {
			Id = "ZXing.Net.Mobile",
			Version = "2.4.1",
			TargetFramework = "MonoAndroid10",
		};
		public static Package Xamarin_Legacy_OpenTK = new Package {
			Id = "Xamarin.Legacy.OpenTK",
			Version = "0.0.1-alpha",
			TargetFramework = "MonoAndroid10",
		};
		public static Package Xamarin_Legacy_NUnitLite = new Package {
			Id = "Xamarin.Legacy.NUnitLite",
			Version = "0.0.1-alpha",
			TargetFramework = "MonoAndroid10",
		};
		public static Package Mono_AotProfiler_Android  = new Package {
			Id = "Mono.AotProfiler.Android",
			Version = "7.0.0-preview1",
		};
	}
}

