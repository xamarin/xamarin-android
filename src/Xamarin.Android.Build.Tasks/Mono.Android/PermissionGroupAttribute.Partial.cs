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

	partial class PermissionGroupAttribute {
		
		ICollection<string> specified;

		public static IEnumerable<PermissionGroupAttribute> FromCustomAttributeProvider (ICustomAttributeProvider provider, TypeDefinitionCache cache)
		{
			var attrs = provider.GetCustomAttributes ("Android.App.PermissionGroupAttribute");
			foreach (var attr in attrs) {
				PermissionGroupAttribute self = new PermissionGroupAttribute ();

				self.specified = mapping.Load (self, attr, cache);

				yield return self;
			}
		}

		internal XElement ToElement (string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache);
		}

		internal class PermissionGroupAttributeComparer : IEqualityComparer<PermissionGroupAttribute>
		{
			#region IEqualityComparer<PermissionGroupAttribute> Members
			public bool Equals (PermissionGroupAttribute x, PermissionGroupAttribute y)
			{
				return
					x.Description == y.Description &&
						x.Icon == y.Icon &&
						x.Label == y.Label &&
						x.Name == y.Name;
			}

			public int GetHashCode (PermissionGroupAttribute obj)
			{
				return (obj.Name ?? String.Empty).GetHashCode ();
			}
			#endregion
		}
	}
}
