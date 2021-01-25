using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xamarin.Android.Tests;

namespace Xamarin.Android.Prepare
{
	/// <summary>
	///   A singleton class which holds global state/information for other parts of the application.  Partially shared
	///   with <c>xaprepare</c>
	/// </summary>
	partial class Context
	{
		readonly object testCollectionLock = new object ();
		public static readonly char[] NewlineSplit = new [] { '\n' };
		public static readonly char[] WhitespaceSplit = new [] { ' ', '\t' };

		TestCollection? testCollection;
		Characters? characters;

		/// <summary>
		///   Return the single instance of Context.
		/// </summary>
		public static Context Instance           { get; } = new Context ();

		/// <summary>
		///   Describes the current operating system. See <see cref="Xamarin.Android.Prepare.OS"/>
		/// </summary>
		public OS OS                             { get; } = new OS ();

		/// <summary>
		///   If <c>true</c> then <c>xat</c> will run in "dull mode" - no colors, no emoji.  Meant to be used on CI when
		///   such frivolous things don't matter :)
		/// </summary>
		public bool DullMode                     { get; set; }

		/// <summary>
		///   Do not use any emoji characters if <c>true</c>
		/// </summary>
		public bool NoEmoji                      { get; set; }

		/// <summary>
		///   Allow xat to use color, if <c>true</c>
		/// </summary>
		public bool UseColor                     { get; set; } = true;

		/// <summary>
		///   If <c>true</c>, then xat detected that color is supported by the current console/terminal.
		/// </summary>
		public bool CanConsoleUseUnicode         { get; }

		/// <summary>
		///   Ignore. Required by code imported from <c>xaprepare</c>
		/// </summary>
		public VersionFetchers VersionFetchers   { get; } = new VersionFetchers ();

		/// <summary>
		///   Set the logging verbosity.  Defaults to <c>Normal</c>
		/// </summary>
		public LoggingVerbosity LoggingVerbosity { get; set; } = LoggingVerbosity.Normal;

		/// <summary>
		///   Build test suites using this configuration.  Defaults to xat build configuration.
		/// </summary>
		public string Configuration              { get; set; } = Configurables.Defaults.DefaultConfiguration;

		/// <summary>
		///   Time when xat was started, used to create log file names.
		/// </summary>
		public string BuildTimeStamp             { get; }

		/// <summary>
		///   Ignore. required by code imported from <c>xaprepare</c>
		/// </summary>
		public uint MakeConcurrency              { get; set; } = Configurables.Defaults.MakeConcurrency;

		/// <summary>
		///   MSBuild properties as set on the xat's build time. See <see cref="KnownProperties"/>
		/// </summary>
		public Properties Properties             { get; } = new Properties ();

		/// <summary>
		///   List of any failed test IDs, if any.
		/// </summary>
		public List<string> FailedTests          { get; } = new List<string> ();

		/// <summary>
		///   Path to the main log file (where all the xat messages are logged)
		/// </summary>
		public string MainLogFilePath            { get; }

		/// <summary>
		///   Directory in which to create log files for xat and all the external commands it runs.
		/// </summary>
		public string LogDirectory               { get; }

		/// <summary>
		///   Various characters used when logging (bullet, icons etc).
		/// </summary>
		public Characters Characters             => characters ?? throw new InvalidOperationException ("Context not initialized properly (was .Init called?)");

		/// <summary>
		///   A shortcut to access a small set of essential tools used by the bootstrapper. See <see cref="EssentialTools" />
		/// </summary>
		public EssentialTools Tools                    { get; private set; } = new EssentialTools (quiet: true);

		/// <summary>
		///   If <c>true</c>, each test suite requiring and Android device will have a fresh emulator image (AVD)
		///   created before it runs.
		/// </summary>
		public bool RequireNewEmulator           { get; set; }

		/// <summary>
		///   ADB target device.  Either auto-detected or passed on command line.
		/// </summary>
		public string AdbTarget                  { get; set; } = String.Empty;

		/// <summary>
		///   Path to ADB, detected using <see cref="Properties"/>
		/// </summary>
		public string AdbPath                    { get; set; } = String.Empty;

		/// <summary>
		///   Additional ADB options specified on command line.
		/// </summary>
		public string AdbOptions                 { get; set; } = String.Empty;

		/// <summary>
		///   Path to Android emulator, detected using <see cref="Properties"/>
		/// </summary>
		public string EmulatorPath               { get; set; } = String.Empty;

		/// <summary>
		///   Path to Android emulator (AVD) manager, detected using <see cref="Properties"/>
		/// </summary>
		public string AvdManagerPath             { get; set; } = String.Empty;

		/// <summary>
		///   Path to Android BundleTool utility, detected using <see cref="Properties"/>
		/// </summary>
		public string BundleToolJarPath          { get; set; } = String.Empty;

		/// <summary>
		///   Path to the Java VM, detected using <see cref="Properties"/>
		/// </summary>
		public string JavaPath                   { get; set; } = String.Empty;

		/// <summary>
		///   Path to NUnit console runner, detected using <see cref="Properties"/>
		/// </summary>
		public string NUnitPath                  { get; set; } = String.Empty;

		/// <summary>
		///   Additional NUnit runner options, specified on the command line
		/// </summary>
		public string NUnitOptions               { get; set; } = String.Empty;

		/// <summary>
		///   Path to the <c>dotnet</c> command. NOT IMPLEMENTED YET
		/// </summary>
		public string DotnetPath                 { get; set; } = String.Empty; // TODO: implement DotnetPath

		/// <summary>
		///  Additional <c>dotnet test</c> options, specified on the command line. NOT IMPLEMENTED YET
		/// </summary>
		public string DotnetTestOptions          { get; set; } = String.Empty; // TODO: implement DOtnetTestOptions

		/// <summary>
		///   Path to (or name of) the MSBuild binary to use. The intention is the choice between <c>xabuild</c> and
		///   <c>msbuild</c>.  Autodetected by first looking for <c>xabuild</c> and, if it's absent, using
		///   <c>msbuild</c>
		/// </summary>
		public string MSBuildBinary              { get; set; } = String.Empty; // TODO: add support for `dotnet msbuild`

		/// <summary>
		///   Collection all the known tests.
		/// </summary>
		public TestCollection Tests {
			get {
				lock (testCollectionLock) {
					if (testCollection == null) {
						testCollection = new TestCollection ();
					}
				}

				return testCollection;
			}
		}

		Context ()
		{
			var now = DateTime.Now;
			BuildTimeStamp = $"{now.Year}{now.Month:00}{now.Day:00}T{now.Hour:00}{now.Minute:00}{now.Second:00}";
			LogDirectory = Path.Combine (BuildPaths.XamarinAndroidSourceRoot, "bin", $"Test{Configuration}");
			Directory.CreateDirectory (LogDirectory);

			MainLogFilePath = GetLogFilePath (null, true);

			CanConsoleUseUnicode =
				Console.OutputEncoding is UTF7Encoding ||
				Console.OutputEncoding is UTF8Encoding ||
				Console.OutputEncoding is UTF32Encoding ||
				Console.OutputEncoding is UnicodeEncoding;
		}

		public void Init ()
		{
			characters = Characters.Create (this);
			AddExtraProperties ();
			Configuration = Properties.GetRequiredValue (KnownProperties.Configuration);

			AdbPath = MakeToolPath (KnownProperties.AdbToolExe, KnownProperties.AdbToolPath);
			EmulatorPath = MakeToolPath (KnownProperties.EmulatorToolExe, KnownProperties.EmulatorToolPath);
			AvdManagerPath = MakeToolPath (KnownProperties.AvdManagerToolExe, KnownProperties.CommandLineToolsBinPath);
			NUnitPath = Properties.GetRequiredValue (KnownProperties.NUnit);

			// Support for running AAB tests against a system installation of XA.
			string bundleToolPath = Properties.GetRequiredValue (KnownProperties.BundleToolJarPath);
			if (!Utilities.FileExists (bundleToolPath)) {
				bundleToolPath = Configurables.Paths.DefaultBundleToolJarPath;
			}

			if (!Utilities.FileExists (bundleToolPath)) {
				bundleToolPath = Properties.GetValue (KnownProperties.AndroidBundleToolJarPath) ?? String.Empty;
			}

			if (String.IsNullOrEmpty (bundleToolPath)) {
				throw new InvalidOperationException ("Android BundleTool path must be specified");
			}

			BundleToolJarPath = bundleToolPath!;

			string? javaPath = Properties.GetValue (KnownProperties.JavaPath);
			if (String.IsNullOrWhiteSpace (javaPath)) {
				javaPath = Configurables.Paths.DefaultJavaPath;
			}

			JavaPath = javaPath!;

			if (MSBuildBinary.Length == 0) {
				if (Utilities.FileExists (Configurables.Paths.XABuildReleasePath)) {
					MSBuildBinary = Configurables.Paths.XABuildReleasePath;
				} else if (Utilities.FileExists (Configurables.Paths.XABuildConfigurationPath)) {
					MSBuildBinary = Configurables.Paths.XABuildConfigurationPath;
				} else {
					MSBuildBinary = "msbuild";
				}
			}

			Tools.Init (this);
		}

		string MakeToolPath (string toolExeProperty, string toolPathProperty)
		{
			string toolExe = Properties.GetRequiredValue (toolExeProperty);
			string toolPath = Properties.GetValue (toolPathProperty) ?? String.Empty;
			string ret;

			if (toolPath.Length > 0) {
				ret = Path.Combine (toolPath, toolExe);
			} else {
				ret = toolExe;
			}

			return ret;
		}

		partial void AddExtraProperties ();

		public string GetLogFilePath (string tags)
		{
			return GetLogFilePath (tags, false);
		}

		string GetLogFilePath (string? tags, bool mainLogFile)
		{
			string logFileName;
			if (String.IsNullOrEmpty (tags)) {
				if (!mainLogFile)
					throw new ArgumentException ("must not be null or empty", nameof (tags));
				logFileName = $"{Configurables.Defaults.LogFilePrefix}-{BuildTimeStamp}.log";
			} else {
				logFileName = $"{Configurables.Defaults.LogFilePrefix}-{BuildTimeStamp}.{tags}.log";
			}

			return Path.Combine (LogDirectory, logFileName);
		}
	}
}
