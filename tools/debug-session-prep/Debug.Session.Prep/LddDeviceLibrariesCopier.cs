using System;

using Xamarin.Android.Utilities;
using Xamarin.Android.Tasks;

namespace Xamarin.Debug.Session.Prep;

class LddDeviceLibraryCopier : DeviceLibraryCopier
{
	public LddDeviceLibraryCopier (XamarinLoggingHelper log, AdbRunner adb, bool appIs64Bit, string localDestinationDir, AndroidDevice device)
		: base (log, adb, appIs64Bit, localDestinationDir, device)
	{}

	public override bool Copy (out string? zygotePath)
	{
		throw new NotImplementedException();
	}
}
