#include <cerrno>
#include <cstdlib>
#include <cstdarg>

#ifdef WINDOWS
#include <direct.h>
#include <shlwapi.h>
#endif

#include "basic-utilities.hh"
#include "logger.hh"
#include "cpp-util.hh"

using namespace xamarin::android;

gsl::owner<char*>
BasicUtilities::path_combine (const char *path1, const char *path2) noexcept
{
	// Don't let erroneous nullptr parameters situation propagate
	abort_unless (path1 != nullptr || path2 != nullptr, "At least one path must be a valid pointer");

	if (path1 == nullptr)
		return strdup_new (path2);
	if (path2 == nullptr)
		return strdup_new (path1);

	auto len = ADD_WITH_OVERFLOW_CHECK (size_t, strlen (path1), strlen (path2) + 2);
	gsl::owner<char*> ret = new char [len];
	*ret = '\0';

	strncat (ret, path1, len - 1);
	strncat (ret, MONODROID_PATH_SEPARATOR, len - 1);
	strncat (ret, path2, len - 1);

	return ret;
}

void
BasicUtilities::create_public_directory (const char *dir) noexcept
{
#ifndef WINDOWS
	constexpr mode_t DIRECTORY_PERMISSION_BITS = 0777;

	mode_t m = umask (0);
	mkdir (dir, DIRECTORY_PERMISSION_BITS);
	umask (m);
#else
	wchar_t *buffer = utf8_to_utf16 (dir);
	_wmkdir (buffer);
	free (buffer);
#endif
}

int
BasicUtilities::create_directory (const char *pathname, mode_t mode) noexcept
{
	if (mode <= 0)
		mode = DEFAULT_DIRECTORY_MODE;

	if  (!pathname || *pathname == '\0') {
		errno = EINVAL;
		return -1;
	}

	constexpr mode_t DEFAULT_UMASK = 022;

#ifdef WINDOWS
	using umask_t = int;
#else
	using umask_t = mode_t;
#endif

	umask_t oldumask = umask (DEFAULT_UMASK);
	std::unique_ptr<char[]> path {strdup_new (pathname)};

	int ret = 0;
	for (char *d = path.get (); d != nullptr && *d; ++d) {
		if (*d != '/')
			continue;
		*d = 0;
		if (path[0]) {
			int rv = make_directory (path.get (), mode);
			if  (rv == -1 && errno != EEXIST)  {
				ret = -1;
				break;
			}
		}
		*d = '/';
	}

	if (ret == 0)
		ret = make_directory (pathname, mode);
	umask (oldumask);

	return ret;
}

void
BasicUtilities::set_world_accessable ([[maybe_unused]] const char *path) noexcept
{
#ifdef ANDROID
	constexpr mode_t WORLD_ACCESSIBLE_FILE_PERMISSION_BITS = S_IWUSR | S_IRUSR | S_IRGRP | S_IROTH;

	int r;
	do {
		r = chmod (path, WORLD_ACCESSIBLE_FILE_PERMISSION_BITS);
	} while (r == -1 && errno == EINTR);

	if (r == -1)
		log_error (LOG_DEFAULT, "chmod(\"%s\", %u) failed: %s", path, WORLD_ACCESSIBLE_FILE_PERMISSION_BITS, strerror (errno));
#endif
}

void
BasicUtilities::set_user_executable ([[maybe_unused]] const char *path) noexcept
{
#ifdef ANDROID
	int r;
	do {
		r = chmod (path, S_IRUSR | S_IWUSR | S_IXUSR);
	} while (r == -1 && errno == EINTR);

	if (r == -1)
		log_error (LOG_DEFAULT, "chmod(\"%s\") failed: %s", path, strerror (errno));
#endif
}

bool
BasicUtilities::file_copy (const char *to, const char *from) noexcept
{
	if (to == nullptr || *to == '\0') {
		log_error (LOG_DEFAULT, "BasicUtilities::file_copy: `to` parameter must not be null or empty");
		return false;
	}

	if (from == nullptr || *from == '\0') {
		log_error (LOG_DEFAULT, "BasicUtilities::file_copy: `from` parameter must not be null or empty");
		return false;
	}

	gsl::owner<FILE*> f1 = monodroid_fopen (from, "r");
	if (f1 == nullptr)
		return false;

	gsl::owner<FILE*> f2 = monodroid_fopen (to, "w+");
	if (f2 == nullptr)
		return false;

	using read_buffer_t = std::array<char, BUFSIZ>;
	read_buffer_t buffer;
	size_t n = 0;

	while ((n = fread (buffer.data (), sizeof(read_buffer_t::value_type), buffer.size (), f1)) > 0) {
		if (fwrite (buffer.data (), sizeof(read_buffer_t::value_type), n, f2) != n) {
			int saved_errno = errno;
			fclose (f1);
			fclose (f2);
			errno = saved_errno;

			return false;
		}
	}

	fclose (f1);
	fclose (f2);
	return true;
}

bool
BasicUtilities::is_path_rooted (const char *path) noexcept
{
	if (path == nullptr)
		return false;
#ifdef WINDOWS
	LPCWSTR wpath = utf8_to_utf16 (path);
	bool ret = !PathIsRelativeW (wpath);
	free (const_cast<void*> (reinterpret_cast<const void*> (wpath)));
	return ret;
#else
	return path [0] == MONODROID_PATH_SEPARATOR_CHAR;
#endif
}

gsl::owner<FILE*>
BasicUtilities::monodroid_fopen (const char *filename, const char *mode) noexcept
{
	gsl::owner<FILE*> ret;
#ifndef WINDOWS
	/* On Unix, both path and system calls are all assumed
	 * to be UTF-8 compliant.
	 */
	ret = fopen (filename, mode);
#else
	// Convert the path and mode to a UTF-16 and then use the wide variant of fopen
	wchar_t *wpath = utf8_to_utf16 (filename);
	wchar_t *wmode = utf8_to_utf16 (mode);

	ret = _wfopen (wpath, wmode);
	free (wpath);
	free (wmode);
#endif // ndef WINDOWS
	if (ret == nullptr) {
		log_error (LOG_DEFAULT, "fopen failed for file %s: %s", filename, strerror (errno));
		return nullptr;
	}

	return ret;
}

int
BasicUtilities::monodroid_stat (const char *path, monodroid_stat_t *s) noexcept
{
	int result;

#ifndef WINDOWS
	result = stat (path, s);
#else
	wchar_t *wpath = utf8_to_utf16 (path);
	result = _wstat (wpath, s);
	free (wpath);
#endif

	return result;
}

monodroid_dir_t*
BasicUtilities::monodroid_opendir (const char *filename) noexcept
{
#ifndef WINDOWS
	return opendir (filename);
#else
	wchar_t *wfilename = utf8_to_utf16 (filename);
	monodroid_dir_t *result = _wopendir (wfilename);
	free (wfilename);
	return result;
#endif
}

int
BasicUtilities::monodroid_closedir (monodroid_dir_t *dirp) noexcept
{
#ifndef WINDOWS
	return closedir (dirp);
#else
	return _wclosedir (dirp);
#endif
}

int
BasicUtilities::monodroid_dirent_hasextension (monodroid_dirent_t *e, const char *extension) noexcept
{
#ifndef WINDOWS
	return ends_with_slow (e->d_name, extension);
#else
	char *mb_dname = utf16_to_utf8 (e->d_name);
	int result = ends_with_slow (mb_dname, extension);
	free (mb_dname);
	return result;
#endif
}

char *
BasicUtilities::monodroid_strdup_printf (const char *format, ...) noexcept
{
        va_list args;

        va_start (args, format);
        char *ret = monodroid_strdup_vprintf (format, args);
        va_end (args);

        return ret;
}

char*
BasicUtilities::monodroid_strdup_vprintf (const char *format, va_list vargs) noexcept
{
	char *ret = nullptr;
	int n = vasprintf (&ret, format, vargs);

	return n == -1 ? nullptr : ret;
}
