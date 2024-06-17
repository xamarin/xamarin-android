using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Linker;
using Mono.Linker.Steps;
using Mono.Tuner;
using Java.Interop.Tools.Cecil;
using Xamarin.Android.Tasks;

using Java.Interop.Tools.Cecil;

namespace MonoDroid.Tuner {

	public class MarkJavaObjects : BaseMarkHandler
	{
		Dictionary<ModuleDefinition, Dictionary<string, TypeDefinition>> module_types = new Dictionary<ModuleDefinition, Dictionary<string, TypeDefinition>> ();

		public override void Initialize (LinkContext context, MarkContext markContext)
		{
			base.Initialize (context, markContext);
			context.TryGetCustomData ("AndroidHttpClientHandlerType", out string androidHttpClientHandlerType);
			context.TryGetCustomData ("AndroidCustomViewMapFile", out string androidCustomViewMapFile);
			var customViewMap = MonoAndroidHelper.LoadCustomViewMapFile (androidCustomViewMapFile);

			markContext.RegisterMarkAssemblyAction (assembly => ProcessAssembly (assembly, androidHttpClientHandlerType, customViewMap));
			markContext.RegisterMarkTypeAction (type => ProcessType (type));
		}

		bool IsActiveFor (AssemblyDefinition assembly)
		{
			return assembly.MainModule.HasTypeReference ("System.Net.Http.HttpMessageHandler") || assembly.MainModule.HasTypeReference ("Android.Util.IAttributeSet");
		}

		public void ProcessAssembly (AssemblyDefinition assembly, string androidHttpClientHandlerType, Dictionary<string, HashSet<string>> customViewMap)
		{
			if (!IsActiveFor (assembly))
				return;

			foreach (var type in assembly.MainModule.Types) {
				// Custom HttpMessageHandler
				if (!string.IsNullOrEmpty (androidHttpClientHandlerType) &&
					androidHttpClientHandlerType.StartsWith (type.Name, StringComparison.Ordinal)) {
					var assemblyQualifiedName = type.GetPartialAssemblyQualifiedName (Context);
					if (assemblyQualifiedName == androidHttpClientHandlerType) {
						Annotations.Mark (type);
						PreservePublicParameterlessConstructors (type);
						continue;
					}
				}

				// Custom views in Android .xml files
				if (!type.ImplementsIJavaObject (cache))
					continue;
				if (customViewMap.ContainsKey (type.FullName)) {
					Annotations.Mark (type);
					PreserveJavaObjectImplementation (type);
				}
			}
		}

		public void ProcessType (TypeDefinition type)
		{
			// If this isn't a JLO or IJavaObject implementer,
			// then we don't need to MarkJavaObjects
			if (!type.ImplementsIJavaObject (cache))
				return;

			PreserveJavaObjectImplementation (type);

			if (IsImplementor (type, cache))
				PreserveImplementor (type);

			// If a user overrode a method, we need to preserve it,
			// because it won't be referenced anywhere, but it will
			// be called from Java
			if (IsUserType (type) && type.HasMethods) {
				foreach (var method in type.Methods.Where (m => m.Overrides != null))
					PreserveMethod (type, method);
			}
		}

		void PreserveJavaObjectImplementation (TypeDefinition type)
		{
			PreserveIntPtrConstructor (type);
			PreserveAttributeSetConstructor (type);
			PreserveInvoker (type);
			PreserveInterfaces (type);
		}

		void PreservePublicParameterlessConstructors (TypeDefinition type)
		{
			if (!type.HasMethods)
				return;

			foreach (var constructor in type.Methods)
			{
				if (!constructor.IsConstructor || constructor.IsStatic || !constructor.IsPublic || constructor.HasParameters)
					continue;

				PreserveMethod (type, constructor);
				break; // We can stop when found
			}
		}

		void PreserveAttributeSetConstructor (TypeDefinition type)
		{
			if (!type.HasMethods)
				return;

			foreach (var constructor in GetAttributeSetConstructors (type))
				PreserveMethod (type, constructor);
		}

		static IEnumerable<MethodDefinition> GetAttributeSetConstructors (TypeDefinition type)
		{
			foreach (MethodDefinition constructor in type.Methods.Where (m => m.IsConstructor)) {
				if (!constructor.HasParameters)
					continue;

				var parameters = constructor.Parameters;

				if (parameters.Count < 2 || parameters.Count > 3)
					continue;

				if (parameters [0].ParameterType.FullName != "Android.Content.Context")
					continue;

				if (parameters [1].ParameterType.FullName != "Android.Util.IAttributeSet")
					continue;

				if (parameters.Count == 3 && parameters [2].ParameterType.FullName != "System.Int32")
					continue;

				yield return constructor;
			}
		}

		void PreserveIntPtrConstructor (TypeDefinition type)
		{
			var constructor = GetIntPtrConstructor (type);

			if (constructor != null)
				PreserveMethod (type, constructor);

			var constructor2 = GetNewIntPtrConstructor (type);

			if (constructor2 != null)
				PreserveMethod (type, constructor2);
		}

		static MethodDefinition GetIntPtrConstructor (TypeDefinition type)
		{
			if (!type.HasMethods)
				return null;

			foreach (MethodDefinition constructor in type.Methods.Where (m => m.IsConstructor)) {
				if (!constructor.HasParameters)
					continue;

				if (constructor.Parameters.Count != 1 || constructor.Parameters[0].ParameterType.FullName != "System.IntPtr")
					continue;

				return constructor;
			}

			return null;
		}

		static MethodDefinition GetNewIntPtrConstructor (TypeDefinition type)
		{
			if (!type.HasMethods)
				return null;

			foreach (MethodDefinition constructor in type.Methods.Where (m => m.IsConstructor)) {
				if (!constructor.HasParameters)
					continue;

				if (constructor.Parameters.Count != 2 || constructor.Parameters[0].ParameterType.FullName != "System.IntPtr"
					|| constructor.Parameters[1].ParameterType.FullName != "Android.Runtime.JniHandleOwnership")
					continue;

				return constructor;
			}

			return null;
		}

		void PreserveMethod (TypeDefinition type, MethodDefinition method)
		{
			Annotations.AddPreservedMethod (type, method);
		}

		// Given an Assembly Qualified name, return a *Partial* Assembly Qualified Name.
		// Just `Full.Type.Name, Assembly.Name`.  No version, public key token, etc.
		string TypeNameWithoutKey (string assemblyQualifiedName)
		{
			var c   = assemblyQualifiedName.IndexOf (',');
			if (c < 0) {
				return assemblyQualifiedName;
			}
			var t   = assemblyQualifiedName.Substring (0, c);
			var n   = new AssemblyName (assemblyQualifiedName.Substring (c+1));
			return $"{t}, {n.Name}";
		}

		bool CheckInvokerType (TypeDefinition type, string name)
		{
			return
#if ILLINK
				type.GetPartialAssemblyQualifiedName (base.Context)
#else   // ILLINK
				type.GetPartialAssemblyQualifiedName ()
#endif  // ILLINK
				== TypeNameWithoutKey (name);
		}

		void PreserveInterfaceMethods (TypeDefinition type, TypeDefinition invoker)
		{
			foreach (var m in type.Methods.Where (m => !m.IsConstructor)) {
				string methodAndType;
				if (!m.TryGetRegisterMember (out methodAndType))
					continue;

				if (!methodAndType.Contains (":"))
					continue;

				var values = methodAndType.Split (new char [] { ':' }, 2);
				if (!CheckInvokerType (invoker, values [1]))
					continue;

				foreach (var invokerMethod in invoker.Methods.Where (m => !m.IsConstructor)) {
					if (invokerMethod.Name == values [0]) {
						PreserveMethod (invoker, invokerMethod);
						break;
					}
				}
			}
		}

		void PreserveInvoker (TypeDefinition type)
		{
			var invoker = GetInvokerType (type);
			if (invoker == null)
				return;

			PreserveConstructors (type, invoker);
			PreserveIntPtrConstructor (invoker);
			PreserveInterfaceMethods (type, invoker);
			PreserveInterfaces (invoker);
		}

		void PreserveInterfaces (TypeDefinition type)
		{
			if (!type.HasInterfaces)
				return;

			if (!ShouldPreserveInterfaces (type))
				return;

			foreach (var iface in type.Interfaces) {
				var td = iface.InterfaceType.Resolve ();
				if (!td.ImplementsIJavaPeerable (cache))
					continue;
				Annotations.Mark (td);
			}
		}

		// False if [Register(DoNotGenerateAcw=true)] is on the type
		// False if [JniTypeSignature(GenerateJavaPeer=false)] is on the type
		bool ShouldPreserveInterfaces (TypeDefinition type)
		{
			if (!type.HasCustomAttributes)
				return true;

			foreach (var attr in type.CustomAttributes) {
				switch (attr.AttributeType.FullName) {
					case "Android.Runtime.RegisterAttribute":
						foreach (var property in attr.Properties) {
							if (property.Name == "DoNotGenerateAcw") {
								if (property.Argument.Value is bool value && value)
									return false;
								break;
							}
						}
						break;
					case "Java.Interop.JniTypeSignatureAttribute":
						foreach (var property in attr.Properties) {
							if (property.Name == "GenerateJavaPeer") {
								if (property.Argument.Value is bool value && !value)
									return false;
								break;
							}
						}
						break;
					default:
						break;
				}
			}

			return true;
		}

		TypeDefinition GetInvokerType (TypeDefinition type)
		{
			const string suffix = "Invoker";
			string fullname = type.FullName;

			if (type.HasGenericParameters) {
				var pos = fullname.IndexOf ('`');
				if (pos == -1)
					throw new ArgumentException ();

				fullname = fullname.Substring (0, pos) + suffix + fullname.Substring (pos);
			} else
				fullname = fullname + suffix;

			return FindType (type, fullname);
		}

		// Keep a dictionary cache of all types in a module rather than
		// looping through them on every lookup.
		TypeDefinition FindType (TypeDefinition type, string fullname)
		{
			Dictionary<string, TypeDefinition> types;

			if (!module_types.TryGetValue (type.Module, out types)) {
				types = GetTypesInModule (type.Module);
				module_types.Add (type.Module, types);
			}

			TypeDefinition helper;

			if (types.TryGetValue (fullname, out helper))
				return helper;

			return null;
		}

		static Dictionary<string, TypeDefinition> GetTypesInModule (ModuleDefinition module)
		{
			var types = module.Types.ToDictionary (p => p.FullName);

			foreach (var t in module.Types)
				AddNestedTypes (types, t);

			return types;
		}

		static void AddNestedTypes (Dictionary<string, TypeDefinition> types, TypeDefinition type)
		{
			if (!type.HasNestedTypes)
				return;

			foreach (var t in type.NestedTypes) {
				types.Add (t.FullName, t);
				AddNestedTypes (types, t);
			}
		}

		void PreserveConstructors (TypeDefinition type, TypeDefinition helper)
		{
			if (!helper.HasMethods)
				return;

			foreach (MethodDefinition ctor in helper.Methods.Where (m => m.IsConstructor))
				PreserveMethod (type, ctor);
		}

		static bool IsImplementor (TypeDefinition type, IMetadataResolver cache)
		{
			return type.Name.EndsWith ("Implementor", StringComparison.Ordinal) && type.Inherits ("Java.Lang.Object", cache);
		}

		static bool IsUserType (TypeDefinition type)
		{
			return !MonoAndroidHelper.IsFrameworkAssembly (type.Module.Assembly.Name.Name + ".dll");
		}

		void PreserveImplementor (TypeDefinition type)
		{
			if (!type.HasMethods)
				return;

			foreach (MethodDefinition method in type.Methods)
				if (method.Name.EndsWith ("Handler", StringComparison.Ordinal))
					PreserveMethod (type, method);
		}
	}
}
