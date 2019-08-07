using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Xamarin.ProjectTools;

namespace Xamarin.Android.Build.Tests
{
	public class DeviceTest: BaseTest
	{
		protected static void RunAdbInput (string command, params object [] args)
		{
			RunAdbCommand ($"shell {command} {string.Join (" ", args)}");
		}

		protected static string ClearAdbLogcat ()
		{
			return RunAdbCommand ("logcat -c");
		}

		protected static string ClearDebugProperty ()
		{
			return RunAdbCommand ("shell setprop debug.mono.extra \"\"");
		}

		protected static void AdbStartActivity (string activity)
		{
			RunAdbCommand ($"shell am start -S -n \"{activity}\"");
		}

		protected void WaitFor (TimeSpan timeSpan, Func<bool> func)
		{
			var pause = new ManualResetEvent (false);
			TimeSpan total = timeSpan;
			TimeSpan interval = TimeSpan.FromMilliseconds (10);
			while (total.TotalMilliseconds > 0) {
				pause.WaitOne (interval);
				total = total.Subtract (interval);
				if (func ()) {
					break;
				}
			}
		}

		protected static bool MonitorAdbLogcat (Func<string, bool> action, string logcatFilePath, int timeout = 15)
		{
			string ext = Environment.OSVersion.Platform != PlatformID.Unix ? ".exe" : "";
			string adb = Path.Combine (AndroidSdkPath, "platform-tools", "adb" + ext);
			var info = new ProcessStartInfo (adb, "logcat") {
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
			};

			bool didActionSucceed = false;
			using (var proc = Process.Start (info)) {
				var sb = new StringBuilder ();

				proc.OutputDataReceived += (sender, e) => {
					if (e.Data != null) {
						sb.AppendLine (e.Data);
						if (action (e.Data)) {
							didActionSucceed = true;
							proc.Kill ();
						}
					}
				};

				proc.BeginOutputReadLine ();
				if (!proc.WaitForExit (timeout * 1000)) {
					proc.Kill ();
				}

				File.WriteAllText (logcatFilePath, sb.ToString ());
				return didActionSucceed;
			}
		}

		protected static bool WaitForDebuggerToStart (string logcatFilePath, int timeout = 60)
		{
			bool result = MonitorAdbLogcat ((line) => {
				return line.IndexOf ("monodroid-debug: Trying to initialize the debugger with options", StringComparison.OrdinalIgnoreCase) > 0;
			}, logcatFilePath, timeout);
			return result;
		}

		protected static bool WaitForActivityToStart (string activityNamespace, string activityName, string logcatFilePath, int timeout = 60)
		{
			bool result = MonitorAdbLogcat ((line) => {
				var idx1 = line.IndexOf ("ActivityManager: Displayed", StringComparison.OrdinalIgnoreCase);
				var idx2 = idx1 > 0 ? 0 : line.IndexOf ("ActivityTaskManager: Displayed", StringComparison.OrdinalIgnoreCase);
				return (idx1 > 0 || idx2 > 0) && line.Contains (activityNamespace) && line.Contains (activityName);
			}, logcatFilePath, timeout);
			return result;
		}

		protected static (int x, int y, int w, int h) GetControlBounds (string packageName, string uiElement, string text)
		{
			var regex = new Regex (@"[(0-9)]\d*", RegexOptions.Compiled);
			var result = (x: 0, y: 0, w: 0, h: 0);
			var ui = RunAdbCommand ("exec-out uiautomator dump /dev/tty");
			while (ui.Contains ("ERROR:")) {
				ui = RunAdbCommand ("exec-out uiautomator dump /dev/tty");
				WaitFor (1);
			}
			ui = ui.Replace ("UI hierchary dumped to: /dev/tty", string.Empty).Trim ();
			try {
				var uiDoc = XDocument.Parse (ui);
				var node = uiDoc.XPathSelectElement ($"//node[contains(@resource-id,'{uiElement}')]");
				if (node == null)
					node = uiDoc.XPathSelectElement ($"//node[contains(@content-desc,'{uiElement}')]");
				if (node == null)
					node = uiDoc.XPathSelectElement ($"//node[contains(@text,'{text}')]");
				if (node == null)
					return result;
				var bounds = node.Attribute ("bounds");
				var matches = regex.Matches (bounds.Value);
				int.TryParse (matches [0].Value, out int x);
				int.TryParse (matches [1].Value, out int y);
				int.TryParse (matches [2].Value, out int w);
				int.TryParse (matches [3].Value, out int h);
				return (x: x, y: y, w: w, h: h);
			} catch (Exception ex) {
				// Ignore any error and return and empty
				throw new InvalidOperationException ($"uiautomator returned invalid xml {ui}", ex);
			}
		}

		protected static void ClickButton (string packageName, string buttonName, string buttonText)
		{
			var bounds = GetControlBounds (packageName, buttonName, buttonText);
			RunAdbInput ("input tap", bounds.x + ((bounds.w - bounds.x) / 2), bounds.y + ((bounds.h - bounds.y) / 2));
		}
    }
}