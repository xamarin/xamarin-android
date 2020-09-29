using System;

using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;

namespace UnnamedProject
{
	[Register("unnamedproject.unnamedproject.MainActivity"), Activity(Label = "UnnamedProject", MainLauncher = true, Icon = "@drawable/icon")]
	public class MainActivity : Activity
	{
		int count = 1;

		protected override void OnCreate(Bundle bundle)
		{
			base.OnCreate(bundle);

			// Set our view from the "main" layout resource
			SetContentView(Resource.Layout.Main);

			// Get our button from the layout resource,
			// and attach an event to it
			Button button = FindViewById<Button>(Resource.Id.myButton);

			button.Click += delegate {
				button.Text = string.Format("{0} clicks!", count++);
			};

			string TAG = "XALINKERTESTS";

			// [Test] TryCreateInstanceOfSomeClass
			try
			{
				var asm = typeof(Library1.SomeClass).Assembly;
				var o = Activator.CreateInstance(asm.GetType("Library1.SomeClass"));
				Android.Util.Log.Info(TAG, $"[PASS] Able to create instance of '{o.GetType().Name}'.");
			}
			catch (Exception ex)
			{
				Android.Util.Log.Info(TAG, $"[FAIL] Unable to create instance of 'SomeClass'.\n{ex}");
			}

			// [Test] TryCreateInstanceOfXmlPreservedLinkerClass
			try
			{
				var asm = typeof(Library1.SomeClass).Assembly;
				var o = Activator.CreateInstance(asm.GetType("Library1.LinkerClass"));
				Android.Util.Log.Info(TAG, $"[PASS] Able to create instance of '{o.GetType().Name}'.");
			}
			catch (Exception ex)
			{
				Android.Util.Log.Info(TAG, $"[FAIL] Unable to create instance of 'LinkerClass'.\n{ex}");
			}

			// [Test] TryAccessXmlPreservedMethodOfLinkerClass
			try
			{
				var asm = typeof(Library1.SomeClass).Assembly;
				var t = asm.GetType("Library1.LinkerClass");
				var m = t.GetMethod("WasThisMethodPreserved");
				Android.Util.Log.Info(TAG, $"[PASS] Able to locate method '{m.Name}'.");
			}
			catch (Exception ex)
			{
				Android.Util.Log.Info(TAG, $"[FAIL] Unable to access 'WasThisMethodPreserved ()' method of 'LinkerClass'.\n{ex}");
			}

			// [Test] TryAccessAttributePreservedMethodOfLinkerClass
			try
			{
				var asm = typeof(Library1.SomeClass).Assembly;
				var t = asm.GetType("Library1.LinkerClass");
				var m = t.GetMethod("PreserveAttribMethod");
				Android.Util.Log.Info(TAG, $"[PASS] Able to locate method '{m.Name}'.");
			}
			catch (Exception ex)
			{
				Android.Util.Log.Info(TAG, $"[FAIL] Unable to access 'PreserveAttribMethod ()' method of 'LinkerClass'.\n{ex}");
			}

			// [Test] TryAccessXmlPreservedFieldOfLinkerClass
			try
			{
				var asm = typeof(Library1.SomeClass).Assembly;
				var t = asm.GetType("Library1.LinkerClass");
				var m = t.GetProperty("IsPreserved");
				Android.Util.Log.Info(TAG, $"[PASS] Able to locate field '{m.Name}'.");
			}
			catch (Exception ex)
			{
				Android.Util.Log.Info(TAG, $"[FAIL] Unable to access 'IsPreserved' field of 'LinkerClass'.\n{ex}");
			}

			// [Test] TryCreateInstanceOfNonXmlPreservedClass
			try
			{
				var asm = typeof(Library1.SomeClass).Assembly;
				var o = Activator.CreateInstance(asm.GetType("Library1.NonPreserved"));
				Android.Util.Log.Info(TAG, $"[LINKALLFAIL] Able to create instance of '{o.GetType().Name}' which should have been linked away.");
			}
			catch (Exception ex)
			{
				Android.Util.Log.Info(TAG, $"[LINKALLPASS] Unable to create instance of 'NonPreserved' as expected.\n{ex}");
			}

			// [Test] TryAccessNonXmlPreservedMethodOfLinkerModeFullClass
			try
			{
				var asm = typeof(Library1.SomeClass).Assembly;
				var t = asm.GetType("Library1.LinkModeFullClass");
				var m = t.GetMethod("ThisMethodShouldNotBePreserved");
				Android.Util.Log.Info(TAG, $"[LINKALLFAIL] Able to locate method that should have been linked: '{m.Name}'.");
			}
			catch (NullReferenceException ex)
			{
				Android.Util.Log.Info(TAG, $"[LINKALLPASS] Was unable to access 'ThisMethodShouldNotBePreserved ()' method of 'LinkerClass' as expected.\n{ex}");
			}

			Android.Util.Log.Info(TAG, LinkTestLib.Bug21578.MulticastOption_ShouldNotBeStripped());
			Android.Util.Log.Info(TAG, LinkTestLib.Bug21578.MulticastOption_ShouldNotBeStripped2());
			Android.Util.Log.Info(TAG, LinkTestLib.Bug35195.AttemptCreateTable());
			Android.Util.Log.Info(TAG, LinkTestLib.Bug36250.SerializeSearchRequestWithDictionary());

			Android.Util.Log.Info(TAG, "All regression tests completed.");

		}
	}
}
