using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using Mono.Cecil;

using Java.Interop.Tools.Cecil;

using Xamarin.Android.Manifest;

using Android.Content.PM;
using Android.Views;

namespace Android.App
{

	partial class LayoutAttribute
	{
		TypeDefinition type;
		ICollection<string> specified;

		public static LayoutAttribute FromTypeDefinition (TypeDefinition type, TypeDefinitionCache cache)
		{
			CustomAttribute attr = type.GetCustomAttributes ("Android.App.LayoutAttribute")
				.SingleOrDefault ();
			if (attr == null)
				return null;
			LayoutAttribute self = new LayoutAttribute () {
				type = type,
			};
			self.specified = mapping.Load (self, attr, cache);
			return self;
		}

		internal XElement ToElement (IAssemblyResolver resolver, string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache, type, resolver);
		}
	}
}
