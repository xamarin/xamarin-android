using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Xamarin.Android.Tasks.LLVMIR;
using Xamarin.Android.Tools;

using CecilMethodDefinition = global::Mono.Cecil.MethodDefinition;
using CecilParameterDefinition = global::Mono.Cecil.ParameterDefinition;

namespace Xamarin.Android.Tasks;

class PreservePinvokesNativeAssemblyGenerator : LlvmIrComposer
{
	// Maps a component name after ridding it of the `lib` prefix and the extension to a "canonical"
	// name of a library, as used in `[DllImport]` attributes.
	readonly Dictionary<string, string> libraryNameMap = new (StringComparer.Ordinal) {
		{ "xa-java-interop",             "java-interop" },
		{ "mono-android.release-static", String.Empty },
		{ "mono-android.release",        String.Empty },
	};

	readonly NativeCodeGenState state;
	readonly ITaskItem[] monoComponents;

	public PreservePinvokesNativeAssemblyGenerator (TaskLoggingHelper log, NativeCodeGenState codeGenState, ITaskItem[] monoComponents)
		: base (log)
	{
		if (codeGenState.PinvokeInfos == null) {
			throw new InvalidOperationException ($"Internal error: {nameof (codeGenState)} `{nameof (codeGenState.PinvokeInfos)}` property is `null`");
		}

		this.state = codeGenState;
		this.monoComponents = monoComponents;
	}

	protected override void Construct (LlvmIrModule module)
	{
		Log.LogDebugMessage ("Constructing p/invoke preserve code");
		List<PinvokeScanner.PinvokeEntryInfo> pinvokeInfos = state.PinvokeInfos!;
		if (pinvokeInfos.Count == 0) {
			// This is a very unlikely scenario, but we will work just fine.  The module that this generator produces will merely result
			// in an empty (but valid) .ll file and an "empty" object file to link into the shared library.
			return;
		}

		Log.LogDebugMessage ("  Looking for enabled native components");
		var componentNames = new List<string> ();
		var nativeComponents = new NativeRuntimeComponents (monoComponents);
		foreach (NativeRuntimeComponents.Archive archiveItem in nativeComponents.KnownArchives) {
			if (!archiveItem.Include) {
				continue;
			}

			Log.LogDebugMessage ($"    {archiveItem.Name}");
			componentNames.Add (archiveItem.Name);
		}

		if (componentNames.Count == 0) {
			Log.LogDebugMessage ("No native framework components are included in the build, not scanning for p/invoke usage");
			return;
		}

		Log.LogDebugMessage ("  Checking discovered p/invokes against the list of components");
		foreach (PinvokeScanner.PinvokeEntryInfo pinfo in pinvokeInfos) {
			Log.LogDebugMessage ($"    p/invoke: {pinfo.EntryName} in {pinfo.LibraryName}");
			if (MustPreserve (pinfo, componentNames)) {
				Log.LogDebugMessage ("      must be preserved");
			} else {
				Log.LogDebugMessage ("      no need to preserve");
			}
		}
	}

	// Returns `true` for all p/invokes that we know are part of our set of components, otherwise returns `false`.
	// Returning `false` merely means that the p/invoke isn't in any of BCL or our code and therefore we shouldn't
	// care.  It doesn't mean the p/invoke will be removed in any way.
	bool MustPreserve (PinvokeScanner.PinvokeEntryInfo pinfo, List<string> components)
	{
		if (String.Compare ("xa-internal-api", pinfo.LibraryName, StringComparison.Ordinal) == 0) {
			return true;
		}

		foreach (string component in components) {
			// The most common pattern for the BCL - file name without extension
			string componentName = Path.GetFileNameWithoutExtension (component);
			if (Matches (pinfo.LibraryName, componentName)) {
				return true;
			}

			// If it starts with `lib`, drop the prefix
			if (componentName.StartsWith ("lib", StringComparison.Ordinal)) {
				if (Matches (pinfo.LibraryName, componentName.Substring (3))) {
					return true;
				}
			}

			// Might require mapping of component name to a canonical one
			if (libraryNameMap.TryGetValue (componentName, out string? mappedComponentName) && !String.IsNullOrEmpty (mappedComponentName)) {
				if (Matches (pinfo.LibraryName, mappedComponentName)) {
					return true;
				}
			}

			// Try full file name, as the last resort
			if (Matches (pinfo.LibraryName, Path.GetFileName (component))) {
				return true;
			}
		}

		return false;

		bool Matches (string libraryName, string componentName)
		{
			return String.Compare (libraryName, componentName, StringComparison.Ordinal) == 0;
		}
	}
}
