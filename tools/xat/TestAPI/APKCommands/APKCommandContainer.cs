using System;
using System.Threading.Tasks;

namespace Xamarin.Android.Tests.APK
{
    class APKCommandContainer : APKTestCommand
    {
	    public APKCommandContainer ()
		    : base ("APKCommandContainer", "Container for nested APK test commands")
	    {}

#pragma warning disable 1998
        protected override async Task<bool> Run (TestAPK test)
        {
            throw new InvalidOperationException ("Should never be called, did you add commands to the Commands property?");
        }
#pragma warning restore 1998
    }
}
