using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Java.Interop.Tools.Cecil;
using Java.Interop.Tools.Diagnostics;
using Java.Interop.Tools.JavaCallableWrappers.Adapters;
using Java.Interop.Tools.JavaCallableWrappers.CallableWrapperMembers;
using Java.Interop.Tools.JavaCallableWrappers;
using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks;

class JCWGeneratorContext
{
	public bool UseMarshalMethods                    { get; }
	public AndroidTargetArch Arch                    { get; }
	public TypeDefinitionCache TypeDefinitionCache   { get; }
	public XAAssemblyResolver Resolver               { get; }
	public IList<TypeDefinition> JavaTypes           { get; }
	public ICollection<ITaskItem> ResolvedAssemblies { get; }

	public JCWGeneratorContext (AndroidTargetArch arch, XAAssemblyResolver res, ICollection<ITaskItem> resolvedAssemblies, List<TypeDefinition> javaTypesForJCW, TypeDefinitionCache tdCache, bool useMarshalMethods)
	{
		Arch = arch;
		Resolver = res;
		ResolvedAssemblies = resolvedAssemblies;
		JavaTypes = javaTypesForJCW.AsReadOnly ();
		TypeDefinitionCache = tdCache;
		UseMarshalMethods = useMarshalMethods;
	}
}

class JCWGenerator
{
	readonly TaskLoggingHelper log;
	readonly JCWGeneratorContext context;
	readonly string intermediateOutputDirectory;

	public MarshalMethodsClassifier? Classifier { get; private set; }

	public JCWGenerator (TaskLoggingHelper log, JCWGeneratorContext context, string intermediateOutputDirectory)
	{
		this.log = log;
		this.context = context;
		this.intermediateOutputDirectory = intermediateOutputDirectory;
	}

	/// <summary>
	/// Performs marshal method classification, if marshal methods are used, but does not generate any code.
	/// If marshal methods are used, this method will set the <see cref="Classifier"/> property to a valid
	/// classifier instance on return.  If marshal methods are disabled, this call is a no-op but it will
	/// return <c>true</c>.
	/// </summary>
	public bool Classify (string androidSdkPlatform)
	{
		if (!context.UseMarshalMethods) {
			return true;
		}

		Classifier = MakeClassifier ();
		return ProcessTypes (
			generateCode: false,
			androidSdkPlatform,
			Classifier,
			outputPath: null,
			applicationJavaClass: null
		);
	}

	public bool GenerateAndClassify (string androidSdkPlatform, string outputPath, string applicationJavaClass)
	{
		if (context.UseMarshalMethods) {
			Classifier = MakeClassifier ();
		}

		return ProcessTypes (
			generateCode: true,
			androidSdkPlatform,
			Classifier,
			outputPath,
			applicationJavaClass
		);
	}

	MarshalMethodsClassifier MakeClassifier () => new MarshalMethodsClassifier (context.Arch, context.TypeDefinitionCache, context.Resolver, intermediateOutputDirectory, log);

	bool ProcessTypes (bool generateCode, string androidSdkPlatform, MarshalMethodsClassifier? classifier, string? outputPath, string? applicationJavaClass)
	{
		if (generateCode && String.IsNullOrEmpty (outputPath)) {
			throw new ArgumentException ("must not be null or empty", nameof (outputPath));
		}

		string monoInit = GetMonoInitSource (androidSdkPlatform);
		bool hasExportReference = context.ResolvedAssemblies.Any (assembly => Path.GetFileName (assembly.ItemSpec) == "Mono.Android.Export.dll");
		bool ok = true;

		foreach (TypeDefinition type in context.JavaTypes) {
			if (type.IsInterface) {
				// Interfaces are in typemap but they shouldn't have JCW generated for them
				continue;
			}

			CallableWrapperType generator = CreateGenerator (type, classifier, monoInit, hasExportReference, applicationJavaClass);
			if (!generateCode) {
				continue;
			}

			if (!GenerateCode (generator, type, outputPath, hasExportReference, classifier)) {
				ok = false;
			}
		}

		return ok;
	}

	bool GenerateCode (CallableWrapperType generator, TypeDefinition type, string outputPath, bool hasExportReference, MarshalMethodsClassifier? classifier)
	{
		bool ok = true;
		using var writer = MemoryStreamPool.Shared.CreateStreamWriter ();
		var writer_options = new CallableWrapperWriterOptions {
			CodeGenerationTarget    = JavaPeerStyle.XAJavaInterop1
		};

		try {
			generator.Generate (writer, writer_options);
			if (context.UseMarshalMethods) {
				if (classifier.FoundDynamicallyRegisteredMethods (type)) {
					log.LogWarning ($"Type '{type.GetAssemblyQualifiedName (context.TypeDefinitionCache)}' will register some of its Java override methods dynamically. This may adversely affect runtime performance. See preceding warnings for names of dynamically registered methods.");
				}
			}
			writer.Flush ();

			string path = generator.GetDestinationPath (outputPath);
			Files.CopyIfStreamChanged (writer.BaseStream, path);
			if (generator.HasExport && !hasExportReference) {
				Diagnostic.Error (4210, Properties.Resources.XA4210);
			}
		} catch (XamarinAndroidException xae) {
			ok = false;
			log.LogError (
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
				Diagnostic.Error (5301, Properties.Resources.XA5301, type.FullName, ex);
			} else {
				Diagnostic.Error (4209, Properties.Resources.XA4209, type.FullName, ex);
			}
		} catch (Exception ex) {
			ok = false;
			Diagnostic.Error (4209, Properties.Resources.XA4209, type.FullName, ex);
		}

		return ok;
	}

	CallableWrapperType CreateGenerator (TypeDefinition type, MarshalMethodsClassifier? classifier, string monoInit, bool hasExportReference, string? applicationJavaClass)
	{
		var reader_options = new CallableWrapperReaderOptions {
			DefaultApplicationJavaClass         = applicationJavaClass,
			DefaultGenerateOnCreateOverrides    = false, // this was used only when targetting Android API <= 10, which is no longer supported
			DefaultMonoRuntimeInitialization    = monoInit,
			MethodClassifier                    = classifier,
		};

		return CecilImporter.CreateType (type, context.TypeDefinitionCache, reader_options);
	}

	static string GetMonoInitSource (string androidSdkPlatform)
	{
		if (String.IsNullOrEmpty (androidSdkPlatform)) {
			throw new ArgumentException ("must not be null or empty", nameof (androidSdkPlatform));
		}

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
		using var s = assembly.GetManifestResourceStream ($"MonoRuntimeProvider.{runtime}{api}.java");
		using var reader = new StreamReader (s);
		bool copy = false;
		string? line;
		while ((line = reader.ReadLine ()) != null) {
			if (string.CompareOrdinal ("\t\t// Mono Runtime Initialization {{{", line) == 0) {
				copy = true;
			}

			if (copy) {
				builder.AppendLine (line);
			}

			if (string.CompareOrdinal ("\t\t// }}}", line) == 0) {
				break;
			}
		}

		return builder.ToString ();
	}

	public static void EnsureAllArchitecturesAreIdentical (TaskLoggingHelper logger, Dictionary<AndroidTargetArch, NativeCodeGenState> javaStubStates)
	{
		if (javaStubStates.Count <= 1) {
			return;
		}

		// An expensive process, but we must be sure that all the architectures have the same data
		NativeCodeGenState? templateState = null;
		foreach (var kvp in javaStubStates) {
			NativeCodeGenState state = kvp.Value;

			if (templateState == null) {
				templateState = state;
				continue;
			}

			EnsureIdenticalCollections (logger, templateState, state);
			EnsureClassifiersMatch (logger, templateState, state);
		}
	}

	static void EnsureIdenticalCollections (TaskLoggingHelper logger, NativeCodeGenState templateState, NativeCodeGenState state)
	{
		logger.LogDebugMessage ($"Ensuring Java type collection in architecture '{state.TargetArch}' matches the one in architecture '{templateState.TargetArch}'");

		List<TypeDefinition> templateTypes = templateState.AllJavaTypes;
		List<TypeDefinition> types = state.AllJavaTypes;

		if (types.Count != templateTypes.Count) {
			throw new InvalidOperationException ($"Internal error: architecture '{state.TargetArch}' has a different number of types ({types.Count}) than the template architecture '{templateState.TargetArch}' ({templateTypes.Count})");
		}

		var matchedTemplateTypes = new HashSet<TypeDefinition> ();
		var mismatchedTypes = new List<TypeDefinition> ();

		foreach (TypeDefinition type in types) {
			TypeDefinition? matchedType = null;

			foreach (TypeDefinition templateType in templateTypes) {
				if (matchedTemplateTypes.Contains (templateType) || !CheckWhetherTypesMatch (templateType, type)) {
					continue;
				}

				matchedTemplateTypes.Add (templateType);
				matchedType = templateType;
				break;
			}

			if (matchedType == null) {
				mismatchedTypes.Add (type);
			}
		}

		if (mismatchedTypes.Count > 0) {
			logger.LogError ($"Architecture '{state.TargetArch}' has Java types which have no counterparts in template architecture '{templateState.TargetArch}':");
			foreach (TypeDefinition td in mismatchedTypes) {
				logger.LogError ($"  {td.FullName}");
			}
		}
	}

	static bool CheckWhetherTypesMatch (TypeDefinition templateType, TypeDefinition type)
	{
		// TODO: should we compare individual methods, fields, properties?
		return String.Compare (templateType.FullName, type.FullName, StringComparison.Ordinal) == 0;
	}

	static void EnsureClassifiersMatch (TaskLoggingHelper logger, NativeCodeGenState templateState, NativeCodeGenState state)
	{
		logger.LogDebugMessage ($"Ensuring marshal method classifier in architecture '{state.TargetArch}' matches the one in architecture '{templateState.TargetArch}'");

		MarshalMethodsClassifier? templateClassifier = templateState.Classifier;
		MarshalMethodsClassifier? classifier = state.Classifier;

		if (templateClassifier == null) {
			if (classifier != null) {
				throw new InvalidOperationException ($"Internal error: architecture '{templateState.TargetArch}' DOES NOT have a marshal methods classifier, unlike architecture '{state.TargetArch}'");
			}
			return;
		}

		if (classifier == null) {
			throw new InvalidOperationException ($"Internal error: architecture '{templateState.TargetArch}' DOES have a marshal methods classifier, unlike architecture '{state.TargetArch}'");
		}

		if (templateClassifier.MarshalMethods.Count != classifier.MarshalMethods.Count) {
			throw new InvalidOperationException (
				$"Internal error: classifier for template architecture '{templateState.TargetArch}' contains {templateClassifier.MarshalMethods.Count} marshal methods, but the one for architecture '{state.TargetArch}' has {classifier.MarshalMethods.Count}"
			);
		}

		var matchedTemplateMethods = new HashSet<MethodDefinition> ();
		var mismatchedMethods = new List<MethodDefinition> ();
		bool foundMismatches = false;

		foreach (var kvp in classifier.MarshalMethods) {
			string key = kvp.Key;
			IList<MarshalMethodEntry> methods = kvp.Value;

			logger.LogDebugMessage ($"Comparing marshal method '{key}' in architecture '{templateState.TargetArch}', with {methods.Count} overloads, against architecture '{state.TargetArch}'");

			if (!templateClassifier.MarshalMethods.TryGetValue (key, out IList<MarshalMethodEntry> templateMethods)) {
				logger.LogDebugMessage ($"Architecture '{state.TargetArch}' has marshal method '{key}' which does not exist in architecture '{templateState.TargetArch}'");
				foundMismatches = true;
				continue;
			}

			if (methods.Count != templateMethods.Count) {
				logger.LogDebugMessage ($"Architecture '{state.TargetArch}' has an incorrect number of marshal method '{key}' overloads. Expected {templateMethods.Count}, but found {methods.Count}");
				continue;
			}

			foreach (MarshalMethodEntry templateMethod in templateMethods) {
				MarshalMethodEntry? match = null;

				foreach (MarshalMethodEntry method in methods) {
					if (CheckWhetherMethodsMatch (logger, templateMethod, templateState.TargetArch, method, state.TargetArch)) {
						match = method;
						break;
					}
				}

				if (match == null) {
					foundMismatches = true;
				}
			}
		}

		if (!foundMismatches) {
			return;
		}

		logger.LogError ($"Architecture '{state.TargetArch}' doesn't match all marshal methods in architecture '{templateState.TargetArch}'. Please see detailed MSBuild logs for more information.");
	}

	static bool CheckWhetherMethodsMatch (TaskLoggingHelper logger, MarshalMethodEntry templateMethod, AndroidTargetArch templateArch, MarshalMethodEntry method, AndroidTargetArch arch)
	{
		bool success = true;
		string methodName = templateMethod.NativeCallback.FullName;

		if (!CheckWhetherTypesMatch (templateMethod.DeclaringType, method.DeclaringType)) {
			logger.LogDebugMessage ($"Marshal method '{methodName}' for architecture '{arch}' should be declared in type '{templateMethod.DeclaringType.FullName}', but instead was declared in '{method.DeclaringType.FullName}'");
			success = false;
		}

		bool skipJniCheck = false;
		if (!CheckWhetherMembersMatch (logger, methodName, "native callback", templateMethod.NativeCallback, templateArch, method.NativeCallback, arch)) {
			success = false;

			// This takes care of overloads for the same methods, and avoids false negatives below
			skipJniCheck = true;
		}

		if (!skipJniCheck) {
			if (String.Compare (templateMethod.JniMethodName, method.JniMethodName, StringComparison.Ordinal) != 0) {
				logger.LogDebugMessage ($"Marshal method '{methodName}' for architecture '{arch}' has a different JNI method name than architecture '{templateArch}':");
				logger.LogDebugMessage ($"  Expected: '{templateMethod.JniMethodName}', found: '{method.JniMethodName}'");
				success = false;
			}

			if (String.Compare (templateMethod.JniMethodSignature, method.JniMethodSignature, StringComparison.Ordinal) != 0) {
				logger.LogDebugMessage ($"Marshal method '{methodName}' for architecture '{arch}' has a different JNI method signature than architecture '{templateArch}':");
				logger.LogDebugMessage ($"  Expected: '{templateMethod.JniMethodSignature}', found: '{method.JniMethodSignature}'");
				success = false;
			}

			if (String.Compare (templateMethod.JniTypeName, method.JniTypeName, StringComparison.Ordinal) != 0) {
				logger.LogDebugMessage ($"Marshal method '{methodName}' for architecture '{arch}' has a different JNI type name than architecture '{templateArch}':");
				logger.LogDebugMessage ($"  Expected: '{templateMethod.JniTypeName}', found: '{method.JniTypeName}'");
				success = false;
			}
		}

		if (templateMethod.IsSpecial) {
			// Other method definitions will be `null`, so we can skip them
			if (method.IsSpecial) {
				return success;
			}

			logger.LogDebugMessage ($"Marshal method '{templateMethod.NativeCallback.FullName}' is marked as special in architecture '{templateArch}', but not in architecture '{arch}'");
			return false;
		}

		if (!CheckWhetherMembersMatch (logger, methodName, "connector", templateMethod.Connector, templateArch, method.Connector, arch)) {
			success = false;
		}

		if (!CheckWhetherMembersMatch (logger, methodName, "implemented", templateMethod.ImplementedMethod, templateArch, method.ImplementedMethod, arch)) {
			success = false;
		}

		if (!CheckWhetherMembersMatch (logger, methodName, "registered", templateMethod.RegisteredMethod, templateArch, method.RegisteredMethod, arch)) {
			success = false;
		}

		if (!CheckWhetherMembersMatch (logger, methodName, "callback backing field", templateMethod.CallbackField, templateArch, method.CallbackField, arch)) {
			success = false;
		}

		return success;
	}

	static bool CheckWhetherMembersMatch (TaskLoggingHelper logger, string marshalMethodName, string description, MemberReference? templateMethod, AndroidTargetArch templateArch, MemberReference? method, AndroidTargetArch arch)
	{
		if (templateMethod == null) {
			if (method == null) {
				return true;
			}

			logger.LogDebugMessage ($"Marshal method '{marshalMethodName}' component '{description}' is null in architecture '{templateArch}', but not null in architecture '{arch}'");
			return false;
		}

		return String.Compare (templateMethod.FullName, method.FullName, StringComparison.Ordinal) == 0;
	}
}
