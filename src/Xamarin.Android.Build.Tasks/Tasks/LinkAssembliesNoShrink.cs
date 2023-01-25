#nullable enable
using Java.Interop.Tools.Cecil;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	/// <summary>
	/// This task is for Debug builds where LinkMode=None, LinkAssemblies is for Release builds
	/// </summary>
	public class LinkAssembliesNoShrink : AndroidTask
	{
		public override string TaskPrefix => "LNS";

		/// <summary>
		/// These are used so we have the full list of SearchDirectories
		/// </summary>
		[Required]
		public ITaskItem [] ResolvedAssemblies { get; set; } = Array.Empty<ITaskItem> ();

		[Required]
		public ITaskItem [] SourceFiles { get; set; } = Array.Empty<ITaskItem> ();

		[Required]
		public ITaskItem [] DestinationFiles { get; set; } = Array.Empty<ITaskItem> ();

		/// <summary>
		/// $(TargetName) would be "AndroidApp1" with no extension
		/// </summary>
		[Required]
		public string TargetName { get; set; } = "";

		public bool UsingAndroidNETSdk { get; set; }

		public bool AddKeepAlives { get; set; }

		public bool UseDesignerAssembly { get; set; }

		public bool Deterministic { get; set; }

		public override bool RunTask ()
		{
			if (SourceFiles.Length != DestinationFiles.Length)
				throw new ArgumentException ("source and destination count mismatch");

			var readerParameters = new ReaderParameters {
				ReadSymbols = true,
			};
			var writerParameters = new WriterParameters {
				DeterministicMvid = Deterministic,
			};

			var logger = this.CreateTaskLogger ();
			using (var resolver = new DirectoryAssemblyResolver (logger, loadDebugSymbols: true, loadReaderParameters: readerParameters)) {
				// Add SearchDirectories with ResolvedAssemblies
				foreach (var assembly in ResolvedAssemblies) {
					var path = Path.GetFullPath (Path.GetDirectoryName (assembly.ItemSpec));
					if (!resolver.SearchDirectories.Contains (path))
						resolver.SearchDirectories.Add (path);
				}

				// Set up the FixAbstractMethodsStep and AddKeepAlivesStep
				var cache = new TypeDefinitionCache ();
				var fixAbstractMethodsStep = new FixAbstractMethodsStep (resolver, cache, Log);
				var addKeepAliveStep = new AddKeepAlivesStep (resolver, cache, Log, UsingAndroidNETSdk);
				var fixLegacyResourceDesignerStep = new FixLegacyResourceDesignerStep (resolver, Log);
				var javaStubHashStep = new MonoDroid.Tuner.CalculateJavaStubHashStep (logger, cache);
				for (int i = 0; i < SourceFiles.Length; i++) {
					var source = SourceFiles [i];
					var destination = DestinationFiles [i];
					var assemblyName = Path.GetFileNameWithoutExtension (source.ItemSpec);

					// In .NET 6+, we can skip the main assembly
					if (UsingAndroidNETSdk && !AddKeepAlives && assemblyName == TargetName) {
						CopyIfChanged (source, destination);
						continue;
					}
					if (fixAbstractMethodsStep.IsProductOrSdkAssembly (assemblyName)) {
						CopyIfChanged (source, destination);
						continue;
					}

					// Check AppDomain usage on any non-Product or Sdk assembly
					AssemblyDefinition? assemblyDefinition = null;
					if (!UsingAndroidNETSdk) {
						assemblyDefinition = resolver.GetAssembly (source.ItemSpec);
						fixAbstractMethodsStep.CheckAppDomainUsage (assemblyDefinition, (string msg) => Log.LogCodedWarning ("XA2000", msg));
					}

					// Only run the following step(s) on "MonoAndroid" assemblies
					if (MonoAndroidHelper.IsMonoAndroidAssembly (source) && !MonoAndroidHelper.IsSharedRuntimeAssembly (source.ItemSpec)) {
						assemblyDefinition ??= resolver.GetAssembly (source.ItemSpec);

						bool save = fixAbstractMethodsStep.FixAbstractMethods (assemblyDefinition);
						if (UseDesignerAssembly)
							save |= fixLegacyResourceDesignerStep.ProcessAssemblyDesigner (assemblyDefinition);
						if (AddKeepAlives)
							save |= addKeepAliveStep.AddKeepAlives (assemblyDefinition);
						javaStubHashStep.Calculate (assemblyDefinition, Path.GetDirectoryName (destination.ItemSpec));
						if (save) {
							Log.LogDebugMessage ($"Saving modified assembly: {destination.ItemSpec}");
							writerParameters.WriteSymbols = assemblyDefinition.MainModule.HasSymbols;
							assemblyDefinition.Write (destination.ItemSpec, writerParameters);
							continue;
						}
					}

					CopyIfChanged (source, destination);
				}
			}

			return !Log.HasLoggedErrors;
		}

		void CopyIfChanged (ITaskItem source, ITaskItem destination)
		{
			if (MonoAndroidHelper.CopyAssemblyAndSymbols (source.ItemSpec, destination.ItemSpec)) {
				Log.LogDebugMessage ($"Copied: {destination.ItemSpec}");
			} else {
				Log.LogDebugMessage ($"Skipped unchanged file: {destination.ItemSpec}");

				// NOTE: We still need to update the timestamp on this file, or this target would run again
				File.SetLastWriteTimeUtc (destination.ItemSpec, DateTime.UtcNow);
			}
		}

		class FixLegacyResourceDesignerStep : MonoDroid.Tuner.FixLegacyResourceDesignerStep
		{
			readonly DirectoryAssemblyResolver resolver;
			readonly TaskLoggingHelper logger;

			public FixLegacyResourceDesignerStep (DirectoryAssemblyResolver resolver, TaskLoggingHelper logger)
			{
				this.resolver = resolver;
				this.logger = logger;
			}

			public override void LogMessage (string message)
			{
				logger.LogDebugMessage ("{0}", message);
			}

			public override AssemblyDefinition Resolve (AssemblyNameReference name)
			{
				return resolver.Resolve (name);
			}
		}

		class FixAbstractMethodsStep : MonoDroid.Tuner.FixAbstractMethodsStep
		{
			readonly DirectoryAssemblyResolver resolver;
			readonly TaskLoggingHelper logger;

			public FixAbstractMethodsStep (DirectoryAssemblyResolver resolver, TypeDefinitionCache cache, TaskLoggingHelper logger)
				: base (cache)
			{
				this.resolver = resolver;
				this.logger = logger;
			}

			protected override AssemblyDefinition GetMonoAndroidAssembly ()
			{
				return resolver.GetAssembly ("Mono.Android.dll");
			}

			public override void LogMessage (string message)
			{
				logger.LogDebugMessage ("{0}", message);
			}
		}

		class AddKeepAlivesStep : MonoDroid.Tuner.AddKeepAlivesStep
		{
			readonly DirectoryAssemblyResolver resolver;
			readonly TaskLoggingHelper logger;
			readonly bool hasSystemPrivateCoreLib;

			public AddKeepAlivesStep (DirectoryAssemblyResolver resolver, TypeDefinitionCache cache, TaskLoggingHelper logger, bool hasSystemPrivateCoreLib)
				: base (cache)
			{
				this.resolver = resolver;
				this.logger = logger;
				this.hasSystemPrivateCoreLib = hasSystemPrivateCoreLib;
			}

			protected override AssemblyDefinition GetCorlibAssembly ()
			{
				return resolver.GetAssembly (hasSystemPrivateCoreLib ? "System.Private.CoreLib.dll" : "mscorlib.dll");
			}

			public override void LogMessage (string message)
			{
				logger.LogDebugMessage ("{0}", message);
			}
		}
	}
}
