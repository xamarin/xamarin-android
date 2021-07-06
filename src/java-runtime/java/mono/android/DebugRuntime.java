package mono.android;

public class DebugRuntime {
	private DebugRuntime ()
	{}

	public static native void init (String[] apks, String runtimeLibDir, String[] appDirs, String packageName);
}
