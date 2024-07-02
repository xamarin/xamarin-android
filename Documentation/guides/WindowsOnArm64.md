# Windows on Arm64

The Android workload in .NET 7 supports [Windows on Arm64][0], with a
few manual workarounds. We are working to address these in future .NET
7 previews.

## Install .NET 7

Pick a [`Windows arm64` build][1] that is at least .NET
7.0.100-preview.6. Note that these builds are not signed, so you will
have to bypass some Windows prompts.

After install you should see at least Preview 6:

```dotnetcli
> dotnet --version
7.0.100-preview.6.22277.6
```

## Disable `.msi`-based Installers

If you are installing to `C:\Program Files\dotnet`, delete the feature flag:

```cmd
C:\Program Files\dotnet\metadata\workloads\7.0.100\installertype\msi
```

## Update .NET Workload Manifests

In a terminal running as Administrator, run:

```dotnetcli
> dotnet workload update --source https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet7/nuget/v3/index.json
```

This downloads the latest files in `C:\Program Files\dotnet\sdk-manifests`.

## Manually Patch `WorkloadManifest.json`

The Mono workloads from dotnet/runtime need a small fix.

Open each `WorkloadManifest.json` under the folder tree `C:\Program Files\dotnet\sdk-manifests` in your favorite text editor and make the following changes:

Do a `Find/Replace` for:

```diff
--"win-x64", 
++"win-x64", "win-arm64", 
```

Next, anywhere you see a `win-x64` alias, add an additional row for `win-arm64`:

```diff
"alias-to": {
  "win-x64": "Microsoft.NETCore.App.Runtime.AOT.win-x64.Cross.android-x86",
++"win-arm64": "Microsoft.NETCore.App.Runtime.AOT.win-x64.Cross.android-x86",
```

## Install the `android` Workload

In a terminal running as Administrator, run:

```dotnetcli
> dotnet workload install android --skip-manifest-update --source https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet7/nuget/v3/index.json --source https://api.nuget.org/v3/index.json
```

`--skip-manifest-update` is very important, otherwise it will blow
away your manual changes to `WorkloadManifest.json`.

## Install Android Studio

The simplest way to get the Android SDK, is to install Android Studio.
Unfortunately, there is not yet a Windows on Arm64 version of Android
Studio.

Download the [Windows 64-bit](https://developer.android.com/studio/)
version of Android Studio for now, and install an Android SDK.

## Install Microsoft OpenJDK 11

[Download Microsoft OpenJDK 11][4] for `AArch64 / ARM64`.

## Setup Windows Subsystem for Android (WSA)

It is not currently possible to create Android emulators from Android
Studio *or* Visual Studio.

However, [Windows Subsystem for Android][2] works just fine! After
you've [setup your development environment][3], I would recommend a
few additional settings:

1. Open `Windows Subsystem for Android Settings`.
2. Toggle `Subsystem Resources` > `Continuous` on
3. Toggle `Developer Mode` on

Once this is setup, you should be able to connect `adb`:

```cmd
> adb connect 127.0.0.1:58526
* daemon not running; starting now at tcp:5037
* daemon started successfully
connected to 127.0.0.1:58526
```

At this point you should be able to view `adb logcat` output or do
other commands.

## Set the Path to the Android SDK

The .NET for Android workload doesn't know how to locate Android Studio's
Android SDK by default. This is because it is normally managed by
Visual Studio.

A couple options to fix this:

1. Set the `%AndroidSdkDirectory%` environment variable system-side to
   `%LocalAppData%\Android\Sdk\`.

2. Add to your `.csproj`:

```xml
<AndroidSdkDirectory>$(LocalAppData)\Android\Sdk\</AndroidSdkDirectory>
```

## Test

Start in a new folder:

```dotnetcli
> dotnet new android
> dotnet build -t:Run
```

[0]: https://www.microsoft.com/software-download/windowsinsiderpreviewarm64
[1]: https://github.com/dotnet/installer#table
[2]: https://docs.microsoft.com/windows/android/wsa/
[3]: https://docs.microsoft.com/windows/android/wsa/#set-up-your-development-environment
[4]: https://docs.microsoft.com/java/openjdk/download#openjdk-11
