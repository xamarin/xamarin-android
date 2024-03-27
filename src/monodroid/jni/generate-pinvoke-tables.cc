//
// To build and run this utility run (on Linux or macOS):
//
//   ../../../build-tools/scripts/generate-pinvoke-tables.sh
//
// A reasonable C++20 compiler is required (g++ 10+, clang 11+, on mac it may require XCode 12.5 or newer)
//
// Whenever a new p/invoke (or entire new shared libary which is part of dotnet distribution) is added, try to keep the
// entries sorted alphabetically.  This is not required by the generator but easier to examine by humans.
//
// If a new library is added, please remember to generate a hash of its name and update pinvoke-override-api.cc
//
// To get the list of exported native symbols for a library, you can run the following command on Unix:
//
//   for s in $(llvm-nm -DUj [LIBRARY] | sort); do echo "\"$s\","; done
//
#include <algorithm>
#include <cerrno>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iomanip>
#include <memory>
#include <string>
#include <unordered_set>
#include <vector>

#include "xxhash.hh"

namespace fs = std::filesystem;
using namespace xamarin::android;

const std::vector<std::string> internal_pinvoke_names = {
	"create_public_directory",
	"java_interop_free",
	"java_interop_strdup",
	"monodroid_clear_gdb_wait",
	"_monodroid_counters_dump",
	"_monodroid_detect_cpu_and_architecture",
	"monodroid_dylib_mono_free",
	"monodroid_dylib_mono_init",
	"monodroid_dylib_mono_new",
	"monodroid_embedded_assemblies_set_assemblies_prefix",
	"monodroid_fopen",
	"monodroid_free",
	"_monodroid_freeifaddrs",
	"_monodroid_gc_wait_for_bridge_processing",
	"_monodroid_get_android_api_level",
	"_monodroid_get_dns_servers",
	"monodroid_get_dylib",
	"_monodroid_get_identity_hash_code",
	"_monodroid_getifaddrs",
	"monodroid_get_log_categories",
	"monodroid_get_namespaced_system_property",
	"_monodroid_get_network_interface_supports_multicast",
	"_monodroid_get_network_interface_up_state",
	"monodroid_get_system_property",
	"_monodroid_gref_get",
	"_monodroid_gref_log",
	"_monodroid_gref_log_delete",
	"_monodroid_gref_log_new",
	"monodroid_log",
	"_monodroid_lookup_replacement_type",
	"_monodroid_lookup_replacement_method_info",
	"_monodroid_lref_log_delete",
	"_monodroid_lref_log_new",
	"_monodroid_max_gref_get",
	"monodroid_store_package_name",
	"monodroid_strdup_printf",
	"monodroid_strfreev",
	"monodroid_strsplit",
	"_monodroid_timezone_get_default_id",
	"monodroid_timing_start",
	"monodroid_timing_stop",
	"monodroid_TypeManager_get_java_class_name",
	"_monodroid_weak_gref_delete",
	"_monodroid_weak_gref_get",
	"_monodroid_weak_gref_new",
	"path_combine",
	"recv_uninterrupted",
	"send_uninterrupted",
	"set_world_accessable",
};

const std::vector<std::string> dotnet_pinvoke_names = {
	// libSystem.IO.Compression.Native.so
	"BrotliDecoderCreateInstance",
	"BrotliDecoderDecompress",
	"BrotliDecoderDecompressStream",
	"BrotliDecoderDestroyInstance",
	"BrotliDecoderErrorString",
	"BrotliDecoderGetErrorCode",
	"BrotliDecoderHasMoreOutput",
	"BrotliDecoderIsFinished",
	"BrotliDecoderIsUsed",
	"BrotliDecoderSetParameter",
	"BrotliDecoderTakeOutput",
	"BrotliDecoderVersion",
	"BrotliDefaultAllocFunc",
	"BrotliDefaultFreeFunc",
	"BrotliEncoderCompress",
	"BrotliEncoderCompressStream",
	"BrotliEncoderCreateInstance",
	"BrotliEncoderDestroyInstance",
	"BrotliEncoderHasMoreOutput",
	"BrotliEncoderIsFinished",
	"BrotliEncoderMaxCompressedSize",
	"BrotliEncoderSetParameter",
	"BrotliEncoderTakeOutput",
	"BrotliEncoderVersion",
	"BrotliGetDictionary",
	"BrotliGetTransforms",
	"BrotliSetDictionaryData",
	"BrotliTransformDictionaryWord",
	"CompressionNative_Crc32",
	"CompressionNative_Deflate",
	"CompressionNative_DeflateEnd",
	"CompressionNative_DeflateInit2_",
	"CompressionNative_DeflateReset",
	"CompressionNative_Inflate",
	"CompressionNative_InflateEnd",
	"CompressionNative_InflateInit2_",
	"CompressionNative_InflateReset",

	// libSystem.Native.so
	"SystemNative_Abort",
	"SystemNative_Accept",
	"SystemNative_Access",
	"SystemNative_AlignedAlloc",
	"SystemNative_AlignedFree",
	"SystemNative_AlignedRealloc",
	"SystemNative_Bind",
	"SystemNative_Calloc",
	"SystemNative_CanGetHiddenFlag",
	"SystemNative_ChDir",
	"SystemNative_ChMod",
	"SystemNative_Close",
	"SystemNative_CloseDir",
	"SystemNative_CloseSocketEventPort",
	"SystemNative_ConfigureTerminalForChildProcess",
	"SystemNative_Connect",
	"SystemNative_ConvertErrorPalToPlatform",
	"SystemNative_ConvertErrorPlatformToPal",
	"SystemNative_CopyFile",
	"SystemNative_CreateAutoreleasePool",
	"SystemNative_CreateNetworkChangeListenerSocket",
	"SystemNative_CreateSocketEventBuffer",
	"SystemNative_CreateSocketEventPort",
	"SystemNative_CreateThread",
	"SystemNative_DisablePosixSignalHandling",
	"SystemNative_Disconnect",
	"SystemNative_DrainAutoreleasePool",
	"SystemNative_Dup",
	"SystemNative_EnablePosixSignalHandling",
	"SystemNative_EnumerateGatewayAddressesForInterface",
	"SystemNative_EnumerateInterfaceAddresses",
	"SystemNative_Exit",
	"SystemNative_FAllocate",
	"SystemNative_FChflags",
	"SystemNative_FChMod",
	"SystemNative_FcntlCanGetSetPipeSz",
	"SystemNative_FcntlGetFD",
	"SystemNative_FcntlGetIsNonBlocking",
	"SystemNative_FcntlGetPipeSz",
	"SystemNative_FcntlSetFD",
	"SystemNative_FcntlSetIsNonBlocking",
	"SystemNative_FcntlSetPipeSz",
	"SystemNative_FLock",
	"SystemNative_ForkAndExecProcess",
	"SystemNative_Free",
	"SystemNative_FreeEnviron",
	"SystemNative_FreeHostEntry",
	"SystemNative_FreeLibrary",
	"SystemNative_FreeSocketEventBuffer",
	"SystemNative_FStat",
	"SystemNative_FSync",
	"SystemNative_FTruncate",
	"SystemNative_FUTimens",
	"SystemNative_GetActiveTcpConnectionInfos",
	"SystemNative_GetActiveUdpListeners",
	"SystemNative_GetAddressFamily",
	"SystemNative_GetAllMountPoints",
	"SystemNative_GetAtOutOfBandMark",
	"SystemNative_GetBootTimeTicks",
	"SystemNative_GetBytesAvailable",
	"SystemNative_GetControlCharacters",
	"SystemNative_GetControlMessageBufferSize",
	"SystemNative_GetCpuUtilization",
	"SystemNative_GetCryptographicallySecureRandomBytes",
	"SystemNative_GetCwd",
	"SystemNative_GetDefaultSearchOrderPseudoHandle",
	"SystemNative_GetDefaultTimeZone",
	"SystemNative_GetDeviceIdentifiers",
	"SystemNative_GetDomainName",
	"SystemNative_GetDomainSocketSizes",
	"SystemNative_GetEGid",
	"SystemNative_GetEnv",
	"SystemNative_GetEnviron",
	"SystemNative_GetErrNo",
	"SystemNative_GetEstimatedTcpConnectionCount",
	"SystemNative_GetEstimatedUdpListenerCount",
	"SystemNative_GetEUid",
	"SystemNative_GetFileSystemType",
	"SystemNative_GetFormatInfoForMountPoint",
	"SystemNative_GetGroupList",
	"SystemNative_GetGroupName",
	"SystemNative_GetGroups",
	"SystemNative_GetHostEntryForName",
	"SystemNative_GetHostName",
	"SystemNative_GetIcmpv4GlobalStatistics",
	"SystemNative_GetIcmpv6GlobalStatistics",
	"SystemNative_GetIPSocketAddressSizes",
	"SystemNative_GetIPv4Address",
	"SystemNative_GetIPv4GlobalStatistics",
	"SystemNative_GetIPv4MulticastOption",
	"SystemNative_GetIPv6Address",
	"SystemNative_GetIPv6MulticastOption",
	"SystemNative_GetLingerOption",
	"SystemNative_GetLoadLibraryError",
	"SystemNative_GetMaximumAddressSize",
	"SystemNative_GetNameInfo",
	"SystemNative_GetNativeIPInterfaceStatistics",
	"SystemNative_GetNetworkInterfaces",
	"SystemNative_GetNonCryptographicallySecureRandomBytes",
	"SystemNative_GetNumRoutes",
	"SystemNative_GetOSArchitecture",
	"SystemNative_GetPeerID",
	"SystemNative_GetPeerName",
	"SystemNative_GetPeerUserName",
	"SystemNative_GetPid",
	"SystemNative_GetPlatformSignalNumber",
	"SystemNative_GetPort",
	"SystemNative_GetPriority",
	"SystemNative_GetProcAddress",
	"SystemNative_GetProcessPath",
	"SystemNative_GetPwNamR",
	"SystemNative_GetPwUidR",
	"SystemNative_GetRawSockOpt",
	"SystemNative_GetReadDirRBufferSize",
	"SystemNative_GetRLimit",
	"SystemNative_GetSid",
	"SystemNative_GetSignalForBreak",
	"SystemNative_GetSocketErrorOption",
	"SystemNative_GetSocketType",
	"SystemNative_GetSockName",
	"SystemNative_GetSockOpt",
	"SystemNative_GetSpaceInfoForMountPoint",
	"SystemNative_GetSystemTimeAsTicks",
	"SystemNative_GetTcpGlobalStatistics",
	"SystemNative_GetTimestamp",
	"SystemNative_GetUdpGlobalStatistics",
	"SystemNative_GetUnixRelease",
	"SystemNative_GetUnixVersion",
	"SystemNative_GetWindowSize",
	"SystemNative_HandleNonCanceledPosixSignal",
	"SystemNative_InitializeConsoleBeforeRead",
	"SystemNative_InitializeTerminalAndSignalHandling",
	"SystemNative_INotifyAddWatch",
	"SystemNative_INotifyInit",
	"SystemNative_INotifyRemoveWatch",
	"SystemNative_InterfaceNameToIndex",
	"SystemNative_iOSSupportVersion",
	"SystemNative_IsATty",
	"SystemNative_Kill",
	"SystemNative_LChflags",
	"SystemNative_LChflagsCanSetHiddenFlag",
	"SystemNative_Link",
	"SystemNative_Listen",
	"SystemNative_LoadLibrary",
	"SystemNative_LockFileRegion",
	"SystemNative_Log",
	"SystemNative_LogError",
	"SystemNative_LowLevelMonitor_Acquire",
	"SystemNative_LowLevelMonitor_Create",
	"SystemNative_LowLevelMonitor_Destroy",
	"SystemNative_LowLevelMonitor_Release",
	"SystemNative_LowLevelMonitor_Signal_Release",
	"SystemNative_LowLevelMonitor_TimedWait",
	"SystemNative_LowLevelMonitor_Wait",
	"SystemNative_LSeek",
	"SystemNative_LStat",
	"SystemNative_MAdvise",
	"SystemNative_Malloc",
	"SystemNative_MapTcpState",
	"SystemNative_MkDir",
	"SystemNative_MkdTemp",
	"SystemNative_MkFifo",
	"SystemNative_MkNod",
	"SystemNative_MksTemps",
	"SystemNative_MMap",
	"SystemNative_MSync",
	"SystemNative_MUnmap",
	"SystemNative_Open",
	"SystemNative_OpenDir",
	"SystemNative_PathConf",
	"SystemNative_Pipe",
	"SystemNative_PlatformSupportsDualModeIPv4PacketInfo",
	"SystemNative_Poll",
	"SystemNative_PosixFAdvise",
	"SystemNative_PRead",
	"SystemNative_PReadV",
	"SystemNative_PWrite",
	"SystemNative_PWriteV",
	"SystemNative_Read",
	"SystemNative_ReadDirR",
	"SystemNative_ReadEvents",
	"SystemNative_ReadLink",
	"SystemNative_ReadProcessStatusInfo",
	"SystemNative_ReadStdin",
	"SystemNative_Realloc",
	"SystemNative_RealPath",
	"SystemNative_Receive",
	"SystemNative_ReceiveMessage",
	"SystemNative_RegisterForSigChld",
	"SystemNative_Rename",
	"SystemNative_RmDir",
	"SystemNative_SchedGetAffinity",
	"SystemNative_SchedGetCpu",
	"SystemNative_SchedSetAffinity",
	"SystemNative_SearchPath",
	"SystemNative_SearchPath_TempDirectory",
	"SystemNative_Send",
	"SystemNative_SendFile",
	"SystemNative_SendMessage",
	"SystemNative_SetAddressFamily",
	"SystemNative_SetDelayedSigChildConsoleConfigurationHandler",
	"SystemNative_SetErrNo",
	"SystemNative_SetEUid",
	"SystemNative_SetIPv4Address",
	"SystemNative_SetIPv4MulticastOption",
	"SystemNative_SetIPv6Address",
	"SystemNative_SetIPv6MulticastOption",
	"SystemNative_SetKeypadXmit",
	"SystemNative_SetLingerOption",
	"SystemNative_SetPort",
	"SystemNative_SetPosixSignalHandler",
	"SystemNative_SetPriority",
	"SystemNative_SetRawSockOpt",
	"SystemNative_SetReceiveTimeout",
	"SystemNative_SetRLimit",
	"SystemNative_SetSendTimeout",
	"SystemNative_SetSignalForBreak",
	"SystemNative_SetSockOpt",
	"SystemNative_SetTerminalInvalidationHandler",
	"SystemNative_SetWindowSize",
	"SystemNative_ShmOpen",
	"SystemNative_ShmUnlink",
	"SystemNative_Shutdown",
	"SystemNative_SNPrintF",
	"SystemNative_Socket",
	"SystemNative_Stat",
	"SystemNative_StdinReady",
	"SystemNative_StrErrorR",
	"SystemNative_SymLink",
	"SystemNative_Sync",
	"SystemNative_SysConf",
	"SystemNative_Sysctl",
	"SystemNative_SysLog",
	"SystemNative_TryChangeSocketEventRegistration",
	"SystemNative_TryGetIPPacketInformation",
	"SystemNative_UninitializeConsoleAfterRead",
	"SystemNative_Unlink",
	"SystemNative_UTimensat",
	"SystemNative_WaitForSocketEvents",
	"SystemNative_WaitIdAnyExitedNoHangNoWait",
	"SystemNative_WaitPidExitedNoHang",
	"SystemNative_Write",

	// libSystem.Security.Cryptography.Native.Android.so
	"AndroidCryptoNative_AeadCipherFinalEx",
	"AndroidCryptoNative_Aes128Cbc",
	"AndroidCryptoNative_Aes128Ccm",
	"AndroidCryptoNative_Aes128Cfb128",
	"AndroidCryptoNative_Aes128Cfb8",
	"AndroidCryptoNative_Aes128Ecb",
	"AndroidCryptoNative_Aes128Gcm",
	"AndroidCryptoNative_Aes192Cbc",
	"AndroidCryptoNative_Aes192Ccm",
	"AndroidCryptoNative_Aes192Cfb128",
	"AndroidCryptoNative_Aes192Cfb8",
	"AndroidCryptoNative_Aes192Ecb",
	"AndroidCryptoNative_Aes192Gcm",
	"AndroidCryptoNative_Aes256Cbc",
	"AndroidCryptoNative_Aes256Ccm",
	"AndroidCryptoNative_Aes256Cfb128",
	"AndroidCryptoNative_Aes256Cfb8",
	"AndroidCryptoNative_Aes256Ecb",
	"AndroidCryptoNative_Aes256Gcm",
	"AndroidCryptoNative_BigNumToBinary",
	"AndroidCryptoNative_ChaCha20Poly1305",
	"AndroidCryptoNative_CipherCreate",
	"AndroidCryptoNative_CipherCreatePartial",
	"AndroidCryptoNative_CipherCtxSetPadding",
	"AndroidCryptoNative_CipherDestroy",
	"AndroidCryptoNative_CipherFinalEx",
	"AndroidCryptoNative_CipherIsSupported",
	"AndroidCryptoNative_CipherReset",
	"AndroidCryptoNative_CipherSetKeyAndIV",
	"AndroidCryptoNative_CipherSetNonceLength",
	"AndroidCryptoNative_CipherSetTagLength",
	"AndroidCryptoNative_CipherUpdate",
	"AndroidCryptoNative_CipherUpdateAAD",
	"AndroidCryptoNative_DecodeRsaSubjectPublicKeyInfo",
	"AndroidCryptoNative_DeleteGlobalReference",
	"AndroidCryptoNative_Des3Cbc",
	"AndroidCryptoNative_Des3Cfb64",
	"AndroidCryptoNative_Des3Cfb8",
	"AndroidCryptoNative_Des3Ecb",
	"AndroidCryptoNative_DesCbc",
	"AndroidCryptoNative_DesCfb8",
	"AndroidCryptoNative_DesEcb",
	"AndroidCryptoNative_DsaGenerateKey",
	"AndroidCryptoNative_DsaKeyCreateByExplicitParameters",
	"AndroidCryptoNative_DsaSign",
	"AndroidCryptoNative_DsaSignatureFieldSize",
	"AndroidCryptoNative_DsaSizeP",
	"AndroidCryptoNative_DsaSizeSignature",
	"AndroidCryptoNative_DsaVerify",
	"AndroidCryptoNative_EcdhDeriveKey",
	"AndroidCryptoNative_EcDsaSign",
	"AndroidCryptoNative_EcDsaSize",
	"AndroidCryptoNative_EcDsaVerify",
	"AndroidCryptoNative_EcKeyCreateByExplicitParameters",
	"AndroidCryptoNative_EcKeyCreateByKeyParameters",
	"AndroidCryptoNative_EcKeyCreateByOid",
	"AndroidCryptoNative_EcKeyDestroy",
	"AndroidCryptoNative_EcKeyGetCurveName",
	"AndroidCryptoNative_EcKeyGetSize",
	"AndroidCryptoNative_EcKeyUpRef",
	"AndroidCryptoNative_GetBigNumBytes",
	"AndroidCryptoNative_GetDsaParameters",
	"AndroidCryptoNative_GetECCurveParameters",
	"AndroidCryptoNative_GetECKeyParameters",
	"AndroidCryptoNative_GetRsaParameters",
	"AndroidCryptoNative_NewGlobalReference",
	"AndroidCryptoNative_RegisterRemoteCertificateValidationCallback",
	"AndroidCryptoNative_RsaCreate",
	"AndroidCryptoNative_RsaDestroy",
	"AndroidCryptoNative_RsaGenerateKeyEx",
	"AndroidCryptoNative_RsaPrivateDecrypt",
	"AndroidCryptoNative_RsaPublicEncrypt",
	"AndroidCryptoNative_RsaSignPrimitive",
	"AndroidCryptoNative_RsaSize",
	"AndroidCryptoNative_RsaUpRef",
	"AndroidCryptoNative_RsaVerificationPrimitive",
	"AndroidCryptoNative_SetRsaParameters",
	"AndroidCryptoNative_SSLGetSupportedProtocols",
	"AndroidCryptoNative_SSLStreamCreate",
	"AndroidCryptoNative_SSLStreamCreateWithCertificates",
	"AndroidCryptoNative_SSLStreamGetApplicationProtocol",
	"AndroidCryptoNative_SSLStreamGetCipherSuite",
	"AndroidCryptoNative_SSLStreamGetPeerCertificate",
	"AndroidCryptoNative_SSLStreamGetPeerCertificates",
	"AndroidCryptoNative_SSLStreamGetProtocol",
	"AndroidCryptoNative_SSLStreamHandshake",
	"AndroidCryptoNative_SSLStreamInitialize",
	"AndroidCryptoNative_SSLStreamIsLocalCertificateUsed",
	"AndroidCryptoNative_SSLStreamRead",
	"AndroidCryptoNative_SSLStreamRelease",
	"AndroidCryptoNative_SSLStreamRequestClientAuthentication",
	"AndroidCryptoNative_SSLStreamSetApplicationProtocols",
	"AndroidCryptoNative_SSLStreamSetEnabledProtocols",
	"AndroidCryptoNative_SSLStreamSetTargetHost",
	"AndroidCryptoNative_SSLStreamShutdown",
	"AndroidCryptoNative_SSLStreamVerifyHostname",
	"AndroidCryptoNative_SSLStreamWrite",
	"AndroidCryptoNative_SSLSupportsApplicationProtocolsConfiguration",
	"AndroidCryptoNative_X509ChainBuild",
	"AndroidCryptoNative_X509ChainCreateContext",
	"AndroidCryptoNative_X509ChainDestroyContext",
	"AndroidCryptoNative_X509ChainGetCertificateCount",
	"AndroidCryptoNative_X509ChainGetCertificates",
	"AndroidCryptoNative_X509ChainGetErrorCount",
	"AndroidCryptoNative_X509ChainGetErrors",
	"AndroidCryptoNative_X509ChainSetCustomTrustStore",
	"AndroidCryptoNative_X509ChainValidate",
	"AndroidCryptoNative_X509Decode",
	"AndroidCryptoNative_X509DecodeCollection",
	"AndroidCryptoNative_X509Encode",
	"AndroidCryptoNative_X509ExportPkcs7",
	"AndroidCryptoNative_X509GetContentType",
	"AndroidCryptoNative_X509PublicKey",
	"AndroidCryptoNative_X509StoreAddCertificate",
	"AndroidCryptoNative_X509StoreAddCertificateWithPrivateKey",
	"AndroidCryptoNative_X509StoreContainsCertificate",
	"AndroidCryptoNative_X509StoreEnumerateCertificates",
	"AndroidCryptoNative_X509StoreEnumerateTrustedCertificates",
	"AndroidCryptoNative_X509StoreOpenDefault",
	"AndroidCryptoNative_X509StoreRemoveCertificate",
	"CryptoNative_EnsureOpenSslInitialized",
	"CryptoNative_ErrClearError",
	"CryptoNative_ErrErrorStringN",
	"CryptoNative_ErrGetErrorAlloc",
	"CryptoNative_ErrPeekError",
	"CryptoNative_ErrPeekLastError",
	"CryptoNative_ErrReasonErrorString",
	"CryptoNative_EvpDigestCurrent",
	"CryptoNative_EvpDigestFinalEx",
	"CryptoNative_EvpDigestOneShot",
	"CryptoNative_EvpDigestReset",
	"CryptoNative_EvpDigestUpdate",
	"CryptoNative_EvpMd5",
	"CryptoNative_EvpMdCtxCreate",
	"CryptoNative_EvpMdCtxDestroy",
	"CryptoNative_EvpMdSize",
	"CryptoNative_EvpSha1",
	"CryptoNative_EvpSha256",
	"CryptoNative_EvpSha384",
	"CryptoNative_EvpSha512",
	"CryptoNative_GetMaxMdSize",
	"CryptoNative_GetRandomBytes",
	"CryptoNative_HmacCreate",
	"CryptoNative_HmacCurrent",
	"CryptoNative_HmacDestroy",
	"CryptoNative_HmacFinal",
	"CryptoNative_HmacOneShot",
	"CryptoNative_HmacReset",
	"CryptoNative_HmacUpdate",
};

template<typename Hash>
struct PinvokeEntry
{
	std::string name;
	Hash hash;
	bool write_func_pointer;

	template<class Os> friend
	Os& operator<< (Os& os, PinvokeEntry<Hash> const& p)
	{
		os << std::showbase << std::hex << p.hash << ", \"" << p.name << "\", ";

		if (p.write_func_pointer) {
			return os << "reinterpret_cast<void*>(&" << p.name << ")";
		}

		return os << "nullptr";
	}
};

void print (std::ostream& os, std::string comment, std::string variable_name, auto const& seq)
{
	os << "//" << comment << '\n';
	os << "static PinvokeEntry " << variable_name << "[] = {" << std::endl;

	for (auto const& elem : seq) {
		os << "\t{" << elem << "}," << std::endl;
	}

	os << "};" << std::endl;
}

template<typename Hash>
bool add_hash (std::string const& pinvoke, Hash hash, std::vector<PinvokeEntry<Hash>>& vec, std::unordered_set<Hash>& used_cache, bool write_func_pointer)
{
	vec.emplace_back (pinvoke, hash, write_func_pointer);
	if (used_cache.contains (hash)) {
		std::cerr << (sizeof(Hash) == 4 ? "32" : "64") << "-bit hash collision for key '" << pinvoke << "': " << std::hex << std::showbase << hash << std::endl;
		return true;
	}

	used_cache.insert (hash);
	return false;
}

bool generate_hashes (std::string table_name, std::vector<std::string> const& names, std::vector<PinvokeEntry<uint32_t>>& pinvokes32, std::vector<PinvokeEntry<uint64_t>>& pinvokes64, bool write_func_pointer)
{
	std::unordered_set<uint32_t> used_pinvokes32{};
	std::unordered_set<uint64_t> used_pinvokes64{};
	uint32_t hash32;
	uint64_t hash64;
	bool have_collisions = false;

	std::cout << "There are " << names.size () << " " << table_name << " p/invoke functions" << std::endl;
	for (std::string const& pinvoke : names) {
		have_collisions |= add_hash (pinvoke, xxhash32::hash (pinvoke.c_str (), pinvoke.length ()), pinvokes32, used_pinvokes32, write_func_pointer);
		have_collisions |= add_hash (pinvoke, xxhash64::hash (pinvoke.c_str (), pinvoke.length ()), pinvokes64, used_pinvokes64, write_func_pointer);
	}

	std::cout << "p/invoke hash collisions for '" << table_name << "' were " << (have_collisions ? "" : "not ") << "found" << std::endl;

	std::ranges::sort (pinvokes32, {}, &PinvokeEntry<uint32_t>::hash);
	std::ranges::sort (pinvokes64, {}, &PinvokeEntry<uint64_t>::hash);

	return have_collisions;
}

template<typename Hash>
void write_library_name_hash (Hash (*hasher)(const char*, size_t), std::ostream& os, std::string library_name, std::string variable_prefix)
{
	Hash hash = hasher (library_name.c_str (), library_name.length ());
	os << "constexpr hash_t " << variable_prefix << "_library_hash = " << std::hex << hash << ";" << std::endl;
}

template<typename Hash>
void write_library_name_hashes (Hash (*hasher)(const char*, size_t), std::ostream& output)
{
	write_library_name_hash (hasher, output, "java-interop", "java_interop");
	write_library_name_hash (hasher, output, "xa-internal-api", "xa_internal_api");
	write_library_name_hash (hasher, output, "libSystem.Native", "system_native");
	write_library_name_hash (hasher, output, "libSystem.IO.Compression.Native", "system_io_compression_native");
	write_library_name_hash (hasher, output, "libSystem.Security.Cryptography.Native.Android", "system_security_cryptography_native_android");
}

int main (int argc, char **argv)
{
	if (argc < 2) {
		std::cerr << "Usage: generate-pinvoke-tables OUTPUT_FILE_PATH" << std::endl << std::endl;
		return 1;
	}

	fs::path output_file_path {argv[1]};

	if (fs::exists (output_file_path)) {
		if (fs::is_directory (output_file_path)) {
			std::cerr << "Output destination '" << output_file_path << "' is a directory" << std::endl;
			return 1;
		}

		fs::remove (output_file_path);
	} else {
		fs::path file_dir = output_file_path.parent_path ();
		if (fs::exists (file_dir)) {
			if (!fs::is_directory (file_dir)) {
				std::cerr << "Output destination parent path points to a file ('" << file_dir << "'" << std::endl;
				return 1;
			}
		} else if (!file_dir.empty ()) {
			if (!fs::create_directories (file_dir)) {
				std::cerr << "Failed to create output directory '" << file_dir << "'" << std::endl;
				std::cerr << strerror (errno) << std::endl;
				return 1;
			}
		}
	}

	bool have_collisions = false;
	std::vector<PinvokeEntry<uint32_t>> internal_pinvokes32{};
	std::vector<PinvokeEntry<uint64_t>> internal_pinvokes64{};
	have_collisions |= generate_hashes ("internal", internal_pinvoke_names, internal_pinvokes32, internal_pinvokes64, true);

	std::vector<PinvokeEntry<uint32_t>> dotnet_pinvokes32{};
	std::vector<PinvokeEntry<uint64_t>> dotnet_pinvokes64{};
	have_collisions |= generate_hashes ("dotnet", dotnet_pinvoke_names, dotnet_pinvokes32, dotnet_pinvokes64, false);

	std::cout << "Generating tables in file: " << output_file_path << std::endl;

	std::ofstream output {output_file_path, std::ios::binary};

	output << "//" << std::endl;
	output << "// Autogenarated file. DO NOT EDIT." << std::endl;
	output << "//" << std::endl;
	output << "// To regenerate run ../../../build-tools/scripts/generate-pinvoke-tables.sh on Linux or macOS" << std::endl;
	output << "// A compiler with support for C++20 ranges is required" << std::endl;
	output << "//" << std::endl << std::endl;

	output << "#include <cstdint>" << std::endl << std::endl;

	output << "#if INTPTR_MAX == INT64_MAX" << std::endl;
	print (output, "64-bit internal p/invoke table", "internal_pinvokes", internal_pinvokes64);
	print (output, "64-bit DotNet p/invoke table", "dotnet_pinvokes", dotnet_pinvokes64);
	output << std::endl;
	write_library_name_hashes<uint64_t> (xxhash64::hash, output);

	output << "#else" << std::endl;

	print (output, "32-bit internal p/invoke table", "internal_pinvokes", internal_pinvokes32);
	print (output, "32-bit DotNet p/invoke table", "dotnet_pinvokes", dotnet_pinvokes32);
	output << std::endl;
	write_library_name_hashes<uint32_t> (xxhash32::hash, output);

	output << "#endif" << std::endl << std::endl;

	output << "constexpr size_t internal_pinvokes_count = " << std::dec << std::noshowbase << internal_pinvoke_names.size () << ";" << std::endl;
	output << "constexpr size_t dotnet_pinvokes_count = " << std::dec << std::noshowbase << dotnet_pinvoke_names.size () << ";" << std::endl;

	return have_collisions ? 1 : 0;
}

// This serves as a quick compile-time test of the algorithm's correctness.
// The tests are copied from https://github.com/ekpyron/xxhashct/test.cpp

template<uint64_t value, uint64_t expected>
struct constexpr_test {
	static_assert (value == expected, "Compile-time hash mismatch.");
};

constexpr_test<xxhash32::hash<0> ("", 0), 0x2CC5D05U> constexprTest_1;
constexpr_test<xxhash32::hash<2654435761U> ("", 0), 0x36B78AE7U> constexprTest_2;
//constexpr_test<xxhash64::hash<0> ("", 0), 0xEF46DB3751D8E999ULL> constexprTest_3;
//constexpr_test<xxhash64::hash<2654435761U> ("", 0), 0xAC75FDA2929B17EFULL> constexprTest_4;
constexpr_test<xxhash32::hash<0> ("test", 4), 0x3E2023CFU> constexprTest32_5;
constexpr_test<xxhash32::hash<2654435761U> ("test", 4), 0xA9C14438U> constexprTest32_6;
//constexpr_test<xxhash64::hash<0> ("test", 4), 0x4fdcca5ddb678139ULL> constexprTest64_7;
//constexpr_test<xxhash64::hash<2654435761U> ("test", 4), 0x5A183B8150E2F651ULL> constexprTest64_8;
