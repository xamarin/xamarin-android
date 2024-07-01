using System;
using System.IO;
using System.Collections.Generic;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class PrepareAbiItems : AndroidTask
	{
		const string ArmV7a = "armeabi-v7a";
		const string TypeMapBase = "typemaps";
		const string EnvBase = "environment";
		const string CompressedAssembliesBase = "compressed_assemblies";
		const string JniRemappingBase = "jni_remap";
		const string MarshalMethodsBase = "marshal_methods";
		const string PinvokePreserveBase = "pinvoke_preserve";

		public override string TaskPrefix => "PAI";

		[Required]
		public string [] BuildTargetAbis { get; set; }

		[Required]
		public string NativeSourcesDir { get; set; }

		[Required]
		public string Mode { get; set; }

		[Required]
		public bool Debug { get; set; }

		[Required]
		public bool InstantRunEnabled { get; set; }

		[Output]
		public ITaskItem[] AssemblySources { get; set; }

		[Output]
		public ITaskItem[] AssemblyIncludes { get; set; }

		public override bool RunTask ()
		{
			var sources = new List<ITaskItem> ();
			var includes = new List<ITaskItem> ();
			string baseName;

			if (String.Compare ("typemap", Mode, StringComparison.OrdinalIgnoreCase) == 0) {
				baseName = TypeMapBase;
			} else if (String.Compare ("environment", Mode, StringComparison.OrdinalIgnoreCase) == 0) {
				baseName = EnvBase;
			} else if (String.Compare ("compressed", Mode, StringComparison.OrdinalIgnoreCase) == 0) {
				baseName = CompressedAssembliesBase;
			} else if (String.Compare ("jniremap", Mode, StringComparison.OrdinalIgnoreCase) == 0) {
				baseName = JniRemappingBase;
			} else if (String.Compare ("marshal_methods", Mode, StringComparison.OrdinalIgnoreCase) == 0) {
				baseName = MarshalMethodsBase;
			} else if (String.Compare ("runtime_linking", Mode, StringComparison.OrdinalIgnoreCase) == 0) {
				baseName = PinvokePreserveBase;
			} else {
				Log.LogError ($"Unknown mode: {Mode}");
				return false;
			}

			TaskItem item;
			foreach (string abi in BuildTargetAbis) {
				item = new TaskItem (Path.Combine (NativeSourcesDir, $"{baseName}.{abi}.ll"));
				item.SetMetadata ("abi", abi);
				sources.Add (item);
			}

			AssemblySources = sources.ToArray ();
			AssemblyIncludes = includes.ToArray ();
			return !Log.HasLoggedErrors;
		}
	}
}
