#include <array>
#include <cerrno>
#include <cctype>
#include <vector>
#include <libgen.h>

#include <mono/metadata/assembly.h>

#include "embedded-assemblies.hh"
#include "globals.hh"
#include "strings.hh"
#include "xamarin-app.hh"
#include "xxhash.hh"

using namespace xamarin::android::internal;

using read_count_type = size_t;

force_inline bool
EmbeddedAssemblies::zip_load_entry_common (size_t entry_index, std::vector<uint8_t> const& buf, dynamic_local_string<SENSIBLE_PATH_MAX> &entry_name, ZipEntryLoadState &state) noexcept
{
	entry_name.clear ();

	bool result = zip_read_entry_info (buf, entry_name, state);

	log_debug (LOG_ASSEMBLY, "%s entry: %s", state.file_name, entry_name.get () == nullptr ? "unknown" : entry_name.get ());
	if (!result || entry_name.empty ()) {
		log_fatal (LOG_ASSEMBLY, "Failed to read Central Directory info for entry %u in APK file %s", entry_index, state.file_name);
		Helpers::abort_application ();
	}

	if (!zip_adjust_data_offset (state.file_fd, state)) {
		log_fatal (LOG_ASSEMBLY, "Failed to adjust data start offset for entry %u in APK file %s", entry_index, state.file_name);
		Helpers::abort_application ();
	}

	log_debug (LOG_ASSEMBLY, "    ZIP: local header offset: %u; data offset: %u; file size: %u", state.local_header_offset, state.data_offset, state.file_size);
	if (state.compression_method != 0) {
		return false;
	}

	if (entry_name.get ()[0] != state.prefix[0] || entry_name.length () < state.prefix_len || memcmp (state.prefix, entry_name.get (), state.prefix_len) != 0) {
		// state.prefix and apk_lib_prefix can point to the same location, see get_assemblies_prefix_and_length()
		// In such instance we short-circuit and avoid a couple of comparisons below.
		if (state.prefix == apk_lib_prefix.data ()) {
			return false;
		}

		if (entry_name.get ()[0] != apk_lib_prefix[0] || memcmp (apk_lib_prefix.data (), entry_name.get (), apk_lib_prefix.size () - 1) != 0) {
			return false;
		}
	}

	if (application_config.have_runtime_config_blob && !runtime_config_blob_found) {
		if (Util::ends_with (entry_name, SharedConstants::RUNTIME_CONFIG_BLOB_NAME)) {
			runtime_config_blob_found = true;
			runtime_config_blob_mmap = md_mmap_apk_file (state.file_fd, state.data_offset, state.file_size, entry_name.get ());
			return false;
		}
	}

	// assemblies must be 16-byte or 4-byte aligned, or Bad Things happen
	if (((state.data_offset & 0xf) != 0) || ((state.data_offset & 0x3) != 0)) {
		log_fatal (LOG_ASSEMBLY, "Assembly '%s' is located at bad offset %lu within the .apk", entry_name.get (), state.data_offset);
		log_fatal (LOG_ASSEMBLY, "You MUST run `zipalign` on %s to align it on 4 or 16 bytes ", strrchr (state.file_name, '/') + 1);
		Helpers::abort_application ();
	}

	return true;
}

void
EmbeddedAssemblies::store_individual_assembly_data (dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name, ZipEntryLoadState const& state, [[maybe_unused]] monodroid_should_register should_register) noexcept
{
#if defined (DEBUG)
	const char *last_slash = Util::find_last (entry_name, '/');
	bool entry_is_overridden = last_slash == nullptr ? false : !should_register (last_slash + 1);
#else
	constexpr bool entry_is_overridden = false;
#endif

	if (register_debug_symbols && !entry_is_overridden && Util::ends_with (entry_name, SharedConstants::PDB_EXTENSION)) {
		if (bundled_debug_data == nullptr) {
			bundled_debug_data = new std::vector<XamarinAndroidBundledAssembly> ();
			bundled_debug_data->reserve (application_config.number_of_assemblies_in_apk);
		}

		bundled_debug_data->emplace_back ();
		set_debug_entry_data (bundled_debug_data->back (), state, entry_name);
		return;
	}

	if (!Util::ends_with (entry_name, SharedConstants::DLL_EXTENSION)) {
		return;
	}

#if defined (DEBUG)
	if (entry_is_overridden) {
		return;
	}
#endif

	if (bundled_assembly_index >= application_config.number_of_assemblies_in_apk || state.bundled_assemblies_slow_path) [[unlikely]] {
		if (!state.bundled_assemblies_slow_path && bundled_assembly_index == application_config.number_of_assemblies_in_apk) {
			log_warn (LOG_ASSEMBLY, "Number of assemblies stored at build time (%u) was incorrect, switching to slow bundling path.", application_config.number_of_assemblies_in_apk);
		}

		if (extra_bundled_assemblies == nullptr) {
			extra_bundled_assemblies = new std::vector<XamarinAndroidBundledAssembly> ();
		}

		extra_bundled_assemblies->emplace_back ();
		// <true> means we need to allocate memory to store the entry name, only the entries pre-allocated during
		// build have valid pointer to the name storage area
		set_entry_data<true> (extra_bundled_assemblies->back (), state, entry_name);
		return;
	}

	log_debug (LOG_ASSEMBLY, "Setting bundled assembly entry data at index %zu", bundled_assembly_index);
	set_assembly_entry_data (bundled_assemblies [bundled_assembly_index], state, entry_name);
	log_debug (LOG_ASSEMBLY, "[%zu] data set: name == '%s'; file_name == '%s'", bundled_assembly_index, bundled_assemblies [bundled_assembly_index].name, bundled_assemblies [bundled_assembly_index].file_name);
	bundled_assembly_index++;
	number_of_found_assemblies = bundled_assembly_index;
	have_and_want_debug_symbols = register_debug_symbols && bundled_debug_data != nullptr;
}

force_inline void
EmbeddedAssemblies::zip_load_individual_assembly_entries (std::vector<uint8_t> const& buf, uint32_t num_entries, [[maybe_unused]] monodroid_should_register should_register, ZipEntryLoadState &state) noexcept
{
	// TODO: do away with all the string manipulation here. Replace it with generating xxhash for the entry name
	dynamic_local_string<SENSIBLE_PATH_MAX> entry_name;
	configure_state_for_individual_assembly_load (state);

	// clang-tidy claims we have a leak in the loop:
	//
	//   Potential leak of memory pointed to by 'assembly_name'
	//
	// This is because we allocate `assembly_name` for a `.config` file, pass it to Mono and we don't free the value.
	// However, clang-tidy can't know that the value is owned by Mono and we must not free it, thus the suppression.
	//
	// NOLINTNEXTLINE(clang-analyzer-unix.Malloc)
	for (size_t i = 0; i < num_entries; i++) {
		bool interesting_entry = zip_load_entry_common (i, buf, entry_name, state);
		if (!interesting_entry) {
			continue;
		}

		if (entry_name[state.prefix_len + SharedConstants::REGULAR_ASSEMBLY_MARKER_INDEX] == SharedConstants::REGULAR_ASSEMBLY_MARKER_CHAR) {
			unmangle_name<UnmangleRegularAssembly> (entry_name, state.prefix_len);
		} else if (entry_name[state.prefix_len + SharedConstants::SATELLITE_ASSEMBLY_MARKER_INDEX] == SharedConstants::SATELLITE_ASSEMBLY_MARKER_CHAR) {
			unmangle_name<UnmangleSatelliteAssembly> (entry_name, state.prefix_len);
		} else {
			continue; // Can't be an assembly, the name's not mangled
		}

		store_individual_assembly_data (entry_name, state, should_register);
	}
}

inline void
EmbeddedAssemblies::map_assembly_store (dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name, ZipEntryLoadState &state) noexcept
{
	if (number_of_mapped_assembly_stores > number_of_assembly_store_files) {
		log_fatal (LOG_ASSEMBLY, "Too many assembly stores. Expected at most %u", number_of_assembly_store_files);
		Helpers::abort_application ();
	}

	int fd;
	bool close_fd;
	if (!AndroidSystem::is_embedded_dso_mode_enabled ()) {
		log_debug (LOG_ASSEMBLY, "Mapping assembly blob file from filesystem");
		close_fd = true;

		// state.file_fd refers to the directory where our files live
		auto temp_fd = Util::open_file_ro_at (state.file_fd, entry_name.get ());
		if (!temp_fd) {
			return;
		}
		fd = temp_fd.value ();
	} else {
		fd = state.file_fd;
		close_fd = false;
	}

	md_mmap_info assembly_store_map = md_mmap_apk_file (fd, state.data_offset, state.file_size, entry_name.get ());
	if (close_fd) {
		close (fd);
	}
	auto header = static_cast<AssemblyStoreHeader*>(assembly_store_map.area);

	if (header->magic != ASSEMBLY_STORE_MAGIC) {
		log_fatal (LOG_ASSEMBLY, "Assembly store '%s' is not a valid .NET for Android assembly store file", entry_name.get ());
		Helpers::abort_application ();
	}

	if (header->version != ASSEMBLY_STORE_FORMAT_VERSION) {
		log_fatal (LOG_ASSEMBLY, "Assembly store '%s' uses format version 0x%x, instead of the expected 0x%x", entry_name.get (), header->version, ASSEMBLY_STORE_FORMAT_VERSION);
		Helpers::abort_application ();
	}

	constexpr size_t header_size = sizeof(AssemblyStoreHeader);

	assembly_store.data_start = static_cast<uint8_t*>(assembly_store_map.area);
	assembly_store.assembly_count = header->entry_count;
	assembly_store.index_entry_count = header->index_entry_count;
	assembly_store.assemblies = reinterpret_cast<AssemblyStoreEntryDescriptor*>(assembly_store.data_start + header_size + header->index_size);
	assembly_store_hashes = reinterpret_cast<AssemblyStoreIndexEntry*>(assembly_store.data_start + header_size);

	number_of_found_assemblies += assembly_store.assembly_count;
	number_of_mapped_assembly_stores++;
	have_and_want_debug_symbols = register_debug_symbols;
}

force_inline void
EmbeddedAssemblies::zip_load_assembly_store_entries (std::vector<uint8_t> const& buf, uint32_t num_entries, ZipEntryLoadState &state) noexcept
{
	if (all_required_zip_entries_found ()) {
		return;
	}

	dynamic_local_string<SENSIBLE_PATH_MAX> entry_name;
	bool assembly_store_found = false;

	log_debug (LOG_ASSEMBLY, "Looking for assembly stores in APK ('%s)", assembly_store_file_path.data ());
	for (size_t i = 0; i < num_entries; i++) {
		if (all_required_zip_entries_found ()) {
			need_to_scan_more_apks = false;
			break;
		}

		bool interesting_entry = zip_load_entry_common (i, buf, entry_name, state);
		if (!interesting_entry) {
			continue;
		}

		if (!assembly_store_found && Util::ends_with (entry_name, assembly_store_file_path)) {
			assembly_store_found = true;
			map_assembly_store (entry_name, state);
			continue;
		}

		if (number_of_zip_dso_entries >= application_config.number_of_shared_libraries) {
			continue;
		}

		// Since it's not an assembly store, it's a shared library most likely and it is long enough for us not to have
		// to check the length
		if (Util::ends_with (entry_name, dso_suffix)) {
			constexpr size_t apk_lib_prefix_len = apk_lib_prefix.size () - 1;

			const char *const name = entry_name.get () + apk_lib_prefix_len;
			DSOApkEntry *apk_entry = reinterpret_cast<DSOApkEntry*>(reinterpret_cast<uint8_t*>(dso_apk_entries) + (sizeof(DSOApkEntry) * number_of_zip_dso_entries));

			apk_entry->name_hash = xxhash::hash (name, entry_name.length () - apk_lib_prefix_len);
			apk_entry->offset = state.data_offset;
			apk_entry->fd = state.file_fd;

			log_debug (LOG_ASSEMBLY, "Found a shared library entry %s (index: %u; name: %s; hash: 0x%zx; apk offset: %u)", entry_name.get (), number_of_zip_dso_entries, name, apk_entry->name_hash, apk_entry->offset);
			number_of_zip_dso_entries++;
		}
	}
}

void
EmbeddedAssemblies::zip_load_entries (int fd, const char *apk_name, [[maybe_unused]] monodroid_should_register should_register)
{
	uint32_t cd_offset;
	uint32_t cd_size;
	uint16_t cd_entries;

	if (!zip_read_cd_info (fd, cd_offset, cd_size, cd_entries)) {
		log_fatal (LOG_ASSEMBLY,  "Failed to read the EOCD record from APK file %s", apk_name);
		Helpers::abort_application ();
	}
#ifdef DEBUG
	log_info (LOG_ASSEMBLY, "Central directory offset: %u", cd_offset);
	log_info (LOG_ASSEMBLY, "Central directory size: %u", cd_size);
	log_info (LOG_ASSEMBLY, "Central directory entries: %u", cd_entries);
#endif
	off_t retval = ::lseek (fd, static_cast<off_t>(cd_offset), SEEK_SET);
	if (retval < 0) {
		log_fatal (LOG_ASSEMBLY, "Failed to seek to central directory position in the APK file %s. %s (result: %d; errno: %d)", apk_name, std::strerror (errno), retval, errno);
		Helpers::abort_application ();
	}

	std::vector<uint8_t>  buf (cd_size);
	const auto [prefix, prefix_len] = get_assemblies_prefix_and_length ();
	ZipEntryLoadState state {
		.file_fd             = fd,
		.file_name           = apk_name,
		.prefix              = prefix,
		.prefix_len          = prefix_len,
		.buf_offset          = 0,
		.compression_method  = 0,
		.local_header_offset = 0,
		.data_offset         = 0,
		.file_size           = 0,
	};

	ssize_t nread = read (fd, buf.data (), static_cast<read_count_type>(buf.size ()));
	if (static_cast<size_t>(nread) != cd_size) {
		log_fatal (LOG_ASSEMBLY, "Failed to read Central Directory from the APK archive %s. %s (nread: %d; errno: %d)", apk_name, std::strerror (errno), nread, errno);
		Helpers::abort_application ();
	}

	if (application_config.have_assembly_store) {
		zip_load_assembly_store_entries (buf, cd_entries, state);
	} else {
		zip_load_individual_assembly_entries (buf, cd_entries, should_register, state);
	}
}

template<bool NeedsNameAlloc>
force_inline void
EmbeddedAssemblies::set_entry_data (XamarinAndroidBundledAssembly &entry, ZipEntryLoadState const& state, dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name) noexcept
{
	entry.file_fd = state.file_fd;
	if constexpr (NeedsNameAlloc) {
		entry.name = Util::strdup_new (entry_name.get () + state.prefix_len);
		if (!AndroidSystem::is_embedded_dso_mode_enabled () && state.file_name != nullptr) {
			entry.file_name = Util::strdup_new (state.file_name);
		}
	} else {
		// entry.name is preallocated at build time here and is max_name_size + 1 bytes long, filled with 0s, thus we
		// don't need to append the terminating NUL even for strings of `max_name_size` characters
		strncpy (entry.name, entry_name.get () + state.prefix_len, state.max_assembly_name_size);
		if (!AndroidSystem::is_embedded_dso_mode_enabled () && state.file_name != nullptr) {
			strncpy (entry.file_name, state.file_name, state.max_assembly_file_name_size);
		}
	}
	entry.name_length = std::min (static_cast<uint32_t>(entry_name.length ()) - state.prefix_len, state.max_assembly_name_size);
	entry.data_offset = state.data_offset;
	entry.data_size = state.file_size;

	log_debug (
		LOG_ASSEMBLY,
		"Set bundled assembly entry data. file name: '%s'; entry name: '%s'; data size: %u",
		entry.file_name, entry.name, entry.data_size
	);
}

force_inline void
EmbeddedAssemblies::set_assembly_entry_data (XamarinAndroidBundledAssembly &entry, ZipEntryLoadState const& state, dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name) noexcept
{
	set_entry_data<false> (entry, state, entry_name);
}

force_inline void
EmbeddedAssemblies::set_debug_entry_data (XamarinAndroidBundledAssembly &entry, ZipEntryLoadState const& state, dynamic_local_string<SENSIBLE_PATH_MAX> const& entry_name) noexcept
{
	set_entry_data<true> (entry, state, entry_name);
}

bool
EmbeddedAssemblies::zip_read_cd_info (int fd, uint32_t& cd_offset, uint32_t& cd_size, uint16_t& cd_entries)
{
	// The simplest case - no file comment
	off_t ret = ::lseek (fd, -ZIP_EOCD_LEN, SEEK_END);
	if (ret < 0) {
		log_error (LOG_ASSEMBLY, "Unable to seek into the APK to find ECOD: %s (ret: %d; errno: %d)", std::strerror (errno), ret, errno);
		return false;
	}

	std::array<uint8_t, ZIP_EOCD_LEN> eocd;
	ssize_t nread = ::read (fd, eocd.data (), static_cast<read_count_type>(eocd.size ()));
	if (nread < 0 || nread != eocd.size ()) {
		log_error (LOG_ASSEMBLY, "Failed to read EOCD from the APK: %s (nread: %d; errno: %d)", std::strerror (errno), nread, errno);
		return false;
	}

	size_t index = 0; // signature
	std::array<uint8_t, 4> signature;

	if (!zip_read_field (eocd, index, signature)) {
		log_error (LOG_ASSEMBLY, "Failed to read EOCD signature");
		return false;
	}

	if (memcmp (signature.data (), ZIP_EOCD_MAGIC.data (), signature.size ()) == 0) {
		return zip_extract_cd_info (eocd, cd_offset, cd_size, cd_entries);
	}

	// Most probably a ZIP with comment
	constexpr size_t alloc_size = 65535 + ZIP_EOCD_LEN; // 64k is the biggest comment size allowed
	ret = ::lseek (fd, static_cast<off_t>(-alloc_size), SEEK_END);
	if (ret < 0) {
		log_error (LOG_ASSEMBLY, "Unable to seek into the file to find ECOD before APK comment: %s (ret: %d; errno: %d)", std::strerror (errno), ret, errno);
		return false;
	}

	std::vector<uint8_t> buf (alloc_size);

	nread = ::read (fd, buf.data (), static_cast<read_count_type>(buf.size ()));

	if (nread < 0 || static_cast<size_t>(nread) != alloc_size) {
		log_error (LOG_ASSEMBLY, "Failed to read EOCD and comment from the APK: %s (nread: %d; errno: %d)", std::strerror (errno), nread, errno);
		return false;
	}

	// We scan from the end to save time
	bool found = false;
	const uint8_t* data = buf.data ();
	for (ssize_t i = static_cast<ssize_t>(alloc_size - (ZIP_EOCD_LEN + 2)); i >= 0; i--) {
		if (memcmp (data + i, ZIP_EOCD_MAGIC.data (), sizeof(ZIP_EOCD_MAGIC)) != 0)
			continue;

		found = true;
		memcpy (eocd.data (), data + i, ZIP_EOCD_LEN);
		break;
	}

	if (!found) {
		log_error (LOG_ASSEMBLY, "Unable to find EOCD in the APK (with comment)");
		return false;
	}

	return zip_extract_cd_info (eocd, cd_offset, cd_size, cd_entries);
}

bool
EmbeddedAssemblies::zip_adjust_data_offset (int fd, ZipEntryLoadState &state)
{
	static constexpr size_t LH_FILE_NAME_LENGTH_OFFSET   = 26;
	static constexpr size_t LH_EXTRA_LENGTH_OFFSET       = 28;

	off_t result = ::lseek (fd, static_cast<off_t>(state.local_header_offset), SEEK_SET);
	if (result < 0) {
		log_error (LOG_ASSEMBLY, "Failed to seek to archive entry local header at offset %u. %s (result: %d; errno: %d)", state.local_header_offset, result, errno);
		return false;
	}

	std::array<uint8_t, ZIP_LOCAL_LEN> local_header;
	std::array<uint8_t, 4> signature;

	ssize_t nread = ::read (fd, local_header.data (), local_header.size ());
	if (nread < 0 || nread != ZIP_LOCAL_LEN) {
		log_error (LOG_ASSEMBLY, "Failed to read local header at offset %u: %s (nread: %d; errno: %d)", state.local_header_offset, std::strerror (errno), nread, errno);
		return false;
	}

	size_t index = 0;
	if (!zip_read_field (local_header, index, signature)) {
		log_error (LOG_ASSEMBLY, "Failed to read Local Header entry signature at offset %u", state.local_header_offset);
		return false;
	}

	if (memcmp (signature.data (), ZIP_LOCAL_MAGIC.data (), signature.size ()) != 0) {
		log_error (LOG_ASSEMBLY, "Invalid Local Header entry signature at offset %u", state.local_header_offset);
		return false;
	}

	uint16_t file_name_length;
	index = LH_FILE_NAME_LENGTH_OFFSET;
	if (!zip_read_field (local_header, index, file_name_length)) {
		log_error (LOG_ASSEMBLY, "Failed to read Local Header 'file name length' field at offset %u", (state.local_header_offset + index));
		return false;
	}

	uint16_t extra_field_length;
	index = LH_EXTRA_LENGTH_OFFSET;
	if (!zip_read_field (local_header, index, extra_field_length)) {
		log_error (LOG_ASSEMBLY, "Failed to read Local Header 'extra field length' field at offset %u", (state.local_header_offset + index));
		return false;
	}

	state.data_offset = static_cast<uint32_t>(state.local_header_offset) + file_name_length + extra_field_length + local_header.size ();

	return true;
}

template<size_t BufSize>
bool
EmbeddedAssemblies::zip_extract_cd_info (std::array<uint8_t, BufSize> const& buf, uint32_t& cd_offset, uint32_t& cd_size, uint16_t& cd_entries)
{
	constexpr size_t EOCD_TOTAL_ENTRIES_OFFSET = 10;
	constexpr size_t EOCD_CD_SIZE_OFFSET       = 12;
	constexpr size_t EOCD_CD_START_OFFSET      = 16;

	static_assert (BufSize >= ZIP_EOCD_LEN, "Buffer too short for EOCD");

	if (!zip_read_field (buf, EOCD_TOTAL_ENTRIES_OFFSET, cd_entries)) {
		log_error (LOG_ASSEMBLY, "Failed to read EOCD 'total number of entries' field");
		return false;
	}

	if (!zip_read_field (buf, EOCD_CD_START_OFFSET, cd_offset)) {
		log_error (LOG_ASSEMBLY, "Failed to read EOCD 'central directory size' field");
		return false;
	}

	if (!zip_read_field (buf, EOCD_CD_SIZE_OFFSET, cd_size)) {
		log_error (LOG_ASSEMBLY, "Failed to read EOCD 'central directory offset' field");
		return false;
	}

	return true;
}

template<class T>
force_inline bool
EmbeddedAssemblies::zip_ensure_valid_params (T const& buf, size_t index, size_t to_read) const noexcept
{
	if (index + to_read > buf.size ()) {
		log_error (LOG_ASSEMBLY, "Buffer too short to read %u bytes of data", to_read);
		return false;
	}

	return true;
}

template<ByteArrayContainer T>
bool
EmbeddedAssemblies::zip_read_field (T const& src, size_t source_index, uint16_t& dst) const noexcept
{
	if (!zip_ensure_valid_params (src, source_index, sizeof (dst))) {
		return false;
	}

	dst = static_cast<uint16_t>((src [source_index + 1] << 8) | src [source_index]);

	return true;
}

template<ByteArrayContainer T>
bool
EmbeddedAssemblies::zip_read_field (T const& src, size_t source_index, uint32_t& dst) const noexcept
{
	if (!zip_ensure_valid_params (src, source_index, sizeof (dst))) {
		return false;
	}

	dst =
		(static_cast<uint32_t> (src [source_index + 3]) << 24) |
		(static_cast<uint32_t> (src [source_index + 2]) << 16) |
		(static_cast<uint32_t> (src [source_index + 1]) << 8)  |
		(static_cast<uint32_t> (src [source_index + 0]));

	return true;
}

template<ByteArrayContainer T>
bool
EmbeddedAssemblies::zip_read_field (T const& src, size_t source_index, std::array<uint8_t, 4>& dst_sig) const noexcept
{
	if (!zip_ensure_valid_params (src, source_index, dst_sig.size ())) {
		return false;
	}

	memcpy (dst_sig.data (), src.data () + source_index, dst_sig.size ());
	return true;
}

template<ByteArrayContainer T>
bool
EmbeddedAssemblies::zip_read_field (T const& buf, size_t index, size_t count, dynamic_local_string<SENSIBLE_PATH_MAX>& characters) const noexcept
{
	if (!zip_ensure_valid_params (buf, index, count)) {
		return false;
	}

	characters.assign (reinterpret_cast<const char*>(buf.data () + index), count);
	return true;
}

bool
EmbeddedAssemblies::zip_read_entry_info (std::vector<uint8_t> const& buf, dynamic_local_string<SENSIBLE_PATH_MAX>& file_name, ZipEntryLoadState &state)
{
	constexpr size_t CD_COMPRESSION_METHOD_OFFSET = 10;
	constexpr size_t CD_UNCOMPRESSED_SIZE_OFFSET  = 24;
	constexpr size_t CD_FILENAME_LENGTH_OFFSET    = 28;
	constexpr size_t CD_EXTRA_LENGTH_OFFSET       = 30;
	constexpr size_t CD_LOCAL_HEADER_POS_OFFSET   = 42;
	constexpr size_t CD_COMMENT_LENGTH_OFFSET     = 32;

	size_t index = state.buf_offset;
	zip_ensure_valid_params (buf, index, ZIP_CENTRAL_LEN);

	std::array<uint8_t, 4> signature;
	if (!zip_read_field (buf, index, signature)) {
		log_error (LOG_ASSEMBLY, "Failed to read Central Directory entry signature");
		return false;
	}

	if (memcmp (signature.data (), ZIP_CENTRAL_MAGIC.data (), signature.size ()) != 0) {
		log_error (LOG_ASSEMBLY, "Invalid Central Directory entry signature");
		return false;
	}

	index = state.buf_offset + CD_COMPRESSION_METHOD_OFFSET;
	if (!zip_read_field (buf, index, state.compression_method)) {
		log_error (LOG_ASSEMBLY, "Failed to read Central Directory entry 'compression method' field");
		return false;
	}

	index = state.buf_offset + CD_UNCOMPRESSED_SIZE_OFFSET;;
	if (!zip_read_field (buf, index, state.file_size)) {
		log_error (LOG_ASSEMBLY, "Failed to read Central Directory entry 'uncompressed size' field");
		return false;
	}

	uint16_t file_name_length;
	index = state.buf_offset + CD_FILENAME_LENGTH_OFFSET;
	if (!zip_read_field (buf, index, file_name_length)) {
		log_error (LOG_ASSEMBLY, "Failed to read Central Directory entry 'file name length' field");
		return false;
	}

	uint16_t extra_field_length;
	index = state.buf_offset + CD_EXTRA_LENGTH_OFFSET;
	if (!zip_read_field (buf, index, extra_field_length)) {
		log_error (LOG_ASSEMBLY, "Failed to read Central Directory entry 'extra field length' field");
		return false;
	}

	uint16_t comment_length;
	index = state.buf_offset + CD_COMMENT_LENGTH_OFFSET;
	if (!zip_read_field (buf, index, comment_length)) {
		log_error (LOG_ASSEMBLY, "Failed to read Central Directory entry 'file comment length' field");
		return false;
	}

	index = state.buf_offset + CD_LOCAL_HEADER_POS_OFFSET;
	if (!zip_read_field (buf, index, state.local_header_offset)) {
		log_error (LOG_ASSEMBLY, "Failed to read Central Directory entry 'relative offset of local header' field");
		return false;
	}
	index += sizeof(state.local_header_offset);

	if (file_name_length == 0) {
		file_name.clear ();
	} else if (!zip_read_field (buf, index, file_name_length, file_name)) {
		log_error (LOG_ASSEMBLY, "Failed to read Central Directory entry 'file name' field");
		return false;
	}

	state.buf_offset += ZIP_CENTRAL_LEN + file_name_length + extra_field_length + comment_length;
	return true;
}
