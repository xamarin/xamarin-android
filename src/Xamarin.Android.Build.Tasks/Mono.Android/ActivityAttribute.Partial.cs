using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using Mono.Cecil;

using Java.Interop.Tools.Cecil;

using Xamarin.Android.Manifest;

using Android.Content.PM;
using Android.Views;

namespace Android.App {

	partial class ActivityAttribute {

		string _ParentActivity;
		TypeDefinition type;
		ICollection<string> specified;

		static partial void AddManualMapping ()
		{
			mapping.Add (
				member: "MainLauncher",
				attributeName: null,
				getter: null,
				setter: (self, value) => self.MainLauncher = (bool) value
			);
			mapping.Add (
				member: "ParentActivity",
				attributeName: "parentActivityName",
				getter: self => self._ParentActivity,
				setter: (self, value) => self._ParentActivity = (string) value,
				typeof (Type)
			);
		}

		public static ActivityAttribute FromTypeDefinition (TypeDefinition type, TypeDefinitionCache cache)
		{
			CustomAttribute attr = type.GetCustomAttributes ("Android.App.ActivityAttribute")
				.SingleOrDefault ();
			if (attr == null)
				return null;
			ActivityAttribute self = new ActivityAttribute () {
				type = type,
			};
			self.specified = mapping.Load (self, attr, cache);
			return self;
		}

		internal XElement ToElement (IAssemblyResolver resolver, string packageName, TypeDefinitionCache cache, int targetSdkVersion)
		{
			return mapping.ToElement (this, specified, packageName, cache, type, resolver, targetSdkVersion);
		}
	}
}
