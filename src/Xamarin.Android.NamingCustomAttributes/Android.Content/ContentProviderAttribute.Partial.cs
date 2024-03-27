using System;

namespace Android.Content;

public partial class ContentProviderAttribute
{
	public ContentProviderAttribute (string [] authorities)
	{
		if (authorities == null)
			throw new ArgumentNullException ("authorities");
		if (authorities.Length < 1)
			throw new ArgumentException ("At least one authority must be specified.", "authorities");
		Authorities = authorities;
	}
}
