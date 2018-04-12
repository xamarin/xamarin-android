using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace Xamarin.Android.Sdk
{
#if UNSTABLE_FRAMEWORKS
    [Guid("0ce7b215-7684-4884-9f5b-1cdd800a5253")]
#else   // UNSTABLE_FRAMEWORKS
    [Guid("d0e8d881-b09d-40bf-923b-b3efddc53c16")]
#endif  // !UNSTABLE_FRAMEWORKS
    public class AndroidSdkPackage : Package
    {
    }
}
