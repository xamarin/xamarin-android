// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;


using Java.Interop.Tools.Cecil;
using Java.Interop.Tools.Diagnostics;
using Java.Interop.Tools.JavaCallableWrappers;
using Java.Interop.Tools.TypeNameMappings;

using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	using PackageNamingPolicyEnum   = PackageNamingPolicy;

	public class GenerateJavaStubs : AndroidTask
	{
		sealed class RunState
		{
			public XAAssemblyResolver Resolver               { get; set; }
			public ICollection<ITaskItem> JavaTypeAssemblies { get; set; }
			public ICollection<ITaskItem> UserAssemblies     { get; set; }
			public InputAssemblySet AssemblySet              { get; set; }
			public bool UseMarshalMethods                    { get; set; }
			public AndroidTargetArch TargetArch              { get; set; } = AndroidTargetArch.None;

			/// <summary>
			/// If `true`, generate code/data that doesn't depend on a specific RID (e.g. ACW maps or JCWs)
			/// To be used once per multi-RID runs.
			/// </summary>
			public bool GenerateRidAgnosticParts             { get; set; }
		}

		public const string MarshalMethodsRegisterTaskKey = ".:!MarshalMethods!:.";

		public override string TaskPrefix => "GJS";

		[Required]
		public ITaskItem[] ResolvedAssemblies { get; set; }

		[Required]
		public ITaskItem[] ResolvedUserAssemblies { get; set; }

		[Required]
		public string AcwMapFile { get; set; }

		[Required]
		public ITaskItem [] FrameworkDirectories { get; set; }

		[Required]
		public string [] SupportedAbis { get; set; }

		[Required]
		public string TypemapOutputDirectory { get; set; }

		[Required]
		public bool GenerateNativeAssembly { get; set; }

		public string IntermediateOutputDirectory { get; set; }
		public bool LinkingEnabled { get; set; }
		public bool HaveMultipleRIDs { get; set; }
		public bool EnableMarshalMethods { get; set; }
		public string ManifestTemplate { get; set; }
		public string[] MergedManifestDocuments { get; set; }

		public bool Debug { get; set; }
		public bool MultiDex { get; set; }
		public string ApplicationLabel { get; set; }
		public string PackageName { get; set; }
		public string VersionName { get; set; }
		public string VersionCode { get; set; }
		public string [] ManifestPlaceholders { get; set; }

		public string AndroidSdkDir { get; set; }

		public string AndroidSdkPlatform { get; set; }
		public string OutputDirectory { get; set; }
		public string MergedAndroidManifestOutput { get; set; }

		public bool EmbedAssemblies { get; set; }
		public bool NeedsInternet   { get; set; }
		public bool InstantRunEnabled { get; set; }

		public bool ErrorOnCustomJavaObject { get; set; }

		public string BundledWearApplicationName { get; set; }

		public string PackageNamingPolicy { get; set; }

		public string ApplicationJavaClass { get; set; }

		public bool SkipJniAddNativeMethodRegistrationAttributeScan { get; set; }

		public string CheckedBuild { get; set; }

		public string SupportedOSPlatformVersion { get; set; }

		public ITaskItem[] Environments { get; set; }

		[Output]
		public string [] GeneratedBinaryTypeMaps { get; set; }

		internal const string AndroidSkipJavaStubGeneration = "AndroidSkipJavaStubGeneration";

		public override bool RunTask ()
		{
			try {
				Run ();
			} catch (XamarinAndroidException e) {
				Log.LogCodedError (string.Format ("XA{0:0000}", e.Code), e.MessageWithoutCode);
				if (MonoAndroidHelper.LogInternalExceptions)
					Log.LogMessage (e.ToString ());
			}

			if (Log.HasLoggedErrors) {
				// Ensure that on a rebuild, we don't *skip* the `_GenerateJavaStubs` target,
				// by ensuring that the target outputs have been deleted.
				Files.DeleteFile (MergedAndroidManifestOutput, Log);
				Files.DeleteFile (AcwMapFile, Log);
			}

			return !Log.HasLoggedErrors;
		}

		XAAssemblyResolver MakeResolver (bool useMarshalMethods)
		{
			var readerParams = new ReaderParameters();
			if (useMarshalMethods) {
				readerParams.ReadWrite = true;
				readerParams.InMemory = true;
			}

			var res = new XAAssemblyResolver (Log, loadDebugSymbols: true, loadReaderParameters: readerParams);
			foreach (var dir in FrameworkDirectories) {
				if (Directory.Exists (dir.ItemSpec)) {
					res.FrameworkSearchDirectories.Add (dir.ItemSpec);
				}
			}

			return res;
		}

		void Run ()
		{
			if (Debug) {
				if (LinkingEnabled) {
					RunDebugWithLinking ();
				} else {
					RunDebugNoLinking ();
				}
				return;
			}

			bool useMarshalMethods = !Debug && EnableMarshalMethods;
			if (LinkingEnabled) {
				RunReleaseWithLinking (useMarshalMethods);
			} else {
				RunReleaseNoLinking (useMarshalMethods);
			}
		}

		// We have one set of assemblies, no RID-specific ones.
		// Typemaps don't use MVIDs or metadata tokens
		void RunDebugNoLinking ()
		{
			const AndroidTargetArch ArchHere = AndroidTargetArch.None;

			LogRunMode ("Debug, no linking");
			XAAssemblyResolver resolver = MakeResolver (useMarshalMethods: false);
			var assemblies = CollectInterestingAssemblies<RidAgnosticInputAssemblySet> (ArchHere, resolver);
			throw new NotImplementedException ();
		}

		// We have as many sets of assemblies as there are RIDs, all assemblies are RID-specific
		// Typemaps don't use MVIDs or metadata tokens
		void RunDebugWithLinking ()
		{
			const AndroidTargetArch ArchHere = AndroidTargetArch.None;

			LogRunMode ("Debug, with linking");
			XAAssemblyResolver resolver = MakeResolver (useMarshalMethods: false);
			var assemblies = CollectInterestingAssemblies<RidSpecificInputAssemblySet> (ArchHere, resolver);
			throw new NotImplementedException ();
		}

		// We have one set of assemblies, no RID-specific ones.
		// Typemaps use MVIDs and metadata tokens
		void RunReleaseNoLinking (bool useMarshalMethods)
		{
			const AndroidTargetArch ArchHere = AndroidTargetArch.None;

			LogRunMode ("Release, no linking");
			XAAssemblyResolver resolver = MakeResolver (useMarshalMethods);
			var assemblies = CollectInterestingAssemblies<RidAgnosticInputAssemblySet> (ArchHere, resolver);
			var state = new RunState {
				UseMarshalMethods = useMarshalMethods,
				AssemblySet = assemblies,
				JavaTypeAssemblies = assemblies.JavaTypeAssemblies,
				UserAssemblies = assemblies.UserAssemblies,
				GenerateRidAgnosticParts = true,
				Resolver = resolver,
				TargetArch = ArchHere,
			};
			DoRun (state, out ApplicationConfigTaskState appConfState);
			RegisterApplicationConfigState (appConfState);
		}

		// We have as many sets of assemblies as there are RIDs, all assemblies are RID-specific
		// Typemaps use MVIDs and metadata tokens, need to process all per-RID assemblies separately
		void RunReleaseWithLinking (bool useMarshalMethods)
		{
			LogRunMode ("Release, with linking");
			XAAssemblyResolver resolver = MakeResolver (useMarshalMethods);
			var assemblies = CollectInterestingAssemblies<RidSpecificInputAssemblySet> (AndroidTargetArch.None, resolver);
			throw new NotImplementedException ();
		}

		void RegisterApplicationConfigState (ApplicationConfigTaskState appConfState)
		{
			BuildEngine4.RegisterTaskObjectAssemblyLocal (ProjectSpecificTaskObjectKey (ApplicationConfigTaskState.RegisterTaskObjectKey), appConfState, RegisteredTaskObjectLifetime.Build);
		}

		void LogRunMode (string mode)
		{
			Log.LogDebugMessage ($"GenerateJavaStubs mode: {mode}");
		}

		T CollectInterestingAssemblies<T> (AndroidTargetArch targetArch, XAAssemblyResolver resolver) where T: InputAssemblySet, new()
		{
			var assemblies = new T ();
			bool hasExportReference = false;
			bool haveMonoAndroid = false;

			foreach (ITaskItem assembly in ResolvedAssemblies) {
				bool value;
				if (bool.TryParse (assembly.GetMetadata (AndroidSkipJavaStubGeneration), out value) && value) {
					Log.LogDebugMessage ($"Skipping Java Stub Generation for {assembly.ItemSpec}");
					continue;
				}

				bool addAssembly = false;
				string fileName = Path.GetFileName (assembly.ItemSpec);
				if (!hasExportReference && String.Compare ("Mono.Android.Export.dll", fileName, StringComparison.OrdinalIgnoreCase) == 0) {
					hasExportReference = true;
					addAssembly = true;
				} else if (!haveMonoAndroid && String.Compare ("Mono.Android.dll", fileName, StringComparison.OrdinalIgnoreCase) == 0) {
					haveMonoAndroid = true;
					addAssembly = true;
				} else if (MonoAndroidHelper.FrameworkAssembliesToTreatAsUserAssemblies.Contains (fileName)) {
					if (!bool.TryParse (assembly.GetMetadata (AndroidSkipJavaStubGeneration), out value) || !value) {
						string name = Path.GetFileNameWithoutExtension (fileName);
						assemblies.AddUserAssembly (assembly);
						addAssembly = true;
					}
				}

				if (addAssembly) {
					assemblies.AddJavaTypeAssembly (assembly);
				}

				resolver.Load (targetArch, assembly.ItemSpec);
			}

			// However we only want to look for JLO types in user code for Java stub code generation
			foreach (ITaskItem asm in ResolvedUserAssemblies) {
				if (bool.TryParse (asm.GetMetadata (AndroidSkipJavaStubGeneration), out bool value) && value) {
					Log.LogDebugMessage ($"Skipping Java Stub Generation for {asm.ItemSpec}");
					continue;
				}

				resolver.Load (targetArch, asm.ItemSpec);
				assemblies.AddJavaTypeAssembly (asm);
				assemblies.AddUserAssembly (asm);
			}
			return assemblies;
		}

		void DoRun (RunState state, out ApplicationConfigTaskState? appConfState)
		{
			PackageNamingPolicy pnp;
			JavaNativeTypeManager.PackageNamingPolicy = Enum.TryParse (PackageNamingPolicy, out pnp) ? pnp : PackageNamingPolicyEnum.LowercaseCrc64;

			// Step 1 - Find all the JLO types
			var cache = new TypeDefinitionCache ();
			var scanner = new XAJavaTypeScanner (Log, cache) {
				ErrorOnCustomJavaObject     = ErrorOnCustomJavaObject,
			};
			List<JavaType> allJavaTypes = scanner.GetJavaTypes (state.JavaTypeAssemblies, state.Resolver);
			var javaTypes = new List<JavaType> ();

			foreach (JavaType jt in allJavaTypes) {
				// Whem marshal methods are in use we do not want to skip non-user assemblies (such as Mono.Android) - we need to generate JCWs for them during
				// application build, unlike in Debug configuration or when marshal methods are disabled, in which case we use JCWs generated during Xamarin.Android
				// build and stored in a jar file.
				if ((!state.UseMarshalMethods && !state.AssemblySet.IsUserAssembly (jt.Type.Module.Assembly.Name.Name)) || JavaTypeScanner.ShouldSkipJavaCallableWrapperGeneration (jt.Type, cache)) {
					continue;
				}
				javaTypes.Add (jt);
			}

			MarshalMethodsClassifier? classifier = null;
			if (state.UseMarshalMethods) {
				classifier = new MarshalMethodsClassifier (cache, state.Resolver, Log);
			}

			// TODO: JCWs don't need to be generated for every RID, but we do need the classifier for the marshal methods
			// rewriter and generator.  Add a mode to only classify marshal methods without generating the files.
			// For now, always generate the JCWs if marshal methods are enabled
			if (state.UseMarshalMethods || state.GenerateRidAgnosticParts) {
				// Step 2 - Generate Java stub code
				bool success = CreateJavaSources (javaTypes, cache, classifier, state.UseMarshalMethods);
				if (!success) {
					appConfState = null;
					return; // TODO: throw? Return `false`?
				}
			}

			if (state.UseMarshalMethods) {
				// We need to parse the environment files supplied by the user to see if they want to use broken exception transitions. This information is needed
				// in order to properly generate wrapper methods in the marshal methods assembly rewriter.
				// We don't care about those generated by us, since they won't contain the `XA_BROKEN_EXCEPTION_TRANSITIONS` variable we look for.
				var environmentParser = new EnvironmentFilesParser ();
				var rewriter = new MarshalMethodsAssemblyRewriter (classifier.MarshalMethods, classifier.Assemblies, Log);
				rewriter.Rewrite (state.Resolver, environmentParser.AreBrokenExceptionTransitionsEnabled (Environments));
			}

			// Step 3 - Generate type maps
			//   Type mappings need to use all the assemblies, always.
			WriteTypeMappings (state.TargetArch, allJavaTypes, cache, out appConfState);

			if (state.GenerateRidAgnosticParts) {
				WriteAcwMaps (javaTypes, cache);

				// Step 4 - Merge [Activity] and friends into AndroidManifest.xml
				UpdateAndroidManifest (state, cache, allJavaTypes);
				CreateAdditionalJavaSources (javaTypes, cache, classifier);
			}

			if (state.UseMarshalMethods) {
				classifier.AddSpecialCaseMethods ();

				Log.LogDebugMessage ($"Number of generated marshal methods: {classifier.MarshalMethods.Count}");

				if (classifier.RejectedMethodCount > 0) {
					Log.LogWarning ($"Number of methods in the project that will be registered dynamically: {classifier.RejectedMethodCount}");
				}

				if (classifier.WrappedMethodCount > 0) {
					// TODO: change to LogWarning once the generator can output code which requires no non-blittable wrappers
					Log.LogDebugMessage ($"Number of methods in the project that need marshal method wrappers: {classifier.WrappedMethodCount}");
				}
			}
		}

		void CreateAdditionalJavaSources (List<JavaType> javaTypes, TypeDefinitionCache cache, MarshalMethodsClassifier? classifier)
		{
			StringWriter regCallsWriter = new StringWriter ();
			regCallsWriter.WriteLine ("\t\t// Application and Instrumentation ACWs must be registered first.");
			foreach (JavaType jt in javaTypes) {
				TypeDefinition type = jt.Type;
				if (JavaNativeTypeManager.IsApplication (type, cache) || JavaNativeTypeManager.IsInstrumentation (type, cache)) {
					if (classifier != null && !classifier.FoundDynamicallyRegisteredMethods (type)) {
						continue;
					}

					string javaKey = JavaNativeTypeManager.ToJniName (type, cache).Replace ('/', '.');
					regCallsWriter.WriteLine ("\t\tmono.android.Runtime.register (\"{0}\", {1}.class, {1}.__md_methods);",
						type.GetAssemblyQualifiedName (cache), javaKey);
				}
			}
			regCallsWriter.Close ();

			var real_app_dir = Path.Combine (OutputDirectory, "src", "mono", "android", "app");
			string applicationTemplateFile = "ApplicationRegistration.java";
			SaveResource (applicationTemplateFile, applicationTemplateFile, real_app_dir,
				template => template.Replace ("// REGISTER_APPLICATION_AND_INSTRUMENTATION_CLASSES_HERE", regCallsWriter.ToString ()));
		}

		void UpdateAndroidManifest (RunState state, TypeDefinitionCache cache, List<JavaType> allJavaTypes)
		{
			var manifest = new ManifestDocument (ManifestTemplate) {
				PackageName       = PackageName,
				VersionName       = VersionName,
				ApplicationLabel  = ApplicationLabel ?? PackageName,
				Placeholders      = ManifestPlaceholders,
				Resolver          = state.Resolver,
				SdkDir            = AndroidSdkDir,
				TargetSdkVersion  = AndroidSdkPlatform,
				MinSdkVersion     = MonoAndroidHelper.ConvertSupportedOSPlatformVersionToApiLevel (SupportedOSPlatformVersion).ToString (),
				Debug             = Debug,
				MultiDex          = MultiDex,
				NeedsInternet     = NeedsInternet,
				InstantRunEnabled = InstantRunEnabled
			};
			// Only set manifest.VersionCode if there is no existing value in AndroidManifest.xml.
			if (manifest.HasVersionCode) {
				Log.LogDebugMessage ($"Using existing versionCode in: {ManifestTemplate}");
			} else if (!string.IsNullOrEmpty (VersionCode)) {
				manifest.VersionCode = VersionCode;
			}

			foreach (ITaskItem assembly in state.UserAssemblies) {
				manifest.Assemblies.Add (Path.GetFileName (assembly.ItemSpec));
			}

			if (!String.IsNullOrWhiteSpace (CheckedBuild)) {
				// We don't validate CheckedBuild value here, this will be done in BuildApk. We just know that if it's
				// on then we need android:debuggable=true and android:extractNativeLibs=true
				manifest.ForceDebuggable = true;
				manifest.ForceExtractNativeLibs = true;
			}

			var additionalProviders = manifest.Merge (Log, cache, allJavaTypes, ApplicationJavaClass, EmbedAssemblies, BundledWearApplicationName, MergedManifestDocuments);

			// Only write the new manifest if it actually changed
			if (manifest.SaveIfChanged (Log, MergedAndroidManifestOutput)) {
				Log.LogDebugMessage ($"Saving: {MergedAndroidManifestOutput}");
			}
		}

		void WriteAcwMaps (List<JavaType> javaTypes, TypeDefinitionCache cache)
		{
			var writer = new AcwMapWriter (Log, AcwMapFile);
			writer.Write (javaTypes, cache);
		}

		AssemblyDefinition LoadAssembly (string path, XAAssemblyResolver? resolver = null)
		{
			string pdbPath = Path.ChangeExtension (path, ".pdb");
			var readerParameters = new ReaderParameters {
				AssemblyResolver                = resolver,
				InMemory                        = false,
				ReadingMode                     = ReadingMode.Immediate,
				ReadSymbols                     = File.Exists (pdbPath),
				ReadWrite                       = false,
			};

			MemoryMappedViewStream? viewStream = null;
			try {
				// Create stream because CreateFromFile(string, ...) uses FileShare.None which is too strict
				using var fileStream = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, false);
				using var mappedFile = MemoryMappedFile.CreateFromFile (
					fileStream, null, fileStream.Length, MemoryMappedFileAccess.Read, HandleInheritability.None, true);
				viewStream = mappedFile.CreateViewStream (0, 0, MemoryMappedFileAccess.Read);

				AssemblyDefinition result = ModuleDefinition.ReadModule (viewStream, readerParameters).Assembly;

				// We transferred the ownership of the viewStream to the collection.
				viewStream = null;

				return result;
			} finally {
				viewStream?.Dispose ();
			}
		}

		bool CreateJavaSources (IEnumerable<JavaType> newJavaTypes, TypeDefinitionCache cache, MarshalMethodsClassifier classifier, bool useMarshalMethods)
		{
			if (useMarshalMethods && classifier == null) {
				throw new ArgumentNullException (nameof (classifier));
			}

			string outputPath = Path.Combine (OutputDirectory, "src");
			string monoInit = GetMonoInitSource (AndroidSdkPlatform);
			bool hasExportReference = ResolvedAssemblies.Any (assembly => Path.GetFileName (assembly.ItemSpec) == "Mono.Android.Export.dll");
			bool generateOnCreateOverrides = int.Parse (AndroidSdkPlatform) <= 10;

			bool ok = true;
			foreach (JavaType jt in newJavaTypes) {
				TypeDefinition t = jt.Type; // JCW generator doesn't care about ABI-specific types or token ids
				if (t.IsInterface) {
					// Interfaces are in typemap but they shouldn't have JCW generated for them
					continue;
				}

				using (var writer = MemoryStreamPool.Shared.CreateStreamWriter ()) {
					try {
						var jti = new JavaCallableWrapperGenerator (t, Log.LogWarning, cache, classifier) {
							GenerateOnCreateOverrides = generateOnCreateOverrides,
							ApplicationJavaClass = ApplicationJavaClass,
							MonoRuntimeInitialization = monoInit,
						};

						jti.Generate (writer);
						if (useMarshalMethods) {
							if (classifier.FoundDynamicallyRegisteredMethods (t)) {
								Log.LogWarning ($"Type '{t.GetAssemblyQualifiedName (cache)}' will register some of its Java override methods dynamically. This may adversely affect runtime performance. See preceding warnings for names of dynamically registered methods.");
							}
						}
						writer.Flush ();

						var path = jti.GetDestinationPath (outputPath);
						Files.CopyIfStreamChanged (writer.BaseStream, path);
						if (jti.HasExport && !hasExportReference)
							Diagnostic.Error (4210, Properties.Resources.XA4210);
					} catch (XamarinAndroidException xae) {
						ok = false;
						Log.LogError (
								subcategory: "",
								errorCode: "XA" + xae.Code,
								helpKeyword: string.Empty,
								file: xae.SourceFile,
								lineNumber: xae.SourceLine,
								columnNumber: 0,
								endLineNumber: 0,
								endColumnNumber: 0,
								message: xae.MessageWithoutCode,
								messageArgs: Array.Empty<object> ()
						);
					} catch (DirectoryNotFoundException ex) {
						ok = false;
						if (OS.IsWindows) {
							Diagnostic.Error (5301, Properties.Resources.XA5301, t.FullName, ex);
						} else {
							Diagnostic.Error (4209, Properties.Resources.XA4209, t.FullName, ex);
						}
					} catch (Exception ex) {
						ok = false;
						Diagnostic.Error (4209, Properties.Resources.XA4209, t.FullName, ex);
					}
				}
			}

			if (useMarshalMethods) {
				BuildEngine4.RegisterTaskObjectAssemblyLocal (ProjectSpecificTaskObjectKey (MarshalMethodsRegisterTaskKey), new MarshalMethodsState (classifier.MarshalMethods), RegisteredTaskObjectLifetime.Build);
			}

			return ok;
		}

		static string GetMonoInitSource (string androidSdkPlatform)
		{
			// Lookup the mono init section from MonoRuntimeProvider:
			// Mono Runtime Initialization {{{
			// }}}
			var builder = new StringBuilder ();
			var runtime = "Bundled";
			var api = "";
			if (int.TryParse (androidSdkPlatform, out int apiLevel) && apiLevel < 21) {
				api = ".20";
			}
			var assembly = Assembly.GetExecutingAssembly ();
			using (var s = assembly.GetManifestResourceStream ($"MonoRuntimeProvider.{runtime}{api}.java"))
			using (var reader = new StreamReader (s)) {
				bool copy = false;
				string line;
				while ((line = reader.ReadLine ()) != null) {
					if (string.CompareOrdinal ("\t\t// Mono Runtime Initialization {{{", line) == 0)
						copy = true;
					if (copy)
						builder.AppendLine (line);
					if (string.CompareOrdinal ("\t\t// }}}", line) == 0)
						break;
				}
			}
			return builder.ToString ();
		}

		string GetResource (string resource)
		{
			using (var stream = GetType ().Assembly.GetManifestResourceStream (resource))
			using (var reader = new StreamReader (stream))
				return reader.ReadToEnd ();
		}

		void SaveResource (string resource, string filename, string destDir, Func<string, string> applyTemplate)
		{
			string template = GetResource (resource);
			template = applyTemplate (template);
			Files.CopyIfStringChanged (template, Path.Combine (destDir, filename));
		}

		void WriteTypeMappings (AndroidTargetArch targetArch, List<JavaType> types, TypeDefinitionCache cache, out ApplicationConfigTaskState appConfState)
		{
			var tmg = new TypeMapGenerator (targetArch, Log, SupportedAbis);
			if (!tmg.Generate (Debug, SkipJniAddNativeMethodRegistrationAttributeScan, types, cache, TypemapOutputDirectory, GenerateNativeAssembly, out appConfState)) {
				throw new XamarinAndroidException (4308, Properties.Resources.XA4308);
			}
			GeneratedBinaryTypeMaps = tmg.GeneratedBinaryTypeMaps.ToArray ();
		}
	}
}
