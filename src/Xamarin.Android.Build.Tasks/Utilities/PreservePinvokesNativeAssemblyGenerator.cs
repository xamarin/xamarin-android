using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Android.Build.Tasks;
using Microsoft.Build.Utilities;

using Xamarin.Android.Tasks.LLVMIR;
using Xamarin.Android.Tools;

using CecilMethodDefinition = global::Mono.Cecil.MethodDefinition;
using CecilParameterDefinition = global::Mono.Cecil.ParameterDefinition;

namespace Xamarin.Android.Tasks;

class PreservePinvokesNativeAssemblyGenerator : LlvmIrComposer
{
	readonly TaskLoggingHelper log;
	readonly AndroidTargetArch targetArch;
	readonly ICollection<PinvokeScanner.PinvokeEntryInfo> pinfos;

	public PreservePinvokesNativeAssemblyGenerator (TaskLoggingHelper log, AndroidTargetArch targetArch, ICollection<PinvokeScanner.PinvokeEntryInfo> pinfos)
		: base (log)
	{
		this.log = log;
		this.targetArch = targetArch;
		this.pinfos = pinfos;
	}

	protected override void Construct (LlvmIrModule module)
	{
	}
}
