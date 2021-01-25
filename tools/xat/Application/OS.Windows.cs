#if WINDOWS
namespace Xamarin.Android.Tests
{
	partial class OS
	{
		public const string Name = "Windows";
		public const string NDKName = "windows-x86_64";

		public static readonly StringComparer FilePathComparer = StringComparer.OrdinalIgnoreCase;
		public static readonly StringComparison FilePathComparison = StringComparison.OrdinalIgnoreCase;

		static readonly char[] PathExtSplit = new char[]{';'};

		List<string>? ExecutableExtensions => null;

		void InitOS ()
		{
			string[]? pathext = Environment.GetEnvironmentVariable ("PATHEXT")?.Split (PathExtSplit, StringSplitOptions.RemoveEmptyEntries);
			if (pathext == null || pathext.Length == 0) {
				executableExtensions = new List<string> {
					".exe",
					".cmd",
					".bat"
				};
			} else {
				executableExtensions = new List<string> ();
				foreach (string ext in pathext) {
					executableExtensions.Add (ext.ToLowerInvariant ());
				}
			}
		}

		public static string AppendExecutableExtension (string filePath)
		{
			return $"{filePath}{ExecutableExtension}";
		}

		public static string AssertIsExecutable (string filePath)
		{
			return filePath;
		}

		public static bool IsExecutable (string filePath)
		{
			if (!IsExecutableEnsureValidFile (filePath)) {
				return false;
			}

			return true;
		}

		public string GetManagedProgramRunner (string programPath)
		{
			return String.Empty;
		}
	}
}
#endif
