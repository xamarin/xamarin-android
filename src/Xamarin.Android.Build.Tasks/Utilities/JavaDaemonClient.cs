using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace Xamarin.Android.Tasks
{
	public class JavaDaemonClient : IDisposable
	{
		const int Timeout = 3000;

		readonly XmlWriterSettings settings = new XmlWriterSettings {
			OmitXmlDeclaration = true,
		};
		Process process;

		public bool IsConnected => process != null && !process.HasExited;

		public int? ProcessId => process?.Id;

		/// <summary>
		/// A callback for logging purposes
		/// </summary>
		public Action<string> Log { get; set; }

		/// <summary>
		/// Starts the java daemon process
		/// </summary>
		/// <param name="fileName">Full path to java/java.exe</param>
		/// <param name="arguments">Command-line arguments for java</param>
		public void Connect (string fileName, string arguments)
		{
			if (IsConnected)
				return;

			Log?.Invoke ($"Starting java daemon: {fileName} {arguments}");

			var info = new ProcessStartInfo (fileName, arguments) {
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};
			process = Process.Start (info);
		}

		/// <summary>
		/// Using the connected Java daemon, invokes the Main method on a specific Java class in a jar file
		/// </summary>
		/// <param name="className">Java class name, including package name</param>
		/// <param name="jar">Full path to the jar file on disk</param>
		/// <param name="arguments">additional arguments to the command</param>
		/// <returns></returns>
		public (int exitCode, string stdout, string stderr) Invoke (string className, string jar, string arguments)
		{
			if (!IsConnected)
				throw new InvalidOperationException ("Not connected to Java daemon");

			Write (new Request {
				ClassName = className,
				Jar = jar,
				Arguments = arguments,
			});

			var response = Read ();
			return (response.ExitCode, response.StandardOutput, response.StandardError);
		}

		void Write (Request request)
		{
			var builder = new StringBuilder ();
			using (var xml = XmlWriter.Create (builder, settings)) {
				xml.WriteStartElement ("Java");
				if (request.Exit)
					xml.WriteAttributeString (nameof (request.Exit), bool.TrueString);
				if (!string.IsNullOrEmpty (request.ClassName))
					xml.WriteAttributeString (nameof (request.ClassName), request.ClassName);
				if (!string.IsNullOrEmpty (request.Jar))
					xml.WriteAttributeString (nameof (request.Jar), request.Jar);
				if (!string.IsNullOrEmpty (request.Arguments))
					xml.WriteAttributeString (nameof (request.Arguments), request.Arguments);
				xml.WriteEndElement ();
			}

			string text = builder.ToString ();
			Log?.Invoke ("Send: " + text);
			process.StandardInput.WriteLine (text);
		}

		Response Read ()
		{
			if (process.StandardOutput.EndOfStream) {
				string stderr = null;
				try {
					//Try to read from stderror if something goes wrong here
					stderr = process.StandardError.ReadToEnd ();
				} catch {
				}
				if (string.IsNullOrEmpty (stderr)) {
					throw new InvalidOperationException ($"Reached the end of the StandardOutput!");
				} else {
					throw new InvalidOperationException ($"Reached the end of the StandardOutput! stderr: {stderr}");
				}
			}

			string text = process.StandardOutput.ReadLine ();
			Log?.Invoke ("Receive: " + text);

			var response = new Response ();
			using (var str = new StringReader (text))
			using (var xml = XmlReader.Create (str)) {
				if (xml.Read () && xml.NodeType == XmlNodeType.Element && xml.Name == "Java") {
					while (xml.MoveToNextAttribute ()) {
						switch (xml.Name) {
							case nameof (Response.ExitCode):
								response.ExitCode = int.Parse (xml.Value, CultureInfo.InvariantCulture);
								break;
							case nameof (Response.StandardOutput):
								response.StandardOutput = xml.Value;
								break;
							case nameof (Response.StandardError):
								response.StandardError = xml.Value;
								break;
							default:
								break;
						}
					}
				}
			}
			return response;
		}

		/// <summary>
		/// A class for writing the request
		/// </summary>
		class Request
		{
			public bool Exit { get; set; }
			public string ClassName { get; set; }
			public string Jar { get; set; }
			public string Arguments { get; set; }
		}

		/// <summary>
		/// A class for parsing the response
		/// </summary>
		class Response
		{
			public int ExitCode { get; set; }

			public string StandardOutput { get; set; } = "";

			public string StandardError { get; set; } = "";
		}

		public void Dispose ()
		{
			if (process == null)
				return;

			if (process.HasExited) {
				process.Dispose ();
				process = null;
				return;
			}

			Write (new Request { Exit = true });

			if (!process.WaitForExit (Timeout)) {
				process.Kill ();
			}
			process.Dispose ();
			process = null;
		}
	}
}
