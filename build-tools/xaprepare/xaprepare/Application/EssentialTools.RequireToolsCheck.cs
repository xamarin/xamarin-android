using System;

namespace Xamarin.Android.Prepare
{
	partial class EssentialTools
	{
		bool AreToolsRequired (Context context)
		{
			return !context.CheckCondition (KnownConditions.EnsureEssential) && context.CheckCondition (KnownConditions.AllowProgramInstallation);
		}
	}
}
