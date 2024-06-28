---
title: .NET for Android Build Items
description: .NET for Android Build Items
ms.date: 04/11/2024
---

# Build Items

Build items control how a .NET for Android application
or library project is built.

## AndroidAdditionalJavaManifest

`<AndroidAdditionalJavaManifest>` is used in conjunction with
[Java Dependency Resolution](../features/maven/java-dependency-verification.md).

It is used to specify additional POM files that will be needed to verify dependencies.
These are often parent or imported POM files referenced by a Java library's POM file.

```xml
<ItemGroup>
  <AndroidAdditionalJavaManifest Include="mylib-parent.pom" JavaArtifact="com.example:mylib-parent" JavaVersion="1.0.0" />
</ItemGroup>
```

The following MSBuild metadata are required:

- `%(JavaArtifact)`: The group and artifact id of the Java library matching the specifed POM
  file in the form `{GroupId}:{ArtifactId}`.
- `%(JavaVersion)`: The version of the Java library matching the specified POM file.
  
See the [Java Dependency Resolution documentation](../features/maven/java-dependency-verification.md)
for more details.

This build action was introduced in .NET 9.

## AndroidAsset

Supports [Android Assets](https://developer.android.com/guide/topics/resources/providing-resources#OriginalFiles),
files that would be included in the `assets` folder in a Java Android project.

Starting with .NET 9 the `@(AndroidAsset)` build action also supports additional metadata for generating [Asset Packs](https://developer.android.com/guide/playcore/asset-delivery). The `%(AndroidAsset.AssetPack)` metadata can be used to automatically generate an asset pack of that name. This feature is only supported when the [`$(AndroidPackageFormat)`](build-properties.md#androidpackageformat) is set to `.aab`. The following example will place `movie2.mp4` and `movie3.mp4` in separate asset packs.

```xml
<ItemGroup>
   <AndroidAsset Update="Asset/movie.mp4" />
   <AndroidAsset Update="Asset/movie2.mp4" AssetPack="assets1" />
   <AndroidAsset Update="Asset/movie3.mp4" AssetPack="assets2" />
</ItemGroup>
```

This feature can be used to include large files in your application which would normally exceed the max
package size limits of Google Play.

If you have a large number of assets it might be more efficient to make use of the `base` asset pack.
In this scenario you update ALL assets to be in a single asset pack then use the `AssetPack="base"` metadata
to declare which specific assets end up in the base aab file. With this you can use wildcards to move most
assets into the asset pack.

```xml
<ItemGroup>
   <AndroidAsset Update="Assets/*" AssetPack="assets1" />
   <AndroidAsset Update="Assets/movie.mp4" AssetPack="base" />
   <AndroidAsset Update="Assets/some.png" AssetPack="base" />
</ItemGroup>
```

In this example, `movie.mp4` and `some.png` will end up in the `base` aab file, while all the other assets
will end up in the `assets1` asset pack.

The additional metadata is only supported on .NET for Android 9 and above.

## AndroidAarLibrary

The Build action of `AndroidAarLibrary` should be used to directly
reference `.aar` files. This build action will be most commonly used
by Xamarin Components. Namely to include references to `.aar` files
that are required to get Google Play and other services working.

Files with this Build action will be treated in a similar fashion to
the embedded resources found in Library projects. The `.aar` will be
extracted into the intermediate directory. Then any assets, resource
and `.jar` files will be included in the appropriate item groups.

## AndroidAotProfile

Used to provide an AOT profile, for use with profile-guided AOT.

It can be also used from Visual Studio by setting the `AndroidAotProfile`
build action to a file containing an AOT profile.

## AndroidAppBundleMetaDataFile

Specifies a file that will be included as metadata in the Android App Bundle.
The format of the flag value is `<bundle-path>:<physical-file>` where
`bundle-path` denotes the file location inside the App Bundle's metadata
directory, and `physical-file` is an existing file containing the raw data
to be stored.

```xml
<ItemGroup>
  <AndroidAppBundleMetaDataFile
    Include="com.android.tools.build.obfuscation/proguard.map:$(OutputPath)mapping.txt"
  />
</ItemGroup>
```

See [bundletool](https://developer.android.com/studio/build/building-cmdline#build_your_app_bundle_using_bundletool) documentation for more details.

## AndroidBoundLayout

Indicates that the layout file is to have [code-behind generated](../features/layout-code-behind/index.md)
for it in case when the [`$(AndroidGenerateLayoutBindings)`](build-properties.md#androidgeneratelayoutbindings)
property is set to `false`. In all other aspects
it is identical to [`AndroidResource`](#androidresource).

This action can be used **only** with layout files:

```xml
<AndroidBoundLayout Include="Resources\layout\Main.axml" />
```

## AndroidEnvironment

Files with a Build action of `AndroidEnvironment` are used
to [initialize environment variables and system properties during process startup](/xamarin/android/deploy-test/environment).
The `AndroidEnvironment` Build action may be applied to
multiple files, and they will be evaluated in no particular order (so don't
specify the same environment variable or system property in multiple
files).

## AndroidJavaLibrary

Files with a Build action of `AndroidJavaLibrary` are Java
Archives ( `.jar` files) that will be included in the final Android
package.

## AndroidIgnoredJavaDependency

`<AndroidIgnoredJavaDependency>` is used in conjunction with [Java Dependency Resolution](../features/maven/java-dependency-verification.md).

It is used to specify a Java dependency that should be ignored. This can be
used if a dependency will be fulfilled in a way that Java dependency resolution
cannot detect.

```xml
<!-- Include format is {GroupId}:{ArtifactId} -->
<ItemGroup>
  <AndroidIgnoredJavaDependency Include="com.google.errorprone:error_prone_annotations" Version="2.15.0" />
</ItemGroup>
```

The following MSBuild metadata are required:

- `%(Version)`: The version of the Java library matching the specified `%(Include)`.

See the [Java Dependency Resolution documentation](../features/maven/java-dependency-verification.md)
for more details.

This build action was introduced in .NET 9.

## AndroidJavaSource

Files with a Build action of `AndroidJavaSource` are Java source code that
will be included in the final Android package.

Starting with .NET 7, all `**\*.java` files within the project directory
automatically have a Build action of `AndroidJavaSource`, *and* will be
bound prior to the Assembly build.  Allows C# code to easily use
types and members present within the `**\*.java` files.

Set `%(AndroidJavaSource.Bind)` to False to disable this behavior.

## AndroidLibrary

**AndroidLibrary** is a new build action for simplifying how
`.jar` and `.aar` files are included in projects.

Any project can specify:

```xml
<ItemGroup>
  <AndroidLibrary Update="foo.jar" />
  <AndroidLibrary Update="bar.aar" />
</ItemGroup>
```

The result of the above code snippet has a different effect for each
.NET for Android project type:

* Application and class library projects:
  * `foo.jar` maps to [**AndroidJavaLibrary**](#androidjavalibrary).
  * `bar.aar` maps to [**AndroidAarLibrary**](#androidaarlibrary).
* Java binding projects:
  * `foo.jar` maps to [**EmbeddedJar**](#embeddedjar).
  * `foo.jar` maps to [**EmbeddedReferenceJar**](#embeddedreferencejar)
    if `Bind="false"` metadata (attribute) is added.
  * `bar.aar` maps to [**LibraryProjectZip**](#libraryprojectzip).

This simplification means you can use **AndroidLibrary** everywhere.

## AndroidLintConfig

The Build action 'AndroidLintConfig' should be used in conjunction with the
[`$(AndroidLintEnabled)`](/xamarin/android/deploy-test/building-apps/build-properties.md#androidlintenabled)
property. Files with this build action will be merged together and passed to the
android `lint` tooling. They should be XML files containing information on
tests to enable and disable.

See the [lint documentation](https://developer.android.com/studio/write/lint)
for more details.

## AndroidManifestOverlay

The `AndroidManifestOverlay` build action can be used to provide
`AndroidManifest.xml` files to the [Manifest Merger](https://developer.android.com/studio/build/manifest-merge) tool.
Files with this build action will be passed to the Manifest Merger along with
the main `AndroidManifest.xml` file and manifest files from
references. These will then be merged into the final manifest.

You can use this build action to provide changes and settings to
your app depending on your build configuration. For example, if you need to
have a specific permission only while debugging, you can use the overlay to
inject that permission when debugging. For example, given the following
overlay file contents:

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
  <uses-permission android:name="android.permission.CAMERA" />
</manifest>
```

You can use the following to add a manifest overlay for a debug build:

```xml
<ItemGroup>
  <AndroidManifestOverlay Include="DebugPermissions.xml" Condition=" '$(Configuration)' == 'Debug' " />
</ItemGroup>
```

## AndroidInstallModules

Specifies the modules that get installed by **bundletool** command when
installing app bundles.

## AndroidMavenLibrary

`<AndroidMavenLibrary>` allows a Maven artifact to be specified which will 
automatically be downloaded and added to a .NET for Android binding project. 
This can be useful to simplify maintenance of .NET for Android bindings for artifacts 
hosted in Maven.

```xml
<!-- Include format is {GroupId}:{ArtifactId} -->
<ItemGroup>
  <AndroidMavenLibrary Include="com.squareup.okhttp3:okhttp" Version="4.9.3" />
</ItemGroup>
```

The following MSBuild metadata are supported:

- `%(Version)`: Required version of the Java library referenced by `%(Include)`.
- `%(Repository)`: Optional Maven repository to use. Supported values are `Central` (default),
   `Google`, or an `https` URL to a Maven repository.

The `<AndroidMavenLibrary>` item is translated to
[`AndroidLibrary`](#androidlibrary), so any metadata supported by
`<AndroidLibrary>` like `%(Bind)` or `%(Pack)` are also supported.

See the [AndroidMavenLibrary documentation](../features/maven/android-maven-library.md)
for more details.

This build action was introduced in .NET 9.

## AndroidNativeLibrary

[Native libraries](/xamarin/android/platform/native-libraries)
are added to the build by setting their Build action to
`AndroidNativeLibrary`.

Note that since Android supports multiple Application Binary Interfaces
(ABIs), the build system must know the ABI the native library is
built for. There are two ways the ABI can be specified:

1. Path "sniffing".
2. Using the  `%(Abi)` item metadata.

With path sniffing, the parent directory name of the native library is
used to specify the ABI that the library targets. Thus, if you add
`lib/armeabi-v7a/libfoo.so` to the build, then the ABI will be "sniffed" as
`armeabi-v7a`.

### Item Attribute Name

**Abi** &ndash; Specifies the ABI of the native library.

```xml
<ItemGroup>
  <AndroidNativeLibrary Include="path/to/libfoo.so">
    <Abi>armeabi-v7a</Abi>
  </AndroidNativeLibrary>
</ItemGroup>
```

## AndroidPackagingOptionsExclude

A set of file glob compatible items which will allow for items to be
excluded from the final package. The default values are as follows

```xml
<ItemGroup>
	<AndroidPackagingOptionsExclude Include="DebugProbesKt.bin" />
	<AndroidPackagingOptionsExclude Include="$([MSBuild]::Escape('*.kotlin_*')" />
</ItemGroup>
```

Items can use file blob characters for wildcards such as `*` and `?`.
However these Items MUST be URL encoded or use
[`$([MSBuild]::Escape(''))`](/visualstudio/msbuild/how-to-escape-special-characters-in-msbuild).
This is so MSBuild does not try to interpret them as actual file wildcards.

For example 

```xml
<ItemGroup>
	<AndroidPackagingOptionsExclude Include="%2A.foo_%2A" />
  <AndroidPackagingOptionsExclude Include="$([MSBuild]::Escape('*.foo')" />
</ItemGroup>
```

NOTE: `*`, `?` and `.` will be replaced in the `BuildApk` task with the
appropriate file globs.

If the default file glob is too restrictive you can remove it by adding the 
following to your csproj

```xml
<ItemGroup>
	<AndroidPackagingOptionsExclude Remove="$([MSBuild]::Escape('*.kotlin_*')" />
</ItemGroup>
```

Added in .NET 7.

## AndroidPackagingOptionsInclude

A set of file glob compatible items which will allow for items to be
included from the final package. The default values are as follows

```xml
<ItemGroup>
	<AndroidPackagingOptionsInclude Include="$([MSBuild]::Escape('*.kotlin_builtins')" />
</ItemGroup>
```

Items can use file blob characters for wildcards such as `*` and `?`.
However these Items MUST use URL encoding or '$([MSBuild]::Escape(''))'.
This is so MSBuild does not try to interpret them as actual file wildcards.
For example 

```xml
<ItemGroup>
	<AndroidPackagingOptionsInclude Include="%2A.foo_%2A" />
  <AndroidPackagingOptionsInclude Include="$([MSBuild]::Escape('*.foo')" />
</ItemGroup>
```

NOTE: `*`, `?` and `.` will be replaced in the `BuildApk` task with the
appropriate file globs.

Added in .NET 9.

## AndroidResource

All files with an *AndroidResource* build action are compiled into
Android resources during the build process and made accessible via `$(AndroidResgenFile)`.

```xml
<ItemGroup>
  <AndroidResource Include="Resources\values\strings.xml" />
</ItemGroup>
```

More advanced users might perhaps wish to have different resources used in
different configurations but with the same effective path. This can be achieved
by having multiple resource directories and having files with the same relative
paths within these different directories, and using MSBuild conditions to
conditionally include different files in different configurations. For
example:

```xml
<ItemGroup Condition=" '$(Configuration)' != 'Debug' ">
  <AndroidResource Include="Resources\values\strings.xml" />
</ItemGroup>
<ItemGroup  Condition=" '$(Configuration)' == 'Debug' ">
  <AndroidResource Include="Resources-Debug\values\strings.xml"/>
</ItemGroup>
<PropertyGroup>
  <MonoAndroidResourcePrefix>Resources;Resources-Debug</MonoAndroidResourcePrefix>
</PropertyGroup>
```

**LogicalName** &ndash; Specifies the resource path explicitly. Allows
&ldquo;aliasing&rdquo; files so that they will be available as multiple
distinct resource names.

```xml
<ItemGroup Condition="'$(Configuration)'!='Debug'">
  <AndroidResource Include="Resources/values/strings.xml"/>
</ItemGroup>
<ItemGroup Condition="'$(Configuration)'=='Debug'">
  <AndroidResource Include="Resources-Debug/values/strings.xml">
    <LogicalName>values/strings.xml</LogicalName>
  </AndroidResource>
</ItemGroup>
```

## Content

The normal `Content` Build action is not supported (as we
haven't figured out how to support it without a possibly costly first-run
step).

Attempting to use the `@(Content)` Build action will result in a
[XA0101](../messages/xa0101.md) warning.

## EmbeddedJar

In a .NET for Android binding project, the **EmbeddedJar** build action
binds the Java/Kotlin library and embeds the `.jar` file into the
library. When a .NET for Android application project consumes the
library, it will have access to the Java/Kotlin APIs from C# as well
as include the Java/Kotlin code in the final Android application.

You should instead use the
[AndroidLibrary](#androidlibrary) build action as an alternative
such as:

```xml
<Project>
  <ItemGroup>
    <AndroidLibrary Include="Library.jar" />
  </ItemGroup>
</Project>
```

## EmbeddedNativeLibrary

In a .NET for Android class library or Java binding project, the
**EmbeddedNativeLibrary** build action bundles a native library such
as `lib/armeabi-v7a/libfoo.so` into the library. When a
.NET for Android application consumes the library, the `libfoo.so` file
will be included in the final Android application.

You can use the
[**AndroidNativeLibrary**](#androidnativelibrary) build action as an
alternative.

## EmbeddedReferenceJar

In a .NET for Android binding project, the **EmbeddedReferenceJar**
build action embeds the `.jar` file into the library but does not
create a C# binding as [**EmbeddedJar**](#embeddedjar) does. When a
.NET for Android application project consumes the library, it will
include the Java/Kotlin code in the final Android application.

You can use the
[**AndroidLibrary**](#androidlibrary) build action as an alternative
such as `<AndroidLibrary Include="..." Bind="false" />`:

```xml
<Project>
  <ItemGroup>
    <!-- A .jar file to bind & embed -->
    <AndroidLibrary Include="Library.jar" />
    <!-- A .jar file to only embed -->
    <AndroidLibrary Include="Dependency.jar" Bind="false" />
  </ItemGroup>
</Project>
```

## JavaSourceJar

In a .NET for Android binding project, the **JavaSourceJar** build action
is used on `.jar` files that contain *Java source code*, that contain
[Javadoc documentation comments](https://www.oracle.com/technical-resources/articles/java/javadoc-tool.html).

Javadoc will instead be converted into
[C# XML Documentation Comments](/dotnet/csharp/codedoc)
within the generated binding source code.

[`$(AndroidJavadocVerbosity)`](build-properties.md#androidjavadocverbosity)
controls how "verbose" or "complete" the imported Javadoc is.

The following MSBuild metadata is supported:

* `%(CopyrightFile)`: A path to a file that contains copyright
    information for the Javadoc contents, which will be appended to
    all imported documentation.

* `%(UrlPrefix)`: A URL prefix to support linking to online
    documentation within imported documentation.

* `%(UrlStyle)`: The "style" of URLs to generate when linking to
    online documentation.  Only one style is currently supported:
    `developer.android.com/reference@2020-Nov`.

* `%(DocRootUrl)`: A URL prefix to use in place of all `{@docroot}`
    instances in the imported documentation.


## LibraryProjectZip

The **LibraryProjectZip** build
action binds the Java/Kotlin library and embeds the `.zip` or `.aar`
file into the library. When a .NET for Android application project
consumes the library, it will have access to the Java/Kotlin APIs from
C# as well as include the Java/Kotlin code in the final Android
application.

## LinkDescription

Files with a *LinkDescription* build action are used to
[control linker behavior](/xamarin/cross-platform/deploy-test/linker).

## ProguardConfiguration

Files with a *ProguardConfiguration* build action contain options which
are used to control `proguard` behavior. For more information about
this build action, see
[ProGuard](/xamarin/android/deploy-test/release-prep/proguard).

These files are ignored unless the
[`$(EnableProguard)`](/xamarin/android/deploy-test/building-apps/build-properties.md#enableproguard)
MSBuild property is `True`.
