#include <host-config.h>

#if !defined (__MINGW32__) || (defined (__MINGW32__) && __GNUC__ >= 10)
#include <compare>
#endif // ndef MINGW32 || def MINGW32 && GNUC >= 10
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <sys/stat.h>
#include <sys/mman.h>
#include <fcntl.h>
#include <cctype>
#include <libgen.h>
#include <cerrno>
#include <unistd.h>

#if defined (HAVE_LZ4)
#include <lz4.h>
#endif

#include <mono/metadata/assembly.h>
#include <mono/metadata/class.h>
#include <mono/metadata/image.h>
#include <mono/metadata/mono-config.h>
#include <mono/metadata/mono-debug.h>
#include <mono/metadata/reflection.h>

#include "java-interop-util.h"

#include "monodroid.h"
#include "util.hh"
#include "embedded-assemblies.hh"
#include "globals.hh"
#include "monodroid-glue.hh"
#include "mono-image-loader.hh"
#include "xamarin-app.hh"
#include "cpp-util.hh"
#include "startup-aware-lock.hh"
#include "timing-internal.hh"
#include "search.hh"

using namespace xamarin::android;
using namespace xamarin::android::internal;

// A utility class which allows us to manage memory allocated by `mono_guid_to_string` in an elegant way. We can create
// temporary instances of this class in calls to e.g. `log_debug` which are executed ONLY when debug logging is enabled
class MonoGuidString final
{
public:
	explicit MonoGuidString (const uint8_t *id) noexcept
		: guid (mono_guid_to_string (id))
	{}

	MonoGuidString (const MonoGuidString&) = delete;
	MonoGuidString (MonoGuidString&&) = delete;

	~MonoGuidString ()
	{
		::free (guid);
	}

	void operator = (const MonoGuidString&) = delete;
	void operator = (MonoGuidString&&) = delete;

	[[nodiscard]]
	const char* get () const noexcept
	{
		return guid;
	}

private:
	gsl::owner<char*> guid = nullptr;
};

void EmbeddedAssemblies::set_assemblies_prefix (const char *prefix) noexcept
{
	LOG_FUNC_ENTER ();

	if (assemblies_prefix_override != nullptr)
		delete[] assemblies_prefix_override;
	assemblies_prefix_override = prefix != nullptr ? Util::strdup_new (prefix) : nullptr;

	LOG_FUNC_LEAVE ();
}

force_inline void
EmbeddedAssemblies::get_assembly_data (uint8_t *data, uint32_t data_size, [[maybe_unused]] const char *name, uint8_t*& assembly_data, uint32_t& assembly_data_size) noexcept
{
	LOG_FUNC_ENTER ();

#if defined (ANDROID) && defined (HAVE_LZ4) && defined (RELEASE)
	auto header = reinterpret_cast<const CompressedAssemblyHeader*>(data);
	if (header->magic == COMPRESSED_DATA_MAGIC) {
		if (XA_UNLIKELY (compressed_assemblies.descriptors == nullptr)) {
			log_fatal (LOG_ASSEMBLY, "Compressed assembly found but no descriptor defined");
			exit (FATAL_EXIT_MISSING_ASSEMBLY);
		}
		if (XA_UNLIKELY (header->descriptor_index >= compressed_assemblies.count)) {
			log_fatal (LOG_ASSEMBLY, "Invalid compressed assembly descriptor index %u", header->descriptor_index);
			exit (FATAL_EXIT_MISSING_ASSEMBLY);
		}

		CompressedAssemblyDescriptor &cad = compressed_assemblies.descriptors[header->descriptor_index];
		assembly_data_size = data_size - sizeof(CompressedAssemblyHeader);
		if (!cad.loaded) {
			if (XA_UNLIKELY (cad.data == nullptr)) {
				log_fatal (LOG_ASSEMBLY, "Invalid compressed assembly descriptor at %u: no data", header->descriptor_index);
				exit (FATAL_EXIT_MISSING_ASSEMBLY);
			}

			bool log_timing = FastTiming::enabled () && !FastTiming::is_bare_mode ();
			size_t decompress_time_index;
			if (XA_UNLIKELY (log_timing)) {
				decompress_time_index = internal_timing->start_event (TimingEventKind::AssemblyDecompression);
			}

			if (header->uncompressed_length != cad.uncompressed_file_size) {
				if (header->uncompressed_length > cad.uncompressed_file_size) {
					log_fatal (LOG_ASSEMBLY, "Compressed assembly '%s' is larger than when the application was built (expected at most %u, got %u). Assemblies don't grow just like that!", name, cad.uncompressed_file_size, header->uncompressed_length);
					exit (FATAL_EXIT_MISSING_ASSEMBLY);
				} else {
					log_debug (LOG_ASSEMBLY, "Compressed assembly '%s' is smaller than when the application was built. Adjusting accordingly.", name);
				}
				cad.uncompressed_file_size = header->uncompressed_length;
			}

			const char *data_start = reinterpret_cast<const char*>(data + sizeof(CompressedAssemblyHeader));
			int ret = LZ4_decompress_safe (data_start, reinterpret_cast<char*>(cad.data), static_cast<int>(assembly_data_size), static_cast<int>(cad.uncompressed_file_size));

			if (XA_UNLIKELY (log_timing)) {
				internal_timing->end_event (decompress_time_index, true /* uses_more_info */);
				internal_timing->add_more_info (decompress_time_index, name);
			}

			if (ret < 0) {
				log_fatal (LOG_ASSEMBLY, "Decompression of assembly %s failed with code %d", name, ret);
				exit (FATAL_EXIT_MISSING_ASSEMBLY);
			}

			if (static_cast<uint64_t>(ret) != cad.uncompressed_file_size) {
				log_debug (LOG_ASSEMBLY, "Decompression of assembly %s yielded a different size (expected %lu, got %u)", name, cad.uncompressed_file_size, static_cast<uint32_t>(ret));
				exit (FATAL_EXIT_MISSING_ASSEMBLY);
			}
			cad.loaded = true;
		}
		assembly_data = reinterpret_cast<uint8_t*>(cad.data);
		assembly_data_size = cad.uncompressed_file_size;
	} else
#endif
	{
		assembly_data = data;
		assembly_data_size = data_size;
	}

	LOG_FUNC_LEAVE ();
}

force_inline void
EmbeddedAssemblies::get_assembly_data (XamarinAndroidBundledAssembly const& e, uint8_t*& assembly_data, uint32_t& assembly_data_size) noexcept
{
	LOG_FUNC_ENTER ();

	get_assembly_data (e.data, e.data_size, e.name, assembly_data, assembly_data_size);

	LOG_FUNC_LEAVE ();
}

force_inline void
EmbeddedAssemblies::get_assembly_data (AssemblyStoreSingleAssemblyRuntimeData const& e, uint8_t*& assembly_data, uint32_t& assembly_data_size) noexcept
{
	LOG_FUNC_ENTER ();

	get_assembly_data (e.image_data, e.descriptor->data_size, "<assembly_store>", assembly_data, assembly_data_size);

	LOG_FUNC_LEAVE ();
}

template<bool LogMapping>
force_inline void
EmbeddedAssemblies::map_runtime_file (XamarinAndroidBundledAssembly& file) noexcept
{
	LOG_FUNC_ENTER ();

	md_mmap_info map_info = md_mmap_apk_file (file.apk_fd, file.data_offset, file.data_size, file.name);
	if (MonodroidRuntime::is_startup_in_progress ()) {
		file.data = static_cast<uint8_t*>(map_info.area);
	} else {
		uint8_t *expected_null = nullptr;
		bool already_mapped = !__atomic_compare_exchange (
			/* ptr */              &file.data,
			/* expected */         &expected_null,
			/* desired */           reinterpret_cast<uint8_t**>(&map_info.area),
			/* weak */              false,
			/* success_memorder */  __ATOMIC_ACQUIRE,
			/* failure_memorder */  __ATOMIC_RELAXED
		);

		if (already_mapped) {
			log_debug (LOG_ASSEMBLY, "Assembly %s already mmapped by another thread, unmapping our copy", file.name);
			munmap (map_info.area, file.data_size);
			map_info.area = nullptr;
		}
	}

	if constexpr (LogMapping) {
		if (XA_UNLIKELY (Util::should_log (LOG_ASSEMBLY) && map_info.area != nullptr)) {
			auto *p = reinterpret_cast<const char*>(file.data);

			auto byte_to_char = [&] (size_t index) {
				if (index >= file.data_size) {
					return '\0';
				}

				return isprint (p[index]) ? p[index] : '.';
			};

			constexpr size_t NEEDED_FILE_DATA_SIZE = 8;
			std::array<char, NEEDED_FILE_DATA_SIZE + 1> header {
				byte_to_char (0),
				byte_to_char (1),
				byte_to_char (2),
				byte_to_char (3),
				byte_to_char (4),
				byte_to_char (5),
				byte_to_char (6),
				byte_to_char (7),
				'\0',
			};

			log_info_nocheck (LOG_ASSEMBLY, "file-offset: % 8x  start: %08p  end: %08p  len: % 12i  zip-entry:  %s name: %s [%s]",
			                  (int) file.data_offset, file.data, file.data + file.data_size, (int) file.data_size, file.name, file.name, header.data ());
		}
	}

	LOG_FUNC_LEAVE ();
}

force_inline void
EmbeddedAssemblies::map_assembly (XamarinAndroidBundledAssembly& file) noexcept
{
	LOG_FUNC_ENTER ();

	map_runtime_file<true> (file);

	LOG_FUNC_LEAVE ();
}

force_inline void
EmbeddedAssemblies::map_debug_data (XamarinAndroidBundledAssembly& file) noexcept
{
	LOG_FUNC_ENTER ();

	map_runtime_file<false> (file);

	LOG_FUNC_LEAVE ();
}

template<LoaderData TLoaderData>
force_inline MonoAssembly*
EmbeddedAssemblies::load_bundled_assembly (
	XamarinAndroidBundledAssembly& assembly,
	dynamic_local_string<SENSIBLE_PATH_MAX> const& name,
	dynamic_local_string<SENSIBLE_PATH_MAX> const& abi_name,
	TLoaderData loader_data,
	bool ref_only) noexcept
{
	LOG_FUNC_ENTER ();

	if (assembly.name == nullptr || assembly.name[0] == '\0') {
		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	if (strcmp (assembly.name, name.get ()) != 0) {
		if (strcmp (assembly.name, abi_name.get ()) != 0) {
			LOG_FUNC_LEAVE ();
			return nullptr;
		} else {
			log_debug (LOG_ASSEMBLY, "open_from_bundles: found architecture-specific: '%s'", abi_name.get ());
		}
	}

	if (assembly.data == nullptr) {
		map_assembly (assembly);
	}

	uint8_t *assembly_data;
	uint32_t assembly_data_size;

	get_assembly_data (assembly, assembly_data, assembly_data_size);
	MonoImage *image = MonoImageLoader::load (name, loader_data, assembly_data, assembly_data_size);
	if (image == nullptr) {
		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	if (have_and_want_debug_symbols) {
		uint32_t base_name_length = assembly.name_length - 3; // we need the trailing dot
		for (XamarinAndroidBundledAssembly& debug_file : *bundled_debug_data) {
			if (debug_file.name_length != assembly.name_length) {
				continue;
			}

			if (strncmp (debug_file.name, assembly.name, base_name_length) != 0) {
				continue;
			}

			if (debug_file.data == nullptr) {
				map_debug_data (debug_file);
			}

			if (debug_file.data != nullptr) {
				if (debug_file.data_size > std::numeric_limits<int>::max ()) {
					log_warn (LOG_ASSEMBLY, "Debug info file '%s' is too big for Mono to consume", debug_file.name);
				} else {
					mono_debug_open_image_from_memory (image, reinterpret_cast<const mono_byte*>(debug_file.data), static_cast<int>(debug_file.data_size));
				}
			}
			break;
		}
	}

	MonoImageOpenStatus status;
	MonoAssembly *a = mono_assembly_load_from_full (image, name.get (), &status, ref_only);
	if (a == nullptr || status != MonoImageOpenStatus::MONO_IMAGE_OK) {
		log_warn (LOG_ASSEMBLY, "Failed to load managed assembly '%s'. %s", name.get (), mono_image_strerror (status));

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

#if !defined (NET)
	// In dotnet the call is a no-op
	mono_config_for_assembly (image);
#endif

	LOG_FUNC_LEAVE ();
	return a;
}

template<LoaderData TLoaderData>
force_inline MonoAssembly*
EmbeddedAssemblies::individual_assemblies_open_from_bundles (dynamic_local_string<SENSIBLE_PATH_MAX>& name, TLoaderData loader_data, bool ref_only) noexcept
{
	LOG_FUNC_ENTER ();

	if (!Util::ends_with (name, SharedConstants::DLL_EXTENSION)) {
		name.append (SharedConstants::DLL_EXTENSION);
	}

	log_debug (LOG_ASSEMBLY, "individual_assemblies_open_from_bundles: looking for bundled name: '%s'", name.get ());

	dynamic_local_string<SENSIBLE_PATH_MAX> abi_name;
	abi_name
		.assign_c (BasicAndroidSystem::get_built_for_abi_name ())
		.append (zip_path_separator)
		.append (name);

	MonoAssembly *a = nullptr;

	for (size_t i = 0; i < application_config.number_of_assemblies_in_apk; i++) {
		a = load_bundled_assembly (bundled_assemblies [i], name, abi_name, loader_data, ref_only);
		if (a != nullptr) {
			LOG_FUNC_LEAVE ();
			return a;
		}
	}

	if (extra_bundled_assemblies != nullptr) {
		for (XamarinAndroidBundledAssembly& assembly : *extra_bundled_assemblies) {
			a = load_bundled_assembly (assembly, name, abi_name, loader_data, ref_only);
			if (a != nullptr) {
				LOG_FUNC_LEAVE ();
				return a;
			}
		}
	}

	LOG_FUNC_LEAVE ();
	return nullptr;
}

force_inline const AssemblyStoreHashEntry*
EmbeddedAssemblies::find_assembly_store_entry ([[maybe_unused]] hash_t hash, [[maybe_unused]] const AssemblyStoreHashEntry *entries, [[maybe_unused]] size_t entry_count) noexcept
{
	LOG_FUNC_ENTER ();

#if !defined (__MINGW32__) || (defined (__MINGW32__) && __GNUC__ >= 10)
	hash_t entry_hash;
	const AssemblyStoreHashEntry *ret = nullptr;

	while (entry_count > 0) {
		ret = entries + (entry_count / 2);
		if constexpr (std::is_same_v<hash_t, uint64_t>) {
			entry_hash = ret->hash64;
		} else {
			entry_hash = ret->hash32;
		}
		auto result = hash <=> entry_hash;

		if (result < 0) {
			entry_count /= 2;
		} else if (result > 0) {
			entries = ret + 1;
			entry_count -= entry_count / 2 + 1;
		} else {
			LOG_FUNC_LEAVE ();
			return ret;
		}
	}
#endif // ndef __MINGW32__ || (def __MINGW32__ && __GNUC__ >= 10)

	LOG_FUNC_LEAVE ();
	return nullptr;
}

template<LoaderData TLoaderData>
force_inline MonoAssembly*
EmbeddedAssemblies::assembly_store_open_from_bundles (dynamic_local_string<SENSIBLE_PATH_MAX>& name, TLoaderData loader_data, bool ref_only) noexcept
{
	LOG_FUNC_ENTER ();

	size_t len = name.length ();
	bool have_dll_ext = Util::ends_with (name, SharedConstants::DLL_EXTENSION);

	if (have_dll_ext) {
		len -= sizeof(SharedConstants::DLL_EXTENSION) - 1;
	}

	std::string clipped_name;
	clipped_name.assign (name.get (), len);

	hash_t name_hash = xxhash::hash (name.get (), len);
	log_debug (LOG_ASSEMBLY, "assembly_store_open_from_bundles: looking for bundled name: '%s' (hash 0x%zx)", name.get (), name_hash);

	const AssemblyStoreHashEntry *hash_entry = find_assembly_store_entry (name_hash, assembly_store_hashes, application_config.number_of_assemblies_in_apk);
	if (hash_entry == nullptr) {
		log_warn (LOG_ASSEMBLY, "Assembly '%s' (hash 0x%zx) not found", name.get (), name_hash);
		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	if (hash_entry->mapping_index >= application_config.number_of_assemblies_in_apk) {
		log_fatal (LOG_ASSEMBLY, "Invalid assembly index %u, exceeds the maximum index of %u", hash_entry->mapping_index, application_config.number_of_assemblies_in_apk - 1);
		abort ();
	}

	AssemblyStoreSingleAssemblyRuntimeData &assembly_runtime_info = assembly_store_bundled_assemblies[hash_entry->mapping_index];
	if (assembly_runtime_info.image_data == nullptr) {
		if (hash_entry->store_id >= application_config.number_of_assembly_store_files) {
			log_fatal (LOG_ASSEMBLY, "Invalid assembly store ID %u, exceeds the maximum of %u", hash_entry->store_id, application_config.number_of_assembly_store_files - 1);
			abort ();
		}

		AssemblyStoreRuntimeData &rd = assembly_stores[hash_entry->store_id];
		if (hash_entry->local_store_index >= rd.assembly_count) {
			log_fatal (LOG_ASSEMBLY, "Invalid index %u into local store assembly descriptor array", hash_entry->local_store_index);
			abort ();
		}

		AssemblyStoreAssemblyDescriptor *bba = &rd.assemblies[hash_entry->local_store_index];

		// The assignments here don't need to be atomic, the value will always be the same, so even if two threads
		// arrive here at the same time, nothing bad will happen.
		assembly_runtime_info.image_data = rd.data_start + bba->data_offset;
		assembly_runtime_info.descriptor = bba;

		if (bba->debug_data_offset != 0) {
			assembly_runtime_info.debug_info_data = rd.data_start + bba->debug_data_offset;
		}
#if !defined (NET)
		if (bba->config_data_size != 0) {
			assembly_runtime_info.config_data = rd.data_start + bba->config_data_offset;

			// Mono takes ownership of the pointers
			mono_register_config_for_assembly (
				Util::string_concat (name.get (), ".dll"),
				Util::strdup_new (reinterpret_cast<const char*>(assembly_runtime_info.config_data))
			);
		}
#endif // NET

		log_debug (
			LOG_ASSEMBLY,
			"Mapped: image_data == %p; debug_info_data == %p; config_data == %p; descriptor == %p; data size == %u; debug data size == %u; config data size == %u",
			assembly_runtime_info.image_data,
			assembly_runtime_info.debug_info_data,
			assembly_runtime_info.config_data,
			assembly_runtime_info.descriptor,
			assembly_runtime_info.descriptor->data_size,
			assembly_runtime_info.descriptor->debug_data_size,
			assembly_runtime_info.descriptor->config_data_size
		);
	}

	uint8_t *assembly_data;
	uint32_t assembly_data_size;

	if (!have_dll_ext) {
		// AOT needs this since Mono will form the DSO name by appending the .so suffix to the assembly name passed to
		// functions below and `monodroid_dlopen` uses the `.dll.so` extension to check whether we're being asked to load
		// the AOTd code for an assembly.
		name.append (SharedConstants::DLL_EXTENSION);
	}

	get_assembly_data (assembly_runtime_info, assembly_data, assembly_data_size);
	MonoImage *image = MonoImageLoader::load (name, loader_data, name_hash, assembly_data, assembly_data_size);
	if (image == nullptr) {
		log_warn (LOG_ASSEMBLY, "Failed to load MonoImage of '%s'", name.get ());

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	if (have_and_want_debug_symbols && assembly_runtime_info.debug_info_data != nullptr) {
		log_debug (LOG_ASSEMBLY, "Registering debug data for assembly '%s'", name.get ());
		mono_debug_open_image_from_memory (image, reinterpret_cast<const mono_byte*> (assembly_runtime_info.debug_info_data), static_cast<int>(assembly_runtime_info.descriptor->debug_data_size));
	}

	MonoImageOpenStatus status;
	MonoAssembly *a = mono_assembly_load_from_full (image, name.get (), &status, ref_only);
	if (a == nullptr || status != MonoImageOpenStatus::MONO_IMAGE_OK) {
		log_warn (LOG_ASSEMBLY, "Failed to load managed assembly '%s'. %s", name.get (), mono_image_strerror (status));

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

#if !defined (NET)
	mono_config_for_assembly (image);
#endif

	LOG_FUNC_LEAVE ();
	return a;
}

// TODO: need to forbid loading assemblies into non-default ALC if they contain marshal method callbacks.
//       The best way is probably to store the information in the assembly `MonoImage*` cache. We should
//       abort() if the assembly contains marshal callbacks.
template<LoaderData TLoaderData>
force_inline MonoAssembly*
EmbeddedAssemblies::open_from_bundles (MonoAssemblyName* aname, TLoaderData loader_data, [[maybe_unused]] MonoError *error, bool ref_only) noexcept
{
	LOG_FUNC_ENTER ();

	const char *culture = mono_assembly_name_get_culture (aname);
	const char *asmname = mono_assembly_name_get_name (aname);

	dynamic_local_string<SENSIBLE_PATH_MAX> name;
	if (culture != nullptr && *culture != '\0') {
		name.append_c (culture);
		name.append (zip_path_separator);
	}
	name.append_c (asmname);

	MonoAssembly *a;
	if (application_config.have_assembly_store) {
		a = assembly_store_open_from_bundles (name, loader_data, ref_only);
	} else {
		a = individual_assemblies_open_from_bundles (name, loader_data, ref_only);
	}

	if (a == nullptr) {
		log_warn (LOG_ASSEMBLY, "open_from_bundles: failed to load assembly %s", name.get ());
	}

	LOG_FUNC_LEAVE ();
	return a;
}

#if defined (NET)
MonoAssembly*
EmbeddedAssemblies::open_from_bundles (MonoAssemblyLoadContextGCHandle alc_gchandle, MonoAssemblyName *aname, [[maybe_unused]] char **assemblies_path, [[maybe_unused]] void *user_data, MonoError *error)
{
	LOG_FUNC_ENTER ();

	constexpr bool ref_only = false;

	LOG_FUNC_LEAVE ();
	return open_from_bundles (aname, alc_gchandle, error, ref_only);
}
#else // def NET

// The duplicated `ref_only` parameters in the two functions below look weird, but the first one of them is used to
// call the right instance of the templateed MonoImageLoader::load method (just as`alc_gchandle` above) which loads a
// `MonoImage`, while the other is used when loading a `MonoAssembly`
MonoAssembly*
EmbeddedAssemblies::open_from_bundles_refonly (MonoAssemblyName *aname, [[maybe_unused]] char **assemblies_path, [[maybe_unused]] void *user_data)
{
	LOG_FUNC_ENTER ();
	constexpr bool ref_only = true;

	LOG_FUNC_LEAVE ();
	return open_from_bundles (aname, ref_only /* loader_data */, nullptr /* error */, ref_only);
}
#endif // ndef NET

MonoAssembly*
EmbeddedAssemblies::open_from_bundles_full (MonoAssemblyName *aname, [[maybe_unused]] char **assemblies_path, [[maybe_unused]] void *user_data)
{
	LOG_FUNC_ENTER ();

	constexpr bool ref_only = false;

	LOG_FUNC_LEAVE ();
	return open_from_bundles (aname, ref_only /* loader_data */, nullptr /* error */, ref_only);
}

void
EmbeddedAssemblies::install_preload_hooks_for_appdomains () noexcept
{
	LOG_FUNC_ENTER ();

	mono_install_assembly_preload_hook (open_from_bundles_full, nullptr);
#if !defined (NET)
	mono_install_assembly_refonly_preload_hook (open_from_bundles_refonly, nullptr);
#endif // ndef NET

	LOG_FUNC_LEAVE ();
}

#if defined (NET)
void
EmbeddedAssemblies::install_preload_hooks_for_alc () noexcept
{
	LOG_FUNC_ENTER ();

	mono_install_assembly_preload_hook_v3 (
		open_from_bundles,
		nullptr /* user_data */,
		0 /* append */
	);

	LOG_FUNC_LEAVE ();
}
#endif // def NET

template<typename Key, typename Entry, int (*compare)(const Key*, const Entry*), bool use_precalculated_size>
force_inline const Entry*
EmbeddedAssemblies::binary_search (const Key *key, const Entry *base, size_t nmemb, [[maybe_unused]] size_t precalculated_size) noexcept
{
	LOG_FUNC_ENTER ();

	static_assert (compare != nullptr, "compare is a required template parameter");

	// This comes from the user code, so let's be civil
	if (key == nullptr) {
		log_warn (LOG_ASSEMBLY, "Key passed to binary_search must not be nullptr");

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	// This is a coding error on our part, crash!
	if (base == nullptr) {
		log_fatal (LOG_ASSEMBLY, "Map address not passed to binary_search");
		exit (FATAL_EXIT_MISSING_ASSEMBLY);
	}

	[[maybe_unused]]
	size_t size;

	if constexpr (use_precalculated_size) {
		size = precalculated_size;
		log_info (LOG_ASSEMBLY, "Pre-calculated entry size = %u", size);
	}

	while (nmemb > 0) {
		const Entry *ret;
		if constexpr (use_precalculated_size) {
			ret = reinterpret_cast<const Entry*>(reinterpret_cast<const uint8_t*>(base) + (precalculated_size * (nmemb / 2)));
		} else {
			ret = base + (nmemb / 2);
		}

		int result = compare (key, ret);
		if (result < 0) {
			nmemb /= 2;
		} else if (result > 0) {
			if constexpr (use_precalculated_size) {
				base = reinterpret_cast<const Entry*>(reinterpret_cast<const uint8_t*>(ret) + precalculated_size);
			} else {
				base = ret + 1;
			}
			nmemb -= nmemb / 2 + 1;
		} else {
			LOG_FUNC_LEAVE ();
			return ret;
		}
	}

	LOG_FUNC_LEAVE ();
	return nullptr;
}

#if defined (RELEASE) && defined (ANDROID)
force_inline const TypeMapModuleEntry*
EmbeddedAssemblies::binary_search (uint32_t key, const TypeMapModuleEntry *arr, uint32_t n) noexcept
{
	LOG_FUNC_ENTER ();

	ssize_t left = -1;
	auto right = static_cast<ssize_t>(n);
	ssize_t middle;

	while (right - left > 1) {
		middle = (left + right) >> 1;
		if (arr[middle].type_token_id < key) {
			left = middle;
		} else {
			right = middle;
		}
	}

	LOG_FUNC_LEAVE ();
	return arr[right].type_token_id == key ? &arr[right] : nullptr;
}
#endif // def RELEASE && def ANDROID

#if defined (DEBUG) || !defined (ANDROID)
force_inline int
EmbeddedAssemblies::compare_type_name (const char *type_name, const TypeMapEntry *entry) noexcept
{
	LOG_FUNC_ENTER ();
	if (entry == nullptr) {
		LOG_FUNC_LEAVE ();
		return 1;
	}

	LOG_FUNC_LEAVE ();
	return strcmp (type_name, entry->from);
}

force_inline MonoReflectionType*
EmbeddedAssemblies::typemap_java_to_managed ([[maybe_unused]] hash_t hash, const MonoString *java_type) noexcept
{
	LOG_FUNC_ENTER ();

	c_unique_ptr<char> java_type_name {mono_string_to_utf8 (const_cast<MonoString*>(java_type))};
	const TypeMapEntry *entry = nullptr;

	if (application_config.instant_run_enabled) {
		LOG_LOCATION ();
		TypeMap *module;
		for (size_t i = 0; i < type_map_count; i++) {
			module = &type_maps[i];
			entry = binary_search<const char, TypeMapEntry, compare_type_name, false> (java_type_name.get (), module->java_to_managed, module->entry_count);
			if (entry != nullptr)
				break;
		}
	} else {
		LOG_LOCATION ();
		entry = binary_search<const char, TypeMapEntry, compare_type_name, false> (java_type_name.get (), type_map.java_to_managed, type_map.entry_count);
	}

	if (XA_UNLIKELY (entry == nullptr)) {
		log_info (LOG_ASSEMBLY, "typemap: unable to find mapping to a managed type from Java type '%s'", java_type_name.get ());

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	const char *managed_type_name = entry->to;
	if (managed_type_name == nullptr) {
		log_debug (LOG_ASSEMBLY, "typemap: Java type '%s' maps either to an open generic type or an interface type.", java_type_name.get ());

		LOG_FUNC_LEAVE ();
		return nullptr;
	}
 	log_debug (LOG_DEFAULT, "typemap: Java type '%s' corresponds to managed type '%s'", java_type_name.get (), managed_type_name);

	MonoType *type = mono_reflection_type_from_name (const_cast<char*>(managed_type_name), nullptr);
	if (XA_UNLIKELY (type == nullptr)) {
		log_info (LOG_ASSEMBLY, "typemap: managed type '%s' (mapped from Java type '%s') could not be loaded", managed_type_name, java_type_name.get ());

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	MonoReflectionType *ret = mono_type_get_object (Util::get_current_domain (), type);
	if (XA_UNLIKELY (ret == nullptr)) {
		log_warn (LOG_ASSEMBLY, "typemap: unable to instantiate managed type '%s'", managed_type_name);

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	LOG_FUNC_LEAVE ();
	return ret;
}
#else
force_inline MonoReflectionType*
EmbeddedAssemblies::typemap_java_to_managed (hash_t hash, const MonoString *java_type_name) noexcept
{
	LOG_FUNC_ENTER ();

	// In microbrenchmarks, `binary_search_branchless` is faster than `binary_search` but in "real" application tests,
	// the simple version appears to yield faster startup... Leaving both for now, for further investigation and
	// potential optimizations
	ssize_t idx = Search::binary_search (hash, map_java_hashes, java_type_count);
	//ptrdiff_t idx = binary_search_branchless (hash, map_java_hashes, java_type_count);

	TypeMapJava const* java_entry = idx >= 0 ? &map_java[idx] : nullptr;
	TypeMapModule *module = java_entry != nullptr && java_entry->module_index < map_module_count ? &map_modules[java_entry->module_index] : nullptr;
	if (module == nullptr) {
		if (java_entry == nullptr) {
			log_info (LOG_ASSEMBLY, "typemap: unable to find mapping to a managed type from Java type '%s' (hash 0x%zx)", to_utf8 (java_type_name).get (), hash);
		} else {
			log_warn (LOG_ASSEMBLY, "typemap: mapping from Java type '%s' to managed type has invalid module index %u", to_utf8 (java_type_name).get (), java_entry->module_index);
		}

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	const TypeMapModuleEntry *entry = binary_search (java_entry->type_token_id, module->map, module->entry_count);
	if (entry == nullptr) {
		log_info (LOG_ASSEMBLY, "typemap: unable to find mapping from Java type '%s' to managed type with token ID %u in module [%s]", to_utf8 (java_type_name).get (), java_entry->type_token_id, MonoGuidString (module->module_uuid).get ());

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	if (module->image == nullptr) {
		module->image = mono_image_loaded (module->assembly_name);
		if (module->image == nullptr) {
			log_error (LOG_ASSEMBLY, "typemap: unable to load assembly '%s' when looking up managed type corresponding to Java type '%s'", module->assembly_name, to_utf8 (java_type_name).get ());

			LOG_FUNC_LEAVE ();
			return nullptr;
		}
	}

	log_debug (LOG_ASSEMBLY, "typemap: java type '%s' corresponds to managed token id %u (0x%x)", to_utf8 (java_type_name).get (), java_entry->type_token_id, java_entry->type_token_id);
	MonoClass *klass = mono_class_get (module->image, java_entry->type_token_id);
	if (klass == nullptr) [[unlikely]] {
		log_error (LOG_ASSEMBLY, "typemap: unable to find managed type with token ID %u in assembly '%s', corresponding to Java type '%s'", java_entry->type_token_id, module->assembly_name, to_utf8 (java_type_name).get ());

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

#if defined (NET)
	// MonoVM in dotnet runtime doesn't use the `domain` parameter passed to `mono_type_get_object` (since AppDomains
	// are gone in NET 6+), in fact, the function `mono_type_get_object` calls (`mono_type_get_object_checked`) itself
	// calls `mono_get_root_domain`. Thus, we can save on a one function call here by passing `nullptr`
	constexpr MonoDomain *domain = nullptr;
#else
	MonoDomain *domain = Util::get_current_domain ();
#endif
	MonoReflectionType *ret = mono_type_get_object (domain, mono_class_get_type (klass));
	if (ret == nullptr) {
		log_warn (LOG_ASSEMBLY, "typemap: unable to instantiate managed type with token ID %u in assembly '%s', corresponding to Java type '%s'", java_entry->type_token_id, module->assembly_name, to_utf8 (java_type_name).get ());

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	LOG_FUNC_LEAVE ();
	return ret;
}
#endif

MonoReflectionType*
EmbeddedAssemblies::typemap_java_to_managed (MonoString *java_type) noexcept
{
	LOG_FUNC_ENTER ();

	size_t total_time_index;
	if (XA_UNLIKELY (FastTiming::enabled ())) {
		MonodroidRuntime::init_managed_timing ();
		total_time_index = internal_timing->start_event (TimingEventKind::JavaToManaged);
	}

	if (XA_UNLIKELY (java_type == nullptr)) {
		log_warn (LOG_ASSEMBLY, "typemap: null 'java_type' passed to 'typemap_java_to_managed'");

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	// We need to generate hash for all the bytes, and since MonoString is Unicode, we double the length to get the
	// number of bytes.
	int name_len = mono_string_length (java_type) << 1;
	if (XA_UNLIKELY (name_len <= 0)) {
		log_warn (LOG_ASSEMBLY, "typemap: empty 'java_type' passed to 'typemap_java_to_managed'");

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	const mono_unichar2 *type_chars = mono_string_chars (java_type);
	hash_t hash = xxhash::hash (reinterpret_cast<const char*>(type_chars), static_cast<size_t>(name_len));
	MonoReflectionType *ret = typemap_java_to_managed (hash, java_type);

	if (XA_UNLIKELY (FastTiming::enabled ())) {
		DEAR_GCC_THIS_VARIABLE_IS_INITIALIZED_START
		internal_timing->end_event (total_time_index);
		DEAR_GCC_THIS_VARIABLE_IS_INITIALIZED_END
	}

	LOG_FUNC_LEAVE ();
	return ret;
}

#if defined (DEBUG) || !defined (ANDROID)
force_inline const TypeMapEntry*
EmbeddedAssemblies::typemap_managed_to_java (const char *managed_type_name) noexcept
{
	LOG_FUNC_ENTER ();

	const TypeMapEntry *entry = nullptr;

	if (application_config.instant_run_enabled) {
		TypeMap *module;
		for (size_t i = 0; i < type_map_count; i++) {
			module = &type_maps[i];
			entry = binary_search<const char, TypeMapEntry, compare_type_name, false> (managed_type_name, module->managed_to_java, module->entry_count);
			if (entry != nullptr)
				break;
		}
	} else {
		entry = binary_search<const char, TypeMapEntry, compare_type_name, false> (managed_type_name, type_map.managed_to_java, type_map.entry_count);
	}

	LOG_FUNC_LEAVE ();
	return entry;
}

force_inline const char*
EmbeddedAssemblies::typemap_managed_to_java ([[maybe_unused]] MonoType *type, MonoClass *klass, [[maybe_unused]] const uint8_t *mvid) noexcept
{
	LOG_FUNC_ENTER ();

	c_unique_ptr<char> type_name {mono_type_get_name_full (type, MONO_TYPE_NAME_FORMAT_FULL_NAME)};
	MonoImage *image = mono_class_get_image (klass);
	const char *image_name = mono_image_get_name (image);
	size_t type_name_len = strlen (type_name.get ());
	size_t image_name_len = strlen (image_name);

	dynamic_local_string<SENSIBLE_PATH_MAX> full_name;
	full_name
		.append (type_name.get (), type_name_len)
		.append (", ")
		.append (image_name, image_name_len);

	const TypeMapEntry *entry = typemap_managed_to_java (full_name.get ());
	if (XA_UNLIKELY (entry == nullptr)) {
		log_info (LOG_ASSEMBLY, "typemap: unable to find mapping to a Java type from managed type '%s'", full_name.get ());

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	LOG_FUNC_LEAVE ();
	return entry->to;
}
#else
force_inline int
EmbeddedAssemblies::compare_mvid (const uint8_t *mvid, const TypeMapModule *module) noexcept
{
	LOG_FUNC_ENTER ();
	LOG_FUNC_LEAVE ();
	return memcmp (mvid, module->module_uuid, sizeof(module->module_uuid));
}

force_inline const char*
EmbeddedAssemblies::typemap_managed_to_java ([[maybe_unused]] MonoType *type, MonoClass *klass, const uint8_t *mvid) noexcept
{
	LOG_FUNC_ENTER ();

	const TypeMapModule *match = mvid != nullptr ? binary_search<uint8_t, TypeMapModule, compare_mvid> (mvid, map_modules, map_module_count) : nullptr;
	if (match == nullptr) {
		if (mvid == nullptr) {
			log_warn (LOG_ASSEMBLY, "typemap: no mvid specified in call to typemap_managed_to_java");
		} else {
			log_info (LOG_ASSEMBLY, "typemap: module matching MVID [%s] not found.", MonoGuidString (mvid).get ());
		}

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	uint32_t token = mono_class_get_type_token (klass);
	log_debug (LOG_ASSEMBLY, "typemap: MVID [%s] maps to assembly %s, looking for token %d (0x%x), table index %d", MonoGuidString (mvid).get (), match->assembly_name, token, token, token & 0x00FFFFFF);
	// Each map entry is a pair of 32-bit integers: [TypeTokenID][JavaMapArrayIndex]
	const TypeMapModuleEntry *entry = match->map != nullptr ? binary_search (token, match->map, match->entry_count) : nullptr;
	if (entry == nullptr) {
		if (match->map == nullptr) {
			log_warn (LOG_ASSEMBLY, "typemap: module with mvid [%s] has no associated type map.", MonoGuidString (mvid).get ());

			LOG_FUNC_LEAVE ();
			return nullptr;
		}

		if (match->duplicate_count > 0 && match->duplicate_map != nullptr) {
			log_debug (LOG_ASSEMBLY, "typemap: searching module [%s] duplicate map for token %u (0x%x)", MonoGuidString (mvid).get (), token, token);
			entry = binary_search (token, match->duplicate_map, match->duplicate_count);
		}

		if (entry == nullptr) {
			log_info (LOG_ASSEMBLY, "typemap: type with token %d (0x%x) in module {%s} (%s) not found.", token, token, MonoGuidString (mvid).get (), match->assembly_name);

			LOG_FUNC_LEAVE ();
			return nullptr;
		}
	}

	if (XA_UNLIKELY (entry->java_map_index >= java_type_count)) {
		log_warn (LOG_ASSEMBLY, "typemap: type with token %d (0x%x) in module {%s} (%s) has invalid Java type index %u", token, token, MonoGuidString (mvid).get (), match->assembly_name, entry->java_map_index);

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	TypeMapJava const& java_entry = map_java[entry->java_map_index];
	if (XA_UNLIKELY (java_entry.java_name_index >= java_type_count)) {
		log_warn (LOG_ASSEMBLY, "typemap: type with token %d (0x%x) in module {%s} (%s) points to invalid Java type at index %u (invalid type name index %u)", token, token, MonoGuidString (mvid).get (), match->assembly_name, entry->java_map_index, java_entry.java_name_index);

		LOG_FUNC_LEAVE ();
		return nullptr;
	}
	const char *ret = java_type_names[java_entry.java_name_index];

	if (XA_UNLIKELY (ret == nullptr)) {
		log_warn (LOG_ASSEMBLY, "typemap: empty Java type name returned for entry at index %u", entry->java_map_index);
	}

	log_debug (
		LOG_ASSEMBLY,
		"typemap: type with token %d (0x%x) in module {%s} (%s) corresponds to Java type '%s'",
		token,
		token,
		MonoGuidString (mvid).get (),
		match->assembly_name,
		ret
	);

	LOG_FUNC_LEAVE ();
	return ret;
}
#endif

const char*
EmbeddedAssemblies::typemap_managed_to_java (MonoReflectionType *reflection_type, const uint8_t *mvid) noexcept
{
	LOG_FUNC_ENTER ();

	size_t total_time_index;
	if (XA_UNLIKELY (FastTiming::enabled ())) {
		MonodroidRuntime::init_managed_timing ();
		total_time_index = internal_timing->start_event (TimingEventKind::ManagedToJava);
	}

	MonoType *type = mono_reflection_type_get_type (reflection_type);
	if (type == nullptr) {
		log_warn (LOG_ASSEMBLY, "Failed to map reflection type to MonoType");

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	const char *ret = typemap_managed_to_java (type, mono_class_from_mono_type (type), mvid);

	if (XA_UNLIKELY (FastTiming::enabled ())) {
		DEAR_GCC_THIS_VARIABLE_IS_INITIALIZED_START
		internal_timing->end_event (total_time_index);
		DEAR_GCC_THIS_VARIABLE_IS_INITIALIZED_END
	}

	LOG_FUNC_LEAVE ();
	return ret;
}

EmbeddedAssemblies::md_mmap_info
EmbeddedAssemblies::md_mmap_apk_file (int fd, uint32_t offset, size_t size, const char* filename)
{
	LOG_FUNC_ENTER ();

	md_mmap_info file_info;
	md_mmap_info mmap_info;

	auto pageSize          = static_cast<size_t>(Util::monodroid_getpagesize ());
	size_t offsetFromPage  = offset % pageSize;
	size_t offsetPage      = offset - offsetFromPage;
	size_t offsetSize      = size + offsetFromPage;

	mmap_info.area        = mmap (nullptr, offsetSize, PROT_READ, MAP_PRIVATE, fd, static_cast<off_t>(offsetPage));

	if (mmap_info.area == MAP_FAILED) {
		log_fatal (LOG_DEFAULT, "Could not `mmap` apk fd %d entry `%s`: %s", fd, filename, strerror (errno));
		exit (FATAL_EXIT_CANNOT_FIND_APK);
	}

	mmap_info.size  = offsetSize;
	file_info.area  = reinterpret_cast<void*>(static_cast<uint8_t*>(mmap_info.area) + offsetFromPage);
	file_info.size  = size;

	log_info (LOG_ASSEMBLY, "                       mmap_start: %08p  mmap_end: %08p  mmap_len: % 12u  file_start: %08p  file_end: %08p  file_len: % 12u      apk descriptor: %d  file: %s",
	          mmap_info.area, reinterpret_cast<int*> (mmap_info.area) + mmap_info.size, mmap_info.size,
	          file_info.area, reinterpret_cast<int*> (file_info.area) + file_info.size, file_info.size, fd, filename);

	LOG_FUNC_LEAVE ();
	return file_info;
}

void
EmbeddedAssemblies::gather_bundled_assemblies_from_apk (const char* apk, monodroid_should_register should_register) noexcept
{
	LOG_FUNC_ENTER ();

	constexpr int OPEN_FLAGS =
		          O_RDONLY
#if !defined (WINDOWS)
		          | O_CLOEXEC
#endif
		          ;

	int fd = open (apk, OPEN_FLAGS);
	if (fd < 0) {
		log_error (LOG_DEFAULT, "ERROR: Unable to load application package %s.", apk);
		exit (FATAL_EXIT_NO_ASSEMBLIES);
	}
	log_info (LOG_ASSEMBLY, "APK %s FD: %d", apk, fd);

	zip_load_entries (fd, apk, should_register);

	LOG_FUNC_LEAVE ();
}

#if defined (DEBUG) || !defined (ANDROID)
ssize_t EmbeddedAssemblies::do_read (int fd, void *buf, size_t count) noexcept
{
	LOG_FUNC_ENTER ();

	ssize_t ret;
	do {
		ret = ::read (
			fd,
			buf,
#if defined (WINDOWS)
			static_cast<unsigned int>(count)
#else
			count
#endif
		);
	} while (ret < 0 && errno == EINTR);

	LOG_FUNC_LEAVE ();
	return ret;
}

template<typename H>
bool
EmbeddedAssemblies::typemap_read_header ([[maybe_unused]] int dir_fd, const char *file_type, const char *dir_path, const char *file_path, uint32_t expected_magic, H &header, size_t &file_size, int &fd) noexcept
{
	LOG_FUNC_ENTER ();

	struct stat sbuf;
	int res;

#if __ANDROID_API__ < 21
	std::unique_ptr<char> full_file_path {Util::path_combine (dir_path, file_path)};
	res = stat (full_file_path.get (), &sbuf);
#else
	res = fstatat (dir_fd, file_path, &sbuf, 0);
#endif
	if (res < 0) {
		log_error (LOG_ASSEMBLY, "typemap: failed to stat %s file '%s/%s': %s", file_type, dir_path, file_path, strerror (errno));

		LOG_FUNC_LEAVE ();
		return false;
	}

	file_size = static_cast<size_t>(sbuf.st_size);
	if (file_size < sizeof (header)) {
		log_error (LOG_ASSEMBLY, "typemap: %s file '%s/%s' is too small (must be at least %u bytes)", file_type, dir_path, file_path, sizeof (header));

		LOG_FUNC_LEAVE ();
		return false;
	}

#if __ANDROID_API__ < 21
	fd = open (full_file_path.get (), O_RDONLY);
#else
	fd = openat (dir_fd, file_path, O_RDONLY);
#endif
	if (fd < 0) {
		log_error (LOG_ASSEMBLY, "typemap: failed to open %s file %s/%s for reading: %s", file_type, dir_path, file_path, strerror (errno));

		LOG_FUNC_LEAVE ();
		return false;
	}

	ssize_t nread = do_read (fd, &header, sizeof (header));
	if (nread <= 0) {
		if (nread < 0) {
			log_error (LOG_ASSEMBLY, "typemap: failed to read %s file header from '%s/%s': %s", file_type, dir_path, file_path, strerror (errno));
		} else {
			log_error (LOG_ASSEMBLY, "typemap: end of file while reading %s file header from '%s/%s'", file_type, dir_path, file_path);
		}

		LOG_FUNC_LEAVE ();
		return false;
	}

	if (header.magic != expected_magic) {
		log_error (LOG_ASSEMBLY, "typemap: invalid magic value in the %s file header from '%s/%s': expected 0x%X, got 0x%X", file_type, dir_path, file_path, expected_magic, header.magic);

		LOG_FUNC_LEAVE ();
		return false;
	}

	if (header.version != MODULE_FORMAT_VERSION) {
		log_error (LOG_ASSEMBLY, "typemap: incompatible %s format version. This build supports only version %u, file '%s/%s' uses version %u", file_type, MODULE_FORMAT_VERSION, dir_path, file_path, header.version);

		LOG_FUNC_LEAVE ();
		return false;
	}

	LOG_FUNC_LEAVE ();
	return true;
}

std::unique_ptr<uint8_t[]>
EmbeddedAssemblies::typemap_load_index (TypeMapIndexHeader &header, size_t file_size, int index_fd) noexcept
{
	LOG_FUNC_ENTER ();

	size_t entry_size = header.module_file_name_width;
	size_t data_size = entry_size * type_map_count;
	if (sizeof(header) + data_size > file_size) {
		log_error (LOG_ASSEMBLY, "typemap: index file is too small, expected %u, found %u bytes", data_size + sizeof(header), file_size);

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	auto data = std::make_unique<uint8_t[]> (data_size);
	ssize_t nread = do_read (index_fd, data.get (), data_size);
	if (nread != static_cast<ssize_t>(data_size)) {
		log_error (LOG_ASSEMBLY, "typemap: failed to read %u bytes from index file. %s", data_size, strerror (errno));

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	uint8_t *p = data.get ();
	for (size_t i = 0; i < type_map_count; i++) {
		type_maps[i].assembly_name = reinterpret_cast<char*>(p);
		p += entry_size;
	}

	LOG_FUNC_LEAVE ();
	return data;
}

std::unique_ptr<uint8_t[]>
EmbeddedAssemblies::typemap_load_index (int dir_fd, const char *dir_path, const char *index_path) noexcept
{
	LOG_FUNC_ENTER ();
	log_debug (LOG_ASSEMBLY, "typemap: loading TypeMap index file '%s/%s'", dir_path, index_path);

	TypeMapIndexHeader header;
	size_t file_size;
	int fd = -1;
	std::unique_ptr<uint8_t[]> data;

	if (typemap_read_header (dir_fd, "TypeMap index", dir_path, index_path, MODULE_INDEX_MAGIC, header, file_size, fd)) {
		type_map_count = header.entry_count;
		type_maps = new TypeMap[type_map_count]();
		data = typemap_load_index (header, file_size, fd);
	}

	if (fd >= 0)
		close (fd);

	LOG_FUNC_LEAVE ();
	return data;
}

bool
EmbeddedAssemblies::typemap_load_file (BinaryTypeMapHeader &header, const char *dir_path, const char *file_path, int file_fd, TypeMap &module) noexcept
{
	LOG_FUNC_ENTER ();

	size_t alloc_size = ADD_WITH_OVERFLOW_CHECK (size_t, header.assembly_name_length, 1);
	module.assembly_name = new char[alloc_size];

	ssize_t nread = do_read (file_fd, module.assembly_name, header.assembly_name_length);
	if (nread != static_cast<ssize_t>(header.assembly_name_length)) {
		log_error (LOG_ASSEMBLY, "tyemap: failed to read map assembly name from '%s/%s': %s", dir_path, file_path, strerror (errno));

		LOG_FUNC_LEAVE ();
		return false;
	}

	module.assembly_name [header.assembly_name_length] = 0;
	module.entry_count = header.entry_count;

	log_debug (
		LOG_ASSEMBLY,
		"typemap: '%s/%s':: entry count == %u; Java name field width == %u; Managed name width == %u; assembly name length == %u; assembly name == %s",
		dir_path, file_path, header.entry_count, header.java_name_width, header.managed_name_width, header.assembly_name_length, module.assembly_name
	);

	// [name][index]
	size_t java_entry_size = header.java_name_width + sizeof(uint32_t);
	size_t managed_entry_size = header.managed_name_width + sizeof(uint32_t);
	size_t data_size = ADD_WITH_OVERFLOW_CHECK (
		size_t,
		header.entry_count * java_entry_size,
		header.entry_count * managed_entry_size
	);

	module.data = new uint8_t [data_size];
	nread = do_read (file_fd, module.data, data_size);
	if (nread != static_cast<ssize_t>(data_size)) {
		log_error (LOG_ASSEMBLY, "tyemap: failed to read map data from '%s/%s': %s", dir_path, file_path, strerror (errno));

		LOG_FUNC_LEAVE ();
		return false;
	}

	module.java_to_managed = new TypeMapEntry [module.entry_count];
	module.managed_to_java = new TypeMapEntry [module.entry_count];

	uint8_t *java_start = module.data;
	uint8_t *managed_start = module.data + (module.entry_count * java_entry_size);
	uint8_t *java_pos = java_start;
	uint8_t *managed_pos = managed_start;
	TypeMapEntry *cur;

	constexpr uint32_t INVALID_TYPE_INDEX = std::numeric_limits<uint32_t>::max ();
	for (size_t i = 0; i < module.entry_count; i++) {
		cur = const_cast<TypeMapEntry*> (&module.java_to_managed[i]);
		cur->from = reinterpret_cast<char*>(java_pos);

		uint32_t idx;
		// This might seem slow but it is in fact compiled into a single instruction and is safe when loading the 32-bit
		// integer from unaligned memory
		memcpy (&idx, java_pos + header.java_name_width, sizeof (idx));
		if (idx < INVALID_TYPE_INDEX) {
			cur->to = reinterpret_cast<char*>(managed_start + (managed_entry_size * idx));
		} else {
			// Ignore the type mapping
			cur->to = nullptr;
		}
		java_pos += java_entry_size;

		cur = const_cast<TypeMapEntry*>(&module.managed_to_java[i]);
		cur->from = reinterpret_cast<char*>(managed_pos);

		memcpy (&idx, managed_pos + header.managed_name_width, sizeof (idx));
		cur->to = reinterpret_cast<char*>(java_start + (java_entry_size * idx));
		managed_pos += managed_entry_size;
	}

	LOG_FUNC_LEAVE ();
	return true;
}

bool
EmbeddedAssemblies::typemap_load_file (int dir_fd, const char *dir_path, const char *file_path, TypeMap &module) noexcept
{
	LOG_FUNC_ENTER ();
	log_debug (LOG_ASSEMBLY, "typemap: loading TypeMap file '%s/%s'", dir_path, file_path);

	bool ret = true;
	BinaryTypeMapHeader header;
	size_t file_size;
	int fd = -1;

	module.java_to_managed = nullptr;
	module.managed_to_java = nullptr;

	if (!typemap_read_header (dir_fd, "TypeMap", dir_path, file_path, MODULE_MAGIC_NAMES, header, file_size, fd)) {
		ret = false;
		goto cleanup;
	}

	ret = typemap_load_file (header, dir_path, file_path, fd, module);

  cleanup:
	if (fd >= 0)
		close (fd);

	if (!ret) {
		delete[] module.java_to_managed;
		module.java_to_managed = nullptr;
		delete[] module.managed_to_java;
		module.managed_to_java = nullptr;
	}

	LOG_FUNC_LEAVE ();
	return ret;
}

void
EmbeddedAssemblies::try_load_typemaps_from_directory (const char *path) noexcept
{
	LOG_FUNC_ENTER ();

	if (!application_config.instant_run_enabled) {
		log_info (LOG_ASSEMBLY, "typemap: instant run disabled, not loading type maps from storage");

		LOG_FUNC_LEAVE ();
		return;
	}

	std::unique_ptr<char> dir_path {Util::path_combine (path, "typemaps")};
	monodroid_dir_t *dir;
	if ((dir = Util::monodroid_opendir (dir_path.get ())) == nullptr) {
		log_warn (LOG_ASSEMBLY, "typemap: could not open directory: `%s`", dir_path.get ());

		LOG_FUNC_LEAVE ();
		return;
	}

	int dir_fd;
#if __ANDROID_API__ < 21
	dir_fd = -1;
#else
	dir_fd = dirfd (dir);
#endif

	constexpr char index_name[] = "typemap.index";

	// The pointer must be stored here because, after index is loaded, module.assembly_name points into the index data
	// and must be valid until after the actual module file is loaded.
	std::unique_ptr<uint8_t[]> index_data = typemap_load_index (dir_fd, dir_path.get (), index_name);
	if (!index_data) {
		log_fatal (LOG_ASSEMBLY, "typemap: unable to load TypeMap data index from '%s/%s'", dir_path.get (), index_name);
		exit (FATAL_EXIT_NO_ASSEMBLIES); // TODO: use a new error code here
	}

	for (size_t i = 0; i < type_map_count; i++) {
		TypeMap *module = &type_maps[i];
		if (!typemap_load_file (dir_fd, dir_path.get (), module->assembly_name, *module)) {
			continue;
		}
	}

	Util::monodroid_closedir (dir);

	LOG_FUNC_LEAVE ();
}
#endif

size_t
EmbeddedAssemblies::register_from (const char *apk_file, monodroid_should_register should_register) noexcept
{
	LOG_FUNC_ENTER ();

	size_t prev  = number_of_found_assemblies;

	gather_bundled_assemblies_from_apk (apk_file, should_register);

	log_info (LOG_ASSEMBLY, "Package '%s' contains %i assemblies", apk_file, number_of_found_assemblies - prev);

	LOG_FUNC_LEAVE ();
	return number_of_found_assemblies;
}
