using Mono.Cecil;
using System.IO;

namespace Java.Interop.Tools.Cecil;

public static class AssemblyResolverCoda
{
	public static AssemblyDefinition GetAssembly (this IAssemblyResolver resolver, string fileName)
	{
		return resolver.Resolve (AssemblyNameReference.Parse (Path.GetFileNameWithoutExtension (fileName)));
	}
}
