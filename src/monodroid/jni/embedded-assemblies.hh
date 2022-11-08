// Dear Emacs, this is a -*- C++ -*- header
#ifndef INC_MONODROID_EMBEDDED_ASSEMBLIES_H
#define INC_MONODROID_EMBEDDED_ASSEMBLIES_H

#include <array>

#include <cerrno>
#include <cstring>
#include <limits>
#include <functional>
#include <optional>
#include <vector>
#include <semaphore.h>

#include <mono/metadata/object.h>
#include <mono/metadata/assembly.h>

#if defined (NET)
#include <mono/metadata/mono-private-unstable.h>
#endif

#include "strings.hh"
#include "xamarin-app.hh"
#include "cpp-util.hh"
#include "mono-image-loader.hh"
#include "shared-constants.hh"
#include "xxhash.hh"
#include "gsl.hh"

#undef HAVE_CONCEPTS

// Xcode has supports for concepts only since 12.5
#if __has_include (<concepts>)
#define HAVE_CONCEPTS
#include <concepts>
#endif // __has_include

struct TypeMapHeader;

namespace xamarin::android::internal {
#if defined (DEBUG) || !defined (ANDROID)
	struct TypeMappingInfo;
#endif

#if defined (RELEASE) && defined (ANDROID)
#define STATIC_IN_ANDROID_RELEASE static
#else
#define STATIC_IN_ANDROID_RELEASE
#endif // def RELEASE && def ANDROID

#if defined (HAVE_CONCEPTS)
	template<typename T>
	concept ByteArrayContainer = requires (T a) {
		a.size ();
		a.data ();
		requires std::same_as<typename T::value_type, uint8_t>;
	};

	template<typename T>
	concept LoaderData = requires (T a) {
		requires std::same_as<T, bool>
#if defined (NET)
		|| std::same_as<T, MonoAssemblyLoadContextGCHandle>
#endif
		;
	};
#else
#define ByteArrayContainer class
#define LoaderData typename
#endif

	class EmbeddedAssemblies final
	{
		struct md_mmap_info {
			void   *area;
			size_t  size;
		};

		struct ZipEntryLoadState
		{
			int                   apk_fd;
			const char * const    apk_name;
			const char * const    prefix;
			uint32_t              prefix_len;
			size_t                buf_offset;
			uint16_t              compression_method;
			uint32_t              local_header_offset;
			uint32_t              data_offset;
			uint32_t              file_size;
		};

	private:
		static constexpr char  ZIP_CENTRAL_MAGIC[] = "PK\1\2";
		static constexpr char  ZIP_LOCAL_MAGIC[]   = "PK\3\4";
		static constexpr char  ZIP_EOCD_MAGIC[]    = "PK\5\6";
		static constexpr off_t ZIP_EOCD_LEN        = 22;
		static constexpr off_t ZIP_CENTRAL_LEN     = 46;
		static constexpr off_t ZIP_LOCAL_LEN       = 30;
		static constexpr char  assemblies_prefix[] = "assemblies/";
		static constexpr char  zip_path_separator[] = "/";

		static constexpr char assembly_store_prefix[] = "assemblies";
		static constexpr char assembly_store_extension[] = ".blob";
		static constexpr auto assembly_store_common_file_name = concat_const ("/", assembly_store_prefix, assembly_store_extension);
		static constexpr auto assembly_store_arch_file_name = concat_const ("/", assembly_store_prefix, ".", SharedConstants::android_abi, assembly_store_extension);


#if defined (DEBUG) || !defined (ANDROID)
		static constexpr char override_typemap_entry_name[] = ".__override__";
#endif

	public:
		/* filename is e.g. System.dll, System.dll.mdb, System.pdb */
		using monodroid_should_register = bool (*)(const char *filename);

	public:
#if defined (RELEASE) && defined (ANDROID)
		EmbeddedAssemblies () noexcept
		{}
#endif  // def RELEASE && def ANDROID

#if defined (DEBUG) || !defined (ANDROID)
		static void try_load_typemaps_from_directory (const char *path) noexcept;
#endif
		static const char* typemap_managed_to_java (MonoReflectionType *type, const uint8_t *mvid) noexcept;

		static void install_preload_hooks_for_appdomains () noexcept;
#if defined (NET)
		static void install_preload_hooks_for_alc () noexcept;
#endif // def NET
		static MonoReflectionType* typemap_java_to_managed (MonoString *java_type) noexcept;

		/* returns current number of *all* assemblies found from all invocations */
		template<bool (*should_register_fn)(const char*)>
		static size_t register_from (const char *apk_file) noexcept
		{
			LOG_FUNC_ENTER ();

			static_assert (should_register_fn != nullptr, "should_register_fn is a required template parameter");

			LOG_FUNC_LEAVE ();
			return register_from (apk_file, should_register_fn);
		}

		static bool get_register_debug_symbols () noexcept
		{
			LOG_FUNC_ENTER ();
			LOG_FUNC_LEAVE ();
			return register_debug_symbols;
		}

		static void set_register_debug_symbols (bool value) noexcept
		{
			LOG_FUNC_ENTER ();

			register_debug_symbols = value;

			LOG_FUNC_LEAVE ();
		}

		static void set_assemblies_prefix (const char *prefix) noexcept;

#if defined (NET)
		static void get_runtime_config_blob (const char *& area, uint32_t& size) noexcept
		{
			area = static_cast<char*>(runtime_config_blob_mmap.area);

			abort_unless (runtime_config_blob_mmap.size < std::numeric_limits<uint32_t>::max (), "Runtime config binary blob size exceeds %u bytes", std::numeric_limits<uint32_t>::max ());
			size = static_cast<uint32_t>(runtime_config_blob_mmap.size);
		}

		static bool have_runtime_config_blob () noexcept
		{
			return application_config.have_runtime_config_blob && runtime_config_blob_mmap.area != nullptr;
		}
#endif
		static bool keep_scanning () noexcept
		{
			return need_to_scan_more_apks;
		}

		static void ensure_valid_assembly_stores () noexcept
		{
			if (!application_config.have_assembly_store) {
				return;
			}

			abort_unless (index_assembly_store_header != nullptr && assembly_store_hashes != nullptr, "Invalid or incomplete assembly store data");
		}

	private:
		static const char* typemap_managed_to_java (MonoType *type, MonoClass *klass, const uint8_t *mvid) noexcept;
		static MonoReflectionType* typemap_java_to_managed (hash_t hash, const MonoString *java_type_name) noexcept;
		static size_t register_from (const char *apk_file, monodroid_should_register should_register) noexcept;
		static void gather_bundled_assemblies_from_apk (const char* apk, monodroid_should_register should_register) noexcept;

		template<LoaderData TLoaderData>
		static MonoAssembly* individual_assemblies_open_from_bundles (dynamic_local_string<SENSIBLE_PATH_MAX>& name, TLoaderData loader_data, bool ref_only) noexcept;

		template<LoaderData TLoaderData>
		static MonoAssembly* assembly_store_open_from_bundles (dynamic_local_string<SENSIBLE_PATH_MAX>& name, TLoaderData loader_data, bool ref_only) noexcept;

		template<LoaderData TLoaderData>
		static MonoAssembly* open_from_bundles (MonoAssemblyName* aname, TLoaderData loader_data, MonoError *error, bool ref_only) noexcept;

		template<bool LogMapping>
		static void map_runtime_file (XamarinAndroidBundledAssembly& file) noexcept;
		static void map_assembly (XamarinAndroidBundledAssembly& file) noexcept;
		static void map_debug_data (XamarinAndroidBundledAssembly& file) noexcept;

		template<LoaderData TLoaderData>
		static MonoAssembly* load_bundled_assembly (
			XamarinAndroidBundledAssembly& assembly,
			dynamic_local_string<SENSIBLE_PATH_MAX> const& name,
			dynamic_local_string<SENSIBLE_PATH_MAX> const& abi_name,
			TLoaderData loader_data,
			bool ref_only) noexcept;

#if defined (DEBUG) || !defined (ANDROID)
		template<typename H>
		static bool typemap_read_header (int dir_fd, const char *file_type, const char *dir_path, const char *file_path, uint32_t expected_magic, H &header, size_t &file_size, int &fd) noexcept;
		static std::unique_ptr<uint8_t[]> typemap_load_index (int dir_fd, const char *dir_path, const char *index_path) noexcept;
		static std::unique_ptr<uint8_t[]> typemap_load_index (TypeMapIndexHeader &header, size_t file_size, int index_fd) noexcept;
		static bool typemap_load_file (int dir_fd, const char *dir_path, const char *file_path, TypeMap &module) noexcept;
		static bool typemap_load_file (BinaryTypeMapHeader &header, const char *dir_path, const char *file_path, int file_fd, TypeMap &module) noexcept;
		static ssize_t do_read (int fd, void *buf, size_t count) noexcept;
		static const TypeMapEntry *typemap_managed_to_java (const char *managed_type_name) noexcept;
#endif // DEBUG || !ANDROID

		static md_mmap_info md_mmap_apk_file (int fd, uint32_t offset, size_t size, const char* filename);
		static MonoAssembly* open_from_bundles_full (MonoAssemblyName *aname, char **assemblies_path, void *user_data);
#if defined (NET)
		static MonoAssembly* open_from_bundles (MonoAssemblyLoadContextGCHandle alc_gchandle, MonoAssemblyName *aname, char **assemblies_path, void *user_data, MonoError *error);
#else // def NET
		static MonoAssembly* open_from_bundles_refonly (MonoAssemblyName *aname, char **assemblies_path, void *user_data);
#endif // ndef NET
		static void get_assembly_data (uint8_t *data, uint32_t data_size, const char *name, uint8_t*& assembly_data, uint32_t& assembly_data_size) noexcept;
		static void get_assembly_data (XamarinAndroidBundledAssembly const& e, uint8_t*& assembly_data, uint32_t& assembly_data_size) noexcept;
		static void get_assembly_data (AssemblyStoreSingleAssemblyRuntimeData const& e, uint8_t*& assembly_data, uint32_t& assembly_data_size) noexcept;

		static void zip_load_entries (int fd, const char *apk_name, monodroid_should_register should_register) noexcept;
		static void zip_load_individual_assembly_entries (std::vector<uint8_t> const& buf, uint32_t num_entries, monodroid_should_register should_register, ZipEntryLoadState &state) noexcept;
		static void zip_load_assembly_store_entries (std::vector<uint8_t> const& buf, uint32_t num_entries, ZipEntryLoadState &state) noexcept;
		static bool zip_load_entry_common (size_t entry_index, std::vector<uint8_t> const& buf, dynamic_local_string<SENSIBLE_PATH_MAX> &entry_name, ZipEntryLoadState &state) noexcept;
		static bool zip_read_cd_info (int fd, uint32_t& cd_offset, uint32_t& cd_size, uint16_t& cd_entries) noexcept;
		static bool zip_adjust_data_offset (int fd, ZipEntryLoadState &state) noexcept;

		template<size_t BufSize>
		static bool zip_extract_cd_info (std::array<uint8_t, BufSize> const& buf, uint32_t& cd_offset, uint32_t& cd_size, uint16_t& cd_entries) noexcept;

		template<class T>
		static bool zip_ensure_valid_params (T const& buf, size_t index, size_t to_read) noexcept;

		template<ByteArrayContainer T>
		static std::optional<uint16_t> zip_read_field_u16 (T const& src, size_t source_index) noexcept;

		template<ByteArrayContainer T>
		static std::optional<uint32_t> zip_read_field_u32 (T const& src, size_t source_index) noexcept;

		template<ByteArrayContainer T>
		static bool zip_read_field (T const& src, size_t source_index, std::array<uint8_t, 4>& dst_sig) noexcept;

		template<ByteArrayContainer T>
		static bool zip_read_field (T const& buf, size_t index, size_t count, dynamic_local_string<SENSIBLE_PATH_MAX>& characters) noexcept;

		static bool zip_read_entry_info (std::vector<uint8_t> const& buf, dynamic_local_string<SENSIBLE_PATH_MAX>& file_name, ZipEntryLoadState &state) noexcept;

		static const char* get_assemblies_prefix () noexcept
		{
			return assemblies_prefix_override != nullptr ? assemblies_prefix_override : assemblies_prefix;
		}

		static uint32_t get_assemblies_prefix_length () noexcept
		{
			return assemblies_prefix_override != nullptr ? static_cast<uint32_t>(strlen (assemblies_prefix_override)) : sizeof(assemblies_prefix) - 1;
		}

		static bool all_required_zip_entries_found () noexcept
		{
			return
				number_of_mapped_assembly_stores == application_config.number_of_assembly_store_files
#if defined (NET)
				&& ((application_config.have_runtime_config_blob && runtime_config_blob_found) || !application_config.have_runtime_config_blob)
#endif // NET
				;
		}

		static force_inline c_unique_ptr<char> to_utf8 (const MonoString *s) noexcept
		{
			return c_unique_ptr<char> (mono_string_to_utf8 (const_cast<MonoString*>(s)));
		}

		static bool is_debug_file (dynamic_local_string<SENSIBLE_PATH_MAX> const& name) noexcept;

		template<typename Key, typename Entry, int (*compare)(const Key*, const Entry*), bool use_extra_size = false>
		static const Entry* binary_search (const Key *key, const Entry *base, size_t nmemb, size_t extra_size = 0) noexcept;

#if defined (DEBUG) || !defined (ANDROID)
		static int compare_type_name (const char *type_name, const TypeMapEntry *entry) noexcept;
#else
		static int compare_mvid (const uint8_t *mvid, const TypeMapModule *module) noexcept;
		static const TypeMapModuleEntry* binary_search (uint32_t key, const TypeMapModuleEntry *arr, uint32_t n) noexcept;
#endif
		template<bool NeedsNameAlloc>
		static void set_entry_data (XamarinAndroidBundledAssembly &entry, int apk_fd, uint32_t data_offset, uint32_t data_size, uint32_t prefix_len, uint32_t max_name_size, dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name) noexcept;
		static void set_assembly_entry_data (XamarinAndroidBundledAssembly &entry, int apk_fd, uint32_t data_offset, uint32_t data_size, uint32_t prefix_len, uint32_t max_name_size, dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name) noexcept;
		static void set_debug_entry_data (XamarinAndroidBundledAssembly &entry, int apk_fd, uint32_t data_offset, uint32_t data_size, uint32_t prefix_len, uint32_t max_name_size, dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name) noexcept;
		static void map_assembly_store (dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name, ZipEntryLoadState &state) noexcept;
		static const AssemblyStoreHashEntry* find_assembly_store_entry (hash_t hash, const AssemblyStoreHashEntry *entries, size_t entry_count) noexcept;

	private:
		using bundled_assembly_vector = std::vector<XamarinAndroidBundledAssembly>;

		static inline gsl::owner<bundled_assembly_vector*> bundled_debug_data = nullptr;
		static inline gsl::owner<bundled_assembly_vector*> extra_bundled_assemblies = nullptr;

		static inline bool                     register_debug_symbols = false;
		static inline bool                     have_and_want_debug_symbols = false;
		static inline size_t                   bundled_assembly_index = 0;
		static inline size_t                   number_of_found_assemblies = 0;

#if defined (DEBUG) || !defined (ANDROID)
		static inline TypeMappingInfo         *java_to_managed_maps = nullptr;
		static inline TypeMappingInfo         *managed_to_java_maps = nullptr;
		static inline TypeMap                 *type_maps = nullptr;
		static inline size_t                   type_map_count = 0;
#endif // DEBUG || !ANDROID
		static inline const char              *assemblies_prefix_override = nullptr;
#if defined (NET)
		static inline md_mmap_info             runtime_config_blob_mmap{};
		static inline bool                     runtime_config_blob_found = false;
#endif // def NET
		static inline uint32_t                 number_of_mapped_assembly_stores = 0;
		static inline bool                     need_to_scan_more_apks = true;

		static inline AssemblyStoreHeader     *index_assembly_store_header = nullptr;
		static inline AssemblyStoreHashEntry  *assembly_store_hashes;
	};
}

#if !defined (NET)
MONO_API int monodroid_embedded_assemblies_set_assemblies_prefix (const char *prefix);
#endif // ndef NET

#endif /* INC_MONODROID_EMBEDDED_ASSEMBLIES_H */
