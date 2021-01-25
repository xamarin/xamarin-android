# **X**amarin **A**ndroid **T**ests

`XAT` is a utility created to make working with `Xamarin.Android`
tests easier and more approachable.  The main problem that this
utility aims to solve is the scattered nature of the traditional
(based on MSBuild) setup to configure, run and report on results of
the many test suites we have.  The MSBuild-based solution kept the
code and data related to tests in several locations:

 * `tests/RunApkTests.targets`
 * `build-tools/scripts/TestApks.targets`
 * `build-tools/scripts/RunTests.targets`
 * `external/Java.Interop/build-tools/scripts/RunNUnitTests.targets`
 * `build-tools/automation/**/*.yaml`
 * Various `*.projitems` files scattered around the source tree

The locations above together defined code that would gather test
definitions (mostly from the `*.projitems` files, but sometimes from
item groups inside the `*.targets` files), define targets to run
various kinds of tests (host system unit tests, APK tests on device,
host unit tests accessing devices, `MSBuild` tests etc) and process
their results.  The code, out of necessity, included many bits of
repeated code, filesystem locations, conditions etc - all of this
together making management and modification quite unapproachable, even
for people working day-in day-out with `Xamarin.Android`, let alone
with potential external contributors.  It was very easy to forget
where one needs to modify and what in order to add, remove or modify
tests or affect the way they are built, executed and what happens with
the results.

On top of the `MSBuild` infrastructure, we had a separate set of
`YAML` files defining how the tests are built and executed on the CI
servers.  These files indirectly used the data  defined in the
locations above, but introduced their own infrastructure to divide up
the test suites into chunks we can run on CI as effectively and in as
little time as possible.  This required another instance of code and
data duplication (as the `YAML` pipeline cannot directly access the
`MSBuild` files or data stored in them). While absolutely necessary
for every day efficiency of the PR-based workflow as well as
validation of the already committed code, this necessary design
created another problem: one wasn't able to run the tests locally in
the same manner they ran on the CI servers.  This is because of the
fact that the CI pipelines cannot be executed on the developer's
system.

`XAT` puts all that information and capability in a single location,
ideally allowing for changing of a handful of source files (see in
further sections of the document) in order to change how and what
tests are ran, how their results are stored and processed. 

Another problem `XAT` aims to solve is to increase discoverability of
the tests, as well as simplification how one can run a single test
suite or a single test from any of the known test suites.

# Usage

`XAT` uses the concept of `commands` specified on the command line,
each of them taking their own set of arguments.

## Command-line arguments

### Global options
    usage: xat COMMAND [OPTIONS]

    Xamarin.Android v11.1.99 test shell

    Global options:
      -v, --verbosity=LEVEL      Set console log verbosity to LEVEL. Level name may
                                   be abbreviated to the smallest unique part (one
                                   of: silent, quiet, normal, verbose, diagnostic).
                                  Default: normal
      -c, --configuration=CONFIGURATION
                                 Set build CONFIGURATION instead of the default '
                                   Debug'
      -D, --dull-mode            Run xat in "dull mode" - no colors, no emoji
      -E, --no-emoji             Run xat without using emoji, but still allow color

    Available commands:
            list                 List known tests/categories/etc
            run                  Invoke selected tests/categories

### list options

    usage: xat list [OPTIONS]
    By default the command lists everything. Options can be combined
    
      -g, --groups               list only test groups
      -s, --suites               list only test suites
    
      -m, --more                 list more information
      -?, -h, --help             show this help

### run options

	usage: xat run [OPTIONS]
    By default the command runs all the tests. Options can be combined and repeated
    on the command line unless stated otherwise

      -m, --msbuild=BINARY       Use the specified MSBuild BINARY. If omitted, XAT
                                   will first try to find `xabuild` and fall back
                                   to `msbuild`

      -g, --group=GROUPS         Run the specified GROUPS. Takes a comma-separated
                                   list of test group names or can be repeated on
                                   command line
      -s, --suite=SUITES         Run the specified SUITES. Takes a comma-separated
                                   list of test suite names or can be repeated on
                                   command line

    The arguments below use suite-specific format for each entry. They are ignored
    if a test suite runs as part of predefined group:
      -t, --test=TESTS           Run the specified TESTS. Takes a comma-separated
                                   list of test names
      -i, --include-categories=CATEGORIES
                                 Run tests in the specified CATEGORIES. Takes a
                                   comma-separated list of test categories
      -e, --exclude-categories=CATEGORIES
                                 Do not tun tests in the specified CATEGORIES.
                                   Takes a comma-separated list of test categories
      -j, --include-tests=TESTS  Run the specified TESTS. Takes a comma-separated
                                   list of test names
      -f, --exclude-tests=TESTS  Do not run the specified TESTS. Takes a comma-
                                   separated list of test names

      -n, --new-emulator         Create emulator AVD even if it already exists
      -d, --device=DEVICE        ID/name of Android DEVICE to use
      -a, --adb-options=OPTIONS  Additional OPTIONS to pass to ADB
      -o, --nunit-options=OPTIONS
                                 Additional OPTIONS to pass to NUnit console runner

      -?, -h, --help             Show this help
  
## Test suite groups

Groups are primarily meant to simplify our CI pipeline definitions,
but they can also be used locally to run tests in the same
configuration and mode as on the CI servers.

Whenever a test group is specified (with `run -g GROUP_ID`), the
options pertaining to the selection of tests and categories on the
command line, are ignored.  These settings are instead specified in
the group definition ([`TestCollection.Groups.cs`](TestCollection.Groups)). 

# Main sources to edit when adding/removing/modifying tests

Ideally, a developer who needs to add, remove a test or modify one
will need to edit only a single, perhaps two, files described in the
`Test collection` section below.  The rest of the code should be
generic enough to make it possible.  It should be unnecessary to edit
any code in the subdirectories, if the editing has the purpose of
modifying the test collection and tests that are found in it.

If adding or modifying a test requires changes to any of the files in
the subdirectories (barring bug fixes) then the code should be
modified in a way that makes it possible to edit only the `Test
collection` source files below.

## Test collection

All the tests are defined, described and configured in the
[`TestCollection.cs`](TestCollection.cs) file.

Test groups are defined in the
[`TestCollection.Groups.cs`](TestCollection.Groups.cs) file.

