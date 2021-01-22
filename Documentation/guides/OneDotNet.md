# .NET 6 and Xamarin.Android

_NOTE: this document is very likely to change, as the requirements for
.NET 6 are better understood._

A .NET 6 project for a Xamarin.Android application will look something
like:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0-android</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
</Project>
```

For a "library" project, you would omit the `$(OutputType)` property
completely or specify `Library`.

See the [Target Framework Names in .NET 5][net5spec] spec for details.

[net5spec]: https://github.com/dotnet/designs/blob/5e921a9dc8ecce33b3195dcdb6f10ef56ef8b9d7/accepted/2020/net5/net5.md

## Bindings projects

Changes for bindings projects are documented [here][binding].

[binding]: OneDotNetBindingProjects.md

## .NET Configuration Files

No support for [configuration files][config] such as `Foo.dll.config`
or `Foo.exe.config` is available in Xamarin.Android projects targeting
.NET 6. [`<dllmap>`][dllmap] configuration elements are not supported
in .NET Core at all, and other element types for compatibility
packages like [System.Configuration.ConfigurationManager][nuget] have
never been supported in Xamarin.Android projects.

[config]: https://docs.microsoft.com/dotnet/framework/configure-apps/
[nuget]: https://www.nuget.org/packages/System.Configuration.ConfigurationManager/

## Changes to MSBuild tasks

In .NET 6 the behavior of the following MSBuild tasks will change, but
"legacy" projects will stay the same:

* `<ValidateJavaVersion/>` - used to require Java 1.6, 1.7, or 1.8
  based on the version of the Android Build Tools or
  `$(TargetFrameworkVersion)`. .NET 6 will require Java 1.8.

* `<ResolveAndroidTooling/>` - used to support the
  `$(AndroidUseLatestPlatformSdk)` setting or multiple
  `$(TargetFrameworkVersion)`. .NET 6 will always target the latest
  Android APIs for `Mono.Android.dll`.

## Changes to MSBuild properties

`$(AndroidSupportedAbis)` should not be used. Instead of:

```xml
<PropertyGroup>
  <!-- Used in legacy Xamarin.Android projects -->
  <AndroidSupportedAbis>armeabi-v7a;arm64-v8a;x86;x86_64</AndroidSupportedAbis>
</PropertyGroup>
```

Instead use .NET's concept of [runtime identifiers][rids]:

```xml
<PropertyGroup>
  <!-- Used going forward in .NET -->
  <RuntimeIdentifiers>android-arm;android-arm64;android-x86;android-x64</RuntimeIdentifiers>
</PropertyGroup>
```

`$(AndroidUseIntermediateDesignerFile)` will be `True` by default.

`$(AndroidBoundExceptionType)` will be `System` by default.  This will
[alter the types of exceptions thrown from various methods][abet-sys] to
better align with existing .NET 6 semantics, at the cost of compatibility with
previous Xamarin.Android releases.

`$(AndroidClassParser)` will be `class-parse` by default. `jar2xml`
will not be supported.

`$(AndroidDexTool)` will be `d8` by default. `dx` will not be
supported.

`$(AndroidCodegenTarget)` will be `XAJavaInterop1` by default.
`XamarinAndroid` will not be supported.

`$(AndroidManifest)` will default to `AndroidManifest.xml` in the root
of projects as `Properties\AssemblyInfo.cs` is no longer used in
short-form MSBuild projects. `Properties\AndroidManifest.xml` will
also be detected and used if it exists to ease migration.

`$(DebugType)` will be `portable` by default. `full` and `pdbonly`
will not be supported.

`$(MonoSymbolArchive)` will be `False`, since `mono-symbolicate` is
not yet supported.

If Java binding is enabled with `@(InputJar)`, `@(EmbeddedJar)`,
`@(LibraryProjectZip)`, etc. then `$(AllowUnsafeBlocks)` will default
to `True`.

Referencing an Android Wear project from an Android application will
not be supported:

```xml
<ProjectReference Include="..\Foo.Wear\Foo.Wear.csproj">
  <IsAppExtension>True</IsAppExtension>
</ProjectReference>
```

[rids]: https://docs.microsoft.com/dotnet/core/rid-catalog
[abet-sys]: https://github.com/xamarin/xamarin-android/issues/4127

## Default file inclusion

Default Android related file globbing behavior is defined in [`AutoImport.props`][autoimport].
This behavior can be disabled for Android items by setting `$(EnableDefaultAndroidItems)` to `false`, or
all default item inclusion behavior can be disabled by setting `$(EnableDefaultItems)` to `false`.

[autoimport]: https://github.com/dotnet/designs/blob/4703666296f5e59964961464c25807c727282cae/accepted/2020/workloads/workload-resolvers.md#workload-props-files

## Runtime behavior

There is some behavioral changes to the `String.IndexOf()` method in
.NET 5 and higher on different platforms, see details [here][indexof].

[indexof]: https://docs.microsoft.com/dotnet/standard/globalization-localization/globalization-icu

## Linker (ILLink)

.NET 5 and higher has new [settings for the linker][linker]:

* `<PublishTrimmed>true</PublishTrimmed>`
* `<TrimMode>copyused</TrimMode>` - Enable assembly-level trimming.
* `<TrimMode>link</TrimMode>` - Enable member-level trimming.

In Android application projects by default, `Debug` builds will not
use the linker and `Release` builds will set `PublishTrimmed=true` and
`TrimMode=link`. `TrimMode=copyused` is the default the dotnet/sdk,
but it doesn't not seem to be appropriate for mobile applications.
Developers can still opt into `TrimMode=copyused` if needed.

If the legacy `AndroidLinkMode` setting is used, both `SdkOnly` and
`Full` will default to equivalent linker settings:

* `<PublishTrimmed>true</PublishTrimmed>`
* `<TrimMode>link</TrimMode>`

With `AndroidLinkMode=SdkOnly` only BCL and SDK assemblies marked with
`%(Trimmable)` will be linked at the member level.
`AndroidLinkMode=Full` will set `%(TrimMode)=link` on all .NET
assemblies similar to the example in the [trimming
documentation][linker-full].

It is recommended to migrate to the new linker settings, as
`AndroidLinkMode` will eventually be deprecated.

[linker]: https://docs.microsoft.com/dotnet/core/deploying/trimming-options
[linker-full]: https://docs.microsoft.com/dotnet/core/deploying/trimming-options#trimmed-assemblies

## AOT

`$(RunAOTCompilation)` will be the new MSBuild property for enabling
AOT. This is the same property used for [Blazor WASM][blazor].
`$(AotAssemblies)` will also enable AOT, in order to help with
migration from "legacy" Xamarin.Android to .NET 6.

It is recommended to migrate to the new `$(RunAOTCompilation)`
property, as `$(AotAssemblies)` will eventually be deprecated.

[blazor]: https://devblogs.microsoft.com/aspnet/asp-net-core-updates-in-net-6-preview-4/#blazor-webassembly-ahead-of-time-aot-compilation

## dotnet cli

There are currently a few "verbs" we are aiming to get working in
Xamarin.Android:

    dotnet new
    dotnet build
    dotnet publish
    dotnet run

### dotnet new

To support `dotnet new`, we created a few basic project and item
templates for Android that are named following the patterns and naming
of existing .NET templates:

    Templates                                     Short Name           Language    Tags
    --------------------------------------------  -------------------  ----------  ----------------------
    Android Activity template                     android-activity     [C#]        Android
    Android Java Library Binding                  android-bindinglib   [C#]        Android
    Android Layout template                       android-layout       [C#]        Android
    Android Class library                         androidlib           [C#]        Android
    Android Application                           android              [C#]        Android
    Console Application                           console              [C#],F#,VB  Common/Console
    Class library                                 classlib             [C#],F#,VB  Common/Library
    WPF Application                               wpf                  [C#],VB     Common/WPF
    WPF Class library                             wpflib               [C#],VB     Common/WPF
    NUnit 3 Test Project                          nunit                [C#],F#,VB  Test/NUnit
    NUnit 3 Test Item                             nunit-test           [C#],F#,VB  Test/NUnit

To create different types of Android projects:

    dotnet new android            --output MyAndroidApp     --packageName com.mycompany.myandroidapp
    dotnet new androidlib         --output MyAndroidLibrary
    dotnet new android-bindinglib --output MyJavaBinding

Once the projects are created, some basic item templates can also be
used such as:

    dotnet new android-activity --name LoginActivity --namespace MyAndroidApp
    dotnet new android-layout   --name MyLayout      --output Resources/layout

### dotnet build & publish

Currently in .NET console apps, `dotnet publish` is where all the work
to produce a self-contained "app" happens:

* The linker via the `<IlLink/>` MSBuild task
* .NET Core's version of AOT, named "ReadyToRun"

https://docs.microsoft.com/en-us/dotnet/core/whats-new/dotnet-core-3-0#readytorun-images

However, for Xamarin.Android, `dotnet build` should produce something
that is runnable. This basically means we need to create an `.apk` or
`.aab` file during `build`. We will need to reorder any MSBuild tasks
from the dotnet SDK, so that they will run during `build`.

This means Xamarin.Android would run:

* Run `aapt` to generate `Resource.designer.cs` and potentially emit
  build errors for issues in `@(AndroidResource)` files.
* Compile C# code
* Run the new [ILLink][illink] MSBuild target for linking.
* Generate java stubs, `AndroidManifest.xml`, etc. This must happen
  after the linker.
* Compile java code via `javac`
* Convert java code to `.dex` via d8/r8
* Create an `.apk` or `.aab` and sign it

`dotnet publish` will be reserved for publishing an app for Google
Play, ad-hoc distribution, etc. It could be able to sign the `.apk` or
`.aab` with different keys. As a starting point, this will currently
copy the output to a `publish` directory on disk.

_NOTE: Behavior inside IDEs will differ. The `Build` target will not
produce an `.apk` file if `$(BuildingInsideVisualStudio)` is `true`.
IDEs will call the `Install` target for deployment, which will produce
the `.apk` file. This behavior matches "legacy" Xamarin.Android._

[illink]: https://github.com/mono/linker/blob/master/src/linker/README.md

### dotnet run

`dotnet run` can be used to launch applications on a
device or emulator via the `--project` switch:

    dotnet run --project HelloAndroid.csproj

Alternatively, you could use the `Run` MSBuild target such as:

    dotnet build HelloAndroid.csproj -t:Run

### Preview testing

For the latest instructions on preview testing and sample projects,
see the [net6-samples][net6-samples] repo.

[net6-samples]: https://github.com/xamarin/net6-samples

## Package Versioning Scheme

This is the package version scheme: `OS-Major.OS-Minor.InternalRelease[-prereleaseX]+sha.1b2c3d4`.

* Major: The highest stable API level, such as 30. On Apple platforms, this is the major OS version.
* Minor: Always 0 for Android. On Apple platforms, this is the minor OS version.
* Patch: Our internal release version based on `100` as a starting point.
    * Service releases will bump the last two digits of the patch version
    * Feature releases will round the patch version up to the nearest 100
      (this is the same as bumping the first digit of the patch version, and
      resetting the last two digits to 0).
    * This follows [how the dotnet SDK does it][1].
* Pre-release: Optional (e.g.: Android previews, CI, etc.)
    * For CI we use a `ci` prefix + the branch name (cleaned up to only be
      alphanumeric) + the commit distance (number of commits since any of the
      major.minor.patch versions changed).
        * Example: `30.0.100-ci.master.1234`
        * Alphanumeric means `a-zA-Z0-9-`: any character not in this range
          will be replaced with a `-`.
    * Pull requests have `pr` prefix, followed by `gh`+ PR number + commit
      distance.
        * Example: `30.0.200-ci.pr.gh3333.1234`
    * If we have a particular feature we want people to subscribe to (such as
      an Android preview release), we publish previews with a custom pre-release
      identifier:
        * Example: `30.0.100-android-r.beta.1`
        * This way people can sign up for only official previews, by
          referencing `*-android-r.beta.*`
        * It's still possible to sign up for all `android-r` builds, by
          referencing `*-ci.android-r.*`
* Build metadata: Required Hash
    * This is `sha.` + the short commit hash.
        * Use the short hash because the long hash is quite long and
          cumbersome. This leaves the complete version open for duplication,
          but this is extremely unlikely.
    * Example: `30.0.100+sha.1a2b3c`
    * Example (CI build): `30.0.100-ci.master.1234+sha.1a2b3c`
    * Since the build metadata is required for all builds, we're able to
      recognize incomplete version numbers and determine if a particular
      version string refers to a stable version or not.
        * Example: `30.0.100`: incomplete version
        * Example: `30.0.100+sha.1a2b3c`: stable
        * Example: `30.0.100-ci.d17-0.1234+sha.1a2b3c`: CI build
        * Example: `30.0.100-android-r.beta.1+sha.1a2b3c`: official
          preview
            * Technically it's possible to remove the prerelease part, but
              we’d still be able to figure out it’s not a stable version by
              using the commit hash.


[0]: https://github.com/dotnet/installer#installers-and-binaries
[1]: https://github.com/dotnet/designs/blob/master/accepted/2018/sdk-version-scheme.md
