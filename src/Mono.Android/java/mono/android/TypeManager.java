package mono.android;

//#NOTE: make sure the `#FEATURE=*:START` and `#FEATURE=*:END` lines for different values of FEATURE don't overlap or interlace.
//#NOTE: Each FEATURE block should be self-contained, with no other FEATURE blocks nested.
//#NOTE: This is because the code that skips over those FEATURE blocks is VERY primitive (see the `CreateTypeManagerJava` task in XABT)
public class TypeManager {

	public static void Activate (String typeName, String sig, Object instance, Object[] parameterList)
	{
//#FEATURE=CALL_TRACING:START - do not remove or modify this line, it is required during application build
		android.util.Log.i ("monodroid-trace", String.format ("java: activating type: '%s' [%s]", typeName, sig));
//#FEATURE=CALL_TRACING:END - do not remove or modify this line, it is required during application build
		n_activate (typeName, sig, instance, parameterList);
	}

	private static native void n_activate (String typeName, String sig, Object instance, Object[] parameterList);

	static {
		String methods =
//#FEATURE=MARSHAL_METHODS:START - do not remove or modify this line, it is required during application build
			"n_activate:(Ljava/lang/String;Ljava/lang/String;Ljava/lang/Object;[Ljava/lang/Object;)V:GetActivateHandler\n" +
//#FEATURE=MARSHAL_METHODS:END - do not remove or modify this line, it is required during application build
			"";
		mono.android.Runtime.register ("Java.Interop.TypeManager+JavaTypeManager, Mono.Android, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", TypeManager.class, methods);
	}
}
