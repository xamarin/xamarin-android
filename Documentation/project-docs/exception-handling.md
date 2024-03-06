# Exception Handling

Outside of Xamarin.Android, .NET exception handling
[when a debugger is attached][0] potentially involves walking the runtime stack
*twice*, involving three interactions between the runtime and the debugger:

 1. When the exception is first thrown, a *first chance notification* is raised
    in the debugger, which provides the debugger with an opportunity to handle
    breakpoint or single-step exceptions.

 2. If the debugger doesn't handle or continues execution from the first chance
    notification, then:
    
     a. The runtime will attempt to "find a frame-based exception handler that
        handles the exception".

     b. If no frame-based exception handler is found, then a
        *last-chance notification* is raised in the debugger.

 3. If the debugger doesn't handle the last chance notification, then execution
    will continue, causing the stack to be unwound.

The first stack walk is step 2(a), while the second stack walk is step (3).

Within Xamarin.Android, if a thread call-stack doesn't involve any calls to
or from Java code, the same semantics are present.

When a thread call-stack involves calls to or from Java code, the above
"two-pass" semantics cannot be supported, as the Java Native Interface, which
is used to support calls to or from Java code, does not support them.
A cross-VM runtime stack can only be walked *while being unwound*; there is
no way to ask "is there any method which will handle this exception" before
code is executed and the stack is unwound.


## The Setup

Consider the following Java code:

```java
// Java
public class Demo {
    public static void run(Runnable r) {
        /* setup code */
        try {
            r.run();
        } finally {
            /* cleanup code */
            System.out.println("Demo.run() finally block!");
        }
    }
}
```

`Demo.run()` is bound as:

```csharp
partial class Demo {
	public static unsafe void Run (global::Java.Lang.IRunnable p0)
	{
		const string __id = "run.(Ljava/lang/Runnable;)V";
		JniArgumentValue* __args = stackalloc JniArgumentValue [1];
		__args [0] = new JniArgumentValue ((p0 == null) ? IntPtr.Zero : ((global::Java.Lang.Object) p0).Handle);
		_members.StaticMethods.InvokeVoidMethod (__id, __args);
	}
}

```

Now imagine the above Java class has been bound and is used from a
Xamarin.Android app:

```csharp
Action a = () => {
    throw new Exception ("Hm…");
};
Demo.Run(new Java.Lang.Runnable(a));
```


## Exception Handling *Without* A Debugger

When a debugger is *not* attached, the following happens:

 1. `Java.Lang.Runnable` has a Java Callable Wrapper generated at app
    build time, `mono.java.lang.Runnable`, which implements the
    `java.lang.Runnable` Java interface type.

 2. When the `Java.Lang.Runnable` is created, a `mono.java.lang.Runnable`
    instance is also created, and the two instances are associated with each other.

 3. The `Demo.Run()` invocation invokes the Java `Demo.run()` method, passing
    along the `mono.java.lang.Runnable` instance created in (2).

 4. The `r.run()` invocation within `Demo.run()` eventually invokes the method
    `Java.Lang.IRunnableInvoker.n_Run()`:

    ```csharp
    static void n_Run (IntPtr jnienv, IntPtr native__this)
    {
        var __this = global::Java.Lang.Object.GetObject<Java.Lang.IRunnable> (jnienv, native__this, JniHandleOwnership.DoNotTransfer)!;
        __this.Run ();
    }
    ```

 5. *However*, invocation of `n_Run()` is wrapped in a [runtime-generated][1]
    `try`/`catch` block, which is equivalent to:

    ```csharp
    static void Call_n_Run(IntPtr jnienv, IntPtr native__this)
    {
        try {
            IRunnableInvoker.n_Run(jnienv, native__this);
        }
        catch (Exception e) {
            AndroidEnvironment.UnhandledException(e);
        }
    }
    ```

    This runtime-generated `try`/`catch` block is used for *every*
    Java-to-managed call boundary; the method invoked in the `try` block changes.

    [`AndroidEnvironment.UnhandledException()`][2] is responsible for calling
    the [`JNIEnv::Throw()`][3] JNI method.

 6. At this point in time, the runtime call-stack is:

      * Top-level managed method, which calls >
      * Java `Demo.run()` method, which calls >
      * Runtime-generated `try`/`catch` block, which calls >
      * C# `IRunnableInvoker.n_Run()` method, which calls >
      * C# `Action` delegate.

 7. The `Action` delegate is invoked, causing a C# exception to be thrown.

 8. The exception thrown in (7) is caught by the method in (5).  The managed
    exception type is wrapped into a `JavaProxyThrowable` instance, which is
    then raised in the Java code.

 9. The Java `finally` block executes, and then the `Demo.run()` method is
    unwound by the JVM.

10. The `Demo.Run()` binding sees the "pending exception" from the JNI call,
    "unwraps" the `JavaProxyThrowable` to obtain the original `System.Exception`
    then raises the `System.Exception`.

11. If the method calling `Demo.Run()` was invoked from Java code, e.g.
    an `Activity.OnCreate()` method override contained the sample code, then
    the runtime-generated `try`/`catch` block will catch the exception being
    propagated from (10) and likewise wrap it into a `JavaProxyThrowable`.

12. The Java stack frames will unwind, and the Java uncaught exception behavior
    kicks in: [`java.lang.Thread.getUncaughtExceptionHandler()`][4] and
    [`java.lang.Thread.UncaughtExceptionHandler.uncaughtException()`][5]
    are invoked.

13. During process startup, Xamarin.Android inserts itself into Java's
    uncaught exception handler chain.  As such,
    [`JNIEnv.PropagateUncaughtException()`][6] is invoked, which will attempt
    to invoke `Debugger.Mono_UnhandledException()`, which gives an attached
    debugger a chance to observe the exception, and
    `AppDomain.DoUnhandledException()` is invoked, which will raise the
    `AppDomain.UnhandledException` event, giving managed code a chance to
    deal with the pending unhandled exception.

14. The process exits, because the `System.Exception` isn't handled. :-)


## Exception Handling *With* A Debugger

When the debugger is attached, runtime behavior differs significantly.
Steps (1) through (4) are the same, then:

 5. Invocation of `n_Run()` is wrapped in a [runtime-generated][1]
    `try`/`catch` block which pulls in the debugger via an
    *exception filter*, and is equivalent to:

    ```csharp
    static void Call_n_Run(IntPtr jnienv, IntPtr native__this)
    {
        try {
            IRunnableInvoker.n_Run(jnienv, native__this);
        }
        catch (Exception e) when (Debugger.Mono_UnhandledException(e)) {
            AndroidEnvironment.UnhandledException(e);
        }
    }
    ```

 6. The runtime call-stack is unchanged relative to execution without a debugger.

 7. The `Action` delegate is invoked, causing a C# exception to be thrown.

 8. No *first chance notification* is raised.  Instead, Mono will
    "find a frame-based exception handler that handles the exception,"
    and as part of this process will execute any exception filters.  This
    causes `Debugger.Mono_UnhandledException()` to be executed, which is what
    triggers the "**System.Exception** has been thrown" message within the
    debugger.
    
    If you look at the *Call Stack* Debug pad within Visual Studio for Mac,
    `System.Diagnostics.Debugger.Mono_UnhandledException_internal()` and
    `System.Diagnostics.Debugger.Mono_UnhandledException()` are the topmost
    call stack entries.

 9. The Java `finally` block within `Demo.run()` *has not executed yet*.

    If `Demo.run()` had a `catch(Throwable)` block instead of a `finally`
    block, it likewise (1) would not have executed yet, and (2) will not
    participate in the stack walking to determine whether or not the exception
    is handled or unhandled in the first place.

10. The exception is not yet "pending" in Java either, so it is safe to invoke
    Java code in e.g. the Immediate window.

11. If execution is continued, e.g. via **Continue Debugging**, then
    `AndroidEnvironment.UnhandledException()` will be executed, causing the
    exception to be wrapped and become a "pending exception" within Java code.
    After this point, any invocation of Java code from the debugger will
    *immediately* cause the process to abort:

        JNI DETECTED ERROR IN APPLICATION: JNI … called with pending exception android.runtime.JavaProxyThrowable: System.Exception: Hm…

12. ***Furthermore***, *No* Java code can ever again execute within the process.
    Once execution is continued, *Mono* will be unwinding the call stack
    *without involvement of the Java VM*.

    The `finally` block within `Debug.run()` hasn't executed yet, and will
    *never* execute.  In particular, the `System.out.println()` message isn't
    visible in `adb logcat`!
    
    If `Debug.run()` instead had a `catch` block, it will similarly never be
    executed.

13. Execution then "breaks" at the managed `Debug.Run()` method.  At this point
    there is a pending exception within Java; any invocations of Java code from
    the debugger will *immediately* cause the process to abort.


14. If execution is continued again, the process will exit.

    Unexpectedly (2020-06-26), the exit is *also* due to a JNI error:

        JNI DETECTED ERROR IN APPLICATION: JNI NewString called with pending exception android.runtime.JavaProxyThrowable: System.Exception: Hm…



[0]: https://docs.microsoft.com/en-us/windows/win32/debug/debugger-exception-handling
[1]: https://github.com/xamarin/xamarin-android/blob/402ae221be90fdb4b48c2aeb29170b745c30f60b/src/Mono.Android/Android.Runtime/JNINativeWrapper.cs#L34-L97
[2]: https://github.com/xamarin/xamarin-android/blob/402ae221be90fdb4b48c2aeb29170b745c30f60b/src/Mono.Android/Android.Runtime/AndroidEnvironment.cs#L115-L129
[3]: https://docs.oracle.com/javase/8/docs/technotes/guides/jni/spec/functions.html#Throw
[4]: https://developer.android.com/reference/java/lang/Thread#getUncaughtExceptionHandler()
[5]: https://developer.android.com/reference/java/lang/Thread.UncaughtExceptionHandler#uncaughtException(java.lang.Thread,%20java.lang.Throwable)
[6]: https://github.com/xamarin/xamarin-android/blob/4cae5f5e40896c69b7448cb78cf613cf6327c97c/src/Mono.Android/Android.Runtime/JNIEnv.cs#L265-L296


---

Internal context:

  * https://bugzilla.xamarin.com/show_bug.cgi?id=7634
  * https://github.com/xamarin/monodroid/commit/b0f85970102d43bab9cd860a8e8884d136d766b3
  * https://github.com/xamarin/monodroid/commit/a9697ca2ac026b960b347a925fbe414efe3876f7
  * https://github.com/xamarin/monodroid/commit/12a012e00b4533d586ef31ced33351b63c9de883
