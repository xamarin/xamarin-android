using System;
using System.Collections.Generic;

using Mono.Cecil;

namespace Xamarin.Android.Application.Utilities;

abstract class ManagedTypeResolver
{
	protected HashSet<string> AlreadyWarned { get; } = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
	protected HashSet<string> AlreadyLoaded { get; } = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
	Dictionary<string, string> types = new Dictionary<string, string> (StringComparer.Ordinal);

	protected Dictionary<string, string> AssemblyPaths { get; } = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);
	protected ILogger Log { get; }

	protected ManagedTypeResolver (ILogger log)
	{
		Log = log;
	}

	public string Lookup (string assemblyName, Guid mvid, uint tokenID)
	{
		if (tokenID == 0) {
			return "[name unknown]";
		}

		string? assemblyPath = null;
		if (!AssemblyPaths.TryGetValue (assemblyName, out assemblyPath)) {
			assemblyPath = FindAssembly (assemblyName);
			if (!String.IsNullOrEmpty (assemblyPath)) {
				AssemblyPaths.Add (assemblyName, assemblyPath);
			}
		}

		if (!String.IsNullOrEmpty (assemblyPath)) {
			if (TryLookup (assemblyPath, mvid, tokenID, out string typeName)) {
				return typeName;
			}
		} else if (!AlreadyWarned.Contains (assemblyName)) {
			Log.WarningLine ($"Assembly {assemblyName} cannot be found");
			AlreadyWarned.Add (assemblyName);
		}

		return $"[token id: {tokenID}]";
	}

	protected bool TryLookup (string assemblyPath, Guid mvid, uint tokenID, out string typeName)
	{
		LoadAssembly (assemblyPath);

		if (tokenID == 0) {
			typeName = String.Empty;
			return false;
		}

		string key = GetTypeKey (assemblyPath, mvid, tokenID);
		typeName = String.Empty;
		if (types.TryGetValue (key, out string? name)) {
			typeName = name ?? String.Empty;
			return true;
		}

		if (!AlreadyWarned.Contains (key)) {
			Log.WarningLine ($"Type with token ID {tokenID} ({tokenID:X08}) not found in {assemblyPath}");
			AlreadyWarned.Add (key);
		}

		return false;
	}

	protected abstract AssemblyDefinition? ReadAssembly (string assemblyPath);
	protected abstract string? FindAssembly (string assemblyName);

	void LoadAssembly (string assemblyPath)
	{
		if (AlreadyLoaded.Contains (assemblyPath)) {
			return;
		}

		Log.DebugLine ($"Loading '{assemblyPath}'");
		AssemblyDefinition? asm = ReadAssembly (assemblyPath);
		if (asm == null) {
			Log.WarningLine ($"Failed to read assembly {assemblyPath}");
			return;
		}

		foreach (ModuleDefinition module in asm.Modules) {
			foreach (TypeDefinition type in module.Types) {
				LoadType (type);
			}
		}
		AlreadyLoaded.Add (assemblyPath);

		void LoadType (TypeDefinition type)
		{
			Log.DebugLine ($"  {type.Name} (token as int: {type.MetadataToken.ToUInt32()}; RID: {type.MetadataToken.RID}; type: {type.MetadataToken.TokenType})");
			types.Add (GetTypeKey (assemblyPath, type.Module.Mvid, type.MetadataToken.ToUInt32 ()), type.FullName);
			if (type.NestedTypes.Count == 0) {
				return;
			}

			foreach (TypeDefinition nestedType in type.NestedTypes) {
				LoadType (nestedType);
			}
		}
	}

	string GetTypeKey (string assemblyPath, Guid mvid, uint tokenID)
	{
		return $"{assemblyPath.ToLowerInvariant ()}{mvid}{tokenID}";
	}
}
