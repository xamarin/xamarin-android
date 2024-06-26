using System.Linq;
using System.Text;

namespace Xamarin.ProjectTools
{
	static class XmlUtils
	{
		public static string ToXml (IShortFormProject project)
		{
			var sb = new StringBuilder ();
			foreach (var pg in project.PropertyGroups) {
				if (pg.Properties.Count == 0)
					continue;
				if (string.IsNullOrEmpty (pg.Condition)) {
					sb.AppendLine ("\t<PropertyGroup>");
				} else {
					sb.AppendLine ($"\t<PropertyGroup Condition=\"{pg.Condition}\">");
				}
				foreach (var p in pg.Properties) {
					var conditon = string.IsNullOrEmpty (p.Condition) ? "" : $" Condition=\"{p.Condition}\"";
					sb.AppendLine ($"\t\t<{p.Name}{conditon}>{p.Value ()}</{p.Name}>");
				}
				sb.AppendLine ("\t</PropertyGroup>");
			}
			if (project.PackageReferences.Count > 0) {
				sb.AppendLine ("\t<ItemGroup>");
				foreach (var pr in project.PackageReferences) {
					sb.AppendLine ($"\t\t<PackageReference Include=\"{pr.Id}\" Version=\"{pr.Version}\"/>");
				}
				sb.AppendLine ("\t</ItemGroup>");
			}
			if (project.EnableDefaultItems) {
				// If $(EnableDefaultItems), then only OtherBuildItems (excluding EmbeddedResource) and References are added
				if (project.References.Count > 0) {
					sb.AppendLine ("\t<ItemGroup>");
					foreach (var reference in project.References) {
						AppendBuildItem (sb, reference);
					}
					sb.AppendLine ("\t</ItemGroup>");
				}
				if (project.OtherBuildItems.Count > 0) {
					sb.AppendLine ("\t<ItemGroup>");
					foreach (var bi in project.OtherBuildItems) {
						// If its an EmbeddedResource ignore it, unless it has an Update method set.
						if (bi.BuildAction != BuildActions.EmbeddedResource || bi.Update != null) {
							AppendBuildItem (sb, bi);
						}
					}
					sb.AppendLine ("\t</ItemGroup>");
				}
			} else if (project.ItemGroupList.Count > 0) {
				foreach (var itemGroup in project.ItemGroupList) {
					if (itemGroup.Count == 0)
						continue;
					sb.AppendLine ("\t<ItemGroup>");
					foreach (var bi in itemGroup) {
						AppendBuildItem (sb, bi);
					}
					sb.AppendLine ("\t</ItemGroup>");
				}
			}
			foreach (var import in project.Imports) {
				var projectName = import.Project ();
				if (projectName != "Directory.Build.props" && projectName != "Directory.Build.targets")
					sb.AppendLine ($"\t<Import Project=\"{projectName}\" />");
			}
			return $"<Project Sdk=\"{project.Sdk}\">\r\n{sb}\r\n</Project>";
		}

		static void AppendBuildItem (StringBuilder sb, BuildItem bi)
		{
			if (bi.Deleted)
				return;
			sb.Append ($"\t\t<{bi.BuildAction} ");
			if (bi.Include != null) sb.Append ($"Include=\"{bi.Include ()}\" ");
			if (bi.Update != null) sb.Append ($"Update=\"{bi.Update ()}\" ");
			if (bi.Remove != null) sb.Append ($"Remove=\"{bi.Remove ()}\" ");
			if (bi.Generator != null) sb.Append ($"Generator=\"{bi.Generator ()}\" ");
			if (bi.DependentUpon != null) sb.Append ($"DependentUpon=\"{bi.DependentUpon ()}\" ");
			if (bi.Version != null) sb.Append ($"Version=\"{bi.Version ()}\" ");
			if (bi.SubType != null) sb.Append ($"SubType=\"{bi.SubType ()}\" ");
			if (!bi.Metadata.Any ()) {
				sb.AppendLine ($"\t\t/>");
			} else {
				sb.AppendLine ($">");
				foreach (var kvp in bi.Metadata) {
					sb.AppendLine ($"\t\t\t<{kvp.Key}>{kvp.Value}</{kvp.Key}>");
				}
				sb.AppendLine ($"\t\t</{bi.BuildAction}>");
			}
		}
	}
}
