using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using Android.Runtime;
using Xamarin.Android.Net;

using NUnit.Framework;

namespace Xamarin.Android.NetTests
{
	[TestFixture]
	public class AndroidMessageHandlerTests : AndroidHandlerTestBase
	{
		protected override HttpMessageHandler CreateHandler ()
		{
			return new AndroidMessageHandler ();
		}

		// We can't test `deflate` for now because it's broken in the BCL for https://httpbin.org/deflate (S.I.Compression.DeflateStream doesn't recognize the compression
		// method used by the server)
		static readonly object[] DecompressionSource = new object[] {
			new object[] {
				"gzip", // urlPath
				"gzip", // encoding
				"gzipped", // jsonFieldName
			},

			new object[] {
				"brotli", // urlPath
				"br", // encoding
				"brotli", // jsonFieldName
			},
		};

		[Test]
		[TestCaseSource (nameof (DecompressionSource))]
		// Disabled because it doesn't exist in NUnitLite, uncomment when/if we switch to full NUnit
		// When we can use it, replace all the Console.WriteLine calls with Assert.Warn
		// [Retry (5)]
		public async Task Decompression (string urlPath, string encoding, string jsonFieldName)
		{
			// Catch all the exceptions and warn about them or otherwise [Retry] above won't work
			try {
				int count = 0;
				// Remove the loop when [Retry] can be used
				while (count < 5) {
					if (await DoDecompression (urlPath, encoding, jsonFieldName)) {
						return;
					}
					count++;
				}
			} catch (Exception ex) {
				Console.WriteLine ("Unexpected exception thrown");
				Console.WriteLine (ex.ToString ());
				Assert.Fail ("Exception should have not been thrown");
			}
		}

		async Task<bool> DoDecompression (string urlPath, string encoding, string jsonFieldName)
		{
			var handler = new AndroidMessageHandler {
				AutomaticDecompression = DecompressionMethods.All
			};

			var client = new HttpClient (handler);
			HttpResponseMessage response = await client.GetAsync ($"https://httpbin.org/{urlPath}");

			// Failing on error codes other than 2xx will make NUnit retry the test up to the number of times specified in the
			// [Retry] attribute above.  This may or may not the desired effect if httpbin.org is throttling the requests, thus
			// we will sleep a short while before failing the test
			if (!response.IsSuccessStatusCode) {
				System.Threading.Thread.Sleep (1000);
				// Uncomment when we can use [Retry]
				//Assert.Fail ($"Request ended with a failure error code: {response.StatusCode}");
				return false;
			}

			foreach (string enc in response.Content.Headers.ContentEncoding) {
				if (String.Compare (enc, encoding, StringComparison.Ordinal) == 0) {
					Assert.Fail ($"Encoding '{encoding}' should have been removed from the Content-Encoding header");
				}
			}

			string responseBody = await response.Content.ReadAsStringAsync ();

			Console.WriteLine ("-- Retrieved JSON start");
			Console.WriteLine (responseBody);
			Console.WriteLine ("-- Retrieved JSON end");

			Assert.IsTrue (responseBody.Length > 0, "Response was empty");
			Assert.AreEqual (response.Content.Headers.ContentLength, responseBody.Length, "Retrieved data length is different than the one specified in the Content-Length header");
			Assert.IsTrue (responseBody.Contains ($"\"{jsonFieldName}\"", StringComparison.OrdinalIgnoreCase), $"\"{jsonFieldName}\" should have been in the response JSON");

			return true;
		}

		[Test]
		public async Task DoesNotDisposeContentStream()
		{
			using var listener = new HttpListener ();
			listener.Prefixes.Add ("http://+:47663/");
			listener.Start ();
			listener.BeginGetContext (ar => {
				var ctx = listener.EndGetContext (ar);
				ctx.Response.StatusCode = 204;
				ctx.Response.ContentLength64 = 0;
				ctx.Response.Close ();
			}, null);

			var jsonContent = new StringContent ("hello");
			var request = new HttpRequestMessage (HttpMethod.Post, "http://localhost:47663/") { Content = jsonContent };

			var response = await new HttpClient (new AndroidMessageHandler ()).SendAsync (request);
			Assert.True (response.IsSuccessStatusCode);

			var contentValue = await jsonContent.ReadAsStringAsync ();
			Assert.AreEqual ("hello", contentValue);

			listener.Close ();
		}

		[Test]
		public async Task ServerCertificateCustomValidationCallback_ApproveRequest ()
		{
			bool callbackHasBeenCalled = false;

			var handler = new AndroidMessageHandler {
				ServerCertificateCustomValidationCallback = (request, cert, chain, errors) => {
					Assert.NotNull (request, "request");
					Assert.AreEqual ("www.microsoft.com", request.RequestUri.Host);
					Assert.NotNull (cert, "cert");
					Assert.True (cert!.Subject.Contains ("www.microsoft.com"), $"Unexpected certificate subject {cert!.Subject}");
					Assert.True (cert!.Issuer.Contains ("Microsoft"), $"Unexpected certificate issuer {cert!.Issuer}");
					Assert.NotNull (chain, "chain");
					Assert.AreEqual (SslPolicyErrors.None, errors);

					callbackHasBeenCalled = true;
					return true;
				}
			};

			var client = new HttpClient (handler);
			await client.GetStringAsync ("https://www.microsoft.com/");

			Assert.IsTrue (callbackHasBeenCalled, "custom validation callback hasn't been called");
		}

		[Test]
		public async Task ServerCertificateCustomValidationCallback_RejectRequest ()
		{
			bool callbackHasBeenCalled = false;

			var handler = new AndroidMessageHandler {
				ServerCertificateCustomValidationCallback = (request, cert, chain, errors) => {
					callbackHasBeenCalled = true;
					return false;
				}
			};
			var client = new HttpClient (handler);

			await AssertRejectsRemoteCertificate (() => client.GetStringAsync ("https://www.microsoft.com/"));

			Assert.IsTrue (callbackHasBeenCalled, "custom validation callback hasn't been called");
		}

		[Test]
		public async Task ServerCertificateCustomValidationCallback_ApprovesRequestWithInvalidCertificate ()
		{
			bool callbackHasBeenCalled = false;

			var handler = new AndroidMessageHandler {
				ServerCertificateCustomValidationCallback = (request, cert, chain, errors) => {
					callbackHasBeenCalled = true;
					return true;
				}
			};

			var client = new HttpClient (handler);
			await client.GetStringAsync ("https://self-signed.badssl.com/");

			Assert.IsTrue (callbackHasBeenCalled, "custom validation callback hasn't been called");
		}

		[Test]
		public async Task NoServerCertificateCustomValidationCallback_ThrowsWhenThereIsCertificateHostnameMismatch ()
		{
			var handler = new AndroidMessageHandler ();
			var client = new HttpClient (handler);

			await AssertRejectsRemoteCertificate (() => client.GetStringAsync ("https://wrong.host.badssl.com/"));
		}

		[Test]
		public async Task ServerCertificateCustomValidationCallback_IgnoresCertificateHostnameMismatch ()
		{
			bool callbackHasBeenCalled = false;
			SslPolicyErrors reportedErrors = SslPolicyErrors.None;

			var handler = new AndroidMessageHandler {
				ServerCertificateCustomValidationCallback = (request, cert, chain, errors) => {
					callbackHasBeenCalled = true;
					reportedErrors = errors;
					return true;
				}
			};

			var client = new HttpClient (handler);
			await client.GetStringAsync ("https://wrong.host.badssl.com/");

			Assert.IsTrue (callbackHasBeenCalled, "custom validation callback hasn't been called");
			Assert.AreEqual (SslPolicyErrors.RemoteCertificateNameMismatch, reportedErrors & SslPolicyErrors.RemoteCertificateNameMismatch);
		}

		[Test]
		public async Task ServerCertificateCustomValidationCallback_Redirects ()
		{
			int callbackCounter = 0;

			var handler = new AndroidMessageHandler {
				ServerCertificateCustomValidationCallback = (request, cert, chain, errors) => {
					callbackCounter++;
					return errors == SslPolicyErrors.None;
				}
			};

			var client = new HttpClient (handler);
			var result = await client.GetAsync ("https://httpbin.org/redirect-to?url=https://www.microsoft.com/");

			Assert.AreEqual (2, callbackCounter);
			Assert.IsTrue (result.IsSuccessStatusCode);
		}

		[Test]
		public async Task AndroidMessageHandlerFollows308PermanentRedirect ()
		{
			int callbackCounter = 0;

			var handler = new AndroidMessageHandler ();

			var client = new HttpClient (handler);
			var result = await client.GetAsync ("https://httpbin.org/redirect-to?url=https://www.microsoft.com/&status_code=308");

			Assert.IsTrue (result.IsSuccessStatusCode);
			Assert.AreEqual ("https://www.microsoft.com/", result.RequestMessage.RequestUri.ToString ());
		}

		[Test]
		public async Task AndroidMessageHandlerSendsClientCertificate ([Values(true, false)] bool setClientCertificateOptionsExplicitly)
		{
			using X509Certificate2 certificate = BuildClientCertificate ();

			using var handler = new AndroidMessageHandler ();
			if (setClientCertificateOptionsExplicitly) {
				handler.ClientCertificateOptions = ClientCertificateOption.Manual;
			}
			handler.ClientCertificates.Add (certificate);

			using var client = new HttpClient (handler);
			var response = await client.GetAsync ("https://corefx-net-tls.azurewebsites.net/EchoClientCertificate.ashx");
			var content = await response.EnsureSuccessStatusCode ().Content.ReadAsStringAsync ();

			X509Certificate2 certificate2 = new X509Certificate2 (global::System.Convert.FromBase64String (content));
			Assert.AreEqual (certificate.Thumbprint, certificate2.Thumbprint);
		}

		[Test]
		public async Task AndroidMessageHandlerRejectsClientCertificateOptionsAutomatic ()
		{
			var handler = new AndroidMessageHandler
			{
				ClientCertificateOptions = ClientCertificateOption.Automatic,
			};

			Assert.Throws<InvalidOperationException>(() => handler.ClientCertificates.Add (BuildClientCertificate ()));
		}

		private async Task AssertRejectsRemoteCertificate (Func<Task> makeRequest)
		{
			// there is a difference between the exception that's thrown in the .NET build and the legacy Xamarin
			// because there's a difference in the $(AndroidBoundExceptionType) property value (legacy: Java, .NET: System)
			try {
				await makeRequest();
				Assert.Fail ("The request wasn't rejected");
			}
			// While technically we should be throwing only HttpRequestException (as per HttpClient.SendAsync docs), in reality
			// we need to consider legacy code that migrated to .NET and may still expect WebException.  Thus, we throw both
			// of these and we need to catch both here
			catch (System.Net.WebException) {}
			catch (System.Net.Http.HttpRequestException) {}
		}

		// Adapted from https://github.com/dotnet/runtime/blob/e8b89a3fde2911c6cbac0488bf82c74329a7224a/src/libraries/Common/tests/System/Security/Cryptography/X509Certificates/CertificateAuthority.cs#L797
		private static X509Certificate2 BuildClientCertificate ()
		{
			DateTimeOffset start = DateTimeOffset.UtcNow;
			DateTimeOffset end = start.AddMonths (3);

			using RSA rootKey = RSA.Create (keySizeInBits: 2048);
			using RSA clientKey = RSA.Create (keySizeInBits: 2048);

			var rootReq = new CertificateRequest ("CN=Test Root, O=Test Root Organization", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			rootReq.CertificateExtensions.Add (new X509BasicConstraintsExtension (certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
			rootReq.CertificateExtensions.Add (new X509SubjectKeyIdentifierExtension (rootReq.PublicKey, critical: false));
			X509Certificate2 rootCert = rootReq.CreateSelfSigned (start.AddDays (-2), end.AddDays (2));

			var clientReq = new CertificateRequest ("CN=Test End Entity, O=Test End Entity Organization", clientKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			clientReq.CertificateExtensions.Add (new X509BasicConstraintsExtension (certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: false));
			clientReq.CertificateExtensions.Add (new X509KeyUsageExtension (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, critical: false));
			clientReq.CertificateExtensions.Add (new X509EnhancedKeyUsageExtension (enhancedKeyUsages: new OidCollection { new Oid ("1.3.6.1.5.5.7.3.2", null) }, critical: false)); // TLS client EKU
			clientReq.CertificateExtensions.Add (new X509SubjectKeyIdentifierExtension (clientReq.PublicKey, critical: false));

			var serial = new byte [sizeof (long)];
			RandomNumberGenerator.Fill (serial);

			X509Certificate2 clientCert = clientReq.Create (rootCert, start, end, serial);

			var tmp = clientCert;
			clientCert = clientCert.CopyWithPrivateKey (clientKey);
			tmp.Dispose ();

			return clientCert;
		}
	}
}
