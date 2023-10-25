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
		public string ApplicationIcon { get; set; }
		public string ApplicationRoundIcon { get; set; }
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
				bool useMarshalMethods = !Debug && EnableMarshalMethods;
				// We're going to do 3 steps here instead of separate tasks so
				// we can share the list of JLO TypeDefinitions between them
				using (XAAssemblyResolver res = MakeResolver (useMarshalMethods)) {
					Run (res, useMarshalMethods);
				}
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

		void Run (XAAssemblyResolver res, bool useMarshalMethods)
		{
			PackageNamingPolicy pnp;
			JavaNativeTypeManager.PackageNamingPolicy = Enum.TryParse (PackageNamingPolicy, out pnp) ? pnp : PackageNamingPolicyEnum.LowercaseCrc64;

			Dictionary<string, List<ITaskItem>>? abiSpecificAssembliesByPath = null;
			if (useMarshalMethods) {
				abiSpecificAssembliesByPath = new Dictionary<string, List<ITaskItem>> (StringComparer.Ordinal);
			}

			// Put every assembly we'll need in the resolver
			bool hasExportReference = false;
			bool haveMonoAndroid = false;
			var allTypemapAssemblies = new Dictionary<string, ITaskItem> (StringComparer.OrdinalIgnoreCase);
			var userAssemblies = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);

			foreach (var assembly in ResolvedAssemblies) {
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
						if (!userAssemblies.ContainsKey (name))
							userAssemblies.Add (name, assembly.ItemSpec);
						addAssembly = true;
					}
				}

				if (addAssembly) {
					MaybeAddAbiSpecifcAssembly (assembly, fileName);
					if (!allTypemapAssemblies.ContainsKey (assembly.ItemSpec)) {
						allTypemapAssemblies.Add (assembly.ItemSpec, assembly);
					}
				}

				res.Load (MonoAndroidHelper.GetTargetArch (assembly), assembly.ItemSpec);
			}

			// However we only want to look for JLO types in user code for Java stub code generation
			foreach (var asm in ResolvedUserAssemblies) {
				if (bool.TryParse (asm.GetMetadata (AndroidSkipJavaStubGeneration), out bool value) && value) {
					Log.LogDebugMessage ($"Skipping Java Stub Generation for {asm.ItemSpec}");
					continue;
				}
				res.Load (MonoAndroidHelper.GetTargetArch (asm), asm.ItemSpec);
				MaybeAddAbiSpecifcAssembly (asm, Path.GetFileName (asm.ItemSpec));
				if (!allTypemapAssemblies.ContainsKey (asm.ItemSpec)) {
					allTypemapAssemblies.Add (asm.ItemSpec, asm);
				}

				string name = Path.GetFileNameWithoutExtension (asm.ItemSpec);
				if (!userAssemblies.ContainsKey (name))
					userAssemblies.Add (name, asm.ItemSpec);
			}

			// Step 1 - Find all the JLO types
			var cache = new TypeDefinitionCache ();
			var scanner = new XAJavaTypeScanner (Log, cache) {
				ErrorOnCustomJavaObject     = ErrorOnCustomJavaObject,
			};
			List<JavaType> allJavaTypes = scanner.GetJavaTypes (allTypemapAssemblies.Values, res);
			var javaTypes = new List<JavaType> ();

			foreach (JavaType jt in allJavaTypes) {
				// Whem marshal methods are in use we do not want to skip non-user assemblies (such as Mono.Android) - we need to generate JCWs for them during
				// application build, unlike in Debug configuration or when marshal methods are disabled, in which case we use JCWs generated during Xamarin.Android
				// build and stored in a jar file.
				if ((!useMarshalMethods && !userAssemblies.ContainsKey (jt.Type.Module.Assembly.Name.Name)) || JavaTypeScanner.ShouldSkipJavaCallableWrapperGeneration (jt.Type, cache)) {
					continue;
				}
				javaTypes.Add (jt);
			}

			MarshalMethodsClassifier classifier = null;
			if (useMarshalMethods) {
				classifier = new MarshalMethodsClassifier (cache, res, Log);
			}

			// Step 2 - Generate Java stub code
			var success = CreateJavaSources (javaTypes, cache, classifier, useMarshalMethods);
			if (!success)
				return;

			if (useMarshalMethods) {
				// We need to parse the environment files supplied by the user to see if they want to use broken exception transitions. This information is needed
				// in order to properly generate wrapper methods in the marshal methods assembly rewriter.
				// We don't care about those generated by us, since they won't contain the `XA_BROKEN_EXCEPTION_TRANSITIONS` variable we look for.
				var environmentParser = new EnvironmentFilesParser ();

				Dictionary<AssemblyDefinition, string> assemblyPaths = AddMethodsFromAbiSpecificAssemblies (classifier, res, abiSpecificAssembliesByPath);

				var rewriter = new MarshalMethodsAssemblyRewriter (classifier.MarshalMethods, classifier.Assemblies, assemblyPaths, Log);
				rewriter.Rewrite (res, environmentParser.AreBrokenExceptionTransitionsEnabled (Environments));
			}

			// Step 3 - Generate type maps
			//   Type mappings need to use all the assemblies, always.
			WriteTypeMappings (allJavaTypes, cache);

			// We need to save a map of .NET type -> ACW type for resource file fixups
			var managed = new Dictionary<string, TypeDefinition> (javaTypes.Count, StringComparer.Ordinal);
			var java    = new Dictionary<string, TypeDefinition> (javaTypes.Count, StringComparer.Ordinal);

			var managedConflicts = new Dictionary<string, List<string>> (0, StringComparer.Ordinal);
			var javaConflicts    = new Dictionary<string, List<string>> (0, StringComparer.Ordinal);

			using (var acw_map = MemoryStreamPool.Shared.CreateStreamWriter ()) {
				foreach (JavaType jt in javaTypes) {
					TypeDefinition type = jt.Type;
					string managedKey = type.FullName.Replace ('/', '.');
					string javaKey = JavaNativeTypeManager.ToJniName (type, cache).Replace ('/', '.');

					acw_map.Write (type.GetPartialAssemblyQualifiedName (cache));
					acw_map.Write (';');
					acw_map.Write (javaKey);
					acw_map.WriteLine ();

					TypeDefinition conflict;
					bool hasConflict = false;
					if (managed.TryGetValue (managedKey, out conflict)) {
						if (!conflict.Module.Name.Equals (type.Module.Name)) {
							if (!managedConflicts.TryGetValue (managedKey, out var list))
								managedConflicts.Add (managedKey, list = new List<string> { conflict.GetPartialAssemblyName (cache) });
							list.Add (type.GetPartialAssemblyName (cache));
						}
						hasConflict = true;
					}
					if (java.TryGetValue (javaKey, out conflict)) {
						if (!conflict.Module.Name.Equals (type.Module.Name)) {
							if (!javaConflicts.TryGetValue (javaKey, out var list))
								javaConflicts.Add (javaKey, list = new List<string> { conflict.GetAssemblyQualifiedName (cache) });
							list.Add (type.GetAssemblyQualifiedName (cache));
							success = false;
						}
						hasConflict = true;
					}
					if (!hasConflict) {
						managed.Add (managedKey, type);
						java.Add (javaKey, type);

						acw_map.Write (managedKey);
						acw_map.Write (';');
						acw_map.Write (javaKey);
						acw_map.WriteLine ();

						acw_map.Write (JavaNativeTypeManager.ToCompatJniName (type, cache).Replace ('/', '.'));
						acw_map.Write (';');
						acw_map.Write (javaKey);
						acw_map.WriteLine ();
					}
				}

				acw_map.Flush ();
				Files.CopyIfStreamChanged (acw_map.BaseStream, AcwMapFile);
			}

			foreach (var kvp in managedConflicts) {
				Log.LogCodedWarning ("XA4214", Properties.Resources.XA4214, kvp.Key, string.Join (", ", kvp.Value));
				Log.LogCodedWarning ("XA4214", Properties.Resources.XA4214_Result, kvp.Key, kvp.Value [0]);
			}

			foreach (var kvp in javaConflicts) {
				Log.LogCodedError ("XA4215", Properties.Resources.XA4215, kvp.Key);
				foreach (var typeName in kvp.Value)
					Log.LogCodedError ("XA4215", Properties.Resources.XA4215_Details, kvp.Key, typeName);
			}

			// Step 3 - Merge [Activity] and friends into AndroidManifest.xml
			var manifest = new ManifestDocument (ManifestTemplate) {
				PackageName = PackageName,
				VersionName = VersionName,
				ApplicationIcon = ApplicationIcon,
				ApplicationRoundIcon = ApplicationRoundIcon,
				ApplicationLabel = ApplicationLabel ?? PackageName,
				Placeholders = ManifestPlaceholders,
				Resolver = res,
				SdkDir = AndroidSdkDir,
				TargetSdkVersion = AndroidSdkPlatform,
				MinSdkVersion = MonoAndroidHelper.ConvertSupportedOSPlatformVersionToApiLevel (SupportedOSPlatformVersion).ToString (),
				Debug = Debug,
				MultiDex = MultiDex,
				NeedsInternet = NeedsInternet,
				InstantRunEnabled = InstantRunEnabled
			};
			// Only set manifest.VersionCode if there is no existing value in AndroidManifest.xml.
			if (manifest.HasVersionCode) {
				Log.LogDebugMessage ($"Using existing versionCode in: {ManifestTemplate}");
			} else if (!string.IsNullOrEmpty (VersionCode)) {
				manifest.VersionCode = VersionCode;
			}
			manifest.Assemblies.AddRange (userAssemblies.Values);

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

			// Create additional runtime provider java sources.
			string providerTemplateFile = "MonoRuntimeProvider.Bundled.java";
			string providerTemplate = GetResource (providerTemplateFile);

			foreach (var provider in additionalProviders) {
				var contents = providerTemplate.Replace ("MonoRuntimeProvider", provider);
				var real_provider = Path.Combine (OutputDirectory, "src", "mono", provider + ".java");
				Files.CopyIfStringChanged (contents, real_provider);
			}

			// Create additional application java sources.
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

			if (useMarshalMethods) {
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

			void MaybeAddAbiSpecifcAssembly (ITaskItem assembly, string fileName)
			{
				if (abiSpecificAssembliesByPath == null) {
					return;
				}

				string? abi = assembly.GetMetadata ("Abi");
				if (!String.IsNullOrEmpty (abi)) {
					if (!abiSpecificAssembliesByPath.TryGetValue (fileName, out List<ITaskItem>? items)) {
						items = new List<ITaskItem> ();
						abiSpecificAssembliesByPath.Add (fileName, items);
					}

					items.Add (assembly);
				}
			}
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

		void WriteTypeMappings (List<JavaType> types, TypeDefinitionCache cache)
		{
			var tmg = new TypeMapGenerator ((string message) => Log.LogDebugMessage (message), SupportedAbis);
			if (!tmg.Generate (Debug, SkipJniAddNativeMethodRegistrationAttributeScan, types, cache, TypemapOutputDirectory, GenerateNativeAssembly, out ApplicationConfigTaskState appConfState)) {
				throw new XamarinAndroidException (4308, Properties.Resources.XA4308);
			}
			GeneratedBinaryTypeMaps = tmg.GeneratedBinaryTypeMaps.ToArray ();
			BuildEngine4.RegisterTaskObjectAssemblyLocal (ProjectSpecificTaskObjectKey (ApplicationConfigTaskState.RegisterTaskObjectKey), appConfState, RegisteredTaskObjectLifetime.Build);
		}

		/// <summary>
		/// <para>
		/// Classifier will see only unique assemblies, since that's what's processed by the JI type scanner - even though some assemblies may have
		/// abi-specific features (e.g. inlined `IntPtr.Size` or processor-specific intrinsics), the **types** and **methods** will all be the same and, thus,
		/// there's no point in scanning all of the additional copies of the same assembly.
		/// </para>
		/// <para>
		/// This, however, doesn't work for the rewriter which needs to rewrite all of the copies so that they all have the same generated wrappers.  In
		/// order to do that, we need to go over the list of assemblies found by the classifier, see if they are abi-specific ones and then add all the
		/// marshal methods from the abi-specific assembly copies, so that the rewriter can easily rewrite them all.
		/// </para>
		/// <para>
		/// This method returns a dictionary matching `AssemblyDefinition` instances to the path on disk to the assembly file they were loaded from.  It is necessary
		/// because <see cref="LoadAssembly"/> uses a stream to load the data, in order to avoid later sharing violation issues when writing the assemblies.  Path
		/// information is required by <see cref="MarshalMethodsAssemblyRewriter"/> to be available for each <see cref="MarshalMethodEntry"/>
		/// </para>
		/// </summary>
		Dictionary<AssemblyDefinition, string> AddMethodsFromAbiSpecificAssemblies (MarshalMethodsClassifier classifier, XAAssemblyResolver resolver, Dictionary<string, List<ITaskItem>> abiSpecificAssemblies)
		{
			IDictionary<string, IList<MarshalMethodEntry>> marshalMethods = classifier.MarshalMethods;
			ICollection<AssemblyDefinition> assemblies = classifier.Assemblies;
			var newAssemblies = new List<AssemblyDefinition> ();
			var assemblyPaths = new Dictionary<AssemblyDefinition, string> ();

			foreach (AssemblyDefinition asmdef in assemblies) {
				string fileName = Path.GetFileName (asmdef.MainModule.FileName);
				if (!abiSpecificAssemblies.TryGetValue (fileName, out List<ITaskItem>? abiAssemblyItems)) {
					continue;
				}

				List<MarshalMethodEntry> assemblyMarshalMethods = FindMarshalMethodsForAssembly (marshalMethods, asmdef);;
				Log.LogDebugMessage ($"Assembly {fileName} is ABI-specific");
				foreach (ITaskItem abiAssemblyItem in abiAssemblyItems) {
					if (String.Compare (abiAssemblyItem.ItemSpec, asmdef.MainModule.FileName, StringComparison.Ordinal) == 0) {
						continue;
					}

					Log.LogDebugMessage ($"Looking for matching mashal methods in {abiAssemblyItem.ItemSpec}");
					FindMatchingMethodsInAssembly (abiAssemblyItem, classifier, assemblyMarshalMethods, resolver, newAssemblies, assemblyPaths);
				}
			}

			if (newAssemblies.Count > 0) {
				foreach (AssemblyDefinition asmdef in newAssemblies) {
					assemblies.Add (asmdef);
				}
			}

			return assemblyPaths;
		}

		List<MarshalMethodEntry> FindMarshalMethodsForAssembly (IDictionary<string, IList<MarshalMethodEntry>> marshalMethods, AssemblyDefinition asm)
		{
			var seenNativeCallbacks = new HashSet<MethodDefinition> ();
			var assemblyMarshalMethods = new List<MarshalMethodEntry> ();

			foreach (var kvp in marshalMethods) {
				foreach (MarshalMethodEntry method in kvp.Value) {
					if (method.NativeCallback.Module.Assembly != asm) {
						continue;
					}

					// More than one overriden method can use the same native callback method, we're interested only in unique native
					// callbacks, since that's what gets rewritten.
					if (seenNativeCallbacks.Contains (method.NativeCallback)) {
						continue;
					}

					seenNativeCallbacks.Add (method.NativeCallback);
					assemblyMarshalMethods.Add (method);
				}
			}

			return assemblyMarshalMethods;
		}

		void FindMatchingMethodsInAssembly (ITaskItem assemblyItem, MarshalMethodsClassifier classifier, List<MarshalMethodEntry> assemblyMarshalMethods, XAAssemblyResolver resolver, List<AssemblyDefinition> newAssemblies, Dictionary<AssemblyDefinition, string> assemblyPaths)
		{
			AssemblyDefinition asm = LoadAssembly (assemblyItem.ItemSpec, resolver);
			newAssemblies.Add (asm);
			assemblyPaths.Add (asm, assemblyItem.ItemSpec);

			foreach (MarshalMethodEntry methodEntry in assemblyMarshalMethods) {
				TypeDefinition wantedType = methodEntry.NativeCallback.DeclaringType;
				TypeDefinition? type = asm.MainModule.FindType (wantedType.FullName);
				if (type == null) {
					throw new InvalidOperationException ($"Internal error: type '{wantedType.FullName}' not found in assembly '{assemblyItem.ItemSpec}', a linker error?");
				}

				if (type.MetadataToken != wantedType.MetadataToken) {
					throw new InvalidOperationException ($"Internal error: type '{type.FullName}' in assembly '{assemblyItem.ItemSpec}' has a different token ID than the original type");
				}

				FindMatchingMethodInType (methodEntry, type, classifier);
			}
		}

		void FindMatchingMethodInType (MarshalMethodEntry methodEntry, TypeDefinition type, MarshalMethodsClassifier classifier)
		{
			string callbackName = methodEntry.NativeCallback.FullName;

			foreach (MethodDefinition typeNativeCallbackMethod in type.Methods) {
				if (String.Compare (typeNativeCallbackMethod.FullName, callbackName, StringComparison.Ordinal) != 0) {
					continue;
				}

				if (typeNativeCallbackMethod.Parameters.Count != methodEntry.NativeCallback.Parameters.Count) {
					continue;
				}

				if (typeNativeCallbackMethod.MetadataToken != methodEntry.NativeCallback.MetadataToken) {
					throw new InvalidOperationException ($"Internal error: tokens don't match for '{typeNativeCallbackMethod.FullName}'");
				}

				bool allMatch = true;
				for (int i = 0; i < typeNativeCallbackMethod.Parameters.Count; i++) {
					if (String.Compare (typeNativeCallbackMethod.Parameters[i].ParameterType.FullName, methodEntry.NativeCallback.Parameters[i].ParameterType.FullName, StringComparison.Ordinal) != 0) {
						allMatch = false;
						break;
					}
				}

				if (!allMatch) {
					continue;
				}

				Log.LogDebugMessage ($"Found match for '{typeNativeCallbackMethod.FullName}' in {type.Module.FileName}");
				string methodKey = classifier.GetStoreMethodKey (methodEntry);
				classifier.MarshalMethods[methodKey].Add (new MarshalMethodEntry (methodEntry, typeNativeCallbackMethod));
			}
		}
	}
}
