using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

using Xamarin.Android.Application.Utilities;
using Xamarin.Android.AssemblyStore;
using Xamarin.Tools.Zip;

namespace Xamarin.Android.Application;

class DataProviderAppInfo : DataProvider
{
	const string SignatureDirPath = "META-INF";

	InputReaderZip reader;

	public string ApplicationName              { get; private set; } = String.Empty;
	public string ApplicationLabel             { get; private set; } = String.Empty;
	public string ArchiveType                  => reader.ArchiveType;
	public bool ExtractsNativeLibs             { get; private set; }
	public bool HasRuntimeConfigBlob           { get; private set; }
	public bool HasAssemblyStoresManifest      { get; private set; }
	public bool IsClassicXA                    { get; private set; }
	public bool IsDebug                        { get; private set; }
	public bool IsProfileable                  { get; private set; }
	public bool IsSigned                       { get; private set; }
	public bool IsTestOnly                     { get; private set; }
	public string MainActivityName             { get; private set; } = String.Empty;
	public string MinSdkVersion                { get; private set; } = String.Empty;
	public uint NumberOfAbiAssemblyStores      { get; private set; } = 0;
	public string PackageName                  { get; private set; } = String.Empty;
	public string[] SupportedAbis              { get; private set; }
	public string TargetSdkVersion             { get; private set; } = String.Empty;
	public uint TotalNumberOfAssemblyStores    { get; private set; } = 0;
	public bool UsesAOT                        { get; private set; }
	public bool UsesAssemblyStores             { get; private set; }
	public ICollection<string> UsesPermissions { get; private set; } = new List<string> ();

	public DataProviderAppInfo (InputReaderZip reader, ILogger log)
		: base (reader.ArchivePath, log)
	{
		this.reader = reader;

		HasRuntimeConfigBlob = HasFile (reader.AssembliesDirPath, "rc.bin");
		// TODO: full IsClassicXA detection
		if (!HasRuntimeConfigBlob) {
		}

		XmlDocument? manifest = LoadManifest (log, out XmlNamespaceManager? nsManager);
		if (manifest == null) {
                        log.WarningLine ("Unable to parse Android manifest from the apk");
		} else {
			if (nsManager == null) {
				throw new InvalidOperationException ("Internal error: nsManager must not be null");
			}

			// TODO: some strings can refer to resources, parse them
			XmlNode? node = manifest.SelectSingleNode ("//manifest", nsManager);
			PackageName = GetAttributeValue (node, "package") ?? String.Empty;

			node = manifest.SelectSingleNode ("//manifest/uses-sdk", nsManager);
			MinSdkVersion = GetAttributeValue (node, "android:minSdkVersion") ?? String.Empty;
			TargetSdkVersion = GetAttributeValue (node, "android:targetSdkVersion") ?? String.Empty;

			node = manifest.SelectSingleNode ("//manifest/application", nsManager);
			IsDebug = GetBoolAttributeValue (node, "android:debuggable");
			IsTestOnly = GetBoolAttributeValue (node, "android:testOnly");
			ExtractsNativeLibs = GetBoolAttributeValue (node, "android:extractNativeLibs");
			ApplicationName = GetAttributeValue (node, "android:name") ?? String.Empty;
			ApplicationLabel = GetAttributeValue (node, "android:label") ?? String.Empty;

			node = manifest.SelectSingleNode ("//manifest/application/profileable", nsManager);
			IsProfileable = GetBoolAttributeValue (node, "android:enabled");

			node = manifest.SelectSingleNode ("//manifest/application/activity[./intent-filter/action[@android:name='android.intent.action.MAIN']]", nsManager);
			MainActivityName = GetAttributeValue (node, "android:name") ?? String.Empty;

			CollectPermissions (manifest, nsManager, UsesPermissions);
		}

		IsSigned = HasFile (SignatureDirPath, "ANDROIDD.RSA") || HasFile (SignatureDirPath, "BNDLTOOL.RSA");
		UsesAssemblyStores = HasFile (reader.AssembliesDirPath, "assemblies.blob");
		if (UsesAssemblyStores) {
			TotalNumberOfAssemblyStores = 1;
			HasAssemblyStoresManifest = HasFile (reader.AssembliesDirPath, "assemblies.manifest");
		}

		string libDirLead = $"{reader.NativeLibsDirPath}/";
		string assembliesDirLead = $"{reader.AssembliesDirPath}/";

		var abis = new HashSet<string> (StringComparer.Ordinal);

		foreach (ZipEntry entry in reader.Archive) {
			if (UsesAssemblyStores) {
				if (entry.FullName.StartsWith (assembliesDirLead, StringComparison.Ordinal) && IsAssemblyAbiBlob (entry.FullName)) {
					TotalNumberOfAssemblyStores++;
					NumberOfAbiAssemblyStores++;
				}
			}

			if (!entry.FullName.StartsWith (libDirLead, StringComparison.Ordinal)) {
				continue;
			}

			string? dir = Path.GetDirectoryName (entry.FullName);
			string? file = Path.GetFileName (entry.FullName);

			if (String.IsNullOrEmpty (dir) || String.IsNullOrEmpty (file)) {
				continue;
			}

			string? abi = Path.GetFileName (dir);
			if (!abis.Contains (abi!)) {
				abis.Add (abi!);
			}

			if (!UsesAOT && file.StartsWith ("libaot-", StringComparison.Ordinal)) {
				UsesAOT = true;
			}
		}

		SupportedAbis = new string[abis.Count];
		abis.CopyTo (SupportedAbis);
	}

	XmlDocument? LoadManifest (ILogger log, out XmlNamespaceManager? nsManager)
	{
		nsManager = null;

		// TODO: implement support for AAB AndroidManifest.xml, it's not in the same binary XML format as in the APK. It can be dumped with: bundletool dump manifest --bundle
		string manifestPath = MakeZipPath (reader.ManifestDirPath, InputReaderZip.AndroidManifestName);
		log.DebugLine ($"Trying to load and parse Android manifest from the archive: {manifestPath}");

		ZipEntry manifestEntry = reader.Archive.ReadEntry (manifestPath);
		using var manifestData = new MemoryStream ();
		manifestEntry.Extract (manifestData);
		manifestData.Seek (0, SeekOrigin.Begin);

		XmlDocument? manifest;
		try {
			var axml = new AXMLParser (manifestData, log);
			manifest = axml.Parse ();
			if (manifest == null) {
				return null;
			}
		} catch (Exception ex) {
			log.DebugLine ("Failed to parse Android manifest.");
			log.DebugLine (ex.ToString ());
			return null;
		}

		XmlNode? node = manifest.SelectSingleNode ("//manifest");
                if (node == null) {
                        log.ErrorLine ("Unable to find root element 'manifest' of AndroidManifest.xml");
                        return null;
                }

		nsManager = new XmlNamespaceManager (manifest.NameTable);
                if (node.Attributes != null) {
                        const string nsPrefix = "xmlns:";

                        foreach (XmlAttribute attr in node.Attributes) {
                                if (!attr.Name.StartsWith (nsPrefix, StringComparison.Ordinal)) {
                                        continue;
                                }

                                nsManager.AddNamespace (attr.Name.Substring (nsPrefix.Length), attr.Value);
                        }
                }

		return manifest;
	}

	static void CollectPermissions (XmlNode manifest, XmlNamespaceManager nsManager, ICollection<string> permissions)
	{
		XmlNodeList? nodes = manifest.SelectNodes ("//manifest/uses-permission", nsManager);
		if (nodes == null) {
			return;
		}

		foreach (XmlNode node in nodes) {
			string? permission = GetAttributeValue (node, "android:name");
			if (String.IsNullOrEmpty (permission)) {
				continue;
			}

			permissions.Add (permission);
		}
	}

	static bool GetBoolAttributeValue (XmlNode? node, string prefixedAttributeName, bool defaultValue = false)
	{
		string? val = GetAttributeValue (node, prefixedAttributeName)?.ToLowerInvariant ();
		if (String.IsNullOrEmpty (val) || !Boolean.TryParse (val, out bool ret)) {
			return defaultValue;
		}

		return ret;
	}

	static string? GetAttributeValue (XmlNode? node, string prefixedAttributeName)
        {
                if (node?.Attributes == null) {
                        return null;
                }

                foreach (XmlAttribute attr in node.Attributes) {
                        if (String.Compare (prefixedAttributeName, attr.Name, StringComparison.Ordinal) == 0) {
                                return attr.Value;
                        }
                }

                return null;
        }

	bool IsAssemblyAbiBlob (string entryName) => !String.IsNullOrEmpty (AssemblyStoreReader.GetBlobArchitecture (entryName));

	bool HasFile (string path, string fileName, bool caseSensitive = true) => reader.Archive.ContainsEntry (MakeZipPath (path, fileName), caseSensitive);

	static string MakeZipPath (string dirPath, string fileName)
	{
		if (String.IsNullOrEmpty (dirPath))
			return fileName;
		return $"{dirPath}/{fileName}";
	}
}
