# What is this?

`manifest-attribute-codegen` a tool that collects Android SDK
resource information about what XML elements can be placed in an AndroidManifest.xml, then generate a unified
element/attribute definition with "which API introduced this" information.

Using this information, we can generate `[(Application|Activity|etc)Attribute]` classes that can be used
by both `Mono.Android` and `Xamarin.Android.Build.Tasks`.

This generally only needs to be done each time we bind a new Android API level.

# How to use

Ensure all Android platform SDK levels are installed.  This can be done with `xaprepare`:

```
xaprepare android-sdk-platforms=all
```

From this directory, run:

```
dotnet build -t:GenerateManifestAttributes
```

If all found elements/attributes are accounted for in `metadata.xml`, new `*Attribute.cs` files
will be generated.

If everything isn't accounted for, you will be required to specify how to handle the new pieces.

# Handling new types (elements)

When a new type (element) like `<activity>` or `<uses-permission>` is found, there are 2 choices:
ignore it or bind it.

## Ignore the type

The majority of the found types are not bound so the most likely scenario may be to ignore the new type:

```xml
<type name="foo" ignore="true" />
```

## Bind the type

Alternatively, the type can be bound:

```xml
<type name="activity" namespace="Android.App" outputFile="src\Xamarin.Android.NamingCustomAttributes\Android.App\ActivityAttribute.cs" usage="AttributeTargets.Class" />
```

Required metadata:

- **name** - The name of the new element in AndroidManifest.xml, like `activity` or `application`
- **namespace** - The C# namespace the element will be placed in
- **outputFile** - The path to write the generated .cs
- **usage** - The `validOn` attribute usage information passed to `[AttributeUsage]`

Optional metadata:

- **allowMultiple** - The `allowMultiple` attribute usage information passed to `[AttributeUsage]`, defaults to `false`
- **jniNameProvider** - Whether the attribute should implement `Java.Interop.IJniNameProviderAttribute`, defaults to `false`
- **defaultConstructor** - Whether a parameterless constructor should be generated, defaults to `true`
- **sealed** - Whether the attribute type should be `sealed`, defaults to `true`
- **managedName** - The name of the managed attribute class if the default isn't correct
- **generateMapping** - Whether to generate the `mapping` field used by `Xamarin.Android.Build.Tasks, defaults to `true`

Note that if a new type is created, there will be additional manual code that needs to written to configure
`Xamarin.Android.Build.Tasks` to translate the attribute to AndroidManifest xml.

Example:

 - src/Xamarin.Android.Build.Tasks/Mono.Android/ApplicationAttribute.Partial.cs
 - src/Xamarin.Android.Build.Tasks/Utilities/ManifestDocument.cs

# Handling new attributes

When a new attribute like `<activity foo="bar" />` is added, it also must be specified if the attribute should be visible to users or not.

## Hide the attribute

To hide the attribute:

```xml
<element path="activity.foo" visible="false" />
```

## Surface the attribute

To surface the attribute:

```xml
<element path="activity.foo" visible="true" />
```

Optional metadata (note that if any metadata is set, `visible` is assumed to be `true` unless specified otherwise):

- **type** - C# type to override missing type information from the manifest definition
- **name** - The name of the managed attribute property if the default isn't correct
- **obsolete** - A string describing the reason for this member being `[Obsolete ("foo")]`
- **readonly** - Whether to generate the property with a `private set`, defaults to `false`
- **manualMap** - Whether to exclude the property from the `mapping` field used by `Xamarin.Android.Build.Tasks, defaults to `false`



