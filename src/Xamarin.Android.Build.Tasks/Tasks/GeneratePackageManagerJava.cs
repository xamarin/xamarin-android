// Copyright (C) 2011 Xamarin, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Java.Interop.Tools.TypeNameMappings;
using Xamarin.Android.Tools;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks
{
	using PackageNamingPolicyEnum   = PackageNamingPolicy;

	public class GeneratePackageManagerJava : AndroidTask
	{
		public override string TaskPrefix => "GPM";

		Guid buildId = Guid.NewGuid ();

		[Required]
		public ITaskItem[] ResolvedAssemblies { get; set; }

		[Required]
		public ITaskItem[] ResolvedUserAssemblies { get; set; }

		[Required]
		public string OutputDirectory { get; set; }

		[Required]
		public string EnvironmentOutputDirectory { get; set; }

		[Required]
		public string MainAssembly { get; set; }

		[Required]
		public string TargetFrameworkVersion { get; set; }

		[Required]
		public string Manifest { get; set; }

		[Required]
		public bool IsBundledApplication { get; set; }

		[Required]
		public string [] SupportedAbis { get; set; }

		[Required]
		public string AndroidPackageName { get; set; }

		[Required]
		public bool EnablePreloadAssembliesDefault { get; set; }

		[Required]
		public bool InstantRunEnabled { get; set; }

		public string RuntimeConfigBinFilePath { get; set; }
		public string BoundExceptionType { get; set; }

		public string PackageNamingPolicy { get; set; }
		public string Debug { get; set; }
		public ITaskItem[] Environments { get; set; }
		public string AndroidAotMode { get; set; }
		public bool EnableLLVM { get; set; }
		public string HttpClientHandlerType { get; set; }
		public string TlsProvider { get; set; }
		public string AndroidSequencePointsMode { get; set; }
		public bool EnableSGenConcurrent { get; set; }

		[Output]
		public string BuildId { get; set; }

		bool _Debug {
			get {
				return string.Equals (Debug, "true", StringComparison.OrdinalIgnoreCase);
			}
		}

		public override bool RunTask ()
		{
			BuildId = buildId.ToString ();
			Log.LogDebugMessage ("  [Output] BuildId: {0}", BuildId);

			var doc = AndroidAppManifest.Load (Manifest, MonoAndroidHelper.SupportedVersions);
			int minApiVersion = doc.MinSdkVersion == null ? 4 : (int) doc.MinSdkVersion;
			// We need to include any special assemblies in the Assemblies list
			var assemblies = ResolvedUserAssemblies
				.Concat (MonoAndroidHelper.GetFrameworkAssembliesToTreatAsUserAssemblies (ResolvedAssemblies))
				.ToList ();
			var mainFileName = Path.GetFileName (MainAssembly);
			Func<string,string,bool> fileNameEq = (a,b) => a.Equals (b, StringComparison.OrdinalIgnoreCase);
			assemblies = assemblies.Where (a => fileNameEq (a.ItemSpec, mainFileName)).Concat (assemblies.Where (a => !fileNameEq (a.ItemSpec, mainFileName))).ToList ();

			using (var pkgmgr = MemoryStreamPool.Shared.CreateStreamWriter ()) {
				pkgmgr.WriteLine ("package mono;");

				// Write all the user assemblies
				pkgmgr.WriteLine ("public class MonoPackageManager_Resources {");
				pkgmgr.WriteLine ("\tpublic static String[] Assemblies = new String[]{");

				pkgmgr.WriteLine ("\t\t/* We need to ensure that \"{0}\" comes first in this list. */", mainFileName);
				foreach (var assembly in assemblies) {
					pkgmgr.WriteLine ("\t\t\"" + Path.GetFileName (assembly.ItemSpec) + "\",");
				}

				// Write the assembly dependencies
				pkgmgr.WriteLine ("\t};");
				pkgmgr.WriteLine ("\tpublic static String[] Dependencies = new String[]{");

				//foreach (var assembly in assemblies.Except (args.Assemblies)) {
				//        if (args.SharedRuntime && !Toolbox.IsInSharedRuntime (assembly))
				//                pkgmgr.WriteLine ("\t\t\"" + Path.GetFileName (assembly) + "\",");
				//}

				pkgmgr.WriteLine ("\t};");

				pkgmgr.WriteLine ("}");
				pkgmgr.Flush ();

				// Only copy to the real location if the contents actually changed
				var dest = Path.GetFullPath (Path.Combine (OutputDirectory, "MonoPackageManager_Resources.java"));

				Files.CopyIfStreamChanged (pkgmgr.BaseStream, dest);
			}

			AddEnvironment ();

			return !Log.HasLoggedErrors;
		}

		static internal NativeAssemblerTargetProvider GetAssemblyTargetProvider (string abi)
		{
			switch (abi.Trim ()) {
				case "armeabi-v7a":
					return new ARMNativeAssemblerTargetProvider (false);

				case "arm64-v8a":
					return new ARMNativeAssemblerTargetProvider (true);

				case "x86":
					return new X86NativeAssemblerTargetProvider (false);

				case "x86_64":
					return new X86NativeAssemblerTargetProvider (true);

				default:
					throw new InvalidOperationException ($"Unknown ABI {abi}");
			}
		}

		static readonly string[] defaultLogLevel = {"MONO_LOG_LEVEL", "info"};
		static readonly string[] defaultMonoDebug = {"MONO_DEBUG", "gen-compact-seq-points"};
		static readonly string[] defaultHttpMessageHandler = {"XA_HTTP_CLIENT_HANDLER_TYPE", "System.Net.Http.HttpClientHandler, System.Net.Http"};
		static readonly string[] defaultTlsProvider = {"XA_TLS_PROVIDER", "btls"};

		void AddEnvironment ()
		{
			bool usesMonoAOT = false;
			bool usesAssemblyPreload = EnablePreloadAssembliesDefault;
			bool brokenExceptionTransitions = false;
			var environmentVariables = new Dictionary<string, string> (StringComparer.Ordinal);
			var systemProperties = new Dictionary<string, string> (StringComparer.Ordinal);

			if (!Enum.TryParse (PackageNamingPolicy, out PackageNamingPolicy pnp)) {
				pnp = PackageNamingPolicyEnum.LowercaseCrc64;
			}

			AotMode aotMode = AotMode.None;
			if (!string.IsNullOrEmpty (AndroidAotMode) && Aot.GetAndroidAotMode (AndroidAotMode, out aotMode) && aotMode != AotMode.None) {
				usesMonoAOT = true;
			}

			bool haveLogLevel = false;
			bool haveMonoDebug = false;
			bool havebuildId = false;
			bool haveHttpMessageHandler = false;
			bool haveTlsProvider = false;
			bool haveMonoGCParams = false;

			SequencePointsMode sequencePointsMode;
			if (!Aot.TryGetSequencePointsMode (AndroidSequencePointsMode, out sequencePointsMode))
				sequencePointsMode = SequencePointsMode.None;

			foreach (ITaskItem env in Environments ?? new TaskItem[0]) {
				foreach (string line in File.ReadLines (env.ItemSpec)) {
					var lineToWrite = line;
					if (lineToWrite.StartsWith ("MONO_LOG_LEVEL=", StringComparison.Ordinal))
						haveLogLevel = true;
					if (lineToWrite.StartsWith ("MONO_GC_PARAMS=", StringComparison.Ordinal))
						haveMonoGCParams = true;
					if (lineToWrite.StartsWith ("XAMARIN_BUILD_ID=", StringComparison.Ordinal))
						havebuildId = true;
					if (lineToWrite.StartsWith ("MONO_DEBUG=", StringComparison.Ordinal)) {
						haveMonoDebug = true;
						if (sequencePointsMode != SequencePointsMode.None && !lineToWrite.Contains ("gen-compact-seq-points"))
							lineToWrite = line  + ",gen-compact-seq-points";
					}
					if (lineToWrite.StartsWith ("XA_HTTP_CLIENT_HANDLER_TYPE=", StringComparison.Ordinal))
						haveHttpMessageHandler = true;
					if (lineToWrite.StartsWith ("XA_TLS_PROVIDER=", StringComparison.Ordinal))
						haveTlsProvider = true;
					if (lineToWrite.StartsWith ("mono.enable_assembly_preload=", StringComparison.Ordinal)) {
						int idx = lineToWrite.IndexOf ('=');
						uint val;
						if (idx < lineToWrite.Length - 1 && UInt32.TryParse (lineToWrite.Substring (idx + 1), out val)) {
							usesAssemblyPreload = idx == 1;
						}
						continue;
					}
					if (lineToWrite.StartsWith ("XA_BROKEN_EXCEPTION_TRANSITIONS=")) {
						brokenExceptionTransitions = true;
						continue;
					}

					AddEnvironmentVariableLine (lineToWrite);
				}
			}

			if (_Debug && !haveLogLevel) {
				AddEnvironmentVariable (defaultLogLevel[0], defaultLogLevel[1]);
			}

			if (sequencePointsMode != SequencePointsMode.None && !haveMonoDebug) {
				AddEnvironmentVariable (defaultMonoDebug[0], defaultMonoDebug[1]);
			}

			if (!havebuildId)
				AddEnvironmentVariable ("XAMARIN_BUILD_ID", BuildId);

			if (!haveHttpMessageHandler) {
				if (HttpClientHandlerType == null)
					AddEnvironmentVariable (defaultHttpMessageHandler[0], defaultHttpMessageHandler[1]);
				else
					AddEnvironmentVariable ("XA_HTTP_CLIENT_HANDLER_TYPE", HttpClientHandlerType.Trim ());
			}

			if (!haveTlsProvider) {
				if (TlsProvider == null)
					AddEnvironmentVariable (defaultTlsProvider[0], defaultTlsProvider[1]);
				else
					AddEnvironmentVariable ("XA_TLS_PROVIDER", TlsProvider.Trim ());
			}

			if (!haveMonoGCParams) {
				if (EnableSGenConcurrent)
					AddEnvironmentVariable ("MONO_GC_PARAMS", "major=marksweep-conc");
				else
					AddEnvironmentVariable ("MONO_GC_PARAMS", "major=marksweep");
			}

			global::Android.Runtime.BoundExceptionType boundExceptionType;
			if (String.IsNullOrEmpty (BoundExceptionType) || String.Compare (BoundExceptionType, "System", StringComparison.OrdinalIgnoreCase) == 0) {
				boundExceptionType = global::Android.Runtime.BoundExceptionType.System;
			} else if (String.Compare (BoundExceptionType, "Java", StringComparison.OrdinalIgnoreCase) == 0) {
				boundExceptionType = global::Android.Runtime.BoundExceptionType.Java;
			} else {
				throw new InvalidOperationException ($"Unsupported BoundExceptionType value '{BoundExceptionType}'");
			}

			bool haveRuntimeConfigBlob = !String.IsNullOrEmpty (RuntimeConfigBinFilePath) && File.Exists (RuntimeConfigBinFilePath);
			var appConfState = BuildEngine4.GetRegisteredTaskObjectAssemblyLocal<ApplicationConfigTaskState> (ApplicationConfigTaskState.RegisterTaskObjectKey, RegisteredTaskObjectLifetime.Build);
			foreach (string abi in SupportedAbis) {
				NativeAssemblerTargetProvider asmTargetProvider = GetAssemblyTargetProvider (abi);
				string baseAsmFilePath = Path.Combine (EnvironmentOutputDirectory, $"environment.{abi.ToLowerInvariant ()}");
				string asmFilePath = $"{baseAsmFilePath}.s";

				var asmgen = new ApplicationConfigNativeAssemblyGenerator (asmTargetProvider, baseAsmFilePath, environmentVariables, systemProperties) {
					IsBundledApp = IsBundledApplication,
					UsesMonoAOT = usesMonoAOT,
					UsesMonoLLVM = EnableLLVM,
					UsesAssemblyPreload = usesAssemblyPreload,
					MonoAOTMode = aotMode.ToString ().ToLowerInvariant (),
					AndroidPackageName = AndroidPackageName,
					BrokenExceptionTransitions = brokenExceptionTransitions,
					PackageNamingPolicy = pnp,
					BoundExceptionType = boundExceptionType,
					InstantRunEnabled = InstantRunEnabled,
					JniAddNativeMethodRegistrationAttributePresent = appConfState != null ? appConfState.JniAddNativeMethodRegistrationAttributePresent : false,
					HaveRuntimeConfigBlob = haveRuntimeConfigBlob,
					TokenIdCollection = appConfState?.TokenIdCollection ?? new NativeRuntimeTokenIdCollection (Log),
				};

				using (var sw = MemoryStreamPool.Shared.CreateStreamWriter ()) {
					asmgen.Write (sw);
					sw.Flush ();
					Files.CopyIfStreamChanged (sw.BaseStream, asmFilePath);
				}
			}

			void AddEnvironmentVariable (string name, string value)
			{
				if (Char.IsUpper(name [0]) || !Char.IsLetter(name [0]))
					environmentVariables [ValidAssemblerString (name)] = ValidAssemblerString (value);
				else
					systemProperties [ValidAssemblerString (name)] = ValidAssemblerString (value);
			}

			void AddEnvironmentVariableLine (string l)
			{
				string line = l?.Trim ();
				if (String.IsNullOrEmpty (line) || line [0] == '#')
					return;

				string[] nv = line.Split (new char[]{'='}, 2);
				AddEnvironmentVariable (nv[0].Trim (), nv.Length < 2 ? String.Empty : nv[1].Trim ());
			}

			string ValidAssemblerString (string s)
			{
				return s.Replace ("\"", "\\\"");
			}
		}
	}
}
