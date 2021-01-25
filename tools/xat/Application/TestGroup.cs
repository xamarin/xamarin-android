using System;
using System.Collections.Generic;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	class TestGroup : AppObject
	{
		HashSet<string> addedIDs = new HashSet<string> (StringComparer.Ordinal);

		/// <summary>
		///  Unique group name.
		/// </summary>
		public string Name                           { get; }

		/// <summary>
		///  ID generated from <see cref="Name"/>
		/// </summary>
		public string ID                             { get; }

		/// <summary>
		///   Whether tests suites in this group require a fresh Android emulator to be created before they are
		///   executed.
		/// </summary>
		public bool CreateNewEmulator                { get; set; }

		/// <summary>
		///   Additional options to pass to ADB
		/// </summary>
		public string AdbOptions                     { get; set; } = String.Empty;

		/// <summary>
		///   Additional options to pass to <c>nunit-console</c>
		/// </summary>
		public string NUnitOptions                   { get; set; } = String.Empty;

		/// <summary>
		///   Path (or name of the executable) to use to build suites in this group.  If omitted, XAT will first try to
		///   find <c>xabuild</c>, falling back to <c>msbuild</c>
		/// </summary>
		public string MSBuildPath                    { get; set; } = String.Empty;

		/// <summary>
		///   Additional options to pass to <c>dotnet test</c>
		/// </summary>
		public string DotnetTestOptions              { get; set; } = String.Empty; // TODO: implement DOtnetTestOptions

		/// <summary>
		///   Comma-separated list of specific tests to run in the test suites gathered in this group.
		/// </summary>
		public List<string> Tests                    { get; set; } = new List<string> ();

		/// <summary>
		///   Comma-separated list of additional specific tests to be included in the execution of test suites gathered
		///   in this group.
		/// </summary>
		public List<string> IncludeTests             { get; set; } = new List<string> ();

		/// <summary>
		///   Comma-separated list of additional specific tests to be excluded from the execution of test suites gathered
		///   in this group.
		/// </summary>
		public List<string> ExcludeTests             { get; set; } = new List<string> ();

		/// <summary>
		///   Comma-separated list of test categories to be included in the execution of test suites gathered
		///   in this group.
		/// </summary>
		public List<string> IncludeCategories        { get; set; } = new List<string> ();

		/// <summary>
		///   Comma-separated list of test categories to be excluded from the execution of test suites gathered
		///   in this group.
		/// </summary>
		public List<string> ExcludeCategories        { get; set; } = new List<string> ();

		/// <summary>
		///   IDs (<see cref="XATest.ID"/>) of test suites that are gathered in this group.  Do not add entries directly
		///   to this property, use <see cref="AddSuite"/> overloads to do that instead (they take care of ignoring
		///   duplicate IDs).  Suite ID is the dictionary key, optional value specifies this suites override for the
		///   result file path (<see cref="AddSuite"/>, <see cref="ResultFilePath"/>)
		/// </summary>
		public IDictionary<string, string?> SuitesByID { get; set; } = new SortedDictionary<string, string?> (StringComparer.Ordinal);

		/// <summary>
		///   Optional path to the results file.  Overrides the results path specified in the test suite itself for all
		///   the suites added to this group. See <see cref="AddSuite"/> for a way to override the result file path just
		///   for a single suite.
		/// </summary>
		public string ResultFilePath                 { get; set; } = String.Empty;

		/// <summary>
		///   A "nickname" for a group to make it easier to add tests to a group (or a set of groups).  The nickname can
		///   be shared among several groups (e.g. those which divide tests to run on separate nodes)
		/// </summary>
		public GroupNick Nick { get; }

		public TestGroup (string name, GroupNick nick)
		{
			Name = EnsureParameterValue (nameof (name), name);
			ID = Utilities.MakeID (name);
			Nick = nick;
		}

		/// <summary>
		///   Add a test suite with the specified <paramref name="id"/> to this group, specifying a group specific
		///   result file override for this test, if <paramref name="resultFileName"/> is not null.  Note that <paramref
		///   name="id"/> MUST correspond to an existing test suite.
		/// </summary>
		public void AddSuite (string id, string? resultFileName = null)
		{
			if (id.Length == 0 || addedIDs.Contains (id)) {
				return;
			}

			addedIDs.Add (id);
			SuitesByID.Add (id, resultFileName);
		}

		/// <summary>
		///   Add a test suite to this group, specifying a group specific result file override for this test, if
		///   <paramref name="resultFileName"/> is not null.
		/// </summary>
		public void AddSuite (XATest suite, string? resultFileName = null)
		{
			AddSuite (suite.ID, resultFileName);
		}
	}
}
