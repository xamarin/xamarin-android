using System;
using System.IO;

namespace Xamarin.Android.Prepare
{
	class MonoJitRuntime : MonoRuntime
	{
		public override string Flavor => "Android JIT";

		public MonoJitRuntime (string abiName, bool interpreter, Func<Context, bool> enabledCheck)
			: base (abiName, enabledCheck)
		{
			if (interpreter) {
				MonoSdksPrefix = "interpreter-";
				DisplayName = $"{abiName} (interpreter)";
			}
		}

		public override void Init (Context context)
		{
			InstallPath                   = "lib";
			NativeLibraryExtension        = Configurables.Defaults.MonoJitRuntimeNativeLibraryExtension;
			OutputAotProfilerFilename     = Configurables.Defaults.MonoRuntimeOutputAotProfilerFilename;
			OutputMonoBtlsFilename        = Configurables.Defaults.MonoRuntimeOutputMonoBtlsFilename;
			OutputMonoPosixHelperFilename = Configurables.Defaults.MonoRuntimeOutputMonoPosixHelperFilename;
			OutputProfilerFilename        = Configurables.Defaults.MonoRuntimeOutputProfilerFilename;
			Strip                         = Path.Combine (Configurables.Paths.AndroidToolchainBinDirectory, "llvm-strip");
		}
	}
}
