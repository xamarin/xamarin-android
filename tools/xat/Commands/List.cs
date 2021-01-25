using System;
using System.Threading.Tasks;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class List : XatCommand
	{
		ListItem whatToList;
		bool verbose;

		public List (ListItem whatToList, bool verbose)
		{
			this.whatToList = whatToList;
			this.verbose = verbose;
		}

#pragma warning disable 1998
		public override async Task<bool> Invoke ()
		{
			if (whatToList.HasFlag (ListItem.Suites)) {
				ListSuites ();
			}

			if (whatToList.HasFlag (ListItem.Groups)) {
				if (whatToList.HasFlag (ListItem.Suites)) {
					Log.MessageLine ();
				}
				ListGroups ();
			}

			return true;
		}
#pragma warning restore 1998

		void ListSuites ()
		{
			Log.MessageLine ("Suites:");
			foreach (var kvp in Context.Instance.Tests.AllSuitesByName) {
				XATest suite = kvp.Value;
				PrintSuiteInfo (" ", suite);
			}
		}

		void PrintSuiteInfo (string indent, XATest suite)
		{
			Log.Message ($"{indent}{Context.Characters.Bullet} ");
			Log.Message (suite.ID, ConsoleColor.Cyan);
			Log.Message ($" ({suite.KindName}", ConsoleColor.White);
			Log.MessageLine ($": '{suite.Name}')");
		}

		void ListGroups ()
		{
			TestCollection tests = Context.Instance.Tests;
			string indent = " ";
			string verboseIndent = $"{indent}  ";
			bool first = true;

			Log.MessageLine ("Groups:");
			foreach (var kvp in tests.GroupsByName) {
				string groupName = kvp.Key;
				TestGroup group = kvp.Value;

				if (verbose) {
					if (first) {
						first = false;
					} else {
						Log.MessageLine ();
					}
				}

				Log.Message ($"{indent}{Context.Characters.Bullet} ");
				Log.Message (group.ID, ConsoleColor.Cyan);
				Log.MessageLine ($": '{group.Name}'");
				if (!verbose) {
					continue;
				}

				foreach (string suiteID in group.SuitesByID.Keys) {
					if (!tests.AllSuitesByID.TryGetValue (suiteID, out XATest suite)) {
						throw new InvalidOperationException ($"Unknown suite id '{suiteID}'");
					}

					PrintSuiteInfo (verboseIndent, suite);
				}
			}
		}
	}
}
