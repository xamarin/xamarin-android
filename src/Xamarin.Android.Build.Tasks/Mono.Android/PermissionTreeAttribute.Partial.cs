using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using Mono.Cecil;

using MonoDroid.Utils;

using Java.Interop.Tools.Cecil;

using Xamarin.Android.Manifest;

using Android.Content.PM;

namespace Android.App {

	partial class PermissionTreeAttribute {
		
		ICollection<string> specified;

		public static IEnumerable<PermissionTreeAttribute> FromCustomAttributeProvider (ICustomAttributeProvider provider, TypeDefinitionCache cache)
		{
			var attrs = provider.GetCustomAttributes ("Android.App.PermissionTreeAttribute");
			foreach (var attr in attrs) {
				PermissionTreeAttribute self = new PermissionTreeAttribute ();

				self.specified = mapping.Load (self, attr, cache);

				yield return self;
			}
		}

		internal XElement ToElement (string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache);
		}

		internal class PermissionTreeAttributeComparer : IEqualityComparer<PermissionTreeAttribute>
		{
			#region IEqualityComparer<PermissionTreeAttribute> Members
			public bool Equals (PermissionTreeAttribute x, PermissionTreeAttribute y)
			{
				return
						x.Icon == y.Icon &&
						x.Label == y.Label &&
						x.Name == y.Name;
			}

			public int GetHashCode (PermissionTreeAttribute obj)
			{
				return (obj.Name ?? String.Empty).GetHashCode ();
			}
			#endregion
		}
	}
}
