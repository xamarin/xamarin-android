using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Java.Interop.Tools.JavaCallableWrappers;
using Microsoft.Android.Build.Tasks;
using Mono.Cecil;
using Mono.Linker;
using Mono.Linker.Steps;

#if ILLINK
using Microsoft.Android.Sdk.ILLink;
#endif  // ILLINK


namespace MonoDroid.Tuner
{
	public class CalculateJavaStubHashStep : BaseStep
	{
#if ILLINK
		protected override void Process ()
		{
			logger = (level, message) => Context.LogMessage ($"{level} {message}");
			cache = Context;
			scanner = new JavaTypeScanner (logger, Context);
		}
#else   // !ILLINK
		public CalculateJavaStubHashStep (Action<TraceLevel, string> logger, IMetadataResolver cache)
		{
			this.logger = logger;
			this.cache = cache;
			scanner = new JavaTypeScanner (logger, this.cache);
		}
#endif  // !ILLINK

		static readonly Encoding Encoding = Encoding.UTF8;

		Action<TraceLevel, string> logger;
		IMetadataResolver cache;
		JavaTypeScanner scanner;
		string outputDirectory;

		public void Calculate (AssemblyDefinition assembly, string outputDirectory)
		{
			this.outputDirectory = outputDirectory;
			ProcessAssembly (assembly);
		}

		protected override void ProcessAssembly (AssemblyDefinition assembly)
		{
			HashAlgorithm hasher = new Microsoft.Android.Build.Tasks.Crc64 ();
			foreach (var type in scanner.GetJavaTypes (assembly)) {
				Hash (hasher, type.FullName);

				foreach (var method in type.Methods) {
					Hash (hasher, method.Name);
				}
			}

			hasher.TransformFinalBlock (Array.Empty<byte> (), 0, 0);

			var assemblyFile = Path.GetFileName (assembly.MainModule.FileName);
			var hashFile = Path.Combine (outputDirectory, $"{assemblyFile}.hash");
			if (Files.CopyIfStringChanged (Convert.ToBase64String (hasher.Hash), hashFile)) {
				logger (TraceLevel.Verbose, $"Saved: {hashFile}");
			}
		}

		static void Hash (HashAlgorithm hasher, string value)
		{
			int length = Encoding.GetByteCount (value);
			var bytes = ArrayPool<byte>.Shared.Rent (length);
			try {
				Encoding.GetBytes (value, 0, value.Length, bytes, 0);
				hasher.TransformBlock (bytes, 0, length, null, 0);
			} finally {
				ArrayPool<byte>.Shared.Return (bytes);
			}
		}
	}
}
