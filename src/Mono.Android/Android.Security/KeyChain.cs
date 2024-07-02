#nullable enable
using System;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Android.Security
{
	public partial class KeyChain
	{
		public static X509Certificate2? GetX509Certificate2WithPrivateKey (Android.Content.Context context, string alias)
		{
			var privateKey = KeyChain.GetPrivateKey (context, alias);
			if (privateKey is null) {
				return null;
			}

			var chain = KeyChain.GetCertificateChain (context, alias);
			if (chain is null) {
				return null;
			}

			var privateKeyEntry = new Java.Security.KeyStore.PrivateKeyEntry (privateKey, chain);
			var certificate = new X509Certificate2 (privateKeyEntry.Handle);
			GC.KeepAlive (privateKeyEntry);
			return certificate;
		}

		[SupportedOSPlatform("android23.0")]
		public static async Task<string?> ChoosePrivateKeyAliasAsync (
			Android.App.Activity activity,
			string[]? keyTypes,
			Java.Security.IPrincipal[]? issuers,
			Android.Net.Uri? uri,
			string? alias)
		{
			var tcs = new TaskCompletionSource<string?> ();
			KeyChain.ChoosePrivateKeyAlias (activity, new KeyChainAliasCallback(tcs), keyTypes, issuers, uri, alias);
			return await tcs.Task;
		}

		public static async Task<string?> ChoosePrivateKeyAliasAsync (
			Android.App.Activity activity,
			string[]? keyTypes,
			Java.Security.IPrincipal[]? issuers,
			string? host,
			int port,
			string? alias)
		{
			var tcs = new TaskCompletionSource<string?> ();
			KeyChain.ChoosePrivateKeyAlias (activity, new KeyChainAliasCallback(tcs), keyTypes, issuers, host, port, alias);
			return await tcs.Task;
		}

		[SupportedOSPlatform("android23.0")]
		public static async Task<X509Certificate2?> ChooseX509Certificate2WithPrivateKeyAsync (
			Android.App.Activity activity,
			string[]? keyTypes,
			Java.Security.IPrincipal[]? issuers,
			Android.Net.Uri? uri,
			string? alias)
		{
			alias = await ChoosePrivateKeyAliasAsync (activity, keyTypes, issuers, uri, alias);
			if (alias is null) {
				return null;
			}

			return GetX509Certificate2WithPrivateKey (activity, alias);
		}

		public static async Task<X509Certificate2?> ChooseX509Certificate2WithPrivateKeyAsync (
			Android.App.Activity activity,
			string[]? keyTypes,
			Java.Security.IPrincipal[]? issuers,
			string? host,
			int port,
			string? alias)
		{
			alias = await ChoosePrivateKeyAliasAsync (activity, keyTypes, issuers, host, port, alias);
			if (alias is null) {
				return null;
			}

			return GetX509Certificate2WithPrivateKey (activity, alias);
		}

		private sealed class KeyChainAliasCallback(TaskCompletionSource<string?> tcs)
			: Java.Lang.Object, IKeyChainAliasCallback
		{
			public void Alias (string? alias) => tcs.SetResult (alias);
		}
	}
}
