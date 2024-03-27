using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;

using Java.Interop.Tools.Cecil;

using Xamarin.Android.Manifest;

namespace Android.Content {

	partial class BroadcastReceiverAttribute {
		
		ICollection<string> specified;

		public static BroadcastReceiverAttribute FromTypeDefinition (TypeDefinition type, TypeDefinitionCache cache)
		{
			CustomAttribute attr = type.GetCustomAttributes ("Android.Content.BroadcastReceiverAttribute")
				.SingleOrDefault ();
			if (attr == null)
				return null;
			var self = new BroadcastReceiverAttribute ();
			self.specified = mapping.Load (self, attr, cache);
			return self;
		}

		public XElement ToElement (string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache);
		}
	}
}
