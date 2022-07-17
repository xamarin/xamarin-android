# `dotnet trace` support for Android

## .NET 7

.NET 7 has new MSBuild targets to simplify `dotnet trace` usage.

First, you'll need to install two .NET global tools:

```
$ dotnet tool install -g dotnet-trace
$ dotnet tool install -g dotnet-dsrouter --add-source=https://aka.ms/dotnet-tools/index.json --prerelease
```

Then to profile a `Release` build on an arm64 Android device:

```
$ dotnet build -t:BuildAndStartTracing -c Release -r android-arm64
```

If you would like `.speedscope` output instead of `.nettrace`, you can
use `-p:TraceFormat=speedscope`. This corresponds to the `--format`
flag of `dotnet trace`.

After the app is launched, and you are ready to complete the trace, run:

```
$ dotnet build -t:FinishTracing
```

You should find the appropriate `.speedscope` or `.nettrace` files in
the project directory.

## .NET 6

The new MSBuild targets do not exist in .NET 6, but you can achieve
the same results manually.

### Set up reverse port forwarding:
```
$ adb reverse tcp:9000 tcp:9001
```
This will forward port 9000 on device to port 9001.

_Alternatively:_
```
$ adb reverse tcp:0 tcp:9001
43399
```
This will allocate a random port on remote and forward it to port 9001 on the host. The forwarded port is printed by adb

### Configure the device so that the profiled app suspends until tracing utility connects

```
$ adb shell setprop debug.mono.profile '127.0.0.1:9000,suspend'
```

### Install `dotnet-dsrouter`

Use a build from the feed `https://aka.ms/dotnet-tools/index.json`:

```
$ dotnet tool install -g dotnet-dsrouter --add-source=https://aka.ms/dotnet-tools/index.json --prerelease
You can invoke the tool using the following command: dotnet-dsrouter
Tool 'dotnet-dsrouter' (version '6.0.306901') was successfully installed.
```

### Start the tracing router/proxy on host
We assume ports as given above, in the first example.
```
$ dotnet-dsrouter client-server -tcps 127.0.0.1:9001 -ipcc /tmp/maui-app --verbose debug
WARNING: dotnet-dsrouter is an experimental development tool not intended for production environments.

info: dotnet-dsrounter[0]
      Starting IPC client (/tmp/maui-app) <--> TCP server (127.0.0.1:9001) router.
dbug: dotnet-dsrounter[0]
      Trying to create a new router instance.
dbug: dotnet-dsrounter[0]
      Waiting for a new tcp connection at endpoint "127.0.0.1:9001".
```

This starts a `dsrouter` TCP/IP server on host port `9000` and an IPC (Unix socket on *nix machines) client with the socket name/path `/tmp/maui-app`

### Start the tracing client

Before starting the client make sure that the socket file does **not** exist.

```
$ dotnet-trace collect --diagnostic-port /tmp/maui-app --format speedscope
No profile or providers specified, defaulting to trace profile 'cpu-sampling'

Provider Name                           Keywords            Level               Enabled By
Microsoft-DotNETCore-SampleProfiler     0x0000F00000000000  Informational(4)    --profile 
Microsoft-Windows-DotNETRuntime         0x00000014C14FCCBD  Informational(4)    --profile 

Waiting for connection on /tmp/maui-app
Start an application with the following environment variable: DOTNET_DiagnosticPorts=/tmp/maui-app
```

The `--format` argument is optional and it defaults to `nettrace`. However, `nettrace` files can be viewed only with
Perfview on Windows, while the speedscope JSON files can be viewed "on" Unix by uploading them to https://speedscope.app

_NOTE: on Windows, we found the `speedscope` format sometimes shows
`???` for method names. You can also open `.nettrace` files in
PerfView and export them to `speedscope` format._

### Compile and run the application

```
$ dotnet build -f net6.0-android -t:Run -c Release -p:AndroidEnableProfiler=true
```
_NOTE: `-f net6.0-android` is only needed for projects with multiple `$(TargetFrameworks)`._

Once the application is installed and started, `dotnet-trace` should show something similar to:

```
Process        : $HOME/.dotnet/tools/dotnet-dsrouter
Output File    : /tmp/hellomaui-app-trace
[00:00:00:35]	Recording trace 1.7997   (MB)
Press <Enter> or <Ctrl+C> to exit...812  (KB)
```

Once `<Enter>` is pressed, you should see:

```
Stopping the trace. This may take up to minutes depending on the application being traced.

Trace completed.
Writing:	hellomaui-app-trace.speedscope.json
```

And the output files should be found in the current directory. You can
use the `-o` switch if you would prefer to output them to a specific
directory.
