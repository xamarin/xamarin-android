# Building Xamarin.Android on Linux and macOS

Building Xamarin.Android on Linux and macOS relies on GNU make and
MSBuild via the `msbuild` command (within Mono). MSBuild via `xbuild`
can also be used by setting the `$(MSBUILD)` make variable to `xbuild`.

# Building Xamarin.Android

 1. Install the [build dependencies](dependencies.md).

 2. Clone the xamarin-android repo:

        git clone https://github.com/xamarin/xamarin-android.git

 3. Navigate to the `xamarin-android` directory

 4. (Optional) [Configure the build](configuration.md).

 5. Prepare the project:

        make prepare
        # -or-
        make prepare MSBUILD=msbuild

    This will ensure that the build dependencies are installed, perform
    `git submodule update`, download NuGet dependencies, and other
    "preparatory" and pre-build tasks that need to be performed.

 6. Build the project:

        make
        # -or-
        make MSBUILD=msbuild

    The default `make all` target builds a *subset* of everything, in
    the interests of build speed: it builds only one
    `$(TargetFrameworkVersion)`, and only supports the `armeabi-v7a`
    and `x86` ABIs (for hardware and emulator testing).

    If you want to build *everything* -- support for *all*
    `$(TargetFrameworkVersion)`s, all ABIs, Windows cross-compilers, etc. --
    then use the `make jenkins` target:

        make jenkins
        # -or-
        make jenkins MSBUILD=msbuild


# Building Unit Tests

Once `make all` or `make jenkins` have completed, the unit tests may
be built with:

    make all-tests


# Running Unit Tests


The `xamarin-android` repo contains several unit tests:

  * NUnit-based unit tests, for stand-alone assemblies and utilities.

  * `.apk`-based unit tests, which are NUnitLite-based tests.

    Note: apk-based tests need a running Android emulator or a connected
    Android device as an installation target. If either of them exists,
    the test apps are automatically installed, and if there isn't any
    available target, it will automatically create and run an emulator.

All unit tests can be executed via the `make run-all-tests` target:

	$ make run-all-tests

All NUnit-based tests can be executed via the `make run-nunit-tests` target:

	$ make run-nunit-tests

All `.apk`-based unit tests can be executed via the `make run-apk-tests` target:

	$ make run-apk-tests


## Running Individual NUnit Tests

Individual NUnit-based tests can be executed by overriding the `$(NUNIT_TESTS)`
make variable:

	$ make run-nunit-tests NUNIT_TESTS=bin/TestDebug/Xamarin.Android.Build.Tests.dll

## Listing Nunit Tests

In order to get a list of the tests you can use the `list-nunit-tests` make target

    make list-nunit-tests 

or via the `ListNUnitTests` target

    msbuild Xamarin.Android.sln /t:ListNUnitTests

This will produce a list of the tests in all of the test assemblies.

## Running Specific Nunit Tests

You can run then a single (or a group) of tests using the `$(TEST)` make variable
or msbuild property.

    make run-nunit-tests TEST=Xamarin.Android.Build.Tests.Aapt2Tests.Aapt2Compile

or via

    msbuild Xamarin.Android.sln /t:RunNunitTests /p:TEST=Xamarin.Android.Build.Tests.Aapt2Tests.Aapt2Compile

## Running Individual `.apk` Projects

You can run selected apk test by passing PACKAGES variable to
`make run-apk-tests`. For example:

    make run-apk-tests PACKAGES="Xamarin.Forms_Performance_Integration;Xamarin.Android.Locale_Tests"

or with msbuild:

    msbuild /t:RunApkTests tests/RunApkTests.targets /p:ApkTests='"Xamarin.Forms_Performance_Integration;Xamarin.Android.Locale_Tests"'

Another possibility is to run them manually as described below.

See also the [`tests/RunApkTests.targets`](../../tests/RunApkTests.targets) and
[`build-tools/scripts/TestApks.targets`](../../build-tools/scripts/TestApks.targets)
files.

All `.apk`-based unit test projects provide the following targets:

  * `DeployTestApks`: Installs the associated `.apk` to an Android device.
  * `UndeployTestApks`: Uninstalls the associated `.apk` from an Android device.
  * `RunTestApks`: Executes the unit tests contained within a `.apk`.
    This target must be executed *after* the `DeployTestApks` target.

To run an individual `.apk`-based test project, a package must be built, using the
`SignAndroidPackage` target, installed, and executed.

For example:

	$ bin/Debug/bin/xabuild /t:SignAndroidPackage  tests/locales/Xamarin.Android.Locale-Tests/Xamarin.Android.Locale-Tests.csproj
	$ bin/Debug/bin/xabuild /t:DeployTestApks      tests/locales/Xamarin.Android.Locale-Tests/Xamarin.Android.Locale-Tests.csproj
	$ bin/Debug/bin/xabuild /t:RunTestApks         tests/locales/Xamarin.Android.Locale-Tests/Xamarin.Android.Locale-Tests.csproj


## Running `.apk` Projects with Include/Exclude

If an `.apk`-based unit test uses the NUnit `[Category]` custom attribute, then
those tests can be explicitly included or excluded from execution by setting
the `$(INCLUDECATEGORIES)` or `$(EXCLUDECATEGORIES)` make variables.

For example, to exclude tests that use the internet (`InetAccess`) category:

	$ make run-apk-tests EXCLUDECATEGORIES=InetAccess

`$(INCLUDECATEGORIES)` functions in the same fashion.

To specify multiple categories, separate each category with a `:` character.


### Running A Single Test Fixture

A single NUnit *Test Fixture* -- a class with the `[TestFixture]`
custom attribute -- may be executed instead of executing *all* test fixtures.

The `RunTestApks` target accepts a `TestFixture` MSBuild property
to specify the test fixture class to execute:

	$ bin/Debug/bin/xabuild /t:RunTestApks \
	    /p:TestFixture=Xamarin.Android.LocaleTests.SatelliteAssemblyTests \
	    tests/locales/Xamarin.Android.Locale-Tests/Xamarin.Android.Locale-Tests.csproj

If using `Xamarin.Android.NUnitLite` for projects outside the `xamarin-android`
repository, such as NUnit tests for a custom app, the `RunTestApks` target
will not exist. In such scenarios, the [`adb shell am`][adb-shell-am]
`instrument` command can be used instead. It follows the format:

[adb-shell-am]: https://developer.android.com/studio/command-line/adb.html#am

	$ adb shell am instrument -e suite FIXTURE -w PACKAGE/INSTRUMENTATION

Where:

  * `FIXTURE` is the full *managed* class name of the NUnit test fixture to
    execute.
  * `PACKAGE` is the Android package name containing the NUnit tests
  * `INSTRUMENTATION` is the *Java callable wrapper* class name to execute,
    located within the Android package `PACKAGE`.

For example:

	$ adb shell am instrument -e suite Xamarin.Android.LocaleTests.SatelliteAssemblyTests \
		-w "Xamarin.Android.Locale_Tests/xamarin.android.localetests.TestInstrumentation"


# How do I build `Mono.Android.dll` for a given API Level?

There are a few ways to do it:

  * Use [`Configuration.Override.props`][override-props], and override 
    `$(AndroidApiLevel)` and `$(AndroidFrameworkVersion)`.

  * Build all the platforms with:
  
        make framework-assemblies

  * Build several platforms other than the default
  
        make framework-assemblies API_LEVELS="LEVEL1 LEVEL2"
  
    where *LEVEL1* and *LEVEL2* are [API levels from the `$(API_LEVELS)` variable][api-levels].
  
  * Build just the platform you want, other than the default one with
  
        make API_LEVEL=LEVEL
  
    where *LEVEL* is one of the [API levels from the `$(API_LEVELS)` variable][api-levels].

[override-props]: ../README.md#build-configuration
[api-levels]: ../../build-tools/scripts/BuildEverything.mk#L31

# How do I rebuild the Mono Runtime and Native Binaries?

## The short way

From the top of the Xamarin Android source tree you can run make for the following
targets, which implement the steps to rebuild the runtime and BCL described further
down in the document:

    # Show help on all the rebuild targets
    make rebuild-help

    # Rebuild Mono runtime for all configured architectures regardless
    # of whether a cached copy was used.
    rebuild-mono

    # Rebuild and install Mono runtime for the armeabi-v7a architecture only regardless
    # of whether a cached copy was used.
    rebuild-armeabi-v7a-mono

    # Rebuild and install Mono runtime for the arm64-v8a architecture only regardless
    # of whether a cached copy was used.
    rebuild-arm64-v8a-mono

    # Rebuild and install Mono runtime for the x86 architecture only regardless
    # of whether a cached copy was used.
    rebuild-x86-mono

    # Rebuild and install Mono runtime for the x86_64 architecture only regardless
    # of whether a cached copy was used.
    rebuild-x86_64-mono

    # Rebuild and install a specific BCL assembly. Assembly name must be passed in the ASSEMBLY Make variable
    rebuild-bcl-assembly ASSEMBLY=bcl_assembly_name

    # Rebuild and install all the BCL assemblies
    rebuild-all-bcl

Note that rebuilding Mono using the targets above will modify the commit at the git HEAD by resetting
its date to the current one (see below for more info) - do *NOT* commit the change to Mono as it rewrites
history.


## The long way
The various Mono runtimes -- over *20* of them (!) -- all store object code
within `src/mono-runtimes/obj/$(Configuration)/TARGET`.

If you change sources within `external/mono`, a top-level `make`/`msbuild`
invocation may not rebuild those mono native binaries. To explicitly rebuild
*all* Mono runtimes, you must do two things:

 1. Ensure that the timestamp of the HEAD commit in `external/mono` has changed.
 2. Use the `ForceBuild` target on `mono-runtimes.csproj`.

Changing the timestamp of the HEAD commit can be done with `git pull`,
`git commit` or `git commit --amend`. *How* the timestamp changes isn't
important; it needs to change in order for `ForceBuild` to do anything.
(This is admittedly annoying for those working directly on Mono; it requires
an "intermediate" commit in order to trigger a rebuild.)

The `ForceBuild` target can be executed as:

	# Build and install all runtimes
	$ msbuild /t:ForceBuild src/mono-runtimes/mono-runtimes.csproj

The `ForceBuild` target will build mono for *all* configured architectures,
then invoke the `_InstallRuntimes` target when all the mono's have finished
building; see the `$(AndroidSupportedHostJitAbis)`,
`$(AndroidSupportedTargetAotAbis)`, and `$(AndroidSupportedTargetJitAbis)`
MSBuild properties within [README.md](../README.md). This may not always be
desirable, for example if you're trying to fix a Mono runtime bug for a
specific ABI, and improving turnaround time is paramount.
(Building for all ABIs can be time consuming.)

To build Mono for a specific target, run `make` from the relevant directory,
where the "relevant directory" is the target of interest within
`src/mono-runtimes/obj/$(Configuration)`. When `make` has completed,
invoke the `_InstallRuntimes` target so that the updated native libraries
are copied into `bin/$(Configuration)/lib`, which will allow subsequent
top-level `make` and [`xabuild`](../../tools/xabuild) invocations to use them.

For example, to rebuild Mono for armeabi-v7a:

	$ make -C src/mono-runtimes/obj/Debug/armeabi-v7a
	
	# This updates bin/$(Configuration)/lib/xamarin.android/xbuild/Xamarin/Android/lib/armeabi-v7a/libmonosgen-2.0.so
	$ msbuild /t:_InstallRuntimes src/mono-runtimes/mono-runtimes.csproj


# How do I rebuild BCL assemblies?

The Xamarin.Android Base Class Library assemblies, such as `mscorlib.dll`,
are built within `external/mono`, using Mono's normal build system:

	# This updates external/mono/mcs/class/lib/monodroid/ASSEMBLY.dll
	$ make -C external/mono/mcs/class/ASSEMBLY PROFILE=monodroid

Alternatively, if you want to rebuild *all* the assemblies, the "host"
Mono needs to be rebuilt. Note that the name of the "host" directory
varies based on the operating system you're building from:

	$ make -C src/mono-runtimes/obj/Debug/host-Darwin

Once the assemblies have been rebuilt, they can be copied into the appropriate
Xamarin.Android SDK directory by using the `_InstallBcl` target:

	# This updates bin/$(Configuration)/lib/xamarin.android/xbuild-frameworks/MonoAndroid/v1.0/ASSEMBLY.dll
	$ msbuild src/mono-runtimes/mono-runtimes.csproj /t:_InstallBcl

