using System;
using Android.Runtime;

namespace Android.Net.Wifi.P2p
{
	// This was converted to an enum in .NET 9
	partial class WifiP2pManager
	{
		// Metadata.xml XPath field reference: path="/api/package[@name='android.net.wifi.p2p']/class[@name='WifiP2pManager']/field[@name='WIFI_P2P_DISCOVERY_STARTED']"
		[Register ("WIFI_P2P_DISCOVERY_STARTED")]
		public const int WifiP2pDiscoveryStarted = (int) 2;

		// Metadata.xml XPath field reference: path="/api/package[@name='android.net.wifi.p2p']/class[@name='WifiP2pManager']/field[@name='WIFI_P2P_DISCOVERY_STOPPED']"
		[Register ("WIFI_P2P_DISCOVERY_STOPPED")]
		public const int WifiP2pDiscoveryStopped = (int) 1;
	}
}
