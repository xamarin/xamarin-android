using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using Xamarin.Android.Tools;
using Xamarin.Android.Tasks.LLVMIR;

namespace Xamarin.Android.Tasks;

using CecilMethodDefinition = global::Mono.Cecil.MethodDefinition;
using CecilParameterDefinition = global::Mono.Cecil.ParameterDefinition;

partial class MarshalMethodsNativeAssemblyGenerator
{
	sealed class LlvmLifetimePointerSizeArgumentPlaceholder : LlvmIrInstructionPointerSizeArgumentPlaceholder
	{
		public override object? GetValue (LlvmIrModuleTarget target)
		{
			// the llvm.lifetime functions need a 64-bit integer, target.NativePointerSize is 32-bit
			return (ulong)(uint)base.GetValue (target);
		}
	}

	enum TracingRenderArgumentFunction
	{
		None,
		GetClassName,
		GetObjectClassname,
		GetCString,
		GetBooleanString,
	}

	sealed class TracingState
	{
		public List<object?>? trace_enter_leave_args                   = null;
		public LlvmIrLocalVariable? asprintfAllocatedStringVar         = null;
	}

	readonly MarshalMethodsTracingMode tracingMode;

	LlvmIrFunction? asprintf;
	LlvmIrFunction? free;

	LlvmIrFunction? llvm_lifetime_end;
	LlvmIrFunction? llvm_lifetime_start;

	LlvmIrFunction? mm_trace_func_enter;
	LlvmIrFunction? mm_trace_func_leave;
	LlvmIrFunction? mm_trace_get_boolean_string;
	LlvmIrFunction? mm_trace_get_c_string;
	LlvmIrFunction? mm_trace_get_class_name;
	LlvmIrFunction? mm_trace_get_object_class_name;
	LlvmIrFunction? mm_trace_init;

	LlvmIrFunctionAttributeSet? freeCallAttributes;
	LlvmIrFunctionAttributeSet? asprintfCallAttributes;

	void InitTracing (LlvmIrModule module)
	{
		var externalFunctionsAttributeSet = module.AddAttributeSet (MakeTraceFunctionsAttributeSet (module));

		InitLlvmFunctions (module);
		InitLibcFunctions (module, externalFunctionsAttributeSet);
		InitTracingFunctions (module, externalFunctionsAttributeSet);
	}

	void WriteTracingAtFunctionTop (LlvmIrModule module, MarshalMethodInfo method, LlvmIrFunctionBody body, LlvmIrFunction function, MarshalMethodsWriteState writeState)
	{
		if (tracingMode == MarshalMethodsTracingMode.None) {
			return;
		}

		module.RegisterString (method.NativeSymbolName);

		body.AddComment (" Tracing code start");

		var tracingState = new TracingState {
			asprintfAllocatedStringVar = function.CreateLocalVariable (typeof(string), "render_buf"),
		};
		body.Alloca (tracingState.asprintfAllocatedStringVar);

		LlvmIrFunctionAttributeSet lifetimeAttrSet = MakeLlvmLifetimeAttributeSet (module);
		LlvmIrInstructions.Call call = body.Call (llvm_lifetime_start, arguments: new List<object?> { new LlvmLifetimePointerSizeArgumentPlaceholder (), tracingState.asprintfAllocatedStringVar });
		call.AttributeSet = lifetimeAttrSet;

		body.Store (tracingState.asprintfAllocatedStringVar, module.TbaaAnyPointer);

		AddAsprintfCall (module, function, method, tracingState);

		CecilMethodDefinition nativeCallback = method.Method.NativeCallback;
		var assemblyCacheIndexPlaceholder = new MarshalMethodAssemblyIndexValuePlaceholder (method, writeState.AssemblyCacheState);
		tracingState.trace_enter_leave_args = new List<object?> {
			function.Signature.Parameters[0], // JNIEnv *env
			(int)tracingMode,
			assemblyCacheIndexPlaceholder,
			method.ClassCacheIndex,
			nativeCallback.MetadataToken.ToUInt32 (),
			method.NativeSymbolName,
			tracingState.asprintfAllocatedStringVar,
		};
		body.Call (mm_trace_func_enter, arguments: tracingState.trace_enter_leave_args);
		AddFreeCall (function, tracingState.asprintfAllocatedStringVar);

		body.AddComment (" Tracing code end");
	}

	void AddAsprintfCall (LlvmIrModule module, LlvmIrFunction function, MarshalMethodInfo method, TracingState tracingState)
	{
		Mono.Collections.Generic.Collection<CecilParameterDefinition>? managedMethodParameters = method.Method.RegisteredMethod?.Parameters ?? method.Method.ImplementedMethod?.Parameters;

		int expectedRegisteredParamCount;
		int managedParametersOffset;
		if (managedMethodParameters == null) {
			// There was no registered nor implemented methods, so we're looking at a native callback - it should have the same number of parameters as our wrapper
			managedMethodParameters = method.Method.NativeCallback.Parameters;
			expectedRegisteredParamCount = method.Parameters.Count;
			managedParametersOffset = 0;
		} else {
			// Managed methods get two less parameters (no `env` and `klass`)
			expectedRegisteredParamCount = method.Parameters.Count - 2;
			managedParametersOffset = 2;
		}

		if (managedMethodParameters.Count != expectedRegisteredParamCount) {
			throw new InvalidOperationException ($"Internal error: unexpected number of registered method '{method.Method.NativeCallback.FullName}' parameters. Should be {expectedRegisteredParamCount}, but is {managedMethodParameters.Count}");
		}

		if (method.Parameters.Count != function.Signature.Parameters.Count) {
			throw new InvalidOperationException ($"Internal error: number of native method parameter ({method.Parameters.Count}) doesn't match the number of marshal method parameters ({function.Signature.Parameters.Count})");
		}

		var varargs = new List<object?> ();
		var variablesToFree = new List<LlvmIrVariable> ();
		var formatSb = new StringBuilder ('(');
		bool first = true;

		for (int i = 0; i < method.Parameters.Count; i++) {
			LlvmIrFunctionParameter parameter = method.Parameters[i];

			if (!first) {
				formatSb.Append (", ");
			} else {
				first = false;
			}

			// Native method will have two more parameters than its managed counterpart - one for JNIEnv* and another for jclass.  They always are the first two
			// parameters, so we start looking at the managed parameters only once the first two are out of the way
			CecilParameterDefinition? managedParameter = i >= managedParametersOffset ? managedMethodParameters[i - managedParametersOffset] : null;
			Type actualType = VerifyAndGetActualParameterType (parameter, managedParameter);
			PrepareAsprintfArgument (formatSb, function, parameter, actualType, varargs, variablesToFree);
		}

		formatSb.Append (')');

		string format = formatSb.ToString ();
		var asprintfArgs = new List<object?> {
			tracingState.asprintfAllocatedStringVar,
			format,
		};
		asprintfArgs.AddRange (varargs);

		DoAddAsprintfCall (asprintfArgs, module, function, format, tracingState);

		foreach (LlvmIrVariable vtf in variablesToFree) {
			AddFreeCall (function, vtf);
		}

		Type VerifyAndGetActualParameterType (LlvmIrFunctionParameter nativeParameter, CecilParameterDefinition? managedParameter)
		{
			if (managedParameter == null) {
				return nativeParameter.Type;
			}

			if (nativeParameter.Type == typeof(byte) && String.Compare ("System.Boolean", managedParameter.Name, StringComparison.Ordinal) == 0) {
				// `bool`, as a non-blittable type, is mapped to `byte` by the marshal method rewriter
				return typeof(bool);
			}

			return nativeParameter.Type;
		}
	}

	void PrepareAsprintfArgument (StringBuilder format, LlvmIrFunction function, LlvmIrVariable parameter, Type actualType, List<object?> varargs, List<LlvmIrVariable> variablesToFree)
	{
		LlvmIrVariable? result = null;
		if (actualType == typeof(_jclass)) {
			format.Append ("%s @%p");
			result = AddTransformFunctionCall (function, TracingRenderArgumentFunction.GetClassName, parameter);
			varargs.Add (result);
			variablesToFree.Add (result);
			varargs.Add (parameter);
			return;
		}

		if (actualType == typeof(_jobject)) {
			format.Append ("%s @%p");
			result = AddTransformFunctionCall (function, TracingRenderArgumentFunction.GetObjectClassname, parameter);
			varargs.Add (result);
			variablesToFree.Add (result);
			varargs.Add (parameter);
			return;
		}

		if (actualType == typeof(_jstring)) {
			format.Append ("\"%s\"");
			result = AddTransformFunctionCall (function, TracingRenderArgumentFunction.GetCString, parameter);
			varargs.Add (result);
			variablesToFree.Add (result);
			return;
		}

		if (actualType == typeof(bool)) {
			format.Append ("%s");

			// No need to free(3) it, returns pointer to a constant
			result = AddTransformFunctionCall (function, TracingRenderArgumentFunction.GetBooleanString, parameter);
			varargs.Add (result);
			return;
		}

		if (actualType == typeof(byte) || actualType == typeof(ushort)) {
			format.Append ("%u");
			AddUpcast<uint> ();
			return;
		}

		if (actualType == typeof(sbyte) || actualType == typeof(short)) {
			format.Append ("%d");
			AddUpcast<int> ();
			return;
		}

		if (actualType == typeof(char)) {
			format.Append ("'\\%x'");
			AddUpcast<uint> ();
			return;
		}

		if (actualType == typeof(float)) {
			format.Append ("%g");
			AddUpcast<double> ();
			return;
		}

		if (actualType == typeof(string)) {
			format.Append ("\"%s\"");
		} else if (actualType == typeof(IntPtr) || typeof(_jobject).IsAssignableFrom (actualType) || actualType == typeof(_JNIEnv)) {
			format.Append ("%p");
		} else if (actualType == typeof(int)) {
			format.Append ("%d");
		} else if (actualType == typeof(uint)) {
			format.Append ("%u");
		} else if (actualType == typeof(long)) {
			format.Append ("%ld");
		} else if (actualType == typeof(ulong)) {
			format.Append ("%lu");
		} else if (actualType == typeof(double)) {
			format.Append ("%g");
		} else {
			throw new InvalidOperationException ($"Unsupported type '{actualType}'");
		}

		varargs.Add (parameter);

		void AddUpcast<T> ()
		{
			LlvmIrVariable ret = function.CreateLocalVariable (typeof(T));
			function.Body.Ext (parameter, typeof(T), ret);
			varargs.Add (ret);
		}
	}

	LlvmIrVariable? AddTransformFunctionCall (LlvmIrFunction function, TracingRenderArgumentFunction renderFunction, LlvmIrVariable paramVar)
	{
		if (renderFunction == TracingRenderArgumentFunction.None) {
			return null;
		}

		var transformerArgs = new List<object?> ();
		LlvmIrFunction transformerFunc;

		switch (renderFunction) {
			case TracingRenderArgumentFunction.GetClassName:
				transformerFunc = mm_trace_get_class_name;
				AddJNIEnvArgument ();
				break;

			case TracingRenderArgumentFunction.GetObjectClassname:
				transformerFunc = mm_trace_get_object_class_name;
				AddJNIEnvArgument ();
				break;

			case TracingRenderArgumentFunction.GetCString:
				transformerFunc = mm_trace_get_c_string;
				AddJNIEnvArgument ();
				break;

			case TracingRenderArgumentFunction.GetBooleanString:
				transformerFunc = mm_trace_get_boolean_string;
				break;

			default:
				throw new InvalidOperationException ($"Internal error: unsupported transformer function {renderFunction}");
		};
		transformerArgs.Add (paramVar);

		if (!transformerFunc.ReturnsValue) {
			return null;
		}
		LlvmIrLocalVariable? result = function.CreateLocalVariable (transformerFunc.Signature.ReturnType);
		function.Body.Call (transformerFunc, result, transformerArgs);

		return result;

		void AddJNIEnvArgument ()
		{
			transformerArgs.Add (function.Signature.Parameters[0]);
		}
	}

	void DoAddAsprintfCall (List<object?> args, LlvmIrModule module, LlvmIrFunction function, string format, TracingState tracingState)
	{
		module.RegisterString (format);

		LlvmIrFunctionBody body = function.Body;
		LlvmIrLocalVariable asprintf_ret = function.CreateLocalVariable (typeof(int), "asprintf_ret");
		LlvmIrInstructions.Call call = body.Call (asprintf, asprintf_ret, args);
		call.AttributeSet = asprintfCallAttributes;
		call.Comment = $" Format: {format}";

		// Check whether asprintf returned a negative value (it returns -1 at failure, but we widen the check just in case)
		LlvmIrLocalVariable asprintf_failed = function.CreateLocalVariable (typeof(bool), "asprintf_failed");
		body.Icmp (LlvmIrIcmpCond.SignedLessThan, asprintf_ret, (int)0, asprintf_failed);

		var asprintfIfThenLabel = new LlvmIrFunctionLabelItem ();
		var asprintfIfDoneLabel = new LlvmIrFunctionLabelItem ();

		body.Br (asprintf_failed, asprintfIfThenLabel, asprintfIfDoneLabel);

		// Condition is true if asprintf **failed**
		body.Add (asprintfIfThenLabel);
		body.Store (tracingState.asprintfAllocatedStringVar);
		body.Br (asprintfIfDoneLabel);

		body.Add (asprintfIfDoneLabel);
	}

	void AddFreeCall (LlvmIrFunction function, object? toFree)
	{
		LlvmIrInstructions.Call call = function.Body.Call (free, arguments: new List<object?> { toFree });
		call.AttributeSet = freeCallAttributes;
	}

	void InitLibcFunctions (LlvmIrModule module, LlvmIrFunctionAttributeSet? attrSet)
	{
		var asprintf_params = new List<LlvmIrFunctionParameter> {
			new (typeof(string), "strp") {
				NoUndef = true,
			},
			new (typeof(string), "fmt") {
				NoUndef = true,
			},
			new (typeof(void)) {
				IsVarArgs = true,
			},
		};

		var asprintf_sig = new LlvmIrFunctionSignature (
			name: "asprintf",
			returnType: typeof(int),
			parameters: asprintf_params
		);

		asprintf = module.DeclareExternalFunction (new LlvmIrFunction (asprintf_sig, attrSet));

		var free_params = new List<LlvmIrFunctionParameter> {
			new (typeof(IntPtr), "ptr") {
				AllocPtr = true,
				NoCapture = true,
				NoUndef = true,
			},
		};

		var free_sig = new LlvmIrFunctionSignature (
			name: "free",
			returnType: typeof(void),
			parameters: free_params
		);

		free = module.DeclareExternalFunction (new LlvmIrFunction (free_sig, MakeFreeFunctionAttributeSet (module)));
		freeCallAttributes = MakeFreeCallAttributeSet (module);
		asprintfCallAttributes = MakeAsprintfCallAttributeSet (module);
	}

	void InitLlvmFunctions (LlvmIrModule module)
	{
		var llvmFunctionsAttributeSet = module.AddAttributeSet (MakeLlvmIntrinsicFunctionsAttributeSet (module));
		var llvm_lifetime_params = new List<LlvmIrFunctionParameter> {
			new (typeof(ulong), "size"),
			new (typeof(IntPtr), "pointer"),
		};

		var lifetime_sig = new LlvmIrFunctionSignature (
			name: "llvm.lifetime.start",
			returnType: typeof(void),
			parameters: llvm_lifetime_params
		);

		llvm_lifetime_start = module.DeclareExternalFunction (
			new LlvmIrFunction (lifetime_sig, llvmFunctionsAttributeSet) {
				AddressSignificance = LlvmIrAddressSignificance.Default
			}
		);

		llvm_lifetime_end = module.DeclareExternalFunction (
			new LlvmIrFunction ("llvm.lifetime.end", lifetime_sig, llvmFunctionsAttributeSet) {
				AddressSignificance = LlvmIrAddressSignificance.Default
			}
		);
	}

	void InitTracingFunctions (LlvmIrModule module, LlvmIrFunctionAttributeSet? attrSet)
	{
		// Function names and declarations must match those in src/monodroid/jni/marshal-methods-tracing.hh
		var mm_trace_init_params = new List<LlvmIrFunctionParameter> {
			new (typeof(IntPtr), "env"),
		};

		var mm_trace_init_sig = new LlvmIrFunctionSignature (
			name: "_mm_trace_init",
			returnType: typeof(void),
			parameters: mm_trace_init_params
		);

		mm_trace_init = module.DeclareExternalFunction (new LlvmIrFunction (mm_trace_init_sig, attrSet));

		var mm_trace_func_enter_or_leave_params = new List<LlvmIrFunctionParameter> {
			new (typeof(IntPtr), "env"), // JNIEnv *env
			new (typeof(int), "tracing_mode"),
			new (typeof(uint), "mono_image_index"),
			new (typeof(uint), "class_index"),
			new (typeof(uint), "method_token"),
			new (typeof(string), "native_method_name"),
			new (typeof(string), "method_extra_info"),
		};

		var mm_trace_func_enter_leave_sig = new LlvmIrFunctionSignature (
			name: "_mm_trace_func_enter",
			returnType: typeof(void),
			parameters: mm_trace_func_enter_or_leave_params
		);

		mm_trace_func_enter = module.DeclareExternalFunction (new LlvmIrFunction (mm_trace_func_enter_leave_sig, attrSet));
		mm_trace_func_leave = module.DeclareExternalFunction (new LlvmIrFunction ("_mm_trace_func_leave", mm_trace_func_enter_leave_sig, attrSet));

		var mm_trace_get_class_name_params = new List<LlvmIrFunctionParameter> {
			new (typeof(IntPtr), "env") {
				NoUndef = true,
			},
			new (typeof(_jclass), "v") {
				NoUndef = true,
			},
		};

		var mm_trace_get_class_name_sig = new LlvmIrFunctionSignature (
			name: "_mm_trace_get_class_name",
			returnType: typeof(string),
			parameters: mm_trace_get_class_name_params
		);

		mm_trace_get_class_name = module.DeclareExternalFunction (new LlvmIrFunction (mm_trace_get_class_name_sig, attrSet));

		var mm_trace_get_object_class_name_params = new List<LlvmIrFunctionParameter> {
			new (typeof(IntPtr), "env") {
				NoUndef = true,
			},
			new (typeof(_jobject), "v") {
				NoUndef = true,
			},
		};

		var mm_trace_get_object_class_name_sig = new LlvmIrFunctionSignature (
			name: "_mm_trace_get_object_class_name",
			returnType: typeof(string),
			parameters: mm_trace_get_object_class_name_params
		);

		mm_trace_get_object_class_name = module.DeclareExternalFunction (new LlvmIrFunction (mm_trace_get_object_class_name_sig, attrSet));

		var mm_trace_get_c_string_params = new List<LlvmIrFunctionParameter> {
			new (typeof(IntPtr), "env") {
				NoUndef = true,
			},
			new (typeof(_jstring), "v") {
				NoUndef = true,
			},
		};

		var mm_trace_get_c_string_sig = new LlvmIrFunctionSignature (
			name: "_mm_trace_get_c_string",
			returnType: typeof(string),
			parameters: mm_trace_get_c_string_params
		);

		mm_trace_get_c_string = module.DeclareExternalFunction (new LlvmIrFunction (mm_trace_get_c_string_sig, attrSet));

		var mm_trace_get_boolean_string_params = new List<LlvmIrFunctionParameter> {
			new (typeof(bool), "v") {
				NoUndef = true,
			},
		};

		var mm_trace_get_boolean_string_sig = new LlvmIrFunctionSignature (
			name: "_mm_trace_get_boolean_string",
			returnType: typeof(string),
			parameters: mm_trace_get_boolean_string_params
		);
		mm_trace_get_boolean_string_sig.ReturnAttributes.NonNull = true;

		mm_trace_get_boolean_string = module.DeclareExternalFunction (new LlvmIrFunction (mm_trace_get_boolean_string_sig, attrSet));
	}

	LlvmIrFunctionAttributeSet MakeLlvmIntrinsicFunctionsAttributeSet (LlvmIrModule module)
	{
		return new LlvmIrFunctionAttributeSet {
			new ArgmemonlyFunctionAttribute (),
			new MustprogressFunctionAttribute (),
			new NocallbackFunctionAttribute (),
			new NofreeFunctionAttribute (),
			new NosyncFunctionAttribute (),
			new NounwindFunctionAttribute (),
			new WillreturnFunctionAttribute (),
		};
	}

	LlvmIrFunctionAttributeSet MakeTraceFunctionsAttributeSet (LlvmIrModule module)
	{
		var ret = new LlvmIrFunctionAttributeSet {
			new NounwindFunctionAttribute (),
			new NoTrappingMathFunctionAttribute (true),
			new StackProtectorBufferSizeFunctionAttribute (8),
		};

		ret.Add (AndroidTargetArch.Arm64, new FramePointerFunctionAttribute ("non-leaf"));
		ret.Add (AndroidTargetArch.Arm, new FramePointerFunctionAttribute ("all"));
		ret.Add (AndroidTargetArch.X86, new FramePointerFunctionAttribute ("none"));
		ret.Add (AndroidTargetArch.X86_64, new FramePointerFunctionAttribute ("none"));

		return ret;
	}

	LlvmIrFunctionAttributeSet MakeFreeCallAttributeSet (LlvmIrModule module)
	{
		var ret = new LlvmIrFunctionAttributeSet {
			new NounwindFunctionAttribute (),
		};
		ret.DoNotAddTargetSpecificAttributes = true;

		return module.AddAttributeSet (ret);
	}

	LlvmIrFunctionAttributeSet MakeAsprintfCallAttributeSet (LlvmIrModule module)
	{
		var ret = new LlvmIrFunctionAttributeSet {
			new NounwindFunctionAttribute (),
		};
		ret.DoNotAddTargetSpecificAttributes = true;

		return module.AddAttributeSet (ret);
	}

	LlvmIrFunctionAttributeSet MakeFreeFunctionAttributeSet (LlvmIrModule module)
	{
		var ret = new LlvmIrFunctionAttributeSet {
			new MustprogressFunctionAttribute (),
			new NounwindFunctionAttribute (),
			new WillreturnFunctionAttribute (),
			new AllockindFunctionAttribute ("free"),
			// TODO: LLVM 16+ feature, enable when we switch to this version
			// new MemoryFunctionAttribute {
			// 	Default = MemoryAttributeAccessKind.Write,
			// 	Argmem = MemoryAttributeAccessKind.None,
			// 	InaccessibleMem = MemoryAttributeAccessKind.None,
			// },
			new AllocFamilyFunctionAttribute ("malloc"),
			new NoTrappingMathFunctionAttribute (true),
			new StackProtectorBufferSizeFunctionAttribute (8),
		};

		return module.AddAttributeSet (ret);
	}

	LlvmIrFunctionAttributeSet MakeLlvmLifetimeAttributeSet (LlvmIrModule module)
	{
		var ret = new LlvmIrFunctionAttributeSet {
			new NounwindFunctionAttribute (),
		};

		ret.DoNotAddTargetSpecificAttributes = true;
		return module.AddAttributeSet (ret);
	}
}
