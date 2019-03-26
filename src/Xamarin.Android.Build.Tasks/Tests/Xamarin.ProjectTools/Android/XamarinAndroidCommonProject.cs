using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Construction;
using System.Drawing;

namespace Xamarin.ProjectTools
{
	public abstract class XamarinAndroidCommonProject : XamarinAndroidProject
	{
		public IList<BuildItem> AndroidResources { get; private set; }

		public static readonly byte[] icon_binary_mdpi;
		public static readonly byte[] icon_binary_hdpi;
		public static readonly byte[] icon_binary_xhdpi;
		public static readonly byte[] icon_binary_xxhdpi;
		public static readonly byte[] icon_binary_xxxhdpi;

		BuildItem.Source resourceDesigner;

		static byte[] ScaleIcon (Image image, int width, int height)
		{
			float scale = Math.Min (width / image.Width, height / image.Height);
			using (var bmp = new Bitmap ((int)width, (int)height)) {
				using (var graphics = Graphics.FromImage (bmp)) {
					var scaleWidth = (int)(image.Width * scale);
					var scaleHeight = (int)(image.Height * scale);
					using (var brush = new SolidBrush (Color.Transparent)) {
						graphics.FillRectangle (brush, new RectangleF (0, 0, width, height));
						graphics.DrawImage (image, new Rectangle (((int)width - scaleWidth) / 2, ((int)height - scaleHeight) / 2, scaleWidth, scaleHeight));
						using (var ms = new MemoryStream ()) {
							bmp.Save (ms, System.Drawing.Imaging.ImageFormat.Png);
							return ms.ToArray ();
						}
					}
				}
			}
		}

		static XamarinAndroidCommonProject ()
		{
			var stream = typeof(XamarinAndroidCommonProject).Assembly.GetManifestResourceStream ("Xamarin.ProjectTools.Resources.Base.Icon.png");
			icon_binary_mdpi = new byte [stream.Length];
			stream.Read (icon_binary_mdpi, 0, (int) stream.Length);

			stream.Position = 0;
			using (var icon = Bitmap.FromStream (stream)) {
				icon_binary_hdpi = ScaleIcon (icon, 72, 72);
				icon_binary_xhdpi = ScaleIcon (icon, 96, 96);
				icon_binary_xxhdpi = ScaleIcon (icon, 144, 144);
				icon_binary_xxxhdpi = ScaleIcon (icon, 192, 192);
			}
		}

		protected XamarinAndroidCommonProject (string debugConfigurationName = "Debug", string releaseConfigurationName = "Release")
			: base (debugConfigurationName, releaseConfigurationName)
		{
			AndroidResources = new List<BuildItem> ();
			ItemGroupList.Add (AndroidResources);
			resourceDesigner = new BuildItem.Source (() => "Resources\\Resource.designer" + Language.DefaultDesignerExtension) { TextContent = () => string.Empty };
			Sources.Add (resourceDesigner);
			AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\drawable-mdpi\\Icon.png") { BinaryContent = () => icon_binary_mdpi });
			AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\drawable-hdpi\\Icon.png") { BinaryContent = () => icon_binary_hdpi });
			AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\drawable-xhdpi\\Icon.png") { BinaryContent = () => icon_binary_xhdpi });
			AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\drawable-xxhdpi\\Icon.png") { BinaryContent = () => icon_binary_xxhdpi });
			AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\drawable-xxxhdpi\\Icon.png") { BinaryContent = () => icon_binary_xxxhdpi });
			//AndroidResources.Add (new AndroidItem.AndroidResource ("Resources\\drawable-nodpi\\Icon.png") { BinaryContent = () => icon_binary });
		}
		
		public override string ProjectTypeGuid {
			get { return "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC"; }
		}

		public override ProjectRootElement Construct ()
		{
			var root = base.Construct ();
			foreach (var import in ImportsPrepended)
				root.AddImport (import.Project ());
			root.AddImport (XamarinAndroidLanguage.NormalProjectImport);
			foreach (var import in ImportsAppended)
				root.AddImport (import.Project ());
			return root;
		}

		public override ProjectLanguage Language {
			get {
				return base.Language;
			}
			set {
				base.Language = value;
				if (value == XamarinAndroidProjectLanguage.FSharp) {
					// add the stuff needed for FSharp
					PackageReferences.Add (KnownPackages.FSharp_Core_Latest);
					PackageReferences.Add (KnownPackages.Xamarin_Android_FSharp_ResourceProvider_Runtime);
					Sources.Remove (resourceDesigner);
					OtherBuildItems.Add (new BuildItem.NoActionResource (() => "Resources\\Resource.designer" + Language.DefaultDesignerExtension) { TextContent = () => string.Empty });
				}
			}
		}
	}
}
