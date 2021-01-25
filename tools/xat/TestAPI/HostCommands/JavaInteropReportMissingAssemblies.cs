using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests.Host
{
	class JavaInteropReportMissingAssemblies : HostTestCommand
	{
		string testAssemblyGlob;
		HashSet<string> knownAssemblies;

		public JavaInteropReportMissingAssemblies (string testAssemblyGlob, IEnumerable<string> knownAssemblies)
			: base (nameof (JavaInteropReportMissingAssemblies), "Report test assemblies that aren't in XAT but are in the JI repository")
		{
			this.testAssemblyGlob = EnsureParameterValue (nameof (testAssemblyGlob), testAssemblyGlob);
			this.knownAssemblies = new HashSet<string> (StringComparer.OrdinalIgnoreCase);

			foreach (string assembly in knownAssemblies) {
				this.knownAssemblies.Add (assembly);
			}
		}

#pragma warning disable 1998
		protected async override Task<bool> Run (TestHostUnit test)
		{
			bool foundMissing = false;

			Log.DebugLine ($"Looking for missing JI test assemblies: {testAssemblyGlob}");
			foreach (string jiAssembly in Directory.EnumerateFiles (Path.GetDirectoryName (testAssemblyGlob), Path.GetFileName (testAssemblyGlob))) {
				string fileName = Path.GetFileName (jiAssembly);
				if (knownAssemblies.Contains (fileName)) {
					continue;
				}

				foundMissing = true;
				Log.Warning ($"Unused Java.Interop test assembly: ");
				Log.WarningLine (fileName, ConsoleColor.Cyan, showSeverity: false);
			}

			return !foundMissing;
		}
#pragma warning restore 1998
	}
}
