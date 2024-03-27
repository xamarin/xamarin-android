using System;
using Android.Views;

namespace Android.App;

public sealed partial class ActivityAttribute
{
	[Obsolete ("There is no //activity/@android:layoutDirection attribute. This was a mistake. " +
	    "Perhaps you wanted ConfigurationChanges=ConfigChanges.LayoutDirection?")]
	public LayoutDirection LayoutDirection { get; set; }

	public bool MainLauncher { get; set; }
}
