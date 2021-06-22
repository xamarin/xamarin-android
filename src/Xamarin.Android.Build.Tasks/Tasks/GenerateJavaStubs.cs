// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

		[Output]
		public string [] GeneratedBinaryTypeMaps { get; set; }

		internal const string AndroidSkipJavaStubGeneration = "AndroidSkipJavaStubGeneration";

		public override bool RunTask ()
		{
			try {
				// We're going to do 3 steps here instead of separate tasks so
				// we can share the list of JLO TypeDefinitions between them
				using (var res = new DirectoryAssemblyResolver (this.CreateTaskLogger (), loadDebugSymbols: true)) {
					Run (res);
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

		void Run (DirectoryAssemblyResolver res)
		{
			PackageNamingPolicy pnp;
			JavaNativeTypeManager.PackageNamingPolicy = Enum.TryParse (PackageNamingPolicy, out pnp) ? pnp : PackageNamingPolicyEnum.LowercaseCrc64;

			foreach (var dir in FrameworkDirectories) {
				if (Directory.Exists (dir.ItemSpec))
					res.SearchDirectories.Add (dir.ItemSpec);
			}

			// Put every assembly we'll need in the resolver
			bool hasExportReference = false;
			bool haveMonoAndroid = false;
			var allTypemapAssemblies = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
			var userAssemblies = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);
			var tokenIdCollection = new NativeRuntimeTokenIdCollection (Log);
			foreach (var assembly in ResolvedAssemblies) {
				bool value;
				string fileName = Path.GetFileName (assembly.ItemSpec);

				if (String.Compare ("Java.Interop.dll", fileName, StringComparison.CurrentCultureIgnoreCase) == 0) {
				    tokenIdCollection.ProcessJavaInterop (assembly.ItemSpec);
				}

				if (bool.TryParse (assembly.GetMetadata (AndroidSkipJavaStubGeneration), out value) && value) {
					Log.LogDebugMessage ($"Skipping Java Stub Generation for {assembly.ItemSpec}");
					continue;
				}

				bool addAssembly = false;
				if (!hasExportReference && String.Compare ("Mono.Android.Export.dll", fileName, StringComparison.OrdinalIgnoreCase) == 0) {
					hasExportReference = true;
					addAssembly = true;
				} else if (!haveMonoAndroid && String.Compare ("Mono.Android.dll", fileName, StringComparison.OrdinalIgnoreCase) == 0) {
					haveMonoAndroid = true;
					addAssembly = true;
					tokenIdCollection.ProcessMonoAndroid (assembly.ItemSpec);
				} else if (MonoAndroidHelper.FrameworkAssembliesToTreatAsUserAssemblies.Contains (fileName)) {
					if (!bool.TryParse (assembly.GetMetadata (AndroidSkipJavaStubGeneration), out value) || !value) {
						string name = Path.GetFileNameWithoutExtension (fileName);
						if (!userAssemblies.ContainsKey (name))
							userAssemblies.Add (name, assembly.ItemSpec);
						addAssembly = true;
					}
				}

				if (addAssembly) {
					allTypemapAssemblies.Add (assembly.ItemSpec);
				}

				res.Load (assembly.ItemSpec);
			}

			// However we only want to look for JLO types in user code for Java stub code generation
			foreach (var asm in ResolvedUserAssemblies) {
				if (bool.TryParse (asm.GetMetadata (AndroidSkipJavaStubGeneration), out bool value) && value) {
					Log.LogDebugMessage ($"Skipping Java Stub Generation for {asm.ItemSpec}");
					continue;
				}
				if (!allTypemapAssemblies.Contains (asm.ItemSpec))
					allTypemapAssemblies.Add (asm.ItemSpec);
				string name = Path.GetFileNameWithoutExtension (asm.ItemSpec);
				if (!userAssemblies.ContainsKey (name))
					userAssemblies.Add (name, asm.ItemSpec);
			}

			// Step 1 - Find all the JLO types
			var cache = new TypeDefinitionCache ();
			var scanner = new JavaTypeScanner (this.CreateTaskLogger (), cache) {
				ErrorOnCustomJavaObject     = ErrorOnCustomJavaObject,
			};

			List<TypeDefinition> allJavaTypes = scanner.GetJavaTypes (allTypemapAssemblies, res);

			// Step 2 - Generate type maps
			//   Type mappings need to use all the assemblies, always.
			WriteTypeMappings (allJavaTypes, tokenIdCollection, cache);

			var javaTypes = new List<TypeDefinition> ();
			foreach (TypeDefinition td in allJavaTypes) {
				if (!userAssemblies.ContainsKey (td.Module.Assembly.Name.Name) || JavaTypeScanner.ShouldSkipJavaCallableWrapperGeneration (td, cache)) {
					continue;
				}

				javaTypes.Add (td);
			}

			// Step 3 - Generate Java stub code
			var success = CreateJavaSources (javaTypes, cache);
			if (!success)
				return;

			// We need to save a map of .NET type -> ACW type for resource file fixups
			var managed = new Dictionary<string, TypeDefinition> (javaTypes.Count, StringComparer.Ordinal);
			var java    = new Dictionary<string, TypeDefinition> (javaTypes.Count, StringComparer.Ordinal);

			var managedConflicts = new Dictionary<string, List<string>> (0, StringComparer.Ordinal);
			var javaConflicts    = new Dictionary<string, List<string>> (0, StringComparer.Ordinal);

			using (var acw_map = MemoryStreamPool.Shared.CreateStreamWriter ()) {
				foreach (TypeDefinition type in javaTypes) {
					string managedKey = type.FullName.Replace ('/', '.');
					string javaKey = JavaNativeTypeManager.ToJniName (type).Replace ('/', '.');

					acw_map.Write (type.GetPartialAssemblyQualifiedName (cache));
					acw_map.Write (';');
					acw_map.Write (javaKey);
					acw_map.WriteLine ();

					TypeDefinition conflict;
					bool hasConflict = false;
					if (managed.TryGetValue (managedKey, out conflict)) {
						if (!managedConflicts.TryGetValue (managedKey, out var list))
							managedConflicts.Add (managedKey, list = new List<string> { conflict.GetPartialAssemblyName (cache) });
						list.Add (type.GetPartialAssemblyName (cache));
						hasConflict = true;
					}
					if (java.TryGetValue (javaKey, out conflict)) {
						if (!javaConflicts.TryGetValue (javaKey, out var list))
							javaConflicts.Add (javaKey, list = new List<string> { conflict.GetAssemblyQualifiedName (cache) });
						list.Add (type.GetAssemblyQualifiedName (cache));
						success = false;
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
				ApplicationLabel = ApplicationLabel ?? PackageName,
				Placeholders = ManifestPlaceholders,
				Resolver = res,
				SdkDir = AndroidSdkDir,
				SdkVersion = AndroidSdkPlatform,
				Debug = Debug,
				MultiDex = MultiDex,
				NeedsInternet = NeedsInternet,
				InstantRunEnabled = InstantRunEnabled
			};
			// Only set manifest.VersionCode if it is not blank.
			// We do not want to override the existing manifest in this case.
			if (!string.IsNullOrEmpty (VersionCode)) {
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
			foreach (var type in javaTypes) {
				if (JavaNativeTypeManager.IsApplication (type, cache) || JavaNativeTypeManager.IsInstrumentation (type, cache)) {
					string javaKey = JavaNativeTypeManager.ToJniName (type).Replace ('/', '.');
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

		bool CreateJavaSources (IEnumerable<TypeDefinition> javaTypes, TypeDefinitionCache cache)
		{
			string outputPath = Path.Combine (OutputDirectory, "src");
			string monoInit = GetMonoInitSource (AndroidSdkPlatform);
			bool hasExportReference = ResolvedAssemblies.Any (assembly => Path.GetFileName (assembly.ItemSpec) == "Mono.Android.Export.dll");
			bool generateOnCreateOverrides = int.Parse (AndroidSdkPlatform) <= 10;

			bool ok = true;
			foreach (var t in javaTypes) {
				if (t.IsInterface) {
					// Interfaces are in typemap but they shouldn't have JCW generated for them
					continue;
				}

				using (var writer = MemoryStreamPool.Shared.CreateStreamWriter ()) {
					try {
						var jti = new JavaCallableWrapperGenerator (t, Log.LogWarning, cache) {
							GenerateOnCreateOverrides = generateOnCreateOverrides,
							ApplicationJavaClass = ApplicationJavaClass,
							MonoRuntimeInitialization = monoInit,
						};

						jti.Generate (writer);
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
								messageArgs: new object [0]
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

		void WriteTypeMappings (List<TypeDefinition> types, NativeRuntimeTokenIdCollection tokenIdCollection, TypeDefinitionCache cache)
		{
			var tmg = new TypeMapGenerator ((string message) => Log.LogDebugMessage (message), SupportedAbis);
			if (!tmg.Generate (Debug, SkipJniAddNativeMethodRegistrationAttributeScan, types, cache, TypemapOutputDirectory, GenerateNativeAssembly, out ApplicationConfigTaskState appConfState))
				throw new XamarinAndroidException (4308, Properties.Resources.XA4308);
			appConfState.TokenIdCollection = tokenIdCollection;
			GeneratedBinaryTypeMaps = tmg.GeneratedBinaryTypeMaps.ToArray ();
			BuildEngine4.RegisterTaskObjectAssemblyLocal (ApplicationConfigTaskState.RegisterTaskObjectKey, appConfState, RegisteredTaskObjectLifetime.Build);
		}
	}
}
