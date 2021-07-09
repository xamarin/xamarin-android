---
title: "Build Properties"
description: "This document will list all supported properties in the Xamarin.Android build process."
ms.prod: xamarin
ms.assetid: FC0DBC08-EBCB-4D2D-AB3F-76B54E635C22
ms.technology: xamarin-android
author: jonpryor
ms.author: jopryo
ms.date: 03/01/2021
---

# Build Properties

MSBuild properties control the behavior of the
[targets](~/android/deploy-test/building-apps/build-targets.md).
They are specified within the project file, for example **MyApp.csproj**, within
an [MSBuild PropertyGroup](/visualstudio/msbuild/propertygroup-element-msbuild).

## AdbTarget

The `$(AdbTarget)` property specifies the Android target device the
Android package may be installed to or removed from.
The value of this property is the same as the
[`adb` Target Device option](https://developer.android.com/tools/help/adb.html#issuingcommands).

## AfterGenerateAndroidManifest

MSBuild Targets listed in this
property will run directly after the internal `_GenerateJavaStubs`
target, which is where the `AndroidManifest.xml` file is generated
in the `$(IntermediateOutputPath)`. If you want to make any
modifications to the generated `AndroidManifest.xml` file, you can
do that using this extension point.

Added in Xamarin.Android 9.4.

## AndroidAapt2CompileExtraArgs

Specifies additional
command-line options to pass to the **aapt2 compile** command when
processing Android assets and resources.

Added in Xamarin.Android 9.1.

## AndroidAapt2LinkExtraArgs

Specifies additional
command-line options to pass to the **aapt2 link** command when
processing Android assets and resources.

Added in Xamarin.Android 9.1.

## AndroidAddKeepAlives

A boolean property that controls whether the linker will insert
`GC.KeepAlive()` invocations within binding projects to prevent premature
object collection.

Defaults to `True` for Release configuration builds.

This property was added in Xamarin.Android 11.2.

## AndroidAotCustomProfilePath

The file that `aprofutil` should create to hold profiler data.

## AndroidAotProfiles

A string property that allows the
developer to add AOT profiles from the command line. It is a
semicolon or comma-separated list of absolute paths.
Added in Xamarin.Android 10.1.

## AndroidAotProfilerPort

The port that `aprofutil` should connect to when obtaining profiling data.

## AndroidApkDigestAlgorithm

A string value which specifies
the digest algorithm to use with `jarsigner -digestalg`.

The default value is `SHA-256`. In Xamarin.Android 10.0 and earlier,
the default value was `SHA1`.

Added in Xamarin.Android 9.4.

## AndroidApkSignerAdditionalArguments

A string property which allows
the developer to provide additional arguments to the `apksigner` tool.

Added in Xamarin.Android 8.2.

## AndroidApkSigningAlgorithm

A string value which specifies
the signing algorithm to use with `jarsigner -sigalg`.

The default value is `SHA256withRSA`. In Xamarin.Android 10.0 and
earlier, the default value was `md5withRSA`.

Added in Xamarin.Android 8.2.

## AndroidApplication

A boolean value that indicates
whether the project is for an Android Application (`True`) or for
an Android Library Project (`False` or not present).

Only one project with
`<AndroidApplication>True</AndroidApplication>` may be present
within an Android package. (Unfortunately this is not yet verified,
which can result in subtle and bizarre errors regarding Android
resources.)

## AndroidApplicationJavaClass

The full Java class name to
use in place of `android.app.Application` when a class inherits
from [Android.App.Application](xref:Android.App.Application).

This property is generally set by *other* properties, such as the
`$(AndroidEnableMultiDex)` MSBuild property.

Added in Xamarin.Android 6.1.

## AndroidBinUtilsPath

A path to a directory containing
the Android [binutils][binutils] such as `ld`, the native linker,
and `as`, the native assembler. These tools are part of the Android
NDK and are also included in the Xamarin.Android installation.

The default value is `$(MonoAndroidBinDirectory)\ndk\`.

Added in Xamarin.Android 10.0.

[binutils]: https://android.googlesource.com/toolchain/binutils/

## AndroidBoundExceptionType

A string value that specifies how
exceptions should be propagated when a Xamarin.Android-provided type
implements a .NET type or interface in terms of Java types, for example
`Android.Runtime.InputStreamInvoker` and `System.IO.Stream`, or
`Android.Runtime.JavaDictionary` and `System.Collections.IDictionary`.

- `Java`: The original Java exception type is propagated as-is.

  This means that, for example, `InputStreamInvoker` does not properly implement
  the `System.IO.Stream` API because `Java.IO.IOException` may be thrown
  from `Stream.Read()` instead of `System.IO.IOException`.

  This is the exception propagation behavior in all releases of
  Xamarin.Android prior to 11.1.

  This is the default value in Xamarin.Android 11.1.

- `System`: The original Java exception type is caught and wrapped in an
  appropriate .NET exception type.

  This means that, for example, `InputStreamInvoker` properly implements
  `System.IO.Stream`, and `Stream.Read()` will *not* throw `Java.IO.IOException`
  instances.  (It may instead throw a `System.IO.IOException` which has a
  `Java.IO.IOException` as the `Exception.InnerException` value.)

  This will become the default value in .NET 6.0.

Added in Xamarin.Android 10.2.

## AndroidBuildApplicationPackage

A boolean value that
indicates whether to create and sign the package (.apk). Setting
this value to `True` is equivalent to using the
[`SignAndroidPackage`](~/android/deploy-test/building-apps/build-targets.md#install)
build target.

Support for this property was added after Xamarin.Android 7.1.

This property is `False` by default.

## AndroidBundleConfigurationFile

Specifies a filename to use as a
[configuration file][bundle-config-format] for `bundletool`
when building an Android App Bundle. This file controls some aspects
of how APKs are generated from the bundle, such as on what
dimensions the bundle is split to produce APKs. Note that
Xamarin.Android configures some of these settings automatically,
including the list of file extensions to leave uncompressed.

This property is only relevant if
[`$(AndroidPackageFormat)`](#androidpackageformat) is set to `aab`.

Added in Xamarin.Android 10.3.

[bundle-config-format]: https://developer.android.com/studio/build/building-cmdline#bundleconfig

## AndroidBundleToolExtraArgs

Specifies additional
command-line options to pass to the **bundletool** command when
build app bundles.

This property was added in Xamarin.Android 11.3.

## AndroidClassParser

A string property which controls how
`.jar` files are parsed. Possible values include:

- **class-parse**: Uses `class-parse.exe` to parse Java bytecode
  directly, without assistance of a JVM. This value is
  experimental.

- **jar2xml**: Use `jar2xml.jar` to use Java reflection to extract
  types and members from a `.jar` file.

The advantages of `class-parse` over `jar2xml` are:

- `class-parse` can extract parameter names from Java
  bytecode which contains *debug* symbols (bytecode compiled
  with `javac -g`).

- `class-parse` doesn't "skip" classes which inherit from or
  contain members of unresolvable types.

**Experimental**. Added in Xamarin.Android 6.0.

The default value is `jar2xml`.

Support for `jar2xml` is obsolete, and support for `jar2xml` will be removed
as part of .NET 6.

## AndroidCodegenTarget

A string property which controls the code generation target ABI.
Possible values include:

- **XamarinAndroid**: Uses the JNI binding API present since
  Mono for Android 1.0. Binding assemblies built with
  Xamarin.Android 5.0 or later can only run on Xamarin.Android 5.0
  or later (API/ABI additions), but the *source* is compatible with
  prior product versions.

- **XAJavaInterop1**: Use Java.Interop for JNI invocations. Binding
  assemblies using `XAJavaInterop1` can only build and execute with
  Xamarin.Android 6.1 or later. Xamarin.Android 6.1 and later bind
  `Mono.Android.dll` with this value.

The benefits of `XAJavaInterop1` include:

- Smaller assemblies.

- `jmethodID` caching for `base` method invocations,
  so long as all other binding types in the inheritance
  hierarchy are built with `XAJavaInterop1` or later.

- `jmethodID` caching of Java Callable Wrapper constructors for
  managed subclasses.

The default value is `XAJavaInterop1`.

## AndroidCreatePackagePerAbi

A boolean property that determines if a *set* of files -- on per ABI
specified in [`$(AndroidSupportedAbis)`](#androidsupportedabis) --
should be created instead of having support for all ABIs in a single `.apk`.

See also the [Building ABI-Specific APKs](~/android/deploy-test/building-apps/abi-specific-apks.md)
guide.

## AndroidDebugKeyAlgorithm

Specifies the default
algorithm to use for the `debug.keystore`. It defaults to
`RSA`.

## AndroidDebugKeyValidity

Specifies the default
validity to use for the `debug.keystore`. It defaults to
`10950` or `30 * 365` or `30 years`.

## AndroidDebugStoreType

Specifies the
key store file format to use for the `debug.keystore`. It defaults
to `pkcs12`.

Added in Xamarin.Android 10.2.


## AndroidDeviceUserId

Allows deploying and debugging the application under guest
or work accounts. The value is the `uid` value you get
from the following adb command:

```
adb shell pm list users
```

This will return the following data:

```
Users:
	UserInfo{0:Owner:c13} running
	UserInfo{10:Guest:404}
```

The `uid` is the first integer value. In the example they
are `0` and `10`.

This property was added in Xamarin.Android 11.2.

## AndroidDexTool

An enum-style property with valid
values of `dx` or `d8`. Indicates which Android [dex][dex]
compiler is used during the Xamarin.Android build process.
Currently defaults to `dx`. For further information see our
documentation on [D8 and R8][d8-r8].

[dex]: https://source.android.com/devices/tech/dalvik/dalvik-bytecode
[d8-r8]: https://github.com/xamarin/xamarin-android/blob/main/Documentation/guides/D8andR8.md

## AndroidEnableDesugar

A boolean property that
determines if `desugar` is enabled. Android does not currently
support all Java 8 features, and the default toolchain implements
the new language features by performing bytecode transformations,
called `desugar`, on the output of the `javac` compiler. Defaults
to `False` if using `AndroidDexTool=dx` and defaults to `True` if
using [`$(AndroidDexTool)`](#androiddextool)=`d8`.

## AndroidEnableGooglePlayStoreChecks

A bool property
which allows developers to disable the following Google Play
Store checks: XA1004, XA1005 and XA1006. This is useful for
developers who are not targeting the Google Play Store and do
not wish to run those checks.

Added in Xamarin.Android 9.4.

## AndroidEnableMultiDex

A boolean property that
determines whether or not multi-dex support will be used in the
final `.apk`.

Support for this property was added in Xamarin.Android 5.1.

This property is `False` by default.

## AndroidEnablePreloadAssemblies

A boolean property which controls
whether or not all managed assemblies bundled within the application package
are loaded during process startup or not.

When set to `True`, all assemblies bundled within the application package
will be loaded during process startup, before any application code is invoked.
This is consistent with what Xamarin.Android did in releases prior to
Xamarin.Android 9.2.

When set to `False`, assemblies will only be loaded on an as-needed basis.
This allows applications to startup faster, and is also more consistent with
desktop .NET semantics.  To see the time savings, set the `debug.mono.log`
System Property to include `timing`, and look for the
`Finished loading assemblies: preloaded` message within `adb logcat`.

Applications or libraries which use dependency injection may *require* that
this property be `True` if they in turn require that
`AppDomain.CurrentDomain.GetAssemblies()` return all assemblies within the
application bundle, even if the assembly wouldn't otherwise have been needed.

By default this value will be set to `True`.

Added in Xamarin.Android 9.2.

## AndroidEnableProfiledAot

A boolean property that
determines whether or not the AOT profiles are used during
Ahead-of-Time compilation.

The profiles are listed in
[`@(AndroidAotProfile)`](~/android/deploy-test/building-apps/build-items.md#androidaotprofile)
item group. This ItemGroup contains default profile(s). It can be overridden by
removing the existing one(s) and adding your own AOT profiles.

Support for this property was added in Xamarin.Android 9.4.

This property is `False` by default.

## AndroidEnableSGenConcurrent

A boolean property that
determines whether or not Mono's
[concurrent GC collector](https://www.mono-project.com/docs/about-mono/releases/4.8.0/#concurrent-sgen)
will be used.

Support for this property was added in Xamarin.Android 7.2.

This property is `False` by default.

## AndroidErrorOnCustomJavaObject

A boolean property that
determines whether types may implement `Android.Runtime.IJavaObject`
*without* also inheriting from `Java.Lang.Object` or `Java.Lang.Throwable`:

```csharp
class BadType : IJavaObject {
    public IntPtr Handle {
        get {return IntPtr.Zero;}
    }

    public void Dispose()
    {
    }
}
```

When True, such types will generate an XA4212 error, otherwise an
XA4212 warning will be generated.

Support for this property was added in Xamarin.Android 8.1.

This property is `True` by default.

## AndroidExplicitCrunch

No longer supported in Xamarin.Android 11.0.

## AndroidExtraAotOptions

A string property that allows
passing additional options to the Mono compiler during the `Aot`
task for projects that have either
[`$(AndroidEnableProfiledAot)`](#androidenableprofiledaot) or
[`$(AotAssemblies)`](#aotassemblies) set to `true`.
The string value of the property is added to the response file when
calling the Mono cross-compiler.

In general, this property should be left blank, but in certain
special scenarios it might provide useful flexibility.

Note that this property is different from the related
`$(AndroidAotAdditionalArguments)` property. That property places
comma-separated arguments into the `--aot` option of the Mono
compiler. `$(AndroidExtraAotOptions)` instead passes full standalone
space-separated options like `--verbose` or `--debug` to the
compiler.

Added in Xamarin.Android 10.2.

<a name="AndroidFastDeploymentType"></a>

## AndroidFastDeploymentType

A `:` (colon)-separated list
of values to control what types can be deployed to the
[Fast Deployment directory](~/android/deploy-test/building-apps/build-process.md#Fast_Deployment)
on the target device
when the [`$(EmbedAssembliesIntoApk)`](#embedassembliesintoapk) MSBuild
property is `False`. If a resource is fast deployed, it is *not*
embedded into the generated `.apk`, which can speed up deployment
times. (The more that is fast deployed, then the less frequently
the `.apk` needs to be rebuilt, and the install process can be
faster.) Valid values include:

- `Assemblies`: Deploy application assemblies.
- `Dexes`: Deploy `.dex` files, native libraries and typemaps.
  **This value can *only* be used on devices running
  Android 4.4 or later (API-19).**

The default value is `Assemblies`.

Support for Fast Deploying resources and assets via that system was
removed in commit [f0d565fe](https://github.com/xamarin/xamarin-android/commit/f0d565fe4833f16df31378c77bbb492ffd2904b9). This was becuase it required the use of
deprecated API's to work.

**Experimental**. This property was added in Xamarin.Android 6.1.

## AndroidManifestVersions

This is an ItemGroup which represents the final `android:versionCode` and
'android:versionName' which were written to the `AndroidManifest.xml` files.

This are read only and is for information only.

Each item in the group will contain the path to the `AndroidManifest.xml` the
data was retrieved from and two items of Metadata.

* `VersionCode`: The final code used.
* `VersionName`: The final name used.
* `Abi`: The abi for this manifest.

This is an item group because Xamarin.Android allows developers to generate
multiple apk files per abi. Each Apk can have a slightly different `android:versionCode`.

```xml
<AndroidManifestVersions Include="com.foo.bar"
  VersionCode="316241012" VersionName="1.0" Abi="all" />
<AndroidManifestVersions Include="com.foo.bar-armeabi-v7a"
  VersionCode="316241012" VersionName="1.0" Abi="armeabi-v7a" />
<AndroidManifestVersions Include="com.foo.bar-x86"
  VersionCode="316241012" VersionName="1.0" Abi="x86" />
```

Added in Xamarin.Android 11.3.


## AndroidGenerateJniMarshalMethods

A bool property which
enables generating of JNI marshal methods as part of the build
process. This greatly reduces the `System.Reflection` usage in the
binding helper code.

By default this will be set to False. If the developers wish to use
the new JNI marshal methods feature, they can set

```xml
<AndroidGenerateJniMarshalMethods>True</AndroidGenerateJniMarshalMethods>
```

in their `.csproj`. Alternatively provide the property on the command
line via

```shell
/p:AndroidGenerateJniMarshalMethods=True
```

**Experimental**. Added in Xamarin.Android 9.2.
The default value is False.

## AndroidGenerateJniMarshalMethodsAdditionalArguments

A string property which can be used to add additional parameters to
the `jnimarshalmethod-gen.exe` invocation.  This is useful for
debugging, so that options such as `-v`, `-d`, or `--keeptemp` can
be used.

Default value is empty string. It can be set in the `.csproj` file or
on the command line. For example:

```xml
<AndroidGenerateJniMarshalMethodsAdditionalArguments>-v -d --keeptemp</AndroidGenerateJniMarshalMethodsAdditionalArguments>
```

or:

```shell
/p:AndroidGenerateJniMarshalMethodsAdditionalArguments="-v -d --keeptemp"
```

Added in Xamarin.Android 9.2.

## AndroidGenerateLayoutBindings

Enables generation of [layout code-behind](https://github.com/xamarin/xamarin-android/blob/main/Documentation/guides/LayoutCodeBehind.md)
if set to `true` or disables it completely if set to `false`. The
default value is `false`.

## AndroidHttpClientHandlerType

Controls the default
`System.Net.Http.HttpMessageHandler` implementation which will be used by
the `System.Net.Http.HttpClient` default constructor. The value is an
assembly-qualified type name of an `HttpMessageHandler` subclass, suitable
for use with
[`System.Type.GetType(string)`](/dotnet/api/system.type.gettype#System_Type_GetType_System_String_).
The most common values for this property are:

- `Xamarin.Android.Net.AndroidClientHandler`: Use the Android Java APIs
  to perform network requests. This allows accessing TLS 1.2 URLs when
  the underlying Android version supports TLS 1.2. Only Android 5.0 and
  later reliably provide TLS 1.2 support through Java.

  This corresponds to the **Android** option in the Visual Studio
  property pages and the **AndroidClientHandler** option in the Visual
  Studio for Mac property pages.

  The new project wizard selects this option for new projects when the
  **Minimum Android Version** is configured to **Android 5.0
  (Lollipop)** or higher in Visual Studio or when **Target Platforms**
  is set to **Latest and Greatest** in Visual Studio for Mac.

- Unset/the empty string: This is equivalent to
  `System.Net.Http.HttpClientHandler, System.Net.Http`

  This corresponds to the **Default** option in the Visual Studio
  property pages.

  The new project wizard selects this option for new projects when the
  **Minimum Android Version** is configured to **Android 4.4.87** or
  lower in Visual Studio or when **Target Platforms** is set to **Modern
  Development** or **Maximum Compatibility** in Visual Studio for Mac.

- `System.Net.Http.HttpClientHandler, System.Net.Http`: Use the managed
  `HttpMessageHandler`.

  This corresponds to the **Managed** option in the Visual Studio
  property pages.

> [!NOTE]
> If TLS 1.2 support is required on Android versions prior to 5.0,
> *or* if TLS 1.2 support is required with the `System.Net.WebClient` and
> related APIs, then [`$(AndroidTlsProvider)`](#androidtlsprovider) should be used.

> [!NOTE]
> Support for this property works by setting the
> [`XA_HTTP_CLIENT_HANDLER_TYPE` environment variable](~/android/deploy-test/environment.md).
> A `$XA_HTTP_CLIENT_HANDLER_TYPE` value found in a file
> with a Build action of
> [`@(AndroidEnvironment)`](~/android/deploy-test/building-apps/build-items.md#androidenvironment)
> will take precedence.

Added in Xamarin.Android 6.1.

## AndroidIncludeWrapSh

A boolean value which indicates whether the Android wrapper script
([`wrap.sh`](https://developer.android.com/ndk/guides/wrap-script))
should be packaged into the APK. The property defaults to `false`
since the wrapper script may significantly influence the way the
application starts up and works and the script should be included only
when necessary e.g. for debugging or otherwise changing the
application startup/runtime behavior.

The script is added to the project using the
[`@(AndroidNativeLibrary)`](~/android/deploy-test/building-apps/build-items.md#androidnativelibrary)
build action, because it is placed in the same directory as
architecture-specific native libraries, and must be named `wrap.sh`.

The easiest way to specify path to the `wrap.sh` script is to put it
in a directory named after the target architecture. This approach will
work if you have just one `wrap.sh` per architecture:

```xml
<AndroidNativeLibrary Include="path/to/arm64-v8a/wrap.sh" />
```

However, if your project needs more than one `wrap.sh` per
architecture, for different purposes, this approach won't work.
Instead, in such cases the name can be specified using the `Link`
metadata of the `AndroidNativeLibrary`:

```xml
<AndroidNativeLibrary Include="/path/to/my/arm64-wrap.sh">
  <Link>lib\arm64-v8a\wrap.sh</Link>
</AndroidNativeLibrary>
```

If the `Link` metadata is used, the path specified in its value must
be a valid native architecture-specific library path, relative to the
APK root directory. The format of the path is `lib\ARCH\wrap.sh` where
`ARCH` can be one of:

+ `arm64-v8a`
+ `armeabi-v7a`
+ `x86_64`
+ `x86`

## AndroidJavadocVerbosity

Specifies how "verbose"
[C# XML Documentation Comments](https://docs.microsoft.com/en-us/dotnet/csharp/codedoc)
should be when importing Javadoc documentation within binding projects.

Requires use of the
[`@(JavaSourceJar)`](~/android/deploy-test/building-apps/build-items.md#javasourcejar)
build action.

This is an enum-style property, with possible values of `full` or
`intellisense`:

  * `intellisense`: Only emit the XML comments:
    [`<exception/>](https://docs.microsoft.com/en-us/dotnet/csharp/codedoc#exception),
    [`<param/>`](https://docs.microsoft.com/en-us/dotnet/csharp/codedoc#param),
    [`<returns/>`](https://docs.microsoft.com/en-us/dotnet/csharp/codedoc#returns),
    [`<summary/>`](https://docs.microsoft.com/en-us/dotnet/csharp/codedoc#summary).
  * `full`: Emit `intellisense` elements, as well as
    [`<remarks/>`](https://docs.microsoft.com/en-us/dotnet/csharp/codedoc#remarks),
    [`<seealso/>`](https://docs.microsoft.com/en-us/dotnet/csharp/codedoc#seealso),
    and anything else that's supportable.

The default value is `intellisense`.

Support for this property was added in Xamarin.Android 11.3.

## AndroidKeyStore

A boolean value which indicates whether
custom signing information should be used. The default value is
`False`, meaning that the default debug-signing key will be used
to sign packages.

## AndroidLaunchActivity

The Android activity to launch.

## AndroidLinkMode

Specifies which type of
[linking](~/android/deploy-test/linker.md) should be
performed on assemblies contained within the Android package. Only
used in Android Application projects. The default value is
*SdkOnly*. Valid values are:

- **None**: No linking will be attempted.

- **SdkOnly**: Linking will be performed on the base class
  libraries only, not user's assemblies.

- **Full**: Linking will be performed on base class libraries and
  user assemblies.

  > [!NOTE]
  > Using an `AndroidLinkMode` value of *Full* often
  > results in broken apps, particularly when Reflection is used. Avoid unless
  > you *really* know what you're doing.

```xml
<AndroidLinkMode>SdkOnly</AndroidLinkMode>
```

## AndroidLinkResources

When `true` this will make the build system link out the Nested Types
of the Resource.Designer.cs `Resource` class in all assemblies. The
IL code that uses those types will be updated to use the values
directly rather than accessing fields.

This can have a small impact on reducing the apk size, it might also
help slightly with startup performance. This will only effect "Release"
based builds.

***Experimental***.  Only designed to work with code such as

```
var view = FindViewById(Resources.Ids.foo);
```

Any other scenarios (such as reflection) will not be supported.

Support for this property was added in Xamarin.Android 11.3

## AndroidLinkSkip

Specifies a semicolon-delimited (`;`)
list of assembly names, without file extensions, of assemblies that
should not be linked. Only used within Android Application
projects.

```xml
<AndroidLinkSkip>Assembly1;Assembly2</AndroidLinkSkip>
```

## AndroidLinkTool

An enum-style property with valid
values of `proguard` or `r8`. Indicates which code shrinker is
used for Java code. Currently defaults to an empty string, or
`proguard` if `$(AndroidEnableProguard)` is `True`. For further
information see our documentation on [D8 and R8][d8-r8].

[d8-r8]: https://github.com/xamarin/xamarin-android/blob/main/Documentation/guides/D8andR8.md

## AndroidLintEnabled

A bool property which allows the developer to
run the android `lint` tool as part of the packaging process.

When `$(AndroidLintEnabled)`=True, the following properties are used:

- [`$(AndroidLintEnabledIssues)`](#androidlintenabledissues):
- [`$(AndroidLintDisabledIssues)`](#androidlintdisabledissues):
- [`$(AndroidLintCheckIssues)`](#androidlintcheckissues):

The following build actions may also be used:

- [`@(AndroidLintConfig)`](~/android/deploy-test/building-apps/build-items.md#androidlintconfig):

See [Lint Help](https://developer.android.com/studio/write/lint) for more details on
the android `lint` tooling.

## AndroidLintEnabledIssues

This property is only used when [`$(AndroidLintEnabled)`](#androidlintenabled)=True.

A comma-separated list of lint issues to enable.

## AndroidLintDisabledIssues

This property is only used when [`$(AndroidLintEnabled)`](#androidlintenabled)=True.

A comma-separated list of lint issues to disable.

## AndroidLintCheckIssues

This property is only used when [`$(AndroidLintEnabled)`](#androidlintenabled)=True.

A comma-separated list of lint issues to check.

Note: only these issues will be checked.

## AndroidManagedSymbols

A boolean property that controls
whether sequence points are generated so that file name and line
number information can be extracted from `Release` stack traces.

Added in Xamarin.Android 6.1.

## AndroidManifest

Specifies a filename to use as the template for the app's
[`AndroidManifest.xml`](~/android/platform/android-manifest.md).
During the build, any other necessary values will be merged into to
produce the actual `AndroidManifest.xml`.
The `$(AndroidManifest)` must contain the package name in the `/manifest/@package` attribute.

## AndroidManifestMerger

Specifies the implementation for
merging *AndroidManifest.xml* files. This is an enum-style property
where `legacy` selects the original C# implementation
and `manifestmerger.jar` selects Google's Java implementation.

The default value is currently `legacy`. This will change to
`manifestmerger.jar` in a future release to align behavior with
Android Studio.

Google's merger enables support for `xmlns:tools="http://schemas.android.com/tools"`
as described in the [Android documentation][manifest-merger].

Introduced in Xamarin.Android 10.2

[manifest-merger]: https://developer.android.com/studio/build/manifest-merge

## AndroidManifestPlaceholders

A semicolon-separated list of
key-value replacement pairs for *AndroidManifest.xml*, where each pair
has the format `key=value`.

For example, a property value of `assemblyName=$(AssemblyName)`
defines an `${assemblyName}` placeholder that can then appear in
*AndroidManifest.xml*:

```xml
<application android:label="${assemblyName}"
```

This provides a way to insert variables from the build process into
the *AndroidManifest.xml* file.

## AndroidMultiDexClassListExtraArgs

A string property
which allows developers to pass additional arguments to the
`com.android.multidex.MainDexListBuilder` when generating the
`multidex.keep` file.

One specific case is if you are getting the following error
during the `dx` compilation.

```text
com.android.dex.DexException: Too many classes in --main-dex-list, main dex capacity exceeded
```

If you are getting this error you can add the following to the
`.csproj`.

```xml
<DxExtraArguments>--force-jumbo </DxExtraArguments>
<AndroidMultiDexClassListExtraArgs>--disable-annotation-resolution-workaround</AndroidMultiDexClassListExtraArgs>
```

this should allow the `dx` step to succeed.

Added in Xamarin.Android 8.3.

## AndroidPackageFormat

An enum-style property with valid
values of `apk` or `aab`. This indicates if you want to package
the Android application as an [APK file][apk] or [Android App
Bundle][bundle]. App Bundles are a new format for `Release` builds
that are intended for submission on Google Play. This value
currently defaults to `apk`.

When `$(AndroidPackageFormat)` is set to `aab`, other MSBuild
properties are set, which are required for Android App Bundles:

- [`$(AndroidUseAapt2)`](~/android/deploy-test/building-apps/build-properties.md#androiduseaapt2) is `True`.
- [`$(AndroidUseApkSigner)`](#androiduseapksigner) is `False`.
- [`$(AndroidCreatePackagePerAbi)`](#androidcreatepackageperabi) is `False`.

[apk]: https://en.wikipedia.org/wiki/Android_application_package
[bundle]: https://developer.android.com/platform/technology/app-bundle

## AndroidPackageNamingPolicy

An enum-style property for
specifying the Java package names of generated Java source code.

In Xamarin.Android 10.2 and later, the only supported value is
`LowercaseCrc64`.

In Xamarin.Android 10.1, a transitional `LowercaseMD5` value was
also available that allowed switching back to the original Java
package name style as used in Xamarin.Android 10.0 and earlier. That
option was removed in Xamarin.Android 10.2 to improve compatibility
with build environments that have FIPS compliance enforced.

Added in Xamarin.Android 10.1.

## AndroidProguardMappingFile

Specifies the `-printmapping` proguard rule for `r8`. This will
mean the `mapping.txt` file will be produced in the `$(OutputPath)`
folder. This file can then be used when uploading packages to the
Google Play Store.

The default value is `$(OutputPath)mapping.txt`.

This property was added in Xamarin.Android 11.2.

## AndroidR8IgnoreWarnings

Specifies
the `-ignorewarnings` proguard rule for `r8`. This allows `r8`
to continue with dex compilation even if certain warnings are
encountered. Defaults to `True`, but can be set to `False` to
enforce more strict behavior. See the [ProGuard manual](https://www.guardsquare.com/products/proguard/manual/usage)
for details.

Added in Xamarin.Android 10.3.

## AndroidR8JarPath

The path to `r8.jar` for use with the
r8 dex-compiler and shrinker. Defaults to a path in the
Xamarin.Android installation. For further information see our
documentation on [D8 and R8][d8-r8].

## AndroidResgenExtraArgs

Specifies additional
command-line options to pass to the **aapt** command when
processing Android assets and resources.

## AndroidResgenFile

Specifies the name of the Resource
file to generate. The default template sets this to
`Resource.designer.cs`.

## AndroidSdkBuildToolsVersion

The Android SDK
build-tools package provides the **aapt** and **zipalign** tools,
among others. Multiple different versions of the build-tools package
may be installed simultaneously. The build-tools package chosen for
packaging is done by checking for and using a
"preferred" build-tools version if it is present; if
the "preferred" version is *not* present, then the
highest versioned installed build-tools package is used.

The `$(AndroidSdkBuildToolsVersion)` MSBuild property contains
the preferred build-tools version. The Xamarin.Android build system
provides a default value in `Xamarin.Android.Common.targets`, and
the default value may be overridden within your project file to
choose an alternate build-tools version, if (for example) the
latest aapt is crashing out while a previous aapt version is known
to work.

## AndroidSigningKeyAlias

Specifies the alias for the key
in the keystore. This is the **keytool -alias** value used when
creating the keystore.

## AndroidSigningKeyPass

Specifies the password of the key within the keystore file. This is
the value entered when `keytool` asks **Enter key password for
$(AndroidSigningKeyAlias)**.

In Xamarin.Android 10.0 and earlier, this property only supports
plain text passwords.

In Xamarin.Android 10.1 and later, this property also supports
`env:` and `file:` prefixes that can be used to specify an
environment variable or file that contains the password. These
options provide a way to prevent the password from appearing in
build logs.

For example, to use an environment variable named
*AndroidSigningPassword*:

```xml
<PropertyGroup>
    <AndroidSigningKeyPass>env:AndroidSigningPassword</AndroidSigningKeyPass>
</PropertyGroup>
```

To use a file located at `C:\Users\user1\AndroidSigningPassword.txt`:

```xml
<PropertyGroup>
    <AndroidSigningKeyPass>file:C:\Users\user1\AndroidSigningPassword.txt</AndroidSigningKeyPass>
</PropertyGroup>
```

> [!NOTE]
> The `env:` prefix is not supported when [`$(AndroidPackageFormat)`](#androidpackageformat)
> is set to `aab`.

## AndroidSigningKeyStore

Specifies the filename of the
keystore file created by `keytool`. This corresponds to the value
provided to the **keytool -keystore** option.

## AndroidSigningStorePass

Specifies the password to
[`$(AndroidSigningKeyStore)`](#androidsigningkeystore).
This is the value provided to
`keytool` when creating the keystore file and asked **Enter
keystore password:**.

In Xamarin.Android 10.0 and earlier, this property only supports
plain text passwords.

In Xamarin.Android 10.1 and later, this property also supports
`env:` and `file:` prefixes that can be used to specify an
environment variable or file that contains the password. These
options provide a way to prevent the password from appearing in
build logs.

For example, to use an environment variable named
*AndroidSigningPassword*:

```xml
<PropertyGroup>
    <AndroidSigningStorePass>env:AndroidSigningPassword</AndroidSigningStorePass>
</PropertyGroup>
```

To use a file located at `C:\Users\user1\AndroidSigningPassword.txt`:

```xml
<PropertyGroup>
    <AndroidSigningStorePass>file:C:\Users\user1\AndroidSigningPassword.txt</AndroidSigningStorePass>
</PropertyGroup>
```

> [!NOTE]
> The `env:` prefix is not supported when [`$(AndroidPackageFormat)`](#androidpackageformat)
> is set to `aab`.

## AndroidSigningPlatformKey

Specifies the key file to use to sign the apk.
This is only used when building `system` applications.

Support for this property was added in Xamarin.Android 11.3.

## AndroidSigningPlatformCert

Specifies the certificate file to use to sign the apk.
This is only used when building `system` applications.

Support for this property was added in Xamarin.Android 11.3.

## AndroidSupportedAbis

A string property that contains a
semicolon (`;`)-delimited list of ABIs which should be included
into the `.apk`.

Supported values include:

- `armeabi-v7a`
- `x86`
- `arm64-v8a`: Requires Xamarin.Android 5.1 and later.
- `x86_64`: Requires Xamarin.Android 5.1 and later.

## AndroidTlsProvider

A string value which specifies which
TLS provider should be used in an application. Possible values are:

- Unset/the empty string: In Xamarin.Android 7.3 and higher, this is
  equivalent to `btls`.

  In Xamarin.Android 7.1, this is equivalent to `legacy`.

  This corresponds to the **Default** setting in the Visual Studio
  property pages.

- `btls`: Use
  [Boring SSL](https://boringssl.googlesource.com/boringssl) for
  TLS communication with
  [HttpWebRequest](xref:System.Net.HttpWebRequest).

  This allows use of TLS 1.2 on all Android versions.

  This corresponds to the **Native TLS 1.2+** setting in the
  Visual Studio property pages.

- `legacy`: In Xamarin.Android 10.1 and earlier, use the historical
  managed SSL implementation for network interaction. This *does not* support TLS 1.2.

  This corresponds to the **Managed TLS 1.0** setting in the
  Visual Studio property pages.

  In Xamarin.Android 10.2 and later, this value is ignored and the
  `btls` setting is used.

- `default`: This value is unlikely to be used in Xamarin.Android
  projects. The recommended value to use instead is the empty string,
  which corresponds to the **Default** setting in the Visual Studio
  property pages.

  The `default` value is not offered in the Visual Studio property
  pages.

  This is currently equivalent to `legacy`.

Added in Xamarin.Android 7.1.

## AndroidUseAapt2

A boolean property which allows the developer to
control the use of the `aapt2` tool for packaging.
By default this will be False and Xamarin.Android will use `aapt`.
If the developer wishes to use the new `aapt2` functionality, add:

```xml
<AndroidUseAapt2>True</AndroidUseAapt2>
```

in their `.csproj`. Alternatively provide the property on the command line:

```shell
/p:AndroidUseAapt2=True
```

This property was added in Xamarin.Android 8.3. Setting `AndroidUseAapt2` to `false` is
deprecated in Xamarin.Android 11.2.

## AndroidUseApkSigner

A bool property which allows the developer to
use the `apksigner` tool rather than `jarsigner`.

Added in Xamarin.Android 8.2.

## AndroidUseDefaultAotProfile

A bool property that allows
the developer to suppress usage of the default AOT profiles.

To suppress the default AOT profiles, set the property to `false`.

Added in Xamarin.Android 10.1.

## AndroidUseInterpreter

A boolean property which causes the `.apk` to contain the mono
*interpreter*, and not the normal JIT.

***Experimental***.

Support for this property was added in Xamarin.Android 11.3.

## AndroidUseLegacyVersionCode

A boolean property which allows
the developer to revert the versionCode calculation back to its old pre
Xamarin.Android 8.2 behavior. This should ONLY be used for developers
with existing applications in the Google Play Store. It is highly recommended
that the new [`$(AndroidVersionCodePattern)`](#androidversioncodepattern)
property is used.

Added in Xamarin.Android 8.2.

## AndroidUseManagedDesignTimeResourceGenerator

A boolean property which
will switch over the design time builds to use the managed resource parser rather
than `aapt`.

Added in Xamarin.Android 8.1.

## AndroidUseSharedRuntime

A boolean property that
determines whether the *shared runtime packages* are required in
order to run the Application on the target device. Relying on the
shared runtime packages allows the Application package to be
smaller, speeding up the package creation and deployment process,
resulting in a faster build/deploy/debug turnaround cycle.

Prior to Xamarin.Android 11.2, this property should be `True` for
Debug builds, and `False` for Release projects.

This property was *removed* in Xamarin.Android 11.2.

## AndroidVersionCode

An MSBuild property that can be used as an alternative to
`/manifest/@android:versionCode` in the [`AndroidManifest.xml`](~/android/platform/android-manifest.md)
file. To opt into this feature you must also enable
`<GenerateApplicationManifest>true</GenerateApplicationManifest>`.
This will be the default going forward in .NET 6.

This property is ignored if
[`$(AndroidCreatePackagePerAbi)`](#androidcreatepackageperabi) and
[`$(AndroidVersionCodePattern)`](#androidversioncodepattern) are used.

`@android:versionCode` is an integer value that must be incremented
for each Google Play release. See the [Android documentation][manifest-element]
for further details about the requirements for
`/manifest/@android:versionCode`.

Support for this property was added in Xamarin.Android 11.3.

## AndroidVersionCodePattern

A string property which allows
the developer to customize the `versionCode` in the manifest.
See [Creating the Version Code for the APK](~/android/deploy-test/building-apps/abi-specific-apks.md)
for information on deciding a `versionCode`.

ome examples, if `abi` is `armeabi` and `versionCode` in the manifest
s `123`, `{abi}{versionCode}`
ill produce a versionCode of `1123` when `$(AndroidCreatePackagePerAbi)`
s True, otherwise will produce a value of 123.
f `abi` is `x86_64` and `versionCode` in the manifest
s `44`. This will produce `544` when `$(AndroidCreatePackagePerAbi)`
s True, otherwise will produce a value of `44`.

If we include a left padding format string
`{abi}{versionCode:0000}`,
it would produce `50044` because we are left padding the `versionCode`
with `0`. Alternatively, you can use the decimal padding such as
`{abi}{versionCode:D4}`
which does the same as the previous example.

Only '0' and 'Dx' padding format strings are supported since the value
MUST be an integer.

Pre-defined key items

- **abi**  &ndash; Inserts the targeted abi for the app
  - 2 &ndash; `armeabi-v7a`
  - 3 &ndash; `x86`
  - 4 &ndash; `arm64-v8a`
  - 5 &ndash; `x86_64`

- **minSDK**  &ndash; Inserts the minimum supported Sdk
  value from the `AndroidManifest.xml` or `11` if none is
  defined.

- **versionCode** &ndash; Uses the version code directly from
  `Properties\AndroidManifest.xml`.

You can define custom items using the `$(AndroidVersionCodeProperties)`
property (defined next).

By default the value will be set to `{abi}{versionCode:D6}`. If a developer
wants to keep the old behavior you can override the default by setting
the `$(AndroidUseLegacyVersionCode)` property to `true`

Added in Xamarin.Android 7.2.

## AndroidVersionCodeProperties

A string property which
allows the developer to define custom items to use with the
[`$(AndroidVersionCodePattern)`](#androidversioncodepattern).
They are in the form of a `key=value`
pair. All items in the `value` should be integer values. For
example: `screen=23;target=$(_AndroidApiLevel)`. As you can see
you can make use of existing or custom MSBuild properties in the
string.

Added in Xamarin.Android 7.2.

## ApplicationId

An MSBuild property that can be used as an alternative to
`/manifest/@package` in the [`AndroidManifest.xml`](~/android/platform/android-manifest.md)
file. To opt into this feature you must also enable
`<GenerateApplicationManifest>true</GenerateApplicationManifest>`.
This will be the default going forward in .NET 6.

See the [Android documentation][manifest-element] for further details
about the requirements for `/manifest/@package`.

[manifest-element]: https://developer.android.com/guide/topics/manifest/manifest-element

Support for this property was added in Xamarin.Android 11.3.

## ApplicationTitle

An MSBuild property that can be used as an alternative to
`/manifest/application/@android:label` in the [`AndroidManifest.xml`](~/android/platform/android-manifest.md)
file. To opt into this feature you must also enable
`<GenerateApplicationManifest>true</GenerateApplicationManifest>`.
This will be the default going forward in .NET 6.

See the [Android documentation][application-element] for further details
about the requirements for `/manifest/application/@android:label`.

Support for this property was added in Xamarin.Android 11.3.

[application-element]: https://developer.android.com/guide/topics/manifest/application-element

## ApplicationVersion

An MSBuild property that can be used as an alternative to
`/manifest/@android:versionName` in the [`AndroidManifest.xml`](~/android/platform/android-manifest.md)
file. To opt into this feature you must also enable
`<GenerateApplicationManifest>true</GenerateApplicationManifest>`.
This will be the default going forward in .NET 6.

See the [Android documentation][manifest-element] for further details
about the requirements for `/manifest/@android:versionName`.

Support for this property was added in Xamarin.Android 11.3.

## AotAssemblies

A boolean property that determines
whether or not assemblies will be Ahead-of-Time compiled into
native code and included in the `.apk`.

Support for this property was added in Xamarin.Android 5.1.

This property is `False` by default.

## AProfUtilExtraOptions

Extra options to pass to `aprofutil`.

## BeforeGenerateAndroidManifest

MSBuild Targets listed in this
property will run directly before `_GenerateJavaStubs`.

Added in Xamarin.Android 9.4.

## Configuration

Specifies the build configuration to use,
such as "Debug" or "Release". The
Configuration property is used to determine default values for
other properties which determine target behavior. Additional
configurations may be created within your IDE.

*By default*, the `Debug` configuration will result in the
[`Install`](~/android/deploy-test/building-apps/build-targets.md#install)
and
[`SignAndroidPackage`](~/android/deploy-test/building-apps/build-targets.md#signandroidpackage)
targets creating a smaller Android package which requires the presence of other
files and packages to operate.

The default `Release` configuration will result in the
[`Install`](~/android/deploy-test/building-apps/build-targets.md#install)
and
[`SignAndroidPackage`](~/android/deploy-test/building-apps/build-targets.md#signandroidpackage)
targets creating an Android package which is *stand-alone*, and may be used
without installing any other packages or files.

## DebugSymbols

A boolean value which determines whether
the Android package is *debuggable*, in combination with the
[`$(DebugType)`](#debugtype) property.
A debuggable package contains debug symbols, sets the
[`//application/@android:debuggable` attribute](https://developer.android.com/guide/topics/manifest/application-element#debug)
to `true`, and automatically adds the
[`INTERNET`](https://developer.android.com/reference/android/Manifest.permission#INTERNET)
permission so that a debugger can attach to the process. An application is
debuggable if `DebugSymbols` is `True` *and* `DebugType` is either the empty
string or `Full`.

## DebugType

Specifies the
[type of debug symbols](/visualstudio/msbuild/csc-task)
to generate as part of the build, which also impacts whether the
Application is debuggable. Possible values include:

- **Full**: Full symbols are generated. If the
  [`DebugSymbols`](#debugsymbols)
  MSBuild property is also `True`, then the Application package is
  debuggable.

- **PdbOnly**: "PDB" symbols are generated. The
  Application package is not debuggable.

If `DebugType` is not set or is the empty string, then the
`DebugSymbols` property controls whether or not the Application is
debuggable.

## EmbedAssembliesIntoApk

A boolean property that
determines whether or not the app's assemblies should be embedded
into the Application package.

This property should be `True` for Release builds and `False` for
Debug builds. It *may* need to be `True` in Debug builds if Fast
Deployment doesn't support the target device.

When this property is `False`, then the
[`$(AndroidFastDeploymentType)`](#androidfastdeploymenttype)
MSBuild property also controls what
will be embedded into the `.apk`, which can impact deployment and
rebuild times.

## EnableLLVM

A boolean property that determines whether
or not LLVM will be used when Ahead-of-Time compiling assemblies
into native code.

The Android NDK must be installed to build a project that has this
property enabled.

Support for this property was added in Xamarin.Android 5.1.

This property is `False` by default.

This property is ignored unless the
[`$(AotAssemblies)`](#aotassemblies) MSBuild property is `True`.

## EnableProguard

A boolean property that determines
whether or not [proguard](https://developer.android.com/tools/help/proguard.html)
is run as part of the packaging process to link Java code.

Support for this property was added in Xamarin.Android 5.1.

This property is `False` by default.

When `True`,
[@(ProguardConfiguration)](~/android/deploy-test/building-apps/build-items.md#proguardconfiguration)
files will be used
to control `proguard` execution.

## GenerateApplicationManifest

Enables or disables the following MSBuild properties that emit values
in the final [`AndroidManifest.xml`](~/android/platform/android-manifest.md) file:

- [`$(AndroidVersionCode)`](#androidversioncode)
- [`$(ApplicationId)`](#applicationid)
- [`$(ApplicationTitle)`](#applicationtitle)
- [`$(ApplicationVersion)`](#applicationversion)

`$(GenerateApplicationManifest)` defaults to `true` in .NET 6 and
`false` in "legacy" Xamarin.Android.

Support for this property was added in Xamarin.Android 11.3.

## JavaMaximumHeapSize

Specifies the value of the **java**
`-Xmx` parameter value to use when building the `.dex` file as part
of the packaging process. If not specified, then the `-Xmx` option
supplies **java** with a value of `1G`. This was found to be commonly
required on Windows in comparison to other platforms.

Specifying this property is necessary if the
[`_CompileDex` target throws a `java.lang.OutOfMemoryError`](https://bugzilla.xamarin.com/show_bug.cgi?id=18327).

Customize the value by changing:

```xml
<JavaMaximumHeapSize>1G</JavaMaximumHeapSize>
```

## JavaOptions

Specifies additional command-line options
to pass to **java** when building the `.dex` file.

## JarsignerTimestampAuthorityCertificateAlias

This
property allows you to specify an alias in the keystore
for a timestamp authority.
See the Java [Signature Timestamp Support](https://docs.oracle.com/javase/8/docs/technotes/guides/security/time-of-signing.html) documentation for more details.

```xml
<PropertyGroup>
    <JarsignerTimestampAuthorityCertificateAlias>Alias</JarsignerTimestampAuthorityCertificateAlias>
</PropertyGroup>
```

## JarsignerTimestampAuthorityUrl

This property
allows you to specify a URL to a timestamp authority
service. This can be used to make sure your `.apk` signature
includes a timestamp.
See the Java [Signature Timestamp Support](https://docs.oracle.com/javase/8/docs/technotes/guides/security/time-of-signing.html) documentation for more details.

```xml
<PropertyGroup>
    <JarsignerTimestampAuthorityUrl>http://example.tsa.url</JarsignerTimestampAuthorityUrl>
</PropertyGroup>
```

## LinkerDumpDependencies

A bool property which enables
generating of linker dependencies file. This file can be used as
input for
[illinkanalyzer](https://github.com/mono/linker/blob/master/src/analyzer/README.md)
tool.

The dependencies file named `linker-dependencies.xml.gz` is written
to the project directory. On .NET5/6 it is written next to the linked
assemblies in `obj/<Configuration>/android<ABI>/linked` directory.

The default value is False.

## MandroidI18n

Specifies the internationalization support
included with the Application, such as collation and sorting
tables. The value is a comma- or semicolon-separated list of one or
more of the following case-insensitive values:

- **None**: Include no additional encodings.

- **All**: Include all available encodings.

- **CJK**: Include Chinese, Japanese, and Korean encodings such as
  *Japanese (EUC)* \[enc-jp, CP51932\], *Japanese (Shift-JIS)*
  \[iso-2022-jp, shift\_jis, CP932\], *Japanese (JIS)* \[CP50220\],
  *Chinese Simplified (GB2312)* \[gb2312, CP936\], *Korean (UHC)*
  \[ks\_c\_5601-1987, CP949\], *Korean (EUC)* \[euc-kr, CP51949\],
  *Chinese Traditional (Big5)* \[big5, CP950\], and *Chinese
  Simplified (GB18030)* \[GB18030, CP54936\].

- **MidEast**: Include Middle-Eastern encodings such as *Turkish
  (Windows)* \[iso-8859-9, CP1254\], *Hebrew (Windows)*
  \[windows-1255, CP1255\], *Arabic (Windows)* \[windows-1256,
  CP1256\], *Arabic (ISO)* \[iso-8859-6, CP28596\], *Hebrew (ISO)*
  \[iso-8859-8, CP28598\], *Latin 5 (ISO)* \[iso-8859-9, CP28599\],
  and *Hebrew (Iso Alternative)* \[iso-8859-8, CP38598\].

- **Other**: Include Other encodings such as *Cyrillic (Windows)*
  \[CP1251\], *Baltic (Windows)* \[iso-8859-4, CP1257\], *Vietnamese
  (Windows)* \[CP1258\], *Cyrillic (KOI8-R)* \[koi8-r, CP1251\],
  *Ukrainian (KOI8-U)* \[koi8-u, CP1251\], *Baltic (ISO)*
  \[iso-8859-4, CP1257\], *Cyrillic (ISO)* \[iso-8859-5, CP1251\],
  *ISCII Davenagari* \[x-iscii-de, CP57002\], *ISCII Bengali*
  \[x-iscii-be, CP57003\], *ISCII Tamil* \[x-iscii-ta, CP57004\],
  *ISCII Telugu* \[x-iscii-te, CP57005\], *ISCII Assamese*
  \[x-iscii-as, CP57006\], *ISCII Oriya* \[x-iscii-or, CP57007\],
  *ISCII Kannada* \[x-iscii-ka, CP57008\], *ISCII Malayalam*
  \[x-iscii-ma, CP57009\], *ISCII Gujarati* \[x-iscii-gu, CP57010\],
  *ISCII Punjabi* \[x-iscii-pa, CP57011\], and *Thai (Windows)*
  \[CP874\].

- **Rare**: Include Rare encodings such as *IBM EBCDIC (Turkish)*
  \[CP1026\], *IBM EBCDIC (Open Systems Latin 1)* \[CP1047\], *IBM
  EBCDIC (US-Canada with Euro)* \[CP1140\], *IBM EBCDIC (Germany with
  Euro)* \[CP1141\], *IBM EBCDIC (Denmark/Norway with Euro)*
  \[CP1142\], *IBM EBCDIC (Finland/Sweden with Euro)* \[CP1143\],
  *IBM EBCDIC (Italy with Euro)* \[CP1144\], *IBM EBCDIC (Latin
  America/Spain with Euro)* \[CP1145\], *IBM EBCDIC (United Kingdom
  with Euro)* \[CP1146\], *IBM EBCDIC (France with Euro)* \[CP1147\],
  *IBM EBCDIC (International with Euro)* \[CP1148\], *IBM EBCDIC
  (Icelandic with Euro)* \[CP1149\], *IBM EBCDIC (Germany)*
  \[CP20273\], *IBM EBCDIC (Denmark/Norway)* \[CP20277\], *IBM EBCDIC
  (Finland/Sweden)* \[CP20278\], *IBM EBCDIC (Italy)* \[CP20280\],
  *IBM EBCDIC (Latin America/Spain)* \[CP20284\], *IBM EBCDIC (United
  Kingdom)* \[CP20285\], *IBM EBCDIC (Japanese Katakana Extended)*
  \[CP20290\], *IBM EBCDIC (France)* \[CP20297\], *IBM EBCDIC
  (Arabic)* \[CP20420\], *IBM EBCDIC (Hebrew)* \[CP20424\], *IBM
  EBCDIC (Icelandic)* \[CP20871\], *IBM EBCDIC (Cyrillic - Serbian,
  Bulgarian)* \[CP21025\], *IBM EBCDIC (US-Canada)* \[CP37\], *IBM
  EBCDIC (International)* \[CP500\], *Arabic (ASMO 708)* \[CP708\],
  *Central European (DOS)* \[CP852\]*, Cyrillic (DOS)* \[CP855\],
  *Turkish (DOS)* \[CP857\], *Western European (DOS with Euro)*
  \[CP858\], *Hebrew (DOS)* \[CP862\], *Arabic (DOS)* \[CP864\],
  *Russian (DOS)* \[CP866\], *Greek (DOS)* \[CP869\], *IBM EBCDIC
  (Latin 2)* \[CP870\], and *IBM EBCDIC (Greek)* \[CP875\].

- **West**: Include Western encodings such as *Western European
  (Mac)* \[macintosh, CP10000\], *Icelandic (Mac)* \[x-mac-icelandic,
  CP10079\], *Central European (Windows)* \[iso-8859-2, CP1250\],
  *Western European (Windows)* \[iso-8859-1, CP1252\], *Greek
  (Windows)* \[iso-8859-7, CP1253\], *Central European (ISO)*
  \[iso-8859-2, CP28592\], *Latin 3 (ISO)* \[iso-8859-3, CP28593\],
  *Greek (ISO)* \[iso-8859-7, CP28597\], *Latin 9 (ISO)*
  \[iso-8859-15, CP28605\], *OEM United States* \[CP437\], *Western
  European (DOS)* \[CP850\], *Portuguese (DOS)* \[CP860\], *Icelandic
  (DOS)* \[CP861\], *French Canadian (DOS)* \[CP863\], and *Nordic
  (DOS)* \[CP865\].

```xml
<MandroidI18n>West</MandroidI18n>
```

## MonoAndroidResourcePrefix

Specifies a *path prefix*
that is removed from the start of filenames with a Build action of
`AndroidResource`. This is to allow changing where resources are
located.

The default value is `Resources`. Change this to `res` for the
Java project structure.

## MonoSymbolArchive

A boolean property which controls
whether `.mSYM` artifacts are created for later use with
`mono-symbolicate`, to extract &ldquo;real&rdquo; filename and line
number information from Release stack traces.

This is True by default for &ldquo;Release&rdquo; apps which have
debugging symbols enabled:
[`$(EmbedAssembliesIntoApk)`](#embedassembliesintoapk) is True,
[`$(DebugSymbols)`](~/android/deploy-test/building-apps/build-properties.md#debugsymbols)
 is True, and
[`$(Optimize)`](/visualstudio/msbuild/common-msbuild-project-properties)
is True.

Added in Xamarin.Android 7.1.
