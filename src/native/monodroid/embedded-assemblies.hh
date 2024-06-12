// Dear Emacs, this is a -*- C++ -*- header
#ifndef INC_MONODROID_EMBEDDED_ASSEMBLIES_H
#define INC_MONODROID_EMBEDDED_ASSEMBLIES_H

#include <array>
#include <cerrno>
#include <cstring>
#include <limits>
#include <string_view>
#include <tuple>
#include <vector>

#include <dirent.h>
#include <semaphore.h>

#include <mono/metadata/object.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/mono-private-unstable.h>

#include "strings.hh"
#include "xamarin-app.hh"
#include "cpp-util.hh"
#include "cppcompat.hh"
#include "shared-constants.hh"
#include "xxhash.hh"

#include <concepts>

struct TypeMapHeader;

namespace xamarin::android::internal {
#if defined (DEBUG)
	struct TypeMappingInfo;
#endif

#if defined (RELEASE)
#define STATIC_IN_ANDROID_RELEASE static
#else
#define STATIC_IN_ANDROID_RELEASE
#endif // def RELEASE

	template<typename T>
	concept ByteArrayContainer = requires (T a) {
		a.size ();
		a.data ();
		requires std::same_as<typename T::value_type, uint8_t>;
	};

	template<typename T>
	concept LoaderData = requires (T a) {
		requires std::same_as<T, bool> || std::same_as<T, MonoAssemblyLoadContextGCHandle>;
	};

	class EmbeddedAssemblies final
	{
		struct md_mmap_info {
			void   *area;
			size_t  size;
		};

		struct ZipEntryLoadState
		{
			int                   file_fd;
			const char *          file_name;
			const char * const    prefix;
			uint32_t              prefix_len;
			size_t                buf_offset;
			uint16_t              compression_method;
			uint32_t              local_header_offset;
			uint32_t              data_offset;
			uint32_t              file_size;
			bool                  bundled_assemblies_slow_path;
			uint32_t              max_assembly_name_size;
			uint32_t              max_assembly_file_name_size;
		};

	private:
		static constexpr std::string_view ZIP_CENTRAL_MAGIC { "PK\1\2" };
		static constexpr std::string_view ZIP_LOCAL_MAGIC   { "PK\3\4" };
		static constexpr std::string_view ZIP_EOCD_MAGIC    { "PK\5\6" };
		static constexpr off_t ZIP_EOCD_LEN        = 22;
		static constexpr off_t ZIP_CENTRAL_LEN     = 46;
		static constexpr off_t ZIP_LOCAL_LEN       = 30;

		static constexpr std::string_view zip_path_separator { "/" };
		static constexpr std::string_view apk_lib_dir_name { "lib" };
		static constexpr size_t assemblies_prefix_size = calc_size(apk_lib_dir_name, zip_path_separator, SharedConstants::android_lib_abi, zip_path_separator);
		static constexpr auto assemblies_prefix = concat_string_views<assemblies_prefix_size> (apk_lib_dir_name, zip_path_separator, SharedConstants::android_lib_abi, zip_path_separator);

		// We have two records for each assembly, for names with and without the extension
		static constexpr uint32_t assembly_store_index_entries_per_assembly = 2;
		static constexpr uint32_t number_of_assembly_store_files = 1;
		static constexpr std::string_view dso_suffix { ".so" };

		static constexpr auto apk_lib_prefix = assemblies_prefix; // concat_const (apk_lib_dir_name, zip_path_separator, SharedConstants::android_lib_abi, zip_path_separator);
		static constexpr std::string_view assembly_store_prefix { "libassemblies." };
		static constexpr std::string_view assembly_store_extension { ".blob" };

		static constexpr size_t assembly_store_file_name_size = calc_size (assembly_store_prefix, SharedConstants::android_lib_abi, assembly_store_extension, dso_suffix);
		static constexpr auto assembly_store_file_name = concat_string_views<assembly_store_file_name_size> (assembly_store_prefix, SharedConstants::android_lib_abi, assembly_store_extension, dso_suffix);

		static constexpr size_t assembly_store_file_path_size = calc_size(apk_lib_dir_name, zip_path_separator, SharedConstants::android_lib_abi, zip_path_separator, assembly_store_prefix, SharedConstants::android_lib_abi, assembly_store_extension, dso_suffix);
		static constexpr auto assembly_store_file_path = concat_string_views<assembly_store_file_path_size> (apk_lib_dir_name, zip_path_separator, SharedConstants::android_lib_abi, zip_path_separator, assembly_store_prefix, SharedConstants::android_lib_abi, assembly_store_extension, dso_suffix);

	public:
		/* filename is e.g. System.dll, System.dll.mdb, System.pdb */
		using monodroid_should_register = bool (*)(const char *filename);

	public:
#if defined (RELEASE)
		EmbeddedAssemblies () noexcept
		{}
#endif  // def RELEASE

#if defined (DEBUG)
		void try_load_typemaps_from_directory (const char *path);
#endif
		STATIC_IN_ANDROID_RELEASE const char* typemap_managed_to_java (MonoReflectionType *type, const uint8_t *mvid) noexcept;

		void install_preload_hooks_for_appdomains ();
		void install_preload_hooks_for_alc ();
		STATIC_IN_ANDROID_RELEASE MonoReflectionType* typemap_java_to_managed (MonoString *java_type) noexcept;

		/* returns current number of *all* assemblies found from all invocations */
		template<bool (*should_register_fn)(const char*)>
		size_t register_from_apk (const char *apk_file) noexcept
		{
			static_assert (should_register_fn != nullptr, "should_register_fn is a required template parameter");
			return register_from_apk (apk_file, should_register_fn);
		}

		template<bool (*should_register_fn)(const char*)>
		size_t register_from_filesystem () noexcept
		{
			static_assert (should_register_fn != nullptr, "should_register_fn is a required template parameter");
			return register_from_filesystem (should_register_fn);
		}

		static constexpr decltype(assemblies_prefix) const& get_assemblies_prefix () noexcept
		{
			return assemblies_prefix;
		}

		bool get_register_debug_symbols () const
		{
			return register_debug_symbols;
		}

		void set_register_debug_symbols (bool value)
		{
			register_debug_symbols = value;
		}

		void set_assemblies_prefix (const char *prefix);

		void get_runtime_config_blob (const char *& area, uint32_t& size) const
		{
			area = static_cast<char*>(runtime_config_blob_mmap.area);

			abort_unless (runtime_config_blob_mmap.size < std::numeric_limits<uint32_t>::max (), "Runtime config binary blob size exceeds %u bytes", std::numeric_limits<uint32_t>::max ());
			size = static_cast<uint32_t>(runtime_config_blob_mmap.size);
		}

		bool have_runtime_config_blob () const noexcept
		{
			return application_config.have_runtime_config_blob && runtime_config_blob_mmap.area != nullptr;
		}

		bool keep_scanning () const noexcept
		{
			return need_to_scan_more_apks;
		}

		void ensure_valid_assembly_stores () const noexcept
		{
			if (!application_config.have_assembly_store) {
				return;
			}

			abort_unless (assembly_store_hashes != nullptr, "Invalid or incomplete assembly store data");
		}

	private:
		STATIC_IN_ANDROID_RELEASE const char* typemap_managed_to_java (MonoType *type, MonoClass *klass, const uint8_t *mvid) noexcept;
		STATIC_IN_ANDROID_RELEASE MonoReflectionType* typemap_java_to_managed (hash_t hash, const MonoString *java_type_name) noexcept;
		size_t register_from_apk (const char *apk_file, monodroid_should_register should_register) noexcept;
		size_t register_from_filesystem (monodroid_should_register should_register) noexcept;
		size_t register_from_filesystem (const char *dir, bool look_for_mangled_names, monodroid_should_register should_register) noexcept;

		template<bool MangledNamesMode>
		bool maybe_register_assembly_from_filesystem (monodroid_should_register should_register, size_t& assembly_count, const dirent* dir_entry, ZipEntryLoadState& state) noexcept;
		bool maybe_register_blob_from_filesystem (monodroid_should_register should_register, size_t& assembly_count, const dirent* dir_entry, ZipEntryLoadState& state) noexcept;

		void gather_bundled_assemblies_from_apk (const char* apk, monodroid_should_register should_register);

		template<LoaderData TLoaderData>
		MonoAssembly* individual_assemblies_open_from_bundles (dynamic_local_string<SENSIBLE_PATH_MAX>& name, TLoaderData loader_data, bool ref_only) noexcept;

		template<LoaderData TLoaderData>
		MonoAssembly* assembly_store_open_from_bundles (dynamic_local_string<SENSIBLE_PATH_MAX>& name, TLoaderData loader_data, bool ref_only) noexcept;

		template<LoaderData TLoaderData>
		MonoAssembly* open_from_bundles (MonoAssemblyName* aname, TLoaderData loader_data, MonoError *error, bool ref_only) noexcept;

		template<bool LogMapping>
		void map_runtime_file (XamarinAndroidBundledAssembly& file) noexcept;
		void map_assembly (XamarinAndroidBundledAssembly& file) noexcept;
		void map_debug_data (XamarinAndroidBundledAssembly& file) noexcept;

		template<LoaderData TLoaderData>
		MonoAssembly* load_bundled_assembly (
			XamarinAndroidBundledAssembly& assembly,
			dynamic_local_string<SENSIBLE_PATH_MAX> const& name,
			dynamic_local_string<SENSIBLE_PATH_MAX> const& abi_name,
			TLoaderData loader_data,
			bool ref_only) noexcept;

#if defined (DEBUG)
		template<typename H>
		bool typemap_read_header (int dir_fd, const char *file_type, const char *dir_path, const char *file_path, uint32_t expected_magic, H &header, size_t &file_size, int &fd);
		std::unique_ptr<uint8_t[]> typemap_load_index (int dir_fd, const char *dir_path, const char *index_path);
		std::unique_ptr<uint8_t[]> typemap_load_index (TypeMapIndexHeader &header, size_t file_size, int index_fd);
		bool typemap_load_file (int dir_fd, const char *dir_path, const char *file_path, TypeMap &module);
		bool typemap_load_file (BinaryTypeMapHeader &header, const char *dir_path, const char *file_path, int file_fd, TypeMap &module);
		static ssize_t do_read (int fd, void *buf, size_t count);
		const TypeMapEntry *typemap_managed_to_java (const char *managed_type_name) noexcept;
#endif // DEBUG

		static md_mmap_info md_mmap_apk_file (int fd, uint32_t offset, size_t size, const char* filename);
		static MonoAssembly* open_from_bundles_full (MonoAssemblyName *aname, char **assemblies_path, void *user_data);
		static MonoAssembly* open_from_bundles (MonoAssemblyLoadContextGCHandle alc_gchandle, MonoAssemblyName *aname, char **assemblies_path, void *user_data, MonoError *error);

		void set_assembly_data_and_size (uint8_t* source_assembly_data, uint32_t source_assembly_data_size, uint8_t*& dest_assembly_data, uint32_t& dest_assembly_data_size) noexcept;
		void get_assembly_data (uint8_t *data, uint32_t data_size, const char *name, uint8_t*& assembly_data, uint32_t& assembly_data_size) noexcept;
		void get_assembly_data (XamarinAndroidBundledAssembly const& e, uint8_t*& assembly_data, uint32_t& assembly_data_size) noexcept;
		void get_assembly_data (AssemblyStoreSingleAssemblyRuntimeData const& e, uint8_t*& assembly_data, uint32_t& assembly_data_size) noexcept;

		void zip_load_entries (int fd, const char *apk_name, monodroid_should_register should_register);
		void zip_load_individual_assembly_entries (std::vector<uint8_t> const& buf, uint32_t num_entries, monodroid_should_register should_register, ZipEntryLoadState &state) noexcept;
		void zip_load_assembly_store_entries (std::vector<uint8_t> const& buf, uint32_t num_entries, ZipEntryLoadState &state) noexcept;
		bool zip_load_entry_common (size_t entry_index, std::vector<uint8_t> const& buf, dynamic_local_string<SENSIBLE_PATH_MAX> &entry_name, ZipEntryLoadState &state) noexcept;
		bool zip_read_cd_info (int fd, uint32_t& cd_offset, uint32_t& cd_size, uint16_t& cd_entries);
		bool zip_adjust_data_offset (int fd, ZipEntryLoadState &state);

		template<size_t BufSize>
		bool zip_extract_cd_info (std::array<uint8_t, BufSize> const& buf, uint32_t& cd_offset, uint32_t& cd_size, uint16_t& cd_entries);

		template<class T>
		bool zip_ensure_valid_params (T const& buf, size_t index, size_t to_read) const noexcept;

		template<ByteArrayContainer T>
		bool zip_read_field (T const& src, size_t source_index, uint16_t& dst) const noexcept;

		template<ByteArrayContainer T>
		bool zip_read_field (T const& src, size_t source_index, uint32_t& dst) const noexcept;

		template<ByteArrayContainer T>
		bool zip_read_field (T const& src, size_t source_index, std::array<uint8_t, 4>& dst_sig) const noexcept;

		template<ByteArrayContainer T>
		bool zip_read_field (T const& buf, size_t index, size_t count, dynamic_local_string<SENSIBLE_PATH_MAX>& characters) const noexcept;

		bool zip_read_entry_info (std::vector<uint8_t> const& buf, dynamic_local_string<SENSIBLE_PATH_MAX>& file_name, ZipEntryLoadState &state);

		std::tuple<const char*, uint32_t> get_assemblies_prefix_and_length () const noexcept
		{
			if (assemblies_prefix_override != nullptr) {
				return { assemblies_prefix_override, static_cast<uint32_t>(strlen (assemblies_prefix_override)) };
			}

			if (application_config.have_assembly_store) {
				return { apk_lib_prefix.data (), apk_lib_prefix.size () - 1 };
			}

			return {assemblies_prefix.data (), assemblies_prefix.size () - 1};
		}

		bool all_required_zip_entries_found () const noexcept
		{
			return
				number_of_mapped_assembly_stores == number_of_assembly_store_files && number_of_zip_dso_entries >= application_config.number_of_shared_libraries
				&& ((application_config.have_runtime_config_blob && runtime_config_blob_found) || !application_config.have_runtime_config_blob);
		}

		force_inline static c_unique_ptr<char> to_utf8 (const MonoString *s) noexcept
		{
			return c_unique_ptr<char> (mono_string_to_utf8 (const_cast<MonoString*>(s)));
		}

		template<typename Key, typename Entry, int (*compare)(const Key*, const Entry*), bool use_extra_size = false>
		static const Entry* binary_search (const Key *key, const Entry *base, size_t nmemb, size_t extra_size = 0) noexcept;

#if defined (DEBUG)
		static int compare_type_name (const char *type_name, const TypeMapEntry *entry) noexcept;
#else
		static int compare_mvid (const uint8_t *mvid, const TypeMapModule *module) noexcept;
		static const TypeMapModuleEntry* binary_search (uint32_t key, const TypeMapModuleEntry *arr, uint32_t n) noexcept;
#endif
		template<bool NeedsNameAlloc>
		void set_entry_data (XamarinAndroidBundledAssembly &entry, ZipEntryLoadState const& state, dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name) noexcept;
		void set_assembly_entry_data (XamarinAndroidBundledAssembly &entry, ZipEntryLoadState const& state, dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name) noexcept;
		void set_debug_entry_data (XamarinAndroidBundledAssembly &entry, ZipEntryLoadState const& state, dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name) noexcept;
		void map_assembly_store (dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name, ZipEntryLoadState &state) noexcept;
		const AssemblyStoreIndexEntry* find_assembly_store_entry (hash_t hash, const AssemblyStoreIndexEntry *entries, size_t entry_count) noexcept;
		void store_individual_assembly_data (dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name, ZipEntryLoadState const& state, monodroid_should_register should_register) noexcept;

		constexpr size_t get_mangled_name_max_size_overhead ()
		{
			return SharedConstants::MANGLED_ASSEMBLY_NAME_EXT.size() +
				   std::max (SharedConstants::MANGLED_ASSEMBLY_REGULAR_ASSEMBLY_MARKER.size(), SharedConstants::MANGLED_ASSEMBLY_SATELLITE_ASSEMBLY_MARKER.size()) +
				   1; // For the extra `-` char in the culture portion of satellite assembly's name
		}

		void configure_state_for_individual_assembly_load (ZipEntryLoadState& state) noexcept
		{
			state.bundled_assemblies_slow_path = bundled_assembly_index >= application_config.number_of_assemblies_in_apk;
			state.max_assembly_name_size = application_config.bundled_assembly_name_width - 1;

			// Enough room for the mangle character at the start, plus the extra extension
			state.max_assembly_file_name_size = static_cast<uint32_t>(state.max_assembly_name_size + get_mangled_name_max_size_overhead ());
		}

		template<bool IsSatelliteAssembly>
		static constexpr size_t get_mangled_prefix_length ()
		{
			if constexpr (IsSatelliteAssembly) {
				// +1 for the extra `-` char in the culture portion of satellite assembly's name;
				return SharedConstants::MANGLED_ASSEMBLY_SATELLITE_ASSEMBLY_MARKER.length () + 1;
			} else {
				return SharedConstants::MANGLED_ASSEMBLY_REGULAR_ASSEMBLY_MARKER.length ();
			}
		}

		template<bool IsSatelliteAssembly>
		static constexpr size_t get_mangled_data_size ()
		{
			return SharedConstants::MANGLED_ASSEMBLY_NAME_EXT.length () + get_mangled_prefix_length<IsSatelliteAssembly> ();
		}

		template<bool IsSatelliteAssembly>
		static void unmangle_name (dynamic_local_string<SENSIBLE_PATH_MAX> &name, size_t start_idx = 0) noexcept
		{
			constexpr size_t mangled_data_size = get_mangled_data_size<IsSatelliteAssembly> ();
			if (name.length () <= mangled_data_size) {
				// Nothing to do, the name is too short
				return;
			}

			size_t new_size = name.length () - mangled_data_size;
			memmove (name.get () + start_idx, name.get () + start_idx + get_mangled_prefix_length<IsSatelliteAssembly> (), new_size);
			name.set_length (new_size);

			if constexpr (IsSatelliteAssembly) {
				// Make sure assembly name is {CULTURE}/assembly.dll
				for (size_t idx = start_idx; idx < name.length (); idx++) {
					if (name[idx] == SharedConstants::SATELLITE_ASSEMBLY_MARKER_CHAR) {
						name[idx] = '/';
						break;
					}
				}
			}
			log_debug (LOG_ASSEMBLY, "Unmangled name to '%s'", name.get ());
		};

	private:
		static inline constexpr bool UnmangleSatelliteAssembly = true;
		static inline constexpr bool UnmangleRegularAssembly = false;

		std::vector<XamarinAndroidBundledAssembly> *bundled_debug_data = nullptr;
		std::vector<XamarinAndroidBundledAssembly> *extra_bundled_assemblies = nullptr;

		bool                   register_debug_symbols;
		bool                   have_and_want_debug_symbols;
		size_t                 bundled_assembly_index = 0;
		size_t                 number_of_found_assemblies = 0;

#if defined (DEBUG)
		TypeMappingInfo       *java_to_managed_maps;
		TypeMappingInfo       *managed_to_java_maps;
		TypeMap               *type_maps;
		size_t                 type_map_count;
#endif // DEBUG
		const char            *assemblies_prefix_override = nullptr;

		md_mmap_info           runtime_config_blob_mmap{};
		bool                   runtime_config_blob_found = false;
		uint32_t               number_of_mapped_assembly_stores = 0;
		uint32_t               number_of_zip_dso_entries = 0;
		bool                   need_to_scan_more_apks = true;

		AssemblyStoreIndexEntry *assembly_store_hashes;
		xamarin::android::mutex  assembly_decompress_mutex;
	};
}

#endif /* INC_MONODROID_EMBEDDED_ASSEMBLIES_H */
