using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Android.OS;
using Android.Runtime;
using Java.IO;
using Java.Net;
using Java.Security;
using Java.Security.Cert;
using Javax.Net.Ssl;
using JavaX509Certificate = Java.Security.Cert.X509Certificate;

namespace Xamarin.Android.Net
{
	/// <summary>
	/// A custom implementation of <see cref="System.Net.Http.HttpMessageHandler"/> which internally uses <see cref="Java.Net.HttpURLConnection"/>
	/// (or its HTTPS incarnation) to send HTTP requests.
	/// </summary>
	/// <remarks>
	/// <para>Instance of this class is used to configure <see cref="System.Net.Http.HttpClient"/> instance
	/// in the following way:
	///
	/// <example>
	/// var handler = new AndroidMessageHandler {
	///    UseCookies = true,
	///    AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
	/// };
	///
	/// var httpClient = new HttpClient (handler);
	/// var response = httpClient.GetAsync ("http://example.com")?.Result as AndroidHttpResponseMessage;
	/// </example></para>
	/// <para>
	/// The class supports pre-authentication of requests albeit in a slightly "manual" way. Namely, whenever a request to a server requiring authentication
	/// is made and no authentication credentials are provided in the <see cref="PreAuthenticationData"/> property (which is usually the case on the first
	/// request), the <see cref="RequestNeedsAuthorization"/> property will return <c>true</c> and the <see cref="RequestedAuthentication"/> property will
	/// contain all the authentication information gathered from the server. The application must then fill in the blanks (i.e. the credentials) and re-send
	/// the request configured to perform pre-authentication. The reason for this manual process is that the underlying Java HTTP client API supports only a
	/// single, VM-wide, authentication handler which cannot be configured to handle credentials for several requests. AndroidMessageHandler, therefore, implements
	/// the authentication in managed .NET code. Message handler supports both Basic and Digest authentication. If an authentication scheme that's not supported
	/// by AndroidMessageHandler is requested by the server, the application can provide its own authentication module (<see cref="AuthenticationData"/>,
	/// <see cref="PreAuthenticationData"/>) to handle the protocol authorization.</para>
	/// <para>AndroidMessageHandler also supports requests to servers with "invalid" (e.g. self-signed) SSL certificates. Since this process is a bit convoluted using
	/// the Java APIs, AndroidMessageHandler defines two ways to handle the situation. First, easier, is to store the necessary certificates (either CA or server certificates)
	/// in the <see cref="TrustedCerts"/> collection or, after deriving a custom class from AndroidMessageHandler, by overriding one or more methods provided for this purpose
	/// (<see cref="ConfigureTrustManagerFactory"/>, <see cref="ConfigureKeyManagerFactory"/> and <see cref="ConfigureKeyStore"/>). The former method should be sufficient
	/// for most use cases, the latter allows the application to provide fully customized key store, trust manager and key manager, if needed. Note that the instance of
	/// AndroidMessageHandler configured to accept an "invalid" certificate from the particular server will most likely fail to validate certificates from other servers (even
	/// if they use a certificate with a fully validated trust chain) unless you store the CA certificates from your Android system in <see cref="TrustedCerts"/> along with
	/// the self-signed certificate(s).</para>
	/// </remarks>
	public class AndroidMessageHandler : HttpMessageHandler
	{
		sealed class RequestRedirectionState
		{
			public Uri? NewUrl;
			public int RedirectCounter;
			public HttpMethod? Method;
			public bool MethodChanged;
		}

		/// <summary>
		/// Some requests require modification to the set of headers returned from the native client.
		/// However, the headers collection in it is immutable, so we need to perform the adjustments
		/// in CopyHeaders.  This class describes the necessary operations.
		/// </summary>
		sealed class ContentState
		{
			public bool? RemoveContentLengthHeader;

			/// <summary>
			/// If this is `true`, then `NewContentEncodingHeaderValue` is entirely ignored
			/// </summary>
			public bool? RemoveContentEncodingHeader;

			/// <summary>
			/// New 'Content-Encoding' header value. Ignored if not null and empty.
			/// </summary>
			public List<string>? NewContentEncodingHeaderValue;

			/// <summary>
			/// Reset the class to values that indicate there's no action to take.  MUST be
			/// called BEFORE any of the class members are assigned values and AFTER the state
			/// modification is applied
			/// </summary>
			public void Reset ()
			{
				RemoveContentEncodingHeader = null;
				RemoveContentLengthHeader = null;
				NewContentEncodingHeaderValue = null;
			}
		}

		internal const string LOG_APP = "monodroid-net";

		const string GZIP_ENCODING = "gzip";
		const string DEFLATE_ENCODING = "deflate";
		const string BROTLI_ENCODING = "br";
		const string IDENTITY_ENCODING = "identity";
		const string ContentEncodingHeaderName = "Content-Encoding";
		const string ContentLengthHeaderName = "Content-Length";

		static readonly IDictionary<string, string> headerSeparators = new Dictionary<string, string> {
			["User-Agent"] = " ",
		};

		static readonly HashSet <string> known_content_headers = new HashSet <string> (StringComparer.OrdinalIgnoreCase) {
			"Allow",
			"Content-Disposition",
			ContentEncodingHeaderName,
			"Content-Language",
			ContentLengthHeaderName,
			"Content-Location",
			"Content-MD5",
			"Content-Range",
			"Content-Type",
			"Expires",
			"Last-Modified"
		};

		static readonly List <IAndroidAuthenticationModule> authModules = new List <IAndroidAuthenticationModule> {
			new AuthModuleBasic (),
			new AuthModuleDigest ()
		};

		CookieContainer? _cookieContainer;
		DecompressionMethods _decompressionMethods;

		bool disposed;
		bool started;

		// Now all hail Java developers! Get this... HttpURLClient defaults to accepting AND
		// uncompressing the gzip content encoding UNLESS you set the Accept-Encoding header to ANY
		// value. So if we set it to 'gzip' below we WILL get gzipped stream but HttpURLClient will NOT
		// uncompress it any longer, doh. And they don't support 'deflate' so we need to handle it ourselves.
		bool decompress_here => _acceptEncoding is not null && _acceptEncoding != IDENTITY_ENCODING;
		string? _acceptEncoding;

		public bool SupportsAutomaticDecompression => true;
		public bool SupportsProxy => true;
		public bool SupportsRedirectConfiguration => true;

		public DecompressionMethods AutomaticDecompression
		{
			get => _decompressionMethods;
			set
			{
				CheckDisposedOrStarted ();

				_decompressionMethods = value;
				_acceptEncoding = null;

				if (value == DecompressionMethods.None) {
					_acceptEncoding = IDENTITY_ENCODING;
				} else {
					if ((value & DecompressionMethods.GZip) != 0) {
						_acceptEncoding = GZIP_ENCODING;
					}

					if ((value & DecompressionMethods.Deflate) != 0) {
						_acceptEncoding = _acceptEncoding is null ? DEFLATE_ENCODING : $"{_acceptEncoding}, {DEFLATE_ENCODING}";
					}

					if ((value & DecompressionMethods.Brotli) != 0) {
						_acceptEncoding = _acceptEncoding is null ? BROTLI_ENCODING : $"{_acceptEncoding}, {BROTLI_ENCODING}";
					}
				}
			}
		}

		public CookieContainer CookieContainer
		{
			get => _cookieContainer ?? (_cookieContainer = new CookieContainer ());
			set
			{
				if (value == null)
					throw new ArgumentNullException(nameof(value));

				_cookieContainer = value;
			}
		}

		// NOTE: defaults here are based on:
		// https://github.com/dotnet/runtime/blob/f3b77e64b87895aa7e697f321eb6d4151a4333df/src/libraries/Common/src/System/Net/Http/HttpHandlerDefaults.cs

		public bool UseCookies { get; set; } = true;

		public bool PreAuthenticate { get; set; } = false;

		public bool UseProxy { get; set; } = true;

		public IWebProxy? Proxy { get; set; }

		public ICredentials? Credentials { get; set; }

		public bool AllowAutoRedirect { get; set; } = true;

		public ClientCertificateOption ClientCertificateOptions { get; set; } = ClientCertificateOption.Manual;

		private X509CertificateCollection? _clientCertificates;
		public X509CertificateCollection? ClientCertificates
		{
			get
			{
				if (ClientCertificateOptions != ClientCertificateOption.Manual) {
					throw new InvalidOperationException ($"Enable manual options first on {nameof (ClientCertificateOptions)}");
				}

				return _clientCertificates ?? (_clientCertificates = new X509CertificateCollection ());
			}

			set => _clientCertificates = value;
		}

		public ICredentials? DefaultProxyCredentials { get; set; }

		public int MaxConnectionsPerServer { get; set; } = int.MaxValue;

		public int MaxResponseHeadersLength { get; set; } = 64; // Units in K (1024) bytes.

		public bool CheckCertificateRevocationList { get; set; } = false;

		ServerCertificateCustomValidator? _serverCertificateCustomValidator = null;

		public Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool>? ServerCertificateCustomValidationCallback
		{
			get => _serverCertificateCustomValidator?.Callback;
			set {
				if (value is null) {
					_serverCertificateCustomValidator = null;
				} else if (_serverCertificateCustomValidator is null) {
					_serverCertificateCustomValidator = new ServerCertificateCustomValidator (value);
				} else {
					_serverCertificateCustomValidator.Callback = value;
				}
			}
		}

		// See: https://developer.android.com/reference/javax/net/ssl/SSLSocket#protocols
		public SslProtocols SslProtocols { get; set; } =
			(int)Build.VERSION.SdkInt >= 29 ?
				SslProtocols.Tls13 | SslProtocols.Tls12 : SslProtocols.Tls12;

		public IDictionary<string, object?>? Properties { get; set; }

		int maxAutomaticRedirections = 50;

		public int MaxAutomaticRedirections
		{
			get => maxAutomaticRedirections;
			set {
				// https://github.com/dotnet/runtime/blob/913facdca8b04cc674163e31a7650ef6868a7d5b/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/SocketsHttpHandler.cs#L142-L145
				if (value <= 0)
					throw new ArgumentOutOfRangeException(nameof(value), value, "The specified value must be greater than 0");

				maxAutomaticRedirections = value;
			}
		}

		/// <summary>
		/// <para>
		/// Gets or sets the pre authentication data for the request. This property must be set by the application
		/// before the request is made. Generally the value can be taken from <see cref="RequestedAuthentication"/>
		/// after the initial request, without any authentication data, receives the authorization request from the
		/// server. The application must then store credentials in instance of <see cref="AuthenticationData"/> and
		/// assign the instance to this property before retrying the request.
		/// </para>
		/// <para>
		/// The property is never set by AndroidMessageHandler.
		/// </para>
		/// </summary>
		/// <value>The pre authentication data.</value>
		public AuthenticationData? PreAuthenticationData { get; set; }

		/// <summary>
		/// If the website requires authentication, this property will contain data about each scheme supported
		/// by the server after the response. Note that unauthorized request will return a valid response - you
		/// need to check the status code and and (re)configure AndroidMessageHandler instance accordingly by providing
		/// both the credentials and the authentication scheme by setting the <see cref="PreAuthenticationData"/>
		/// property. If AndroidMessageHandler is not able to detect the kind of authentication scheme it will store an
		/// instance of <see cref="AuthenticationData"/> with its <see cref="AuthenticationData.Scheme"/> property
		/// set to <c>AuthenticationScheme.Unsupported</c> and the application will be responsible for providing an
		/// instance of <see cref="IAndroidAuthenticationModule"/> which handles this kind of authorization scheme
		/// (<see cref="AuthenticationData.AuthModule"/>
		/// </summary>
		public IList <AuthenticationData>? RequestedAuthentication { get; private set; }

		/// <summary>
		/// Server authentication response indicates that the request to authorize comes from a proxy if this property is <c>true</c>.
		/// All the instances of <see cref="AuthenticationData"/> stored in the <see cref="RequestedAuthentication"/> property will
		/// have their <see cref="AuthenticationData.UseProxyAuthentication"/> preset to the same value as this property.
		/// </summary>
		public bool ProxyAuthenticationRequested { get; private set; }

		/// <summary>
		/// If <c>true</c> then the server requested authorization and the application must use information
		/// found in <see cref="RequestedAuthentication"/> to set the value of <see cref="PreAuthenticationData"/>
		/// </summary>
		[MemberNotNullWhen(true, nameof(RequestedAuthentication))]
		public bool RequestNeedsAuthorization {
			get { return RequestedAuthentication?.Count > 0; }
		}

		/// <summary>
		/// <para>
		/// If the request is to the server protected with a self-signed (or otherwise untrusted) SSL certificate, the request will
		/// fail security chain verification unless the application provides either the CA certificate of the entity which issued the
		/// server's certificate or, alternatively, provides the server public key. Whichever the case, the certificate(s) must be stored
		/// in this property in order for AndroidMessageHandler to configure the request to accept the server certificate.</para>
		/// <para>AndroidMessageHandler uses a custom <see cref="KeyStore"/> and <see cref="TrustManagerFactory"/> to configure the connection.
		/// If, however, the application requires finer control over the SSL configuration (e.g. it implements its own TrustManager) then
		/// it should leave this property empty and instead derive a custom class from AndroidMessageHandler and override, as needed, the
		/// <see cref="ConfigureTrustManagerFactory"/>, <see cref="ConfigureKeyManagerFactory"/> and <see cref="ConfigureKeyStore"/> methods
		/// instead</para>
		/// </summary>
		/// <value>The trusted certs.</value>
		public IList <Certificate>? TrustedCerts { get; set; }

		/// <summary>
		/// <para>
		/// Specifies the connection read timeout.
		/// </para>
		/// <para>
		/// Since there's no way for the handler to access <see cref="t:System.Net.Http.HttpClient.Timeout"/>
		/// directly, this property should be set by the calling party to the same desired value. Value of this
		/// property will be passed to the native Java HTTP client, unless it is set to <see
		/// cref="t:System.TimeSpan.Zero"/>
		/// </para>
		/// <para>
		/// The default value is <c>24</c> hours, much higher than the documented value of <see
		/// cref="t:System.Net.Http.HttpClient.Timeout"/> and the same as the value of iOS-specific
		/// NSUrlSessionHandler.
		/// </para>
		/// </summary>
		public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromHours (24);

		/// <summary>
		/// A feature switch that determines whether the message handler should attempt to authenticate the user
		/// using the NTLM/Negotiate authentication method. Enable the feature by adding
		/// <c><AndroidUseNegotiateAuthentication>true</AndroidUseNegotiateAuthentication></c> to your project file.
		/// </summary>
		static bool NegotiateAuthenticationIsEnabled =>
			AppContext.TryGetSwitch ("Xamarin.Android.Net.UseNegotiateAuthentication", out bool isEnabled) && isEnabled;

		/// <summary>
		/// <para>
		/// Specifies the connect timeout
		/// </para>
		/// <para>
		/// The native Java client supports two separate timeouts - one for reading from the connection (<see
		/// cref="ReadTimeout"/>) and another for establishing the connection. This property sets the value of
		/// the latter timeout, unless it is set to <see cref="t:System.TimeSpan.Zero"/> in which case the
		/// native Java client defaults are used.
		/// </para>
		/// <para>
		/// The default value is <c>120</c> seconds.
		/// </para>
		/// </summary>
		public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromHours (24);

		protected override void Dispose (bool disposing)
		{
			disposed = true;

			base.Dispose (disposing);
		}

		protected void AssertSelf ()
		{
			if (!disposed)
				return;
			throw new ObjectDisposedException (nameof (AndroidMessageHandler));
		}

		void CheckDisposedOrStarted ()
		{
			AssertSelf ();
			if (started) {
				throw new InvalidOperationException ("The handler has already started sending requests");
			}
		}

		string EncodeUrl (Uri url)
		{
			if (url == null)
				return String.Empty;

			// UriBuilder takes care of encoding everything properly
			var bldr = new UriBuilder (url);
			if (url.IsDefaultPort)
				bldr.Port = -1; // Avoids adding :80 or :443 to the host name in the result

			// bldr.Uri.ToString () would ruin the good job UriBuilder did
			return bldr.ToString ();
		}

		/// <summary>
		/// Returns a custom host name verifier for a HTTPS connection. By default it returns <c>null</c> and
		/// thus the connection uses whatever host name verification mechanism the operating system defaults to.
		/// Override in your class to define custom host name verification behavior. The overriding class should
		/// not set the <see cref="m:HttpsURLConnection.HostnameVerifier"/> property directly on the passed
		/// <paramref name="connection"/>
		/// </summary>
		/// <returns>Instance of IHostnameVerifier to be used for this HTTPS connection</returns>
		/// <param name="connection">HTTPS connection object.</param>
		protected virtual IHostnameVerifier? GetSSLHostnameVerifier (HttpsURLConnection connection)
		{
			return _serverCertificateCustomValidator?.HostnameVerifier;
		}

		internal IHostnameVerifier? GetSSLHostnameVerifierInternal (HttpsURLConnection connection)
			=> GetSSLHostnameVerifier (connection);

		/// <summary>
		/// Creates, configures and processes an asynchronous request to the indicated resource.
		/// </summary>
		/// <returns>Task in which the request is executed</returns>
		/// <param name="request">Request provided by <see cref="System.Net.Http.HttpClient"/></param>
		/// <param name="cancellationToken">Cancellation token.</param>
		protected override Task <HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (NegotiateAuthenticationIsEnabled) {
				return SendWithNegotiateAuthenticationAsync (request, cancellationToken);
			}

			return DoSendAsync (request, cancellationToken);
		}

		async Task <HttpResponseMessage> SendWithNegotiateAuthenticationAsync (HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var response = await DoSendAsync (request, cancellationToken).ConfigureAwait (false);

			if (RequestNeedsAuthorization && NegotiateAuthenticationHelper.RequestNeedsNegotiateAuthentication (this, request, out var requestedAuth)) {
				var authenticatedResponse = await NegotiateAuthenticationHelper.SendWithAuthAsync (this, request, requestedAuth, cancellationToken).ConfigureAwait (false);
				if (authenticatedResponse != null)
					return authenticatedResponse;
			}

			return response;
		}

		internal async Task <HttpResponseMessage> DoSendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
		{
			started = true;
			AssertSelf ();
			if (request == null)
				throw new ArgumentNullException (nameof (request));

			if (request.RequestUri is null || !request.RequestUri.IsAbsoluteUri)
				throw new ArgumentException ("Must represent an absolute URI", "request");

			var redirectState = new RequestRedirectionState {
				NewUrl = request.RequestUri,
				RedirectCounter = 0,
				Method = request.Method
			};
			while (true) {
				URL java_url = new URL (EncodeUrl (redirectState.NewUrl));
				URLConnection? java_connection;
				if (UseProxy) {
					var javaProxy = await GetJavaProxy (redirectState.NewUrl, cancellationToken).ConfigureAwait (continueOnCapturedContext: false);
					// When you use the parameter Java.Net.Proxy.NoProxy the system proxy is overriden. Leave the parameter out to respect the default settings.
					java_connection = javaProxy == Java.Net.Proxy.NoProxy ? java_url.OpenConnection () : java_url.OpenConnection (javaProxy);
				} else {
					// In this case the consumer of this class has explicitly chosen to not use a proxy, so bypass the default proxy. The default value of UseProxy is true.
					java_connection = java_url.OpenConnection (Java.Net.Proxy.NoProxy);
				}

				var httpsConnection = java_connection as HttpsURLConnection;
				if (httpsConnection != null) {
					IHostnameVerifier? hnv = GetSSLHostnameVerifier (httpsConnection);
					if (hnv != null)
						httpsConnection.HostnameVerifier = hnv;
				}

				if (ConnectTimeout != TimeSpan.Zero)
					java_connection!.ConnectTimeout = checked ((int)ConnectTimeout.TotalMilliseconds);

				if (ReadTimeout != TimeSpan.Zero)
					java_connection!.ReadTimeout = checked ((int)ReadTimeout.TotalMilliseconds);

				try {
					HttpURLConnection httpConnection = await SetupRequestInternal (request, java_connection!).ConfigureAwait (continueOnCapturedContext: false);
					HttpResponseMessage? response = await ProcessRequest (request, java_url, httpConnection, cancellationToken, redirectState).ConfigureAwait (continueOnCapturedContext: false);
					if (response != null)
						return response;

					if (redirectState.NewUrl == null)
						throw new InvalidOperationException ("Request redirected but no new URI specified");
					request.Method = redirectState.Method;
					request.RequestUri = redirectState.NewUrl;
				} catch (Java.Net.SocketTimeoutException ex) when (JNIEnv.ShouldWrapJavaException (ex)) {
					throw new WebException (ex.Message, ex, WebExceptionStatus.Timeout, null);
				} catch (Java.Net.UnknownServiceException ex) when (JNIEnv.ShouldWrapJavaException (ex)) {
					throw new WebException (ex.Message, ex, WebExceptionStatus.ProtocolError, null);
				} catch (Java.Lang.SecurityException ex) when (JNIEnv.ShouldWrapJavaException (ex)) {
					throw new WebException (ex.Message, ex, WebExceptionStatus.SecureChannelFailure, null);
				} catch (Java.IO.IOException ex) when (JNIEnv.ShouldWrapJavaException (ex)) {
					throw new WebException (ex.Message, ex, WebExceptionStatus.UnknownError, null);
				}
			}
		}

		internal Task <HttpResponseMessage> SendAsyncInternal (HttpRequestMessage request, CancellationToken cancellationToken)
			=> SendAsync (request, cancellationToken);

		protected virtual async Task <Java.Net.Proxy?> GetJavaProxy (Uri destination, CancellationToken cancellationToken)
		{
			var proxy = Java.Net.Proxy.NoProxy;

			if (destination == null || Proxy == null) {
				return proxy;
			}

			Uri? puri = Proxy.GetProxy (destination);
			if (puri == null) {
				return proxy;
			}

			proxy = await Task <Java.Net.Proxy>.Run (() => {
				// Let the Java code resolve the address, if necessary
				var addr = new Java.Net.InetSocketAddress (puri.Host, puri.Port);
				return new Java.Net.Proxy (Java.Net.Proxy.Type.Http, addr);
			}, cancellationToken);

			return proxy;
		}

		internal Task <Java.Net.Proxy?> GetJavaProxyInternal (Uri destination, CancellationToken cancellationToken)
			=> GetJavaProxy (destination, cancellationToken);

		Task <HttpResponseMessage?> ProcessRequest (HttpRequestMessage request, URL javaUrl, HttpURLConnection httpConnection, CancellationToken cancellationToken, RequestRedirectionState redirectState)
		{
			cancellationToken.ThrowIfCancellationRequested ();
			httpConnection.InstanceFollowRedirects = false; // We handle it ourselves
			RequestedAuthentication = null;
			ProxyAuthenticationRequested = false;

			return DoProcessRequest (request, javaUrl, httpConnection, cancellationToken, redirectState);
		}

		Task DisconnectAsync (HttpURLConnection httpConnection)
		{
			return Task.Run (() => httpConnection?.Disconnect ());
		}

		Task ConnectAsync (HttpURLConnection httpConnection, CancellationToken ct)
		{
			return Task.Run (() => {
				try {
					using (ct.Register(() => DisconnectAsync(httpConnection).ContinueWith(t => {
							if (t.Exception != null) Logger.Log(LogLevel.Info, LOG_APP, $"Disconnection exception: {t.Exception}");
						}, TaskScheduler.Default)))
						httpConnection?.Connect ();
				} catch (Exception ex) {
					if (ct.IsCancellationRequested) {
						Logger.Log (LogLevel.Info, LOG_APP, $"Exception caught while cancelling connection: {ex}");
						ct.ThrowIfCancellationRequested ();
					}

					// All exceptions related to connectivity should be wrapped in HttpRequestException. In theory it is possible that the exception will be
					// thrown for another reason above, but it's OK to wrap it in HttpRequestException anyway, since we're working in the context of
					// `ConnectAsync` which, from the end user's point of view, is 100% related to connectivity.
					throw new HttpRequestException ("Connection failure", ex);
				}
			}, ct);
		}

		protected virtual async Task WriteRequestContentToOutput (HttpRequestMessage request, HttpURLConnection httpConnection, CancellationToken cancellationToken)
		{
			if (request.Content is null)
				return;

			var stream = await request.Content.ReadAsStreamAsync ().ConfigureAwait (false);
			await stream.CopyToAsync(httpConnection.OutputStream!, 4096, cancellationToken).ConfigureAwait(false);

			//
			// Rewind the stream to beginning in case the HttpContent implementation
			// will be accessed again (e.g. after redirect) and it keeps its stream
			// open behind the scenes instead of recreating it on the next call to
			// ReadAsStreamAsync. If we don't rewind it, the ReadAsStreamAsync
			// call above will throw an exception as we'd be attempting to read an
			// already "closed" stream (that is one whose Position is set to its
			// end).
			//
			// This is not a perfect solution since the HttpContent may do weird
			// things in its implementation, but it's better than copying the
			// content into a buffer since we have no way of knowing how the data is
			// read or generated and also we don't want to keep potentially large
			// amounts of data in memory (which would happen if we read the content
			// into a byte[] buffer and kept it cached for re-use on redirect).
			//
			// See https://bugzilla.xamarin.com/show_bug.cgi?id=55477
			//
			if (stream.CanSeek)
				stream.Seek (0, SeekOrigin.Begin);
		}

		internal Task WriteRequestContentToOutputInternal (HttpRequestMessage request, HttpURLConnection httpConnection, CancellationToken cancellationToken)
			=> WriteRequestContentToOutput (request, httpConnection, cancellationToken);

		async Task <HttpResponseMessage?> DoProcessRequest (HttpRequestMessage request, URL javaUrl, HttpURLConnection httpConnection, CancellationToken cancellationToken, RequestRedirectionState redirectState)
		{
			if (Logger.LogNet)
				Logger.Log (LogLevel.Info, LOG_APP, $"{this}.DoProcessRequest ()");

			if (cancellationToken.IsCancellationRequested) {
				if(Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, " cancelled");

				cancellationToken.ThrowIfCancellationRequested ();
			}

			try {
				if (Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, $"  connecting");

				await ConnectAsync (httpConnection, cancellationToken).ConfigureAwait (continueOnCapturedContext: false);
				if (Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, $"  connected");
			} catch (Java.Net.ConnectException ex) {
				if (Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, $"Connection exception {ex}");
				// Wrap it nicely in a "standard" exception so that it's compatible with HttpClientHandler
				throw new WebException (ex.Message, ex, WebExceptionStatus.ConnectFailure, null);
			}

			if (cancellationToken.IsCancellationRequested) {
				if(Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, " cancelled");

				await DisconnectAsync (httpConnection).ConfigureAwait (continueOnCapturedContext: false);
				cancellationToken.ThrowIfCancellationRequested ();
			}

			CancellationTokenRegistration cancelRegistration = default (CancellationTokenRegistration);
			HttpStatusCode statusCode = HttpStatusCode.OK;
			Uri? connectionUri = null;
			var contentState = new ContentState ();

			try {
				cancelRegistration = cancellationToken.Register (() => {
					DisconnectAsync (httpConnection).ContinueWith (t => {
						if (t.Exception != null)
							Logger.Log (LogLevel.Info, LOG_APP, $"Disconnection exception: {t.Exception}");
					}, TaskScheduler.Default);
				}, useSynchronizationContext: false);

				if (httpConnection.DoOutput)
					await WriteRequestContentToOutput (request, httpConnection, cancellationToken);

				statusCode = await Task.Run (() => (HttpStatusCode)httpConnection.ResponseCode, cancellationToken).ConfigureAwait (false);
				connectionUri = new Uri (httpConnection.URL?.ToString ()!);
			} finally {
				cancelRegistration.Dispose ();
			}

			if (cancellationToken.IsCancellationRequested) {
				await DisconnectAsync (httpConnection).ConfigureAwait (continueOnCapturedContext: false);
				cancellationToken.ThrowIfCancellationRequested();
			}

			// If the request was redirected we need to put the new URL in the request
			request.RequestUri = connectionUri;
			var ret = new AndroidHttpResponseMessage (javaUrl, httpConnection) {
				RequestMessage = request,
				ReasonPhrase = httpConnection.ResponseMessage,
				StatusCode = statusCode,
			};

			if (Logger.LogNet)
				Logger.Log (LogLevel.Info, LOG_APP, $"Status code: {statusCode}");

			if (!IsErrorStatusCode (statusCode)) {
				if (Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, $"Reading...");
				ret.Content = GetContent (httpConnection, httpConnection.InputStream!, contentState);
			} else {
				if (Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, $"Status code is {statusCode}, reading...");
				// For 400 >= response code <= 599 the Java client throws the FileNotFound exception when attempting to read from the input stream.
				// Instead we try to read the error stream and return an empty string if the error stream isn't readable.
				ret.Content = GetErrorContent (httpConnection, new StringContent (String.Empty, Encoding.ASCII), contentState);
			}

			bool disposeRet;
			if (HandleRedirect (statusCode, httpConnection, redirectState, out disposeRet)) {
				if (redirectState.MethodChanged) {
					// If a redirect uses GET but the original request used POST with content, then the redirected
					// request will fail with an exception.
					// There's also no way to send content using GET (except in the URL, of course), so discarding
					// request.Content is what we should do.
					//
					// See https://github.com/xamarin/xamarin-android/issues/1282
					if (redirectState.Method == HttpMethod.Get) {
						if (Logger.LogNet)
							Logger.Log (LogLevel.Info, LOG_APP, $"Discarding content on redirect");
						request.Content = null;
					}
				}

				CopyHeaders (httpConnection, ret, contentState);
				ParseCookies (ret, connectionUri);

				if (disposeRet) {
					ret.Dispose ();
					ret = null!;
				}

				// We don't want to pass the authorization header onto the next location
				request.Headers.Authorization = null;

				return ret;
			}

			switch (statusCode) {
				case HttpStatusCode.Unauthorized:
				case HttpStatusCode.ProxyAuthenticationRequired:
					// We don't resend the request since that would require new set of credentials if the
					// ones provided in Credentials are invalid (or null) and that, in turn, may require asking the
					// user which is not something that should be taken care of by us and in this
					// context. The application should be responsible for this.
					// HttpClientHandler throws an exception in this instance, but I think it's not a good
					// idea. We'll return the response message with all the information required by the
					// application to fill in the blanks and provide the requested credentials instead.
					//
					// We return the body of the response too, but the Java client will throw
					// a FileNotFound exception if we attempt to access the input stream.
					// Instead we try to read the error stream and return an default message if the error stream isn't readable.
					ret.Content = GetErrorContent (httpConnection, new StringContent ("Unauthorized", Encoding.ASCII), contentState);
					CopyHeaders (httpConnection, ret, contentState);

					if (ret.Headers.WwwAuthenticate != null) {
						ProxyAuthenticationRequested = false;
						CollectAuthInfo (ret.Headers.WwwAuthenticate);
					} else if (ret.Headers.ProxyAuthenticate != null) {
						ProxyAuthenticationRequested = true;
						CollectAuthInfo (ret.Headers.ProxyAuthenticate);
					}

					ret.RequestedAuthentication = RequestedAuthentication;
					return ret;
			}

			CopyHeaders (httpConnection, ret, contentState);
			ParseCookies (ret, connectionUri);

			if (Logger.LogNet)
				Logger.Log (LogLevel.Info, LOG_APP, $"Returning");
			return ret;
		}

		HttpContent GetErrorContent (HttpURLConnection httpConnection, HttpContent fallbackContent, ContentState contentState)
		{
			var contentStream = httpConnection.ErrorStream;

			if (contentStream != null) {
				return GetContent (httpConnection, contentStream, contentState);
			}

			return fallbackContent;
		}

		Stream GetDecompressionWrapper (URLConnection httpConnection, Stream inputStream, ContentState contentState)
		{
			contentState.Reset ();
			if (!decompress_here || String.IsNullOrEmpty (httpConnection.ContentEncoding)) {
				return inputStream;
			}

			var encodings = new HashSet<string> (httpConnection.ContentEncoding.Split (','), StringComparer.OrdinalIgnoreCase);
			Stream? ret = null;
			string? supportedEncoding = null;
			if (encodings.Contains (GZIP_ENCODING)) {
				supportedEncoding = GZIP_ENCODING;
				ret = new GZipStream (inputStream, CompressionMode.Decompress);
			} else if (encodings.Contains (DEFLATE_ENCODING)) {
				supportedEncoding = DEFLATE_ENCODING;
				ret = new DeflateStream (inputStream, CompressionMode.Decompress);
			} else if (encodings.Contains (BROTLI_ENCODING)) {
				supportedEncoding = BROTLI_ENCODING;
				ret = new BrotliStream (inputStream, CompressionMode.Decompress);
			}

			if (!String.IsNullOrEmpty (supportedEncoding)) {
				contentState.RemoveContentLengthHeader = true;

				encodings.Remove (supportedEncoding!);
				if (encodings.Count == 0) {
					contentState.RemoveContentEncodingHeader = true;
				} else {
					contentState.NewContentEncodingHeaderValue = new List<string> (encodings);
				}
			}

			return ret ?? inputStream;
		}

		HttpContent GetContent (URLConnection httpConnection, Stream contentStream, ContentState contentState)
		{
			Stream inputStream = GetDecompressionWrapper (httpConnection, new BufferedStream (contentStream), contentState);
			return new StreamContent (inputStream);
		}

		bool HandleRedirect (HttpStatusCode redirectCode, HttpURLConnection httpConnection, RequestRedirectionState redirectState, out bool disposeRet)
		{
			if (!AllowAutoRedirect) {
				disposeRet = false;
				return true; // We shouldn't follow and there's no data to fetch, just return
			}
			disposeRet = true;

			redirectState.NewUrl = null;
			redirectState.MethodChanged = false;
			switch (redirectCode) {
				case HttpStatusCode.MultipleChoices:   // 300
					break;

				case HttpStatusCode.Moved:             // 301
				case HttpStatusCode.Redirect:          // 302
				case HttpStatusCode.SeeOther:          // 303
					redirectState.MethodChanged = redirectState.Method != HttpMethod.Get;
					redirectState.Method = HttpMethod.Get;
					break;

				case HttpStatusCode.NotModified:       // 304
					disposeRet = false;
					return true; // Not much happening here, just return and let the client decide
						     // what to do with the response

				case HttpStatusCode.TemporaryRedirect: // 307
				case HttpStatusCode.PermanentRedirect: // 308
					break;

				default:
					if ((int)redirectCode >= 300 && (int)redirectCode < 400)
						throw new InvalidOperationException ($"HTTP Redirection status code {redirectCode} ({(int)redirectCode}) not supported");
					return false;
			}

			var headers = httpConnection.HeaderFields;
			IList <string>? locationHeader = null;
			string? location = null;

			if (headers?.TryGetValue ("Location", out locationHeader) == true && locationHeader != null && locationHeader.Count > 0) {
				if (locationHeader.Count == 1) {
					location = locationHeader [0]?.Trim ();
				} else {
					if (Logger.LogNet)
						Logger.Log (LogLevel.Info, LOG_APP, $"More than one location header for HTTP {redirectCode} redirect. Will use the first non-empty one.");

					foreach (string l in locationHeader) {
						location = l?.Trim ();
						if (!String.IsNullOrEmpty (location))
							break;
					}
				}
			}

			if (String.IsNullOrEmpty (location)) {
				// As per https://tools.ietf.org/html/rfc7231#section-6.4.1 the reponse isn't required to contain the Location header and the
				// client should act accordingly. Since it is not documented what the action in this case should be, we're following what
				// Xamarin.iOS does and simply return the content of the request as if it wasn't a redirect.
				// It is not clear what to do if there is a Location header but its value is empty, so
				// we assume the same action here.
				disposeRet = false;
				return true;
			}

			redirectState.RedirectCounter++;
			if (redirectState.RedirectCounter >= MaxAutomaticRedirections)
				throw new WebException ($"Maximum automatic redirections exceeded (allowed {MaxAutomaticRedirections}, redirected {redirectState.RedirectCounter} times)");

			Uri redirectUrl;
			try {
				if (Logger.LogNet)
					Logger.Log (LogLevel.Debug, LOG_APP, $"Raw redirect location: {location}");

				var baseUrl = new Uri (httpConnection.URL?.ToString ()!);
				if (location? [0] == '/') {
					// Shortcut for the '/' and '//' cases, simplifies logic since URI won't treat
					// such URLs as relative and we'd have to work around it in the `else` block
					// below.
					redirectUrl = new Uri (baseUrl, location);
				} else {
					// Special case (from https://tools.ietf.org/html/rfc3986#section-5.4.1) not
					// handled by the Uri class: scheme:host
					//
					// This is a valid URI (should be treated as `scheme://host`) but URI throws an
					// exception about DOS path being malformed IF the part before colon is just one
					// character long... We could replace the scheme with the original request's one, but
					// that would NOT be the right thing to do since it is not what the redirecting server
					// meant. The fix doesn't belong here, but rather in the Uri class. So we'll throw...

					redirectUrl = new Uri (location!, UriKind.RelativeOrAbsolute);
					if (!redirectUrl.IsAbsoluteUri)
						redirectUrl = new Uri (baseUrl, location);
				}

				if (Logger.LogNet)
					Logger.Log (LogLevel.Debug, LOG_APP, $"Cooked redirect location: {redirectUrl}");
			} catch (Exception ex) {
				throw new WebException ($"Invalid redirect URI received: {location}", ex);
			}

			UriBuilder? builder = null;
			if (!String.IsNullOrEmpty (httpConnection.URL?.Ref) && String.IsNullOrEmpty (redirectUrl.Fragment)) {
				if (Logger.LogNet)
					Logger.Log (LogLevel.Debug, LOG_APP, $"Appending fragment '{httpConnection.URL?.Ref}' to redirect URL '{redirectUrl}'");

				builder = new UriBuilder (redirectUrl) {
					Fragment = httpConnection.URL?.Ref
				};
			}

			redirectState.NewUrl = builder == null ? redirectUrl : builder.Uri;
			if (Logger.LogNet)
				Logger.Log (LogLevel.Debug, LOG_APP, $"Request redirected to {redirectState.NewUrl}");

			return true;
		}

		bool IsErrorStatusCode (HttpStatusCode statusCode)
		{
			return (int)statusCode >= 400 && (int)statusCode <= 599;
		}

		void CollectAuthInfo (HttpHeaderValueCollection <AuthenticationHeaderValue> headers)
		{
			var authData = new List <AuthenticationData> (headers.Count);

			foreach (AuthenticationHeaderValue ahv in headers) {
				var data = new AuthenticationData {
					Scheme = GetAuthScheme (ahv.Scheme),
					Challenge = $"{ahv.Scheme} {ahv.Parameter}",
					UseProxyAuthentication = ProxyAuthenticationRequested
				};
				authData.Add (data);
			}

			RequestedAuthentication = authData.AsReadOnly ();
		}

		AuthenticationScheme GetAuthScheme (string scheme)
		{
			if (String.Compare ("basic", scheme, StringComparison.OrdinalIgnoreCase) == 0)
				return AuthenticationScheme.Basic;
			if (String.Compare ("digest", scheme, StringComparison.OrdinalIgnoreCase) == 0)
				return AuthenticationScheme.Digest;

			return AuthenticationScheme.Unsupported;
		}

		void ParseCookies (AndroidHttpResponseMessage ret, Uri connectionUri)
		{
			if (!UseCookies || CookieContainer == null || !ret.Headers.TryGetValues ("Set-Cookie", out var cookieHeaderValue) || cookieHeaderValue == null) {
				if (Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, $"No cookies");
				return;
			}

			try {
				if (Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, $"Parsing cookies");
				CookieContainer.SetCookies (connectionUri, String.Join (",", cookieHeaderValue));
			} catch (Exception ex) {
				// We don't want to terminate the response because of a bad cookie, hence just reporting
				// the issue. We might consider adding a virtual method to let the user handle the
				// issue, but not sure if it's really needed. Set-Cookie header will be part of the
				// header collection so the user can always examine it if they spot an error.
				if (Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, $"Failed to parse cookies in the server response. {ex.GetType ()}: {ex.Message}");
			}
		}

		void CopyHeaders (HttpURLConnection httpConnection, HttpResponseMessage response, ContentState contentState)
		{
			var headers = httpConnection.HeaderFields;
			bool removeContentLength = contentState.RemoveContentLengthHeader ?? false;
			bool removeContentEncoding = contentState.RemoveContentEncodingHeader ?? false;
			bool setNewContentEncodingValue = !removeContentEncoding && contentState.NewContentEncodingHeaderValue != null && contentState.NewContentEncodingHeaderValue.Count > 0;

			foreach (var key in headers!.Keys) {
				if (key == null) // First header entry has null key, it corresponds to the response message
					continue;

				HttpHeaders item_headers;

				if (known_content_headers.Contains (key)) {
					item_headers = response.Content.Headers;
				} else {
					item_headers = response.Headers;
				}

				IEnumerable<string> values = headers [key];
				if (removeContentLength && String.Compare (ContentLengthHeaderName, key, StringComparison.OrdinalIgnoreCase) == 0) {
					removeContentLength = false;
					continue;
				}

				if ((removeContentEncoding || setNewContentEncodingValue) && String.Compare (ContentEncodingHeaderName, key, StringComparison.OrdinalIgnoreCase) == 0) {
					if (removeContentEncoding) {
						removeContentEncoding = false;
						continue;
					}

					setNewContentEncodingValue = false;
					values = contentState.NewContentEncodingHeaderValue!;
				}
				item_headers.TryAddWithoutValidation (key, values);
			}
			contentState.Reset ();
		}

		/// <summary>
		/// Configure the <see cref="HttpURLConnection"/> before the request is sent. This method is meant to be overriden
		/// by applications which need to perform some extra configuration steps on the connection. It is called with all
		/// the request headers set, pre-authentication performed (if applicable) but before the request body is set
		/// (e.g. for POST requests). The default implementation in AndroidMessageHandler does nothing.
		/// </summary>
		/// <param name="request">Request data</param>
		/// <param name="conn">Pre-configured connection instance</param>
		protected virtual Task SetupRequest (HttpRequestMessage request, HttpURLConnection conn)
		{
			AssertSelf ();

			return Task.CompletedTask;
		}

		internal Task SetupRequestInternal (HttpRequestMessage request, HttpURLConnection conn)
			=> SetupRequest (request, conn);

		/// <summary>
		/// Configures the key store. The <paramref name="keyStore"/> parameter is set to instance of <see cref="KeyStore"/>
		/// created using the <see cref="KeyStore.DefaultType"/> type and with populated with certificates provided in the <see cref="TrustedCerts"/>
		/// property. AndroidMessageHandler implementation simply returns the instance passed in the <paramref name="keyStore"/> parameter
		/// </summary>
		/// <returns>The key store.</returns>
		/// <param name="keyStore">Key store to configure.</param>
		protected virtual KeyStore? ConfigureKeyStore (KeyStore? keyStore)
		{
			AssertSelf ();

			return keyStore;
		}

		internal KeyStore? ConfigureKeyStoreInternal (KeyStore? keyStore)
			=> ConfigureKeyStore (keyStore);

		/// <summary>
		/// Create and configure an instance of <see cref="KeyManagerFactory"/>. The <paramref name="keyStore"/> parameter is set to the
		/// return value of the <see cref="ConfigureKeyStore"/> method, so it might be null if the application overrode the method and provided
		/// no key store. It will not be <c>null</c> when the default implementation is used. The application can return <c>null</c> here since
		/// KeyManagerFactory is not required for the custom SSL configuration, but it might be used by the application to implement a more advanced
		/// mechanism of key management.
		/// </summary>
		/// <returns>The key manager factory or <c>null</c>.</returns>
		/// <param name="keyStore">Key store.</param>
		protected virtual KeyManagerFactory? ConfigureKeyManagerFactory (KeyStore? keyStore)
		{
			AssertSelf ();

			return null;
		}

		internal KeyManagerFactory? ConfigureKeyManagerFactoryInternal (KeyStore? keyStore)
			=> ConfigureKeyManagerFactoryInternal (keyStore);

		/// <summary>
		/// Create and configure an instance of <see cref="TrustManagerFactory"/>. The <paramref name="keyStore"/> parameter is set to the
		/// return value of the <see cref="ConfigureKeyStore"/> method, so it might be null if the application overrode the method and provided
		/// no key store. It will not be <c>null</c> when the default implementation is used. The application can return <c>null</c> from this
		/// method in which case AndroidMessageHandler will create its own instance of the trust manager factory provided that the <see cref="TrustCerts"/>
		/// list contains at least one valid certificate. If there are no valid certificates and this method returns <c>null</c>, no custom
		/// trust manager will be created since that would make all the HTTPS requests fail.
		/// </summary>
		/// <returns>The trust manager factory.</returns>
		/// <param name="keyStore">Key store.</param>
		protected virtual TrustManagerFactory? ConfigureTrustManagerFactory (KeyStore? keyStore)
		{
			AssertSelf ();

			return null;
		}

		internal TrustManagerFactory? ConfigureTrustManagerFactoryInternal (KeyStore? keyStore)
			=> ConfigureTrustManagerFactory (keyStore);

		async Task <HttpURLConnection> SetupRequestInternal (HttpRequestMessage request, URLConnection conn)
		{
			if (conn == null)
				throw new ArgumentNullException (nameof (conn));
			var httpConnection = conn.JavaCast <HttpURLConnection> ();
			if (httpConnection == null)
				throw new InvalidOperationException ($"Unsupported URL scheme {conn.URL?.Protocol}");

			try {
				httpConnection.RequestMethod = request.Method.ToString ();
			} catch (Java.Net.ProtocolException ex) when (JNIEnv.ShouldWrapJavaException (ex)) {
				throw new WebException (ex.Message, ex, WebExceptionStatus.ProtocolError, null);
			}

			// SSL context must be set up as soon as possible, before adding any content or
			// headers. Otherwise Java won't use the socket factory
			SetupSSL (httpConnection as HttpsURLConnection, request);
			if (request.Content != null)
				AddHeaders (httpConnection, request.Content.Headers);
			AddHeaders (httpConnection, request.Headers);

			if (_acceptEncoding is not null)
				httpConnection.SetRequestProperty ("Accept-Encoding", _acceptEncoding);

			if (UseCookies && CookieContainer != null && request.RequestUri is not null) {
				string cookieHeaderValue = CookieContainer.GetCookieHeader (request.RequestUri);
				if (!String.IsNullOrEmpty (cookieHeaderValue))
					httpConnection.SetRequestProperty ("Cookie", cookieHeaderValue);
			}

			HandlePreAuthentication (httpConnection);
			await SetupRequest (request, httpConnection).ConfigureAwait (continueOnCapturedContext: false);;
			SetupRequestBody (httpConnection, request);

			return httpConnection;
		}

		/// <summary>
		/// Configure and return a custom <see cref="t:SSLSocketFactory"/> for the passed HTTPS <paramref
		/// name="connection"/>. If the class overriding the method returns anything but the default
		/// <c>null</c>, the SSL setup code will not call the <see cref="ConfigureKeyManagerFactory"/> nor the
		/// <see cref="ConfigureTrustManagerFactory"/> methods used to configure a custom trust manager which is
		/// then used to create a default socket factory.
		/// Deriving class must perform all the key manager and trust manager configuration to ensure proper
		/// operation of the returned socket factory.
		/// </summary>
		/// <returns>Instance of SSLSocketFactory ready to use with the HTTPS connection.</returns>
		/// <param name="connection">HTTPS connection to return socket factory for</param>
		protected virtual SSLSocketFactory? ConfigureCustomSSLSocketFactory (HttpsURLConnection connection)
		{
			return null;
		}

		internal SSLSocketFactory? ConfigureCustomSSLSocketFactoryInternal (HttpsURLConnection connection)
			=> ConfigureCustomSSLSocketFactoryInternal (connection);

		void SetupSSL (HttpsURLConnection? httpsConnection, HttpRequestMessage requestMessage)
		{
			if (httpsConnection == null)
				return;

			var socketFactory = ConfigureCustomSSLSocketFactory (httpsConnection);
			if (socketFactory != null) {
				httpsConnection.SSLSocketFactory = socketFactory;
				return;
			}

			KeyStore keyStore = GetConfiguredKeyStoreInstance ();
			KeyManagerFactory? kmf = GetConfiguredKeyManagerFactory (keyStore);
			TrustManagerFactory? tmf = ConfigureTrustManagerFactory (keyStore);

			// If there is no customization there is no point in changing the behavior of the default SSL socket factory.
			if (tmf is null && kmf is null && !HasTrustedCerts && !HasServerCertificateCustomValidationCallback && !HasClientCertificates) {
				return;
			}

			var context = SSLContext.GetInstance ("TLS") ?? throw new InvalidOperationException ("Failed to get the SSLContext instance for TLS");
			var trustManagers = GetTrustManagers (tmf, keyStore, requestMessage);
			context.Init (kmf?.GetKeyManagers (), trustManagers, null);
			httpsConnection.SSLSocketFactory = context.SocketFactory;
		}

		[MemberNotNullWhen (true, nameof(TrustedCerts))]
		bool HasTrustedCerts => TrustedCerts?.Count > 0;

		[MemberNotNullWhen (true, nameof(_serverCertificateCustomValidator))]
		bool HasServerCertificateCustomValidationCallback => _serverCertificateCustomValidator is not null;

		[MemberNotNullWhen (true, nameof(_clientCertificates))]
		bool HasClientCertificates => _clientCertificates?.Count > 0;

		KeyManagerFactory? GetConfiguredKeyManagerFactory (KeyStore keyStore)
		{
			var kmf = ConfigureKeyManagerFactory (keyStore);

			if (kmf is null && HasClientCertificates) {
				kmf = KeyManagerFactory.GetInstance ("PKIX") ?? throw new InvalidOperationException ("Failed to get the KeyManagerFactory instance for PKIX");
				kmf.Init (keyStore, null);
			}

			return kmf;
		}

		KeyStore GetConfiguredKeyStoreInstance ()
		{
			var keyStore = KeyStore.GetInstance (KeyStore.DefaultType) ?? throw new InvalidOperationException ("Failed to get the default KeyStore instance");
			keyStore.Load (null, null);

			if (HasTrustedCerts) {
				for (int i = 0; i < TrustedCerts!.Count; i++) {
					if (TrustedCerts [i] is Certificate cert) {
						keyStore.SetCertificateEntry ($"ca{i}", cert);
					}
				}
			}

			if (HasClientCertificates) {
				if (ClientCertificateOptions != ClientCertificateOption.Manual) {
					throw new InvalidOperationException ($"Use of {nameof(ClientCertificates)} requires that {nameof(ClientCertificateOptions)} be set to ClientCertificateOption.Manual");
				}

				for (int i = 0; i < _clientCertificates.Count; i++) {
					var keyEntry = GetKeyEntry (new X509Certificate2 (_clientCertificates [i]));
					if (keyEntry is var (key, chain)) {
						keyStore.SetKeyEntry ($"key{i}", key, null, chain);
					}
				}
			}

			return ConfigureKeyStore (keyStore) ?? throw new InvalidOperationException ($"{nameof(ConfigureKeyStore)} unexpectedly returned null");
		}

		ITrustManager[]? GetTrustManagers (TrustManagerFactory? tmf, KeyStore keyStore, HttpRequestMessage requestMessage)
		{
			if (tmf is null) {
				tmf = TrustManagerFactory.GetInstance (TrustManagerFactory.DefaultAlgorithm) ?? throw new InvalidOperationException ("Failed to get the default TrustManagerFactory instance");
				tmf.Init (HasTrustedCerts ? keyStore : null); // only use the custom key store if the user defined any trusted certs
			}

			ITrustManager[]? trustManagers = tmf.GetTrustManagers ();

			if (HasServerCertificateCustomValidationCallback) {
				trustManagers = _serverCertificateCustomValidator.ReplaceX509TrustManager (trustManagers, requestMessage);
			}

			return trustManagers;
		}

		static (IPrivateKey, Certificate[])? GetKeyEntry (X509Certificate2 clientCertificate)
		{
			if (!clientCertificate.HasPrivateKey) {
				return null;
			}

			AsymmetricAlgorithm? key = null;
			string? algorithmName = null;

			if (clientCertificate.GetRSAPrivateKey () is {} rsa) {
				(key, algorithmName) = (rsa, "RSA");
			} else if (clientCertificate.GetECDsaPrivateKey () is {} ec) {
				(key, algorithmName) = (ec, "EC");
			} else if (clientCertificate.GetDSAPrivateKey () is {} dsa) {
				(key, algorithmName) = (dsa, "DSA");
			} else {
				return null;
			}

			var keyFactory = KeyFactory.GetInstance (algorithmName) ?? throw new InvalidOperationException ($"Failed to get the KeyFactory instance for algorithm {algorithmName}");
			var privateKey = keyFactory.GeneratePrivate (new Java.Security.Spec.PKCS8EncodedKeySpec (key.ExportPkcs8PrivateKey ()));
			var certificate = Java.Lang.Object.GetObject<Certificate> (clientCertificate.Handle, JniHandleOwnership.DoNotTransfer);

			if (privateKey is null || certificate is null) {
				return null;
			}

			return (privateKey, new Certificate [] { certificate });
		}

		void HandlePreAuthentication (HttpURLConnection httpConnection)
		{
			var data = PreAuthenticationData;
			if (!PreAuthenticate || data == null)
				return;

			var creds = data.UseProxyAuthentication ? Proxy?.Credentials : Credentials;
			if (creds == null) {
				if (Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, $"Authentication using scheme {data.Scheme} requested but no credentials found. No authentication will be performed");
				return;
			}

			var auth = data.Scheme == AuthenticationScheme.Unsupported ? data.AuthModule : authModules.Find (m => m?.Scheme == data.Scheme);
			if (auth == null) {
				if (Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, $"Authentication module for scheme '{data.Scheme}' not found. No authentication will be performed");
				return;
			}

			Authorization authorization = auth.Authenticate (data.Challenge!, httpConnection, creds);
			if (authorization == null) {
				if (Logger.LogNet)
					Logger.Log (LogLevel.Info, LOG_APP, $"Authorization module {auth.GetType ()} for scheme {data.Scheme} returned no authorization");
				return;
			}

			if (Logger.LogNet) {
				var header  = data.UseProxyAuthentication ? "Proxy-Authorization" : "Authorization";
				Logger.Log (LogLevel.Info, LOG_APP, $"Authentication header '{header}' will be set to '{authorization.Message}'");
			}
			httpConnection.SetRequestProperty (data.UseProxyAuthentication ? "Proxy-Authorization" : "Authorization", authorization.Message);
		}

		static string GetHeaderSeparator (string name) => headerSeparators.TryGetValue (name, out var value) ? value : ",";

		void AddHeaders (HttpURLConnection conn, HttpHeaders headers)
		{
			if (headers == null)
				return;

			foreach (KeyValuePair<string, IEnumerable<string>> header in headers) {
				conn.SetRequestProperty (header.Key, header.Value != null ? String.Join (GetHeaderSeparator (header.Key), header.Value) : String.Empty);
			}
		}

		void SetupRequestBody (HttpURLConnection httpConnection, HttpRequestMessage request)
		{
			if (request.Content == null) {
				// Pilfered from System.Net.Http.HttpClientHandler:SendAync
				if (HttpMethod.Post.Equals (request.Method) || HttpMethod.Put.Equals (request.Method) || HttpMethod.Delete.Equals (request.Method)) {
					// Explicitly set this to make sure we're sending a "Content-Length: 0" header.
					// This fixes the issue that's been reported on the forums:
					// http://forums.xamarin.com/discussion/17770/length-required-error-in-http-post-since-latest-release
					httpConnection.SetRequestProperty ("Content-Length", "0");
				}
				return;
			}

			httpConnection.DoOutput = true;
			long? contentLength = request.Content.Headers.ContentLength;
			if (contentLength != null)
				httpConnection.SetFixedLengthStreamingMode ((int)contentLength);
			else
				httpConnection.SetChunkedStreamingMode (0);
		}
	}

}
