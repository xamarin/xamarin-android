using System;
using System.Reflection;

namespace Xamarin.Android.Tasks.LLVM.IR
{
	// TODO: remove these aliases once the refactoring is done
	using NativePointerAttribute = LLVMIR.NativePointerAttribute;

	static class MemberInfoUtilities
	{
		public static bool IsNativePointer (this MemberInfo mi)
		{
			return mi.GetCustomAttribute <NativePointerAttribute> () != null;
		}

		public static bool IsNativePointerToPreallocatedBuffer (this MemberInfo mi, out ulong requiredBufferSize)
		{
			var attr = mi.GetCustomAttribute <NativePointerAttribute> ();
			if (attr == null) {
				requiredBufferSize = 0;
				return false;
			}

			requiredBufferSize = attr.PreAllocatedBufferSize;
			return attr.PointsToPreAllocatedBuffer;
		}

		public static bool ShouldBeIgnored (this MemberInfo mi)
		{
			var attr = mi.GetCustomAttribute<NativeAssemblerAttribute> ();
			return attr != null && attr.Ignore;
		}

		public static bool IsInlineArray (this MemberInfo mi)
		{
			var attr = mi.GetCustomAttribute<NativeAssemblerAttribute> ();
			return attr != null && attr.InlineArray;
		}

		public static int GetInlineArraySize (this MemberInfo mi)
		{
			var attr = mi.GetCustomAttribute<NativeAssemblerAttribute> ();
			if (attr == null || !attr.InlineArray) {
				return -1;
			}

			return attr.InlineArraySize;
		}

		public static bool InlineArrayNeedsPadding (this MemberInfo mi)
		{
			var attr = mi.GetCustomAttribute<NativeAssemblerAttribute> ();
			if (attr == null || !attr.InlineArray) {
				return false;
			}

			return attr.NeedsPadding;
		}
	}
}
