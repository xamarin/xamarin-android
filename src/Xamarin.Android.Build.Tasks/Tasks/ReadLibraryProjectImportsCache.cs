//
// ReadLibraryProjectImportsCache.cs
//
// Author:
//       Dean Ellis <dean.ellis@xamarin.com>
//
// Copyright (c) 2015 Xamarin Inc.
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
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class ReadLibraryProjectImportsCache : AndroidTask
	{
		public override string TaskPrefix => "RLC";

		[Required]
		public string CacheFile { get; set;}

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

		public override bool RunTask ()
		{
			Log.LogDebugMessage ("Task ReadLibraryProjectImportsCache");
			Log.LogDebugMessage ("  CacheFile: {0}", CacheFile);
			if (!File.Exists (CacheFile)) {
				Log.LogDebugMessage ("{0} does not exist. No Project Library Imports found", CacheFile);
				return !Log.HasLoggedErrors;
			}
			var doc = XDocument.Load (CacheFile);
			Jars = doc.GetPathsAsTaskItems ("Jars", "Jar");
			ResolvedAssetDirectories = doc.GetPathsAsTaskItems ("ResolvedAssetDirectories", "ResolvedAssetDirectory");
			ResolvedResourceDirectories = doc.GetPathsAsTaskItems ("ResolvedResourceDirectories", "ResolvedResourceDirectory");
			ResolvedEnvironmentFiles = doc.GetPathsAsTaskItems ("ResolvedEnvironmentFiles", "ResolvedEnvironmentFile");
			ResolvedResourceDirectoryStamps = doc.GetPathsAsTaskItems ("ResolvedResourceDirectoryStamps"
				, "ResolvedResourceDirectoryStamp");
			ProguardConfigFiles = doc.GetPathsAsTaskItems ("ProguardConfigFiles", "ProguardConfigFile");

			Log.LogDebugTaskItems ("  Jars: ", Jars);
			Log.LogDebugTaskItems ("  ResolvedAssetDirectories: ", ResolvedAssetDirectories);
			Log.LogDebugTaskItems ("  ResolvedResourceDirectories: ", ResolvedResourceDirectories);
			Log.LogDebugTaskItems ("  ResolvedEnvironmentFiles: ", ResolvedEnvironmentFiles);
			Log.LogDebugTaskItems ("  ResolvedResourceDirectoryStamps: ", ResolvedResourceDirectoryStamps);
			Log.LogDebugTaskItems ("  ProguardConfigFiles: ", ProguardConfigFiles);

			return !Log.HasLoggedErrors;
		}
	}
}

