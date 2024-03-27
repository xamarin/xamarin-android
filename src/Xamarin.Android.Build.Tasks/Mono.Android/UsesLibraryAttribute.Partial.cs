using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;

using Java.Interop.Tools.Cecil;

using Xamarin.Android.Manifest;

namespace Android.App {

	partial class UsesLibraryAttribute {

		ICollection<string> specified;

		public static IEnumerable<UsesLibraryAttribute> FromCustomAttributeProvider (ICustomAttributeProvider provider, TypeDefinitionCache cache)
		{
			var attrs = provider.GetCustomAttributes ("Android.App.UsesLibraryAttribute");
			foreach (var attr in attrs) {
				UsesLibraryAttribute self;

				string[] extra = null;
				if (attr.ConstructorArguments.Count == 1) {
					self = new UsesLibraryAttribute (
							(string)  attr.ConstructorArguments [0].Value);
					extra = new[]{"Name"};
				} else if (attr.ConstructorArguments.Count == 2) {
					self = new UsesLibraryAttribute (
							(string)  attr.ConstructorArguments [0].Value,
							(bool)    attr.ConstructorArguments [1].Value);
					extra = new[]{"Name", "Required"};
				} else {
					self = new UsesLibraryAttribute ();
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
	}
}
