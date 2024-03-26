using Microsoft.Build.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xamarin.Android.Tools;
using Xamarin.Build;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	/// <summary>
	/// We used to invoke aapt/aapt2 per library (many times!), this task does the work to generate R.java for libraries without calling aapt/aapt2.
	/// </summary>
	public partial class GenerateLibraryResources : AndroidAsyncTask
	{
		public override string TaskPrefix => "GLR";

		/// <summary>
		/// The main R.txt for the app
		/// </summary>
		[Required]
		public string ResourceSymbolsTextFile { get; set; }

		/// <summary>
		/// The output directory for Java source code, such as: $(IntermediateOutputPath)android\src
		/// </summary>
		[Required]
		public string OutputDirectory { get; set; }

		/// <summary>
		/// The list of R.txt files for each library
		/// </summary>
		public string [] LibraryTextFiles { get; set; }

		/// <summary>
		/// The accompanying manifest file for each library
		/// </summary>
		public string [] ManifestFiles { get; set; }

		string main_r_txt;
		string output_directory;
		Dictionary<string, string> r_txt_mapping;

		public async override System.Threading.Tasks.Task RunTaskAsync ()
		{
			if (LibraryTextFiles == null || LibraryTextFiles.Length == 0)
				return;

			// Load the "main" R.txt file into a dictionary
			main_r_txt = Path.GetFullPath (ResourceSymbolsTextFile);
			r_txt_mapping = new Dictionary<string, string> ();
			using (var reader = File.OpenText (main_r_txt)) {
				foreach (var line in ParseFile (reader)) {
					var key = line [Index.Class] + " " + line [Index.Name];
					r_txt_mapping [key] = line [Index.Value];
				}
			}

			Directory.CreateDirectory (OutputDirectory);
			output_directory = Path.GetFullPath (OutputDirectory);

			var libraries = new Dictionary<string, Package> ();
			for (int i = 0; i < LibraryTextFiles.Length; i++) {
				var libraryTextFile = LibraryTextFiles [i];
				var manifestFile = ManifestFiles [i];
				if (!File.Exists (manifestFile)) {
					LogDebugMessage ($"Skipping, AndroidManifest.xml does not exist: {manifestFile}");
					continue;
				}

				var manifest = AndroidAppManifest.Load (Path.GetFullPath (manifestFile), MonoAndroidHelper.SupportedVersions);
				var packageName = manifest.PackageName;
				if (string.IsNullOrEmpty (packageName)) {
					LogDebugMessage ($"Skipping, AndroidManifest.xml does not have a packageName: {manifestFile}");
					continue;
				}
				if (!libraries.TryGetValue (packageName, out Package library)) {
					libraries.Add (packageName, library = new Package {
						Name = packageName,
					});
				}
				library.TextFiles.Add (Path.GetFullPath (libraryTextFile));
			}
			await this.WhenAll (libraries.Values, GenerateJava);
		}

		/// <summary>
		/// A class that represents the input to generate an `R.java` file for a given package
		/// </summary>
		class Package
		{
			/// <summary>
			/// A list of full paths to R.txt files
			/// </summary>
			public List<string> TextFiles { get; private set; } = new List<string> (1); // Usually one item

			/// <summary>
			/// The package name found in the AndroidManifest.xml file
			/// </summary>
			public string Name { get; set; }
		}

		/// <summary>
		/// There can be multiple AndroidManifest.xml with the same package name.
		/// We must merge the combination of the resource IDs to be generated in a "merged" R.java file.
		/// </summary>
		string [][] LoadValues (Package library)
		{
			var dictionary = new Dictionary<string, string []> ();
			foreach (var r_txt in library.TextFiles) {
				using (var reader = File.OpenText (r_txt)) {
					foreach (var line in ParseFile (reader)) {
						var type = line [Index.Type];
						var clazz = line [Index.Class];
						var name = line [Index.Name];
						var key = clazz + " " + name;
						if (!dictionary.ContainsKey (key)) {
							if (SetValue (key, line, r_txt)) {
								dictionary.Add (key, line);
							} else {
								LogDebugMessage ($"{r_txt}: `{type} {clazz} {name}` value not found");
							}
						}
					}
				}
			}
			return dictionary.Values.ToArray ();
		}

		/// <summary>
		/// Sets the actual value from the app's main R.txt file
		/// </summary>
		/// <param name="key">Combination of `clazz + " " + name`</param>
		/// <param name="line">string[] representing a line of the R.txt file</param>
		/// <param name="r_txt">path to the R.txt file on disk</param>
		/// <returns>true if the value was found</returns>
		bool SetValue (string key, string[] line, string r_txt)
		{
			// If this is the main R.txt file, `line` already contains the value
			if (r_txt == main_r_txt) {
				return true;
			}
			if (r_txt_mapping.TryGetValue (key, out string value)) {
				line [Index.Value] = value;
				return true;
			}
			return false;
		}
		static readonly char [] Delimiter = new [] { ' ' };

		class Index
		{
			public const int Type  = 0;
			public const int Class = 1;
			public const int Name  = 2;
			public const int Value = 3;
		}

		/// <summary>
		/// R.txt is of the format:
		///    int id icon 0x7f0c000a
		///    int[] styleable ViewStubCompat { 0x010100d0, 0x010100f2, 0x010100f3 }
		/// This returns a 4-length string[] of the parts.
		/// </summary>
		IEnumerable<string []> ParseFile (StreamReader reader)
		{
			while (!reader.EndOfStream) {
				var line = reader.ReadLine ();
				var items = line.Split (Delimiter, 4);
				if (items.Length == 4)
					yield return items;
			}
		}
	}
}
