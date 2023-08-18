using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Framework;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks
{
	/// <summary>
	/// Called in .NET 6+ to populate %(AotArguments) metadata in the ResolvedAssemblies [Output] property.
	/// </summary>
	public class GetAotAssemblies : GetAotArguments
	{
		public override string TaskPrefix => "GAOT";

		public ITaskItem [] MibcProfiles { get; set; } = Array.Empty<ITaskItem> ();

		public override Task RunTaskAsync ()
		{
			NdkTools ndk = NdkTools.Create (AndroidNdkDirectory, logErrors: UseAndroidNdk, log: Log);
			if (Log.HasLoggedErrors) {
				return Task.CompletedTask; // NdkTools.Create will log appropriate error
			}

			bool hasValidAotMode = GetAndroidAotMode (AndroidAotMode, out AotMode);
			if (!hasValidAotMode) {
				LogCodedError ("XA3002", Properties.Resources.XA3002, AndroidAotMode);
				return Task.CompletedTask;
			}

			if (AotMode == AotMode.Interp) {
				LogDebugMessage ("Interpreter AOT mode enabled");
				return Task.CompletedTask;
			}

			TryGetSequencePointsMode (AndroidSequencePointsMode, out SequencePointsMode);

			SdkBinDirectory = MonoAndroidHelper.GetOSBinPath ();

			var abi = AndroidRidAbiHelper.RuntimeIdentifierToAbi (RuntimeIdentifier);
			if (string.IsNullOrEmpty (abi)) {
				Log.LogCodedError ("XA0035", Properties.Resources.XA0035, RuntimeIdentifier);
				return Task.CompletedTask;
			}

			(_, string outdir, string mtriple, AndroidTargetArch arch) = GetAbiSettings (abi);
			Triple = mtriple;
			ToolPrefix = GetToolPrefix (ndk, arch, out int level);

			GetAotOptions (ndk, arch, level, outdir, ToolPrefix);

			var aotProfiles = new StringBuilder ();
			if (Profiles != null && Profiles.Length > 0) {
				aotProfiles.Append (",profile-only");
				foreach (var p in Profiles) {
					aotProfiles.Append (",profile=");
					aotProfiles.Append (Path.GetFullPath (p.ItemSpec));
				}
			} else if (MibcProfiles != null && MibcProfiles.Length > 0) {
				aotProfiles.Append (",profile-only");
				foreach (var p in MibcProfiles) {
					aotProfiles.Append (",mibc-profile=");
					aotProfiles.Append (Path.GetFullPath (p.ItemSpec));
				}
			}

			foreach (var assembly in ResolvedAssemblies) {
				var temp = Path.Combine (outdir, Path.GetFileNameWithoutExtension (assembly.ItemSpec));
				Directory.CreateDirectory (temp);
				if (Path.GetFileNameWithoutExtension (assembly.ItemSpec) == TargetName) {
					if (Profiles != null && Profiles.Length > 0) {
						LogDebugMessage ($"Not using profile(s) for main assembly: {assembly.ItemSpec}");
					}
					assembly.SetMetadata ("AotArguments", $"asmwriter,temp-path={temp}");
				} else {
					assembly.SetMetadata ("AotArguments", $"asmwriter,temp-path={temp}{aotProfiles}");
				}
			}

			return Task.CompletedTask;
		}
	}
}
