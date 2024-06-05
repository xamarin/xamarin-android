using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Xamarin.Android.Tools;
using Xamarin.Build;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tasks;

public class LinkNativeRuntime : AndroidAsyncTask
{
	class Archive
	{
		public readonly string Name;
		public readonly Func<Archive, bool> Include = (Archive) => true;

		public Archive (string name, Func<Archive, bool>? include = null)
		{
			Name = name;
			if (include != null) {
				Include = include;
			}
		}
	}

	class MonoComponentArchive : Archive
	{
		public readonly string ComponentName;

		public MonoComponentArchive (string name, string componentName, Func<Archive, bool> include)
			: base (name, include)
		{
			ComponentName = componentName;
		}
	}

	readonly List<Archive> KnownArchives;

	public override string TaskPrefix => "LNR";

	public ITaskItem[] MonoComponents { get; set; }

	[Required]
	public string AndroidBinUtilsDirectory { get; set; }

	public LinkNativeRuntime ()
	{
		KnownArchives = new () {
			new MonoComponentArchive ("libmono-component-diagnostics_tracing-static.a", "diagnostics_tracing", MonoComponentPresent),
			new MonoComponentArchive ("libmono-component-diagnostics_tracing-stub-static.a", "diagnostics_tracing", MonoComponentAbsent),
			new MonoComponentArchive ("libmono-component-marshal-ilgen-static.a", "marshal-ilgen", MonoComponentPresent),
			new MonoComponentArchive ("libmono-component-marshal-ilgen-stub-static.a", "marshal-ilgen", MonoComponentAbsent),

			new Archive ("libmonosgen-2.0.a"),
			new Archive ("libSystem.Globalization.Native.a"),
			new Archive ("libSystem.IO.Compression.Native.a"),
			new Archive ("libSystem.Native.a"),
			new Archive ("libSystem.Security.Cryptography.Native.Android.a"),
		};
	}

	public override System.Threading.Tasks.Task RunTaskAsync ()
	{
		throw new NotImplementedException ();
	}

	bool MonoComponentExists (Archive archive)
	{
		if (MonoComponents == null || MonoComponents.Length == 0) {
			return false;
		}

		var mcArchive = archive as MonoComponentArchive;
		if (mcArchive == null) {
			throw new ArgumentException (nameof (archive), "Must be an instance of MonoComponentArchive");
		}

		foreach (ITaskItem item in MonoComponents) {
			if (String.Compare (item.ItemSpec, mcArchive.ComponentName, StringComparison.OrdinalIgnoreCase) == 0) {
				return true;
			}
		}

		return false;
	}

	bool MonoComponentAbsent (Archive archive)
	{
		return !MonoComponentExists (archive);
	}

	bool MonoComponentPresent (Archive archive)
	{
		return MonoComponentExists (archive);
	}
}
