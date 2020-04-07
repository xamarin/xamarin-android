using System;
using System.IO;

namespace Xamarin.Android.Prepare
{
	partial class Configurables
	{
		partial class Urls
		{
			public static Uri Corretto => GetWindowsCorrettoUrl ();

			public static readonly Uri Corretto64 = new Uri ($"{Corretto_BaseUri}{CorrettoUrlPathVersion}/amazon-corretto-{CorrettoDistVersion}-windows-x64-jdk.zip");
			public static readonly Uri Corretto32 = new Uri ($"{Corretto_BaseUri}{CorrettoUrlPathVersion}/amazon-corretto-{CorrettoDistVersion}-windows-x86-jdk.zip");
			public static readonly Uri MSOpenJDK32 = new Uri ($"https://download.visualstudio.microsoft.com/download/pr/473aa299-9b5a-43d0-8c0a-080cc4ccc872/8063625a1f88d79982b053833910c9bc/microsoft_dist_openjdk_{MSOpenJDKVersion}.zip");
			public static readonly Uri MSOpenJDK64 = new Uri ($"https://download.visualstudio.microsoft.com/download/pr/8004dd28-3df5-44ce-8fb6-8cd83a7420f3/e641cf71d591e30d04403fbecee230a2/microsoft_dist_openjdk_{MSOpenJDKVersion}.zip");

			static Uri GetWindowsCorrettoUrl ()
			{
				if (Context.Instance.OS == null)
					return Corretto64;

				return Context.Instance.OS.Is64Bit ? Corretto64 : Corretto32;
			}
		}

		partial class Defaults
		{
			public const string NativeLibraryExtension = ".dll";
			public static readonly Version CorrettoVersion = Version.Parse (CorrettoDistVersion);
		}

		partial class Paths
		{
			static string ArchiveOSType                                           => "Darwin"; // Windows need sources from there
			public const string MonoCrossRuntimeInstallPath                       = "Windows";
			public static readonly string MonoRuntimeHostMingwNativeLibraryPrefix = Path.Combine ("..", "bin");
			public const string NdkToolchainOSTag                                 = "windows-x86_64";
			public static readonly string MSOpenJDKInstallRoot = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.ProgramFiles), "Android", "Jdk");
		}
	}
}
