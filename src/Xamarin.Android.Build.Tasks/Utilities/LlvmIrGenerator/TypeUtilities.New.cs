using System;
using System.Reflection;

namespace Xamarin.Android.Tasks.LLVM.IR
{
	static class TypeUtilities
	{
		public static string GetShortName (this Type type)
		{
			string? fullName = type.FullName;

			if (String.IsNullOrEmpty (fullName)) {
				throw new InvalidOperationException ($"Unnamed types aren't supported ({type})");
			}

			int lastCharIdx = fullName.LastIndexOf ('.');
			string ret;
			if (lastCharIdx < 0) {
				ret = fullName;
			} else {
				ret = fullName.Substring (lastCharIdx + 1);
			}

			lastCharIdx = ret.LastIndexOf ('+');
			if (lastCharIdx >= 0) {
				ret = ret.Substring (lastCharIdx + 1);
			}

			if (String.IsNullOrEmpty (ret)) {
				throw new InvalidOperationException ($"Invalid type name ({type})");
			}

			return ret;
		}

		public static bool IsStructure (this Type type)
		{
			return type.IsValueType &&
				!type.IsEnum &&
				!type.IsPrimitive &&
				!type.IsArray &&
				type != typeof (decimal) &&
				type != typeof (DateTime) &&
				type != typeof (object);
		}

		public static bool IsIRStruct (this StructureMemberInfo smi)
		{
			Type type = smi.MemberType;

			// type.IsStructure() handles checks for primitive types, enums etc
			return
				type != typeof(string) &&
				!smi.Info.IsInlineArray () &&
				!smi.Info.IsNativePointer () &&
				(type.IsStructure () || type.IsClass);
		}

		public static NativeAssemblerStructContextDataProvider? GetDataProvider (this Type t)
		{
			var attr = t.GetCustomAttribute<NativeAssemblerStructContextDataProviderAttribute> ();
			if (attr == null) {
				return null;
			}

			return Activator.CreateInstance (attr.Type) as NativeAssemblerStructContextDataProvider;
		}

		public static bool IsNativeClass (this Type t)
		{
			var attr = t.GetCustomAttribute<NativeClassAttribute> ();
			return attr != null;
		}

		public static bool ImplementsInterface (this Type type, Type requiredIfaceType)
		{
			if (type == null || requiredIfaceType == null) {
				return false;
			}

			bool generic = requiredIfaceType.IsGenericType;
			foreach (Type iface in type.GetInterfaces ()) {
				if (generic) {
					if (!iface.IsGenericType) {
						continue;
					}

					if (iface.GetGenericTypeDefinition () == requiredIfaceType.GetGenericTypeDefinition ()) {
						return true;
					}
					continue;
				}

				if (iface == requiredIfaceType) {
					return true;
				}
			}

			return false;
		}
	}
}
