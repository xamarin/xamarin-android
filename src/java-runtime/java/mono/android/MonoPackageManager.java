package mono;

import java.io.*;
import java.lang.String;
import java.util.Locale;
import java.util.HashSet;
import java.util.zip.*;
import java.util.Arrays;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.res.AssetManager;
import android.os.Build;
import android.util.Log;
import mono.android.Runtime;
import mono.android.DebugRuntime;
import mono.android.BuildConfig;

public class MonoPackageManager {

	static Object lock = new Object ();
	static boolean initialized;

	static android.content.Context Context;

	public static void LoadApplication (Context context, ApplicationInfo runtimePackage, String[] apks)
	{
		synchronized (lock) {
			if (context instanceof android.app.Application) {
				Context = context;
			}
			if (!initialized) {
				android.content.IntentFilter timezoneChangedFilter  = new android.content.IntentFilter (
						android.content.Intent.ACTION_TIMEZONE_CHANGED
				);
				context.registerReceiver (new mono.android.app.NotifyTimeZoneChanges (), timezoneChangedFilter);

				Locale locale       = Locale.getDefault ();
				String language     = locale.getLanguage () + "-" + locale.getCountry ();
				String filesDir     = context.getFilesDir ().getAbsolutePath ();
				String cacheDir     = context.getCacheDir ().getAbsolutePath ();
				String dataDir      = getNativeLibraryPath (context);
				ClassLoader loader  = context.getClassLoader ();
				String runtimeDir = getNativeLibraryPath (runtimePackage);
				String[] appDirs = new String[] {filesDir, cacheDir, dataDir};
				String packageName  = context.getPackageName();

				//
				// Preload DSOs libmonodroid.so depends on so that the dynamic
				// linker can resolve them when loading monodroid. This is not
				// needed in the latest Android versions but is required in at least
				// API 16 and since there's no inherent negative effect of doing it,
				// we can do it unconditionally.
				//
				// Additionally, we need to load all the DSOs which depend on libmonosgen-2.0. The reason is that on
				// some 64-bit devices running Android 5.{0,1} the dynamic linker fails to find `libmonosgen-2.0.so`
				// even though it is in the same directory as the DSO depending on it *and* libmonosgen-2.0 is already
				// in memory. This was seen on some devices (Huawei P8) and the x86_64 Android emulator. See the
				// following issues:
				//
				//   https://github.com/xamarin/xamarin-android/issues/4772
				//   https://github.com/xamarin/xamarin-android/issues/4852
				//
				// We could limit the preloading to only 64-bit 5.x Android versions but it appears to be more effort
				// than necessary as preloading won't hurt performance (much - some libraries might not be needed
				// immediately during startup) and there might be other Android builds out there with similar problems.
				//
				// We need to use our own `BuildConfig` class to detect debug builds here because,
				// it seems, ApplicationInfo.flags information is not reliable - in the debug builds
				// (with `android:debuggable=true` present on the `<application>` element in the
				// manifest) using shared runtime, the `runtimePackage.flags` field does NOT have
				// the FLAGS_DEBUGGABLE (0x00000002) set and thus we'd revert to the `else` clause
				// below, leading to an error locating the Mono runtime
				//
				if (BuildConfig.Debug) {
					System.loadLibrary ("xamarin-debug-app-helper");
					DebugRuntime.init (apks, runtimeDir, appDirs, packageName);
				} else {
					System.loadLibrary("monosgen-2.0");
				}
				System.loadLibrary("xamarin-app");

				if (!BuildConfig.DotNetRuntime) {
					// .net5+ APKs don't contain `libmono-native.so`
					System.loadLibrary("mono-native");
				} else {
					// for .net6 we temporarily need to load the SSL DSO
					// see: https://github.com/dotnet/runtime/issues/51274#issuecomment-832963657
					System.loadLibrary("System.Security.Cryptography.Native.Android");
				}

				System.loadLibrary("monodroid");

				Runtime.initInternal (
						language,
						apks,
						runtimeDir,
						appDirs,
						loader,
						MonoPackageManager_Resources.Assemblies,
						Build.VERSION.SDK_INT,
						isEmulator ()
					);

				mono.android.app.ApplicationRegistration.registerApplications ();

				initialized = true;
			}
		}
	}

	// We need to detect the emulator in order to determine the maximum gref count.
	// The official Android emulator requires a much lower maximum than actual
	// devices. Hopefully other emulators don't need the treatment. If they do, we
	// can add their detection here. We should perform the absolute minimum of
	// checking in order to save time.
	static boolean isEmulator()
	{
		String val = Build.HARDWARE;

		// This detects the official Android emulator
		if (val.contains ("ranchu") || val.contains ("goldfish"))
			return true;

		return false;
	}

	public static void setContext (Context context)
	{
		// Ignore; vestigial
	}

	static String getNativeLibraryPath (Context context)
	{
	    return getNativeLibraryPath (context.getApplicationInfo ());
	}

	static String getNativeLibraryPath (ApplicationInfo ainfo)
	{
		if (android.os.Build.VERSION.SDK_INT >= 9)
			return ainfo.nativeLibraryDir;
		return ainfo.dataDir + "/lib";
	}

	public static String[] getAssemblies ()
	{
		return MonoPackageManager_Resources.Assemblies;
	}

	public static String[] getDependencies ()
	{
		return MonoPackageManager_Resources.Dependencies;
	}
}
