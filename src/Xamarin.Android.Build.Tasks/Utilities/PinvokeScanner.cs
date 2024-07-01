using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Android.Build.Tasks;

using Mono.Cecil;

using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks;

class PinvokeScanner
{
	public sealed class PinvokeEntryInfo
	{
		public readonly string LibraryName;
		public readonly string EntryName;

		public PinvokeEntryInfo (MethodDefinition method)
		{
			LibraryName = method.PInvokeInfo.Module.Name;
			EntryName = method.PInvokeInfo.EntryPoint;
		}
	}

	readonly TaskLoggingHelper log;

	public PinvokeScanner (TaskLoggingHelper log)
	{
		this.log = log;
	}

	public List<PinvokeEntryInfo> Scan (AndroidTargetArch targetArch, XAAssemblyResolver resolver, ICollection<ITaskItem> frameworkAssemblies)
	{
		var pinvokes = new List<PinvokeEntryInfo> ();
		var pinvokeCache = new HashSet<string> (StringComparer.Ordinal);

		foreach (ITaskItem fasm in frameworkAssemblies) {
			string asmName = Path.GetFileNameWithoutExtension (fasm.ItemSpec);
			AssemblyDefinition? asmdef = resolver.Resolve (asmName);
			if (asmdef == null) {
				log.LogWarning ($"Failed to resolve assembly '{fasm.ItemSpec}' for target architecture {targetArch}");
				continue;
			}
			Scan (asmdef, pinvokeCache, pinvokes);
		}

		return pinvokes;
	}

	void Scan (AssemblyDefinition assembly, HashSet<string> pinvokeCache, List<PinvokeEntryInfo> pinvokes)
	{
		log.LogDebugMessage ($"Scanning assembly {assembly}");
		foreach (ModuleDefinition module in assembly.Modules) {
			if (!module.HasTypes) {
				continue;
			}

			foreach (TypeDefinition type in module.Types) {
				Scan (type, pinvokeCache, pinvokes);
			}
		}
	}

	void Scan (TypeDefinition type, HashSet<string> pinvokeCache, List<PinvokeEntryInfo> pinvokes)
	{
		if (!type.HasMethods) {
			return;
		}

		log.LogDebugMessage ($"Scanning type '{type}'");
		foreach (MethodDefinition method in type.Methods) {
			if (!method.HasPInvokeInfo) {
				continue;
			}

			var pinfo = new PinvokeEntryInfo (method);
			string key = $"{pinfo.LibraryName}/{pinfo.EntryName}";
			if (pinvokeCache.Contains (key)) {
				continue;
			}

			log.LogDebugMessage ($"  p/invoke method: {pinfo.LibraryName}/{pinfo.EntryName}");
			pinvokeCache.Add (key);
			pinvokes.Add (pinfo);
		}
	}
}
