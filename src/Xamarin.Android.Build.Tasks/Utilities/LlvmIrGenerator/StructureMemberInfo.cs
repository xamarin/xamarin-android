using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks.LLVMIR
{
	sealed class StructureMemberInfo<T>
	{
		public string IRType        { get; }
		public MemberInfo Info      { get; }
		public Type MemberType      { get; }
		public ulong Size           { get; }
		public bool IsNativePointer { get; }

		// Used only by string members (to make structure generation faster and use less memory, since instead of caching
		// the info here we would have to store the information in a dictionary before outputting the actual structure in
		// LlvmIrGenerator.WriteStructure<T>
		public string? StringVariableName { get; set; } = null;
		public ulong StringSize           { get; set; } = 0;

		public StructureMemberInfo (MemberInfo mi, LlvmIrGenerator generator)
		{
			Info = mi;

			// TODO: check member custom attributes to see if the native member should be a pointer (except for string,
			// since MapManagedType already takes care of that)
			MemberType = mi switch {
				FieldInfo fi => fi.FieldType,
				PropertyInfo pi => pi.PropertyType,
				_ => throw new InvalidOperationException ($"Unsupported member type {mi}")
			};

			ulong size = 0;
			if (MemberType != typeof(string) && (MemberType.IsStructure () || MemberType.IsClass)) {
				IRType = $"%struct.{MemberType.GetShortName ()}";
				// TODO: figure out how to get structure size if it isn't a pointer
			} else {
				IRType = generator.MapManagedTypeToIR (MemberType, out size);
			}
			IsNativePointer = IRType[IRType.Length - 1] == '*';

			if (!IsNativePointer) {
				IsNativePointer = mi.IsNativePointer ();
				if (IsNativePointer) {
					IRType += "*";
				}
			}

			if (IsNativePointer) {
				size = (ulong)generator.PointerSize;
			}

			Size = size;
		}

		public object? GetValue (T instance)
		{
			if (Info is FieldInfo fi) {
				return fi.GetValue (instance);
			}

			var pi = Info as PropertyInfo;
			return pi.GetValue (instance);
		}
	}
}
