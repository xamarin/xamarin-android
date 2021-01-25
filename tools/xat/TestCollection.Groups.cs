using System;
using System.IO;
using System.Collections.Generic;

using Xamarin.Android.Prepare;

namespace Xamarin.Android.Tests
{
	enum GroupNick
	{
		/// <summary>
		///  Unit: On Device macOS
		/// </summary>
		UnitOnDeviceMacOS,

		/// <summary>
		///  Unit: MSBuild macOS Smoke Tests
		/// </summary>
		UnitSmokeTests,

		/// <summary>
		///  Unit: No Node
		/// </summary>
		UnitNoNode,

		/// <summary>
		///  Unit: MSBuild Extra Tests No Node
		/// </summary>
		UnitMSBuildExtraNoNode,

		/// <summary>
		///  Unit: TimeZone Node-*
		/// </summary>
		UnitTimeZoneOnNode,

		/// <summary>
		///  Unit: MSBuild Legacy - macOS Node-*
		/// </summary>
		UnitMSBuildLegacyMacOSOnNode,

		/// <summary>
		///  Unit: Java.Interop
		/// </summary>
		UnitJavaInterop,

		/// <summary>
		///  All APK tests
		/// </summary>
		APK,
	}

	partial class TestCollection
	{
		void CreateTestGroups ()
		{
			var group = new TestGroup ("Unit: On Device macOS", GroupNick.UnitOnDeviceMacOS) {
				ExcludeCategories = {
					"TimezoneInfo",
					"SmokeTests",
				},
			};
			AddGroup (group);

			group = new TestGroup ($"Unit: MSBuild Smoke Tests", GroupNick.UnitSmokeTests) {
				IncludeCategories = {
					"SmokeTests",
				},
			};
			AddGroup (group);

			group = new TestGroup ($"Unit: Java.Interop", GroupNick.UnitJavaInterop);
			AddGroup (group);

			group = new TestGroup ($"APK: all tests", GroupNick.APK);
			AddGroup (group);

			string configuration = Context.Instance.Configuration;
			var noNodeGroup = new TestGroup ("Unit: No Node", GroupNick.UnitNoNode);
			AddGroup (noNodeGroup);

			var msbuildExtraTests = new TestGroup ("Unit: MSBuild Extra Tests No Node", GroupNick.UnitMSBuildExtraNoNode) {
				ResultFilePath = Path.Combine (outputPath, $"TestResult-MSBuildTests-{OS.Name}-NoNode-{configuration}.xml"),
			};
			AddGroup (msbuildExtraTests);

			for (int n = 1; n <= NumberOfTestNodes; n++) {
				noNodeGroup.ExcludeCategories.Add (MakeNodeID (n));
				msbuildExtraTests.ExcludeCategories.Add (MakeNodeID (n));

				group = new TestGroup ($"Unit: TimeZone Node {n}", GroupNick.UnitTimeZoneOnNode) {
					Tests = {
						$"Xamarin.Android.Build.Tests.DeploymentTest.CheckTimeZoneInfoIsCorrectNode{n}",
					},

					ResultFilePath = Path.Combine (outputPath, $"TestResult-TimeZoneInfoTests-Node{n}-{configuration}.xml"),
				};
				AddGroup (group);

				group = new TestGroup ($"Unit: MSBuild Legacy - macOS Node {n}", GroupNick.UnitMSBuildLegacyMacOSOnNode) {
					IncludeCategories = {
						MakeNodeID (n),
					},

					ExcludeCategories = {
						"SmokeTests",
					},

					// Name: mac_msbuild_tests_{n}
					ResultFilePath = Path.Combine (outputPath, $"TestResult-MSBuildTests-mac_msbuild_tests_{n}-{configuration}.xml"),
				};
				AddGroup (group);
			}
		}

		static string MakeNodeID (int node)
		{
			return $"Node-{node}";
		}

		void AddGroup (TestGroup group)
		{
			if (GroupsByName.ContainsKey (group.Name)) {
				throw new InvalidOperationException ($"Duplicate group name '{group.Name}'");
			}

			if (GroupsByID.ContainsKey (group.ID)) {
				throw new InvalidOperationException ($"Duplicate group ID '{group.ID}'");
			}

			GroupsByName.Add (group.Name, group);
			GroupsByID.Add (group.ID, group);
		}

		void AddToGroup (XATest test, GroupNick nick, string? resultFileName = null)
		{
			foreach (TestGroup group in GroupsByName.Values) {
				if (group.Nick != nick) {
					continue;
				}

				group.AddSuite (test, resultFileName);
			}
		}
	}
}
