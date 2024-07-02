using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	static class InlineData
	{
		const string Resx = @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
	<resheader name=""resmimetype"">
		<value>text/microsoft-resx</value>
	</resheader>
	<resheader name=""version"">
		<value>2.0</value>
	</resheader>
	<resheader name=""reader"">
		<value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
	</resheader>
	<resheader name=""writer"">
		<value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
	</resheader>
	<!--contents-->
</root>";

		const string Designer = @"
using System;
using System.Reflection;

namespace @projectName@
{
	[System.CodeDom.Compiler.GeneratedCodeAttribute(""System.Resources.Tools.StronglyTypedResourceBuilder"", ""4.0.0.0"")]
	[System.Diagnostics.DebuggerNonUserCodeAttribute()]
	[System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
	@modifier@ class @className@ {

		private static System.Resources.ResourceManager resourceMan;

		private static System.Globalization.CultureInfo resourceCulture;

		[System.Diagnostics.CodeAnalysis.SuppressMessageAttribute(""Microsoft.Performance"", ""CA1811:AvoidUncalledPrivateCode"")]
		internal @className@() {
		}

		[System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Advanced)]
		internal static System.Resources.ResourceManager ResourceManager {
			get {
				if (object.Equals(null, resourceMan)) {
					System.Resources.ResourceManager temp = new System.Resources.ResourceManager(""@projectName@.@className@"", typeof(@className@).GetTypeInfo().Assembly);
					resourceMan = temp;
				}
				return resourceMan;
			}
		}

		[System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Advanced)]
		internal static System.Globalization.CultureInfo Culture {
			get {
				return resourceCulture;
			}
			set {
				resourceCulture = value;
			}
		}

		@content@
	}
}
";

		public static string ResxWithContents (string contents)
		{
			return Resx.Replace ("<!--contents-->", contents);
		}

		public static void AddCultureResourcesToProject (IShortFormProject proj, string filename, string dataName, CultureTypes types = CultureTypes.AllCultures)
		{
			foreach (var culture in CultureInfo.GetCultures (types)) {
				proj.OtherBuildItems.Add (new BuildItem ("EmbeddedResource", $"{filename}.{culture.Name}.resx") {
					TextContent = () => InlineData.ResxWithContents ($"<data name=\"{dataName}\"><value>{culture.Name}</value></data>")
				});
			}
		}

		public static string DesignerWithContents (string projectName, string className, string modifier, string[] dataNames)
		{
			var content = new StringBuilder ();
			foreach (string data in dataNames) {
				content.AppendFormat (CultureInfo.InvariantCulture, @"internal static string {0} {{
			get {{
				return ResourceManager.GetString(""{0}"", resourceCulture);
			}}
		}}" + Environment.NewLine, data);
			}
			return Designer.Replace ("@modifier@", modifier)
				.Replace ("@className@", className)
				.Replace ("@projectName@", projectName)
				.Replace ("@content@", content.ToString ());
		}

		public static void AddCultureResourceDesignerToProject (IShortFormProject proj, string projectName, string className, params string[] dataNames)
		{
			proj.OtherBuildItems.Add (new BuildItem.Source ($"{className}.Designer.cs") {
				TextContent = () => InlineData.DesignerWithContents (projectName, className, "internal", dataNames)
			});
		}
	}
}
