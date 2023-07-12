using System;
using System.IO;
using System.Linq;
using System.Text;
using Java.Interop.Tools.Cecil;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Linker;
using Mono.Tuner;
using MonoDroid.Tuner;
using NUnit.Framework;
using Xamarin.ProjectTools;
using SR = System.Reflection;

namespace Xamarin.Android.Build.Tests
{
	public class LinkerTests : BaseTest
	{
		[Test]
		public void FixAbstractMethodsStep_SkipDimMembers ()
		{
			var path = Path.Combine (Root, "temp", TestName);
			var step = new FixAbstractMethodsStep (new TypeDefinitionCache ());
			var pipeline = new Pipeline ();

			Directory.CreateDirectory (path);

			using (var context = new LinkContext (pipeline)) {

				context.Resolver.AddSearchDirectory (path);

				var myAssemblyPath = Path.Combine (path, "MyAssembly.dll");

				using (var android = CreateFauxMonoAndroidAssembly ()) {
					android.Write (Path.Combine (path, "Mono.Android.dll"));
					CreateAbstractIfaceImplementation (myAssemblyPath, android);
				}

				using (var assm = context.Resolve (myAssemblyPath)) {
					step.Process (context);

					var impl = assm.MainModule.GetType ("MyNamespace.MyClass");

					Assert.IsTrue (impl.Methods.Any (m => m.Name == "MyAbstractMethod"), "We should have generated an override for MyAbstractMethod");
					Assert.IsFalse (impl.Methods.Any (m => m.Name == "MyDefaultMethod"), "We should not have generated an override for MyDefaultMethod");
				}
			}

			Directory.Delete (path, true);
		}

		static void CreateAbstractIfaceImplementation (string assemblyPath, AssemblyDefinition android)
		{
			using (var assm = AssemblyDefinition.CreateAssembly (new AssemblyNameDefinition ("DimTest", new Version ()), "DimTest", ModuleKind.Dll)) {
				var void_type = assm.MainModule.ImportReference (typeof (void));

				// Create interface
				var iface = new TypeDefinition ("MyNamespace", "IMyInterface", TypeAttributes.Interface);

				var abstract_method = new MethodDefinition ("MyAbstractMethod", MethodAttributes.Abstract, void_type);
				var default_method = new MethodDefinition ("MyDefaultMethod", MethodAttributes.Public, void_type);

				iface.Methods.Add (abstract_method);
				iface.Methods.Add (default_method);

				assm.MainModule.Types.Add (iface);

				// Create implementing class
				var jlo = assm.MainModule.Import (android.MainModule.GetType ("Java.Lang.Object"));
				var impl = new TypeDefinition ("MyNamespace", "MyClass", TypeAttributes.Public, jlo);
				impl.Interfaces.Add (new InterfaceImplementation (iface));

				assm.MainModule.Types.Add (impl);
				assm.Write (assemblyPath);
			}
		}

		[Test]
		public void FixAbstractMethodsStep_Explicit ()
		{
			var path = Path.Combine (Root, "temp", TestName);
			var step = new FixAbstractMethodsStep (new TypeDefinitionCache ());
			var pipeline = new Pipeline ();

			Directory.CreateDirectory (path);

			using (var context = new LinkContext (pipeline)) {

				context.Resolver.AddSearchDirectory (path);

				var myAssemblyPath = Path.Combine (path, "MyAssembly.dll");

				using (var android = CreateFauxMonoAndroidAssembly ()) {
					android.Write (Path.Combine (path, "Mono.Android.dll"));
					CreateExplicitInterface (myAssemblyPath, android);
				}

				using (var assm = context.Resolve (myAssemblyPath)) {
					step.Process (context);

					var impl = assm.MainModule.GetType ("MyNamespace.MyClass");
					Assert.AreEqual (2, impl.Methods.Count, "MyClass should contain 2 methods");
					var method = impl.Methods.FirstOrDefault (m => m.Name == "MyNamespace.IMyInterface.MyMethod");
					Assert.IsNotNull (method, "MyNamespace.IMyInterface.MyMethod should exist");
					method = impl.Methods.FirstOrDefault (m => m.Name == "MyMissingMethod");
					Assert.IsNotNull (method, "MyMissingMethod should exist");
				}
			}

			Directory.Delete (path, true);
		}

		static void CreateExplicitInterface (string assemblyPath, AssemblyDefinition android)
		{
			using (var assm = AssemblyDefinition.CreateAssembly (new AssemblyNameDefinition ("NestedIFaceTest", new Version ()), "NestedIFaceTest", ModuleKind.Dll)) {
				var void_type = assm.MainModule.ImportReference (typeof (void));

				// Create interface
				var iface = new TypeDefinition ("MyNamespace", "IMyInterface", TypeAttributes.Interface);

				var iface_method = new MethodDefinition ("MyMethod", MethodAttributes.Abstract, void_type);
				iface.Methods.Add (iface_method);
				iface.Methods.Add (new MethodDefinition ("MyMissingMethod", MethodAttributes.Abstract, void_type));

				assm.MainModule.Types.Add (iface);

				// Create implementing class
				var jlo = assm.MainModule.Import (android.MainModule.GetType ("Java.Lang.Object"));
				var impl = new TypeDefinition ("MyNamespace", "MyClass", TypeAttributes.Public, jlo);
				impl.Interfaces.Add (new InterfaceImplementation (iface));

				var explicit_method = new MethodDefinition ("MyNamespace.IMyInterface.MyMethod", MethodAttributes.Abstract, void_type);
				explicit_method.Overrides.Add (new MethodReference (iface_method.Name, void_type, iface));
				impl.Methods.Add (explicit_method);

				assm.MainModule.Types.Add (impl);
				assm.Write (assemblyPath);
			}
		}

		static AssemblyDefinition CreateFauxMonoAndroidAssembly ()
		{
			var assm = AssemblyDefinition.CreateAssembly (new AssemblyNameDefinition ("Mono.Android", new Version ()), "DimTest", ModuleKind.Dll);
			var void_type = assm.MainModule.ImportReference (typeof (void));

			// Create fake JLO type
			var jlo = new TypeDefinition ("Java.Lang", "Object", TypeAttributes.Public);
			assm.MainModule.Types.Add (jlo);

			// Create fake Java.Lang.AbstractMethodError type
			var ame = new TypeDefinition ("Java.Lang", "AbstractMethodError", TypeAttributes.Public);
			ame.Methods.Add (new MethodDefinition (".ctor", MethodAttributes.Public, void_type));
			assm.MainModule.Types.Add (ame);

			return assm;
		}

		private void PreserveCustomHttpClientHandler (string handlerType, string handlerAssembly, string testProjectName, string assemblyPath)
		{
			var proj = new XamarinAndroidApplicationProject () { IsRelease = true };
			proj.AddReferences ("System.Net.Http");
			string handlerTypeFullName = string.IsNullOrEmpty(handlerAssembly) ? handlerType : handlerType + ", " + handlerAssembly;
			proj.SetProperty (proj.ActiveConfigurationProperties, "AndroidHttpClientHandlerType", handlerTypeFullName);
			proj.MainActivity = proj.DefaultMainActivity.Replace ("base.OnCreate (bundle);", "base.OnCreate (bundle);\nvar client = new System.Net.Http.HttpClient ();");
			using (var b = CreateApkBuilder (testProjectName)) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");

				using (var assembly = AssemblyDefinition.ReadAssembly (Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath, assemblyPath))) {
					Assert.IsTrue (assembly.MainModule.GetType (handlerType) != null, $"'{handlerTypeFullName}' should have been preserved by the linker.");
				}
			}
		}

		[Test]
		public void PreserveCustomHttpClientHandlers ()
		{
			if (Builder.UseDotNet) {
				PreserveCustomHttpClientHandler ("Xamarin.Android.Net.AndroidMessageHandler", "",
					"temp/PreserveAndroidMessageHandler", "android-arm64/linked/Mono.Android.dll");
				PreserveCustomHttpClientHandler ("System.Net.Http.SocketsHttpHandler", "System.Net.Http",
					"temp/PreserveSocketsHttpHandler", "android-arm64/linked/System.Net.Http.dll");
			} else {
				PreserveCustomHttpClientHandler ("Xamarin.Android.Net.AndroidClientHandler", "",
					"temp/PreserveAndroidHttpClientHandler", "android/assets/Mono.Android.dll");
			}
		}

		[Test]
		public void WarnAboutAppDomains ([Values (true, false)] bool isRelease)
		{
			if (isRelease) {
				// NOTE: trimmer warnings are hidden by default in .NET 7 rc1
				Assert.Ignore("https://github.com/dotnet/linker/issues/2982");
			}

			var path = Path.Combine (Root, "temp", TestName);
			var lib = new XamarinAndroidLibraryProject {
				IsRelease = isRelease,
				ProjectName = "Library",
				Sources = {
					new BuildItem.Source ("Foo.cs") {
						TextContent = () => "class Foo { System.AppDomain Bar => System.AppDomain.CreateDomain (\"myDomain\"); }",
					}
				}
			};

			var app = new XamarinAndroidApplicationProject { IsRelease = isRelease };
			app.SetAndroidSupportedAbis ("arm64-v8a");
			app.AddReference (lib);
			using var libBuilder = CreateDllBuilder (Path.Combine (path, lib.ProjectName));
			Assert.IsTrue (libBuilder.Build (lib), "library build should have succeeded.");
			// AppDomain.CreateDomain() is [Obsolete]
			Assert.IsTrue (StringAssertEx.ContainsText (libBuilder.LastBuildOutput, "1 Warning(s)"), "MSBuild should count 1 warnings.");

			using var appBuilder = CreateApkBuilder (Path.Combine (path, app.ProjectName));
			Assert.IsTrue (appBuilder.Build (app), "app build should have succeeded.");

			// NOTE: in .NET 6, we only emit IL6200 for Release builds
			if (!Builder.UseDotNet || isRelease) {
				string code = Builder.UseDotNet ? "IL6200" : "XA2000";
				Assert.IsTrue (StringAssertEx.ContainsText (appBuilder.LastBuildOutput, "1 Warning(s)"), "MSBuild should count 1 warnings.");
				Assert.IsTrue (StringAssertEx.ContainsText (appBuilder.LastBuildOutput, $"warning {code}: Use of AppDomain.CreateDomain()"), $"Should warn {code} about creating AppDomain.");
			}
		}

		[Test]
		public void RemoveDesigner ([Values (true, false)] bool useAssemblyStore)
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true,
			};
			proj.SetProperty ("AndroidEnableAssemblyCompression", "False");
			proj.SetProperty ("AndroidLinkResources", "True");
			proj.SetProperty ("AndroidUseAssemblyStore", useAssemblyStore.ToString ());
			string assemblyName = proj.ProjectName;
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "build should have succeeded.");
				var apk = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				FileAssert.Exists (apk);
				var helper = new ArchiveAssemblyHelper (apk, useAssemblyStore);
				Assert.IsTrue (helper.Exists ($"assemblies/{assemblyName}.dll"), $"{assemblyName}.dll should exist in apk!");
				using (var stream = helper.ReadEntry ($"assemblies/{assemblyName}.dll")) {
					stream.Position = 0;
					using (var assembly = AssemblyDefinition.ReadAssembly (stream)) {
						var type = assembly.MainModule.GetType ($"{assemblyName}.Resource");
						var intType = typeof(int);
						foreach (var nestedType in type.NestedTypes) {
							int count = 0;
							foreach (var field in nestedType.Fields) {
								if (field.FieldType.FullName == intType.FullName)
									count++;
							}
							Assert.AreEqual (0, count, "All Nested Resource Type int fields should be removed.");
						}
					}
				}
			}
		}

		[Test]
		public void LinkDescription ([Values (true, false)] bool useAssemblyStore)
		{
			string assembly_name = Builder.UseDotNet ? "System.Console" : "mscorlib";
			string linker_xml = "<linker/>";

			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true,
				OtherBuildItems = {
					new BuildItem ("LinkDescription", "linker.xml") {
						TextContent = () => linker_xml
					}
				}
			};
			// So we can use Mono.Cecil to open assemblies directly
			proj.SetProperty ("AndroidEnableAssemblyCompression", "False");
			proj.SetProperty ("AndroidUseAssemblyStore", useAssemblyStore.ToString ());

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "first build should have succeeded.");

				linker_xml =
$@"<linker>
	<assembly fullname=""{assembly_name}"">
		<type fullname=""System.Console"">
			<method name=""Beep"" />
		</type>
	</assembly>
</linker>";
				proj.Touch ("linker.xml");

				Assert.IsTrue (b.Build (proj, doNotCleanupOnUpdate: true), "second build should have succeeded.");

				var apk = Path.Combine (Root, b.ProjectDirectory, proj.OutputPath, $"{proj.PackageName}-Signed.apk");
				FileAssert.Exists (apk);
				var helper = new ArchiveAssemblyHelper (apk, useAssemblyStore);
				Assert.IsTrue (helper.Exists ($"assemblies/{assembly_name}.dll"), $"{assembly_name}.dll should exist in apk!");
				using (var stream = helper.ReadEntry ($"assemblies/{assembly_name}.dll")) {
					stream.Position = 0;
					using (var assembly = AssemblyDefinition.ReadAssembly (stream)) {
						var type = assembly.MainModule.GetType ("System.Console");
						var method = type.Methods.FirstOrDefault (p => p.Name == "Beep");
						Assert.IsNotNull (method, "System.Console.Beep should exist!");
					}
				}
			}
		}

		[Test]
		public void LinkWithNullAttribute ()
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = true,
				OtherBuildItems = {
					new BuildItem ("Compile", "NullAttribute.cs") { TextContent = () => @"
using System;
using Android.Content;
using Android.Widget;
namespace UnnamedProject {
	public class MyAttribute : Attribute
	{
		Type[] types;

		public Type[] Types {
			get { return types; }
		}

		public MyAttribute (Type[] ta)
		{
			types = ta;
		}
	}

	[MyAttribute (null)]
	public class AttributedButtonStub : Button
	{
		public AttributedButtonStub (Context context) : base (context)
		{
		}
	}
}"
					},
				}
			};

			proj.MainActivity = proj.DefaultMainActivity.Replace ("//${AFTER_ONCREATE}",
$@"			var myButton = new AttributedButtonStub (this);
			myButton.Text = ""Bug #35710"";
");

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Building a project with a null attribute value should have succeded.");
			}
		}

		static readonly object [] AndroidAddKeepAlivesSource = new object [] {
			// Debug configuration
			new object [] {
				/* isRelease */                  false,
				/* AndroidAddKeepAlives=true */  false,
				/* AndroidLinkMode=None */       false,
				/* should add KeepAlives */      false,
			},
			// Debug configuration, AndroidAddKeepAlives=true
			new object [] {
				/* isRelease */                  false,
				/* AndroidAddKeepAlives=true */  true,
				/* AndroidLinkMode=None */       false,
				/* should add KeepAlives */      true,
			},
			// Release configuration
			new object [] {
				/* isRelease */                  true,
				/* AndroidAddKeepAlives=true */  false,
				/* AndroidLinkMode=None */       false,
				/* should add KeepAlives */      true,
			},
			// Release configuration, AndroidLinkMode=None
			new object [] {
				/* isRelease */                  true,
				/* AndroidAddKeepAlives=true */  false,
				/* AndroidLinkMode=None */       true,
				/* should add KeepAlives */      true,
			},
		};

		[Test]
		[TestCaseSource (nameof (AndroidAddKeepAlivesSource))]
		public void AndroidAddKeepAlives (bool isRelease, bool setAndroidAddKeepAlivesTrue, bool setLinkModeNone, bool shouldAddKeepAlives)
		{
			var proj = new XamarinAndroidApplicationProject {
				IsRelease = isRelease,
				OtherBuildItems = {
					new BuildItem ("Compile", "Method.cs") { TextContent = () => @"
using System;
using Java.Interop;

namespace UnnamedProject {
	public class MyClass : Java.Lang.Object
	{
		[Android.Runtime.Register(""MyMethod"")]
		public unsafe bool MyMethod (Android.OS.IBinder windowToken, [global::Android.Runtime.GeneratedEnum] Android.Views.InputMethods.HideSoftInputFlags flags)
        {
            const string __id = ""hideSoftInputFromWindow.(Landroid/os/IBinder;I)Z"";
            try {
                JniArgumentValue* __args = stackalloc JniArgumentValue [1];
                __args [0] = new JniArgumentValue ((windowToken == null) ? IntPtr.Zero : ((global::Java.Lang.Object) windowToken).Handle);
                __args [1] = new JniArgumentValue ((int) flags);
                var __rm = JniPeerMembers.InstanceMethods.InvokeAbstractBooleanMethod (__id, this, __args);
                return __rm;
            } finally {
            }
        }
	}
}"
					},
				}
			};

			proj.SetProperty ("AllowUnsafeBlocks", "True");

			if (setAndroidAddKeepAlivesTrue)
				proj.SetProperty ("AndroidAddKeepAlives", "True");

			if (setLinkModeNone)
				proj.SetProperty (isRelease ? proj.ReleaseProperties : proj.DebugProperties, "AndroidLinkMode", "None");

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Building a project should have succeded.");

				var assemblyFile = "UnnamedProject.dll";
				var assemblyPath = (Builder.UseDotNet && (!isRelease || setLinkModeNone)) ? b.Output.GetIntermediaryPath (Path.Combine ("android", "assets", assemblyFile)) : BuildTest.GetLinkedPath (b,  true, assemblyFile);
				using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
					Assert.IsTrue (assembly != null);

					var td = assembly.MainModule.GetType ("UnnamedProject.MyClass");
					Assert.IsTrue (td != null);

					var mr = td.GetMethods ().Where (m => m.Name == "MyMethod").FirstOrDefault ();
					Assert.IsTrue (mr != null);

					var md = mr.Resolve ();
					Assert.IsTrue (md != null);

					bool hasKeepAliveCall = false;
					foreach (var i in md.Body.Instructions) {
						if (i.OpCode.Code != Mono.Cecil.Cil.Code.Call)
							continue;

						if (!i.Operand.ToString ().Contains ("System.GC::KeepAlive"))
							continue;

						hasKeepAliveCall = true;
						break;
					}

					Assert.IsTrue (hasKeepAliveCall == shouldAddKeepAlives);
				}
			}
		}

		[Test]
		public void TypeRegistrationsFallback ([Values (true, false)] bool enabled)
		{
			if (!Builder.UseDotNet)
				Assert.Ignore ("Test only valid on .NET 6");

			var proj = new XamarinAndroidApplicationProject () { IsRelease = true };
			if (enabled)
				proj.SetProperty (proj.ActiveConfigurationProperties, "VSAndroidDesigner", "true");

			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var assemblyFile = "Mono.Android.dll";
				var assemblyPath = BuildTest.GetLinkedPath (b, true, assemblyFile);
				using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
					Assert.IsTrue (assembly != null);

					var td = assembly.MainModule.GetType ("Java.Interop.__TypeRegistrations");
					Assert.IsTrue ((td != null) == enabled);
				}
			}
		}

		[Test]
		public void AndroidUseNegotiateAuthentication ([Values (true, false, null)] bool? useNegotiateAuthentication)
		{
			if (!Builder.UseDotNet)
				Assert.Ignore ("Test only valid on .NET");

			var proj = new XamarinAndroidApplicationProject { IsRelease = true };
			proj.AddReferences ("System.Net.Http");
			proj.MainActivity = proj.DefaultMainActivity.Replace (
				"base.OnCreate (bundle);",
				"base.OnCreate (bundle);\n" +
				"var client = new System.Net.Http.HttpClient (new Xamarin.Android.Net.AndroidMessageHandler ());\n" +
				"client.GetAsync (\"https://microsoft.com\").GetAwaiter ().GetResult ();");

			if (useNegotiateAuthentication.HasValue)
				proj.SetProperty ("AndroidUseNegotiateAuthentication", useNegotiateAuthentication.ToString ());

			var shouldBeEnabled = useNegotiateAuthentication.HasValue && useNegotiateAuthentication.Value;
			using (var b = CreateApkBuilder ()) {
				Assert.IsTrue (b.Build (proj), "Build should have succeeded.");
				var assemblyPath = BuildTest.GetLinkedPath (b, true, "Mono.Android.dll");

				using (var assembly = AssemblyDefinition.ReadAssembly (assemblyPath)) {
					Assert.IsTrue (assembly != null);

					var td = assembly.MainModule.GetType ("Xamarin.Android.Net.NegotiateAuthenticationHelper");
					if (shouldBeEnabled) {
						Assert.IsNotNull (td, "NegotiateAuthenticationHelper shouldn't have been linked out");
					} else {
						Assert.IsNull (td, "NegotiateAuthenticationHelper should have been linked out");
					}
				}
			}
		}

		[Test]
		public void DoNotErrorOnPerArchJavaTypeDuplicates ([Values(true, false)] bool enableMarshalMethods)
		{
			if (!Builder.UseDotNet)
				Assert.Ignore ("Test only valid on .NET");

			var path = Path.Combine (Root, "temp", TestName);
			var lib = new XamarinAndroidLibraryProject { IsRelease = true, ProjectName = "Lib1" };
			lib.SetProperty ("IsTrimmable", "true");
			lib.Sources.Add (new BuildItem.Source ("Library1.cs") {
				TextContent = () => @"
namespace Lib1;
public class Library1 : Com.Example.Androidlib.MyRunner {
	private static bool Is64Bits = IntPtr.Size >= 8;

	public static bool Is64 () {
		return Is64Bits;
	}

	public override void Run () => Console.WriteLine (Is64Bits);
}",
			});
			lib.Sources.Add (new BuildItem ("AndroidJavaSource", "MyRunner.java") {
				Encoding = new UTF8Encoding (encoderShouldEmitUTF8Identifier: false),
				TextContent = () => @"
package com.example.androidlib;

public abstract class MyRunner {
	public abstract void run();
}"
			});
			var proj = new XamarinAndroidApplicationProject { IsRelease = true, ProjectName = "App1" };
			proj.References.Add(new BuildItem.ProjectReference (Path.Combine ("..", "Lib1", "Lib1.csproj"), "Lib1"));
			proj.MainActivity = proj.DefaultMainActivity.Replace (
				"base.OnCreate (bundle);",
				"base.OnCreate (bundle);\n" +
				"if (Lib1.Library1.Is64 ()) Console.WriteLine (\"Hello World!\");");
			proj.SetProperty ("AndroidEnableMarshalMethods", enableMarshalMethods.ToString ());


			using var lb = CreateDllBuilder (Path.Combine (path, "Lib1"));
			using var b = CreateApkBuilder (Path.Combine (path, "App1"));
			Assert.IsTrue (lb.Build (lib), "build should have succeeded.");
			Assert.IsTrue (b.Build (proj), "build should have succeeded.");

			var intermediate = Path.Combine (Root, b.ProjectDirectory, proj.IntermediateOutputPath);
			var dll = $"{lib.ProjectName}.dll";
			Assert64Bit ("android-arm", expected64: false);
			Assert64Bit ("android-arm64", expected64: true);
			Assert64Bit ("android-x86", expected64: false);
			Assert64Bit ("android-x64", expected64: true);

			void Assert64Bit(string rid, bool expected64)
			{
				var assembly = AssemblyDefinition.ReadAssembly (Path.Combine (intermediate, rid, "linked", "shrunk", dll));
				var type = assembly.MainModule.FindType ("Lib1.Library1");
				Assert.NotNull (type, "Should find Lib1.Library1!");
				var cctor = type.GetTypeConstructor ();
				Assert.NotNull (type, "Should find Lib1.Library1.cctor!");
				Assert.AreNotEqual (0, cctor.Body.Instructions.Count);

				/*
				 * IL snippet
				 * .method private hidebysig specialname rtspecialname static 
				 * void .cctor () cil managed 
				 * {
				 *   // Is64Bits = 4 >= 8;
				 *   IL_0000: ldc.i4 4
				 *   IL_0005: ldc.i4.8
				 *   ...
				 */
				var instruction = cctor.Body.Instructions [0];
				Assert.AreEqual (OpCodes.Ldc_I4, instruction.OpCode);
				if (expected64) {
					Assert.AreEqual (8, instruction.Operand, $"Expected 64-bit: {expected64}");
				} else {
					Assert.AreEqual (4, instruction.Operand, $"Expected 64-bit: {expected64}");
				}
			}
		}
	}
}
