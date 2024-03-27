using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using Mono.Cecil;

using MonoDroid.Utils;

using Java.Interop.Tools.Cecil;
using Java.Interop.Tools.TypeNameMappings;

using Xamarin.Android.Manifest;

using Android.Content.PM;

namespace Android.App {

	partial class ApplicationAttribute {

		string _BackupAgent;
		ICustomAttributeProvider provider;

		ICollection<string> specified;

		static partial void AddManualMapping ()
		{
			mapping.Add (
				member: "BackupAgent",
				attributeName: "backupAgent",
				setter: (self, value) => self._BackupAgent = (string) value,
				attributeValue: (self, p, r, cache) => {
					var typeDef = ManifestDocumentElement.ResolveType (self._BackupAgent, p, r);

					if (!typeDef.IsSubclassOf ("Android.App.Backup.BackupAgent", cache))
						throw new InvalidOperationException (
								string.Format ("The Type '{0}', referenced by the Android.App.ApplicationAttribute.BackupAgent property, must be a subclass of the type Android.App.Backup.BackupAgent.",
									typeDef.FullName));

					return ManifestDocumentElement.ToString (typeDef, cache);
				}
			);
			mapping.Add (
				member: "ManageSpaceActivity",
				attributeName: "manageSpaceActivity",
				getter: self => self.ManageSpaceActivity,
				setter: (self, value) => self.ManageSpaceActivity = (Type?) value,
				typeof (Type)
			);
			mapping.Add (
				member: "Name",
				attributeName: "name",
				setter: (self, value) => self.Name = (string) value,
				attributeValue: ToNameAttribute
			);
		}

		public static ApplicationAttribute FromCustomAttributeProvider (ICustomAttributeProvider provider, TypeDefinitionCache cache)
		{
			CustomAttribute attr = provider.GetCustomAttributes ("Android.App.ApplicationAttribute")
				.SingleOrDefault ();
			if (attr == null)
				return null;
			ApplicationAttribute self = new ApplicationAttribute () {
				provider  = provider,
			};
			self.specified = mapping.Load (self, attr, cache);
			if (provider is TypeDefinition) {
				self.specified.Add ("Name");
			}
			return self;
		}

		internal XElement ToElement (IAssemblyResolver resolver, string packageName, TypeDefinitionCache cache)
		{
			return mapping.ToElement (this, specified, packageName, cache, provider, resolver);
		}

		static string ToNameAttribute (ApplicationAttribute self, ICustomAttributeProvider provider, IAssemblyResolver resolver, TypeDefinitionCache cache)
		{
			var type = self.provider as TypeDefinition;
			if (string.IsNullOrEmpty (self.Name) && type != null)
				return JavaNativeTypeManager.ToJniName (type, cache).Replace ('/', '.');

			return self.Name;
		}
	}
}
