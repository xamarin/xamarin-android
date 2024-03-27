using System;

namespace Android.App;

public sealed partial class UsesLibraryAttribute
{
	public UsesLibraryAttribute (string name)
	{
		Name = name;
	}

	public UsesLibraryAttribute (string name, bool required) : this (name)
	{
		Required = required;
	}
}
