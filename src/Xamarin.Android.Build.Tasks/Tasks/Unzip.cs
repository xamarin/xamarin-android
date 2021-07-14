using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xamarin.Tools.Zip;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	public class Unzip : AndroidTask
	{
		public override string TaskPrefix => "UNZ";

		public ITaskItem [] Sources { get; set; }
		public ITaskItem [] DestinationDirectories { get; set; }
		public ITaskItem [] Files { get; set; }

		public override bool RunTask ()
		{
			foreach (var pair in Sources.Zip (DestinationDirectories, (s, d) => new { Source = s, Destination = d })) {
				if (!Directory.Exists (pair.Destination.ItemSpec))
					Directory.CreateDirectory (pair.Destination.ItemSpec);
				using (var z = ZipArchive.Open (pair.Source.ItemSpec, FileMode.Open)) {
					if (Files == null || Files.Length == 0) {
						z.ExtractAll (pair.Destination.ItemSpec);
					} else {
						foreach (var file in Files) {
							ZipEntry entry = z.ReadEntry (file.ItemSpec);
							if (entry == null) {
								Log.LogDebugMessage ($"Skipping not existant file {file.ItemSpec}");
								continue;
							}
							string destinationFileName = file.GetMetadata ("DestinationFileName");
							Log.LogDebugMessage ($"Extracting {file.ItemSpec} to {destinationFileName ?? file.ItemSpec}");
							entry.Extract (pair.Destination.ItemSpec, destinationFileName ?? file.ItemSpec);
						}
					}
				}
			}

			return true;
		}
	}
}
