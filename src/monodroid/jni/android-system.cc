#include <array>
#include <cstring>
#include <cstdlib>
#include <cerrno>
#include <cctype>
#include <fcntl.h>
#include <type_traits>

#if defined (WINDOWS)
#include <windef.h>
#include <winbase.h>
#include <shlobj.h>
#include <objbase.h>
#include <knownfolders.h>
#include <shlwapi.h>
#endif

#include <mono/metadata/object.h>

#include "android-system.hh"
#include "debug.hh"
#include "monodroid.h"
#include "monodroid-glue-internal.hh"
#include "jni-wrappers.hh"
#include "xamarin-app.hh"
#include "cpp-util.hh"
#include "java-interop-dlfcn.h"
#include "java-interop.h"
#include "gsl.hh"

#if defined (DEBUG) || !defined (ANDROID)
namespace xamarin::android::internal {
	struct BundledProperty {
		char     *name;
		char     *value;
		size_t    value_len;
		struct BundledProperty *next;
	};
}
#endif // DEBUG || !ANDROID

using namespace microsoft::java_interop;
using namespace xamarin::android;
using namespace xamarin::android::internal;

#if defined (DEBUG) || !defined (ANDROID)
BundledProperty *AndroidSystem::bundled_properties = nullptr;
#endif // DEBUG || !ANDROID

#if defined (WINDOWS)
std::mutex AndroidSystem::readdir_mutex;
char *AndroidSystem::libmonoandroid_directory_path = nullptr;
#endif

#if defined (DEBUG) || !defined (ANDROID)
BundledProperty*
AndroidSystem::lookup_system_property (const char *name) noexcept
{
	LOG_FUNC_ENTER ();

	for (BundledProperty *p = bundled_properties; p != nullptr; p = p->next) {
		if (strcmp (p->name, name) == 0) {
			LOG_FUNC_LEAVE ();
			return p;
		}
	}

	LOG_FUNC_LEAVE ();
	return nullptr;
}
#endif // DEBUG || !ANDROID

const char*
AndroidSystem::lookup_system_property (const char *name, size_t &value_len) noexcept
{
	LOG_FUNC_ENTER ();

	value_len = 0;
#if defined (DEBUG) || !defined (ANDROID)
	BundledProperty *p = lookup_system_property (name);
	if (p != nullptr) {
		value_len = p->value_len;

		LOG_FUNC_LEAVE ();
		return p->name;
	}
#endif // DEBUG || !ANDROID

	if (application_config.system_property_count == 0) {
		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	if (application_config.system_property_count % 2 != 0) {
		log_warn (LOG_DEFAULT, "Corrupted environment variable array: does not contain an even number of entries (%u)", application_config.environment_variable_count);

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	const char *prop_name = nullptr;
	const char *prop_value = nullptr;
	for (size_t i = 0; i < application_config.system_property_count; i += 2) {
		prop_name = app_system_properties[i];
		if (prop_name == nullptr || *prop_name == '\0')
			continue;

		if (strcmp (prop_name, name) == 0) {
			prop_value = app_system_properties [i + 1];
			if (prop_value == nullptr || *prop_value == '\0') {
				value_len = 0;

				LOG_FUNC_LEAVE ();
				return "";
			}

			value_len = strlen (prop_value);

			LOG_FUNC_LEAVE ();
			return prop_value;
		}
	}

	LOG_FUNC_LEAVE ();
	return nullptr;
}

#if defined (DEBUG) || !defined (ANDROID)
void
AndroidSystem::add_system_property (const char *name, const char *value) noexcept
{
	LOG_FUNC_ENTER ();

	BundledProperty* p = lookup_system_property (name);
	if (p != nullptr) {
		char *n = value != nullptr ? strdup (value) : nullptr;
		if (n == nullptr) {
			LOG_FUNC_LEAVE ();
			return;
		}
		free (p->value);
		p->value      = n;
		p->value_len  = strlen (p->value);

		LOG_FUNC_LEAVE ();
		return;
	}

	size_t name_len  = strlen (name);
	size_t alloc_size = ADD_WITH_OVERFLOW_CHECK (size_t, sizeof (BundledProperty), name_len + 1);
	p = reinterpret_cast<BundledProperty*> (malloc (alloc_size));
	if (p == nullptr) {
		LOG_FUNC_LEAVE ();
		return;
	}

	p->name = ((char*) p) + sizeof (struct BundledProperty);
	memcpy (p->name, name, name_len);
	p->name [name_len] = '\0';

	if (value == nullptr) {
		p->value = nullptr;
		p->value_len = 0;
	} else {
		p->value = strdup (value);
		p->value_len = strlen (value);
	}

	p->next             = bundled_properties;
	bundled_properties  = p;

	LOG_FUNC_LEAVE ();
}
#endif // DEBUG || !ANDROID

#ifndef ANDROID
void
AndroidSystem::monodroid_strreplace (char *buffer, char old_char, char new_char) noexcept
{
	LOG_FUNC_ENTER ();

	if (buffer == nullptr) {
		LOG_FUNC_LEAVE ();
		return;
	}
	while (*buffer != '\0') {
		if (*buffer == old_char)
			*buffer = new_char;
		buffer++;
	}

	LOG_FUNC_LEAVE ();
}

int
AndroidSystem::_monodroid__system_property_get (const char *name, char *sp_value, size_t sp_value_len) noexcept
{
	LOG_FUNC_ENTER ();

	if (!name || !sp_value) {
		LOG_FUNC_LEAVE ();
		return -1;
	}

	char *env_name = Util::monodroid_strdup_printf ("__XA_%s", name);
	monodroid_strreplace (env_name, '.', '_');
	char *env_value = getenv (env_name);
	free (env_name);

	size_t env_value_len = env_value ? strlen (env_value) : 0;
	if (env_value_len == 0) {
		sp_value[0] = '\0';

		LOG_FUNC_LEAVE ();
		return 0;
	}

	if (env_value_len >= sp_value_len)
		log_warn (LOG_DEFAULT, "System property buffer size too small by %u bytes", env_value_len == sp_value_len ? 1 : env_value_len - sp_value_len);

	//
	// sp_value_len includes the terminating nul, avoid a mingw g++ warning about string truncation
	// by making the amount of data copied one less than the indicated length. The warning reported
	// is:
	//
	//  In function ‘int xamarin::android::internal::AndroidSystem::_monodroid__system_property_get(const char*, char*, size_t)’,
	//    inlined from ‘int xamarin::android::internal::AndroidSystem::monodroid_get_system_property(const char*, char**)’ at ../../../jni/android-system.cc:243:44:
	//    ../../../jni/android-system.cc(206,10): warning G20816D19: ‘char* strncpy(char*, const char*, size_t)’ specified bound 93 equals destination size [-Wstringop-truncation] [/home/grendel/vc/xamarin/xamarin-android-worktrees/code-quality-improvements/src/monodroid/monodroid.csproj]
	//    strncpy (sp_value, env_value, sp_value_len);
	//
	strncpy (sp_value, env_value, sp_value_len - 2);
	sp_value[sp_value_len - 1] = '\0';

	LOG_FUNC_LEAVE ();
	return static_cast<int>(strlen (sp_value));
}
#else
int
AndroidSystem::_monodroid__system_property_get (const char *name, char *sp_value, const size_t sp_value_len) noexcept
{
	LOG_FUNC_ENTER ();

	if (name == nullptr || sp_value == nullptr) {
		LOG_FUNC_LEAVE ();
		return -1;
	}

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> buf;
	bool use_buf = sp_value_len < PROPERTY_VALUE_BUFFER_LEN;
	if (use_buf) {
		log_warn (LOG_DEFAULT, "Buffer to store system property may be too small, will copy only %u bytes", sp_value_len);
	}

	int len = __system_property_get (name, use_buf ? buf.get () : sp_value);
	if (use_buf) {
		strncpy (sp_value, buf.get (), sp_value_len);
		sp_value [sp_value_len] = '\0';
	}

	LOG_FUNC_LEAVE ();
	return len;
}
#endif

#if defined (ANDROID)
// Disable inlining for MinGW and G++ < 11, which (at least on our CI bots) remove the method causing linking errors later on
inline
#endif
int
AndroidSystem::fetch_system_property (const char *name, dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN>& value) noexcept
{
	LOG_FUNC_ENTER ();

	int len = _monodroid__system_property_get (name, value.get (), value.size ());
	if (len > 0) {
		// Clumsy, but if we want direct writes to be fast, this is the price we pay
		value.set_length_after_direct_write (static_cast<size_t>(len));

		LOG_FUNC_LEAVE ();
		return len;
	}

	size_t plen = 0;
	const char *v = lookup_system_property (name, plen);
	if (v == nullptr) {
		LOG_FUNC_LEAVE ();
		return len;
	}

	value.assign (v, plen);

	LOG_FUNC_LEAVE ();
	return ADD_WITH_OVERFLOW_CHECK (int, plen, 0);
}

int
AndroidSystem::monodroid_get_system_property (const char *name, gsl::owner<char**> value) noexcept
{
	LOG_FUNC_ENTER ();

	if (value != nullptr)
		*value = nullptr;

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> sp_value;
	const char *pvalue = sp_value.get ();
	int len = _monodroid__system_property_get (name, sp_value.get (), sp_value.length ());

	if (len <= 0) {
		size_t plen = 0;
		const char *v = lookup_system_property (name, plen);
		if (v != nullptr) {
			pvalue  = v;
			len     = static_cast<int> (plen);
		}
	}

	if (len >= 0 && value != nullptr) {
		auto alloc_size = ADD_WITH_OVERFLOW_CHECK (size_t, static_cast<size_t>(len), 1);
		*value = new char [alloc_size];
		if (*value == nullptr) {
			LOG_FUNC_LEAVE ();
			return -len;
		}
		if (len > 0)
			memcpy (*value, pvalue, static_cast<size_t>(len));
		(*value)[len] = '\0';
	}

	LOG_FUNC_LEAVE ();
	return len;
}

bool
AndroidSystem::monodroid_system_property_exists_impl (const char *name) noexcept
{
	LOG_FUNC_ENTER ();

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> value;

	LOG_FUNC_LEAVE ();
	return fetch_system_property (name, value) >= 0;
}

#if defined (DEBUG) || !defined (ANDROID)
size_t
AndroidSystem::_monodroid_get_system_property_from_file (const char *path, char **value) noexcept
{
	LOG_FUNC_ENTER ();

	if (value != nullptr)
		*value = nullptr;

	FILE* fp = Util::monodroid_fopen (path, "r");
	if (fp == nullptr) {
		LOG_FUNC_LEAVE ();
		return 0;
	}

	struct stat fileStat;
	if (fstat (fileno (fp), &fileStat) < 0) {
		fclose (fp);

		LOG_FUNC_LEAVE ();
		return 0;
	}

	size_t file_size = static_cast<size_t>(fileStat.st_size);
	if (value == nullptr) {
		fclose (fp);

		LOG_FUNC_LEAVE ();
		return file_size + 1;
	}

	size_t alloc_size = ADD_WITH_OVERFLOW_CHECK (size_t, file_size, 1);
	*value = new char[alloc_size];

	size_t len = fread (*value, 1, file_size, fp);
	fclose (fp);
	for (size_t i = 0; i < file_size + 1; ++i) {
		if ((*value) [i] != '\n' && (*value) [i] != '\r')
			continue;
		(*value) [i] = 0;
		break;
	}

	LOG_FUNC_LEAVE ();
	return len;
}
#endif

size_t
AndroidSystem::monodroid_get_system_property_from_overrides ([[maybe_unused]] const char *name, [[maybe_unused]] char ** value) noexcept
{
	LOG_FUNC_ENTER ();

#if defined (DEBUG) || !defined (ANDROID)
	for (auto const& dir : override_dirs ()) {
		if (dir == nullptr) {
			continue;
		}

		std::unique_ptr<char[]> override_file {Util::path_combine (dir, name)};
		log_info (LOG_DEFAULT, "Trying to get property from %s", override_file.get ());

		size_t result = _monodroid_get_system_property_from_file (override_file.get (), value);
		if (result == 0 || value == nullptr || (*value) == nullptr || **value == '\0') {
			continue;
		}
		log_info (LOG_DEFAULT, "Property '%s' from  %s has value '%s'.", name, dir, *value);

		LOG_FUNC_LEAVE ();
		return result;
	}
#endif

	LOG_FUNC_LEAVE ();
	return 0;
}

void
AndroidSystem::create_update_dir (char *override_dir) noexcept
{
	LOG_FUNC_ENTER ();

#if defined(RELEASE)
	/*
	 * Don't create .__override__ on Release builds, because Google requires
	 * that pre-loaded apps not create world-writable directories.
	 *
	 * However, if any logging is enabled (which should _not_ happen with
	 * pre-loaded apps!), we need the .__override__ directory...
	 */
	if (log_categories == 0 && monodroid_system_property_exists (Debug::DEBUG_MONO_PROFILE_PROPERTY)) {
		LOG_FUNC_LEAVE ();
		return;
	}
#endif

	set_override_dir (0, override_dir);
	Util::create_public_directory (override_dir);
	log_warn (LOG_DEFAULT, "Creating public update directory: `%s`", override_dir);

	LOG_FUNC_LEAVE ();
}

bool
AndroidSystem::get_full_dso_path (const char *base_dir, const char *dso_path, dynamic_local_string<SENSIBLE_PATH_MAX>& path) noexcept
{
	LOG_FUNC_ENTER ();

	if (dso_path == nullptr || *dso_path == '\0') {
		LOG_FUNC_LEAVE ();
		return false;
	}

	if (base_dir == nullptr || Util::is_path_rooted (dso_path)) {
		path.assign_c (dso_path); // Absolute path or no base path, can't do much with it
		LOG_FUNC_LEAVE ();
		return true;
	}

	path.assign_c (base_dir)
		.append (MONODROID_PATH_SEPARATOR)
		.append_c (dso_path);

	LOG_FUNC_LEAVE ();
	return true;
}

void*
AndroidSystem::load_dso (const char *path, unsigned int dl_flags, bool skip_exists_check) noexcept
{
	LOG_FUNC_ENTER ();

	if (path == nullptr || *path == '\0') {
		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	log_info (LOG_ASSEMBLY, "Trying to load shared library '%s'", path);
	if (!skip_exists_check && !is_embedded_dso_mode_enabled () && !Util::file_exists (path)) {
		log_info (LOG_ASSEMBLY, "Shared library '%s' not found", path);

		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	char *error = nullptr;
	void *handle = java_interop_lib_load (path, dl_flags, &error);
	if (handle == nullptr && Util::should_log (LOG_ASSEMBLY))
		log_info_nocheck (LOG_ASSEMBLY, "Failed to load shared library '%s'. %s", path, error);
	java_interop_free (error);

	LOG_FUNC_LEAVE ();
	return handle;
}

force_inline void*
AndroidSystem::load_dso_from_directory (const char *directory, const char *dso_name, unsigned int dl_flags) noexcept
{
	LOG_FUNC_ENTER ();

	dynamic_local_string<SENSIBLE_PATH_MAX> full_path;
	if (!get_full_dso_path (directory, dso_name, full_path)) {
		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	LOG_FUNC_LEAVE ();
	return load_dso (full_path.get (), dl_flags, false);
}

void*
AndroidSystem::load_dso_from_app_lib_dirs (const char *name, unsigned int dl_flags) noexcept
{
	LOG_FUNC_ENTER ();
	LOG_FUNC_LEAVE ();
	return load_dso_from_specified_dirs (app_lib_directories (), name, dl_flags);
}

void*
AndroidSystem::load_dso_from_override_dirs ([[maybe_unused]] const char *name, [[maybe_unused]] unsigned int dl_flags) noexcept
{
	LOG_FUNC_ENTER ();
	LOG_FUNC_LEAVE ();
#ifdef RELEASE
	return nullptr;
#else
	return load_dso_from_specified_dirs (override_dirs (), name, dl_flags);
#endif
}

void*
AndroidSystem::load_dso_from_any_directories (const char *name, unsigned int dl_flags) noexcept
{
	LOG_FUNC_ENTER ();

	void *handle = load_dso_from_override_dirs (name, dl_flags);
	if (handle == nullptr)
		handle = load_dso_from_app_lib_dirs (name, dl_flags);

	LOG_FUNC_LEAVE ();
	return handle;
}

bool
AndroidSystem::get_existing_dso_path_on_disk (const char *base_dir, const char *dso_name, dynamic_local_string<SENSIBLE_PATH_MAX>& path) noexcept
{
	LOG_FUNC_ENTER ();
	LOG_FUNC_LEAVE ();
	return get_full_dso_path (base_dir, dso_name, path) && Util::file_exists (path.get ());
}

bool
AndroidSystem::get_full_dso_path_on_disk (const char *dso_name, dynamic_local_string<SENSIBLE_PATH_MAX>& path) noexcept
{
	LOG_FUNC_ENTER ();

	if (is_embedded_dso_mode_enabled ()) {
		LOG_FUNC_LEAVE ();
		return false;
	}

#ifndef RELEASE
	for (auto const& dir : override_dirs ()) {
		if (dir == nullptr) {
			continue;
		}

		if (get_existing_dso_path_on_disk (dir, dso_name, path)) {
			LOG_FUNC_LEAVE ();
			return true;
		}
	}
#endif
	for (auto const& dir : app_lib_directories ()) {
		if (get_existing_dso_path_on_disk (dir, dso_name, path)) {
			LOG_FUNC_LEAVE ();
			return true;
		}
	}

	LOG_FUNC_LEAVE ();
	return false;
}

int
AndroidSystem::count_override_assemblies () noexcept
{
	LOG_FUNC_ENTER ();

	int c = 0;

	for (auto const dir_path : override_dirs ()) {
		if (dir_path == nullptr || !Util::directory_exists (dir_path))
			continue;

		monodroid_dir_t *dir = Util::monodroid_opendir (dir_path);
		if (dir == nullptr)
			continue;

		monodroid_dirent_t *e = nullptr;
		while ((e = readdir (dir)) != nullptr && e) {
			if (Util::monodroid_dirent_hasextension (e, ".dll"))
				++c;
		}
		Util::monodroid_closedir (dir);
	}

	LOG_FUNC_LEAVE ();
	return c;
}

long
AndroidSystem::get_max_gref_count_from_system () noexcept
{
	LOG_FUNC_ENTER ();

	constexpr long MAX_GREF_FOR_EMULATOR = 2000;
	constexpr long MAX_GREF_FOR_DEVICE = 51200;

	long max = running_in_emulator ? MAX_GREF_FOR_EMULATOR : MAX_GREF_FOR_DEVICE;

	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> override;
	if (monodroid_get_system_property (Debug::DEBUG_MONO_MAX_GREFC, override) > 0) {
		constexpr long KILOBYTES_MULTIPLIER = 1000;
		constexpr long MEGABYTES_MULTIPLIER = 1000000;
		constexpr long CONVERSION_BASE_DECIMAL = 10;

		char *e = nullptr;
		max       = strtol (override.get (), &e, CONVERSION_BASE_DECIMAL);
		switch (*e) {
			case 'k':
				e++;
				max *= KILOBYTES_MULTIPLIER;
				break;
			case 'm':
				e++;
				max *= MEGABYTES_MULTIPLIER;
				break;
		}
		if (max < 0)
			max = std::numeric_limits<int>::max ();
		if (*e) {
			log_warn (LOG_GC, "Unsupported '%s' value '%s'.", Debug::DEBUG_MONO_MAX_GREFC, override.get ());
		}
		log_warn (LOG_GC, "Overriding max JNI Global Reference count to %i", max);
	}

	LOG_FUNC_LEAVE ();
	return max;
}

long
AndroidSystem::get_gref_gc_threshold () noexcept
{
	LOG_FUNC_ENTER ();

	if (max_gref_count == std::numeric_limits<int>::max ()) {
		LOG_FUNC_LEAVE ();
		return max_gref_count;
	}

	LOG_FUNC_LEAVE ();
	return static_cast<int> ((max_gref_count * 90LL) / 100LL);
}

#if defined (DEBUG) || !defined (ANDROID)
void
AndroidSystem::setup_environment (const char *name, const char *value) noexcept
{
	LOG_FUNC_ENTER ();

	if (name == nullptr || *name == '\0') {
		LOG_FUNC_LEAVE ();
		return;
	}

	const char *v = value;
	if (v == nullptr)
		v = "";

	if (isupper (name [0]) || name [0] == '_') {
		if (setenv (name, v, 1) < 0)
			log_warn (LOG_DEFAULT, "(Debug) Failed to set environment variable: %s", strerror (errno));
		LOG_FUNC_LEAVE ();
		return;
	}

	add_system_property (name, v);

	LOG_FUNC_LEAVE ();
}

void
AndroidSystem::setup_environment_from_override_file (const char *path) noexcept
{
	LOG_FUNC_ENTER ();

#if WINDOWS
	using read_count_type = unsigned int;
#else
	using read_count_type = size_t;
#endif
	monodroid_stat_t sbuf;
	if (Util::monodroid_stat (path, &sbuf) < 0) {
		log_warn (LOG_DEFAULT, "Failed to stat the environment override file %s: %s", path, strerror (errno));

		LOG_FUNC_LEAVE ();
		return;
	}

	int fd = open (path, O_RDONLY);
	if (fd < 0) {
		log_warn (LOG_DEFAULT, "Failed to open the environment override file %s: %s", path, strerror (errno));

		LOG_FUNC_LEAVE ();
		return;
	}

	auto     file_size = static_cast<size_t>(sbuf.st_size);
	size_t   nread = 0;
	ssize_t  r;
	auto     buf = std::make_unique<char[]> (file_size);

	do {
		auto read_count = static_cast<read_count_type>(file_size - nread);
		r = read (fd, buf.get () + nread, read_count);
		if (r > 0)
			nread += static_cast<size_t>(r);
	} while (r < 0 && errno == EINTR);

	if (nread == 0) {
		log_warn (LOG_DEFAULT, "Failed to read the environment override file %s: %s", path, strerror (errno));

		LOG_FUNC_LEAVE ();
		return;
	}

	// The file format is as follows (no newlines are used, this is just for illustration
	// purposes, comments aren't part of the file either):
	//
	// # 10 ASCII characters formattted as a C++ hexadecimal number terminated with NUL: name
	// # width (including the terminating NUL)
	// 0x00000000\0
	//
	// # 10 ASCII characters formattted as a C++ hexadecimal number terminated with NUL: value
	// # width (including the terminating NUL)
	// 0x00000000\0
	//
	// # Variable name, terminated with NUL and padded to [name width] with NUL characters
	// name\0
	//
	// # Variable value, terminated with NUL and padded to [value width] with NUL characters
	// value\0
	if (nread < OVERRIDE_ENVIRONMENT_FILE_HEADER_SIZE) {
		log_warn (LOG_DEFAULT, "Invalid format of the environment override file %s: malformatted header", path);

		LOG_FUNC_LEAVE ();
		return;
	}

	char *endptr;
	unsigned long name_width = strtoul (buf.get (), &endptr, 16);
	if ((name_width == std::numeric_limits<unsigned long>::max () && errno == ERANGE) || (buf[0] != '\0' && *endptr != '\0')) {
		log_warn (LOG_DEFAULT, "Malformed header of the environment override file %s: name width has invalid format", path);

		LOG_FUNC_LEAVE ();
		return;
	}

	unsigned long value_width = strtoul (buf.get () + 11, &endptr, 16);
	if ((value_width == std::numeric_limits<unsigned long>::max () && errno == ERANGE) || (buf[0] != '\0' && *endptr != '\0')) {
		log_warn (LOG_DEFAULT, "Malformed header of the environment override file %s: value width has invalid format", path);

		LOG_FUNC_LEAVE ();
		return;
	}

	uint64_t data_width = name_width + value_width;
	if (data_width > file_size - OVERRIDE_ENVIRONMENT_FILE_HEADER_SIZE || (file_size - OVERRIDE_ENVIRONMENT_FILE_HEADER_SIZE) % data_width != 0) {
		log_warn (LOG_DEFAULT, "Malformed environment override file %s: invalid data size", path);

		LOG_FUNC_LEAVE ();
		return;
	}

	uint64_t data_size = static_cast<uint64_t>(file_size);
	char *name = buf.get () + OVERRIDE_ENVIRONMENT_FILE_HEADER_SIZE;
	while (data_size > 0 && data_size >= data_width) {
		if (*name == '\0') {
			log_warn (LOG_DEFAULT, "Malformed environment override file %s: name at offset %lu is empty", path, name - buf.get ());

			LOG_FUNC_LEAVE ();
			return;
		}

		log_debug (LOG_DEFAULT, "Setting environment variable from the override file %s: '%s' = '%s'", path, name, name + name_width);
		setup_environment (name, name + name_width);
		name += data_width;
		data_size -= data_width;
	}

	LOG_FUNC_LEAVE ();
}
#endif // DEBUG || !ANDROID

void
AndroidSystem::setup_environment () noexcept
{
	LOG_FUNC_ENTER ();

	if (is_mono_aot_enabled () && *mono_aot_mode_name != '\0') {
		switch (mono_aot_mode_name [0]) {
			case 'n':
				aotMode = MonoAotMode::MONO_AOT_MODE_NORMAL;
				break;

			case 'h':
				aotMode = MonoAotMode::MONO_AOT_MODE_HYBRID;
				break;

			case 'f':
				aotMode = MonoAotMode::MONO_AOT_MODE_FULL;
				break;

			case 'i':
#if !defined (NET)
				aotMode = MonoAotMode::MONO_AOT_MODE_LAST;
				aot_mode_last_is_interpreter = true;
#else   // defined (NET)
				aotMode = MonoAotMode::MONO_AOT_MODE_INTERP_ONLY;
#endif  // !defined (NET)
				break;

			default:
				aotMode = MonoAotMode::MONO_AOT_MODE_LAST;
				break;
		}

		if (aotMode != MonoAotMode::MONO_AOT_MODE_LAST) {
			log_info (LOG_DEFAULT, "Mono AOT mode: %s", mono_aot_mode_name);
		} else {
			if (!is_interpreter_enabled ()) {
				log_warn (LOG_DEFAULT, "Unknown Mono AOT mode: %s", mono_aot_mode_name);
			} else {
				log_warn (LOG_DEFAULT, "Mono AOT mode: interpreter");
			}
		}
	}

	if (application_config.environment_variable_count == 0) {
		LOG_FUNC_LEAVE ();
		return;
	}

	if (application_config.environment_variable_count % 2 != 0) {
		log_warn (LOG_DEFAULT, "Corrupted environment variable array: does not contain an even number of entries (%u)", application_config.environment_variable_count);

		LOG_FUNC_ENTER ();
		return;
	}

	for (size_t i = 0; i < application_config.environment_variable_count; i += 2) {
		const char *var_name = app_environment_variables [i];
		if (var_name == nullptr || *var_name == '\0')
			continue;

		const char *var_value = app_environment_variables [i + 1];
		if (var_value == nullptr)
			var_value = "";

#if defined (DEBUG)
		log_info (LOG_DEFAULT, "Setting environment variable '%s' to '%s'", var_name, var_value);
#endif
		if (setenv (var_name, var_value, 1) < 0)
			log_warn (LOG_DEFAULT, "Failed to set environment variable: %s", strerror (errno));
	}
#if defined (DEBUG) || !defined (ANDROID)
	// TODO: for debug read from file in the override directory named `environment`
	for (const auto override_dir : override_dirs ()) {
		std::unique_ptr<char[]> env_override_file {Util::path_combine (override_dir, OVERRIDE_ENVIRONMENT_FILE_NAME)};
		if (Util::file_exists (env_override_file.get ())) {
			setup_environment_from_override_file (env_override_file.get ());
		}
	}
#endif

	LOG_FUNC_LEAVE ();
}

void
AndroidSystem::setup_process_args_apk (const char *apk, size_t index, size_t apk_count, [[maybe_unused]] void *user_data) noexcept
{
	LOG_FUNC_ENTER ();

	if (apk == nullptr || index != apk_count - 1) {
		LOG_FUNC_LEAVE ();
		return;
	}

	std::array<char*, 1> args { const_cast<char*>(apk) };
	mono_runtime_set_main_args (1, args.data ());

	LOG_FUNC_LEAVE ();
}

void
AndroidSystem::setup_process_args (jstring_array_wrapper &runtimeApks) noexcept
{
	LOG_FUNC_ENTER ();

	for_each_apk (runtimeApks, static_cast<BasicAndroidSystem::ForEachApkHandler> (&AndroidSystem::setup_process_args_apk), nullptr);

	LOG_FUNC_LEAVE ();
}

monodroid_dirent_t*
AndroidSystem::readdir (monodroid_dir_t *dir) noexcept
{
	LOG_FUNC_ENTER ();
	LOG_FUNC_LEAVE ();
#if defined (WINDOWS)
	return readdir_windows (dir);
#else
	return ::readdir (dir);
#endif
}

#if defined (WINDOWS)
struct _wdirent*
AndroidSystem::readdir_windows (_WDIR *dirp) noexcept
{
	LOG_FUNC_ENTER ();

	std::lock_guard<std::mutex> lock (readdir_mutex);
	errno = 0;
	struct _wdirent *entry = _wreaddir (dirp);

	if (entry == nullptr && errno != 0) {
		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	LOG_FUNC_LEAVE ();
	return entry;
}

// Returns the directory in which this library was loaded from
char*
AndroidSystem::get_libmonoandroid_directory_path () noexcept
{
	LOG_FUNC_ENTER ();

	wchar_t module_path[MAX_PATH];
	HMODULE module = nullptr;

	if (libmonoandroid_directory_path != nullptr) {
		LOG_FUNC_LEAVE ();
		return libmonoandroid_directory_path;
	}

	DWORD flags = GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT;
	const wchar_t *dir_path = reinterpret_cast<wchar_t*>(&libmonoandroid_directory_path);
	BOOL retval = GetModuleHandleExW (flags, dir_path, &module);
	if (!retval) {
		LOG_FUNC_LEAVE ();
		return nullptr;
	}

	GetModuleFileNameW (module, module_path, sizeof (module_path) / sizeof (module_path[0]));
	PathRemoveFileSpecW (module_path);
	libmonoandroid_directory_path = Util::utf16_to_utf8 (module_path);

	LOG_FUNC_LEAVE ();
	return libmonoandroid_directory_path;
}

int
AndroidSystem::setenv (const char *name, const char *value, [[maybe_unused]] int overwrite) noexcept
{
	LOG_FUNC_ENTER ();

	wchar_t *wname  = Util::utf8_to_utf16 (name);
	wchar_t *wvalue = Util::utf8_to_utf16 (value);

	BOOL result = SetEnvironmentVariableW (wname, wvalue);
	free (wname);
	free (wvalue);

	LOG_FUNC_LEAVE ();
	return result ? 0 : -1;
}

int
AndroidSystem::symlink (const char *target, const char *linkpath) noexcept
{
	LOG_FUNC_ENTER ();
	LOG_FUNC_LEAVE ();
	return Util::file_copy (target, linkpath);
}
#else
#endif
