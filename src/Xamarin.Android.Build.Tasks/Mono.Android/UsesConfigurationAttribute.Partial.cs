using System;

using System.Collections.Generic;
using Xamarin.Android.Manifest;
using System.Xml.Linq;
using Mono.Cecil;

using Java.Interop.Tools.Cecil;

namespace Android.App {

	partial class UsesConfigurationAttribute	{

		internal XElement ToElement (string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache);
		}

		ICollection<string> specified;

		public static IEnumerable<UsesConfigurationAttribute> FromCustomAttributeProvider (ICustomAttributeProvider provider, TypeDefinitionCache cache)
		{
			var attrs = provider.GetCustomAttributes ("Android.App.UsesConfigurationAttribute");
			foreach (var attr in attrs) {

				UsesConfigurationAttribute self = new UsesConfigurationAttribute ();

				self.specified = mapping.Load (self, attr, cache);

				yield return self;
			}
		}
	}
}

