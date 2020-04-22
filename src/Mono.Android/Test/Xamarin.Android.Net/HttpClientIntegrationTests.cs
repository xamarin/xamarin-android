//
// HttpClientIntegrationTests.cs
//
// Authors:
//	Marek Safar  <marek.safar@gmail.com>
//
// Copyright (C) 2011 Xamarin Inc (http://www.xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Linq;
using System.IO;

namespace Xamarin.Android.NetTests {
	[Category ("InetAccess")]
	public abstract class HttpClientIntegrationTestBase
	{
		protected abstract HttpClientHandler CreateHandler ();

		class CustomStream : Stream
		{
			public override void Flush ()
			{
				throw new NotImplementedException ();
			}

			int pos;

			public override int Read (byte[] buffer, int offset, int count)
			{
				++pos;
				if (pos > 4)
					return 0;

				return 11;
			}

			public override long Seek (long offset, SeekOrigin origin)
			{
				throw new NotImplementedException ();
			}

			public override void SetLength (long value)
			{
				throw new NotImplementedException ();
			}

			public override void Write (byte[] buffer, int offset, int count)
			{
				throw new NotImplementedException ();
			}

			public override bool CanRead {
				get {
					return true;
				}
			}

			public override bool CanSeek {
				get {
					return false;
				}
			}

			public override bool CanWrite {
				get {
					throw new NotImplementedException ();
				}
			}

			public override long Length {
				get {
					throw new NotImplementedException ();
				}
			}

			public override long Position {
				get {
					throw new NotImplementedException ();
				}
				set {
					throw new NotImplementedException ();
				}
			}
		}

		const int WaitTimeout = 5000;

		string port, TestHost, LocalServer;

		[SetUp]
		public void SetupFixture ()
		{
			if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
				port = "810";
			} else {
				port = "8810";
			}

			TestHost = "localhost:" + port;
			LocalServer = string.Format ("http://{0}/", TestHost);
		}

		[Test]
		public void Ctor_Default ()
		{
			using (var handler = CreateHandler ()) {
				var client = new HttpClient (handler);
				Assert.IsNull (client.BaseAddress, "#1");
				Assert.IsNotNull (client.DefaultRequestHeaders, "#2");  // TODO: full check
				Assert.AreEqual (int.MaxValue, client.MaxResponseContentBufferSize, "#3");
				Assert.AreEqual (TimeSpan.FromSeconds (100), client.Timeout, "#4");
			}
		}


		[Test]
		public void CancelRequestViaProxy ()
		{
			using (var handler = CreateHandler ()) {
				handler.Proxy = new WebProxy ("192.168.10.25:8888/"); // proxy that doesn't exist
				handler.UseProxy = true;
				handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

				var httpClient = new HttpClient (handler) {
					BaseAddress = new Uri ("https://google.com"),
					Timeout = TimeSpan.FromMilliseconds (1)
				};

				try {
					var restRequest = new HttpRequestMessage {
						Method = HttpMethod.Post,
						RequestUri = new Uri ("foo", UriKind.Relative),
						Content = new StringContent ("", null, "application/json")
					};

					httpClient.PostAsync (restRequest.RequestUri, restRequest.Content).Wait (WaitTimeout);
					Assert.Fail ("#1");
				} catch (AggregateException e) {
					Console.WriteLine ("CancelRequestViaProxy exception: {0}", e);
					Assert.IsTrue (e.InnerException is TaskCanceledException, "#2; threw: {0}", e);
				}
			}
		}

		[Test]
		public void Properties ()
		{
			using (var handler = CreateHandler ()) {
				var client = new HttpClient (handler);
				client.BaseAddress = null;
				client.MaxResponseContentBufferSize = int.MaxValue;
				client.Timeout = Timeout.InfiniteTimeSpan;

				Assert.IsNull (client.BaseAddress, "#1");
				Assert.AreEqual (int.MaxValue, client.MaxResponseContentBufferSize, "#2");
				Assert.AreEqual (Timeout.InfiniteTimeSpan, client.Timeout, "#3");
			}
		}

		[Test]
		public void Properties_Invalid ()
		{
			using (var handler = CreateHandler ()) {
				var client = new HttpClient (handler);
				try {
					client.MaxResponseContentBufferSize = 0;
					Assert.Fail ("#1");
				} catch (ArgumentOutOfRangeException) {
				}

				try {
					client.Timeout = TimeSpan.MinValue;
					Assert.Fail ("#2");
				} catch (ArgumentOutOfRangeException) {
				}
			}
		}

#if TODO
		[Test]
		public void Send_Complete_Default ()
		{
			bool? failed = null;
			var listener = CreateListener (l => {
					try {
						var request = l.Request;

						Assert.IsNull (request.AcceptTypes, "#1");
						Assert.AreEqual (0, request.ContentLength64, "#2");
						Assert.IsNull (request.ContentType, "#3");
						Assert.AreEqual (0, request.Cookies.Count, "#4");
						Assert.IsFalse (request.HasEntityBody, "#5");
						Assert.AreEqual (TestHost, request.Headers["Host"], "#6b");
						Assert.AreEqual ("GET", request.HttpMethod, "#7");
						Assert.IsFalse (request.IsAuthenticated, "#8");
#if false
						Assert.IsTrue (request.IsLocal, "#9");
#endif // Buggy HttpListenerRequest (https://bugzilla.xamarin.com/show_bug.cgi?id=38322)
						Assert.IsFalse (request.IsSecureConnection, "#10");
						Assert.IsFalse (request.IsWebSocketRequest, "#11");
						Assert.IsTrue (request.KeepAlive, "#12");
						Assert.AreEqual (HttpVersion.Version11, request.ProtocolVersion, "#13");
						Assert.IsNull (request.ServiceName, "#14");
						Assert.IsNull (request.UrlReferrer, "#15");
						Assert.IsNotNull (request.UserAgent, "#16"); // We're not using .NET client here, but rather the Java one which sets the UserAgent header
						Assert.IsNull (request.UserLanguages, "#17");
						failed = false;
					} catch (Exception e) {
						Console.WriteLine ("# jonp: Send_Complete_Default");
						Console.WriteLine (e);
						failed = true;
					}
				});

			using (listener) {
				try {
					using (var handler = CreateHandler ()) {
						var client = new HttpClient (handler);
						var request = new HttpRequestMessage (HttpMethod.Get, LocalServer);
						var response = client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead).Result;

						Assert.AreEqual ("", response.Content.ReadAsStringAsync ().Result, "#100");
						Assert.AreEqual (HttpStatusCode.OK, response.StatusCode, "#101");
						Assert.AreEqual (false, failed, "#102");
					}
				} finally {
					listener.Close ();
				}
			}
		}

		[Test]
		public void Send_Complete_Version_1_0 ()
		{
			bool? failed = null;

			var listener = CreateListener (l => {
					try {
						var request = l.Request;

						Assert.IsNull (request.AcceptTypes, "#1");
						Assert.AreEqual (0, request.ContentLength64, "#2");
						Assert.IsNull (request.ContentType, "#3");
						Assert.AreEqual (0, request.Cookies.Count, "#4");
						Assert.IsFalse (request.HasEntityBody, "#5");
						Assert.AreEqual (1, request.Headers.Count, "#6");
						Assert.AreEqual (TestHost, request.Headers["Host"], "#6a");
						Assert.AreEqual ("GET", request.HttpMethod, "#7");
						Assert.IsFalse (request.IsAuthenticated, "#8");
#if false
						Assert.IsTrue (request.IsLocal, "#9");
#endif // Buggy HttpListenerRequest (https://bugzilla.xamarin.com/show_bug.cgi?id=38322)
						Assert.IsFalse (request.IsSecureConnection, "#10");
						Assert.IsFalse (request.IsWebSocketRequest, "#11");
						Assert.IsFalse (request.KeepAlive, "#12");
#if false // Java HTTP client doesn't support 1.0, always uses 1.1
						Assert.AreEqual (HttpVersion.Version10, request.ProtocolVersion, "#13");
#endif
						Assert.IsNull (request.ServiceName, "#14");
						Assert.IsNull (request.UrlReferrer, "#15");
						Assert.IsNotNull (request.UserAgent, "#16"); // We're not using .NET client here, but rather the Java one which sets the UserAgent header
						Assert.IsNull (request.UserLanguages, "#17");
						failed = false;
					} catch (Exception e) {
						Console.WriteLine ("# jonp: Send_Complete_Version_1_0");
						Console.WriteLine (e);
						failed = true;
					}
				});

			using (listener) {
				try {
					using (var handler = CreateHandler ()) {
						var client = new HttpClient (handler);
						var request = new HttpRequestMessage (HttpMethod.Get, LocalServer);
						//request.Version = HttpVersion.Version10;
						var response = client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead).Result;

						Assert.AreEqual ("", response.Content.ReadAsStringAsync ().Result, "#100");
						Assert.AreEqual (HttpStatusCode.OK, response.StatusCode, "#101");
						Assert.AreEqual (false, failed, "#102");
					}
				} finally {
					listener.Close ();
				}
			}
		}

		// This is failing because the `try/catch` block (lines 308-336) aren't executed,
		// and thus `failed` (line 304) is `null` on line 361, resulting in a NRE.
		[Test]
		public void Send_Complete_ClientHandlerSettings ()
		{
			bool? failed = null;

			var listener = CreateListener (l => {
					var request = l.Request;

					try {
						Assert.IsNull (request.AcceptTypes, "#1");
						Assert.AreEqual (0, request.ContentLength64, "#2");
						Assert.IsNull (request.ContentType, "#3");
						Assert.AreEqual (1, request.Cookies.Count, "#4");
						Assert.AreEqual (new Cookie ("mycookie", "vv"), request.Cookies[0], "#4a");
						Assert.IsFalse (request.HasEntityBody, "#5");
						Assert.AreEqual (4, request.Headers.Count, "#6");
						Assert.AreEqual (TestHost, request.Headers["Host"], "#6a");
						Assert.AreEqual ("gzip", request.Headers["Accept-Encoding"], "#6b");
						Assert.AreEqual ("mycookie=vv", request.Headers["Cookie"], "#6c");
						Assert.AreEqual ("GET", request.HttpMethod, "#7");
						Assert.IsFalse (request.IsAuthenticated, "#8");
#if false
						Assert.IsTrue (request.IsLocal, "#9");
#endif // Buggy HttpListenerRequest (https://bugzilla.xamarin.com/show_bug.cgi?id=38322)
						Assert.IsFalse (request.IsSecureConnection, "#10");
						Assert.IsFalse (request.IsWebSocketRequest, "#11");
						Assert.IsTrue (request.KeepAlive, "#12");
#if false // Java HTTP client doesn't support 1.0, always uses 1.1
						Assert.AreEqual (HttpVersion.Version10, request.ProtocolVersion, "#13");
#endif
						Assert.IsNull (request.ServiceName, "#14");
						Assert.IsNull (request.UrlReferrer, "#15");
#if false
						Assert.IsNull (request.UserAgent, "#16"); // We're not using .NET client here, but rather the Java one which sets the UserAgent header
#endif
						Assert.IsNull (request.UserLanguages, "#17");
						failed = false;
					} catch (Exception x) {
						Console.WriteLine ("# jonp: Send_Complete_ClientHandlerSettings: ERROR");
						Console.WriteLine (x.ToString ());
						failed = true;
					}
				});

			using (listener) {
				try {
					using (var chandler = CreateHandler ()) {
						chandler.AllowAutoRedirect = true;
						chandler.AutomaticDecompression = DecompressionMethods.GZip;
						chandler.MaxAutomaticRedirections = 33;
						chandler.MaxRequestContentBufferSize = 5555;
						chandler.PreAuthenticate = true;
						chandler.CookieContainer.Add (new Uri (LocalServer), new Cookie ("mycookie", "vv"));
						chandler.UseCookies = true;
						chandler.UseDefaultCredentials = true;
						chandler.Proxy = new WebProxy ("ee");
						chandler.UseProxy = true;

						var client = new HttpClient (chandler);
						var request = new HttpRequestMessage (HttpMethod.Get, LocalServer);
						request.Version = HttpVersion.Version10;
						request.Headers.Add ("Keep-Alive", "false");
						var response = client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead).Result;

						Assert.AreEqual ("", response.Content.ReadAsStringAsync ().Result, "#100");
						Assert.AreEqual (HttpStatusCode.OK, response.StatusCode, "#101");
						Console.WriteLine ("# jonp: Send_Complete_ClientHandlerSettings: failed? {0}", failed.HasValue);
						Assert.AreEqual (false, failed, "#102");
					}
				} finally {
					listener.Abort ();
					listener.Close ();
				}
			}
		}

		[Test]
		public void Send_Complete_CustomHeaders ()
		{
			bool? failed = null;

			var listener = CreateListener (l => {
					var request = l.Request;
					try {
						Assert.AreEqual ("vv", request.Headers["aa"], "#1");

						var response = l.Response;
						response.Headers.Add ("rsp", "rrr");
						response.Headers.Add ("upgrade", "vvvvaa");
						response.Headers.Add ("Date", "aa");
						response.Headers.Add ("cache-control", "audio");

						response.StatusDescription = "test description";
						response.ProtocolVersion = HttpVersion.Version10;
						response.SendChunked = true;
						response.RedirectLocation = "w3.org";

						failed = false;
					} catch {
						failed = true;
					}
				});

			using (listener) {
				try {
					using (var handler = CreateHandler ()) {
						var client = new HttpClient (handler);
						var request = new HttpRequestMessage (HttpMethod.Get, LocalServer);
						Assert.IsTrue (request.Headers.TryAddWithoutValidation ("aa", "vv"), "#0");
						var response = client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead).Result;

						Assert.AreEqual ("", response.Content.ReadAsStringAsync ().Result, "#100");
						Assert.AreEqual (HttpStatusCode.OK, response.StatusCode, "#101");

						IEnumerable<string> values;
						Assert.IsTrue (response.Headers.TryGetValues ("rsp", out values), "#102");
						Assert.AreEqual ("rrr", values.First (), "#102a");

						Assert.IsTrue (response.Headers.TryGetValues ("Transfer-Encoding", out values), "#103");
						Assert.AreEqual ("chunked", values.First (), "#103a");
						Assert.AreEqual (true, response.Headers.TransferEncodingChunked, "#103b");

						Assert.IsTrue (response.Headers.TryGetValues ("Date", out values), "#104");
						Assert.AreEqual (1, values.Count (), "#104b");
						// .NET overwrites Date, Mono does not
						// Assert.IsNotNull (response.Headers.Date, "#104c");

						Assert.AreEqual (new ProductHeaderValue ("vvvvaa"), response.Headers.Upgrade.First (), "#105");

						Assert.AreEqual ("audio", response.Headers.CacheControl.Extensions.First ().Name, "#106");

						Assert.AreEqual ("w3.org", response.Headers.Location.OriginalString, "#107");

						Assert.AreEqual ("test description", response.ReasonPhrase, "#110");
						Assert.AreEqual (HttpVersion.Version11, response.Version, "#111");

						Assert.AreEqual (false, failed, "#112");
					}
				} finally {
					listener.Close ();
				}
			}
		}

		[Test]
		public void Send_Complete_CustomHeaders_SpecialSeparators ()
		{
			bool? failed = null;

			var listener = CreateListener (l => {
					var request = l.Request;

					try {
						Assert.AreEqual ("MLK Android Phone 1.1.9", request.UserAgent, "#1");
						failed = false;
					} catch (Exception ex) {
						failed = true;
						Console.WriteLine (ex);
					}
				});

			using (listener) {
				try {
					using (var handler = CreateHandler ()) {
						var client = new HttpClient (handler);

						client.DefaultRequestHeaders.Add ("User-Agent", "MLK Android Phone 1.1.9");

						var request = new HttpRequestMessage (HttpMethod.Get, LocalServer);

						var response = client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead).Result;

						Assert.AreEqual ("", response.Content.ReadAsStringAsync ().Result, "#100");
						Assert.AreEqual (HttpStatusCode.OK, response.StatusCode, "#101");
						Assert.AreEqual (false, failed, "#102");
					}
				} finally {
					listener.Abort ();
					listener.Close ();
				}
			}
		}

		[Test]
		public void Send_Complete_CustomHeaders_Host ()
		{
			bool? failed = null;
			var listener = CreateListener (l => {
					var request = l.Request;

					try {
						Assert.AreEqual ("customhost", request.Headers["Host"], "#1");
						failed = false;
					} catch (Exception ex) {
						failed = true;
						Console.WriteLine (ex);
					}
				});

			using (listener) {
				try {
					using (var handler = CreateHandler ()) {
						var client = new HttpClient (handler);

						client.DefaultRequestHeaders.Add ("Host", "customhost");

						var request = new HttpRequestMessage (HttpMethod.Get, LocalServer);

						var response = client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead).Result;

						Assert.AreEqual ("", response.Content.ReadAsStringAsync ().Result, "#100");
						Assert.AreEqual (HttpStatusCode.OK, response.StatusCode, "#101");
						Assert.AreEqual (false, failed, "#102");
					}
				} finally {
					listener.Abort ();
					listener.Close ();
				}
			}
		}

		[Test]
		public void Send_Transfer_Encoding_Chunked ()
		{
			bool? failed = null;

			var listener = CreateListener (l => {
					var request = l.Request;

					try {
						Assert.AreEqual (5, request.Headers.Count, "#1");
						failed = false;
					} catch (Exception ex) {
						failed = true;
						Console.WriteLine (ex);
					}
				});

			using (listener) {
				try {
					using (var handler = CreateHandler ()) {
						var client = new HttpClient (handler);
						client.DefaultRequestHeaders.TransferEncodingChunked = true;

						client.GetAsync (LocalServer).Wait ();

						Assert.AreEqual (false, failed, "#102");
					}
				} finally {
					listener.Abort ();
					listener.Close ();
				}
			}
		}
#endif  // TODO
		[Test]
		void UrlEscaping_Bug43411 ()
		{
			UrlEscaping_TestUrl ($"http://{TestHost}/?example=value%20_value", "#1");
			UrlEscaping_TestUrl ($"http://{TestHost}/?query=anna%20%26%20lotte&param2=true", "#2");
		}

		void UrlEscaping_TestUrl (string url, string messagePrefix)
		{
			bool? failed = null;

			var listener = CreateListener (l => {
					failed = true;
				});

			using (listener) {
				try {
					var client = new HttpClient ();
					var request = new HttpRequestMessage (HttpMethod.Get, url);

					client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead).Wait ();
					Assert.AreEqual (url, request.RequestUri.ToString (), $"{messagePrefix}-1");
					Assert.IsNull (failed, $"{messagePrefix}-2");
				} finally {
					listener.Abort ();
					listener.Close ();
				}
			}
		}
#if TODO
		[Test]
		public void Send_Complete_Content ()
		{
			var listener = CreateListener (l => {
					var request = l.Request;
					l.Response.OutputStream.WriteByte (55);
					l.Response.OutputStream.WriteByte (75);
				});

			using (listener) {
				try {
					using (var handler = CreateHandler ()) {
						var client = new HttpClient (handler);
						var request = new HttpRequestMessage (HttpMethod.Get, LocalServer);
						Assert.IsTrue (request.Headers.TryAddWithoutValidation ("aa", "vv"), "#0");
						var response = client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead).Result;

						Assert.AreEqual ("7K", response.Content.ReadAsStringAsync ().Result, "#100");
						Assert.AreEqual (HttpStatusCode.OK, response.StatusCode, "#101");

						IEnumerable<string> values;
						Assert.IsTrue (response.Headers.TryGetValues ("Transfer-Encoding", out values), "#102");
						Assert.AreEqual ("chunked", values.First (), "#102a");
						Assert.AreEqual (true, response.Headers.TransferEncodingChunked, "#102b");
					}
				} finally {
					listener.Close ();
				}
			}
		}

		[Test]
		public void Send_Complete_Content_MaxResponseContentBufferSize ()
		{
			var listener = CreateListener (l => {
					var request = l.Request;
					var b = new byte[4000];
					l.Response.OutputStream.Write (b, 0, b.Length);
				});

			using (listener) {
				try {
					using (var handler = CreateHandler ()) {
						var client = new HttpClient (handler);
						client.MaxResponseContentBufferSize = 1000;
						var request = new HttpRequestMessage (HttpMethod.Get, LocalServer);
						var response = client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead).Result;

						Assert.AreEqual (4000, response.Content.ReadAsStringAsync ().Result.Length, "#100");
						Assert.AreEqual (HttpStatusCode.OK, response.StatusCode, "#101");
					}
				} finally {
					listener.Close ();
				}
			}
		}

		[Test]
		public void Send_Complete_Content_MaxResponseContentBufferSize_Error ()
		{
			var listener = CreateListener (l => {
					var request = l.Request;
					var b = new byte[4000];
					l.Response.OutputStream.Write (b, 0, b.Length);
				});

			using (listener) {
				try {
					using (var handler = CreateHandler ()) {
						var client = new HttpClient (handler);
						client.MaxResponseContentBufferSize = 1000;
						var request = new HttpRequestMessage (HttpMethod.Get, LocalServer);

						try {
							client.SendAsync (request, HttpCompletionOption.ResponseContentRead).Wait (WaitTimeout);
							Assert.Fail ("#2");
						} catch (AggregateException e) {
							Assert.IsTrue (e.InnerException is HttpRequestException, "#3; threw: {0}", e);
						}
					}
				} finally {
					listener.Close ();
				}
			}
		}
#endif
		public void Send_Complete_NoContent (HttpMethod method)
		{
			bool? failed = null;
			var listener = CreateListener (l => {
					try {
						var request = l.Request;

						Assert.AreEqual (6, request.Headers.Count, $"#1-{method}");
						Assert.AreEqual ("0", request.Headers ["Content-Length"], $"#1b-{method}");
						Assert.AreEqual (method.Method, request.HttpMethod, $"#2-{method}");
						Console.WriteLine ($"Asserts are fine - {method}");
						failed = false;
					} catch (Exception ex) {
						failed = true;
						Console.WriteLine (ex);
					}
				});

			using (listener) {
				try {
					using (var handler = CreateHandler ()) {
						var client = new HttpClient (handler);
						var request = new HttpRequestMessage (method, LocalServer);
						var response = client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead).Result;

						Assert.AreEqual ("", response.Content.ReadAsStringAsync ().Result, $"#100-{method}");
						Assert.AreEqual (HttpStatusCode.OK, response.StatusCode, $"#101-{method}");
						Assert.AreEqual (false, failed, $"#102-{method}");
					}
				} finally {
					listener.Close ();
				}
			}
		}
#if TODO
		[Test]
		public void Send_Complete_NoContent_POST ()
		{
			Send_Complete_NoContent (HttpMethod.Post);
		}

		[Test]
		public void Send_Complete_NoContent_PUT ()
		{
			Send_Complete_NoContent (HttpMethod.Put);
		}


		[Test]
		public void Send_Complete_NoContent_DELETE ()
		{
			Send_Complete_NoContent (HttpMethod.Delete);
		}

		[Test]
		public void Send_Complete_Error ()
		{
			var listener = CreateListener (l => {
					var response = l.Response;
					response.StatusCode = 500;
				});

			using (listener) {
				try {
					using (var handler = CreateHandler ()) {
						var client = new HttpClient (handler);
						var request = new HttpRequestMessage (HttpMethod.Get, LocalServer);
						var response = client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead).Result;

						Assert.AreEqual ("", response.Content.ReadAsStringAsync ().Result, "#100");
						Assert.AreEqual (HttpStatusCode.InternalServerError, response.StatusCode, "#101");
					}
				} finally {
					listener.Close ();
				}
			}
		}

		[Test]
		public void Send_Content_Get ()
		{
			var listener = CreateListener (l => {
					var request = l.Request;
					l.Response.OutputStream.WriteByte (72);
				});

			using (listener) {
				try {
					var client = new HttpClient ();
					var r = new HttpRequestMessage (HttpMethod.Get, LocalServer);
					var response = client.SendAsync (r).Result;

					Assert.AreEqual ("H", response.Content.ReadAsStringAsync ().Result);
				} finally {
					listener.Close ();
				}
			}
		}

		[Test]
		public void Send_Content_BomEncoding ()
		{
			var listener = CreateListener (l => {
					var request = l.Request;

					var str = l.Response.OutputStream;
					str.WriteByte (0xEF);
					str.WriteByte (0xBB);
					str.WriteByte (0xBF);
					str.WriteByte (71);
				});

			using (listener) {
				try {
					var client = new HttpClient ();
					var r = new HttpRequestMessage (HttpMethod.Get, LocalServer);
					var response = client.SendAsync (r).Result;

					Assert.AreEqual ("G", response.Content.ReadAsStringAsync ().Result);
				} finally {
					listener.Close ();
				}
			}
		}

		[Test]
		public void Send_Content_Put ()
		{
			bool passed = false;
			var listener = CreateListener (l => {
					var request = l.Request;
					passed = 7 == request.ContentLength64;
					passed &= request.ContentType == "text/plain; charset=utf-8";
					passed &= request.InputStream.ReadByte () == 'm';
				});

			using (listener) {
				try {
					var client = new HttpClient ();
					var r = new HttpRequestMessage (HttpMethod.Put, LocalServer);
					r.Content = new StringContent ("my text");
					var response = client.SendAsync (r).Result;

					Assert.AreEqual (HttpStatusCode.OK, response.StatusCode, "#1");
					Assert.IsTrue (passed, "#2");
				} finally {
					listener.Abort ();
					listener.Close ();
				}
			}
		}

		[Test]
		public void Send_Content_Put_CustomStream ()
		{
			bool passed = false;
			var listener = CreateListener (l => {
					var request = l.Request;
					passed = 44 == request.ContentLength64;
					passed &= request.ContentType == null;
				});

			using (listener) {
				try {
					var client = new HttpClient ();
					var r = new HttpRequestMessage (HttpMethod.Put, LocalServer);
					r.Content = new StreamContent (new CustomStream ());
					var response = client.SendAsync (r).Result;

					Assert.AreEqual (HttpStatusCode.OK, response.StatusCode, "#1");
					Assert.IsTrue (passed, "#2");
				} finally {
					listener.Abort ();

					listener.Close ();
				}
			}
		}
#endif  // TODO

		[Test]
		public void Send_Invalid ()
		{
			var client = new HttpClient (CreateHandler ());
			try {
				client.SendAsync (null).Wait (WaitTimeout);
				Assert.Fail ("#1");
			} catch (ArgumentNullException) {
			}

			try {
				var request = new HttpRequestMessage ();
				client.SendAsync (request).Wait (WaitTimeout);
				Assert.Fail ("#2");
			} catch (InvalidOperationException) {
			}
		}

		[Test]
		[Category ("MobileNotWorking")] // Missing encoding
		public void GetString_Many ()
		{
			var client = new HttpClient (CreateHandler ());
			var t1 = client.GetStringAsync ("http://example.org");
			var t2 = client.GetStringAsync ("http://example.org");
			Assert.IsTrue (Task.WaitAll (new [] { t1, t2 }, WaitTimeout));
		}

#if TODO
		// Currently fails because GetByteArrayAsync().Wait(timeout) doesn't throw
		[Test]
		public void GetByteArray_ServerError ()
		{
			var listener = CreateListener (l => {
					var response = l.Response;
					response.StatusCode = 500;
					l.Response.OutputStream.WriteByte (72);
				});

			using (listener) {
				try {
					var client = new HttpClient (CreateHandler ());
					try {
						client.GetByteArrayAsync (LocalServer).Wait (WaitTimeout);
						Assert.Fail ("#1");
					} catch (AggregateException e) {
						Console.WriteLine ("# jonp: GetByteArray_ServerError");
						Console.WriteLine (e);
						Assert.IsTrue (e.InnerException is HttpRequestException, "#2; threw: {0}", e);
					}
				} finally {
					listener.Close ();
				}
			}
		}
#endif  // TODO

		[Test]
		public void DisallowAutoRedirect ()
		{
			var listener = CreateListener (l => {
					var request = l.Request;
					var response = l.Response;

					response.StatusCode = (int)HttpStatusCode.Moved;
					response.RedirectLocation = "http://xamarin.com/";
				});

			using (listener) {
				try {
					var chandler = CreateHandler ();
					chandler.AllowAutoRedirect = false;
					var client = new HttpClient (chandler);

					try {
						client.GetStringAsync (LocalServer).Wait (WaitTimeout);
						Assert.Fail ("#1: HttpRequestException wasn't thrown.");
					} catch (AggregateException e) {
						Assert.IsTrue (e.InnerException is HttpRequestException, "#2: " + e.ToString ());
					}
				} finally {
					listener.Abort ();
					listener.Close ();
				}
			}
		}
#if TODO
		[Test]
		public void RequestUriAfterRedirect ()
		{
			var listener = CreateListener (l => {
					var request = l.Request;
					var response = l.Response;

					response.StatusCode = (int)HttpStatusCode.Moved;
					response.RedirectLocation = "http://xamarin.com/";
				});

			using (listener) {
				try {
					var chandler = CreateHandler ();
					chandler.AllowAutoRedirect = true;
					var client = new HttpClient (chandler);

					var r = client.GetAsync (LocalServer);
					Assert.IsTrue (r.Wait (WaitTimeout), "#1");
					var resp = r.Result;
					Assert.AreEqual ("http://xamarin.com/", resp.RequestMessage.RequestUri.AbsoluteUri, "#2");
				} finally {
					listener.Abort ();
					listener.Close ();
				}
			}
		}
#endif
#if false
		// It doesn't appear to be possible to satisfy this test, because e.g.
		// HttpClientHandler.set_AllowAutoRedirect only throws when
		// HttpClientHandler.sentRequest is true, and sentRequest is only set
		// if HttpClientHandler.SendAsync() is invoked, and *we can't call it*.
		// Perhaps a mono implementation bug?
		[Test]
		/*
		 * Properties may only be modified before sending the first request.
		 */
		public void ModifyHandlerAfterFirstRequest ()
		{
			var chandler = CreateHandler ();
			chandler.AllowAutoRedirect = true;
			var client = new HttpClient (chandler, true);

			var listener = CreateListener (l => {
					var response = l.Response;
					response.StatusCode = 200;
					response.OutputStream.WriteByte (55);
				});

			try {
				client.GetStringAsync (LocalServer).Wait (WaitTimeout);
				try {
					chandler.AllowAutoRedirect = false;
					Assert.Fail ("#1");
				} catch (InvalidOperationException) {
					;
				}
			} finally {
				listener.Abort ();
				listener.Close ();
			}
		}
#endif
		// Verifies the handler always returns a response on 401, regardless of the
		// presence of a WWW-Authenticate header in the HTTP response. (Pre API 19,
		// HttpURLConnectionImpl would throw an Java.IO.IOException in this scenario).
		[Test]
		public void ShouldHandle401WithoutWWWAuthenticateHeader() 
		{
			var listener = CreateListener(l => {
				var response = l.Response;
				response.StatusCode = 401;
			});

			using (listener)
			using (var httpClient = new HttpClient(CreateHandler(), true)) {
				var response = httpClient.GetAsync(LocalServer).Result;

				Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
			}
		}

		HttpListener CreateListener (Action<HttpListenerContext> contextAssert)
		{
			var l = new HttpListener ();
			l.Prefixes.Add (string.Format ("http://+:{0}/", port));
			l.Start ();
			l.BeginGetContext (ar => {
					var ctx = l.EndGetContext (ar);
					try {
						if (contextAssert != null)
							contextAssert (ctx);
					} finally {
						ctx.Response.Close ();
					}
				}, null);

			return l;
		}
	}

	[TestFixture]
	public class AndroidClientHandlerIntegrationTests : HttpClientIntegrationTestBase
	{
		protected override HttpClientHandler CreateHandler ()
		{
			return new Xamarin.Android.Net.AndroidClientHandler ();
		}
	}
}
