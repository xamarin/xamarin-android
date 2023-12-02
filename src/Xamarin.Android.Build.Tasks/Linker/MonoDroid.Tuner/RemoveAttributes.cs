using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Mono.Linker;
using Mono.Linker.Steps;

using Mono.Tuner;
using Mobile.Tuner;

using Mono.Cecil;

namespace MonoDroid.Tuner {

	public class RemoveAttributes : RemoveAttributesBase {

		bool changed;

		protected virtual bool DebugBuild {
			get {
#if ILLINK
				return true;
#else
				return context.LinkSymbols;
#endif
			}
		}

		public override bool IsActiveFor (AssemblyDefinition assembly)
		{
			return true;
		}

		void SetAssemblyAction (AssemblyDefinition assembly)
		{
			switch (Annotations.GetAction (assembly)) {
				case AssemblyAction.Copy:
				case AssemblyAction.CopyUsed:
					Annotations.SetAction (assembly, AssemblyAction.Save);
					break;
			}
		}

		public override void ProcessAssembly (AssemblyDefinition assembly)
		{
			changed = false;

			base.ProcessAssembly (assembly);

			if (changed)
				SetAssemblyAction (assembly);
		}

		public override void ProcessType (TypeDefinition type)
		{
			changed = false;

			base.ProcessType (type);

			if (changed)
				SetAssemblyAction (type.Module.Assembly);
		}

		protected override void WillRemoveAttribute (ICustomAttributeProvider provider, CustomAttribute attribute)
		{
			base.WillRemoveAttribute (provider, attribute);

			changed = true;
		}

		protected override bool IsRemovedAttribute (CustomAttribute attribute)
		{
			// note: this also avoid calling FullName (which allocates a string)
			var attr_type = attribute.Constructor.DeclaringType;
			switch (attr_type.Name) {
			case "IntDefinitionAttribute":
				return attr_type.Namespace == "Android.Runtime";
			case "ObsoleteAttribute":
			// System.Mono*Attribute from mono/mcs/build/common/MonoTODOAttribute.cs
			case "MonoDocumentationNoteAttribute":
			case "MonoExtensionAttribute":
			case "MonoInternalNoteAttribute":
			case "MonoLimitationAttribute":
			case "MonoNotSupportedAttribute":
			case "MonoTODOAttribute":
				return attr_type.Namespace == "System";
			case "MonoFIXAttribute":
				return attr_type.Namespace == "System.Xml";
			// remove debugging-related attributes if we're not linking symbols (i.e. we're building release builds)
			case "DebuggableAttribute":
			case "DebuggerBrowsableAttribute":
			case "DebuggerDisplayAttribute":
			case "DebuggerHiddenAttribute":
			case "DebuggerNonUserCodeAttribute":
			case "DebuggerStepperBoundaryAttribute":
			case "DebuggerStepThroughAttribute":
			case "DebuggerTypeProxyAttribute":
			case "DebuggerVisualizerAttribute":
				return !DebugBuild && attr_type.Namespace == "System.Diagnostics";
			case "NullableContextAttribute":
			case "NullableAttribute":
				return attr_type.Namespace == "System.Runtime.CompilerServices";
			case "AllowNullAttribute":
			case "MaybeNullAttribute":
			case "NotNullAttribute":
			case "NotNullWhenAttribute":
			case "NotNullIfNotNullAttribute":
				return attr_type.Namespace == "System.Diagnostics.CodeAnalysis";
			default:
				return false;
			}
		}
	}
}
