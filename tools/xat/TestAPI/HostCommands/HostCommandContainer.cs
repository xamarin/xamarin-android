using System;
using System.Threading.Tasks;

namespace Xamarin.Android.Tests.Host
{
	class HostCommandContainer : HostTestCommand
	{
		public HostCommandContainer ()
			: base ("HostCommandContainer", "Container for nested Host Unit test commands")
		{}

#pragma warning disable 1998
		protected override async Task<bool> Run (TestHostUnit test)
		{
			throw new InvalidOperationException ("Should never be called, did you add commands to the Commands property?");
		}
#pragma warning restore 1998
	}
}
