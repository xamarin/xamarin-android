using System;
using System.Threading.Tasks;

namespace Xamarin.Android.Prepare
{
	partial class Step_ProvisionMSOpenJDK
	{
		partial void InitOS ()
		{
			if (Context.Instance.OS.Is64Bit)
				archiveURL = Configurables.Urls.MSOpenJDK64;
			else
				archiveURL = Configurables.Urls.MSOpenJDK32;
			destinationDir = Configurables.Paths.MSOpenJDKInstallRoot;
		}
	}
}
