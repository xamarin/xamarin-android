using System;

namespace Xamarin.Android.Tests
{
	[Flags]
	enum ListItem
	{
		None       = 0 << 0,
		Suites     = 1 << 0,
		Groups = 1 << 1,
		All        = Suites | Groups
	}
}
