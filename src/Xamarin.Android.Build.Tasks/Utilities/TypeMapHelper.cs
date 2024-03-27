using System;
using System.IO.Hashing;
using System.Text;

namespace Xamarin.Android.Tasks;

static class TypeMapHelper
{
	/// <summary>
	/// Hash the given Java type name for use in java-to-managed typemap array.
	/// </summary>
	public static ulong HashJavaName (string name, bool is64Bit)
	{
		if (name.Length == 0) {
			return UInt64.MaxValue;
		}

		// Native code (EmbeddedAssemblies::typemap_java_to_managed in embedded-assemblies.cc) will operate on wchar_t cast to a byte array, we need to do
		// the same
		return HashBytes (Encoding.Unicode.GetBytes (name), is64Bit);
	}

	static ulong HashBytes (byte[] bytes, bool is64Bit)
	{
		if (is64Bit) {
			return XxHash3.HashToUInt64 (bytes);
		}

		return (ulong)XxHash32.HashToUInt32 (bytes);
	}
}
