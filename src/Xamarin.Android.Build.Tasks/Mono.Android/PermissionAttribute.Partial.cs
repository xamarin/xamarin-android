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

	partial class PermissionAttribute {
		
		ICollection<string> specified;

		public static IEnumerable<PermissionAttribute> FromCustomAttributeProvider (ICustomAttributeProvider provider, TypeDefinitionCache cache)
		{
			var attrs = provider.GetCustomAttributes ("Android.App.PermissionAttribute");
			foreach (var attr in attrs) {
				PermissionAttribute self = new PermissionAttribute ();

				self.specified = mapping.Load (self, attr, cache);

				yield return self;
			}
		}

		internal XElement ToElement (string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache);
		}

		internal class PermissionAttributeComparer : IEqualityComparer<PermissionAttribute>
		{
			#region IEqualityComparer<PermissionAttribute> Members
			public bool Equals (PermissionAttribute x, PermissionAttribute y)
			{
				return
					x.Description == y.Description &&
						x.Icon == y.Icon &&
						x.Label == y.Label &&
						x.Name == y.Name &&
						x.PermissionGroup == y.PermissionGroup &&
						x.ProtectionLevel == y.ProtectionLevel;
			}

			public int GetHashCode (PermissionAttribute obj)
			{
				return (obj.Name ?? String.Empty).GetHashCode ();
			}
			#endregion
		}
	}
}
