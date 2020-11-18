using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using Xamarin.Tools.Zip;
using Xamarin.Android.Tools;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class ResolveLibraryProjectImports : AndroidTask
	{
		public override string TaskPrefix => "RLP";

		internal const string AndroidSkipResourceExtraction = "AndroidSkipResourceExtraction";

		[Required]
		public string ImportsDirectory { get; set; }

		[Required]
		public string NativeImportsDirectory { get; set; }

		[Required]
		public string OutputDirectory { get; set; }

		[Required]
		public string OutputImportDirectory { get; set; }

		[Required]
		public ITaskItem[] Assemblies { get; set; }

		public ITaskItem [] AarLibraries { get; set; }

		[Required]
		public string AssemblyIdentityMapFile { get; set; }

		public string CacheFile { get; set; }

		[Required]
		public bool DesignTimeBuild { get; set; }

		[Output]
		public ITaskItem [] Jars { get; set; }

		[Output]
		public ITaskItem [] ResolvedAssetDirectories { get; set; }

		[Output]
		public ITaskItem [] ResolvedResourceDirectories { get; set; }

		[Output]
		public ITaskItem [] ResolvedEnvironmentFiles { get; set; }

		[Output]
		public ITaskItem [] ResolvedResourceDirectoryStamps { get; set; }

		[Output]
		public ITaskItem [] ProguardConfigFiles { get; set; }

		internal const string OriginalFile = "OriginalFile";
		internal const string AndroidSkipResourceProcessing = "AndroidSkipResourceProcessing";
		static readonly string [] knownMetadata = new [] {
			OriginalFile,
			AndroidSkipResourceProcessing
		};

		AssemblyIdentityMap assemblyMap = new AssemblyIdentityMap();

		public ResolveLibraryProjectImports ()
		{
		}

		// Extracts library project contents under e.g. obj/Debug/[__library_projects__/*.jar | res/*/*]
		// Extracts library project contents under e.g. obj/Debug/[lp/*.jar | res/*/*]
		public override bool RunTask ()
		{
			var jars                          = new Dictionary<string, ITaskItem> ();
			var resolvedResourceDirectories   = new List<ITaskItem> ();
			var resolvedAssetDirectories      = new List<ITaskItem> ();
			var resolvedEnvironmentFiles      = new List<ITaskItem> ();
			var proguardConfigFiles           = new List<ITaskItem> ();

			assemblyMap.Load (AssemblyIdentityMapFile);
			try {
				Extract (jars, resolvedResourceDirectories, resolvedAssetDirectories, resolvedEnvironmentFiles, proguardConfigFiles);
			} catch (ZipIOException ex) {
				Log.LogCodedError ("XA1004", ex.Message);
				Log.LogDebugMessage (ex.ToString ());
			}

			Jars                        = jars.Values.ToArray ();
			ResolvedResourceDirectories = resolvedResourceDirectories.ToArray ();
			ResolvedAssetDirectories    = resolvedAssetDirectories.ToArray ();
			ResolvedEnvironmentFiles    = resolvedEnvironmentFiles.ToArray ();
			ProguardConfigFiles         = proguardConfigFiles.ToArray ();

			ResolvedResourceDirectoryStamps = ResolvedResourceDirectories
				.Select (s => new TaskItem (Path.GetFullPath (Path.Combine (s.ItemSpec, "../..")) + ".stamp"))
				.ToArray ();

			foreach (var directory in ResolvedResourceDirectories) {
				Files.SetDirectoryWriteable (directory.ItemSpec);
			}

			foreach (var directory in ResolvedAssetDirectories) {
				Files.SetDirectoryWriteable (directory.ItemSpec);
			}

			if (!string.IsNullOrEmpty (CacheFile)) {
				var document = new XDocument (
					new XDeclaration ("1.0", "UTF-8", null),
					new XElement ("Paths",
						new XElement ("Jars",
							Jars.Select(e => new XElement ("Jar", e))),
						new XElement ("ResolvedResourceDirectories",
							ResolvedResourceDirectories.ToXElements ("ResolvedResourceDirectory", knownMetadata)),
						new XElement ("ResolvedAssetDirectories",
							ResolvedAssetDirectories.ToXElements ("ResolvedAssetDirectory", knownMetadata)),
						new XElement ("ResolvedEnvironmentFiles",
							ResolvedEnvironmentFiles.ToXElements ("ResolvedEnvironmentFile", knownMetadata)),
						new XElement ("ResolvedResourceDirectoryStamps",
							ResolvedResourceDirectoryStamps.ToXElements ("ResolvedResourceDirectoryStamp", knownMetadata)),
						new XElement ("ProguardConfigFiles",
							ProguardConfigFiles.ToXElements ("ProguardConfigFile", knownMetadata))
					));
				document.SaveIfChanged (CacheFile);
			}

			assemblyMap.Save (AssemblyIdentityMapFile);

			Log.LogDebugTaskItems ("  Jars: ", Jars);
			Log.LogDebugTaskItems ("  ResolvedResourceDirectories: ", ResolvedResourceDirectories);
			Log.LogDebugTaskItems ("  ResolvedAssetDirectories: ", ResolvedAssetDirectories);
			Log.LogDebugTaskItems ("  ResolvedEnvironmentFiles: ", ResolvedEnvironmentFiles);
			Log.LogDebugTaskItems ("  ResolvedResourceDirectoryStamps: ", ResolvedResourceDirectoryStamps);
			Log.LogDebugTaskItems ("  ProguardConfigFiles:", ProguardConfigFiles);

			return !Log.HasLoggedErrors;
		}

		// Extracts library project contents under e.g. obj/Debug/[__library_projects__/*.jar | res/*/*]
		// Extracts library project contents under e.g. obj/Debug/[lp/*.jar | res/*/*]
		void Extract (
				IDictionary<string, ITaskItem> jars,
				ICollection<ITaskItem> resolvedResourceDirectories,
				ICollection<ITaskItem> resolvedAssetDirectories,
				ICollection<ITaskItem> resolvedEnvironments,
				ICollection<ITaskItem> proguardConfigFiles)
		{
			// lets "upgrade" the old directory.
			string oldPath = Path.GetFullPath (Path.Combine (OutputImportDirectory, "..", "__library_projects__"));
			if (!OutputImportDirectory.Contains ("__library_projects__") && Directory.Exists (oldPath)) {
				Files.SetDirectoryWriteable (Path.Combine (oldPath, ".."));
				Directory.Delete (oldPath, recursive: true);
			}
			var outdir = Path.GetFullPath (OutputImportDirectory);
			Directory.CreateDirectory (outdir);

			bool skip;
			foreach (var assemblyItem in Assemblies) {
				var assemblyPath = assemblyItem.ItemSpec;
				var fileName = Path.GetFileName (assemblyPath);
				if (MonoAndroidHelper.IsFrameworkAssembly (fileName) &&
						!MonoAndroidHelper.FrameworkEmbeddedJarLookupTargets.Contains (fileName) &&
						!MonoAndroidHelper.FrameworkEmbeddedNativeLibraryAssemblies.Contains (fileName)) {
					Log.LogDebugMessage ($"Skipping framework assembly '{fileName}'.");
					continue;
				}
				if (!File.Exists (assemblyPath)) {
					Log.LogDebugMessage ($"Skipping non-existent dependency '{assemblyPath}'.");
					continue;
				}
				if (bool.TryParse (assemblyItem.GetMetadata (AndroidSkipResourceExtraction), out skip) && skip) {
					Log.LogDebugMessage ("Skipping resource extraction for '{0}' .", assemblyPath);
					continue;
				}
				string assemblyFileName = Path.GetFileName (assemblyPath);
				string assemblyIdentName = assemblyMap.GetLibraryImportDirectoryNameForAssembly (assemblyFileName);
				string outDirForDll = Path.Combine (OutputImportDirectory, assemblyIdentName);
				string importsDir = Path.Combine (outDirForDll, ImportsDirectory);
				string nativeimportsDir = Path.Combine (outDirForDll, NativeImportsDirectory);
				string resDir = Path.Combine (importsDir, "res");
				string assetsDir = Path.Combine (importsDir, "assets");

				// Skip already-extracted resources.
				bool updated = false;
				string assemblyHash = Files.HashFile (assemblyPath);
				string stamp = Path.Combine (outdir, assemblyIdentName + ".stamp");
				string stampHash = File.Exists (stamp) ? File.ReadAllText (stamp) : null;
				if (assemblyHash == stampHash) {
					Log.LogDebugMessage ("Skipped resource lookup for {0}: extracted files are up to date", assemblyPath);
					if (Directory.Exists (importsDir)) {
						foreach (var file in Directory.EnumerateFiles (importsDir, "*.jar", SearchOption.AllDirectories)) {
							AddJar (jars, Path.GetFullPath (file));
						}
					}
					if (Directory.Exists (resDir)) {
						var taskItem = new TaskItem (Path.GetFullPath (resDir), new Dictionary<string, string> {
							{ OriginalFile, assemblyPath },
						});
						if (bool.TryParse (assemblyItem.GetMetadata (AndroidSkipResourceProcessing), out skip) && skip)
							taskItem.SetMetadata (AndroidSkipResourceProcessing, "True");
						resolvedResourceDirectories.Add (taskItem);
					}
					if (Directory.Exists (assetsDir))
						resolvedAssetDirectories.Add (new TaskItem (Path.GetFullPath (assetsDir), new Dictionary<string, string> {
							{ OriginalFile, assemblyPath }
						}));
					foreach (var env in Directory.EnumerateFiles (outDirForDll, "__AndroidEnvironment__*", SearchOption.TopDirectoryOnly)) {
						resolvedEnvironments.Add (new TaskItem (env, new Dictionary<string, string> {
							{ OriginalFile, assemblyPath }
						}));
					}
					continue;
				}

				Log.LogDebugMessage ($"Refreshing {assemblyFileName}");

				using (var pe = new PEReader (File.OpenRead (assemblyPath))) {
					var reader = pe.GetMetadataReader ();
					foreach (var handle in reader.ManifestResources) {
						var resource = reader.GetManifestResource (handle);
						string name = reader.GetString (resource.Name);

						// android environment files
						if (name.StartsWith ("__AndroidEnvironment__", StringComparison.OrdinalIgnoreCase)) {
							var outFile = Path.Combine (outDirForDll, name);
							using (var stream = pe.GetEmbeddedResourceStream (resource)) {
								updated |= Files.CopyIfStreamChanged (stream, outFile);
							}
							resolvedEnvironments.Add (new TaskItem (Path.GetFullPath (outFile), new Dictionary<string, string> {
								{ OriginalFile, assemblyPath }
							}));
						}
						// embedded jars (EmbeddedJar, EmbeddedReferenceJar)
						else if (name.EndsWith (".jar", StringComparison.InvariantCultureIgnoreCase)) {
							using (var stream = pe.GetEmbeddedResourceStream (resource)) {
								AddJar (jars, importsDir, name, assemblyPath);
								updated |= Files.CopyIfStreamChanged (stream, Path.Combine (importsDir, name));
							}
						}
						// embedded native libraries
						else if (name == "__AndroidNativeLibraries__.zip") {
							List<string> files = new List<string> ();
							using (var stream = pe.GetEmbeddedResourceStream (resource))
							using (var zip = Xamarin.Tools.Zip.ZipArchive.Open (stream)) {
								try {
									updated |= Files.ExtractAll (zip, nativeimportsDir, modifyCallback: (entryFullName) => {
										files.Add (Path.GetFullPath (Path.Combine (nativeimportsDir, entryFullName)));
										return entryFullName
											.Replace ("native_library_imports\\", "")
											.Replace ("native_library_imports/", "");
									}, deleteCallback: (fileToDelete) => {
										return !files.Contains (fileToDelete);
									});
								} catch (PathTooLongException ex) {
									Log.LogCodedError ("XA4303", Properties.Resources.XA4303, assemblyPath, ex);
									return;
								} catch (NotSupportedException ex) {
									Log.LogCodedError ("XA4303", Properties.Resources.XA4303, assemblyPath, ex);
									return;
								}
							}
						}
						// embedded AndroidResourceLibrary archive
						else if (name == "__AndroidLibraryProjects__.zip") {
							// temporarily extracted directory will look like:
							//    __library_projects__/[dllname]/[library_project_imports | jlibs]/bin
							using (var stream = pe.GetEmbeddedResourceStream (resource))
							using (var zip = Xamarin.Tools.Zip.ZipArchive.Open (stream)) {
								try {
									updated |= Files.ExtractAll (zip, importsDir, modifyCallback: (entryFullName) => {
										var path = entryFullName
											.Replace ("library_project_imports\\", "")
											.Replace ("library_project_imports/", "");
										if (path.EndsWith (".jar", StringComparison.OrdinalIgnoreCase)) {
											AddJar (jars, importsDir, path, assemblyPath);
										}
										return path;
									}, deleteCallback: (fileToDelete) => {
										return !jars.ContainsKey (fileToDelete);
									});
								} catch (PathTooLongException ex) {
									Log.LogCodedError ("XA4303", Properties.Resources.XA4303, assemblyPath, ex);
									return;
								} catch (NotSupportedException ex) {
									Log.LogCodedError ("XA4303", Properties.Resources.XA4303, assemblyPath, ex);
									return;
								}
							}

							// We used to *copy* the resources to overwrite other resources,
							// which resulted in missing resource issue.
							// Here we replaced copy with use of '-S' option and made it to work.
							if (Directory.Exists (resDir)) {
								var taskItem = new TaskItem (Path.GetFullPath (resDir), new Dictionary<string, string> {
									{ OriginalFile, assemblyPath }
								});
								if (bool.TryParse (assemblyItem.GetMetadata (AndroidSkipResourceProcessing), out skip) && skip)
									taskItem.SetMetadata (AndroidSkipResourceProcessing, "True");
								resolvedResourceDirectories.Add (taskItem);
							}
							if (Directory.Exists (assetsDir)) {
								resolvedAssetDirectories.Add (new TaskItem (Path.GetFullPath (assetsDir), new Dictionary<string, string> {
									{ OriginalFile, assemblyPath }
								}));
							}
						}
					}
				}

				if (Directory.Exists (importsDir)) {
					// Delete unknown files in the top directory of importsDir
					foreach (var file in Directory.EnumerateFiles (importsDir, "*")) {
						var fullPath = Path.GetFullPath (file);
						if (file.StartsWith ("__AndroidEnvironment__", StringComparison.OrdinalIgnoreCase) && !resolvedEnvironments.Any (x => x.ItemSpec == fullPath)) {
							Log.LogDebugMessage ($"Deleting unknown AndroidEnvironment file: {Path.GetFileName (file)}");
							File.Delete (fullPath);
							updated = true;
						} else if (file.EndsWith (".jar", StringComparison.OrdinalIgnoreCase) && !jars.ContainsKey (fullPath)) {
							Log.LogDebugMessage ($"Deleting unknown jar: {Path.GetFileName (file)}");
							File.Delete (fullPath);
							updated = true;
						}
					}
					if (assemblyHash != stampHash) {
						Log.LogDebugMessage ($"Saving hash to {stamp}, changes: {updated}");
						//NOTE: if the hash is different we always want to write the file, but preserve the timestamp if no changes
						WriteAllText (stamp, assemblyHash, preserveTimestamp: !updated);
					}
				}
			}
			foreach (var aarFile in AarLibraries ?? new ITaskItem[0]) {
				if (!File.Exists (aarFile.ItemSpec))
					continue;
				string aarIdentityName = Path.GetFileName (aarFile.ItemSpec);
				aarIdentityName = assemblyMap.GetLibraryImportDirectoryNameForAssembly (aarIdentityName);
				string outDirForDll = Path.Combine (OutputImportDirectory, aarIdentityName);
				string importsDir = Path.Combine (outDirForDll, ImportsDirectory);
				string resDir = Path.Combine (importsDir, "res");
				string assetsDir = Path.Combine (importsDir, "assets");
				string proguardFile = Path.Combine (importsDir, "proguard.txt");

				bool updated = false;
				string aarHash = Files.HashFile (aarFile.ItemSpec);
				string stamp = Path.Combine (outdir, aarIdentityName + ".stamp");
				string stampHash = File.Exists (stamp) ? File.ReadAllText (stamp) : null;
				var aarFullPath = Path.GetFullPath (aarFile.ItemSpec);
				if (aarHash == stampHash) {
					Log.LogDebugMessage ("Skipped {0}: extracted files are up to date", aarFile.ItemSpec);
					if (Directory.Exists (importsDir)) {
						foreach (var file in Directory.EnumerateFiles (importsDir, "*.jar", SearchOption.AllDirectories)) {
							AddJar (jars, Path.GetFullPath (file));
						}
					}
					if (Directory.Exists (resDir)) {
						var skipProcessing = aarFile.GetMetadata (AndroidSkipResourceProcessing);
						if (string.IsNullOrEmpty (skipProcessing)) {
							skipProcessing = "True";
						}
						resolvedResourceDirectories.Add (new TaskItem (Path.GetFullPath (resDir), new Dictionary<string, string> {
							{ OriginalFile, Path.GetFullPath (aarFile.ItemSpec) },
							{ AndroidSkipResourceProcessing, skipProcessing },
						}));
					}
					if (Directory.Exists (assetsDir))
						resolvedAssetDirectories.Add (new TaskItem  (Path.GetFullPath (assetsDir), new Dictionary<string, string> {
							{ OriginalFile, aarFullPath },
						}));
					continue;
				}

				Log.LogDebugMessage ($"Refreshing {aarFile.ItemSpec}");

				// temporarily extracted directory will look like:
				// _lp_/[aarFile]

				using (var zip = MonoAndroidHelper.ReadZipFile (aarFile.ItemSpec)) {
					try {
						updated |= Files.ExtractAll (zip, importsDir, modifyCallback: (entryFullName) => {
							var entryFileName = Path.GetFileName (entryFullName);
							var entryPath = Path.GetDirectoryName (entryFullName);
							if (entryFileName.StartsWith ("internal_impl", StringComparison.InvariantCulture)) {
								var hash = Files.HashString (entryFileName);
								var jar = Path.Combine (entryPath, $"internal_impl-{hash}.jar");
								AddJar (jars, importsDir, jar, aarFullPath);
								return jar;
							}
							if (entryFullName.EndsWith (".jar", StringComparison.OrdinalIgnoreCase)) {
								AddJar (jars, importsDir, entryFullName, aarFullPath);
							} else if (entryFullName.StartsWith (".net/env/", StringComparison.OrdinalIgnoreCase) ||
									entryFullName.StartsWith (".net\\env\\", StringComparison.OrdinalIgnoreCase)) {
								var fullPath = Path.GetFullPath (Path.Combine (importsDir, entryFullName));
								resolvedEnvironments.Add (new TaskItem (fullPath, new Dictionary<string, string> {
									{ OriginalFile, aarFile.ItemSpec }
								}));
							}
							return entryFullName;
						}, deleteCallback: (fileToDelete) => {
							return !jars.ContainsKey (fileToDelete);
						}, skipCallback: Files.ShouldSkipEntryInAar);

						if (Directory.Exists (importsDir) && aarHash != stampHash) {
							Log.LogDebugMessage ($"Saving hash to {stamp}, changes: {updated}");
							//NOTE: if the hash is different we always want to write the file, but preserve the timestamp if no changes
							WriteAllText (stamp, aarHash, preserveTimestamp: !updated);
						}
					} catch (PathTooLongException ex) {
						Log.LogErrorFromException (new PathTooLongException ($"Error extracting resources from \"{aarFile.ItemSpec}\"", ex));
					}
				}
				if (Directory.Exists (resDir)) {
					var skipProcessing = aarFile.GetMetadata (AndroidSkipResourceProcessing);
					if (string.IsNullOrEmpty (skipProcessing)) {
						skipProcessing = "True";
					}
					resolvedResourceDirectories.Add (new TaskItem (Path.GetFullPath (resDir), new Dictionary<string, string> {
						{ OriginalFile, aarFullPath },
						{ AndroidSkipResourceProcessing, skipProcessing },
					}));
				}
				if (Directory.Exists (assetsDir))
					resolvedAssetDirectories.Add (new TaskItem (Path.GetFullPath (assetsDir), new Dictionary<string, string> {
						{ OriginalFile, aarFullPath },
					}));
				if (File.Exists (proguardFile)) {
					proguardConfigFiles.Add (new TaskItem (Path.GetFullPath (proguardFile), new Dictionary<string, string> {
						{ OriginalFile, aarFullPath },
					}));
				}
			}
		}

		static void AddJar (IDictionary<string, ITaskItem> jars, string destination, string path, string originalFile = null)
		{
			var fullPath = Path.GetFullPath (Path.Combine (destination, path));
			AddJar (jars, fullPath, originalFile);
		}

		static void AddJar (IDictionary<string, ITaskItem> jars, string fullPath, string originalFile = null)
		{
			if (!jars.ContainsKey (fullPath)) {
				jars.Add (fullPath, new TaskItem (fullPath, new Dictionary<string, string> {
					{  OriginalFile, originalFile },
				}));
			}
		}

		void WriteAllText (string path, string contents, bool preserveTimestamp)
		{
			if (preserveTimestamp && File.Exists (path)) {
				var timestamp = File.GetLastWriteTimeUtc (path);
				File.WriteAllText (path, contents);
				File.SetLastWriteTimeUtc (path, timestamp);
			} else {
				File.WriteAllText (path, contents);
			}
		}
	}
}
