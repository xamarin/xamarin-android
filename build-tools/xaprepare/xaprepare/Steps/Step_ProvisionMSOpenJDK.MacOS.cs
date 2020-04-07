using System;
using System.Threading.Tasks;

namespace Xamarin.Android.Prepare
{
	partial class Step_ProvisionMSOpenJDK
	{
		partial void InitOS ()
		{
			archiveURL = Configurables.Urls.MSOpenJDK;
			destinationDir = Configurables.Paths.MSOpenJDKInstallRoot;
		}
	}
}
