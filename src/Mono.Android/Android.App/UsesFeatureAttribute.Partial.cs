using System;

namespace Android.App;

public sealed partial class UsesFeatureAttribute
{
	public UsesFeatureAttribute (string name)
	{
		Name = name;
	}
}
