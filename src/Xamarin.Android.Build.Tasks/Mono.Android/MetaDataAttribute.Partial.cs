using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;

using Java.Interop.Tools.Cecil;

using Xamarin.Android.Manifest;

namespace Android.App {

	partial class MetaDataAttribute {

		ICollection<string> specified;

		public static IEnumerable<MetaDataAttribute> FromCustomAttributeProvider (ICustomAttributeProvider type, TypeDefinitionCache cache)
		{
			IEnumerable<CustomAttribute> attrs = type.GetCustomAttributes ("Android.App.MetaDataAttribute");
			if (!attrs.Any ())
				yield break;
			foreach (CustomAttribute attr in attrs) {
				var self = new MetaDataAttribute ((string) attr.ConstructorArguments [0].Value);
				self.specified = mapping.Load (self, attr, cache);
				self.specified.Add ("Name");
				yield return self;
			}
		}

		public XElement ToElement (string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache);
		}
	}
}
