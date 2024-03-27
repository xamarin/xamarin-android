using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;

using Java.Interop.Tools.Cecil;

using Xamarin.Android.Manifest;

namespace Android.App {

	partial class UsesPermissionAttribute {

		ICollection<string> specified;

		public static IEnumerable<UsesPermissionAttribute> FromCustomAttributeProvider (ICustomAttributeProvider provider, TypeDefinitionCache cache)
		{
			var attrs = provider.GetCustomAttributes ("Android.App.UsesPermissionAttribute");
			foreach (var attr in attrs) {
				UsesPermissionAttribute self;

				string[] extra;
				if (attr.ConstructorArguments.Count == 1) {
					self = new UsesPermissionAttribute ((string)attr.ConstructorArguments[0].Value);
					extra = new[]{"Name"};
				} else {
					self = new UsesPermissionAttribute ();
					extra = Array.Empty<string> ();
				}

				self.specified = mapping.Load (self, attr, cache);

				foreach (var e in extra)
					self.specified.Add (e);

				yield return self;
			}
		}

		public XElement ToElement (string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache);
		}

		internal class UsesPermissionComparer : IEqualityComparer<UsesPermissionAttribute>
		{
			#region IEqualityComparer<UsesPermissionAttribute> Members
			public bool Equals (UsesPermissionAttribute x, UsesPermissionAttribute y)
			{
				return x.Name == y.Name;
			}

			public int GetHashCode (UsesPermissionAttribute obj)
			{
				return (obj.Name ?? String.Empty).GetHashCode ();
			}
			#endregion
		}
	}
}
