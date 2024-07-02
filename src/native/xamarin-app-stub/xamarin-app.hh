// Dear Emacs, this is a -*- C++ -*- header
#ifndef __XAMARIN_ANDROID_TYPEMAP_H
#define __XAMARIN_ANDROID_TYPEMAP_H

#include <cstdint>
#include <string_view>

#include <jni.h>
#include <mono/metadata/image.h>
#include <mono/utils/mono-publib.h>

#include "xxhash.hh"

static constexpr uint64_t FORMAT_TAG = 0x00035E6972616D58; // 'Xmari^XY' where XY is the format version
static constexpr uint32_t COMPRESSED_DATA_MAGIC = 0x5A4C4158; // 'XALZ', little-endian
static constexpr uint32_t ASSEMBLY_STORE_MAGIC = 0x41424158; // 'XABA', little-endian

// The highest bit of assembly store version is a 64-bit ABI flag
#if INTPTR_MAX == INT64_MAX
static constexpr uint32_t ASSEMBLY_STORE_64BIT_FLAG = 0x80000000;
#else
static constexpr uint32_t ASSEMBLY_STORE_64BIT_FLAG = 0x00000000;
#endif

// The second-to-last byte denotes the actual ABI
#if defined(__aarch64__)
static constexpr uint32_t ASSEMBLY_STORE_ABI = 0x00010000;
#elif defined(__arm__)
static constexpr uint32_t ASSEMBLY_STORE_ABI = 0x00020000;
#elif defined(__x86_64__)
static constexpr uint32_t ASSEMBLY_STORE_ABI = 0x00030000;
#elif defined(__i386__)
static constexpr uint32_t ASSEMBLY_STORE_ABI = 0x00040000;
#endif

// Increase whenever an incompatible change is made to the assembly store format
static constexpr uint32_t ASSEMBLY_STORE_FORMAT_VERSION = 2 | ASSEMBLY_STORE_64BIT_FLAG | ASSEMBLY_STORE_ABI;

static constexpr uint32_t MODULE_MAGIC_NAMES = 0x53544158; // 'XATS', little-endian
static constexpr uint32_t MODULE_INDEX_MAGIC = 0x49544158; // 'XATI', little-endian
static constexpr uint8_t  MODULE_FORMAT_VERSION = 2;       // Keep in sync with the value in src/Xamarin.Android.Build.Tasks/Utilities/TypeMapGenerator.cs

#if defined (DEBUG)
struct BinaryTypeMapHeader
{
	uint32_t magic;
	uint32_t version;
	uint32_t entry_count;
	uint32_t java_name_width;
	uint32_t managed_name_width;
	uint32_t assembly_name_length;
};

struct TypeMapIndexHeader
{
	uint32_t magic;
	uint32_t version;
	uint32_t entry_count;
	uint32_t module_file_name_width;
};

struct TypeMapEntry
{
	const char *from;
	const char *to;
};

// MUST match src/Xamarin.Android.Build.Tasks/Utilities/TypeMappingDebugNativeAssemblyGenerator.cs
struct TypeMap
{
	uint32_t             entry_count;
	char                *assembly_name;
	uint8_t             *data;
	const TypeMapEntry  *java_to_managed;
	const TypeMapEntry  *managed_to_java;
};
#else
struct TypeMapModuleEntry
{
	uint32_t       type_token_id;
	uint32_t       java_map_index;
};

struct TypeMapModule
{
	uint8_t                   module_uuid[16];
	uint32_t                  entry_count;
	uint32_t                  duplicate_count;
	TypeMapModuleEntry const *map;
	TypeMapModuleEntry const *duplicate_map;
	char const               *assembly_name;
	MonoImage                *image;
	uint32_t                  java_name_width;
	uint8_t                  *java_map;
};

struct TypeMapJava
{
	uint32_t module_index;
	uint32_t type_token_id;
	uint32_t java_name_index;
};
#endif

struct CompressedAssemblyHeader
{
	uint32_t magic; // COMPRESSED_DATA_MAGIC
	uint32_t descriptor_index;
	uint32_t uncompressed_length;
};

struct CompressedAssemblyDescriptor
{
	uint32_t   uncompressed_file_size;
	bool       loaded;
	uint8_t   *data;
};

struct CompressedAssemblies
{
	uint32_t                      count;
	CompressedAssemblyDescriptor *descriptors;
};

struct XamarinAndroidBundledAssembly
{
	int32_t  file_fd;
	char    *file_name;
	uint32_t data_offset;
	uint32_t data_size;
	uint8_t *data;
	uint32_t name_length;
	char    *name;
};

//
// Assembly store format
//
// Each target ABI/architecture has a single assembly store file, composed of the following parts:
//
// [HEADER]
// [INDEX]
// [ASSEMBLY_DESCRIPTORS]
// [ASSEMBLY DATA]
//
// Formats of the sections above are as follows:
//
// HEADER (fixed size)
//  [MAGIC]              uint; value: 0x41424158
//  [FORMAT_VERSION]     uint; store format version number
//  [ENTRY_COUNT]        uint; number of entries in the store
//  [INDEX_ENTRY_COUNT]  uint; number of entries in the index
//  [INDEX_SIZE]         uint; index size in bytes
//
// INDEX (variable size, HEADER.ENTRY_COUNT*2 entries, for assembly names with and without the extension)
//  [NAME_HASH]          uint on 32-bit platforms, ulong on 64-bit platforms; xxhash of the assembly name
//  [DESCRIPTOR_INDEX]   uint; index into in-store assembly descriptor array
//
// ASSEMBLY_DESCRIPTORS (variable size, HEADER.ENTRY_COUNT entries), each entry formatted as follows:
//  [MAPPING_INDEX]      uint; index into a runtime array where assembly data pointers are stored
//  [DATA_OFFSET]        uint; offset from the beginning of the store to the start of assembly data
//  [DATA_SIZE]          uint; size of the stored assembly data
//  [DEBUG_DATA_OFFSET]  uint; offset from the beginning of the store to the start of assembly PDB data, 0 if absent
//  [DEBUG_DATA_SIZE]    uint; size of the stored assembly PDB data, 0 if absent
//  [CONFIG_DATA_OFFSET] uint; offset from the beginning of the store to the start of assembly .config contents, 0 if absent
//  [CONFIG_DATA_SIZE]   uint; size of the stored assembly .config contents, 0 if absent
//
// ASSEMBLY_NAMES (variable size, HEADER.ENTRY_COUNT entries), each entry formatted as follows:
//  [NAME_LENGTH]        uint: length of assembly name
//  [NAME]               byte: UTF-8 bytes of assembly name, without the NUL terminator
//

//
// The structures which are found in the store files must be packed to avoid problems when calculating offsets (runtime
// size of a structure can be different than the real data size)
//
struct [[gnu::packed]] AssemblyStoreHeader final
{
	uint32_t magic;
	uint32_t version;
	uint32_t entry_count;
	uint32_t index_entry_count;
	uint32_t index_size; // index size in bytes
};

struct [[gnu::packed]] AssemblyStoreIndexEntry final
{
	xamarin::android::hash_t name_hash;
	uint32_t descriptor_index;
};

struct [[gnu::packed]] AssemblyStoreEntryDescriptor final
{
	uint32_t mapping_index;

	uint32_t data_offset;
	uint32_t data_size;

	uint32_t debug_data_offset;
	uint32_t debug_data_size;

	uint32_t config_data_offset;
	uint32_t config_data_size;
};

struct AssemblyStoreRuntimeData final
{
	uint8_t             *data_start;
	uint32_t             assembly_count;
	uint32_t             index_entry_count;
	AssemblyStoreEntryDescriptor *assemblies;
};

struct AssemblyStoreSingleAssemblyRuntimeData final
{
	uint8_t             *image_data;
	uint8_t             *debug_info_data;
	uint8_t             *config_data;
	AssemblyStoreEntryDescriptor *descriptor;
};

enum class MonoComponent : uint32_t
{
	None      = 0x00,
	Debugger  = 0x01,
	HotReload = 0x02,
	Tracing   = 0x04,
};

struct ApplicationConfig
{
	bool uses_mono_llvm;
	bool uses_mono_aot;
	bool aot_lazy_load;
	bool uses_assembly_preload;
	bool broken_exception_transitions;
	bool instant_run_enabled;
	bool jni_add_native_method_registration_attribute_present;
	bool have_runtime_config_blob;
	bool have_assembly_store;
	bool marshal_methods_enabled;
	bool ignore_split_configs;
	uint8_t bound_exception_type;
	uint32_t package_naming_policy;
	uint32_t environment_variable_count;
	uint32_t system_property_count;
	uint32_t number_of_assemblies_in_apk;
	uint32_t bundled_assembly_name_width;
	uint32_t number_of_dso_cache_entries;
	uint32_t number_of_shared_libraries;
	uint32_t android_runtime_jnienv_class_token;
	uint32_t jnienv_initialize_method_token;
	uint32_t jnienv_registerjninatives_method_token;
	uint32_t jni_remapping_replacement_type_count;
	uint32_t jni_remapping_replacement_method_index_entry_count;
	MonoComponent mono_components_mask;
	const char *android_package_name;
};

struct DSOApkEntry
{
	uint64_t name_hash;
	uint32_t offset; // offset into the APK
	int32_t  fd; // apk file descriptor
};

struct DSOCacheEntry
{
	uint64_t       hash;
	uint64_t       real_name_hash;
	bool           ignore;
	const char    *name;
	void          *handle;
};

struct JniRemappingString
{
	const uint32_t  length;
	const char     *str;
};

struct JniRemappingReplacementMethod
{
	const char    *target_type;
	const char    *target_name;
	// const char    *target_signature;
	// const int32_t  param_count;
	const bool     is_static;
};

struct JniRemappingIndexMethodEntry
{
	const JniRemappingString            name;
	const JniRemappingString            signature;
	const JniRemappingReplacementMethod replacement;
};

struct JniRemappingIndexTypeEntry
{
	const JniRemappingString            name;
	const uint32_t             method_count;
	const JniRemappingIndexMethodEntry *methods;
};

struct JniRemappingTypeReplacementEntry
{
	const JniRemappingString  name;
	const char      *replacement;
};

MONO_API MONO_API_EXPORT const JniRemappingIndexTypeEntry jni_remapping_method_replacement_index[];
MONO_API MONO_API_EXPORT const JniRemappingTypeReplacementEntry jni_remapping_type_replacements[];

MONO_API MONO_API_EXPORT const uint64_t format_tag;

#if defined (DEBUG)
MONO_API MONO_API_EXPORT const TypeMap type_map; // MUST match src/Xamarin.Android.Build.Tasks/Utilities/TypeMappingDebugNativeAssemblyGenerator.cs
#else
MONO_API MONO_API_EXPORT const uint32_t map_module_count;
MONO_API MONO_API_EXPORT const uint32_t java_type_count;
MONO_API MONO_API_EXPORT const char* const java_type_names[];
MONO_API MONO_API_EXPORT TypeMapModule map_modules[];
MONO_API MONO_API_EXPORT const TypeMapJava map_java[];
MONO_API MONO_API_EXPORT const xamarin::android::hash_t map_java_hashes[];
#endif

MONO_API MONO_API_EXPORT CompressedAssemblies compressed_assemblies;
MONO_API MONO_API_EXPORT const ApplicationConfig application_config;
MONO_API MONO_API_EXPORT const char* const app_environment_variables[];
MONO_API MONO_API_EXPORT const char* const app_system_properties[];

MONO_API MONO_API_EXPORT const char* const mono_aot_mode_name;

MONO_API MONO_API_EXPORT XamarinAndroidBundledAssembly bundled_assemblies[];
MONO_API MONO_API_EXPORT AssemblyStoreSingleAssemblyRuntimeData assembly_store_bundled_assemblies[];
MONO_API MONO_API_EXPORT AssemblyStoreRuntimeData assembly_store;

MONO_API MONO_API_EXPORT DSOCacheEntry dso_cache[];
MONO_API MONO_API_EXPORT DSOApkEntry dso_apk_entries[];

//
// Support for marshal methods
//
#if defined (RELEASE)
struct MarshalMethodsManagedClass
{
	const uint32_t   token;
	MonoClass       *klass;
};

// Number of assembly name forms for which we generate hashes (essentially file name mutations. For instance
// `HelloWorld.dll`, `HelloWorld`, `en-US/HelloWorld` etc). This is multiplied by the number of assemblies in the apk to
// obtain number of entries in the `assembly_image_cache_hashes` and `assembly_image_cache_indices` entries
constexpr uint32_t number_of_assembly_name_forms_in_image_cache = 3;

// These 3 arrays constitute the cache used to store pointers to loaded managed assemblies.
// Three arrays are used so that we can have multiple hashes pointing to the same MonoImage*.
//
// This is done by the `assembly_image_cache_hashes` containing hashes for all mutations of some
// assembly's name (e.g. with culture prefix, without extension etc) and position of that hash in
// `assembly_image_cache_hashes` is an index into `assembly_image_cache_indices` which, in turn,
// stores final index into the `assembly_image_cache` array.
//
MONO_API MONO_API_EXPORT MonoImage* assembly_image_cache[];
MONO_API MONO_API_EXPORT const uint32_t assembly_image_cache_indices[];
MONO_API MONO_API_EXPORT const xamarin::android::hash_t assembly_image_cache_hashes[];

// Number of unique classes which contain native callbacks we bind
MONO_API MONO_API_EXPORT uint32_t marshal_methods_number_of_classes;
MONO_API MONO_API_EXPORT MarshalMethodsManagedClass marshal_methods_class_cache[];

//
// These tables store names of classes and managed callback methods used in the generated marshal methods
// code. They are used just for error reporting.
//
// Class names are found at the same indexes as their corresponding entries in the `marshal_methods_class_cache` array
// above. Method names are stored as token:name pairs and the array must end with an "invalid" terminator entry (token
// == 0; name == nullptr)
//
struct MarshalMethodName
{
	// combination of assembly index (high 32 bits) and method token (low 32 bits)
	const uint64_t  id;
	const char     *name;
};

MONO_API MONO_API_EXPORT const char* const mm_class_names[];
MONO_API MONO_API_EXPORT const MarshalMethodName mm_method_names[];

using get_function_pointer_fn = void(*)(uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr);

MONO_API MONO_API_EXPORT void xamarin_app_init (JNIEnv *env, get_function_pointer_fn fn) noexcept;
#endif // def RELEASE

#endif // __XAMARIN_ANDROID_TYPEMAP_H
