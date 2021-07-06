#include <jni.h>
/* Header for class mono_android_DebugRuntime */

#ifndef _Included_mono_android_DebugRuntime
#define _Included_mono_android_DebugRuntime
#ifdef __cplusplus
extern "C" {
#endif
	/*
	 * Class:     mono_android_DebugRuntime
	 * Method:    init
	 * Signature: ([Ljava/lang/String;Ljava/lang/String;[Ljava/lang/String);[Ljava/lang/String);Ljava/lang/String;IZ)V
	 */
	JNIEXPORT void JNICALL Java_mono_android_DebugRuntime_init
	(JNIEnv *, jclass, jobjectArray, jstring, jobjectArray, jstring);
}
#endif // _Included_mono_android_DebugRuntime
