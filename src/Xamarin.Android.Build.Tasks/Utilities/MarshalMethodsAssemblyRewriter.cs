#if ENABLE_MARSHAL_METHODS
using System;
using System.Collections.Generic;
using System.IO;

using Java.Interop.Tools.Cecil;
using Mono.Cecil;

namespace Xamarin.Android.Tasks
{
	class MarshalMethodsAssemblyRewriter
	{
		IDictionary<string, MarshalMethodEntry> methods;
		ICollection<AssemblyDefinition> uniqueAssemblies;
		IDictionary <string, HashSet<string>> assemblyPaths;

		public MarshalMethodsAssemblyRewriter (IDictionary<string, MarshalMethodEntry> methods, ICollection<AssemblyDefinition> uniqueAssemblies, IDictionary <string, HashSet<string>> assemblyPaths)
		{
			this.methods = methods ?? throw new ArgumentNullException (nameof (methods));
			this.uniqueAssemblies = uniqueAssemblies ?? throw new ArgumentNullException (nameof (uniqueAssemblies));
			this.assemblyPaths = assemblyPaths ?? throw new ArgumentNullException (nameof (assemblyPaths));
		}

		public void Rewrite (DirectoryAssemblyResolver resolver)
		{
			var unmanagedCallersOnlyAttributes = new Dictionary<AssemblyDefinition, CustomAttribute> ();
			foreach (AssemblyDefinition asm in uniqueAssemblies) {
				unmanagedCallersOnlyAttributes.Add (asm, GetUnmanagedCallersOnlyAttribute (asm, resolver));
			}

			Console.WriteLine ("Adding the [UnmanagedCallersOnly] attribute to native callback methods and removing unneeded fields+methods");
			foreach (MarshalMethodEntry method in methods.Values) {
				Console.WriteLine ($"\t{method.NativeCallback.FullName} (token: 0x{method.NativeCallback.MetadataToken.RID:x})");
				method.NativeCallback.CustomAttributes.Add (unmanagedCallersOnlyAttributes [method.NativeCallback.Module.Assembly]);
				method.Connector.DeclaringType.Methods.Remove (method.Connector);
				method.CallbackField?.DeclaringType.Fields.Remove (method.CallbackField);
			}

			Console.WriteLine ();
			Console.WriteLine ("Rewriting assemblies");

			var newAssemblyPaths = new List<string> ();
			foreach (AssemblyDefinition asm in uniqueAssemblies) {
				foreach (string path in GetAssemblyPaths (asm)) {
					var writerParams = new WriterParameters {
						WriteSymbols = (File.Exists (path + ".mdb") || File.Exists (Path.ChangeExtension (path, ".pdb"))),
					};

					string output = $"{path}.new";
					Console.WriteLine ($"\t{asm.Name} => {output}");
					asm.Write (output, writerParams);
					newAssemblyPaths.Add (output);
				}
			}

			// Replace old versions of the assemblies only after we've finished rewriting without issues, otherwise leave the new
			// versions around.
			foreach (string path in newAssemblyPaths) {
				string target = Path.Combine (Path.GetDirectoryName (path), Path.GetFileNameWithoutExtension (path));
				MoveFile (path, target);

				string source = Path.ChangeExtension (path, ".pdb");
				if (File.Exists (source)) {
					target = Path.ChangeExtension (Path.Combine (Path.GetDirectoryName (source), Path.GetFileNameWithoutExtension (source)), ".pdb");

					MoveFile (source, target);
				}

				source = $"{path}.mdb";
				if (File.Exists (source)) {
					target = Path.ChangeExtension (path, ".mdb");
					MoveFile (source, target);
				}
			}

			Console.WriteLine ();
			Console.WriteLine ("Method tokens:");
			foreach (MarshalMethodEntry method in methods.Values) {
				Console.WriteLine ($"\t{method.NativeCallback.FullName} (token: 0x{method.NativeCallback.MetadataToken.RID:x})");
			}

			void MoveFile (string source, string target)
			{
				Console.WriteLine ($"Moving '{source}' => '{target}'");
				if (File.Exists (target)) {
					File.Delete (target);
				}

				File.Move (source, target);
			}
		}

		ICollection<string> GetAssemblyPaths (AssemblyDefinition asm)
		{
			if (!assemblyPaths.TryGetValue (asm.Name.Name, out HashSet<string> paths)) {
				throw new InvalidOperationException ($"Unable to determine file path for assembly '{asm.Name.Name}'");
			}

			return paths;
		}

		CustomAttribute GetUnmanagedCallersOnlyAttribute (AssemblyDefinition targetAssembly, DirectoryAssemblyResolver resolver)
		{
			AssemblyDefinition asm = resolver.Resolve ("System.Runtime.InteropServices");
			TypeDefinition unmanagedCallersOnlyAttribute = null;
			foreach (ModuleDefinition md in asm.Modules) {
				foreach (ExportedType et in md.ExportedTypes) {
					if (!et.IsForwarder) {
						continue;
					}

					if (String.Compare ("System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute", et.FullName, StringComparison.Ordinal) != 0) {
						continue;
					}

					unmanagedCallersOnlyAttribute = et.Resolve ();
					break;
				}
			}

			MethodDefinition attrConstructor = null;
			foreach (MethodDefinition md in unmanagedCallersOnlyAttribute.Methods) {
				if (!md.IsConstructor) {
					continue;
				}

				attrConstructor = md;
			}

			return new CustomAttribute (targetAssembly.MainModule.ImportReference (attrConstructor));
		}
	}
}
#endif
