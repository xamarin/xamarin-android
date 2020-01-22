using System;

namespace Xamarin.Android.Tasks
{
	public abstract class BundleTool : JavaToolTask
	{
		protected override string MainClass => "com.android.tools.build.bundletool.BundleToolMain";
	}
}
