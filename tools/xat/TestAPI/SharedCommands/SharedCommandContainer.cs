using System;
using System.Threading.Tasks;

namespace Xamarin.Android.Tests.Shared
{
	class SharedCommandContainer : SharedTestCommand
	{
		public SharedCommandContainer ()
			: base (nameof (SharedCommandContainer), "Container for nested test commands")
		{}

#pragma warning disable 1998
		protected override async Task<bool> Execute (XATest test)
		{
			throw new InvalidOperationException ("Should never be called, did you add commands to the Commands property?");
		}
#pragma warning restore 1998
	}
}
