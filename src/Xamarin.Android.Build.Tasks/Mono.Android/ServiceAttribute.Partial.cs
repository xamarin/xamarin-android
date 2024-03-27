using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;

using Java.Interop.Tools.Cecil;

using Xamarin.Android.Manifest;

using Android.Content.PM;

namespace Android.App {

	partial class ServiceAttribute {

		ICollection<string> specified;

		public static ServiceAttribute FromTypeDefinition (TypeDefinition type, TypeDefinitionCache cache)
		{
			CustomAttribute attr = type.GetCustomAttributes ("Android.App.ServiceAttribute")
				.SingleOrDefault ();
			if (attr == null)
				return null;
			ServiceAttribute self = new ServiceAttribute ();
			self.specified = mapping.Load (self, attr, cache);
			return self;
		}

		public XElement ToElement (string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache);
		}
	}
}
