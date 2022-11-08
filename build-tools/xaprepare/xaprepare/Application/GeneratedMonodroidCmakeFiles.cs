using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Xamarin.Android.Prepare
{
	partial class GeneratedMonodroidCmakeFiles : GeneratedFile
	{
		string outputPath;

		public GeneratedMonodroidCmakeFiles (string outputPath)
			: base (MakeOutputPath (outputPath))
		{
			this.outputPath = outputPath;
		}

		static string MakeOutputPath (string outputPath)
		{
			return Path.Combine (outputPath, $"{{{Configurables.Paths.CmakeMSBuildPropsName},{Configurables.Paths.CmakeShellScriptsPropsName},{Configurables.Paths.CmakeMonodroidTargets}}}");
		}

		public override void Generate (Context context)
		{
			using (StreamWriter sw = Utilities.OpenStreamWriter (Path.Combine (outputPath, Configurables.Paths.CmakeMSBuildPropsName))) {
				GenerateMSBuildProps (context, sw);
				sw.Flush ();
			}

			using (StreamWriter sw = Utilities.OpenStreamWriter (Path.Combine (outputPath, Configurables.Paths.CmakeMonodroidTargets))) {
				GenerateMonodroidTargets (context, sw);
				sw.Flush ();
			}

			using (StreamWriter sw = Utilities.OpenStreamWriter (Path.Combine (outputPath, Configurables.Paths.CmakeShellScriptsPropsName))) {
				GenerateShellConfig (context, sw);
				sw.Flush ();
			}
		}

		static readonly string[] JitAbis = new [] {
			AbiNames.TargetJit.AndroidArmV7a,
			AbiNames.TargetJit.AndroidArmV8a,
			AbiNames.TargetJit.AndroidX86,
			AbiNames.TargetJit.AndroidX86_64,
		};

		static readonly string[] HostAbis = new [] {
			Context.Instance.OS.Type,
			AbiNames.HostJit.Win32,
			AbiNames.HostJit.Win64,
		};

		void GenerateShellConfig (Context context, StreamWriter sw)
		{
			var commonReplacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@BUILD_TYPE@", "${__BUILD_TYPE}" },
				{ "@CONFIGURATION@", "${__CONFIGURATION}" },
				{ "@XA_BUILD_CONFIGURATION@", "${XA_BUILD_CONFIGURATION}" },
				{ "@XA_LIB_TOP_DIR@", Configurables.Paths.InstallMSBuildDir.Replace (Path.DirectorySeparatorChar, '/') },
				{ "@JdkIncludePath@", "${JDK_INCLUDE_PATH}" },
				{ "@MonoSourceFullPath@", "${MONO_SOURCE_PATH}" },
				{ "@NinjaPath@", "${NINJA}" },
				{ "@OUTPUT_DIRECTORY@", "${__OUTPUT_DIR}" },
				{ "@SOURCE_DIRECTORY@", "${MONODROID_SOURCE_DIR}" },
			};

			var androidRuntimeReplacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@CmakeAndroidFlags@", "" },
				{ "@NATIVE_API_LEVEL@", "${__NATIVE_API_LEVEL}" },
				{ "@ABI@", "${__NATIVE_ABI}" },
				{ "@AndroidNdkDirectory@", "${NDK_DIRECTORY}" },
				{ "@AndroidToolchainPath@", GetRelativeToolchainDefinitionPath () },
			};

			var hostRuntimeReplacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@CmakeHostFlags@", "" },
				{ "@MingwDependenciesRootDirectory@", CmakeBuilds.MingwDependenciesRootDirectory },
				{ "@MxeToolchainBasePath@", "${MXE_TOOLCHAIN_BASE_PATH}" },
				{ "@BITNESS@", "" },
			};

			AddReplacements (commonReplacements, androidRuntimeReplacements);
			AddReplacements (commonReplacements, hostRuntimeReplacements);

			string monodroidObjDir = Path.Combine (Configurables.Paths.MonodroidSourceDir, "obj", context.Configuration);
			string jdkInfoPropsPath = Path.Combine (Configurables.Paths.ExternalJavaInteropDir, "bin", $"Build{context.Configuration}", "JdkInfo.props");

			sw.WriteLine ("# This is a bash(1) script");
			sw.WriteLine ();
			sw.WriteLine ($"CMAKE=\"{context.Properties.GetRequiredValue(KnownProperties.CMakePath)}\"");
			sw.WriteLine ($"JDK_INCLUDE_PATH=\"$(grep JdkIncludePath {jdkInfoPropsPath} | cut -d '\"' -f 2 | tr '\\n' ' ')\"");
			sw.WriteLine ($"MONO_SOURCE_PATH=\"{Configurables.Paths.MonoSourceFullPath}\"");
			sw.WriteLine ($"MONODROID_OBJ_DIR=\"{monodroidObjDir}\"");
			sw.WriteLine ($"MONODROID_SOURCE_DIR=\"{Configurables.Paths.MonodroidSourceDir}\"");
			sw.WriteLine ($"MXE_TOOLCHAIN_BASE_PATH=\"{CmakeBuilds.MxeToolchainBasePath}\"");
			sw.WriteLine ($"NDK_DIRECTORY=\"{Configurables.Paths.AndroidNdkDirectory}\"");
			sw.WriteLine ($"NINJA=\"{context.Properties.GetRequiredValue(KnownProperties.NinjaPath)}\"");
			sw.WriteLine ($"XA_BUILD_CONFIGURATION={context.Configuration}");
			sw.WriteLine ($"XA_INSTALL_DIR=\"{Configurables.Paths.InstallMSBuildDir}/lib\"");
			sw.WriteLine ();

			string indent = "\t";
			sw.WriteLine ("function __xa_build()");
			sw.WriteLine ("{");

			WriteVariableValidationCode ("__BUILD_DIR");

			sw.WriteLine ($"{indent}\"${{NINJA}}\" -C \"${{__BUILD_DIR}}\" \"$@\"");
			sw.WriteLine ("}");
			sw.WriteLine ();

			sw.WriteLine ("function __xa_configure_android_runtime()");
			sw.WriteLine ("{");

			WriteVariableValidationCode ("__BUILD_DIR");
			WriteVariableValidationCode ("__CONFIGURATION");
			WriteVariableValidationCode ("__BUILD_TYPE");
			WriteVariableValidationCode ("__NATIVE_API_LEVEL");
			WriteVariableValidationCode ("__NATIVE_ABI");
			WriteVariableValidationCode ("__OUTPUT_DIR");

			sw.WriteLine ($"{indent}cleanup_build_dir");
			sw.WriteLine ();

			indent = "\t\t";
			var flags = new StringBuilder ();
			AppendFlags (flags, CmakeBuilds.CommonFlags, indent);
			AppendFlags (flags, CmakeBuilds.MonodroidCommonDefines, indent);
			AppendFlags (flags, CmakeBuilds.AndroidFlags, indent);
			AppendFlags (flags, CmakeBuilds.ConfigureAndroidRuntimeCommandsCommonFlags, indent);
			indent = "\t";

			WriteCmakeCall (flags, androidRuntimeReplacements);

			sw.WriteLine ("}");
			sw.WriteLine ();

			sw.WriteLine ("function __xa_configure_host_runtime()");
			sw.WriteLine ("{");
			sw.WriteLine ($"{indent}local is_mxe=$1");
			sw.WriteLine ();

			WriteVariableValidationCode ("__BUILD_DIR");
			WriteVariableValidationCode ("__CONFIGURATION");
			WriteVariableValidationCode ("__BUILD_TYPE");
			WriteVariableValidationCode ("__OUTPUT_DIR");

			sw.WriteLine ($"{indent}cleanup_build_dir");
			sw.WriteLine ();

			sw.WriteLine ($"{indent}shift");
			sw.WriteLine ();
			sw.WriteLine ($"{indent}if [ \"${{is_mxe}}\" = \"yes\" ]; then");
			indent = "\t\t";

			// Windows cross builds
			flags.Clear ();
			indent = "\t\t\t";
			AppendFlags (flags, CmakeBuilds.CommonFlags, indent);
			AppendFlags (flags, CmakeBuilds.MonodroidCommonDefines, indent);
			AppendFlags (flags, CmakeBuilds.MonodroidMxeCommonFlags, indent);
			AppendFlags (flags, CmakeBuilds.ConfigureHostRuntimeCommandsCommonFlags, indent);
			indent = "\t\t";
			WriteCmakeCall (flags, hostRuntimeReplacements);

			// Host build
			sw.WriteLine ($"\telse");
			indent = "\t\t\t";
			flags.Clear ();
			AppendFlags (flags, CmakeBuilds.CommonFlags, indent);
			AppendFlags (flags, CmakeBuilds.MonodroidCommonDefines, indent);
			AppendFlags (flags, CmakeBuilds.ConfigureHostRuntimeCommandsCommonFlags, indent);
			indent = "\t\t";
			WriteCmakeCall (flags, hostRuntimeReplacements);

			indent = "\t";
			sw.WriteLine ($"{indent}fi");

			sw.WriteLine ("}");
			sw.WriteLine ();

			foreach (CmakeBuilds.RuntimeCommand rc in CmakeBuilds.AndroidRuntimeCommands) {
				WriteShellRuntimeCommand (sw, JitAbis, rc, androidRuntimeReplacements);
			}

			foreach (CmakeBuilds.RuntimeCommand rc in CmakeBuilds.HostRuntimeCommands) {
				WriteShellRuntimeCommand (sw, HostAbis, rc, hostRuntimeReplacements);
			}

			void WriteCmakeCall (StringBuilder args, Dictionary<string, string> replacements)
			{
				sw.WriteLine ($"{indent}\"${{CMAKE}}\" \\");
				sw.WriteLine ($"{indent}\t-B \"${{__BUILD_DIR}}\" \\");
				sw.Write (ApplyReplacements (args, replacements).ToString ());
				sw.WriteLine (" \"$@\"");
			}

			void AppendFlags (StringBuilder sb, List<string> flags, string indent)
			{
				if (sb.Length > 0) {
					sb.Append ($" \\\n{indent}");
				} else {
					sb.Append (indent);
				}

				sb.Append (String.Join ($" \\\n{indent}", flags));
			}

			void WriteVariableValidationCode (string varName)
			{
				sw.WriteLine ($"{indent}if [ -z \"${{{varName}}}\" ]; then");
				sw.WriteLine ($"{indent}\tdie \"Variable '{varName}' is empty\"");
				sw.WriteLine ($"{indent}fi");
				sw.WriteLine ();
			}
		}

		void WriteShellRuntimeCommand (StreamWriter sw, IEnumerable<string> abis, CmakeBuilds.RuntimeCommand command, Dictionary<string, string> replacements)
		{
			var funcName = new StringBuilder ();
			string indent = "\t";

			foreach (var a in abis) {
				string abi = a;
				uint minApiLevel = command.IsDotNet ? BuildAndroidPlatforms.NdkMinimumAPI : !command.IsHost ? BuildAndroidPlatforms.NdkMinimumAPIMap [abi] : 0;
				string outputDirName = command.IsDotNet ? AbiNames.AbiToRuntimeIdentifier (abi) : abi;
				string isMxe = "no";
				string mxeBitness = String.Empty;
				bool forMxe = false;

				if (command.IsHost) {
					if (String.Compare (abi, AbiNames.HostJit.Win32, StringComparison.OrdinalIgnoreCase) == 0) {
						mxeBitness = "32";
					} else if (String.Compare (abi, AbiNames.HostJit.Win64, StringComparison.OrdinalIgnoreCase) == 0) {
						mxeBitness = "64";
					}

					if (mxeBitness.Length > 0) {
						isMxe = "yes";
						forMxe = true;
					}
				}

				if (command.IsHost) {
					outputDirName = $"host-{outputDirName}";
					abi = $"host-{abi}";
				}

				funcName.Clear ();
				funcName.Append (abi.ToLowerInvariant ());
				funcName.Append ('_');
				funcName.Append (command.Suffix.ToLowerInvariant ());
				funcName.Replace ('-', '_');

				sw.WriteLine ();
				sw.WriteLine ($"function _configure_{funcName}()");
				sw.WriteLine ("{");
				sw.WriteLine ($"{indent}local build_directory=\"$1\"");
				sw.WriteLine ($"{indent}local rebuild=\"$2\"");
				sw.WriteLine ($"{indent}local force=\"$3\"");
				sw.WriteLine ();
				sw.WriteLine ($"{indent}if [ -z \"${{build_directory}}\" ]; then");
				sw.WriteLine ($"{indent}\tbuild_directory=\"${{MONODROID_OBJ_DIR}}\"");
				sw.WriteLine ($"{indent}fi");
				sw.WriteLine ($"{indent}__BUILD_DIR=\"${{build_directory}}/{outputDirName}-{command.Suffix}\"");
				sw.WriteLine ($"{indent}__OUTPUT_DIR=\"${{XA_INSTALL_DIR}}/{outputDirName}\"");
				sw.WriteLine ($"{indent}__CONFIGURATION={command.Configuration}");
				sw.WriteLine ($"{indent}__BUILD_TYPE={command.BuildType}");
				sw.WriteLine ($"{indent}__REBUILD=\"${{rebuild}}\"");
				sw.WriteLine ($"{indent}__FORCE=\"${{force}}\"");
				if (!command.IsHost) {
					sw.WriteLine ($"{indent}__NATIVE_ABI={abi}");
					sw.WriteLine ($"{indent}__NATIVE_API_LEVEL={minApiLevel}");
				}
				sw.WriteLine ();
				if (!command.IsHost) {
					sw.Write ($"{indent}__xa_configure_android_runtime");
				} else {
					sw.Write ($"{indent}__xa_configure_host_runtime {isMxe}");
				}

				StringBuilder? flags = null;
				if (forMxe) {
					flags = new StringBuilder (String.Join (" ", CmakeBuilds.MonodroidMxeCommonFlagsBitness));
					replacements["@BITNESS@"] = mxeBitness;
				}

				if (command.ExtraOptions != null && command.ExtraOptions.Count > 0) {
					if (flags == null) {
						flags = new StringBuilder ();
					} else {
						flags.Append (" ");
					}
					flags.Append (String.Join (" ", command.ExtraOptions));
				}

				if (flags != null && flags.Length > 0) {
					sw.Write (" ");
					sw.Write (ApplyReplacements (flags, replacements));
				}

				sw.WriteLine ();
				sw.WriteLine ();
				sw.WriteLine ("}");

				indent = "\t";
				sw.WriteLine ();
				sw.WriteLine ($"function _build_{funcName}()");
				sw.WriteLine ("{");
				sw.WriteLine ($"{indent}local build_directory=\"$1\"");
				sw.WriteLine ();
				sw.WriteLine ($"{indent}shift");
				sw.WriteLine ($"{indent}if [ -z \"${{build_directory}}\" ]; then");
				sw.WriteLine ($"{indent}\tbuild_directory=\"${{MONODROID_OBJ_DIR}}\"");
				sw.WriteLine ($"{indent}fi");
				sw.WriteLine ($"{indent}__BUILD_DIR=\"${{build_directory}}/{abi}-{command.Suffix}\"");
				sw.WriteLine ();
				sw.WriteLine ($"{indent}__xa_build \"$@\"");
				sw.WriteLine ("}");
			};
		}

		void GenerateMonodroidTargets (Context context, StreamWriter sw)
		{
			string sourceDir = Utilities.GetRelativePath (Configurables.Paths.BuildBinDir, Configurables.Paths.MonodroidSourceDir);

			var commonReplacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@BUILD_TYPE@", "" },
				{ "@CONFIGURATION@", "" },
				{ "@SOURCE_DIRECTORY@", $"$(MSBuildThisFileDirectory){sourceDir}" },
			};

			var androidRuntimeReplacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@CmakeAndroidFlags@", "$(_CmakeAndroidFlags)" },
				{ "@NATIVE_API_LEVEL@", "" },
				{ "@ABI@", "%(AndroidSupportedTargetJitAbi.Identity)" },
				{ "@RID@", "%(AndroidSupportedTargetJitAbi.AndroidRID)" },
				{ "@OUTPUT_DIRECTORY@", "" },
			};

			var hostRuntimeReplacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@CmakeHostFlags@", "%(_HostRuntime.CmakeFlags)" },
				{ "@JdkIncludePath@", "@(JdkIncludePath->'%(Identity)', ' ')" },
				{ "@OUTPUT_DIRECTORY@", "" },
			};

			AddReplacements (commonReplacements, androidRuntimeReplacements);
			AddReplacements (commonReplacements, hostRuntimeReplacements);

			WriteMSBuildProjectStart (sw);
			sw.WriteLine ("  <Target Name=\"_PrepareConfigureRuntimeCommands\" DependsOnTargets=\"_GetBuildHostRuntimes\">");

			string indent = "    ";
			foreach (CmakeBuilds.RuntimeCommand rc in CmakeBuilds.AndroidRuntimeCommands) {
				WriteMSBuildConfigureAndroidRuntimeCommands (sw, indent, rc, androidRuntimeReplacements);
			}

			foreach (CmakeBuilds.RuntimeCommand rc in CmakeBuilds.HostRuntimeCommands) {
				WriteMSBuildConfigureHostRuntimeCommands (sw, indent, rc, hostRuntimeReplacements);
			};

			sw.WriteLine ("  </Target>");
			WriteMSBuildProjectEnd (sw);
		}

		void WriteMSBuildConfigureRuntimeCommands (StreamWriter sw, string indent, string workingDirectory, string outputDirectory, string itemName, List<string> commonFlags, CmakeBuilds.RuntimeCommand command, Dictionary<string, string> replacements, bool needsApiLevel)
		{
			replacements["@CONFIGURATION@"] = EnsureRequired ("Configuration", command.Configuration);
			replacements["@BUILD_TYPE@"] = EnsureRequired ("BuildType", command.BuildType);
			replacements["@NATIVE_API_LEVEL@"] = needsApiLevel ? EnsureRequired ("MSBuildApiLevel", command.MSBuildApiLevel) : String.Empty;
			replacements["@OUTPUT_DIRECTORY@"] = outputDirectory;

			var flags = new StringBuilder ();
			flags.Append (String.Join (" ", commonFlags));

			if (command.ExtraOptions != null && command.ExtraOptions.Count > 0) {
				flags.Append (" ");
				flags.Append (String.Join (" ", command.ExtraOptions));
			}

			var conditionString = (workingDirectory.IndexOf ("-asan", StringComparison.OrdinalIgnoreCase) >= 0
				|| workingDirectory.IndexOf ("-ubsan", StringComparison.OrdinalIgnoreCase) >= 0)
				? " Condition=\"'$(EnableNativeAnalyzers)' == 'true'\" " : string.Empty;

			sw.WriteLine ($"{indent}<MakeDir Directories=\"{workingDirectory}\"{conditionString}/>");
			sw.WriteLine ($"{indent}<ItemGroup{conditionString}>");
			sw.WriteLine ($"{indent}  <_ConfigureRuntimeCommands Include=\"{itemName}\">");
			sw.WriteLine ($"{indent}    <Command>$(CmakePath)</Command>");
			WriteProperty (sw, $"{indent}    ", "Arguments", flags, replacements);
			sw.WriteLine ($"{indent}    <WorkingDirectory>{workingDirectory}</WorkingDirectory>");
			sw.WriteLine ($"{indent}  </_ConfigureRuntimeCommands>");
			sw.WriteLine ($"{indent}</ItemGroup>");
			sw.WriteLine ();

			string EnsureRequired (string name, string v)
			{
				if (v.Length > 0) {
					return v;
				}

				throw new InvalidOperationException ($"RuntimeCommand.{name} must not be an empty string");
			}
		}

		void WriteMSBuildConfigureAndroidRuntimeCommands (StreamWriter sw, string indent, CmakeBuilds.RuntimeCommand command, Dictionary<string, string> replacements)
		{
			const string LegacyOutputDirectory = "%(AndroidSupportedTargetJitAbi.Identity)";
			const string OutputDirectory = "%(AndroidSupportedTargetJitAbi.AndroidRID)";

			WriteMSBuildConfigureRuntimeCommands (
				sw,
				indent,
				command.IsDotNet ? $"$(IntermediateOutputPath){OutputDirectory}-{command.Suffix}" : $"$(IntermediateOutputPath){LegacyOutputDirectory}-{command.Suffix}",
				command.IsDotNet ? $"$(OutputPath){OutputDirectory}" : $"$(OutputPath){LegacyOutputDirectory}",
				"@(AndroidSupportedTargetJitAbi)",
				CmakeBuilds.ConfigureAndroidRuntimeCommandsCommonFlags,
				command,
				replacements,
				true
			);
		}

		void WriteMSBuildConfigureHostRuntimeCommands (StreamWriter sw, string indent, CmakeBuilds.RuntimeCommand command, Dictionary<string, string> replacements)
		{
			WriteMSBuildConfigureRuntimeCommands (
				sw,
				indent,
				$"$(IntermediateOutputPath)%(_HostRuntime.OutputDirectory)-{command.Suffix}",
				"$(OutputPath)/%(_HostRuntime.OutputDirectory)",
				"@(_HostRuntime)",
				CmakeBuilds.ConfigureHostRuntimeCommandsCommonFlags,
				command,
				replacements,
				false
			);
		}

		void GenerateMSBuildProps (Context context, StreamWriter sw)
		{
			var MSBuildReplacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@NinjaPath@", "$(NinjaPath)" },
				{ "@XA_BUILD_CONFIGURATION@", "$(Configuration)" },
				{ "@XA_LIB_TOP_DIR@", "$(MicrosoftAndroidSdkOutDir)" },
				{ "@AndroidNdkDirectory@", "$(AndroidNdkDirectory)" },
				{ "@MonoSourceFullPath@", "$(MonoSourceFullPath)" },
				{ "@AndroidToolchainPath@", GetRelativeToolchainDefinitionPath () },
			};

			var MSBuildMingwReplacements = new Dictionary<string, string> (StringComparer.Ordinal) {
				{ "@MingwDependenciesRootDirectory@", CmakeBuilds.MingwDependenciesRootDirectory },
				{ "@MxeToolchainBasePath@", CmakeBuilds.MxeToolchainBasePath },
				{ "@BITNESS@", "" },
			};

			WriteMSBuildProjectStart (sw);

			var sharedByAll = new StringBuilder ("--debug-output ");
			sharedByAll.Append (String.Join (" ", CmakeBuilds.CommonFlags));
			sharedByAll.Append (" ");
			sharedByAll.Append (String.Join (" ", CmakeBuilds.MonodroidCommonDefines));

			var flags = new StringBuilder ();
			flags.Append (sharedByAll);
			flags.Append (" ");
			flags.Append (String.Join (" ", CmakeBuilds.AndroidFlags));

			string propertyIndent = "    ";
			sw.WriteLine ("  <PropertyGroup>");
			WriteProperty (sw, propertyIndent, "_CmakeAndroidFlags", flags, MSBuildReplacements);
			WriteProperty (sw, propertyIndent, "_CmakeCommonHostFlags", sharedByAll, MSBuildReplacements);
			sw.WriteLine ("  </PropertyGroup>");

			if (CmakeBuilds.MxeToolchainBasePath.Length > 0 && CmakeBuilds.MingwDependenciesRootDirectory.Length > 0) {
				AddReplacements (MSBuildReplacements, MSBuildMingwReplacements);

				sw.WriteLine ();
				sw.WriteLine ("  <PropertyGroup>");

				flags.Clear ();
				flags.Append (sharedByAll);
				flags.Append (" ");
				flags.Append (String.Join (" ", CmakeBuilds.MonodroidMxeCommonFlags));
				flags.Append (" ");
				flags.Append (String.Join (" ", CmakeBuilds.MonodroidMxeCommonFlagsBitness));

				MSBuildMingwReplacements["@BITNESS@"] = "32";
				WriteProperty (sw, propertyIndent, "_CmakeMxeCommonFlags32", flags, MSBuildMingwReplacements);

				MSBuildMingwReplacements["@BITNESS@"] = "64";
				WriteProperty (sw, propertyIndent, "_CmakeMxeCommonFlags64", flags, MSBuildMingwReplacements);

				sw.WriteLine ("  </PropertyGroup>");
			}

			WriteMSBuildProjectEnd (sw);
		}

		StringBuilder ApplyReplacements (StringBuilder value, Dictionary<string, string> replacements)
		{
			var text = new StringBuilder ();
			text.Append (value);

			foreach (var kvp in replacements) {
				string placeholder = kvp.Key;
				string replacement = kvp.Value;

				text.Replace (placeholder, replacement);
			}

			return text;
		}

		void WriteProperty (StreamWriter sw, string indent, string name, StringBuilder value, Dictionary<string, string> replacements)
		{
			var text = ApplyReplacements (value, replacements);
			sw.WriteLine ($"{indent}<{name}>{text}</{name}>");
		}

		void WriteMSBuildProjectStart (StreamWriter sw)
		{
			sw.WriteLine ("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
			sw.WriteLine ("<Project ToolsVersion=\"4.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
		}

		void WriteMSBuildProjectEnd (StreamWriter sw)
		{
			sw.WriteLine ("</Project>");
		}

		string GetRelativeToolchainDefinitionPath ()
		{
			return Path.DirectorySeparatorChar + Path.Combine ("build", "cmake", "android.toolchain.cmake");
		}

		void AddReplacements (Dictionary<string, string> source, Dictionary<string, string> target)
		{
			foreach (var kvp in source) {
				target.Add (kvp.Key, kvp.Value);
			}
		}
	}
}
