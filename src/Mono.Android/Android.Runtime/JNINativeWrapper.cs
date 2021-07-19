using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace Android.Runtime {
	public static class JNINativeWrapper {

		static MethodInfo? exception_handler_method;
		static MethodInfo? wait_for_bridge_processing_method;

		static void get_runtime_types ()
		{
			if (exception_handler_method != null)
				return;
			exception_handler_method = typeof (AndroidEnvironment).GetMethod (
				"UnhandledException", BindingFlags.NonPublic | BindingFlags.Static);
			if (exception_handler_method == null)
				AndroidEnvironment.FailFast ("Cannot find AndroidEnvironment.UnhandledException");

			wait_for_bridge_processing_method = typeof (JNIEnv).GetMethod ("WaitForBridgeProcessing", BindingFlags.Public | BindingFlags.Static);
			if (wait_for_bridge_processing_method == null)
				AndroidEnvironment.FailFast ("Cannot find JNIEnv.WaitForBridgeProcessing");
		}

		public static Delegate CreateDelegate (Delegate dlg)
		{
			if (dlg == null)
				throw new ArgumentNullException ();
			if (dlg.Target != null)
				throw new ArgumentException ();
			if (dlg.Method == null)
				throw new ArgumentException ();

			get_runtime_types ();

			var ret_type = dlg.Method.ReturnType;
			var parameters = dlg.Method.GetParameters ();
			var param_types = new Type [parameters.Length];
			for (int i = 0; i < parameters.Length; i++) {
				param_types [i] = parameters [i].ParameterType;
			}

			var dynamic = new DynamicMethod (DynamicMethodNameCounter.GetUniqueName (), ret_type, param_types, typeof (DynamicMethodNameCounter), true);
			var ig = dynamic.GetILGenerator ();

			LocalBuilder? retval = null;
			if (ret_type != typeof (void))
				retval = ig.DeclareLocal (ret_type);

			ig.Emit (OpCodes.Call, wait_for_bridge_processing_method!);

			bool  filter = Debugger.IsAttached || !JNIEnv.PropagateExceptions;
			if (!filter) {
				var label = ig.BeginExceptionBlock ();

				for (int i = 0; i < param_types.Length; i++)
					ig.Emit (OpCodes.Ldarg, i);
				ig.Emit (OpCodes.Call, dlg.Method);

				if (retval != null)
					ig.Emit (OpCodes.Stloc, retval);

				ig.Emit (OpCodes.Leave, label);

				ig.BeginCatchBlock (typeof (Exception));

				ig.Emit (OpCodes.Dup);
				ig.Emit (OpCodes.Call, exception_handler_method!);

				ig.EndExceptionBlock ();
			}
			else { //let the debugger handle the exception
				for (int i = 0; i < param_types.Length; i++)
					ig.Emit (OpCodes.Ldarg, i);
				ig.Emit (OpCodes.Call, dlg.Method);

				if (retval != null)
					ig.Emit (OpCodes.Stloc, retval);
			}

			if (retval != null)
				ig.Emit (OpCodes.Ldloc, retval);

			ig.Emit (OpCodes.Ret);

			return dynamic.CreateDelegate (dlg.GetType ());
		}

	}
}

