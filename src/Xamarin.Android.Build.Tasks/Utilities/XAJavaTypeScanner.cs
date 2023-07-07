using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
//using System.IO.MemoryMappedFiles;

using Java.Interop.Tools.Cecil;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks;

class JavaType
{
	public readonly TypeDefinition Type;
	public readonly IDictionary<AndroidTargetArch, TypeDefinition>? PerAbiTypes;
	public bool IsABiSpecific { get; }

	public JavaType (TypeDefinition type, IDictionary<AndroidTargetArch, TypeDefinition>? perAbiTypes)
	{
		Type = type;
		if (perAbiTypes != null) {
			PerAbiTypes = new ReadOnlyDictionary<AndroidTargetArch, TypeDefinition> (perAbiTypes);
			IsABiSpecific = perAbiTypes.Count > 1 || (perAbiTypes.Count == 1 && !perAbiTypes.ContainsKey (AndroidTargetArch.None));
		}
	}
}

class XAJavaTypeScanner
{
	sealed class TypeData
	{
		public readonly TypeDefinition FirstType;
		public readonly Dictionary<AndroidTargetArch, TypeDefinition> PerAbi;

		public bool IsAbiSpecific => !PerAbi.ContainsKey (AndroidTargetArch.None);

		public TypeData (TypeDefinition firstType)
		{
			FirstType = firstType;
			PerAbi = new Dictionary<AndroidTargetArch, TypeDefinition> ();
		}
	}

	public bool ErrorOnCustomJavaObject { get; set; }

	TaskLoggingHelper log;
	TypeDefinitionCache cache;

	public XAJavaTypeScanner (TaskLoggingHelper log, TypeDefinitionCache cache)
	{
		this.log = log;
		this.cache = cache;
	}

	public List<JavaType> GetJavaTypes (ICollection<ITaskItem> inputAssemblies, DirectoryAssemblyResolver resolver)
	{
		var types = new Dictionary<string, TypeData> (StringComparer.Ordinal);
		var stopwatch = new Stopwatch ();
		foreach (ITaskItem asmItem in inputAssemblies) {
			AndroidTargetArch arch = GetTargetArch (asmItem);

			stopwatch.Start ();
			AssemblyDefinition asmdef = LoadAssembly (asmItem.ItemSpec, resolver);
			stopwatch.Stop ();
			log.LogMessage ($"Load of assembly '{asmItem.ItemSpec}', elapsed: {stopwatch.Elapsed}");
			stopwatch.Reset ();

			stopwatch.Start ();
			foreach (ModuleDefinition md in asmdef.Modules) {
				foreach (TypeDefinition td in md.Types) {
					AddJavaType (td, types, arch);
				}
			}
			stopwatch.Stop ();
			log.LogMessage ($"Add all types from assembly '{asmItem.ItemSpec}', elapsed: {stopwatch.Elapsed}");
			stopwatch.Reset ();
		}

		var ret = new List<JavaType> ();
		foreach (var kvp in types) {
			ret.Add (new JavaType (kvp.Value.FirstType, kvp.Value.IsAbiSpecific ? kvp.Value.PerAbi : null));
		}

		return ret;
	}

	void AddJavaType (TypeDefinition type, Dictionary<string, TypeData> types, AndroidTargetArch arch)
	{
		if (type.IsSubclassOf ("Java.Lang.Object", cache) || type.IsSubclassOf ("Java.Lang.Throwable", cache) || (type.IsInterface && type.ImplementsInterface ("Java.Interop.IJavaPeerable", cache))) {
			// For subclasses of e.g. Android.App.Activity.
			string typeName = type.GetPartialAssemblyQualifiedName (cache);
			if (!types.TryGetValue (typeName, out TypeData typeData)) {
				typeData = new TypeData (type);
				types.Add (typeName, typeData);
			}

			if (typeData.PerAbi.ContainsKey (AndroidTargetArch.None)) {
				if (arch == AndroidTargetArch.None) {
					throw new InvalidOperationException ($"Duplicate type '{type.FullName}' in assembly {type.Module.FileName}");
				}

				throw new InvalidOperationException ($"Previously added type '{type.FullName}' was in ABI-agnostic assembly, new one comes from ABI {arch} assembly");
			}

			if (typeData.PerAbi.ContainsKey (arch)) {
				throw new InvalidOperationException ($"Duplicate type '{type.FullName}' in assembly {type.Module.FileName}, for ABI {arch}");
			}

			typeData.PerAbi.Add (arch, type);
		} else if (type.IsClass && !type.IsSubclassOf ("System.Exception", cache) && type.ImplementsInterface ("Android.Runtime.IJavaObject", cache)) {
			string message = $"XA4212: Type `{type.FullName}` implements `Android.Runtime.IJavaObject` but does not inherit `Java.Lang.Object` or `Java.Lang.Throwable`. This is not supported.";

			if (ErrorOnCustomJavaObject) {
				log.LogError (message);
			} else {
				log.LogWarning (message);
			}
			return;
		}

		if (!type.HasNestedTypes) {
			return;
		}

		foreach (TypeDefinition nested in type.NestedTypes) {
			AddJavaType (nested, types, arch);
		}
	}

	AndroidTargetArch GetTargetArch (ITaskItem asmItem)
	{
		string? abi = asmItem.GetMetadata ("Abi");
		if (String.IsNullOrEmpty (abi)) {
			return AndroidTargetArch.None;
		}

		return abi switch {
			"armeabi-v7a" => AndroidTargetArch.Arm,
			"arm64-v8a"   => AndroidTargetArch.Arm64,
			"x86"         => AndroidTargetArch.X86,
			"x86_64"      => AndroidTargetArch.X86_64,
			_             => throw new NotSupportedException ($"Unsupported ABI '{abi}' for assembly {asmItem.ItemSpec}")
		};
	}

	AssemblyDefinition LoadAssembly (string path, DirectoryAssemblyResolver resolver)
	{
		string pdbPath = Path.ChangeExtension (path, ".pdb");
		var readerParameters = new ReaderParameters {
			AssemblyResolver = resolver,
			InMemory         = true,
			ReadingMode      = ReadingMode.Immediate,
			ReadSymbols      = File.Exists (pdbPath),
			ReadWrite        = false,
		};

		try {
			return AssemblyDefinition.ReadAssembly (path, readerParameters);
		} catch (Exception ex) {
			throw new InvalidOperationException ($"Failed to load assembly: {path}", ex);
		}
	}
}
