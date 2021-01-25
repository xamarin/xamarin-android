using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	abstract class XatCommand : AppObject
	{
		public abstract Task<bool> Invoke ();
	}
}
