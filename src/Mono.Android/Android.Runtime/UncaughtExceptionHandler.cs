#nullable enable

using System;
using System.Reflection;

namespace Android.Runtime {

	sealed class UncaughtExceptionHandler : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler {

		Action<AppDomain, UnhandledExceptionEventArgs>?      AppDomain_DoUnhandledException;

		Java.Lang.Thread.IUncaughtExceptionHandler defaultHandler;

		public UncaughtExceptionHandler (Java.Lang.Thread.IUncaughtExceptionHandler defaultHandler)
		{
			this.defaultHandler = defaultHandler;
		}

		internal Java.Lang.Thread.IUncaughtExceptionHandler DefaultHandler {
			get { return defaultHandler; }
		}

		public void UncaughtException (Java.Lang.Thread thread, Java.Lang.Throwable ex)
		{
			try {
				Initialize ();
			} catch (Exception e) {
				Android.Runtime.AndroidEnvironment.FailFast ($"Unable to initialize UncaughtExceptionHandler. Nested exception caught: {e}");
			}

			if (AppDomain_DoUnhandledException != null) {
				try {
					var jltp = ex as JavaProxyThrowable;
					Exception? innerException = jltp?.InnerException;
					var args  = new UnhandledExceptionEventArgs (innerException ?? ex, isTerminating: true);
					AppDomain_DoUnhandledException (AppDomain.CurrentDomain, args);
				}
				catch (Exception e) {
					Logger.Log (LogLevel.Error, "monodroid", "Exception thrown while raising AppDomain.UnhandledException event: " + e.ToString ());
				}
			}
			if (defaultHandler != null)
				defaultHandler.UncaughtException (thread, ex);
		}

		void Initialize ()
		{

			if (AppDomain_DoUnhandledException == null) {
				var ad_due = typeof (AppDomain)
					.GetMethod ("DoUnhandledException",
					            bindingAttr:  BindingFlags.NonPublic | BindingFlags.Instance,
					            binder:       null,
					            types:        new []{typeof (UnhandledExceptionEventArgs)},
					            modifiers:    null);
				if (ad_due != null) {
					AppDomain_DoUnhandledException  = (Action<AppDomain, UnhandledExceptionEventArgs>) Delegate.CreateDelegate (
						typeof (Action<AppDomain, UnhandledExceptionEventArgs>), ad_due);
				}
			}
		}
	}
}
