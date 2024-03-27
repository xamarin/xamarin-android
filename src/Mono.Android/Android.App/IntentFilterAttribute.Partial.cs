using System;

namespace Android.App;

public sealed partial class IntentFilterAttribute
{
	public IntentFilterAttribute (string [] actions)
	{
		if (actions == null)
			throw new ArgumentNullException ("actions");
		if (actions.Length < 1)
			throw new ArgumentException ("At least one action must be specified.", "actions");
		Actions = actions;
	}

	public string [] Actions { get; }
	public string []? Categories { get; set; }
	public string? DataHost { get; set; }
	public string? DataMimeType { get; set; }
	public string? DataPath { get; set; }
	public string? DataPathPattern { get; set; }
	public string? DataPathPrefix { get; set; }
	public string? DataPort { get; set; }
	public string? DataScheme { get; set; }
	public string []? DataHosts { get; set; }
	public string []? DataMimeTypes { get; set; }
	public string []? DataPaths { get; set; }
	public string []? DataPathPatterns { get; set; }
	public string []? DataPathPrefixes { get; set; }
	public string []? DataPorts { get; set; }
	public string []? DataSchemes { get; set; }
	public string? DataPathAdvancedPattern { get; set; }
	public string []? DataPathAdvancedPatterns { get; set; }
	public string? DataPathSuffix { get; set; }
	public string []? DataPathSuffixes { get; set; }
}
