using System;
using NUnit.Framework;
using Xamarin.ProjectTools;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using System.Text;

namespace Xamarin.Android.Build.Tests
{
	[TestFixture]
	[Category ("Node-3")]
	[Parallelizable (ParallelScope.Children)]
	public class AidlTest : BaseTest
	{
		void TestAidl (string testName, string aidl)
		{
			var proj = new XamarinAndroidApplicationProject () {
				IsRelease = true,
			};
			proj.OtherBuildItems.Add (new BuildItem (AndroidBuildActions.AndroidInterfaceDescription, "Test.aidl") { TextContent = () => aidl });
			using (var p = CreateApkBuilder (testName)) {
				Assert.IsTrue (p.Build (proj), "Build should have succeeded.");
			}
		}

		[Test]
		public void ListAndMap ()
		{
			string aidl = @"
import android.os;
import android.view;
import android.animation;

interface Test {
	void arrayList(inout List list);
	void map(inout Map m);
}
";
			TestAidl ($"temp/AidlTest.{nameof (ListAndMap)}", aidl);
		}

		[Test]
		public void NamespaceResolution ()
		{
			string aidl = @"
import android.os;
import android.view;
import android.animation;

interface Test {
}
";
			TestAidl ("temp/AidlTest.NamespaceResolution", aidl);
		}

		[Test]
		public void PrimitiveTypes ()
		{
			string aidl = @"
package com.xamarin.test;
interface Test {
	void test0();
	int test1 ();
	// now those test methods are explicitly added 'inout' because otherwise Android SDK aidl rejects it...
	int test2 (inout int [] args);
	boolean test3 (inout boolean [] args);
	byte test4 (inout byte [] args);
	char test5 (inout char [] args);
	long test6 (inout long [] args);
	float test7 (inout float [] args);
	double test8 (inout double [] args);
	String test9 (inout String [] args);
	// thought that it's missing 'short' ? It's not supported - http://stackoverflow.com/questions/6742167/android-aidl-support-short-type
	
	void test10 (in byte [] args);
	void test11 (out byte [] args);
	void test12 (inout byte [] args);
	
	int [] test20 ();
	boolean [] test21 ();
	byte [] test22 ();
	String [] test23 ();
}
";
			TestAidl ("temp/AidlTest.PrimitiveTypes", aidl);
		}
	}
}
