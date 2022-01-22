// 
// AndroidUpdateResDir.cs
//  
// Author:
//       Michael Hutchinson <mhutchinson@novell.com>
// 
// Copyright (c) 2010 Novell, Inc. (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Text;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class AndroidComputeResPaths : AndroidTask
	{
		public override string TaskPrefix => "CRP";

		[Required]
		public ITaskItem[] ResourceFiles { get; set; }
		
		[Required]
		public string IntermediateDir { get; set; }
		
		public string Prefixes { get; set; }

		public bool LowercaseFilenames { get; set; }

		public string ProjectDir { get; set; }

		public string AndroidLibraryFlatFilesDirectory { get; set; }
		
		[Output]
		public ITaskItem[] IntermediateFiles { get; set; }

		[Output]
		public ITaskItem [] ResolvedResourceFiles { get; set; }

		public override bool RunTask ()
		{
			var intermediateFiles = new Dictionary<string, ITaskItem> (ResourceFiles.Length, StringComparer.OrdinalIgnoreCase);
			var resolvedFiles = new List<ITaskItem> (ResourceFiles.Length);

			string[] prefixes = Prefixes != null ? Prefixes.Split (';') : null;
			if (prefixes != null) {
				for (int i = 0; i < prefixes.Length; i++) {
					string p = prefixes [i];
					char c = p [p.Length - 1];
					if (c != '\\' && c != '/')
						prefixes [i] = p + Path.DirectorySeparatorChar;
				}
			}

			var nameCaseMap = new Dictionary<string, string> (ResourceFiles.Length, StringComparer.Ordinal);

			for (int i = 0; i < ResourceFiles.Length; i++) {
				var item = ResourceFiles [i];

				if (Directory.Exists (item.ItemSpec))
					continue;
				//compute the target path
				string rel;
				var logicalName = item.GetMetadata ("LogicalName").Replace ('\\', Path.DirectorySeparatorChar);
				if (item.GetMetadata ("IsWearApplicationResource") == "True") {
					rel = item.ItemSpec.Substring (IntermediateDir.Length);
				} else if (!string.IsNullOrEmpty (logicalName)) {
					rel = logicalName;
				} else {
					rel = item.GetMetadata ("Link").Replace ('\\', Path.DirectorySeparatorChar);
					if (string.IsNullOrEmpty (rel)) {
						rel = item.GetMetadata ("Identity");
						if (!string.IsNullOrEmpty (ProjectDir)) {
							var fullRelPath = Path.GetFullPath (rel).Normalize (NormalizationForm.FormC);
							var fullProjectPath = Path.GetFullPath (ProjectDir).Normalize (NormalizationForm.FormC);
							if (fullRelPath.StartsWith (fullProjectPath, StringComparison.OrdinalIgnoreCase)) {
								rel = fullRelPath.Replace (fullProjectPath, string.Empty);
							}
						}
					}
				}

				if (Path.IsPathRooted (rel)) {
					var root = Path.GetPathRoot (rel);
					rel = rel.Substring (root.Length);
				}

				if (prefixes != null) {
					foreach (var p in prefixes) {
						if (rel.StartsWith (p, StringComparison.OrdinalIgnoreCase))
							rel = rel.Substring (p.Length);
					}
				}

				string baseFileName = LowercaseFilenames ? rel.ToLowerInvariant () : rel;
				if (Aapt2.IsInvalidFilename (baseFileName)) {
					Log.LogDebugMessage ($"Invalid filename, ignoring: {baseFileName}");
					continue;
				}
				if (Path.GetExtension (baseFileName) == ".axml")
					baseFileName = Path.ChangeExtension (baseFileName, ".xml");
				if (baseFileName != rel) {
					nameCaseMap [baseFileName] = rel;
				}
				string dest = Path.GetFullPath (Path.Combine (IntermediateDir, baseFileName));
				string intermediateDirFullPath = Path.GetFullPath (IntermediateDir);
				// if the path ends up "outside" of our target intermediate directory, just use the filename
				if (String.Compare (intermediateDirFullPath, 0, dest, 0, intermediateDirFullPath.Length, StringComparison.OrdinalIgnoreCase) != 0) {
					dest = Path.GetFullPath (Path.Combine (IntermediateDir, Path.GetFileName (baseFileName)));
				}
				if (!File.Exists (item.ItemSpec)) {
					Log.LogCodedError ("XA2001", file: item.ItemSpec, lineNumber: 0, message: Properties.Resources.XA2001, item.ItemSpec);
					continue;
				}
				if (intermediateFiles.TryGetValue (dest, out ITaskItem conflict)) {
					string conflictItemSpec = conflict.GetMetadata ("OriginalItemSpec");
					string conflictPath = Path.GetFullPath (conflictItemSpec);
					string path = Path.GetFullPath (item.ItemSpec);
					if (string.Compare (conflictPath, path, StringComparison.Ordinal) == 0) {
						Log.LogCodedWarning (
							"XA1029",
							file: conflictItemSpec,
							lineNumber: 0,
							message: Properties.Resources.XA1029,
							conflictItemSpec,
							conflict.GetMetadata ("LogicalName"),
							rel);
						continue;
					}
					if (string.Compare (Path.ChangeExtension (conflictPath, ""), Path.ChangeExtension (path, ""), StringComparison.OrdinalIgnoreCase) == 0) {
						Log.LogCodedError (
							"XA1030",
							file: conflictItemSpec,
							lineNumber: 0,
							message: Properties.Resources.XA1030,
							conflictItemSpec,
							item.ItemSpec);
						return false;
					}
					Log.LogCodedError (
						"XA1031",
						file: conflictItemSpec,
						lineNumber: 0,
						message: Properties.Resources.XA1031,
						conflictItemSpec,
						conflict.GetMetadata ("LogicalName"),
						item.ItemSpec,
						rel);
					return false;
				}
				var newItem = new TaskItem (dest);
				newItem.SetMetadata ("LogicalName", rel);
				newItem.SetMetadata ("_FlatFile", Monodroid.AndroidResource.CalculateAapt2FlatArchiveFileName (dest));
				newItem.SetMetadata ("_ArchiveDirectory", AndroidLibraryFlatFilesDirectory);
				item.CopyMetadataTo (newItem);
				intermediateFiles.Add (dest, newItem);
				resolvedFiles.Add (item);
			}

			IntermediateFiles = intermediateFiles.Values.ToArray ();
			ResolvedResourceFiles = resolvedFiles.ToArray ();
			MonoAndroidHelper.SaveResourceCaseMap (BuildEngine4, nameCaseMap);
			return !Log.HasLoggedErrors;
		}
	}
}

