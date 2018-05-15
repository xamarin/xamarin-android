﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Text.RegularExpressions;

namespace Xamarin.Android.Tasks
{
	class ManagedResourceParser : ResourceParser
	{
		CodeTypeDeclaration resources;
		CodeTypeDeclaration layout, ids, drawable, strings, colors, dimension, raw, animator, animation, attrib, boolean, font, ints, interpolators, menu, mipmaps, plurals, styleable, style, arrays, xml;
		Dictionary<string, string> map;
		bool app;

		void SortMembers (CodeTypeDeclaration decl)
		{
			CodeTypeMember [] members = new CodeTypeMember [decl.Members.Count];
			decl.Members.CopyTo (members, 0);
			decl.Members.Clear ();
			Array.Sort (members, (x, y) => string.Compare (x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
			decl.Members.AddRange (members);
		}

		public CodeTypeDeclaration Parse (string resourceDirectory, IEnumerable<string> additionalResourceDirectories, bool isApp, Dictionary<string, string> resourceMap)
		{
			app = isApp;
			if (!Directory.Exists (resourceDirectory))
				throw new ArgumentException ("Specified resource directory was not found: " + resourceDirectory);

			app = isApp;
			map = resourceMap ?? new Dictionary<string, string> ();
			resources = CreateResourceClass ();
			animation = CreateClass ("Animation");
			animator = CreateClass ("Animator");
			arrays = CreateClass ("Array");
			attrib = CreateClass ("Attribute");
			boolean = CreateClass ("Boolean");
			font = CreateClass ("Font");
			layout = CreateClass ("Layout");
			ids = CreateClass ("Id");
			ints = CreateClass ("Integer");
			interpolators = CreateClass ("Interpolator");
			menu = CreateClass ("Menu");
			mipmaps = CreateClass ("Mipmap");
			drawable = CreateClass ("Drawable");
			strings = CreateClass ("String");
			colors = CreateClass ("Color");
			dimension = CreateClass ("Dimension");
			raw = CreateClass ("Raw");
			plurals = CreateClass ("Plurals");
			styleable = CreateClass ("Styleable");
			style = CreateClass ("Style");
			xml = CreateClass ("Xml");

			// This top most R.txt will contain EVERYTHING we need. including library resources since it represents
			// the final build.
			var rTxt = Path.Combine(resourceDirectory, "..", "R.txt");
			if (File.Exists (rTxt)) {
				ProcessRtxtFile (rTxt);
			} else {
				foreach (var dir in Directory.EnumerateDirectories (resourceDirectory, "*", SearchOption.TopDirectoryOnly)) {
					foreach (var file in Directory.EnumerateFiles (dir, "*.*", SearchOption.AllDirectories)) {
						ProcessResourceFile (file);
					}
				}
				if (additionalResourceDirectories != null) {
					foreach (var dir in additionalResourceDirectories) {
						rTxt = Path.Combine (dir, "..", "R.txt");
						if (File.Exists (rTxt)) {
							ProcessRtxtFile (rTxt);
						} else {
							foreach (var file in Directory.EnumerateFiles (dir, "*.*", SearchOption.AllDirectories)) {
								ProcessResourceFile (file);
							}
						}
					}
				}
			}

			SortMembers (animation);
			SortMembers (animator);
			SortMembers (ids);
			SortMembers (attrib);
			SortMembers (arrays);
			SortMembers (boolean);
			SortMembers (colors);
			SortMembers (dimension);
			SortMembers (drawable);
			SortMembers (font);
			SortMembers (ints);
			SortMembers (interpolators);
			SortMembers (layout);
			SortMembers (mipmaps);
			SortMembers (menu);
			SortMembers (raw);
			SortMembers (plurals);
			SortMembers (strings);
			SortMembers (style);
			SortMembers (styleable);
			SortMembers (xml);


			if (animation.Members.Count > 1)
				resources.Members.Add (animation);
			if (animator.Members.Count > 1)
				resources.Members.Add (animator);
			if (arrays.Members.Count > 1)
				resources.Members.Add (arrays);
			if (attrib.Members.Count > 1)
				resources.Members.Add (attrib);
			if (boolean.Members.Count > 1)
				resources.Members.Add (boolean);
			if (colors.Members.Count > 1)
				resources.Members.Add (colors);
			if (dimension.Members.Count > 1)
				resources.Members.Add (dimension);
			if (drawable.Members.Count > 1)
				resources.Members.Add (drawable);
			if (font.Members.Count > 1)
				resources.Members.Add (font);
			if (ids.Members.Count > 1)
				resources.Members.Add (ids);
			if (ints.Members.Count > 1)
				resources.Members.Add (ints);
			if (interpolators.Members.Count > 1)
				resources.Members.Add (interpolators);
			if (layout.Members.Count > 1)
				resources.Members.Add (layout);
			if (menu.Members.Count > 1)
				resources.Members.Add (menu);
			if (mipmaps.Members.Count > 1)
				resources.Members.Add (mipmaps);
			if (raw.Members.Count > 1)
				resources.Members.Add (raw);
			if (plurals.Members.Count > 1)
				resources.Members.Add (plurals);
			if (strings.Members.Count > 1)
				resources.Members.Add (strings);
			if (style.Members.Count > 1)
				resources.Members.Add (style);
			if (styleable.Members.Count > 1)
				resources.Members.Add (styleable);
			if (xml.Members.Count > 1)
				resources.Members.Add (xml);

			return resources;
		}

		static Regex regex = new Regex (@"([A-Za-z\[\]]+) ([A-Za-z]+) (.+(?=0x))(\w*)",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);
		static Regex regexarray = new Regex (@"([A-Za-z\[\]]+) ([A-Za-z]+) (.+(?={))(\w*)",
			RegexOptions.Compiled | RegexOptions.IgnoreCase);


		void ProcessRtxtFile (string file)
		{
			var lines = System.IO.File.ReadLines (file);
			foreach (var line in lines) {
				var match = regex.Match (line);
				if (!match.Success)
					continue;
				string type = match.Groups[1].Value.Trim();
				string item = match.Groups[2].Value.Trim();
				string itemName = match.Groups[3].Value.Trim();
				string strValue = match.Groups[4].Value;
				int value = 0;
				if (type != "int[]") {
					value = Convert.ToInt32 (strValue.Trim(), 16);
				}
				switch (item) {
				case "anim":
					CreateIntField (animation, itemName, value);
					break;
				case "animator":
					CreateIntField (animator, itemName, value);
					break;
				case "attr":
					CreateIntField (attrib, itemName, value);
					break;
				case "bool":
					CreateIntField (boolean, itemName, value);
					break;
				case "color":
					CreateIntField (colors, itemName, value);
					break;
				case "dimen":
					CreateIntField (dimension, itemName, value);
					break;
				case "drawable":
					CreateIntField (drawable, itemName, value);
					break;
				case "font":
					CreateIntField (font, itemName, value);
					break;
				case "id":
					CreateIntField (ids, itemName, value);
					break;
				case "integer":
					CreateIntField (ints, itemName, value);
					break;
				case "interpolator":
					CreateIntField (interpolators, itemName, value);
					break;
				case "layout":
					CreateIntField (layout, itemName, value);
					break;
				case "menu":
					CreateIntField (menu, itemName, value);
					break;
				case "mipmap":
					CreateIntField (mipmaps, itemName, value);
					break;
				case "plurals":
					CreateIntField (plurals, itemName, value);
					break;
				case "string":
					CreateIntField (strings, itemName, value);
					break;
				case "style":
					CreateIntField (style, itemName, value);
					break;
				case "styleable":
					switch (type) {
					case "int":
						CreateIntField (styleable, itemName, value);
						break;
					case "int[]":
						match = regexarray.Match (line);
						if (!match.Success)
							continue;
						itemName = match.Groups[3].Value.Trim();
						strValue = match.Groups [4].Value;
						var arrayValues = strValue.Trim (new char [] { '{', '}' })
							.Replace (" ", "")
							.Split (new char [] { ',' });
						CreateIntArrayField (styleable, itemName, arrayValues.Length,
							arrayValues.Select (x => string.IsNullOrEmpty (x) ? 0 : Convert.ToInt32 (x, 16)).ToArray ());
						break;
					}
					break;
				case "xml":
					CreateIntField (xml, itemName, value);
					break;
				}
			}
		}

		void ProcessResourceFile (string file)
		{
			var fileName = Path.GetFileNameWithoutExtension (file);
			if (string.IsNullOrEmpty (fileName))
				return;
			if (fileName.EndsWith (".9", StringComparison.OrdinalIgnoreCase))
				fileName = Path.GetFileNameWithoutExtension (fileName);
			var path = Directory.GetParent (file).Name;
			var ext = Path.GetExtension (file);
			switch (ext) {
			case ".xml":
			case ".axml":
				if (string.Compare (path, "raw", StringComparison.OrdinalIgnoreCase) == 0)
					goto default;
				ProcessXmlFile (file);
				break;
			default:
				break;
			}
			CreateResourceField (path, fileName);
		}

		CodeTypeDeclaration CreateResourceClass ()
		{
			var decl = new CodeTypeDeclaration ("Resource") {
				IsPartial = true,
			};
			var asm = Assembly.GetExecutingAssembly ().GetName ();
			var codeAttrDecl =
				new CodeAttributeDeclaration ("System.CodeDom.Compiler.GeneratedCodeAttribute",
					new CodeAttributeArgument (
						new CodePrimitiveExpression (asm.Name)),
					new CodeAttributeArgument (
						new CodePrimitiveExpression (asm.Version.ToString ()))
				);
			decl.CustomAttributes.Add (codeAttrDecl);
			return decl;
		}

		CodeTypeDeclaration CreateClass (string type)
		{
			var t = new CodeTypeDeclaration (ResourceParser.GetNestedTypeName (type)) {
				IsPartial = true,
				TypeAttributes = TypeAttributes.Public,
			};
			t.Members.Add (new CodeConstructor () {
				Attributes = MemberAttributes.Private,
			});
			return t;
		}

		void CreateField (CodeTypeDeclaration parentType, string name, Type type)
		{
			var f = new CodeMemberField (type, name) {
				// pity I can't make the member readonly...
				Attributes = app ? MemberAttributes.Const | MemberAttributes.Public : MemberAttributes.Static | MemberAttributes.Public,
			};
			parentType.Members.Add (f);
		}

		void CreateIntField (CodeTypeDeclaration parentType, string name, int value = 0)
		{
			string mappedName = GetResourceName (parentType.Name, name, map);
			if (parentType.Members.OfType<CodeTypeMember> ().Any (x => string.Compare (x.Name, mappedName, StringComparison.OrdinalIgnoreCase) == 0))
				return;
			var f = new CodeMemberField (typeof (int), mappedName) {
				// pity I can't make the member readonly...
				Attributes = app ? MemberAttributes.Const | MemberAttributes.Public : MemberAttributes.Static | MemberAttributes.Public,
				InitExpression = new CodePrimitiveExpression (value),
				Comments = {
						new CodeCommentStatement ($"aapt resource value: {value}"),
					},
			};
			parentType.Members.Add (f);
		}

		void CreateIntArrayField (CodeTypeDeclaration parentType, string name, int count, params int[] values)
		{
			string mappedName = GetResourceName (parentType.Name, name, map);
			if (parentType.Members.OfType<CodeTypeMember> ().Any (x => string.Compare (x.Name, mappedName, StringComparison.OrdinalIgnoreCase) == 0))
				return;
			var f = new CodeMemberField (typeof (int []), name) {
				// pity I can't make the member readonly...
				Attributes = MemberAttributes.Static | MemberAttributes.Public,
			};
			CodeArrayCreateExpression c = (CodeArrayCreateExpression)f.InitExpression;
			if (c == null) {
				f.InitExpression = c = new CodeArrayCreateExpression (typeof (int []));
			}
			for (int i = 0; i < count; i++) {
				int value = 0;
				if (i < values.Length)
					value = values[i];
				c.Initializers.Add (new CodePrimitiveExpression (value));
			}

			parentType.Members.Add (f);
		}

		HashSet<string> resourceNamesToUseDirectly = new HashSet<string> () {
				"integer-array",
				"string-array",
				"declare-styleable",
				"add-resource",
			};

		void CreateResourceField (string root, string fieldName, XmlReader element = null)
		{
			var i = root.IndexOf ('-');
			var item = i < 0 ? root : root.Substring (0, i);
			item = resourceNamesToUseDirectly.Contains (root) ? root : item;
			switch (item.ToLower ()) {
			case "animator":
				CreateIntField (animator, fieldName);
				break;
			case "bool":
				CreateIntField (boolean, fieldName);
				break;
			case "color":
				CreateIntField (colors, fieldName);
				break;
			case "drawable":
				CreateIntField (drawable, fieldName);
				break;
			case "dimen":
			case "font":
				CreateIntField (font, fieldName);
				break;
			case "fraction":
				CreateIntField (dimension, fieldName);
				break;
			case "integer":
				CreateIntField (ints, fieldName);
				break;
			case "interpolator":
				CreateIntField (interpolators, fieldName);
				break;
			case "anim":
				CreateIntField (animation, fieldName);
				break;
			case "attr":
				CreateIntField (attrib, fieldName);
				break;
			case "layout":
				CreateIntField (layout, fieldName);
				break;
			case "menu":
				CreateIntField (menu, fieldName);
				break;
			case "mipmap":
				CreateIntField (mipmaps, fieldName);
				break;
			case "raw":
				CreateIntField (raw, fieldName);
				break;
			case "plurals":
				CreateIntField (plurals, fieldName);
				break;
			case "string":
				CreateIntField (strings, fieldName);
				break;
			case "enum":
			case "flag":
			case "id":
				CreateIntField (ids, fieldName);
				break;
			case "integer-array":
			case "string-array":
				CreateIntField (arrays, fieldName);
				break;
			case "configVarying":
			case "add-resource":
			case "declare-styleable":
				ProcessStyleable (element);
				break;
			case "style":
				CreateIntField (style, fieldName.Replace (".", "_"));
				break;
			case "xml":
				CreateIntField (xml, fieldName);
				break;
			default:
				break;
			}
		}

		void ProcessStyleable (XmlReader reader)
		{
			string topName = null;
			int fieldCount = 0;
			while (reader.Read ()) {
				if (reader.NodeType == XmlNodeType.Whitespace || reader.NodeType == XmlNodeType.Comment)
					continue;
				string name = null;
				if (string.IsNullOrEmpty (topName)) {
					if (reader.HasAttributes) {
						while (reader.MoveToNextAttribute ()) {
							if (reader.Name.Replace ("android:", "") == "name")
								topName = reader.Value;
						}
					}
				}
				if (!reader.IsStartElement () || reader.LocalName == "declare-styleable")
					continue;
				if (reader.HasAttributes) {
					while (reader.MoveToNextAttribute ()) {
						if (reader.Name.Replace ("android:", "") == "name")
							name = reader.Value;
					}
				}
				reader.MoveToElement ();
				if (reader.LocalName == "attr") {
					CreateIntField (styleable, $"{topName}_{name.Replace (":", "_")}");
					if (!name.StartsWith ("android:", StringComparison.OrdinalIgnoreCase))
						CreateIntField (attrib, name);
					fieldCount++;
				} else {
					if (name != null)
						CreateIntField (ids, $"{name.Replace (":", "_")}");
				}
			}
			CreateIntArrayField (styleable, topName, fieldCount);
		}

		void ProcessXmlFile (string file)
		{
			using (var reader = XmlReader.Create (file)) {
				while (reader.Read ()) {
					if (reader.NodeType == XmlNodeType.Whitespace || reader.NodeType == XmlNodeType.Comment)
						continue;
					if (reader.IsStartElement ()) {
						var elementName = reader.Name;
						if (reader.HasAttributes) {
							string name = null;
							string type = null;
							string id = null;
							while (reader.MoveToNextAttribute ()) {
								if (reader.LocalName == "name")
									name = reader.Value;
								if (reader.LocalName == "type")
									type = reader.Value;
								if (reader.LocalName == "id")
									id = reader.Value.Replace ("@+id/", "").Replace ("@id/", ""); ;
							}
							if (name?.Contains ("android:") ?? false)
								continue;
							if (id?.Contains ("android:") ?? false)
								continue;
							// Move the reader back to the element node.
							reader.MoveToElement ();
							if (!string.IsNullOrEmpty (name))
								CreateResourceField (type ?? elementName, name, reader.ReadSubtree ());
							if (!string.IsNullOrEmpty (id)) {
								CreateIntField (ids, id);
							}
						}
					}
				}
			}
		}
	}
}
