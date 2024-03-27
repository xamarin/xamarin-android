using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;

using Java.Interop.Tools.Cecil;

using Xamarin.Android.Manifest;

namespace Android.App {

	partial class InstrumentationAttribute {
		
		ICollection<string> specified;

		public static IEnumerable<InstrumentationAttribute> FromCustomAttributeProvider (ICustomAttributeProvider provider, TypeDefinitionCache cache)
		{
			foreach (CustomAttribute attr in provider.GetCustomAttributes ("Android.App.InstrumentationAttribute")) {
				InstrumentationAttribute self = new InstrumentationAttribute ();
				self.specified = mapping.Load (self, attr, cache);
				yield return self;
			}
		}

		public void SetTargetPackage (string package)
		{
			TargetPackage = package;
			specified.Add ("TargetPackage");
		}

		public XElement ToElement (string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache);
		}
	}
}
