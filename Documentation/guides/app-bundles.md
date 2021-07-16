This is the Android App Bundle and `bundletool` integration
specification for Xamarin.Android.

# What are "app bundles"?

[Android App Bundles][app_bundle] are a new publishing format for
Google Play that has a wide array of benefits.

* You no longer have to upload multiple APKs to Google Play:

    > With the Android App Bundle, you build one artifact that
    > includes all of your app's compiled code, resources, and native
    > libraries for your app. You no longer need to build, sign,
    > upload, and manage version codes for multiple APKs.

* "Dynamic Delivery" provides an optimized APK download from Google
  Play:

    > Google Play’s Dynamic Delivery uses your Android App Bundle to
    > build and serve APKs that are optimized for each device
    > configuration. This results in a smaller app download for
    > end-users by removing unused code and resources needed for other
    > devices.

These first two features of Android App Bundles are a natural fit for
Xamarin.Android apps. The first version of `bundletool` support in
Xamarin.Android will focus on these two benefits.

*Unfortunately* the next two features will be more involved. We could
perhaps support them in Xamarin.Android down the road.

* Support for "Instant Apps":

    > Instant-enable your Android App Bundle, so that users can launch
    > an instant app entry point module from the Try Now button on
    > Google Play and web links without installation.

Xamarin.Android does not yet have full support for [Instant
Apps][instant_apps], in general. There would likely be some changes
needed to the runtime, and there is a file size limit on the base APK
size. App Bundles won't necessarily help anything for this.

* Deliver features on-demand:

    > Further reduce the size of your app by installing only the
    > features that the majority of your audience use. Users can
    > download and install dynamic features when they’re needed. Use
    > Android Studio 3.2 to build apps with dynamic features, and join
    > the beta program to publish them on Google Play.

For Xamarin.Android to implement this feature, I believe Instant App
support is needed first.

For more information on App Bundles, visit the [getting
started][getting_started] guide.

[app_bundle]: https://developer.android.com/platform/technology/app-bundle
[instant_apps]: https://developer.android.com/topic/google-play-instant
[getting_started]: https://developer.android.com/guide/app-bundle/

# What is `bundletool`?

[bundletool][bundletool] is the underlying command-line tool that
gradle, Android Studio, and Google Play use for working with Android
App Bundles.

Xamarin.Android will need to run `bundletool` for the following cases:

* Create an Android App Bundle from a "base" zip file
* Create an APK Set (`.apks` file) from an Android App Bundle
* Deploy an APK Set (`.apks` file) to a device or emulator

The help text for `bundletool` reads:

```
Synopsis: bundletool <command> ...

Use 'bundletool help <command>' to learn more about the given command.

build-bundle command:
    Builds an Android App Bundle from a set of Bundle modules provided as zip
    files.

build-apks command:
    Generates an APK Set archive containing either all possible split APKs and
    standalone APKs or APKs optimized for the connected device (see connected-
    device flag).

extract-apks command:
    Extracts from an APK Set the APKs that should be installed on a given
    device.

get-device-spec command:
    Writes out a JSON file containing the device specifications (i.e. features
    and properties) of the connected Android device.

install-apks command:
    Installs APKs extracted from an APK Set to a connected device. Replaces
    already installed package.

validate command:
    Verifies the given Android App Bundle is valid and prints out information
    about it.

dump command:
    Prints files or extract values from the bundle in a human-readable form.

get-size command:
    Computes the min and max download sizes of APKs served to different devices
    configurations from an APK Set.

version command:
    Prints the version of BundleTool.
```

The source code for `bundletool` is on [Github][github]!

[bundletool]: https://developer.android.com/studio/command-line/bundletool
[github]: https://github.com/google/bundletool

# Implementation

To enable app bundles, a new MSBuild property is needed:

```xml
<AndroidPackageFormat>aab</AndroidPackageFormat>
```

`$(AndroidPackageFormat)` will default to `apk` for the current
Xamarin.Android behavior.

Due to the various requirements for Android App Bundles, here are a
reasonable set of defaults for `bundletool`:
```xml
<AndroidPackageFormat       Condition=" '$(AndroidPackageFormat)' == '' ">apk</AndroidPackageFormat>
<AndroidUseAapt2            Condition=" '$(AndroidPackageFormat)' == 'aab' ">True</AndroidUseAapt2>
<AndroidUseApkSigner        Condition=" '$(AndroidPackageFormat)' == 'aab' ">False</AndroidUseApkSigner>
<AndroidCreatePackagePerAbi Condition=" '$(AndroidCreatePackagePerAbi)' == 'aab' ">False</AndroidCreatePackagePerAbi>
```

Adding `<AndroidPackageFormat>` for Android App Bundles would most
commonly be enabled in `Release` builds for submission to Google Play:

```xml
<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
  <AndroidPackageFormat>apk</AndroidPackageFormat>
</PropertyGroup>
<PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
  <AndroidPackageFormat>aab</AndroidPackageFormat>
</PropertyGroup>
```

Using `$(AndroidPackageFormat)` could impact build times, since it
takes some time for `bundletool` to generate an app bundle and
device-specific APK set to be deployed. It would make sense, in a
given Xamarin.Android `.csproj` file to use `apk` for `Debug` builds
and `aab` for `Release` builds.

## aapt2

The first requirement is that App Bundles require a special protobuf
format for resource files that can only be produced by `aapt2`. Adding
the `--proto-format` flag to the `aapt2` call produces a
`resources.pb` file:

```
aapt2 link [options] -o arg --manifest arg files...

Options:
...

--proto-format

Generates compiled resources in Protobuf format.
Suitable as input to the bundle tool for generating an App Bundle.
```

This command-line switch is new in `aapt2` and can only be used with
the version of `aapt2` from Maven. We are now shipping this new
version in Xamarin.Android.

## Generate a base ZIP file

Once we have a `resources.pb` file, we must generate a [base ZIP
file][zip_format] of the following structure:

* `manifest/AndroidManifest.xml`: in protobuf format
* `dex/`: all `.dex` files
* `res/`: all Android resources
* `assets/`: all Android assets
* `lib/`: all native libraries (`.so` files)
* `root/`: any arbitrary files that need to go in the root of the
  final APK on-device. Xamarin.Android will need to put .NET
  assemblies in `root/assemblies`.
* `resources.pb`: the resource table in protobuf format

See the [.aab format spec][aab_format] for further detail.

[zip_format]: https://developer.android.com/studio/build/building-cmdline#package_pre-compiled_code_and_resources
[aab_format]: https://developer.android.com/guide/app-bundle#aab_format

## BundleConfig.json

Since .NET assemblies and typemap files must remain uncompressed in
Xamarin.Android apps, we will also need to specify a
`BundleConfig.json` file:

```json
{
  "compression": {
    "uncompressedGlob": ["typemap.mj", "typemap.jm", "assemblies/*"]
  }
}
```

We also must include rules for what is specified in
`$(AndroidStoreUncompressedFileExtensions)`, which is currently a
delimited list of file extensions. Prepending `**/*` to each extension
should match the glob-pattern syntax that `bundletool` expects.

See details about `BundleConfig.json` in the [app bundle
documentation][bundleconfig_json], or the [proto3 declaration on
Github][bundleconfig_proto].

From here we can generate a `.aab` file with:

```
bundletool build-bundle --modules=base.zip --output=foo.aab --config=BundleConfig.json
```

[bundleconfig_json]: https://developer.android.com/studio/build/building-cmdline#bundleconfig
[bundleconfig_proto]: https://github.com/google/bundletool/blob/8e3aef8dd8ba239874008df33324b6f343261139/src/main/proto/config.proto

## Native Libraries

It appears that app bundles use `android:extractNativeLibs="false"` by
default, so that native libraries remain in the APK, but stored
uncompressed.

They take it even further, in that the current default behavior
(`extractNativeLibs="true"`) cannot be enabled, and is only enabled on
older API levels:

    // Only the split APKs targeting devices below Android M should be compressed. Instant apps
    // always support uncompressed native libraries (even on Android L), because they are not always
    // executed by the Android platform.

A developer's `extractNativeLibs` setting in `AndroidManifest.xml` is
basically ignored. To make matters worse, on some devices the value
will be set and others not. This means we cannot rely on a known value
at build time.

See [bundletool's source code][nativelibs] for details.

[nativelibs]: https://github.com/google/bundletool/blob/5ac94cb61e949f135c50f6ce52bbb5f00e8e959f/src/main/java/com/android/tools/build/bundletool/splitters/NativeLibrariesCompressionSplitter.java#L75-L86

## Signing

App Bundles can only be signed with `jarsigner` (not `apksigner`). App
Bundles do not need to use `zipalign`. Xamarin.Android should go ahead
and sign the `.aab` file the same as it currently does for `.apk`
files. A `com.company.app-Signed.aab` file will be generated in
`$(OutputPath)`, to match our current behavior with APK files.

Google Play has recently added support for [doing the final,
production signing][app_signing], but Xamarin.Android should sign App
Bundles with what is configured in the existing MSBuild properties.

[app_signing]: https://developer.android.com/studio/publish/app-signing

## Deployment

### Create a device-specific APK Set

First, we will need to invoke `bundletool` to create an APK set:

```
bundletool build-apks --bundle=foo.aab --output=foo.apks
```

Running the [build-apks][build_apks] command, generates a `.apks` file.

The help text for `bundletool build-apks` reads:

```
Description:
    Generates an APK Set archive containing either all possible split APKs and
    standalone APKs or APKs optimized for the connected device (see connected-
    device flag).

Synopsis:
    bundletool build-apks
        --bundle=<bundle.aab>
        --output=<output.apks>
        [--aapt2=<path/to/aapt2>]
        [--adb=<path/to/adb>]
        [--connected-device]
        [--device-id=<device-serial-name>]
        [--device-spec=<device-spec.json>]
        [--key-pass=<key-password>]
        [--ks=<path/to/keystore>]
        [--ks-key-alias=<key-alias>]
        [--ks-pass=<[pass|file]:value>]
        [--max-threads=<num-threads>]
        [--mode=<default|universal|system|system_compressed>]
        [--optimize-for=<abi|screen_density|language>]
        [--overwrite]

Flags:
    --bundle: Path to the Android App Bundle to generate APKs from.

    --output: Path to where the APK Set archive should be created.

    --aapt2: (Optional) Path to the aapt2 binary to use.

    --adb: (Optional) Path to the adb utility. If absent, an attempt will be
        made to locate it if the ANDROID_HOME environment variable is set. Used
        only if connected-device flag is set.

    --connected-device: (Optional) If set, will generate APK Set optimized for
        the connected device. The generated APK Set will only be installable on
        that specific class of devices. This flag should be only be set with --
        mode=default flag.

    --device-id: (Optional) Device serial name. If absent, this uses the
        ANDROID_SERIAL environment variable. Either this flag or the environment
        variable is required when more than one device or emulator is connected.
        Used only if connected-device flag is set.

    --device-spec: (Optional) Path to the device spec file generated by the
        'get-device-spec' command. If present, it will generate an APK Set
        optimized for the specified device spec. This flag should be only be set
        with --mode=default flag.

    --key-pass: (Optional) Password of the key in the keystore to use to sign
        the generated APKs. If provided, must be prefixed with either 'pass:'
        (if the password is passed in clear text, e.g. 'pass:qwerty') or 'file:'
        (if the password is the first line of a file, e.g. 'file:
        /tmp/myPassword.txt'). If this flag is not set, the keystore password
        will be tried. If that fails, the password will be requested on the
        prompt.

    --ks: (Optional) Path to the keystore that should be used to sign the
        generated APKs. If not set, the default debug keystore will be used if
        it exists. If not found the APKs will not be signed. If set, the flag
        'ks-key-alias' must also be set.

    --ks-key-alias: (Optional) Alias of the key to use in the keystore to sign
        the generated APKs.

    --ks-pass: (Optional) Password of the keystore to use to sign the generated
        APKs. If provided, must be prefixed with either 'pass:' (if the password
        is passed in clear text, e.g. 'pass:qwerty') or 'file:' (if the password
        is the first line of a file, e.g. 'file:/tmp/myPassword.txt'). If this
        flag is not set, the password will be requested on the prompt.

    --max-threads: (Optional) Sets the maximum number of threads to use
        (default: 4).

    --mode: (Optional) Specifies which mode to run 'build-apks' command against.
        Acceptable values are 'default|universal|system|system_compressed'. If
        not set or set to 'default' we generate split, standalone and instant
        APKs. If set to 'universal' we generate universal APK. If set to
        'system' we generate APKs for system image. If set to
        'system_compressed' we generate compressed APK and an additional
        uncompressed stub APK (containing only Android manifest) for the system
        image.

    --optimize-for: (Optional) If set, will generate APKs with optimizations for
        the given dimensions. Acceptable values are
        'abi|screen_density|language'. This flag should be only be set with --
        mode=default flag.

    --overwrite: (Optional) If set, any previous existing output will be
        overwritten.
```

### Deploy an APK set

To deploy a `.apks` file to a connected device:

```
bundletool install-apks --apks=foo.apks
```

The [install-apks][install_apks] command will *finally* get the app
onto the device!

The help text for `bundletool install-apks` reads:

```
Description:
    Installs APKs extracted from an APK Set to a connected device. Replaces
    already installed package.

    This will extract from the APK Set archive and install only the APKs that
    would be served to that device. If the app is not compatible with the device
    or if the APK Set archive was generated for a different type of device, this
    command will fail.

Synopsis:
    bundletool install-apks
        --apks=<archive.apks>
        [--adb=<path/to/adb>]
        [--allow-downgrade]
        [--device-id=<device-serial-name>]
        [--modules=<base,module1,module2>]

Flags:
    --apks: Path to the archive file generated by the 'build-apks' command.

    --adb: (Optional) Path to the adb utility. If absent, an attempt will be
        made to locate it if the ANDROID_HOME environment variable is set.

    --allow-downgrade: (Optional) If set, allows APKs to be installed on the
        device even if the app is already installed with a lower version code.

    --device-id: (Optional) Device serial name. If absent, this uses the
        ANDROID_SERIAL environment variable. Either this flag or the environment
        variable is required when more than one device or emulator is connected.

    --modules: (Optional) List of modules to be installed, or "_ALL_" for all
        modules. Defaults to modules installed during first install, i.e. not
        on-demand. Note that the dependent modules will also be installed. The
        value of this flag is ignored if the device receives a standalone APK.
```

[build_apks]: https://developer.android.com/studio/command-line/bundletool#generate_apks
[install_apks]: https://developer.android.com/studio/command-line/bundletool#deploy_with_bundletool