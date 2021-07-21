# Android Dynamic Feature Delivery

_NOTE: This document is very likely to change, as the the implementation
of Dynamic Features matures._

Android Dynamic Features are a way of splitting up your application
into smaller parts which users can download on-demand. This can be
useful in scenarios such as Games or apps with low usage parts.
An example would be a Support module which does not get used very
often, rather than including this code in the main app it could be
a dynamic feature which is downloaded when the user requests support.
Alternatively think of Game levels in a game. On first install only
the first few levels are installed to save download size. But later
the additional levels can be downloaded while the user is not playing
the game.

Dynamic Features can ONLY be used when targeting the Google Play
Store and using `PackageFormat` set to `aab`. Using `apk` will
NOT work.

## Xamarin Dynamic Feature Delivery

The Xamarin implementation of this will start by focusing on asset
delivery. This is where the `AndroidAsset` items can be split out
into a separate module for downloading later.

_NOTE: In the future we plan to support feature or code delivery
however there are significant challenges to supporting that in our
runtime. So this will be done after the initial rollout of asset
delivery._

The new Dynamic Feature Module will be based around a normal
Xamarin Android class library. The kind you get when calling

```
dotnet new androidlib
```

Although we plan to provide a template. The new Module will generally
be nothing different from a normal class library project. The
current restrictions are that it only contains `AndroidAsset` items.
No `AndroidResource` items can be included and any C# code in the
library will NOT end up in the application.

The one additional file is a special `AndroidManifest.xml` file which
will be generated as part of the module build process. This manifest
file needs a few special elements which need to be provided by
the following MSBuild properties.

* FeatureSplitName: (string) The name of the feature. You will use this in code to
    install the feature. Defaults to `ProjectName`.
* IsFeatureSplit: (bool) Defines if this feature is a "feature split". Defaults to `true`.
* FeatureDeliveryType: (Enum) The type of delivery mechanism to use. Valid values are
    OnDemand or InstallTime. Defaults to InstallTime.
* FeatureTitleResource: (string) This MUST be a valid @string which is present in your
    application strings.xml file. For example `@strings/assetsfeature`.
    Note the actual value of the resource can be 50 characters long and can be localized.
    It is this resource which is used to let the users know which feature is being
    downloaded. So it should be descriptive.
    This Item does NOT have default and will need to be provided.

These properties will have default values based on things like the Project name.

Here is a example of an Asset Feature Module for .net 6.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0-android</TargetFramework>
  </PropertyGroup>
  <PropertyGroup>
    <FeatureSplitName>assetsfeature</FeatureSplitName>
    <IsFeatureSplit>true</IsFeatureSplit>
    <FeatureDeliveryType>OnDemand</FeatureDeliveryType>
    <FeatureTitleResource>@string/assetsfeature</FeatureTitleResource>
  </PropertyGroup>
</Project>
```

This is defining an OnDemand feature.

In order to reference a feature you can use a normal `ProjectReference` MSBuild
Item. However you need to make some alterations in order for the system to work
correctly. The `ProjectReference` will need to use the following

```xml
<ItemGroup>
    <ProjectReference Include="Features\AssetsFeature\AssetsFeature.csproj">
        <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
        <AndroidDynamicFeature>true</AndroidDynamicFeature>
    </ProjectReference>
</ItemGroup>
```

We set `ReferenceOutputAssembly` to `false` to ensure that the assembly
for the feature is NOT included in the final aab file. If you do not do
this, the main application will include all the code and assets from this
feature. It will also pack those into a separate feature. The new meta
Data `AndroidDynamicFeature` allows the build system to pick up project
references which are "features".

As part of the build system all `Features` will include a `Reference` to
the main application `Assembly`. So there is no need to add any `PackageReference`
items to any NuGet which is already being used by main app.

_NOTE: The above is really important. Without these changes the Dynamic
Feature build system will NOT work._

Once you have done this you should be able to build your application as
normal.

## Downloading a Dynamic Feature at runtime

In order to download features you need to reference the
`Xamarin.Google.Android.Play.Core` Nuget Package from your main application.
You can do this by adding the following `PackageReference`s.

```xml
    <PackageReference Include="Xamarin.GooglePlayServices.Base" Version="117.6.0" />
    <PackageReference Include="Xamarin.Google.Android.Play.Core" Version="1.9.1" />
```

Note that `Xamarin.GooglePlayServices.Base` is also required. The `Version` numbers
here will change as new versions are being released all the time.

Unfortunately some of the classes we need to use in the `Play.Core` API use Java
Generics. The Xamarin Android Binding system has a problem with these types of
classes. So some of the API will either be missing or will produce build errors.

You will also need the following Nuget Package which will provide some additional
Helper classes to work with Dynamic Features.

```xml
<PackageReference Include="Xamarin.Google.Android.Play.Core.Helpers" Version="1.0.0" />
```

In order to download a feature we need to create an instance of a class implementing
`ISplitInstallManager`. There are two of these available. The first is for your
live application and can be created using the `SplitInstallManagerFactory`
The second is for local testing and should be created using the `FakeSplitInstallManagerFactory`.
You can use a `#if DEBUG` conditional compilation block to change which one you use.

```csharp
ISplitInstallManager manager;
#if DEBUG
var fakeManager = FakeSplitInstallManagerFactory.Create(this);
manager = fakeManager;
#else
manager = SplitInstallManagerFactory.Create (this);
#endif
```

Once you have an instance of the `ISplitInstallManager` the process of requesting
the install of a feature is quite straight forward. You create an instance of a
`SplitInstallRequest` via the `SplitInstallRequest.NewBuilder` method. Call
`builder.AddModule("foo")` to add all the modules you want to install. Finally call
`Build()` on that `request` and pass the result `manager.StartInstall` method.
Here is some sample code.

```csharp
var builder = SplitInstallRequest.NewBuilder ();
builder.AddModule ("assetsfeature");
manager.StartInstall (builder.Build ());
```

This will start the installation of the `assetsfeature`. Note that the string passed
in here is the same value as we defined for `FeatureSplitName` earlier in our
Feature.

Most of the Google samples use the `SplitInstallStateUpdatedListener` class to
track the progress of the installation. This is where the problems start.
`SplitInstallStateUpdatedListener` is one of those Java Generic classes which cause
a build error if you try to use it. So what we have done is created a wrapper class
in the `Xamarin.Google.Android.Play.Core.Helpers` Nuget Package which you can use
instead and still get the same functionality.

To use this listener first create an instance of the `SplitInstallStateUpdatedListenerWrapper`.
You can then add an `EventHander` for the `StatusUpdate` event to keep track of the
download and install progress.

```csharp
listener = new SplitInstallStateUpdatedListenerWrapper ();
listener.StateUpdate += (s, e) => {
    var state = e.State.;
    System.Console.WriteLine (state.Status());
};
```

To get the `manager` object to use this listener we need to call the `RegisterListener` method.
We also need to call the `UnregisterListener` as well when we are finish or if the app is paused.
Because we are using a wrapper class we cannot just pass our `listener` to the `RegisterListener`
method directly. This is because it is expecting a `SplitInstallStateUpdatedListener` type.
But we do provide a property on the `SplitInstallStateUpdatedListenerWrapper` which will give
you access to the underlying `SplitInstallStateUpdatedListener` class. So to register you can
call the following.

```csharp
manager.RegisterListener (listener.Listener);
```

To Unregsiter use this code.

```csharp
manager.UnregisterListener (listener.Listener);
```

With our listener registered we can now keep track of the download and install progress of our
feature. Here is a full sample `Activity` which shows at a basic level how to install a feature
based on a button click.


```csharp
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Widget;
using Java.Lang;
using Xamarin.Google.Android.Play.Core.SplitInstall;
using Xamarin.Google.Android.Play.Core.SplitInstall.Model;
using Xamarin.Google.Android.Play.Core.SplitInstall.Testing;
using Android.Content;

namespace DynamicAssetsExample
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class MainActivity : Activity
    {
        ISplitInstallManager manager;
        SplitInstallStateUpdatedListenerWrapper listener;
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);
            var button = FindViewById<Button>(Resource.Id.installButton);
            // create the ISplitInstallManager instances.
#if DEBUG
            var fakeManager = FakeSplitInstallManagerFactory.Create(this);
            manager = fakeManager;
#else
            manager = SplitInstallManagerFactory.Create (this);
#endif
            // Setup the Install State Update Listener.
            listener = new SplitInstallStateUpdatedListenerWrapper ();
            listener.StateUpdate += (s, e) => {
                var status = e.State.Status ();
                System.Console.WriteLine (status);
            };
            // Setup to install the feature on a button click.
            button.Click += (s, e) => {
                var builder = SplitInstallRequest.NewBuilder ();
                builder.AddModule ("assetsfeature");
                var installTask = manager.StartInstall (builder.Build ());
            };
        }

        protected override void OnResume ()
        {
            // Register our listener
            manager.RegisterListener (listener.Listener);
            base.OnResume ();
        }

        protected override void OnPause()
        {
            // Unregister our listener. We don't want notifications
            // when the app is not running.
            manager.UnregisterListener (listener.Listener);
            base.OnPause();
        }
    }
}
```

## Testing a Dynamic Feature Locally

In order to test Dynamic features on a local device you need to provide some additional
arguments to `bundletool` during the build. The `--local-testing` argument will inject
some additional metadata into the `aab` file which will cause your `features` to be
deployed to a holding area on the device.

Then when the `FakeSplitInstallManagerFactory` manager tries to install these features
it will get them from this holding area.

If you are developing using dynamic features this is the recommended arguments you need

```xml
<AndroidPackageFormat>aab</AndroidPackageFormat>
<EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>
<AndroidBundleToolExtraArgs Condition=" '$(Configuration)' == 'Debug' ">--local-testing</AndroidBundleToolExtraArgs>
```

This will force you to always use `aab` files and embed the .net assemblies into the
package. While this is not ideal it is currently the only way to debug dynamic features.
The `AndroidBundleToolExtraArgs` will allow you to test downloading features on demand.


