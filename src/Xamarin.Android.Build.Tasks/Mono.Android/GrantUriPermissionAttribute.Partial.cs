using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;

using Java.Interop.Tools.Cecil;

using Xamarin.Android.Manifest;

namespace Android.Content {

	partial class GrantUriPermissionAttribute {

		ICollection<string> specified;

		public static IEnumerable<GrantUriPermissionAttribute> FromTypeDefinition (TypeDefinition type, TypeDefinitionCache cache)
		{
			IEnumerable<CustomAttribute> attrs = type.GetCustomAttributes ("Android.Content.GrantUriPermissionAttribute");
			if (!attrs.Any ())
				yield break;
			foreach (CustomAttribute attr in attrs) {
				var self = new GrantUriPermissionAttribute ();
				self.specified = mapping.Load (self, attr, cache);
				yield return self;
			}
		}

		public XElement ToElement (string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache);
		}
	}
}
