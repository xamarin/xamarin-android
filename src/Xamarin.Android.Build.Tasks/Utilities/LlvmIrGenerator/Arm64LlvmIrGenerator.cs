using System;
using System.Collections.Generic;
using System.IO;

namespace Xamarin.Android.Tasks
{
	class Arm64LlvmIrGenerator : LlvmIrGenerator
	{
		// See https://llvm.org/docs/LangRef.html#data-layout
		//
		//   Value as used by Android NDK's clang++
		//
		protected override string DataLayout => "e-m:e-i8:8:32-i16:16:32-i64:64-i128:128-n32:64-S128";
		protected override int PointerSize	 => 8;
		protected override string Triple	 => "aarch64-unknown-linux-android"; // NDK appends API level, we don't need that

		public Arm64LlvmIrGenerator (StreamWriter output, string fileName)
			: base (output, fileName)
		{}

		protected override void AddModuleFlagsMetadata (List<LlvmIrMetadataItem> flagsFields)
		{
			base.AddModuleFlagsMetadata (flagsFields);

			flagsFields.Add (MetadataManager.AddNumbered (LlvmIrModuleMergeBehavior.Error, "branch-target-enforcement", 0));
			flagsFields.Add (MetadataManager.AddNumbered (LlvmIrModuleMergeBehavior.Error, "sign-return-address", 0));
			flagsFields.Add (MetadataManager.AddNumbered (LlvmIrModuleMergeBehavior.Error, "sign-return-address-all", 0));
			flagsFields.Add (MetadataManager.AddNumbered (LlvmIrModuleMergeBehavior.Error, "sign-return-address-with-bkey", 0));
		}
	}
}
