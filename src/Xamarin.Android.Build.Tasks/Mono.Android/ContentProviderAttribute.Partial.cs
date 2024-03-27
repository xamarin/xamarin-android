using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;

using Java.Interop.Tools.Cecil;

using Xamarin.Android.Manifest;

namespace Android.Content {

	partial class ContentProviderAttribute {
		
		static partial void AddManualMapping ()
		{
			mapping.Add (
				member: "Authorities",
				attributeName: "authorities",
				setter: (self, value) => self.Authorities = ToStringArray (value),
				attributeValue: self => string.Join (";", self.Authorities)
			);
		}

		static string[] ToStringArray (object value)
		{
			var values = (CustomAttributeArgument []) value;
			return values.Select (v => (string) v.Value).ToArray ();
		}

		ICollection<string> specified;

		public static ContentProviderAttribute FromTypeDefinition (TypeDefinition type, TypeDefinitionCache cache)
		{
			CustomAttribute attr = type.GetCustomAttributes ("Android.Content.ContentProviderAttribute")
				.SingleOrDefault ();
			if (attr == null)
				return null;
			var self = new ContentProviderAttribute (ToStringArray (attr.ConstructorArguments [0].Value));
			self.specified = mapping.Load (self, attr, cache);
			self.specified.Add ("Authorities");
			return self;
		}

		public XElement ToElement (string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache);
		}
	}
}
