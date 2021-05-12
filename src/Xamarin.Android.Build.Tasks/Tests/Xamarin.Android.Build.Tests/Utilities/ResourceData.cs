using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	static class ResourceData
	{
		static Lazy<byte[]> javaSourceJarTestJar        = new Lazy<byte[]>(() => GetResourceData ("javasourcejartest.jar"));
		static Lazy<byte[]> javaSourceJarTestSourcesJar = new Lazy<byte[]>(() => GetResourceData ("javasourcejartest-sources.jar"));
		static Lazy<byte[]> javaSourceJarTestJavadocJar = new Lazy<byte[]>(() => GetResourceData ("javasourcejartest-javadoc.jar"));
		static Lazy<byte []> library1Aar = new Lazy<byte []> (() => GetResourceData ("library1.aar"));
		static Lazy<byte []> library2Aar = new Lazy<byte []> (() => GetResourceData ("library2.aar"));
		static Lazy<byte []> javaSourceTestExtension = new Lazy<byte []> (() => GetResourceData ("JavaSourceTestExtension.java"));

		public  static  byte[]  JavaSourceJarTestJar            => javaSourceJarTestJar.Value;
		public  static  byte[]  JavaSourceJarTestSourcesJar     => javaSourceJarTestSourcesJar.Value;
		public  static  byte[]  JavaSourceJarTestJavadocJar     => javaSourceJarTestJavadocJar.Value;
		public  static  byte [] Library1Aar => library1Aar.Value;
		public  static  byte [] Library2Aar => library2Aar.Value;

		public  static  string JavaSourceTestExtension => Encoding.ASCII.GetString (javaSourceTestExtension.Value);

		static byte[] GetResourceData (string name)
		{
			using var s = typeof (ResourceData).Assembly.GetManifestResourceStream (name);
			using var m = new MemoryStream (checked ((int) s.Length));
			s.CopyTo (m);
			return m.ToArray ();
		}

		public static byte [] GetKeystore (string keyname = "test.keystore")
		{
			var assembly = typeof (XamarinAndroidCommonProject).Assembly;
			using (var stream = assembly.GetManifestResourceStream ($"Xamarin.ProjectTools.Resources.Base.{keyname}")) {
				var data = new byte [stream.Length];
				stream.Read (data, 0, (int) stream.Length);
				return data;
			}
		}
	}
}
