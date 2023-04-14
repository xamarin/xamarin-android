using System;

namespace Xamarin.Android.Debug;

class ApplicationInfo
{
	public string PackageName { get; }
	public uint MinSdkVersion { get; }
	public bool Debuggable    { get; set; }
	public string? Activity   { get; set; }

	public ApplicationInfo (string packageName, string minSdkVersion)
	{
		PackageName = packageName;

		if (!UInt32.TryParse (minSdkVersion, out uint ver)) {
			throw new ArgumentException ($"Unable to parse minimum SDK version from '{minSdkVersion}'", nameof (minSdkVersion));
		}
		MinSdkVersion = ver;
	}
}
