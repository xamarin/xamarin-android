# Build Dependencies for Linux and macOS

Building Xamarin.Android requires:

  * [Homebrew](#homebrew)
  * [Latest Mono](#mono-sdk)
  * [The Java Development Kit (JDK)](#jdk)
  * [Autotools (`autoconf`, `automake`, etc.)](#autotools)
  * [The Android SDK and NDK](#ndk)
  * [The Microsoft .NET SDK][dotnetsdk]
  * [Linux](#Linux) and [macOS](#macOS) Dependencies
  * C++ compiler with support for C++17 (clang 5, gcc 7 or higher)
  * MinGW 6 or newer, if cross-building of Windows tooling on the
    Unix host is desired

[dotnetsdk]: https://docs.microsoft.com/de-de/dotnet/core/install/sdk

The `make prepare` build step (or `/t:Prepare` on Windows) will
check that all required dependencies are present.
If you would like `make prepare` to automatically install
required dependencies, set the `$(AutoProvision)` MSBuild property to True
and (if necessary) set the `$(AutoProvisionUsesSudo)` property to True.
(This is not supported on all operating systems;
see [configuration.md](../configuration.md) for details.)

If `$(AutoProvision)` is False (the default) and a dependency is missing,
then the build will fail and an error message will be displayed attempting
to provide install instructions to obtain the missing dependency, e.g.:

    error : Could not find required program '7za'. Please run: brew install 'p7zip'.

<a name="homebrew" />

## Homebrew

[Homebrew](https://brew.sh) must be installed and available via `$PATH` in
order to provision xamarin-android.

When building on Apple Silicon (arm64) machines, use the **arch**(1) command to
allow Homebrew to be installed:

	% arch -arch x86_64 /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"


<a name="mono-sdk" />

## Mono MDK

Latest Mono is required to build on [macOS][osx-mono] and Linux.
The build will tell you if your version is outdated.

[osx-mono]: http://www.mono-project.com/download/#download-mac
[xmlpeek]: https://msdn.microsoft.com/en-us/library/ff598684.aspx

The minimum Mono version which is checked for can be overridden by the
`$(MonoRequiredMinimumVersion)` MSBuild property, but things may not build.
(This is your warning.)


<a name="jdk" />

## Java Development Kit

Most Linux distributions include [OpenJDK][openjdk].
Alternatively, the Java Development Kit may be downloaded from the
[Oracle Java SE Downloads page][download-jdk].

[openjdk]: https://openjdk.java.net
[download-jdk]: http://www.oracle.com/technetwork/java/javase/downloads/

At this time, we only support building with JDK 1.8.


<a name="autotools" />

## Autotools

Autotools -- including `autoconf` and `automake` -- are required to build
the Mono runtimes.


On macOS, autotools are should be used from `brew`, and may be installed via:

    brew install automake


<a name="ndk" />

## Android NDK, SDK

*Note*: A xamarin-android checkout maintains *its own* Android NDK + SDK
to ensure consistent builds and build behavior, permitting reproducible
builds and providing greater flexibility around when we need to perform
Android SDK + NDK updates. The Android SDK and NDK are maintained by default
via two directories in your home directory:

  * `$(AndroidToolchainCacheDirectory)`: Where downloaded files are cached.
    Defaults to the `$HOME/android-archives` directory.
  * `$(AndroidToolchainDirectory)`: Where the Android NDK and SDK are installed.
    Defaults to the `$HOME/android-toolchain` directory.

Developers may use these directories for their own use, but *please* **DO NOT**
update or alter the contents of the `$(AndroidToolchainDirectory)`, as that may
prevent the xamarin-android build from working as expected.

The files that will be downloaded and installed are controlled by
[build-tools/android-toolchain/android-toolchain.projitems][android-toolchain.projitems]
via the `@(AndroidNdkItem)` and `@(AndroidSdkItem)` item groups, and the
URL to download files from is controlled by the `$(AndroidUri)` property.

[android-toolchain.projitems]: ../../../build-tools/android-toolchain/android-toolchain.projitems


<a name="Linux" />

## Linux Dependencies

If you have the `binfmt_misc` module enabled with any of Mono or Wine installed and
you plan to cross-build the Windows compilers and tools (by enabling the `mxe-Win32`
or `mxe-Win64` host targets) as well as LLVM+AOT targets, you will need to disable
`binfmt_misc` for the duration of the build or the Mono/LLVM configure scripts will
fail to detect they are cross-compiling and they will produce Windows PE executables
for tools required by the build.

To disable `binfmt_misc` you need to issue the following command as root:

    echo 0 > /proc/sys/fs/binfmt_misc/status

and to enable it again, issue the following command:

    echo 1 > /proc/sys/fs/binfmt_misc/status


<a name="macOS" />

## macOS Dependencies

The [`android-toolchain.projitems`](../../../build-tools/android-toolchain/android-toolchain.projitems), and
[`monodroid.projitems`](../../../src/monodroid/monodroid.projitems) project files, among
others, use the `@(RequiredProgram)` build action to check for the existence
of a program within `$PATH` during the build. If a required program doesn't
exist, then the build will fail and a suggested `brew install` command line
will be provided to install the missing commands.


Add `brew` tap for crossbuilding windows binaroes on MacOSX:

```
brew tap xamarin/xamarin-android-windeps
```

Details can be found: https://github.com/xamarin/homebrew-xamarin-android-windeps

Otherwise, if building `xamarin-android` for the first time users might experience following error:

```
Checking xamarin/xamarin-android-windeps/mingw-zlib Error: No available formula with the name "xamarin/xamarin-android-windeps/mingw-zlib" 
Please tap it and then try again: brew tap xamarin/xamarin-android-windeps
```

### Brew Programs

Suggested `brew install` commands:

    brew install git
    brew install cmake
    brew install libtool
    brew install p7zip
    brew install gdk-pixbuf
    brew install gettext
    brew install coreutils
    brew install findutils
    brew install gnu-tar
    brew install gnu-sed
    brew install gawk
    brew install gnutls
    brew install gnu-indent
    brew install gnu-getopt
    brew install intltool
    brew install ninja
    brew install scons
    brew install wget
    brew install xz

If any program is still not found, try to ensure it's linked via:

    brew link <package name>


Note: Apple provided `git` seems to be old and user might experience problems during tool detection reporting
that `git` is missing:

```
Checking autoconf                                    [FOUND 2.69]
Checking automake                                    [FOUND 1.15]
Checking cmake                                       [FOUND 3.10.2]
Checking git                                         [MISSING]
Checking make                                        [FOUND 3.81]
Checking mingw-w64                                   [MISSING]
Checking ninja                                       [FOUND 1.10.0]
Checking p7zip                                       [FOUND 16.02]
```

and that could lead to further errors like:

```
Updating external repositories
  • monodroid
    🔗 cloning from xamarin/monodroid
stderr | Cloning into 'monodroid'...
stderr | remote: Enumerating objects: 58, done.        
stderr | remote: Counting objects:   1% (1/58)        
stderr | remote: Counting objects:   3% (2/58)        
```

```
stderr | remote: Compressing objects:   2% (1/45)        
stderr | remote: Compressing objects:   4% (2/45)        
```

```
stderr | Receiving objects:   0% (1/79000)
stderr | Receiving objects:   1% (790/79000)
stderr | Receiving objects:   2% (1580/79000), 596.00 KiB | 1.15 MiB/s
stderr | Receiving objects:   3% (2370/79000), 596.00 KiB | 1.15 MiB/s
stderr | Receiving objects:   4% (3160/79000), 596.00 KiB | 1.15 MiB/s
```

```
stderr | remote: Total 79000 (delta 26), reused 31 (delta 13), pack-reused 78942        
stderr | Receiving objects: 100% (79000/79000), 81.78 MiB | 3.58 MiB/s
stderr | Receiving objects: 100% (79000/79000), 82.14 MiB | 2.53 MiB/s, done.
stderr | Resolving deltas:   0% (0/57659)
stderr | Resolving deltas:   1% (693/57659)
```

```
stderr | Submodule 'external/Java.Interop' (https://github.com/xamarin/java.interop.git) registered for path 'external/Java.Interop'
stderr | Submodule 'external/android-api-docs' (https://github.com/xamarin/android-api-docs) registered for path 'external/android-api-docs'
stderr | Submodule 'external/debugger-libs' (https://github.com/mono/debugger-libs) registered for path 'external/debugger-libs'
stderr | Submodule 'external/dlfcn-win32' (https://github.com/dlfcn-win32/dlfcn-win32.git) registered for path 'external/dlfcn-win32'
stderr | Submodule 'external/lz4' (https://github.com/lz4/lz4.git) registered for path 'external/lz4'
stderr | Submodule 'external/mman-win32' (https://github.com/witwall/mman-win32.git) registered for path 'external/mman-win32'
stderr | Submodule 'external/nrefactory' (https://github.com/icsharpcode/NRefactory.git) registered for path 'external/nrefactory'
stderr | Submodule 'external/opentk' (https://github.com/mono/opentk.git) registered for path 'external/opentk'
stderr | Submodule 'external/proguard' (https://github.com/Guardsquare/proguard.git) registered for path 'external/proguard'
stderr | Submodule 'external/sqlite' (https://github.com/xamarin/sqlite.git) registered for path 'external/sqlite'
stderr | Submodule 'external/xamarin-android-tools' (https://github.com/xamarin/xamarin-android-tools) registered for path 'external/xamarin-android-tools'
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/Java.Interop'...
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/android-api-docs'...
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/debugger-libs'...
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/dlfcn-win32'...
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/lz4'...
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/mman-win32'...
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/nrefactory'...
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/opentk'...
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/proguard'...
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/sqlite'...
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/xamarin-android-tools'...
stderr | Submodule 'external/xamarin-android-tools' (https://github.com/xamarin/xamarin-android-tools.git) registered for path 'external/Java.Interop/external/xamarin-android-tools'
stderr | Cloning into '/Users/Shared/Projects/d/X.tmp/xamarin-android/external/Java.Interop/external/xamarin-android-tools'...
```

