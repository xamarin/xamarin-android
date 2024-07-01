using System.Collections.Generic;

using Java.Interop.Tools.Cecil;
using Mono.Cecil;
using Xamarin.Android.Tools;

namespace Xamarin.Android.Tasks;

/// <summary>
/// Holds state for typemap and marshal methods generators.  A single instance of this
/// class is created for each enabled target architecture.
/// </summary>
class NativeCodeGenState
{
	public static NativeCodeGenState? Template                 { get; set; }

	/// <summary>
	/// Target architecture for which this instance was created.
	/// </summary>
	public AndroidTargetArch TargetArch                        { get; }

	/// <summary>
	/// Classifier used when scanning for Java types in the target architecture's
	/// assemblies.  Will be **null** if marshal methods are disabled.
	/// </summary>
	public MarshalMethodsClassifier? Classifier                { get; }

	/// <summary>
	/// All the Java types discovered in the target architecture's assemblies.
	/// </summary>
	public List<TypeDefinition> AllJavaTypes                   { get; }

	/// <summary>
	/// Contains information about p/invokes used by the managed assemblies included in the
	/// application.  Will be **null** unless native runtime linking at application build time
	/// is enabled.
	/// </summary>
	public List<PinvokeScanner.PinvokeEntryInfo>? PinvokeInfos { get; set; }

	public List<TypeDefinition> JavaTypesForJCW                { get; }
	public XAAssemblyResolver Resolver                         { get; }
	public TypeDefinitionCache TypeCache                       { get; }
	public bool JniAddNativeMethodRegistrationAttributePresent { get; set; }

	public NativeCodeGenState (AndroidTargetArch arch, TypeDefinitionCache tdCache, XAAssemblyResolver resolver, List<TypeDefinition> allJavaTypes, List<TypeDefinition> javaTypesForJCW, MarshalMethodsClassifier? classifier)
	{
		TargetArch = arch;
		TypeCache = tdCache;
		Resolver = resolver;
		AllJavaTypes = allJavaTypes;
		JavaTypesForJCW = javaTypesForJCW;
		Classifier = classifier;
	}
}
