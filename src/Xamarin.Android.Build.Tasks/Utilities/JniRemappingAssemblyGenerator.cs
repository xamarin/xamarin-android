using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.Build.Utilities;

using Xamarin.Android.Tasks.LLVMIR;

namespace Xamarin.Android.Tasks
{
	sealed class JniRemappingTypeReplacement
	{
		public string From { get; }
		public string To   { get; }

		public JniRemappingTypeReplacement (string from, string to)
		{
			From = from;
			To = to;
		}
	}

	sealed class JniRemappingMethodReplacement
	{
		public string SourceType { get; }
		public string SourceMethod { get; }
		public string SourceMethodSignature { get; }

		public string TargetType { get; }
		public string TargetMethod { get; }

		public bool TargetIsStatic { get; }

		public JniRemappingMethodReplacement (string sourceType, string sourceMethod, string sourceMethodSignature,
		                                      string targetType, string targetMethod, bool targetIsStatic)
		{
			SourceType = sourceType;
			SourceMethod = sourceMethod;
			SourceMethodSignature = sourceMethodSignature;

			TargetType = targetType;
			TargetMethod = targetMethod;
			TargetIsStatic = targetIsStatic;
		}
	}

	class JniRemappingAssemblyGenerator : LlvmIrComposer
	{
		const string TypeReplacementsVariableName = "jni_remapping_type_replacements";
		const string MethodReplacementIndexVariableName = "jni_remapping_method_replacement_index";

		sealed class JniRemappingTypeReplacementEntryContextDataProvider : NativeAssemblerStructContextDataProvider
		{
			public override string GetComment (object data, string fieldName)
			{
				var entry = EnsureType<JniRemappingTypeReplacementEntry>(data);

				if (String.Compare ("name", fieldName, StringComparison.Ordinal) == 0) {
					return $" name: {entry.name.str}";
				}

				if (String.Compare ("replacement", fieldName, StringComparison.Ordinal) == 0) {
					return $" replacement: {entry.replacement}";
				}

				return String.Empty;
			}
		}

		sealed class JniRemappingIndexTypeEntryContextDataProvider : NativeAssemblerStructContextDataProvider
		{
			public override string GetComment (object data, string fieldName)
			{
				var entry = EnsureType<JniRemappingIndexTypeEntry> (data);

				if (String.Compare ("name", fieldName, StringComparison.Ordinal) == 0) {
					return $" name: {entry.name.str}";
				}

				return String.Empty;
			}

			public override string GetPointedToSymbolName (object data, string fieldName)
			{
				var entry = EnsureType<JniRemappingIndexTypeEntry> (data);

				if (String.Compare ("methods", fieldName, StringComparison.Ordinal) == 0) {
					return entry.MethodsArraySymbolName;
				}

				return base.GetPointedToSymbolName (data, fieldName);
			}

			public override ulong GetBufferSize (object data, string fieldName)
			{
				var entry = EnsureType<JniRemappingIndexTypeEntry> (data);
				if (String.Compare ("methods", fieldName, StringComparison.Ordinal) == 0) {
					return (ulong)entry.TypeMethods.Count;
				}

				return 0;
			}
		}

		sealed class JniRemappingIndexMethodEntryContextDataProvider : NativeAssemblerStructContextDataProvider
		{
			public override string GetComment (object data, string fieldName)
			{
				var entry = EnsureType<JniRemappingIndexMethodEntry> (data);

				if (String.Compare ("name", fieldName, StringComparison.Ordinal) == 0) {
					return $" name: {entry.name.str}";
				}

				if (String.Compare ("replacement", fieldName, StringComparison.Ordinal) == 0) {
					return $" replacement: {entry.replacement.target_type}.{entry.replacement.target_name}";
				}

				if (String.Compare ("signature", fieldName, StringComparison.Ordinal) == 0) {
					if (entry.signature.length == 0) {
						return String.Empty;
					}

					return $"signature: {entry.signature.str}";
				}

				return String.Empty;
			}
		}

		sealed class JniRemappingString
		{
			public uint   length;
			public string str;
		};

		sealed class JniRemappingReplacementMethod
		{
			public string  target_type;
			public string  target_name;
			public bool    is_static;
		};

		[NativeAssemblerStructContextDataProvider (typeof(JniRemappingIndexMethodEntryContextDataProvider))]
		sealed class JniRemappingIndexMethodEntry
		{
			[NativeAssembler (UsesDataProvider = true)]
			public JniRemappingString            name;

			[NativeAssembler (UsesDataProvider = true)]
			public JniRemappingString            signature;

			[NativeAssembler (UsesDataProvider = true)]
			public JniRemappingReplacementMethod replacement;
		};

		[NativeAssemblerStructContextDataProvider (typeof(JniRemappingIndexTypeEntryContextDataProvider))]
		sealed class JniRemappingIndexTypeEntry
		{
			[NativeAssembler (UsesDataProvider = true)]
			public JniRemappingString           name;
			public uint                method_count;

			[NativeAssembler (UsesDataProvider = true), NativePointer (PointsToSymbol = "")]
			public JniRemappingIndexMethodEntry methods;

			[NativeAssembler (Ignore = true)]
			public string MethodsArraySymbolName;

			[NativeAssembler (Ignore = true)]
			public List<StructureInstance<JniRemappingIndexMethodEntry>> TypeMethods;
		};

		[NativeAssemblerStructContextDataProvider (typeof(JniRemappingTypeReplacementEntryContextDataProvider))]
		sealed class JniRemappingTypeReplacementEntry
		{
			[NativeAssembler (UsesDataProvider = true)]
			public JniRemappingString name;

			[NativeAssembler (UsesDataProvider = true)]
			public string    replacement;
		};

		List<JniRemappingTypeReplacement> typeReplacementsInput;
		List<JniRemappingMethodReplacement> methodReplacementsInput;

		StructureInfo jniRemappingStringStructureInfo;
		StructureInfo jniRemappingReplacementMethodStructureInfo;
		StructureInfo jniRemappingIndexMethodEntryStructureInfo;
		StructureInfo jniRemappingIndexTypeEntryStructureInfo;
		StructureInfo jniRemappingTypeReplacementEntryStructureInfo;

		public int ReplacementMethodIndexEntryCount { get; private set; } = 0;

		public JniRemappingAssemblyGenerator (TaskLoggingHelper log)
			: base (log)
		{}

		public JniRemappingAssemblyGenerator (TaskLoggingHelper log, List<JniRemappingTypeReplacement> typeReplacements, List<JniRemappingMethodReplacement> methodReplacements)
			: base (log)
		{
			this.typeReplacementsInput = typeReplacements ?? throw new ArgumentNullException (nameof (typeReplacements));
			this.methodReplacementsInput = methodReplacements ?? throw new ArgumentNullException (nameof (methodReplacements));
		}

		(List<StructureInstance<JniRemappingTypeReplacementEntry>>? typeReplacements, List<StructureInstance<JniRemappingIndexTypeEntry>>? methodIndexTypes) Init ()
		{
			if (typeReplacementsInput == null) {
				return (null, null);
			}

			var typeReplacements = new List<StructureInstance<JniRemappingTypeReplacementEntry>> ();
			Console.WriteLine ($"Type replacement input count: {typeReplacementsInput.Count}");

			foreach (JniRemappingTypeReplacement mtr in typeReplacementsInput) {
				var entry = new JniRemappingTypeReplacementEntry {
					name = MakeJniRemappingString (mtr.From),
					replacement = mtr.To,
				};

				typeReplacements.Add (new StructureInstance<JniRemappingTypeReplacementEntry> (jniRemappingTypeReplacementEntryStructureInfo, entry));
			}
			typeReplacements.Sort ((StructureInstance<JniRemappingTypeReplacementEntry> l, StructureInstance<JniRemappingTypeReplacementEntry> r) => l.Instance.name.str.CompareTo (r.Instance.name.str));

			var methodIndexTypes = new List<StructureInstance<JniRemappingIndexTypeEntry>> ();
			var types = new Dictionary<string, StructureInstance<JniRemappingIndexTypeEntry>> (StringComparer.Ordinal);

			foreach (JniRemappingMethodReplacement mmr in methodReplacementsInput) {
				if (!types.TryGetValue (mmr.SourceType, out StructureInstance<JniRemappingIndexTypeEntry> typeEntry)) {
					var entry = new JniRemappingIndexTypeEntry {
						name = MakeJniRemappingString (mmr.SourceType),
						MethodsArraySymbolName = MakeMethodsArrayName (mmr.SourceType),
						TypeMethods = new List<StructureInstance<JniRemappingIndexMethodEntry>> (),
					};

					typeEntry = new StructureInstance<JniRemappingIndexTypeEntry> (jniRemappingIndexTypeEntryStructureInfo, entry);
					methodIndexTypes.Add (typeEntry);
					types.Add (mmr.SourceType, typeEntry);
				}

				var method = new JniRemappingIndexMethodEntry {
					name = MakeJniRemappingString (mmr.SourceMethod),
					signature = MakeJniRemappingString (mmr.SourceMethodSignature),
					replacement = new JniRemappingReplacementMethod {
						target_type = mmr.TargetType,
						target_name = mmr.TargetMethod,
						is_static = mmr.TargetIsStatic,
					},
				};

				typeEntry.Instance.TypeMethods.Add (new StructureInstance<JniRemappingIndexMethodEntry> (jniRemappingIndexMethodEntryStructureInfo, method));
			}

			foreach (var kvp in types) {
				kvp.Value.Instance.method_count = (uint)kvp.Value.Instance.TypeMethods.Count;
				kvp.Value.Instance.TypeMethods.Sort ((StructureInstance<JniRemappingIndexMethodEntry> l, StructureInstance<JniRemappingIndexMethodEntry> r) => l.Instance.name.str.CompareTo (r.Instance.name.str));
			}

			methodIndexTypes.Sort ((StructureInstance<JniRemappingIndexTypeEntry> l, StructureInstance<JniRemappingIndexTypeEntry> r) => l.Instance.name.str.CompareTo (r.Instance.name.str));
			ReplacementMethodIndexEntryCount = methodIndexTypes.Count;

			return (typeReplacements, methodIndexTypes);

			string MakeMethodsArrayName (string typeName)
			{
				return $"mm_{typeName.Replace ('/', '_')}";
			}

			JniRemappingString MakeJniRemappingString (string str)
			{
				return new JniRemappingString {
					length = GetLength (str),
					str = str,
				};
			}

			uint GetLength (string str)
			{
				if (String.IsNullOrEmpty (str)) {
					return 0;
				}

				return (uint)Encoding.UTF8.GetBytes (str).Length;
			}
		}

		protected override void Construct (LlvmIrModule module)
		{
			MapStructures (module);
			List<StructureInstance<JniRemappingTypeReplacementEntry>>? typeReplacements;
			List<StructureInstance<JniRemappingIndexTypeEntry>>? methodIndexTypes;

			(typeReplacements, methodIndexTypes) = Init ();

			if (typeReplacements == null) {
				module.AddGlobalVariable (
					typeof(StructureInstance<JniRemappingTypeReplacementEntry>),
					TypeReplacementsVariableName,
					new StructureInstance<JniRemappingTypeReplacementEntry> (jniRemappingTypeReplacementEntryStructureInfo, new JniRemappingTypeReplacementEntry ()) { IsZeroInitialized = true },
					LlvmIrVariableOptions.GlobalConstant
				);

				module.AddGlobalVariable (
					typeof(StructureInstance<JniRemappingIndexTypeEntry>),
					MethodReplacementIndexVariableName,
					new StructureInstance<JniRemappingIndexTypeEntry> (jniRemappingIndexTypeEntryStructureInfo, new JniRemappingIndexTypeEntry ()) { IsZeroInitialized = true },
					LlvmIrVariableOptions.GlobalConstant
				);
				return;
			}

			module.AddGlobalVariable (TypeReplacementsVariableName, typeReplacements, LlvmIrVariableOptions.GlobalConstant);

			foreach (StructureInstance<JniRemappingIndexTypeEntry> entry in methodIndexTypes) {
				module.AddGlobalVariable (entry.Instance.MethodsArraySymbolName, entry.Instance.TypeMethods, LlvmIrVariableOptions.LocalConstant);
			}

			module.AddGlobalVariable (MethodReplacementIndexVariableName, methodIndexTypes, LlvmIrVariableOptions.GlobalConstant);
		}

		void MapStructures (LlvmIrModule module)
		{
			jniRemappingStringStructureInfo = module.MapStructure<JniRemappingString> ();
			jniRemappingReplacementMethodStructureInfo = module.MapStructure<JniRemappingReplacementMethod> ();
			jniRemappingIndexMethodEntryStructureInfo = module.MapStructure<JniRemappingIndexMethodEntry> ();
			jniRemappingIndexTypeEntryStructureInfo = module.MapStructure<JniRemappingIndexTypeEntry> ();
			jniRemappingTypeReplacementEntryStructureInfo = module.MapStructure<JniRemappingTypeReplacementEntry> ();
		}
	}
}
