using System;
using System.Collections.Generic;
using Microsoft.CSharp;
using System.IO;
using System.Linq;

namespace Xamarin.Android.Tools.Aidl
{
	public class CSharpCodeGenerator
	{
		class NameResoltionCache
		{
			BindingDatabase database;
			CompilationUnit unit;
			Dictionary<string,string> cache = new Dictionary<string, string> ();
			IList<TypeName> parcelable_names;
			
			public NameResoltionCache (BindingDatabase database, CompilationUnit unit, IList<TypeName> parcelableNames)
			{
				this.database = database;
				this.unit = unit;
				parcelable_names = parcelableNames;
				cache ["IBinder"] = "Android.OS.IBinder";
				cache ["CharSequence"] = "Java.Lang.ICharSequence";
				cache ["List"] = "Android.Runtime.JavaList";
				cache ["Map"] = "Android.Runtime.JavaDictionary";
			}
		
			public bool IsParcelable (TypeName type)
			{
				var jn = ToFullJavaName (type);
				jn = type.ArrayDimension > 0 ? jn.Substring (0, jn.Length - 3) : jn;
				foreach (var p in parcelable_names)
					if (p.ToJavaString () == jn)
						return true;
				return false;
			}
			
			bool IsSupportedPrimitiveJavaType (string name)
			{
				switch (name) {
				case "boolean":
				case "byte":
				case "char":
				case "int":
				case "long":
				case "float":
				case "double":
				case "void":
				case "String":
					return true;
				}
				return false;
			}
			
			public string GetCSharpNamespace (TypeName name)
			{
				string dummy, javapkg = name != null ? name.GetPackage () : String.Empty;
				return database.NamespaceMappings.TryGetValue (javapkg, out dummy) ? dummy : name.GetNamespace ();
			}
			
			public string ToCSharpNamespace (TypeName package)
			{
				string dummy, javapkg = package != null ? package.ToJavaString () : String.Empty;
				return package == null ? String.Empty : database.NamespaceMappings.TryGetValue (javapkg, out dummy) ? dummy : package.ToString ();
			}
			
			public string ToFullJavaName (TypeName javaType)
			{
				string java = javaType.ToJavaString ();
				string suffix = javaType.ArrayDimension > 0 ? " []" : "";
				java = javaType.ArrayDimension > 0 ? java.Substring (0, java.Length - 3) : java;
				if (IsSupportedPrimitiveJavaType (java))
					return java;
				foreach (var imp in unit.Imports) {
					int idx = java.IndexOf ('.');
					string head = idx < 0 ? java : java.Substring (0, idx);
					if (imp.Identifiers.Length > 0 && head == imp.Identifiers.Last ())
						return imp.ToJavaString () + (idx < 0 ? "" : java.Substring (idx)) + suffix;
				}
				// FIXME: implement lookup with wildcard import

				// not found.
				return (unit.Package != null ? unit.Package.ToJavaString () + '.' : null) + java + suffix;
			}
			
			public string ToCSharp (TypeName javaType) // java name could be ns-less, or even partial ("import foo.Bar;" + "Bar.Baz" => "foo.Bar.Baz")
			{
				string java = javaType.ToJavaString ();
				string suffix = javaType.ArrayDimension > 0 ? " []" : "";
				java = javaType.ArrayDimension > 0 ? java.Substring (0, java.Length - 3) : java;
				string cs;
				Func<string,string> filter = (ret) => ret == "sbyte []" ? "byte []" : ret;

				if (cache.TryGetValue (java, out cs))
					return filter (cs + suffix);
				if (IsSupportedPrimitiveJavaType (java))
					return filter ((java == "boolean" ? "bool" : java == "byte" ? "sbyte" : java) + suffix);
				var javaFullName = ToFullJavaName (javaType);
				javaFullName = javaFullName.Substring (0, javaFullName.Length - suffix.Length);
				if (database.RegisteredTypes.TryGetValue (javaFullName, out cs)) {
//Console.WriteLine ("Found in lookup " + javaFullName + " / " + cs);
					cache [java] = cs;

					return filter (cs + suffix);
				}
				
				var p = parcelable_names.FirstOrDefault (pp => pp.ToJavaString () == javaFullName);
				if (p != null) {
					var decentNS = GetCSharpNamespace (p);
					var stupidNS = p.GetNamespace ();
					cache [java] = cs = (stupidNS != decentNS ? p.ToString ().Replace (stupidNS, decentNS) : p.ToString ());
//Console.WriteLine ("Found in parcelable lookup " + java + " / " + cs);
					return filter (cs + suffix);
				}
				
				string csns = javaFullName != java ? GetCSharpNamespace (javaType) : String.Empty;
				cache [java] = cs = csns + (string.IsNullOrEmpty (csns) ? "" : ".") + javaType.Identifiers.Last ();
//Console.WriteLine ("NOT Found in lookup " + java + " / " + cs);
				return filter (cs + suffix);
			}
		}

		TextWriter w;
		BindingDatabase database;
		
		public CSharpCodeGenerator (TextWriter writer, BindingDatabase database)
		{
			w = writer;
			this.database = database;
		}
		
		CompilationUnit unit;
		NameResoltionCache name_cache;
		string self_ns;
		string cl_variable;
		
		string EnsureAndGetClassloader ()
		{
			if (cl_variable != null)
				return null;	// classloader variable already initialized
			cl_variable = "__cl";
			return string.Format("global::Java.Lang.ClassLoader __cl = this.Class.ClassLoader; ");
		}

		public void GenerateCode (CompilationUnit unit, IList<TypeName> parcelableNames, ConverterOptions opts)
		{
			w.WriteLine ("// This file is automatically generated and not supposed to be modified.");
			w.WriteLine ("using System;");
			w.WriteLine ("using Boolean = System.Boolean;");
			w.WriteLine ("using String = System.String;");
			w.WriteLine ("using List = Android.Runtime.JavaList;");
			w.WriteLine ("using Map = Android.Runtime.JavaDictionary;");

			this.unit = unit;
			name_cache = new NameResoltionCache (database, unit, parcelableNames);
			self_ns = name_cache.ToCSharpNamespace (unit.Package);
			
			if (unit.Imports != null) {
				var nss = new List<string> ();
				foreach (var imp in unit.Imports) {
					string dummy, pkg = imp.GetPackage ();
					string ns = database.NamespaceMappings.TryGetValue (pkg, out dummy) ? dummy : imp.GetNamespace ();
					if (nss.Contains (ns))
						continue;
					nss.Add (ns);
					w.WriteLine ("using " + ns + ";");
				}
			}
			if (unit.Package != null) {
				w.WriteLine ();
				w.WriteLine ("namespace {0}", self_ns);
				w.WriteLine ("{");
			}

			foreach (var type in unit.Types) {
				if (type is Parcelable) {
					if (type is Parcelable) {
						switch (opts.ParcelableHandling) {
						case ParcelableHandling.Ignore:
							continue;
						case ParcelableHandling.Error:
							throw new InvalidOperationException ("Parcelable AIDL cannot be converted to C#");
						case ParcelableHandling.Stub:
							StubParcelable ((Parcelable) type);
							break;
						}
					}
				}
				else if (type is Interface)
					GenerateCode ((Interface) type);
			}
			
			if (unit.Package != null)
				w.WriteLine ("}");
			
			this.unit = null;
		}
		
		public void StubParcelable (Parcelable type)
		{
			string csNS = name_cache.GetCSharpNamespace (type.Name);
			if (csNS.Length > 0)
				w.WriteLine ("namespace {0} {{", csNS);
			string name = type.Name.Identifiers.Last ();
			w.WriteLine (@"
	public class {0} : global::Java.Lang.Object, global::Android.OS.IParcelable
	{{
		public static global::Android.OS.IParcelableCreator Creator; // FIXME: implement

		public virtual int DescribeContents ()
		{{
			throw new NotImplementedException ();
		}}

		// LAMESPEC: Android IParcelable interface is bogus by design. It does not expose
		// this method, while aidl tool explicitly expects this method and generates such
		// code that invokes it(!)
		// Seealso: http://code.google.com/p/android/issues/detail?id=21777
		public virtual void ReadFromParcel (global::Android.OS.Parcel parcel)
		{{
			throw new NotImplementedException ();
		}}

		public virtual void WriteToParcel (global::Android.OS.Parcel parcel, global::Android.OS.ParcelableWriteFlags flags)
		{{
			throw new NotImplementedException ();
		}}
	}}", name);
			if (csNS.Length > 0)
				w.WriteLine ("}");
		}
		
		public void GenerateCode (Interface type)
		{
			w.WriteLine ("\tpublic interface " + type.Name + " : global::Android.OS.IInterface");
			w.WriteLine ("\t{");
			foreach (var m in type.Methods)
				GenerateCode (m);
			w.WriteLine ("\t}");

			w.WriteLine (@"
	public abstract class {0}Stub : global::Android.OS.Binder, global::Android.OS.IInterface, {1}{2}{0}
	{{
		const string descriptor = ""{3}{2}{4}"";
		public {0}Stub ()
		{{
			this.AttachInterface (this, descriptor);
		}}

		public static {1}{2}{0} AsInterface (global::Android.OS.IBinder obj)
		{{
			if (obj == null)
				return null;
			var iin = (global::Android.OS.IInterface) obj.QueryLocalInterface (descriptor);
			if (iin != null && iin is {1}{2}{0})
				return ({1}{2}{0}) iin;
			return new Proxy (obj);
		}}

		public global::Android.OS.IBinder AsBinder ()
		{{
			return this;
		}}

		protected override bool OnTransact (int code, global::Android.OS.Parcel data, global::Android.OS.Parcel reply, int flags)
		{{
			switch (code) {{
			case global::Android.OS.BinderConsts.InterfaceTransaction:
				reply.WriteString (descriptor);
				return true;",
// end of long formatted output...
				type.Name,
				self_ns,
				unit.Package != null ? "." : String.Empty,
				unit.Package != null ? unit.Package.ToJavaString () : null,
				type.JavaName);

			foreach (var method in type.Methods) {
				cl_variable = null;
				bool isVoidReturn = method.ReturnType.ToString () == "void";
				w.WriteLine (@"
			case Transaction{0}: {{
				data.EnforceInterface (descriptor);", method.Name);
				for (int i = 0; method.Arguments != null && i < method.Arguments.Length; i++) {
					var a = method.Arguments [i];
					w.WriteLine ("\t\t\t\t{0} {1} = default ({0});", ToOutputTypeName (name_cache.ToCSharp (a.Type)), "arg" + i);
					if (a.Modifier == null || a.Modifier.Contains ("in"))
						w.WriteLine ("\t\t\t\t{0}", GetCreateStatements (a.Type, "data", "arg" + i));
				}
				string args = String.Join (", ", (from i in Enumerable.Range (0, method.Arguments.Length) select "arg" + i).ToArray ());
				if (isVoidReturn)
					w.WriteLine ("\t\t\t\tthis.{0} ({1});", method.Name, args);
				else
					w.WriteLine ("\t\t\t\tvar result = this.{0} ({1});", method.Name, args);
				if (method.Modifier == null || !method.Modifier.Contains ("oneway"))
					w.WriteLine ("\t\t\t\treply.WriteNoException ();");
				if (!isVoidReturn)
					w.WriteLine ("\t\t\t\t{0}", GetWriteStatements (method.ReturnType, "reply", "result", "global::Android.OS.ParcelableWriteFlags.ReturnValue"));
				for (int i = 0; method.Arguments != null && i < method.Arguments.Length; i++) {
					var a = method.Arguments [i];
					if (a.Modifier == null || a.Modifier.Contains ("out"))
						w.WriteLine ("\t\t\t\t{0}", GetWriteStatements (a.Type, "data", "arg" + i, "global::Android.OS.ParcelableWriteFlags.None"));
				}
				w.WriteLine ("\t\t\t\treturn true;");
				w.WriteLine ("\t\t\t\t}");
			}
			w.WriteLine (@"
			}}
			return base.OnTransact (code, data, reply, flags);
		}}

		public class Proxy : Java.Lang.Object, {1}{2}{0}
		{{
			global::Android.OS.IBinder remote;

			public Proxy (global::Android.OS.IBinder remote)
			{{
				this.remote = remote;
			}}

			public global::Android.OS.IBinder AsBinder ()
			{{
				return remote;
			}}

			public string GetInterfaceDescriptor ()
			{{
				return descriptor;
			}}",
				type.Name, self_ns, unit.Package != null ? "." : String.Empty);
			foreach (var method in type.Methods) {
				cl_variable = null;
				string args = JoinArguments (method);
				w.WriteLine (@"
			public {0} {1} ({2})
			{{
				global::Android.OS.Parcel __data = global::Android.OS.Parcel.Obtain ();
", ToOutputTypeName (name_cache.ToCSharp (method.ReturnType)), method.Name, args);
				bool isOneWay = type.Modifier == "oneway";
				bool hasReturn = method.ReturnType.ToString () != "void";
				if (!isOneWay)
					w.WriteLine ("\t\t\t\tglobal::Android.OS.Parcel __reply = global::Android.OS.Parcel.Obtain ();");
				if (hasReturn)
					w.WriteLine ("{0} __result = default ({0});", ToOutputTypeName (name_cache.ToCSharp (method.ReturnType)));
				w.WriteLine (@"
				try {
					__data.WriteInterfaceToken (descriptor);");
				foreach (var arg in method.Arguments)
					if (arg.Modifier == null || arg.Modifier.Contains ("in"))
						w.WriteLine ("\t\t\t\t\t" + GetWriteStatements (arg.Type, "__data", SafeCSharpName (arg.Name), "global::Android.OS.ParcelableWriteFlags.None"));
				w.WriteLine ("\t\t\t\t\tremote.Transact ({1}Stub.Transaction{0}, __data, {2}, 0);",
					method.Name,
					type.Name,
					isOneWay ? "null" : "__reply");
				if (!isOneWay)
					w.WriteLine ("\t\t\t\t\t__reply.ReadException ();");
				if (hasReturn)
					w.WriteLine ("\t\t\t\t\t{0}", GetCreateStatements (method.ReturnType, "__reply", "__result"));
				foreach (var arg in method.Arguments)
					if (arg.Modifier != null && arg.Modifier.Contains ("out"))
						w.WriteLine ("\t\t\t\t\t{0}", GetReadStatements (arg.Type, "__reply", SafeCSharpName (arg.Name)));
				if (hasReturn)
					w.WriteLine (@"
				} finally {
					__reply.Recycle ();
					__data.Recycle ();
				}
				return __result;");
				else
					w.WriteLine (@"
				} finally {
					__data.Recycle ();
				}");
				w.WriteLine (@"
			}
");
			}
			w.WriteLine (@"
		}"); // end of Proxy

			for (int i = 0; i < type.Methods.Length; i++) {
				var method = type.Methods [i];
				w.WriteLine (@"
		internal const int Transaction{0} = global::Android.OS.Binder.InterfaceConsts.FirstCallTransaction + {1};", method.Name, i);
			}
			foreach (var method in type.Methods)
				w.WriteLine (@"
		public abstract {0} {1} ({2});",
					ToOutputTypeName (name_cache.ToCSharp (method.ReturnType)),
					method.Name,
					JoinArguments (method));
			w.WriteLine (@"
	}"); // end of Stub
		}
		
		public void GenerateCode (Method method)
		{
			w.Write ("\t\t{0} {1} (", ToOutputTypeName (name_cache.ToCSharp (method.ReturnType)), method.Name);
			bool written = false;
			if (method.Arguments != null)
				foreach (var a in method.Arguments) {
					if (written)
						w.Write (", ");
					else
						written = true;
					w.Write ("{0} {1}", ToOutputTypeName (name_cache.ToCSharp (a.Type)), SafeCSharpName (a.Name));
				}
			w.WriteLine (");");
		}
		
		string GetCreateStatements (TypeName type, string parcel, string arg)
		{
			string csname = name_cache.ToCSharp (type);
			switch (csname) {
			case "String":
				return String.Format ("{0} = {1}.ReadString ();", arg, parcel);
			case "String []":
				return String.Format ("{0} = {1}.CreateStringArray ();", arg, parcel);
			case "bool":
				return String.Format ("{0} = {1}.ReadInt () != 0;", arg, parcel);
			case "bool []":
				return String.Format ("{0} = {1}.CreateBooleanArray ();", arg, parcel);
			// FIXME: I'm not sure if aidl should support byte...
			case "sbyte":
				return String.Format ("{0} = {1}.ReadByte ();", arg, parcel);
			case "byte []":
				return String.Format ("{0} = {1}.CreateByteArray ();", arg, parcel);
			case "char":
				return String.Format ("{0} = (char) {1}.ReadInt ();", arg, parcel);
			case "char []":
				return String.Format ("{0} = {1}.CreateCharArray ();", arg, parcel);
			case "int":
				return String.Format ("{0} = {1}.ReadInt ();", arg, parcel);
			case "int []":
				return String.Format ("{0} = {1}.CreateIntArray ();", arg, parcel);
			case "long":
				return String.Format ("{0} = {1}.ReadLong ();", arg, parcel);
			case "long []":
				return String.Format ("{0} = {1}.CreateLongArray ();", arg, parcel);
			case "float":
				return String.Format ("{0} = {1}.ReadFloat ();", arg, parcel);
			case "float []":
				return String.Format ("{0} = {1}.CreateFloatArray ();", arg, parcel);
			case "double":
				return String.Format ("{0} = {1}.ReadDouble ();", arg, parcel);
			case "double []":
				return String.Format ("{0} = {1}.CreateDoubleArray ();", arg, parcel);
			// FIXME: are JavaList for List and JavaDictionary for Map always appropriate?
			case "List":
			case "Android.Runtime.JavaList":
				if (type.GenericArguments != null) {
					switch (name_cache.ToCSharp (type.GenericArguments.First ())) {
					case "String":
					case "Java.Lang.ICharSequence":
						return String.Format ("{0} = (global::Android.Runtime.JavaList) {1}.CreateStringArrayList ();", arg, parcel);
					case "Android.OS.IBinder":
						return String.Format ("{0} = (global::Android.Runtime.JavaList) {1}.CreateBinderArrayList ();", arg, parcel);
					default:
						return String.Format ("{0} = (global::Android.Runtime.JavaList) {1}.CreateTypedArrayList ({2}.Creator);", arg, parcel, ToOutputTypeName (name_cache.ToCSharp (type.GenericArguments [0])));
					}
				}
				return EnsureAndGetClassloader() + String.Format ("{0} = (global::Android.Runtime.JavaList) {1}.ReadArrayList ({2});", arg, parcel, cl_variable);
			case "Map":
			case "Android.Runtime.JavaDictionary":
				return EnsureAndGetClassloader() + String.Format ("{0} = (global::Android.Runtime.JavaDictionary) {1}.ReadHashMap ({2});", arg, parcel, cl_variable);
			case "Android.OS.IParcelable":
				return String.Format ("{0} = {1}.ReadInt () != 0 ? ({2}) global::Android.OS.Bundle.Creator.CreateFromParcel ({1}) : null;", arg, parcel, ToOutputTypeName (csname));
			case "Android.OS.IBinder":
				return String.Format ("{0} = {1}.ReadStrongBinder ();", arg, parcel);
			case "Android.OS.IBinder []":
				return String.Format ("{0} = {1}.CreateBinderArray ();", arg, parcel);
			case "Java.Lang.ICharSequence":
				return String.Format ("{0} = {1}.ReadInt () != 0 ? (global::Java.Lang.ICharSequence) global::Android.Text.TextUtils.CharSequenceCreator.CreateFromParcel ({1}) : null;", arg, parcel);
			default:
				if (name_cache.IsParcelable (type)) {
					if (type.ArrayDimension > 0) // ParcelableCreator
						return String.Format ("{0} = global::System.Array.ConvertAll<global::Java.Lang.Object,{2}> ({1}.CreateTypedArray ({2}.Creator), __input => ({2}) __input);", arg, parcel, ToOutputTypeName (csname.Substring (0, csname.Length - 3)));
					else
						return String.Format ("{0} = {1}.ReadInt () != 0 ? ({2}) global::Android.OS.Bundle.Creator.CreateFromParcel ({1}) : null;", arg, parcel, ToOutputTypeName (csname));
				}
				else if (type.ArrayDimension > 0)
					throw new NotSupportedException (String.Format ("AIDL does not support creating this array type: {0}", type));
				else
					// interfaces
					return String.Format ("{0} = {2}Stub.AsInterface ({1}.ReadStrongBinder ());", arg, parcel, ToOutputTypeName (csname));
			}
		}
		
		string GetReadStatements (TypeName type, string parcel, string arg)
		{
			string csname = name_cache.ToCSharp (type);
			switch (csname) {
			case "String []":
				return String.Format ("{1}.ReadStringArray ({0});", arg, parcel);
			case "bool []":
				return String.Format ("{1}.ReadBooleanArray ({0});", arg, parcel);
			// FIXME: I'm not sure if aidl should support byte...
			case "byte []":
				return String.Format ("{1}.ReadByteArray ({0});", arg, parcel);
			case "char []":
				return String.Format ("{1}.ReadCharArray ({0});", arg, parcel);
			case "int []":
				return String.Format ("{1}.ReadIntArray ({0});", arg, parcel);
			case "long []":
				return String.Format ("{1}.ReadLongArray ({0});", arg, parcel);
			case "float []":
				return String.Format ("{1}.ReadFloatArray ({0});", arg, parcel);
			case "double []":
				return String.Format ("{1}.ReadDoubleArray ({0});", arg, parcel);
			case "Android.OS.IParcelable":
				return String.Format ("{0} = {1}.ReadInt () != 0 ? ({2}) global::Android.OS.Bundle.Creator.CreateFromParcel ({1}) : null;", arg, parcel, ToOutputTypeName (csname));
			// FIXME: are JavaList for List and JavaDictionary for Map always appropriate?
			case "List":
			case "Android.Runtime.JavaList":
				if (type.GenericArguments != null) {
					switch (name_cache.ToCSharp (type.GenericArguments.First ())) {
					case "String":
					case "Java.Lang.ICharSequence":
						return String.Format ("{1}.ReadStringList ((global::System.Collections.Generic.IList<string>) {0});", arg, parcel);
					case "Android.OS.IBinder":
						return String.Format ("{1}.ReadBinderList ((global::System.Collections.Generic.IList<global::Android.OS.IBinder>) {0});", arg, parcel);
					default:
						return String.Format ("{1}.ReadTypedList ({0}, {2}.Creator);", arg, parcel, ToOutputTypeName (name_cache.ToCSharp (type.GenericArguments [0])));
					}
				}
				return EnsureAndGetClassloader() + String.Format ("{0} = (global::Android.Runtime.JavaList) {1}.ReadArrayList ({2});", arg, parcel, cl_variable);
			case "Map":
			case "Android.Runtime.JavaDictionary":
				return EnsureAndGetClassloader() + String.Format ("{0} = (global::Android.Runtime.JavaDictionary) {1}.ReadHashMap ({2});", arg, parcel, cl_variable);
			case "Android.OS.IBinder":
				return String.Format ("{0} = {1}.ReadStrongBinder ();", arg, parcel);
			case "Android.OS.IBinder []":
				return String.Format ("{1}.ReadBinderArray ({0});", arg, parcel);
			default:
				if (name_cache.IsParcelable (type)) {
					if (type.ArrayDimension > 0) // ParcelableCreator
						return String.Format ("{1}.ReadTypedArray ({0}, {2}.Creator);", arg, parcel, ToOutputTypeName (csname.Substring (0, csname.Length - 3)));
					else
						return String.Format ("if ({1}.ReadInt () != 0) {0}.ReadFromParcel ({1});", arg, parcel);
				}
				else if (type.ArrayDimension > 0)
					throw new NotSupportedException (String.Format ("AIDL does not support reading this array type: {0}", type));
				else
					// interfaces
					return String.Format ("{0} = {2}Stub.AsInterface ({1}.ReadStrongBinder ());", arg, parcel, ToOutputTypeName (csname));
			}
		}
		
		string GetWriteStatements (TypeName type, string parcel, string arg, string parcelableWriteFlags)
		{
			string csname = name_cache.ToCSharp (type);
			switch (csname) {
			case "String":
				return parcel + ".WriteString (" + arg + ");";
			case "String []":
				return parcel + ".WriteStringArray (" + arg + ");";
			case "bool":
				return parcel + ".WriteInt (" + arg + " ? 1 : 0);";
			case "bool []":
				return parcel + ".WriteBooleanArray (" + arg + ");";
			// FIXME: I'm not sure if aidl should support byte...
			case "sbyte":
				return parcel + ".WriteByte (" + arg + ");";
			case "byte []":
				return String.Format ("{1}.WriteByteArray ({0});", arg, parcel);
			case "char":
				return parcel + ".WriteInt ((int) " + arg + ");";
			case "char []":
				return parcel + ".WriteCharArray (" + arg + ");";
			case "int":
				return parcel + ".WriteInt (" + arg + ");";
			case "int []":
				return parcel + ".WriteIntArray (" + arg + ");";
			case "long":
				return parcel + ".WriteLong (" + arg + ");";
			case "long []":
				return parcel + ".WriteLongArray (" + arg + ");";
			case "float":
				return parcel + ".WriteFloat (" + arg + ");";
			case "float []":
				return parcel + ".WriteFloatArray (" + arg + ");";
			case "double":
				return parcel + ".WriteDouble (" + arg + ");";
			case "double []":
				return parcel + ".WriteDoubleArray (" + arg + ");";
			// FIXME: are JavaList for List and JavaDictionary for Map always appropriate?
			case "List":
			case "Android.Runtime.JavaList":
				if (type.GenericArguments != null) {
					switch (name_cache.ToCSharp (type.GenericArguments.First ())) {
					case "String":
					case "Java.Lang.ICharSequence":
						return String.Format ("{1}.WriteStringList ((global::System.Collections.Generic.IList<string>) {0});", arg, parcel);
					case "Android.OS.IBinder":
						return String.Format ("{1}.WriteBinderList ((global::System.Collections.Generic.IList<global::Android.OS.IBinder>) {0});", arg, parcel);
					default:
						return String.Format ("{1}.WriteTypedList ({0});", arg, parcel, ToOutputTypeName (name_cache.ToCSharp (type.GenericArguments [0])));
					}
				}
				return String.Format ("{1}.WriteList ({0});", arg, parcel);
			case "Map":
			case "Android.Runtime.JavaDictionary":
				return String.Format ("{1}.WriteMap ({0});", arg, parcel);
			case "Android.OS.Bundle":
			case "Android.OS.IParcelable":
				return String.Format ("if ({0} != null) {{ {1}.WriteInt (1); {0}.WriteToParcel ({1}, {2}); }} else {1}.WriteInt (0);", arg, parcel, parcelableWriteFlags);
			case "Android.OS.IBinder":
				return String.Format ("{1}.WriteStrongBinder ({0});", arg, parcel);
			case "Android.OS.IBinder []":
				return String.Format ("{1}.WriteBinderArray ({0});", arg, parcel);
			case "Java.Lang.ICharSequence":
				return String.Format ("if ({0} != null) {{ {1}.WriteInt (1); global::Android.Text.TextUtils.WriteToParcel ({0}, {1}, {2}); }} else {1}.WriteInt (0);", arg, parcel, parcelableWriteFlags);
			default: // interfaces
				if (name_cache.IsParcelable (type)) {
					if (type.ArrayDimension > 0)
						return String.Format ("{1}.WriteTypedArray ({0}, {2});", arg, parcel, parcelableWriteFlags);
					else
						return String.Format ("if ({0} != null) {{ {1}.WriteInt (1); {0}.WriteToParcel ({1}, {2}); }} else {1}.WriteInt (0);", arg, parcel, parcelableWriteFlags);
				}
				else if (type.ArrayDimension > 0)
					throw new NotSupportedException (String.Format ("AIDL does not support writing this array type: {0}", type));
				else
					return String.Format ("{1}.WriteStrongBinder (((({0} != null)) ? ({0}.AsBinder ()) : (null)));", arg, parcel);
			}
		}
		
		// FIXME: should this be used?
		string GetCreatorName (TypeName type)
		{
			string csname = name_cache.ToCSharp (type);
			switch (csname) {
			case "String":
			case "Java.Lang.ICharSequence":
				return "Android.OS.Parcel.StringCreator";
			case "List":
			case "Android.Runtime.JavaList":
				return "Android.OS.Parcel.ArrayListCreator";
			default:
				if (name_cache.IsParcelable (type))
					return csname + ".Creator";
				return String.Empty;
			}
		}
		
		string [] cs_units;
		
		string ToOutputTypeName (string name)
		{
			if (cs_units == null)
				cs_units = unit.Package == null ? new string [0] : name_cache.ToCSharpNamespace (unit.Package).Split ('.');
			int idx = name.IndexOf ('.');
			if (idx < 0)
				return name;
			if (Array.IndexOf (cs_units, name.Substring (0, idx)) < 0)
				return name;
			return "global::" + name;
		}
	
		static CSharpCodeProvider csp = new CSharpCodeProvider ();
		static string SafeCSharpName (string name)
		{
			return csp.IsValidIdentifier (name) ? name : "@" + name;
		}
		
		string JoinArguments (Method m)
		{
			return m.Arguments != null ? String.Join (", ", (from a in m.Arguments select ToOutputTypeName (name_cache.ToCSharp (a.Type)) + " " + SafeCSharpName (a.Name)).ToArray ()) : null;
		}
	}
}

