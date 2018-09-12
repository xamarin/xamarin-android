
#include <stdlib.h>
#include <stdarg.h>
#include <jni.h>
#include <time.h>
#include <stdio.h>
#include <string.h>
#include <strings.h>
#include <ctype.h>
#include <assert.h>
#include <errno.h>
#include <limits.h>

#ifdef ANDROID
#include <sys/system_properties.h>
#else
#define PROP_NAME_MAX   32
#define PROP_VALUE_MAX  92
#endif

#include <dlfcn.h>
#include <fcntl.h>
#include <unistd.h>
#include <stdint.h>

#include <sys/time.h>

#include "mono_android_Runtime.h"

#if defined (LINUX)
#include <sys/syscall.h>
#endif

#if defined (DEBUG) && !defined (WINDOWS)
#include <fcntl.h>
#include <arpa/inet.h>
#include <sys/types.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <errno.h>
#endif

#if defined (LINUX)
#include <sys/syscall.h>
#endif

#ifndef WINDOWS
#include <sys/mman.h>
#include <sys/utsname.h>
#else
#include <windef.h>
#include <winbase.h>
#include <shlobj.h>
#include <objbase.h>
#include <knownfolders.h>
#include <shlwapi.h>
#endif

#include <sys/stat.h>
#include <unistd.h>
#include <dirent.h>
#include <pthread.h>

#include "java-interop-util.h"

#include "monodroid.h"
#include "dylib-mono.h"
#include "util.h"
#include "debug.h"
#include "embedded-assemblies.h"
#include "unzip.h"
#include "ioapi.h"
#include "monodroid-glue.h"
#include "mkbundle-api.h"

#ifndef WINDOWS
#include "xamarin_getifaddrs.h"
#endif

static pthread_mutex_t process_cmd_mutex = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t process_cmd_cond = PTHREAD_COND_INITIALIZER;
static int debugging_configured;
static int sdb_fd;
static int profiler_configured;
static int profiler_fd;
static char *profiler_description;
#if DEBUG
static int config_timedout;
static struct timeval wait_tv;
static struct timespec wait_ts;
#endif  // def DEBUG
static char *runtime_libdir;
static int register_debug_symbols;
static MonoMethod* registerType;
/*
 * If set, monodroid will spin in a loop until the debugger breaks the wait by
 * clearing monodroid_gdb_wait.
 */
static int wait_for_gdb;
static volatile int monodroid_gdb_wait = TRUE;
static int android_api_level = 0;

/* Can be called by a native debugger to break the wait on startup */
MONO_API void
monodroid_clear_gdb_wait (void)
{
	monodroid_gdb_wait = FALSE;
}

#ifdef ANDROID64
#define SYSTEM_LIB_PATH "/system/lib64"
#elif ANDROID
#define SYSTEM_LIB_PATH "/system/lib"
#elif LINUX_FLATPAK
#define SYSTEM_LIB_PATH "/app/lib/mono"
#elif LINUX
#define SYSTEM_LIB_PATH "/usr/lib"
#elif APPLE_OS_X
#define SYSTEM_LIB_PATH "/Library/Frameworks/Xamarin.Android.framework/Libraries/"
#elif WINDOWS
#define SYSTEM_LIB_PATH get_xamarin_android_msbuild_path()
#else
#define SYSTEM_LIB_PATH ""
#endif

FILE  *gref_log;
FILE  *lref_log;

/* !DO NOT REMOVE! Used by Mono BCL */
MONO_API int
_monodroid_get_android_api_level (void)
{
	return android_api_level;
}

/* Invoked by System.Core.dll!System.IO.MemoryMappedFiles.MemoryMapImpl.getpagesize */
MONO_API int
monodroid_getpagesize (void)
{
#ifndef WINDOWS
	return getpagesize ();
#else
	SYSTEM_INFO info;
	GetSystemInfo (&info);
	return info.dwPageSize;
#endif
}

/* Invoked by:
    - System.Core.dll!System.TimeZoneInfo.Android.GetDefaultTimeZoneName
    - Mono.Android.dll!Android.Runtime.AndroidEnvironment.GetDefaultTimeZone
*/

MONO_API void
monodroid_free (void *ptr)
{
	free (ptr);
}

static int max_gref_count;

struct BundledProperty {
	char *name;
	char *value;
	int   value_len;
	struct BundledProperty *next;
};

static struct BundledProperty* bundled_properties;

static struct BundledProperty*
lookup_system_property (const char *name)
{
	struct BundledProperty *p = bundled_properties;
	for ( ; p ; p = p->next)
		if (strcmp (p->name, name) == 0)
			return p;
	return NULL;
}

static void
add_system_property (const char *name, const char *value)
{
	int name_len, value_len;

	struct BundledProperty* p = lookup_system_property (name);
	if (p) {
		char *n = monodroid_strdup_printf ("%s", value);
		if (!n)
			return;
		free (p->value);
		p->value      = n;
		p->value_len  = strlen (p->value);
		return;
	}

	name_len  = strlen (name);
	value_len = strlen (value);

	p = malloc (sizeof (struct BundledProperty) + name_len + 1);
	if (!p)
		return;

	p->name = ((char*) p) + sizeof (struct BundledProperty);
	strncpy (p->name, name, name_len);
	p->name [name_len] = '\0';

	p->value      = monodroid_strdup_printf ("%s", value);
	p->value_len  = value_len;

	p->next             = bundled_properties;
	bundled_properties  = p;
}

#ifndef ANDROID
static void
monodroid_strreplace (char *buffer, char old_char, char new_char)
{
	if (buffer == NULL)
		return;
	while (*buffer != '\0') {
		if (*buffer == old_char)
			*buffer = new_char;
		buffer++;
	}
}

static int
_monodroid__system_property_get (const char *name, char *sp_value, size_t sp_value_len)
{
	if (!name || !sp_value)
		return -1;

	char *env_name = monodroid_strdup_printf ("__XA_%s", name);
	monodroid_strreplace (env_name, '.', '_');
	char *env_value = getenv (env_name);
	free (env_name);

	size_t env_value_len = env_value ? strlen (env_value) : 0;
	if (env_value_len == 0) {
		sp_value[0] = '\0';
		return 0;
	}

	if (env_value_len >= sp_value_len)
		log_warn (LOG_DEFAULT, "System property buffer size too small by %u bytes", env_value_len == sp_value_len ? 1 : env_value_len - sp_value_len);

	strncpy (sp_value, env_value, sp_value_len);
	sp_value[sp_value_len] = '\0';

	return strlen (sp_value);
}
#elif ANDROID64
/* __system_property_get was removed in Android 5.0/64bit
   this is hopefully temporary replacement, until we find better
   solution

   sp_value buffer should be at least PROP_VALUE_MAX+1 bytes long
*/
static int
_monodroid__system_property_get (const char *name, char *sp_value, size_t sp_value_len)
{
	if (!name || !sp_value)
		return -1;

	char *cmd = monodroid_strdup_printf ("getprop %s", name);
	FILE* result = popen (cmd, "r");
	int len = (int) fread (sp_value, 1, sp_value_len, result);
	fclose (result);
	sp_value [len] = 0;
	if (len > 0 && sp_value [len - 1] == '\n') {
		sp_value [len - 1] = 0;
		len--;
	} else {
		if (len != 0)
			len = 0;
		sp_value [0] = 0;
	}

	log_info (LOG_DEFAULT, "_monodroid__system_property_get %s: '%s' len: %d", name, sp_value, len);

	return len;
}
#else
static int
_monodroid__system_property_get (const char *name, char *sp_value, size_t sp_value_len)
{
	if (!name || !sp_value)
		return -1;

	char *buf = NULL;
	if (sp_value_len < PROP_VALUE_MAX + 1) {
		log_warn (LOG_DEFAULT, "Buffer to store system property may be too small, will copy only %u bytes", sp_value_len);
		buf = xmalloc (PROP_VALUE_MAX + 1);
	}

	int len = __system_property_get (name, buf ? buf : sp_value);
	if (buf) {
		strncpy (sp_value, buf, sp_value_len);
		sp_value [sp_value_len] = '\0';
		free (buf);
	}

	return len;
}
#endif

MONO_API int
monodroid_get_system_property (const char *name, char **value)
{
	char *pvalue;
	char  sp_value [PROP_VALUE_MAX+1] = { 0, };
	int   len;
	struct BundledProperty *p;

	if (value)
		*value = NULL;

	pvalue  = sp_value;
	len     = _monodroid__system_property_get (name, sp_value, sizeof (sp_value));

	if (len <= 0 && (p = lookup_system_property (name)) != NULL) {
		pvalue  = p->value;
		len     = p->value_len;
	}

	if (len >= 0 && value) {
		*value = malloc (len+1);
		if (!*value)
			return -len;
		memcpy (*value, pvalue, len);
		(*value)[len] = '\0';
	}
	return len;
}

#ifdef RELEASE
#define MAX_OVERRIDES 1
#else
#define MAX_OVERRIDES 3
#endif
static char* override_dirs [MAX_OVERRIDES];

static int
_monodroid_get_system_property_from_file (const char *path, char **value)
{
	int i;

	if (value)
		*value = NULL;

	FILE* fp = monodroid_fopen (path, "r");
	if (fp == NULL)
		return 0;

	struct stat fileStat;
	if (fstat (fileno (fp), &fileStat) < 0) {
		fclose (fp);
		return 0;
	}

	if (!value) {
		fclose (fp);
		return fileStat.st_size+1;
	}

	*value = malloc (fileStat.st_size+1);
	if (!(*value)) {
		fclose (fp);
		return fileStat.st_size+1;
	}

	ssize_t len = fread (*value, 1, fileStat.st_size, fp);
	fclose (fp);
	for (i = 0; i < fileStat.st_size+1; ++i) {
		if ((*value) [i] != '\n' && (*value) [i] != '\r')
			continue;
		(*value) [i] = 0;
		break;
	}
	return len;
}

int
monodroid_get_system_property_from_overrides (const char *name, char ** value)
{
	int result = -1;
	int oi;

	for (oi = 0; oi < MAX_OVERRIDES; ++oi) {
		if (override_dirs [oi]) {
			char *overide_file = path_combine (override_dirs [oi], name);
			log_info (LOG_DEFAULT, "Trying to get property from %s", overide_file);
			result = _monodroid_get_system_property_from_file (overide_file, value);
			free (overide_file);
			if (result <= 0 || value == NULL || (*value) == NULL || strlen (*value) == 0) {
				continue;
			}
			log_info (LOG_DEFAULT, "Property '%s' from  %s has value '%s'.", name, override_dirs [oi], *value);
			return result;
		}
	}
	return 0;
}

static char*
get_primary_override_dir (JNIEnv *env, jstring home)
{
	const char *v;
	char *p;

	v = (*env)->GetStringUTFChars (env, home, NULL);
	p = path_combine (v, ".__override__");
	(*env)->ReleaseStringUTFChars (env, home, v);

	return p;
}

static char *primary_override_dir;
static char *external_override_dir;
static char *external_legacy_override_dir;

static void
create_update_dir (char *override_dir)
{
#if defined(RELEASE)
	/*
	 * Don't create .__override__ on Release builds, because Google requires
	 * that pre-loaded apps not create world-writable directories.
	 *
	 * However, if any logging is enabled (which should _not_ happen with
	 * pre-loaded apps!), we need the .__override__ directory...
	 */
	if (log_categories == 0 && monodroid_get_namespaced_system_property (DEBUG_MONO_PROFILE_PROPERTY, NULL) == 0) {
		return;
	}
#endif

	override_dirs [0] = override_dir;
	create_public_directory (override_dir);
	log_info (LOG_DEFAULT, "Creating public update directory: `%s`", override_dir);
}

static int
file_exists (const char *file)
{
	monodroid_stat_t s;
	if (monodroid_stat (file, &s) == 0 && (s.st_mode & S_IFMT) == S_IFREG)
		return 1;
	return 0;
}

static int
directory_exists (const char *directory)
{
	monodroid_stat_t s;
	if (monodroid_stat (directory, &s) == 0 && (s.st_mode & S_IFMT) == S_IFDIR)
		return 1;
	return 0;
}

static struct DylibMono mono;

struct DylibMono*
monodroid_get_dylib (void)
{
	return &mono;
}

static const char *app_libdir;

int file_copy(const char *to, const char *from)
{
    char buffer[BUFSIZ];
    size_t n;
    int saved_errno;

    FILE *f1 = monodroid_fopen(from, "r");
    FILE *f2 = monodroid_fopen(to, "w+");

    while ((n = fread(buffer, sizeof(char), sizeof(buffer), f1)) > 0)
    {
        if (fwrite(buffer, sizeof(char), n, f2) != n)
        {
			saved_errno = errno;
			fclose (f1);
			fclose (f2);
			errno = saved_errno;

        	return -1;
        }
    }

	fclose (f1);
	fclose (f2);
	return 0;
}

/* Set of Windows-specific utility/reimplementation of Unix functions */
#ifdef WINDOWS

#define symlink file_copy

static char *msbuild_folder_path = NULL;

static const char*
get_xamarin_android_msbuild_path (void)
{
	const char *suffix = "MSBuild\\Xamarin\\Android";
	char *base_path = NULL;
	wchar_t *buffer = NULL;

	if (msbuild_folder_path != NULL)
		return msbuild_folder_path;

	// Get the base path for 'Program Files' on Windows
	if (!SUCCEEDED (SHGetKnownFolderPath (&FOLDERID_ProgramFilesX86, 0, NULL, &buffer))) {
		if (buffer != NULL)
			CoTaskMemFree (buffer);
		// returns current directory if a global one couldn't be found
		return ".";
	}

	// Compute the final path
	base_path = utf16_to_utf8 (buffer);
	CoTaskMemFree (buffer);
	msbuild_folder_path = path_combine (base_path, suffix);
	free (base_path);

	return msbuild_folder_path;
}

static char *libmonoandroid_directory_path = NULL;

// Returns the directory in which this library was loaded from
static char*
get_libmonoandroid_directory_path ()
{
	wchar_t module_path[MAX_PATH];
	HMODULE module = NULL;

	if (libmonoandroid_directory_path != NULL)
		return libmonoandroid_directory_path;

	DWORD flags = GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT;
	if (!GetModuleHandleExW (flags, (void*)&libmonoandroid_directory_path, &module))
		return NULL;

	GetModuleFileNameW (module, module_path, sizeof (module_path) / sizeof (module_path[0]));
	PathRemoveFileSpecW (module_path);
	libmonoandroid_directory_path = utf16_to_utf8 (module_path);
	return libmonoandroid_directory_path;
}

static int
setenv(const char *name, const char *value, int overwrite)
{
	wchar_t *wname  = utf8_to_utf16 (name);
	wchar_t *wvalue = utf8_to_utf16 (value);

	BOOL result = SetEnvironmentVariableW (wname, wvalue);
	free (wname);
	free (wvalue);

	return result ? 0 : -1;
}

static pthread_mutex_t readdir_mutex = PTHREAD_MUTEX_INITIALIZER;

static int
readdir_r (_WDIR *dirp, struct _wdirent *entry, struct _wdirent **result)
{
	int error_code = 0;

	pthread_mutex_lock (&readdir_mutex);
	errno = 0;
	entry = _wreaddir (dirp);
	*result = entry;

	if (entry == NULL && errno != 0)
		error_code = -1;

	pthread_mutex_unlock (&readdir_mutex);
	return error_code;
}

#endif // def WINDOWS

#ifndef RELEASE

static void
copy_monosgen_to_internal_location(char *to, char *from)
{
	char *from_libmonoso = path_combine (from, "libmonosgen-2.0.so");

	if (!file_exists (from_libmonoso))
	{
		free (from_libmonoso);
		return;
	}

	log_info (LOG_DEFAULT, "Copying sgen from external location %s to internal location %s", from, to);

	char *to_libmonoso = path_combine (to, "libmonosgen-2.0.so");
	unlink (to_libmonoso);

	if (file_copy (to_libmonoso, from_libmonoso) < 0)
		log_warn (LOG_DEFAULT, "Copy failed: %s", strerror (errno));

	free (from_libmonoso);
	free (to_libmonoso);
}
#endif

#if ANDROID || LINUX
#define MONO_SGEN_SO "libmonosgen-2.0.so"
#define MONO_SGEN_ARCH_SO "libmonosgen-%s-2.0.so"
#elif APPLE_OS_X
#define MONO_SGEN_SO "libmonosgen-2.0.dylib"
#define MONO_SGEN_ARCH_SO "libmonosgen-%s-2.0.dylib"
#elif WINDOWS
#define MONO_SGEN_SO "libmonosgen-2.0.dll"
#define MONO_SGEN_ARCH_SO "libmonosgen-%s-2.0.dll"
#else
#define MONO_SGEN_SO "monosgen-2.0"
#define MONO_SGEN_ARCH_SO "monosgen-%s-2.0"
#endif

#define TRY_LIBMONOSGEN(dir) \
	if (dir) { \
		libmonoso = path_combine (dir, MONO_SGEN_SO); \
		log_info (LOG_DEFAULT, "Trying to load sgen from: %s", libmonoso);	\
		if (file_exists (libmonoso)) \
			return libmonoso; \
		free (libmonoso); \
	}

static char*
get_libmonosgen_path ()
{
	char *libmonoso;

#ifndef RELEASE
	// Android 5 includes some restrictions on loading dynamic libraries via dlopen() from
	// external storage locations so we need to file copy the shared object to an internal
	// storage location before loading it.
	copy_monosgen_to_internal_location (primary_override_dir, external_override_dir);
	copy_monosgen_to_internal_location (primary_override_dir, external_legacy_override_dir);

	int i;
	for (i = 0; i < MAX_OVERRIDES; ++i)
		TRY_LIBMONOSGEN (override_dirs [i])
#endif
	TRY_LIBMONOSGEN (app_libdir)

	libmonoso = runtime_libdir ? monodroid_strdup_printf ("%s" MONODROID_PATH_SEPARATOR MONO_SGEN_ARCH_SO, runtime_libdir, sizeof(void*) == 8 ? "64bit" : "32bit") : NULL;
	if (libmonoso && file_exists (libmonoso)) {
		char* links_dir = path_combine (primary_override_dir, "links");
		char* link = path_combine (links_dir, MONO_SGEN_SO);
		if (!directory_exists (links_dir)) {
			if (!directory_exists (primary_override_dir))
				create_public_directory (primary_override_dir);
			create_public_directory (links_dir);
		}
		free (links_dir);
		if (!file_exists (link)) {
			int result = symlink (libmonoso, link);
			if (result != 0 && errno == EEXIST) {
				log_warn (LOG_DEFAULT, "symlink exists, recreating: %s -> %s", link, libmonoso);
				unlink (link);
				result = symlink (libmonoso, link);
			}
			if (result != 0)
				log_warn (LOG_DEFAULT, "symlink failed with errno=%i %s", errno, strerror (errno));
		}
		free (libmonoso);
		libmonoso = link;
	}

	log_info (LOG_DEFAULT, "Trying to load sgen from: %s", libmonoso);
	if (libmonoso && file_exists (libmonoso))
		return libmonoso;
	free (libmonoso);

#ifdef WINDOWS
	TRY_LIBMONOSGEN (get_libmonoandroid_directory_path ())
#endif

	TRY_LIBMONOSGEN (SYSTEM_LIB_PATH)
	
#ifdef RELEASE
	log_fatal (LOG_DEFAULT, "cannot find libmonosgen-2.0.so in app_libdir: %s nor in previously printed locations.", app_libdir);
#else
	log_fatal (LOG_DEFAULT, "cannot find libmonosgen-2.0.so in override_dir: %s, app_libdir: %s nor in previously printed locations.", override_dirs[0], app_libdir);
#endif
	log_fatal (LOG_DEFAULT, "Do you have a shared runtime build of your app with AndroidManifest.xml android:minSdkVersion < 10 while running on a 64-bit Android 5.0 target? This combination is not supported.");
	log_fatal (LOG_DEFAULT, "Please either set android:minSdkVersion >= 10 or use a build without the shared runtime (like default Release configuration).");
	exit (FATAL_EXIT_CANNOT_FIND_LIBMONOSGEN);

	return libmonoso;
}

typedef void* (*mono_mkbundle_init_ptr) (void (*)(const MonoBundledAssembly **), void (*)(const char* assembly_name, const char* config_xml),void (*) (int mode));
mono_mkbundle_init_ptr mono_mkbundle_init;
void (*mono_mkbundle_initialize_mono_api) (const BundleMonoAPI *info);

static void
setup_bundled_app (const char *libappso)
{
	void *libapp;

	libapp = dlopen (libappso, RTLD_LAZY);

	if (!libapp) {
		log_fatal (LOG_BUNDLE, "bundled app initialization error: %s", dlerror ());
		exit (FATAL_EXIT_CANNOT_LOAD_BUNDLE);
	}

	mono_mkbundle_initialize_mono_api = dlsym (libapp, "initialize_mono_api");
	if (!mono_mkbundle_initialize_mono_api)
		log_error (LOG_BUNDLE, "Missing initialize_mono_api in the application");

	mono_mkbundle_init = dlsym (libapp, "mono_mkbundle_init");
	if (!mono_mkbundle_init)
		log_error (LOG_BUNDLE, "Missing mono_mkbundle_init in the application");
	log_info (LOG_BUNDLE, "Bundled app loaded: %s", libappso);
}

static char*
get_bundled_app (JNIEnv *env, jstring dir)
{
	const char *v;
	char *libapp;

#ifndef RELEASE
	libapp = path_combine (override_dirs [0], "libmonodroid_bundle_app.so");

	if (file_exists (libapp))
		return libapp;

	free (libapp);
#endif

	if (dir) {
		v = (*env)->GetStringUTFChars (env, dir, NULL);
		if (v) {
			libapp = path_combine (v, "libmonodroid_bundle_app.so");
			(*env)->ReleaseStringUTFChars (env, dir, v);
			if (file_exists (libapp))
				return libapp;
		}
	}
	return NULL;
}

static JavaVM *jvm;

typedef struct {
	void *dummy;
} MonoDroidProfiler;

static MonoDroidProfiler monodroid_profiler;

typedef struct MonoJavaGCBridgeType {
	const char *namespace;
	const char *typename;
} MonoJavaGCBridgeType;

static const MonoJavaGCBridgeType mono_java_gc_bridge_types[] = {
	{ "Java.Lang",  "Object" },
	{ "Java.Lang",  "Throwable" },
};

#define NUM_GC_BRIDGE_TYPES (sizeof (mono_java_gc_bridge_types)/sizeof (mono_java_gc_bridge_types [0]))

/* `mono_java_gc_bridge_info` stores shared global data about the last Monodroid assembly loaded.
 * Specifically it stores data about the `mono_java_gc_bridge_types` types.
 * In order for this to work, two rules must be followed.
 *   1. Only one Monodroid appdomain can be loaded at a time.
 *   2. Since the Monodroid appdomain unload clears `mono_java_gc_bridge_info`, anything which
 *      could run at the same time as the domain unload (like gc_bridge_class_kind) must tolerate
 *      the structure fields being set to zero during run
 */
typedef struct MonoJavaGCBridgeInfo {
	MonoClass       *klass;
	MonoClassField  *handle;
	MonoClassField  *handle_type;
	MonoClassField  *refs_added;
	MonoClassField  *weak_handle;
} MonoJavaGCBridgeInfo;

static MonoJavaGCBridgeInfo mono_java_gc_bridge_info [NUM_GC_BRIDGE_TYPES];

static jclass weakrefClass;
static jmethodID weakrefCtor;
static jmethodID weakrefGet;

static jobject    Runtime_instance;
static jmethodID  Runtime_gc;

static jclass     TimeZone_class;
static jmethodID  TimeZone_getDefault;
static jmethodID  TimeZone_getID;

static int gc_disabled = 0;

static int gc_gref_count;
static int gc_weak_gref_count;

static int is_running_on_desktop = 0;

// Do this instead of using memset so that individual pointers are set atomically
static void clear_mono_java_gc_bridge_info() {
	for (int c = 0; c < NUM_GC_BRIDGE_TYPES; c++) {
		MonoJavaGCBridgeInfo *info = &mono_java_gc_bridge_info [c];
		info->klass = NULL;
		info->handle = NULL;
		info->handle_type = NULL;
		info->refs_added = NULL;
		info->weak_handle = NULL;
	}
}

static int
get_gc_bridge_index (MonoClass *klass)
{
	int i;
	int f = 0;

	for (i = 0; i < NUM_GC_BRIDGE_TYPES; ++i) {
		MonoClass *k = mono_java_gc_bridge_info [i].klass;
		if (k == NULL) {
			f++;
			continue;
		}
		if (klass == k || mono.mono_class_is_subclass_of (klass, k, 0))
			return i;
	}
	return f == NUM_GC_BRIDGE_TYPES
		? -(int) NUM_GC_BRIDGE_TYPES
		: -1;
}


static MonoJavaGCBridgeInfo *
get_gc_bridge_info_for_class (MonoClass *klass)
{
	int   i;

	if (klass == NULL)
		return NULL;

	i   = get_gc_bridge_index (klass);
	if (i < 0)
		return NULL;
	return &mono_java_gc_bridge_info [i];
}

static MonoJavaGCBridgeInfo *
get_gc_bridge_info_for_object (MonoObject *object)
{
	if (object == NULL)
		return NULL;
	return get_gc_bridge_info_for_class (mono.mono_object_get_class (object));
}

static jobject
lref_to_gref (JNIEnv *env, jobject lref)
{
	jobject g;
	if (lref == 0)
		return 0;
	g = (*env)->NewGlobalRef (env, lref);
	(*env)->DeleteLocalRef (env, lref);
	return g;
}

static char
get_object_ref_type (JNIEnv *env, void *handle)
{
	jobjectRefType value;
	if (handle == NULL)
		return 'I';
	value = (*env)->GetObjectRefType (env, handle);
	switch (value) {
		case JNIInvalidRefType:     return 'I';
		case JNILocalRefType:       return 'L';
		case JNIGlobalRefType:      return 'G';
		case JNIWeakGlobalRefType:  return 'W';
		default:                    return '*';
	}
}

MONO_API extern int
_monodroid_max_gref_get (void)
{
	return max_gref_count;
}

MONO_API extern int
_monodroid_gref_get (void)
{
	return gc_gref_count;
}

static int
_monodroid_gref_inc (void)
{
	return __sync_add_and_fetch (&gc_gref_count, 1);
}

static int
_monodroid_gref_dec (void)
{
	return __sync_fetch_and_sub (&gc_gref_count, 1);
}

static char*
_get_stack_trace_line_end (char *m)
{
	while (*m && *m != '\n')
		m++;
	return m;
}

static void
_write_stack_trace (FILE *to, char *from)
{
	char *n	= from;

	char c;
	do {
		char *m     = n;
		char *end   = _get_stack_trace_line_end (m);

		n       = end + 1;
		c       = *end;
		*end    = '\0';
		fprintf (to, "%s\n", m);
		fflush (to);
		*end    = c;
	} while (c);
}

MONO_API void
_monodroid_gref_log (const char *message)
{
	if (!gref_log)
		return;
	fprintf (gref_log, "%s", message);
	fflush (gref_log);
}

MONO_API int
_monodroid_gref_log_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, char *from, int from_writable)
{
	int c = _monodroid_gref_inc ();
	if ((log_categories & LOG_GREF) == 0)
		return c;
	log_info (LOG_GREF, "+g+ grefc %i gwrefc %i obj-handle %p/%c -> new-handle %p/%c from thread '%s'(%i)",
			c,
			gc_weak_gref_count,
			curHandle,
			curType,
			newHandle,
			newType,
			threadName,
			threadId);
	if (!gref_log)
		return c;
	fprintf (gref_log, "+g+ grefc %i gwrefc %i obj-handle %p/%c -> new-handle %p/%c from thread '%s'(%i)\n",
			c,
			gc_weak_gref_count,
			curHandle,
			curType,
			newHandle,
			newType,
			threadName,
			threadId);
	if (from_writable)
		_write_stack_trace (gref_log, from);
	else
		fprintf (gref_log, "%s\n", from);

	fflush (gref_log);

	return c;
}

MONO_API void
_monodroid_gref_log_delete (jobject handle, char type, const char *threadName, int threadId, char *from, int from_writable)
{
	int c = _monodroid_gref_dec ();
	if ((log_categories & LOG_GREF) == 0)
		return;
	log_info (LOG_GREF, "-g- grefc %i gwrefc %i handle %p/%c from thread '%s'(%i)",
			c,
			gc_weak_gref_count,
			handle,
			type,
			threadName,
			threadId);
	if (!gref_log)
		return;
	fprintf (gref_log, "-g- grefc %i gwrefc %i handle %p/%c from thread '%s'(%i)\n",
			c,
			gc_weak_gref_count,
			handle,
			type,
			threadName,
			threadId);
	if (from_writable)
		_write_stack_trace (gref_log, from);
	else
		fprintf (gref_log, "%s\n", from);

	fflush (gref_log);
}

MONO_API void
_monodroid_weak_gref_new (jobject curHandle, char curType, jobject newHandle, char newType, const char *threadName, int threadId, char *from, int from_writable)
{
	++gc_weak_gref_count;
	if ((log_categories & LOG_GREF) == 0)
		return;
	log_info (LOG_GREF, "+w+ grefc %i gwrefc %i obj-handle %p/%c -> new-handle %p/%c from thread '%s'(%i)",
			gc_gref_count,
			gc_weak_gref_count,
			curHandle,
			curType,
			newHandle,
			newType,
			threadName,
			threadId);
	if (!gref_log)
		return;
	fprintf (gref_log, "+w+ grefc %i gwrefc %i obj-handle %p/%c -> new-handle %p/%c from thread '%s'(%i)\n",
			gc_gref_count,
			gc_weak_gref_count,
			curHandle,
			curType,
			newHandle,
			newType,
			threadName,
			threadId);
	if (from_writable)
		_write_stack_trace (gref_log, from);
	else
		fprintf (gref_log, "%s\n", from);

	fflush (gref_log);
}

MONO_API void
_monodroid_weak_gref_delete (jobject handle, char type, const char *threadName, int threadId, char *from, int from_writable)
{
	--gc_weak_gref_count;
	if ((log_categories & LOG_GREF) == 0)
		return;
	log_info (LOG_GREF, "-w- grefc %i gwrefc %i handle %p/%c from thread '%s'(%i)",
			gc_gref_count,
			gc_weak_gref_count,
			handle,
			type,
			threadName,
			threadId);
	if (!gref_log)
		return;
	fprintf (gref_log, "-w- grefc %i gwrefc %i handle %p/%c from thread '%s'(%i)\n",
			gc_gref_count,
			gc_weak_gref_count,
			handle,
			type,
			threadName,
			threadId);
	if (from_writable)
		_write_stack_trace (gref_log, from);
	else
		fprintf (gref_log, "%s\n", from);

	fflush (gref_log);
}

MONO_API void
_monodroid_lref_log_new (int lrefc, jobject handle, char type, const char *threadName, int threadId, char *from, int from_writable)
{
	if ((log_categories & LOG_LREF) == 0)
		return;
	log_info (LOG_LREF, "+l+ lrefc %i handle %p/%c from thread '%s'(%i)",
			lrefc,
			handle,
			type,
			threadName,
			threadId);
	if (!lref_log)
		return;
	fprintf (lref_log, "+l+ lrefc %i handle %p/%c from thread '%s'(%i)\n",
			lrefc,
			handle,
			type,
			threadName,
			threadId);
	if (from_writable)
		_write_stack_trace (lref_log, from);
	else
		fprintf (lref_log, "%s\n", from);

	fflush (lref_log);
}

MONO_API void
_monodroid_lref_log_delete (int lrefc, jobject handle, char type, const char *threadName, int threadId, char *from, int from_writable)
{
	if ((log_categories & LOG_LREF) == 0)
		return;
	log_info (LOG_LREF, "-l- lrefc %i handle %p/%c from thread '%s'(%i)",
			lrefc,
			handle,
			type,
			threadName,
			threadId);
	if (!lref_log)
		return;
	fprintf (lref_log, "-l- lrefc %i handle %p/%c from thread '%s'(%i)\n",
			lrefc,
			handle,
			type,
			threadName,
			threadId);
	if (from_writable)
		_write_stack_trace (lref_log, from);
	else
		fprintf (lref_log, "%s\n", from);

	fflush (lref_log);
}

void monodroid_disable_gc_hooks ();

void
monodroid_disable_gc_hooks ()
{
	gc_disabled = 1;
}

#ifndef ANDROID
static pid_t gettid ()
{
#ifdef WINDOWS
	return GetCurrentThreadId ();
#elif defined (LINUX) // WINDOWS
	return syscall (SYS_gettid);
#else // LINUX
	uint64_t tid;
	pthread_threadid_np (NULL, &tid);
	return (pid_t)tid;
#endif
}
#endif // ANDROID

typedef mono_bool (*MonodroidGCTakeRefFunc) (JNIEnv *env, MonoObject *obj);

static MonodroidGCTakeRefFunc take_global_ref;
static MonodroidGCTakeRefFunc take_weak_global_ref;

static mono_bool
take_global_ref_2_1_compat (JNIEnv *env, MonoObject *obj)
{
	void *handle, *weak;
	int type = JNIGlobalRefType;

	MonoJavaGCBridgeInfo    *bridge_info    = get_gc_bridge_info_for_object (obj);
	if (bridge_info == NULL)
		return 0;

	mono.mono_field_get_value (obj, bridge_info->weak_handle, &weak);
	handle = (*env)->CallObjectMethod (env, weak, weakrefGet);
	if (gref_log) {
		fprintf (gref_log, "*try_take_global_2_1 obj=%p -> wref=%p handle=%p\n", obj, weak, handle);
		fflush (gref_log);
	}
	if (handle) {
		void* h = (*env)->NewGlobalRef (env, handle);
		(*env)->DeleteLocalRef (env, handle);
		handle = h;
		_monodroid_gref_log_new (weak, get_object_ref_type (env, weak),
				handle, get_object_ref_type (env, handle), "finalizer", gettid (), "take_global_ref_2_1_compat", 0);
	}
	_monodroid_weak_gref_delete (weak, get_object_ref_type (env, weak), "finalizer", gettid(), "take_global_ref_2_1_compat", 0);
	(*env)->DeleteGlobalRef (env, weak);
	weak = NULL;
	mono.mono_field_set_value (obj, bridge_info->weak_handle, &weak);

	mono.mono_field_set_value (obj, bridge_info->handle, &handle);
	mono.mono_field_set_value (obj, bridge_info->handle_type, &type);
	return handle != NULL;
}

static mono_bool
take_weak_global_ref_2_1_compat (JNIEnv *env, MonoObject *obj)
{
	jobject weaklocal;
	void *handle, *weakglobal;

	MonoJavaGCBridgeInfo    *bridge_info    = get_gc_bridge_info_for_object (obj);
	if (bridge_info == NULL)
		return 0;

	mono.mono_field_get_value (obj, bridge_info->handle, &handle);
	weaklocal = (*env)->NewObject (env, weakrefClass, weakrefCtor, handle);
	weakglobal = (*env)->NewGlobalRef (env, weaklocal);
	(*env)->DeleteLocalRef (env, weaklocal);
	if (gref_log) {
		fprintf (gref_log, "*take_weak_2_1 obj=%p -> wref=%p handle=%p\n", obj, weakglobal, handle);
		fflush (gref_log);
	}
	_monodroid_weak_gref_new (handle, get_object_ref_type (env, handle),
			weakglobal, get_object_ref_type (env, weakglobal), "finalizer", gettid (), "take_weak_global_ref_2_1_compat", 0);

	_monodroid_gref_log_delete (handle, get_object_ref_type (env, handle), "finalizer", gettid (), "take_weak_global_ref_2_1_compat", 0);

	(*env)->DeleteGlobalRef (env, handle);
	mono.mono_field_set_value (obj, bridge_info->weak_handle, &weakglobal);
	return 1;
}

static mono_bool
take_global_ref_jni (JNIEnv *env, MonoObject *obj)
{
	void *handle, *weak;
	int type = JNIGlobalRefType;

	MonoJavaGCBridgeInfo    *bridge_info    = get_gc_bridge_info_for_object (obj);
	if (bridge_info == NULL)
		return 0;

	mono.mono_field_get_value (obj, bridge_info->handle, &weak);
	handle = (*env)->NewGlobalRef (env, weak);
	if (gref_log) {
		fprintf (gref_log, "*try_take_global obj=%p -> wref=%p handle=%p\n", obj, weak, handle);
		fflush (gref_log);
	}
	if (handle) {
		_monodroid_gref_log_new (weak, get_object_ref_type (env, weak),
				handle, get_object_ref_type (env, handle),
				"finalizer", gettid (),
				"take_global_ref_jni", 0);
	}
	_monodroid_weak_gref_delete (weak, get_object_ref_type (env, weak),
			"finalizer", gettid (), "take_global_ref_jni", 0);
	(*env)->DeleteWeakGlobalRef (env, weak);
	if (!handle) {
		void *old_handle = NULL;

		mono.mono_field_get_value (obj, bridge_info->handle, &old_handle);
	}
	mono.mono_field_set_value (obj, bridge_info->handle, &handle);
	mono.mono_field_set_value (obj, bridge_info->handle_type, &type);
	return handle != NULL;
}

static mono_bool
take_weak_global_ref_jni (JNIEnv *env, MonoObject *obj)
{
	void *handle, *weak;
	int type = JNIWeakGlobalRefType;

	MonoJavaGCBridgeInfo    *bridge_info    = get_gc_bridge_info_for_object (obj);
	if (bridge_info == NULL)
		return 0;

	mono.mono_field_get_value (obj, bridge_info->handle, &handle);
	if (gref_log) {
		fprintf (gref_log, "*take_weak obj=%p; handle=%p\n", obj, handle);
		fflush (gref_log);
	}

	weak = (*env)->NewWeakGlobalRef (env, handle);
	_monodroid_weak_gref_new (handle, get_object_ref_type (env, handle), 
			weak, get_object_ref_type (env, weak),
			"finalizer", gettid (), "take_weak_global_ref_jni", 0);

	_monodroid_gref_log_delete (handle, get_object_ref_type (env, handle),
			"finalizer", gettid (), "take_weak_global_ref_jni", 0);
	(*env)->DeleteGlobalRef (env, handle);
	mono.mono_field_set_value (obj, bridge_info->handle, &weak);
	mono.mono_field_set_value (obj, bridge_info->handle_type, &type);
	return 1;
}

static JNIEnv*
ensure_jnienv (void)
{
	JNIEnv *env;
	(*jvm)->GetEnv (jvm, (void**)&env, JNI_VERSION_1_6);
	if (env == NULL) {
		mono.mono_thread_attach (mono.mono_domain_get ());
		(*jvm)->GetEnv (jvm, (void**)&env, JNI_VERSION_1_6);
	}
	return env;
}

JNIEnv*
get_jnienv (void)
{
	return ensure_jnienv ();
}

static MonoGCBridgeObjectKind
gc_bridge_class_kind (MonoClass *class)
{
	int i;
	if (gc_disabled)
		return GC_BRIDGE_TRANSPARENT_CLASS;

	i = get_gc_bridge_index (class);
	if (i == -NUM_GC_BRIDGE_TYPES) {
		log_info (LOG_GC, "asked if a class %s.%s is a bridge before we inited java.lang.Object", 
			mono.mono_class_get_namespace (class),
			mono.mono_class_get_name (class));
		return GC_BRIDGE_TRANSPARENT_CLASS;
	}

	if (i >= 0) {
		return GC_BRIDGE_TRANSPARENT_BRIDGE_CLASS;
	}

	return GC_BRIDGE_TRANSPARENT_CLASS;
}

static mono_bool
gc_is_bridge_object (MonoObject *object)
{
	void *handle;

	MonoJavaGCBridgeInfo    *bridge_info    = get_gc_bridge_info_for_object (object);
	if (bridge_info == NULL)
		return 0;

	mono.mono_field_get_value (object, bridge_info->handle, &handle);
	if (handle == NULL) {
#if DEBUG
		MonoClass *mclass = mono.mono_object_get_class (object);
		log_info (LOG_GC, "object of class %s.%s with null handle",
				mono.mono_class_get_namespace (mclass),
				mono.mono_class_get_name (mclass));
#endif
		return 0;
	}

	return 1;
}

// Add a reference from an IGCUserPeer jobject to another jobject
static mono_bool
add_reference_jobject (JNIEnv *env, jobject handle, jobject reffed_handle)
{
	jclass java_class;
	jmethodID add_method_id;

	java_class = (*env)->GetObjectClass (env, handle);
	add_method_id = (*env)->GetMethodID (env, java_class, "monodroidAddReference", "(Ljava/lang/Object;)V");
	if (add_method_id) {
		(*env)->CallVoidMethod (env, handle, add_method_id, reffed_handle);
		(*env)->DeleteLocalRef (env, java_class);

		return 1;
	}

	(*env)->ExceptionClear (env);
	(*env)->DeleteLocalRef (env, java_class);
	return 0;
}

// add_reference can work with objects which are either MonoObjects with java peers, or raw jobjects
typedef struct {
	mono_bool is_mono_object;
	union {
		MonoObject *obj;
		jobject jobj;
	};
} AddReferenceTarget;

// These will be loaded as needed and persist between GCs
// FIXME: This code assumes it is totally safe to hold onto these GREFs forever. Can mono.android.jar ever be unloaded?
static jobject   ArrayList_class, GCUserPeer_class;
static jmethodID ArrayList_ctor, ArrayList_get, ArrayList_add, GCUserPeer_ctor;

// Given a target, extract the bridge_info (if a mono object) and handle. Return success.
static mono_bool
load_reference_target (AddReferenceTarget target, MonoJavaGCBridgeInfo** bridge_info, jobject *handle)
{
	if (target.is_mono_object) {
		*bridge_info = get_gc_bridge_info_for_object (target.obj);
		if (!*bridge_info)
			return FALSE;
		mono.mono_field_get_value (target.obj, (*bridge_info)->handle, handle);
	} else {
		*handle = target.jobj;
	}
	return TRUE;
}

#if DEBUG
// Allocate and return a string describing a target
static char *
describe_target (AddReferenceTarget target)
{
	if (target.is_mono_object) {
		MonoClass *klass = mono.mono_object_get_class (target.obj);
		return monodroid_strdup_printf ("object of class %s.%s",
			mono.mono_class_get_namespace (klass),
			mono.mono_class_get_name (klass));
	}
	else
		return monodroid_strdup_printf ("<temporary object %p>", target.jobj);
}
#endif

// Add a reference from one target to another. If the "from" target is a mono_object, it must be a user peer
static mono_bool
add_reference (JNIEnv *env, AddReferenceTarget target, AddReferenceTarget reffed_target)
{
	MonoJavaGCBridgeInfo *bridge_info = NULL, *reffed_bridge_info = NULL;
	jobject handle, reffed_handle;

	if (!load_reference_target (target, &bridge_info, &handle))
		return FALSE;

	if (!load_reference_target (reffed_target, &reffed_bridge_info, &reffed_handle))
		return FALSE;

	mono_bool success = add_reference_jobject (env, handle, reffed_handle);

	// Flag MonoObjects so they can be cleared in gc_cleanup_after_java_collection.
	// Java temporaries do not need this because the entire GCUserPeer is discarded.
	if (success && target.is_mono_object) {
		int ref_val = 1;
		mono.mono_field_set_value (target.obj, bridge_info->refs_added, &ref_val);
	}

#if DEBUG
	if (gc_spew_enabled) {
		char *description = describe_target (target),
			 *reffed_description = describe_target (reffed_target);

		if (success)
			log_info (LOG_GC, "Added reference for %s to %s", description, reffed_description);
		else
			log_error (LOG_GC, "Missing monodroidAddReference method for %s", description);

		free (description);
		free (reffed_description);
	}
#endif

	return success;
}

// Create a target
static AddReferenceTarget
target_from_mono_object (MonoObject *obj)
{
	AddReferenceTarget result;
	result.is_mono_object = TRUE;
	result.obj = obj;
	return result;
}

// Create a target
static AddReferenceTarget
target_from_jobject (jobject jobj)
{
	AddReferenceTarget result;
	result.is_mono_object = FALSE;
	result.jobj = jobj;
	return result;
}

/* During the xref phase of gc_prepare_for_java_collection, we need to be able to map bridgeless
 * SCCs to their index in temporary_peers. Because for all bridgeless SCCs the num_objs field of
 * MonoGCBridgeSCC is known 0, we can temporarily stash this index as a negative value in the SCC
 * object. This does mean we have to erase our vandalism at the end of the function.
 */
static int
scc_get_stashed_index (MonoGCBridgeSCC *scc)
{
	assert ( (scc->num_objs < 0) || !"Attempted to load stashed index from an object which does not contain one." );
	return -scc->num_objs - 1;
}

static void
scc_set_stashed_index (MonoGCBridgeSCC *scc, int index)
{
	scc->num_objs = -index - 1;
}

// Extract the root target for an SCC. If the SCC has bridged objects, this is the first object. If not, it's stored in temporary_peers.
static AddReferenceTarget
target_from_scc (MonoGCBridgeSCC **sccs, int idx, JNIEnv *env, jobject temporary_peers)
{
	MonoGCBridgeSCC *scc = sccs [idx];
	if (scc->num_objs > 0)
		return target_from_mono_object (scc->objs [0]);

	jobject jobj = (*env)->CallObjectMethod (env, temporary_peers, ArrayList_get, scc_get_stashed_index (scc));
	return target_from_jobject (jobj);
}

// Must call this on any AddReferenceTarget returned by target_from_scc once done with it
static void
target_release (JNIEnv *env, AddReferenceTarget target)
{
	if (!target.is_mono_object)
		(*env)->DeleteLocalRef (env, target.jobj);
}

// Add a reference between objects if both are already known to be MonoObjects which are user peers
static mono_bool
add_reference_mono_object (JNIEnv *env, MonoObject *obj, MonoObject *reffed_obj)
{
	return add_reference (env, target_from_mono_object (obj), target_from_mono_object (reffed_obj));
}

static void
gc_prepare_for_java_collection (JNIEnv *env, int num_sccs, MonoGCBridgeSCC **sccs, int num_xrefs, MonoGCBridgeXRef *xrefs)
{
	/* Some SCCs might have no IGCUserPeers associated with them, so we must create one */
	jobject temporary_peers = NULL;     // This is an ArrayList
	int temporary_peer_count = 0;       // Number of items in temporary_peers

	/* Before looking at xrefs, scan the SCCs. During collection, an SCC has to behave like a
	 * single object. If the number of objects in the SCC is anything other than 1, the SCC
	 * must be doctored to mimic that one-object nature.
	 */
	for (int i = 0; i < num_sccs; i++) {
		MonoGCBridgeSCC *scc = sccs [i];

		/* num_objs < 0 case: This is a violation of the bridge API invariants. */
		assert ( (scc->num_objs >= 0) || !"Bridge processor submitted an SCC with a negative number of objects." );

		/* num_objs > 1 case: The SCC contains many objects which must be collected as one.
		 * Solution: Make all objects within the SCC directly or indirectly reference each other
		 */
		if (scc->num_objs > 1) {
			MonoGCBridgeSCC *first = scc->objs [0];
			MonoGCBridgeSCC *prev = first;

			/* start at the second item, ref j from j-1 */
			for (int j = 1; j < scc->num_objs; j++) {
				MonoGCBridgeSCC *current = scc->objs [j];

				add_reference_mono_object (env, prev, current);
				prev = current;
			}

			/* ref the first from the final */
			add_reference_mono_object (env, prev, first);

		/* num_objs == 0 case: The SCC contains *no* objects (or rather contains only C# objects).
		 * Solution: Create a temporary Java object to stand in for the SCC.
		 */
		} else if (scc->num_objs == 0) {
			/* Once per process boot, look up JNI metadata we need to make temporary objects */
			if (!ArrayList_class) {
				ArrayList_class = lref_to_gref (env, (*env)->FindClass (env, "java/util/ArrayList"));
				ArrayList_ctor = (*env)->GetMethodID (env, ArrayList_class, "<init>", "()V");
				ArrayList_add = (*env)->GetMethodID (env, ArrayList_class, "add", "(Ljava/lang/Object;)Z");
				ArrayList_get = (*env)->GetMethodID (env, ArrayList_class, "get", "(I)Ljava/lang/Object;");

				assert ( (ArrayList_class && ArrayList_ctor && ArrayList_get) || !"Failed to load classes required for JNI" );
			}

			/* Once per gc_prepare_for_java_collection call, create a list to hold the temporary
			 * objects we create. This will protect them from collection while we build the list.
			 */
			if (!temporary_peers) {
				temporary_peers = (*env)->NewObject (env, ArrayList_class, ArrayList_ctor);
			}

			/* Create this SCC's temporary object */
			jobject peer = (*env)->NewObject (env, GCUserPeer_class, GCUserPeer_ctor);
			(*env)->CallBooleanMethod (env, temporary_peers, ArrayList_add, peer);
			(*env)->DeleteLocalRef (env, peer);

			/* See note on scc_get_stashed_index */
			scc_set_stashed_index (scc, temporary_peer_count);
			temporary_peer_count++;
		}
	}

	/* add the cross scc refs */
	for (int i = 0; i < num_xrefs; i++) {
		AddReferenceTarget src_target = target_from_scc (sccs, xrefs [i].src_scc_index, env, temporary_peers);
		AddReferenceTarget dst_target = target_from_scc (sccs, xrefs [i].dst_scc_index, env, temporary_peers);

		add_reference (env, src_target, dst_target);

		target_release (env, src_target);
		target_release (env, dst_target);
	}

	/* With xrefs processed, the temporary peer list can be released */
	(*env)->DeleteLocalRef (env, temporary_peers);

	/* Post-xref cleanup on SCCs: Undo memoization, switch to weak refs */
	for (int i = 0; i < num_sccs; i++) {
		/* See note on scc_get_stashed_index */
		if (sccs [i]->num_objs < 0)
			sccs [i]->num_objs = 0;

		for (int j = 0; j < sccs [i]->num_objs; j++) {
			take_weak_global_ref (env, sccs [i]->objs [j]);
		}
	}
}

static void
gc_cleanup_after_java_collection (JNIEnv *env, int num_sccs, MonoGCBridgeSCC **sccs)
{
#if DEBUG
	MonoClass *klass;
#endif
	MonoObject *obj;
	jclass java_class;
	jobject jref;
	jmethodID clear_method_id;
	int i, j, total, alive, refs_added;

	total = alive = 0;

	/* try to switch back to global refs to analyze what stayed alive */
	for (i = 0; i < num_sccs; i++)
		for (j = 0; j < sccs [i]->num_objs; j++, total++)
			take_global_ref (env, sccs [i]->objs [j]);

	/* clear the cross references on any remaining items */
	for (i = 0; i < num_sccs; i++) {
		sccs [i]->is_alive = 0;

		for (j = 0; j < sccs [i]->num_objs; j++) {
			MonoJavaGCBridgeInfo    *bridge_info;

			obj = sccs [i]->objs [j];

			bridge_info = get_gc_bridge_info_for_object (obj);
			if (bridge_info == NULL)
				continue;
			mono.mono_field_get_value (obj, bridge_info->handle, &jref);
			if (jref) {
				alive++;
				if (j > 0)
					assert (sccs [i]->is_alive);
				sccs [i]->is_alive = 1;
				mono.mono_field_get_value (obj, bridge_info->refs_added, &refs_added);
				if (refs_added) {
					java_class = (*env)->GetObjectClass (env, jref);
					clear_method_id = (*env)->GetMethodID (env, java_class, "monodroidClearReferences", "()V");
					if (clear_method_id) {
						(*env)->CallVoidMethod (env, jref, clear_method_id);
					} else {
						(*env)->ExceptionClear (env);
#if DEBUG
						if (gc_spew_enabled) {
							klass = mono.mono_object_get_class (obj);
							log_error (LOG_GC, "Missing monodroidClearReferences method for object of class %s.%s",
									mono.mono_class_get_namespace (klass),
									mono.mono_class_get_name (klass));
						}
#endif
					}
					(*env)->DeleteLocalRef (env, java_class);
				}
			} else {
				assert (!sccs [i]->is_alive);
			}
		}
	}
#if DEBUG
	log_info (LOG_GC, "GC cleanup summary: %d objects tested - resurrecting %d.", total, alive);
#endif
}

static void
java_gc (JNIEnv *env)
{
	(*env)->CallVoidMethod (env, Runtime_instance, Runtime_gc);
}

/* The context (mapping to a Mono AppDomain) that is currently selected as the
 * active context from the point of view of Java. We cannot rely on the value
 * of `mono_domain_get` for this as it's stored per-thread and we want to be
 * able to switch our different contexts from different threads.
 */
static int current_context_id = -1;

struct MonodroidBridgeProcessingInfo {
	MonoDomain *domain;
	MonoClassField *bridge_processing_field;
	MonoVTable *jnienv_vtable;

	struct MonodroidBridgeProcessingInfo* next;
};

typedef struct MonodroidBridgeProcessingInfo MonodroidBridgeProcessingInfo;
MonodroidBridgeProcessingInfo *domains_list;

static void
add_monodroid_domain (MonoDomain *domain)
{
	MonodroidBridgeProcessingInfo *node = calloc (1, sizeof (MonodroidBridgeProcessingInfo));

	/* We need to prefetch all these information prior to using them in gc_cross_reference as all those functions
	 * use GC API to allocate memory and thus can't be called from within the GC callback as it causes a deadlock
	 * (the routine allocating the memory waits for the GC round to complete first)
	 */
	MonoClass *jnienv = monodroid_get_class_from_name (&mono, domain, "Mono.Android", "Android.Runtime", "JNIEnv");;
	node->domain = domain;
	node->bridge_processing_field = mono.mono_class_get_field_from_name (jnienv, "BridgeProcessing");
	node->jnienv_vtable = mono.mono_class_vtable (domain, jnienv);
	node->next = domains_list;

	domains_list = node;
}

static void
remove_monodroid_domain (MonoDomain *domain)
{
	MonodroidBridgeProcessingInfo *node = domains_list;
	MonodroidBridgeProcessingInfo *prev = NULL;

	while (node != NULL) {
		if (node->domain != domain) {
			prev = node;
			node = node->next;
			continue;
		}

		if (prev != NULL)
			prev->next = node->next;
		else
			domains_list = node->next;

		free (node);

		break;
	}
}

static void
set_bridge_processing_field (MonodroidBridgeProcessingInfo *list, mono_bool value)
{
	for ( ; list != NULL; list = list->next) {
		MonoClassField *bridge_processing_field = list->bridge_processing_field;
		MonoVTable *jnienv_vtable = list->jnienv_vtable;
		mono.mono_field_static_set_value (jnienv_vtable, bridge_processing_field, &value);
	}
}

static void
gc_cross_references (int num_sccs, MonoGCBridgeSCC **sccs, int num_xrefs, MonoGCBridgeXRef *xrefs)
{
	JNIEnv *env;

	if (gc_disabled)
		return;

#if DEBUG
	if (gc_spew_enabled) {
		int i, j;
		log_info (LOG_GC, "cross references callback invoked with %d sccs and %d xrefs.", num_sccs, num_xrefs);

		for (i = 0; i < num_sccs; ++i) {
			log_info (LOG_GC, "group %d with %d objects", i, sccs [i]->num_objs);
			for (j = 0; j < sccs [i]->num_objs; ++j) {
				MonoObject *obj = sccs [i]->objs [j];
				MonoClass *klass = mono.mono_object_get_class (obj);
				log_info (LOG_GC, "\tobj %p [%s::%s]",
						obj,
						mono.mono_class_get_namespace (klass),
						mono.mono_class_get_name (klass));
			}
		}

		for (i = 0; i < num_xrefs; ++i)
			log_info (LOG_GC, "xref [%d] %d -> %d", i, xrefs [i].src_scc_index, xrefs [i].dst_scc_index);
	}
#endif
	
	env = ensure_jnienv ();

	set_bridge_processing_field (domains_list, 1);
	gc_prepare_for_java_collection (env, num_sccs, sccs, num_xrefs, xrefs);

	java_gc (env);

	gc_cleanup_after_java_collection (env, num_sccs, sccs);
	set_bridge_processing_field (domains_list, 0);
}

static int
platform_supports_weak_refs (void)
{
	char *value;
	int api_level = 0;

	if (monodroid_get_system_property ("ro.build.version.sdk", &value) > 0) {
		api_level = atoi (value);
		free (value);
	}

	if (monodroid_get_namespaced_system_property (DEBUG_MONO_WREF_PROPERTY, &value) > 0) {
		int use_weak_refs = 0;
		if (!strcmp ("jni", value))
			use_weak_refs = 1;
		else if (!strcmp ("java", value))
			use_weak_refs = 0;
		else {
			use_weak_refs = -1;
			log_warn (LOG_GC, "Unsupported debug.mono.wref value '%s'; "
					"supported values are 'jni' and 'java'. Ignoring...",
					value);
		}
		free (value);

		if (use_weak_refs && api_level < 8)
			log_warn (LOG_GC, "Using JNI weak references instead of "
					"java.lang.WeakReference on API-%i. Are you sure you want to do this? "
					"The GC may be compromised.",
					api_level);

		if (use_weak_refs >= 0)
			return use_weak_refs;
	}

	if (monodroid_get_namespaced_system_property ("persist.sys.dalvik.vm.lib", &value) > 0) {
		int art = 0;
		if (!strcmp ("libart.so", value))
			art = 1;
		free (value);
		if (art) {
			int use_java = 0;
			if (monodroid_get_namespaced_system_property ("ro.build.version.release", &value) > 0) {
				// Android 4.x ART is busted; see https://code.google.com/p/android/issues/detail?id=63929
				if (value [0] != 0 && value [0] == '4' && value [1] != 0 && value [1] == '.') {
					use_java = 1;
				}
				free (value);
			}
			if (use_java) {
				log_warn (LOG_GC, "JNI weak global refs are broken on Android with the ART runtime.");
				log_warn (LOG_GC, "Trying to use java.lang.WeakReference instead, but this may fail as well.");
				log_warn (LOG_GC, "App stability may be compromised.");
				log_warn (LOG_GC, "See: https://code.google.com/p/android/issues/detail?id=63929");
				return 0;
			}
		}
	}

	if (api_level > 7)
		return 1;
	return 0;
}

static void
register_gc_hooks (void)
{
	MonoGCBridgeCallbacks bridge_cbs;

	if (platform_supports_weak_refs ()) {
		take_global_ref = take_global_ref_jni;
		take_weak_global_ref = take_weak_global_ref_jni;
		log_info (LOG_GC, "environment supports jni NewWeakGlobalRef");
	} else {
		take_global_ref = take_global_ref_2_1_compat;
		take_weak_global_ref = take_weak_global_ref_2_1_compat;
		log_info (LOG_GC, "environment does not support jni NewWeakGlobalRef");
	}

	bridge_cbs.bridge_version = SGEN_BRIDGE_VERSION;
	bridge_cbs.bridge_class_kind = gc_bridge_class_kind;
	bridge_cbs.is_bridge_object = gc_is_bridge_object;
	bridge_cbs.cross_references = gc_cross_references;
	mono.mono_gc_register_bridge_callbacks (&bridge_cbs);
}

static void
thread_start (MonoProfiler *prof, uintptr_t tid)
{
	JNIEnv* env;
	int r;
#ifdef PLATFORM_ANDROID
	r = (*jvm)->AttachCurrentThread (jvm, &env, NULL);
#else   // ndef PLATFORM_ANDROID
	r = (*jvm)->AttachCurrentThread (jvm, (void**) &env, NULL);
#endif  // ndef PLATFORM_ANDROID
	if (r != JNI_OK) {
#if DEBUG
		log_fatal (LOG_DEFAULT, "ERROR: Unable to attach current thread to the Java VM!");
		exit (FATAL_EXIT_ATTACH_JVM_FAILED);
#endif
	}
}

static void
thread_end (MonoProfiler *prof, uintptr_t tid)
{
	int r;
	r = (*jvm)->DetachCurrentThread (jvm);
	if (r != JNI_OK) {
#if DEBUG
		/*
		log_fatal (LOG_DEFAULT, "ERROR: Unable to detach current thread from the Java VM!");
		 */
#endif
	}
}

FILE *jit_log;

static void
jit_end (MonoProfiler *prof, MonoMethod   *method,   MonoJitInfo* jinfo,   int result)
{
	char *name;
	if (!jit_log)
		return;
	if (result != MONO_PROFILE_OK)
		return;
	name = mono.mono_method_full_name (method, 1);
	fprintf (jit_log, "JITed method: %s\n", name);
	free (name);
}

#ifndef RELEASE
static MonoAssembly*
open_from_update_dir (MonoAssemblyName *aname, char **assemblies_path, void *user_data)
{
	int fi, oi;
	MonoAssembly *result = NULL;
	int found = 0;
	const char *culture = mono.mono_assembly_name_get_culture (aname);
	const char *name    = mono.mono_assembly_name_get_name (aname);
	char *pname;

	for (oi = 0; oi < MAX_OVERRIDES; ++oi)
		if (override_dirs [oi] != NULL && directory_exists (override_dirs [oi]))
			found = 1;
	if (!found)
		return NULL;

	if (culture != NULL && strlen (culture) > 0)
		pname = path_combine (culture, name);
	else
		pname = monodroid_strdup_printf ("%s", name);

	static const char *formats[] = {
		"%s" MONODROID_PATH_SEPARATOR "%s",
		"%s" MONODROID_PATH_SEPARATOR "%s.dll",
		"%s" MONODROID_PATH_SEPARATOR "%s.exe",
	};

	for (fi = 0; fi < sizeof (formats)/sizeof (formats [0]) && result == NULL; ++fi) {
		for (oi = 0; oi < MAX_OVERRIDES; ++oi) {
			char *fullpath;
			if (override_dirs [oi] == NULL || !directory_exists (override_dirs [oi]))
				continue;
			fullpath = monodroid_strdup_printf (formats [fi], override_dirs [oi], pname);
			log_info (LOG_ASSEMBLY, "open_from_update_dir: trying to open assembly: %s\n", fullpath);
			if (file_exists (fullpath))
				result = mono.mono_assembly_open_full (fullpath, NULL, 0);
			free (fullpath);
			if (result) {
				// TODO: register .mdb, .pdb file
				break;
			}
		}
	}
	free (pname);
	if (result) {
		log_info (LOG_ASSEMBLY, "open_from_update_dir: loaded assembly: %p\n", result);
	}
	return result;
}
#endif

static long long
current_time_millis (void)
{
	struct timeval tv;

	gettimeofday(&tv, (struct timezone *) NULL);
	long long when = tv.tv_sec * 1000LL + tv.tv_usec / 1000;
	return when;
}

static int
count_override_assemblies (void)
{
	int c = 0;
	int i;

	for (i = 0; i < MAX_OVERRIDES; ++i) {
		monodroid_dir_t *dir;
		monodroid_dirent_t b, *e;

		const char *dir_path = override_dirs [i];

		if (dir_path == NULL || !directory_exists (dir_path))
			continue;

		if ((dir = monodroid_opendir (dir_path)) == NULL)
			continue;

		while (readdir_r (dir, &b, &e) == 0 && e) {
			if (monodroid_dirent_hasextension (e, ".dll"))
				++c;
		}
		monodroid_closedir (dir);
	}

	return c;
}

static int
should_register_file (const char *filename, void *user_data)
{
#ifndef RELEASE
	int i;
	for (i = 0; i < MAX_OVERRIDES; ++i) {
		int exists;
		char *p;

		const char *odir = override_dirs [i];
		if (odir == NULL)
			continue;

		p       = path_combine (odir, filename);
		exists  = file_exists (p);
		free (p);

		if (exists) {
			log_info (LOG_ASSEMBLY, "should not register '%s' as it exists in the override directory '%s'", filename, odir);
			return !exists;
		}
	}
#endif
	return 1;
}

static void
gather_bundled_assemblies (JNIEnv *env, jobjectArray runtimeApks, mono_bool register_debug_symbols, int *out_user_assemblies_count)
{
	jsize i;
	int   prev_num_assemblies = 0;
	jsize apksLength          = (*env)->GetArrayLength (env, runtimeApks);

	monodroid_embedded_assemblies_set_register_debug_symbols (register_debug_symbols);
	monodroid_embedded_assemblies_set_should_register (should_register_file, NULL);

	for (i = apksLength - 1; i >= 0; --i) {
		int          cur_num_assemblies;
		const char  *apk_file;
		jstring      apk = (*env)->GetObjectArrayElement (env, runtimeApks, i);

		apk_file = (*env)->GetStringUTFChars (env, apk, NULL);

		cur_num_assemblies  = monodroid_embedded_assemblies_register_from (&mono, apk_file);

		if (strstr (apk_file, "/Mono.Android.DebugRuntime") == NULL &&
				strstr (apk_file, "/Mono.Android.Platform.ApiLevel_") == NULL)
			*out_user_assemblies_count += (cur_num_assemblies - prev_num_assemblies);
		prev_num_assemblies = cur_num_assemblies;

		(*env)->ReleaseStringUTFChars (env, apk, apk_file);
	}
}

#if defined (DEBUG) && !defined (WINDOWS)
int monodroid_debug_connect (int sock, struct sockaddr_in addr) {
	long flags = 0;
	int res = 0;
	fd_set fds;
	struct timeval tv;
	socklen_t len;
	int val = 0;

	flags = fcntl (sock, F_GETFL, NULL);
	flags |= O_NONBLOCK;
	fcntl (sock, F_SETFL, flags);

	res = connect (sock, (struct sockaddr *) &addr, sizeof (addr));

	if (res < 0) {
		if (errno == EINPROGRESS) {
			while (1) {
				tv.tv_sec = 2;
				tv.tv_usec = 0;
				FD_ZERO (&fds);
				FD_SET (sock, &fds);

				res = select (sock + 1, 0, &fds, 0, &tv);

				if (res <= 0 && errno != EINTR) return -5;

				len = sizeof (int);

				if (getsockopt (sock, SOL_SOCKET, SO_ERROR, &val, &len) < 0) return -3;

				if (val) return -4;

				break;
			}
		} else {
			return -2;
		}
	}

	flags = fcntl (sock, F_GETFL, NULL);
	flags &= (~O_NONBLOCK);
	fcntl (sock, F_SETFL, flags);

	return 1;
}

int
monodroid_debug_accept (int sock, struct sockaddr_in addr)
{
	char handshake_msg [128];
	int res, accepted;

	res = bind (sock, (struct sockaddr *) &addr, sizeof (addr));
	if (res < 0)
		return -1;

	res = listen (sock, 1);
	if (res < 0)
		return -2;

	accepted = accept (sock, NULL, NULL);
	if (accepted < 0)
		return -3;

	sprintf (handshake_msg, "MonoDroid-Handshake\n");
	do {
		res = send (accepted, handshake_msg, strlen (handshake_msg), 0);
	} while (res == -1 && errno == EINTR);
	if (res < 0)
		return -4;

	return accepted;
}
#endif

#define HARDWARE_TYPE     "ro.hardware"
#define HARDWARE_EMULATOR "goldfish"

static int
get_max_gref_count (void)
{
	int max;
	char value [PROP_VALUE_MAX+1];
	char *override;
	int len;

	len = _monodroid__system_property_get (HARDWARE_TYPE, value, sizeof (value));
	if (len > 0 && strcmp (value, HARDWARE_EMULATOR) == 0) {
		max = 2000;
	} else {
		max = 51200;
	}

	if (monodroid_get_namespaced_system_property (DEBUG_MONO_MAX_GREFC, &override) > 0) {
		char *e;
		max       = strtol (override, &e, 10);
		switch (*e) {
		case 'k':
			e++;
			max *= 1000;
			break;
		case 'm':
			e++;
			max *= 1000000;
			break;
		}
		if (max < 0)
			max = INT_MAX;
		if (*e) {
			log_warn (LOG_GC, "Unsupported '%s' value '%s'.", DEBUG_MONO_MAX_GREFC, override);
		}
		log_info (LOG_GC, "Overriding max JNI Global Reference count to %i", max);
		free (override);
	}
	return max;
}

JNIEXPORT jint JNICALL
JNI_OnLoad (JavaVM *vm, void *reserved)
{
	JNIEnv *env;
	jobject lref;
	jmethodID Runtime_getRuntime;

	max_gref_count = get_max_gref_count ();

	jvm = vm;

	(*jvm)->GetEnv (jvm, (void**)&env, JNI_VERSION_1_6);
	lref                = (*env)->FindClass (env, "java/lang/Runtime");
	Runtime_getRuntime  = (*env)->GetStaticMethodID (env, lref, "getRuntime", "()Ljava/lang/Runtime;");
	Runtime_gc          = (*env)->GetMethodID (env, lref, "gc", "()V");
	Runtime_instance    = lref_to_gref (env, (*env)->CallStaticObjectMethod (env, lref, Runtime_getRuntime));
	(*env)->DeleteLocalRef (env, lref);
	lref = (*env)->FindClass (env, "java/lang/ref/WeakReference");
	weakrefClass = (*env)->NewGlobalRef (env, lref);
	(*env)->DeleteLocalRef (env, lref);
	weakrefCtor = (*env)->GetMethodID (env, weakrefClass, "<init>", "(Ljava/lang/Object;)V");
	weakrefGet = (*env)->GetMethodID (env, weakrefClass, "get", "()Ljava/lang/Object;");

	TimeZone_class      = lref_to_gref (env, (*env)->FindClass (env, "java/util/TimeZone"));
	if (!TimeZone_class) {
		log_fatal (LOG_DEFAULT, "Fatal error: Could not find java.util.TimeZone class!");
		exit (FATAL_EXIT_MISSING_TIMEZONE_MEMBERS);
	}

	TimeZone_getDefault = (*env)->GetStaticMethodID (env, TimeZone_class, "getDefault", "()Ljava/util/TimeZone;");
	if (!TimeZone_getDefault) {
		log_fatal (LOG_DEFAULT, "Fatal error: Could not find java.util.TimeZone.getDefault() method!");
		exit (FATAL_EXIT_MISSING_TIMEZONE_MEMBERS);
	}

	TimeZone_getID      = (*env)->GetMethodID (env, TimeZone_class, "getID",      "()Ljava/lang/String;");
	if (!TimeZone_getID) {
		log_fatal (LOG_DEFAULT, "Fatal error: Could not find java.util.TimeZone.getDefault() method!");
		exit (FATAL_EXIT_MISSING_TIMEZONE_MEMBERS);
	}

	/* When running on Android, as per http://developer.android.com/reference/java/lang/System.html#getProperty(java.lang.String)
	 * the value of java.version is deemed "(Not useful on Android)" and is hardcoded to return zero. We can thus use this fact
	 * to distinguish between running on a normal JVM and an Android VM.
	 */
	jclass System_class = (*env)->FindClass (env, "java/lang/System");
	jmethodID System_getProperty = (*env)->GetStaticMethodID (env, System_class, "getProperty", "(Ljava/lang/String;)Ljava/lang/String;");
	jstring System_javaVersionArg = (*env)->NewStringUTF (env, "java.version");
	jstring System_javaVersion = (*env)->CallStaticObjectMethod (env, System_class, System_getProperty, System_javaVersionArg);
	const char* javaVersion = (*env)->GetStringUTFChars (env, System_javaVersion, NULL);
	is_running_on_desktop = atoi (javaVersion) != 0;
	(*env)->ReleaseStringUTFChars (env, System_javaVersion, javaVersion);
	(*env)->DeleteLocalRef (env, System_javaVersionArg);
	(*env)->DeleteLocalRef (env, System_javaVersion);
	(*env)->DeleteLocalRef (env, System_class);

	return JNI_VERSION_1_6;
}

static void
parse_gdb_options (void)
{
	char *val;

	if (!(monodroid_get_namespaced_system_property (DEBUG_MONO_GDB_PROPERTY, &val) > 0))
		return;

	if (strstr (val, "wait:") == val) {
		/*
		 * The form of the property should be: 'wait:<timestamp>', where <timestamp> should be
		 * the output of date +%s in the android shell.
		 * If this property is set, wait for a native debugger to attach by spinning in a loop.
		 * The debugger can break the wait by setting 'monodroid_gdb_wait' to 0.
		 * If the current time is later than <timestamp> + 10s, the property is ignored.
		 */
		long long v;
		int do_wait = TRUE;

		v = atoll (val + strlen ("wait:"));
		if (v > 100000) {
			time_t secs = time (NULL);

			if (v + 10 < secs) {
				log_warn (LOG_DEFAULT, "Found stale %s property with value '%s', not waiting.", DEBUG_MONO_GDB_PROPERTY, val);
				do_wait = FALSE;
			}
		}

		wait_for_gdb = do_wait;
	}

	free (val);
}

#if DEBUG
typedef struct {
	int debug;
	int loglevel;
	int64_t timeout_time;
	char *host;
	int sdb_port, out_port;
	mono_bool server;
} RuntimeOptions;

static int
parse_runtime_args (char *runtime_args, RuntimeOptions *options)
{
	char **args, **ptr;

	if (!runtime_args)
		return 1;

	options->timeout_time = 0;

	args = monodroid_strsplit (runtime_args, ",", -1);

	for (ptr = args; ptr && *ptr; ptr++) {
		const char *arg = *ptr;

		if (!strncmp (arg, "debug", 5)) {
			char *host = NULL;
			int sdb_port = 1000, out_port = -1;

			options->debug = 1;

			if (arg[5] == '=') {
				char *sep, *endp;

				arg += 6;
				sep = strchr (arg, ':');
				if (sep) {
					host = xmalloc (sep-arg+1);
					memset (host, 0x00, sep-arg+1);
					strncpy (host, arg, sep-arg);
					arg = sep+1;

					sdb_port = (int) strtol (arg, &endp, 10);
					if (endp == arg) {
						log_error (LOG_DEFAULT, "Invalid --debug argument.");
						continue;
					} else if (*endp == ':') {
						arg = endp+1;
						out_port = (int) strtol (arg, &endp, 10);
						if ((endp == arg) || (*endp != '\0')) {
							log_error (LOG_DEFAULT, "Invalid --debug argument.");
							continue;
						}
					} else if (*endp != '\0') {
						log_error (LOG_DEFAULT, "Invalid --debug argument.");
						continue;
					}
				}
			} else if (arg[5] != '\0') {
				log_error (LOG_DEFAULT, "Invalid --debug argument.");
				continue;
			}

			if (!host)
				host = monodroid_strdup_printf ("10.0.2.2");

			options->host = host;
			options->sdb_port = sdb_port;
			options->out_port = out_port;
		} else if (!strncmp (arg, "timeout=", 8)) {
			char *endp;

			arg += sizeof ("timeout");
			options->timeout_time = strtoll (arg, &endp, 10);
			if ((endp == arg) || (*endp != '\0'))
				log_error (LOG_DEFAULT, "Invalid --timeout argument.");
			continue;
		} else if (!strncmp (arg, "server=", 7)) {
			arg += sizeof ("server");
			options->server = *arg == 'y' || *arg == 'Y';
			continue;
		} else if (!strncmp (arg, "loglevel=", 9)) {
			char *endp;

			arg += sizeof ("loglevel");
			options->loglevel = strtoll (arg, &endp, 10);
			if ((endp == arg) || (*endp != '\0'))
				log_error (LOG_DEFAULT, "Invalid --loglevel argument.");
		} else {
				log_error (LOG_DEFAULT, "Unknown runtime argument: '%s'", arg);
			continue;
		}
	}

	monodroid_strfreev (args);
	return 1;
}
#endif  // def DEBUG

static void
load_assembly (MonoDomain *domain, JNIEnv *env, jstring assembly)
{
	const char *assm_name;
	MonoAssemblyName *aname;

	assm_name = (*env)->GetStringUTFChars (env, assembly, NULL);
	aname = mono.mono_assembly_name_new (assm_name);
	(*env)->ReleaseStringUTFChars (env, assembly, assm_name);

	if (domain != mono.mono_domain_get ()) {
		MonoDomain *current = mono.mono_domain_get ();
		mono.mono_domain_set (domain, FALSE);
		mono.mono_assembly_load_full (aname, NULL, NULL, 0);
		mono.mono_domain_set (current, FALSE);
	} else {
		mono.mono_assembly_load_full (aname, NULL, NULL, 0);
	}

	mono.mono_assembly_name_free (aname);
}

static void
set_debug_options (void)
{
	if (monodroid_get_namespaced_system_property (DEBUG_MONO_DEBUG_PROPERTY, NULL) == 0)
		return;

	register_debug_symbols = 1;
	mono.mono_debug_init (MONO_DEBUG_FORMAT_MONO);
}

#ifdef ANDROID
static const char *soft_breakpoint_kernel_list[] = {
	"2.6.32.21-g1e30168", NULL
};

#ifdef DEBUG
static int
enable_soft_breakpoints (void)
{
	struct utsname name;
	const char **ptr;
	char *value;

	/* This check is to make debugging work on some old Samsung device which had
	 * a patched kernel that would abort the application after several segfaults
	 * (with the SIGSEGV being used for single-stepping in Mono)
	*/
	uname (&name);
	for (ptr = soft_breakpoint_kernel_list; *ptr; ptr++) {
		if (!strcmp (name.release, *ptr)) {
			log_info (LOG_DEBUGGER, "soft breakpoints enabled due to kernel version match (%s)", name.release);
			return 1;
		}
	}

	/* Soft breakpoints are enabled by default */
	if (monodroid_get_namespaced_system_property (DEBUG_MONO_SOFT_BREAKPOINTS, &value) <= 0) {
		log_info (LOG_DEBUGGER, "soft breakpoints enabled by default (%s property not defined)", DEBUG_MONO_SOFT_BREAKPOINTS);
		return 1;
	}

	if (strcmp ("0", value) == 0) {
		log_info (LOG_DEBUGGER, "soft breakpoints disabled (%s property set to %s)", DEBUG_MONO_SOFT_BREAKPOINTS, value);
		return 0;
	}
	
	log_info (LOG_DEBUGGER, "soft breakpoints enabled (%s property set to %s)", DEBUG_MONO_SOFT_BREAKPOINTS, value);
	return 1;
}
#endif /* DEBUG */

void
set_world_accessable (const char *path)
{
	int r;
	do
		r = chmod (path, 0664);
	while (r == -1 && errno == EINTR);

	if (r == -1)
		log_error (LOG_DEFAULT, "chmod(\"%s\", 0664) failed: %s", path, strerror (errno));
}

static void
set_user_executable (const char *path)
{
	int r;
	do
		r = chmod (path, S_IRUSR | S_IWUSR | S_IXUSR);
	while (r == -1 && errno == EINTR);

	if (r == -1)
		log_error (LOG_DEFAULT, "chmod(\"%s\") failed: %s", path, strerror (errno));
}

static void
copy_file_to_internal_location(char *to, char *from, char* file)
{
	char *from_file = path_combine (from, file);

	if (!file_exists (from_file))
	{
		free (from_file);
		return;
	}

	log_info (LOG_DEFAULT, "Copying file %s from external location %s to internal location %s",
		file, from, to);

	char *to_file = path_combine (to, file);
	unlink (to_file);

	if (file_copy (to_file, from_file) < 0)
		log_warn (LOG_DEFAULT, "Copy failed: %s", strerror (errno));

	set_user_executable (to_file);

	free (from_file);
	free (to_file);
}
#else  /* !defined (ANDROID) */
#ifdef DEBUG
static int
enable_soft_breakpoints (void)
{
	return 0;
}
#endif /* DEBUG */

void
set_world_accessable (const char *path)
{
}
#endif /* !defined (ANDROID) */

static void
mono_runtime_init (char *runtime_args)
{
	int profile_events;
#if DEBUG
	RuntimeOptions options;
	int64_t cur_time;
#endif
	char *prop_val;

#if defined (DEBUG) && !defined (WINDOWS)
	memset(&options, 0, sizeof (options));

	cur_time = time (NULL);

	if (!parse_runtime_args (runtime_args, &options)) {
		log_error (LOG_DEFAULT, "Failed to parse runtime args: '%s'", runtime_args);
	} else if (options.debug && cur_time > options.timeout_time) {
		log_warn (LOG_DEBUGGER, "Not starting the debugger as the timeout value has been reached; current-time: %lli  timeout: %lli", cur_time, options.timeout_time);
	} else if (options.debug && cur_time <= options.timeout_time) {
		char *debug_arg;
		char *debug_options [2];

		register_debug_symbols = 1;

		debug_arg = monodroid_strdup_printf ("--debugger-agent=transport=dt_socket,loglevel=%d,address=%s:%d,%sembedding=1", options.loglevel, options.host, options.sdb_port, 
				options.server ? "server=y," : "");
		debug_options[0] = debug_arg;

		log_info (LOG_DEBUGGER, "Trying to initialize the debugger with options: %s", debug_arg);

		if (options.out_port > 0) {
			struct sockaddr_in addr;
			int sock;
			int r;

			sock = socket (PF_INET, SOCK_STREAM, IPPROTO_TCP);
			if (sock < 0) {
				log_fatal (LOG_DEBUGGER, "Could not construct a socket for stdout and stderr; does your app have the android.permission.INTERNET permission? %s", strerror (errno));
				exit (FATAL_EXIT_DEBUGGER_CONNECT);
			}

			memset(&addr, 0, sizeof (addr));

			addr.sin_family = AF_INET;
			addr.sin_port = htons (options.out_port);

			if ((r = inet_pton (AF_INET, options.host, &addr.sin_addr)) != 1) {
				log_error (LOG_DEBUGGER, "Could not setup a socket for stdout and stderr: %s",
						r == -1 ? strerror (errno) : "address not parseable in the specified address family");
				exit (FATAL_EXIT_DEBUGGER_CONNECT);
			}

			if (options.server) {
				int accepted = monodroid_debug_accept (sock, addr);
				log_info (LOG_DEBUGGER, "Accepted stdout connection: %d", accepted);
				if (accepted < 0) {
					log_fatal (LOG_DEBUGGER, "Error accepting stdout and stderr (%s:%d): %s",
							     options.host, options.out_port, strerror (errno));
					exit (FATAL_EXIT_DEBUGGER_CONNECT);
				}

				dup2 (accepted, 1);
				dup2 (accepted, 2);
			} else {
				if (monodroid_debug_connect (sock, addr) != 1) {
					log_fatal (LOG_DEBUGGER, "Error connecting stdout and stderr (%s:%d): %s",
							     options.host, options.out_port, strerror (errno));
					exit (FATAL_EXIT_DEBUGGER_CONNECT);
				}

				dup2 (sock, 1);
				dup2 (sock, 2);
			}
		}

		if (enable_soft_breakpoints ()) {
			debug_options[1] = "--soft-breakpoints";
			mono.mono_jit_parse_options (2, debug_options);
		} else {
			mono.mono_jit_parse_options (1, debug_options);
		}

		mono.mono_debug_init (MONO_DEBUG_FORMAT_MONO);
	} else {
		set_debug_options ();
	}
#else
	set_debug_options ();
#endif

	profile_events = MONO_PROFILE_THREADS;
	if ((log_categories & LOG_TIMING) != 0) {
		char *jit_log_path = path_combine (override_dirs [0], "methods.txt");
		jit_log = monodroid_fopen (jit_log_path, "a");
		set_world_accessable (jit_log_path);
		free (jit_log_path);

		profile_events |= MONO_PROFILE_JIT_COMPILATION;
	}
	mono.mono_profiler_install ((MonoProfiler*)&monodroid_profiler, NULL);
	mono.mono_profiler_set_events (profile_events);
	mono.mono_profiler_install_thread (thread_start, thread_end);
	if ((log_categories & LOG_TIMING) != 0)
		mono.mono_profiler_install_jit_end (jit_end);

	parse_gdb_options ();

	if (wait_for_gdb) {
		log_info (LOG_DEFAULT, "Waiting for gdb to attach...");
		while (monodroid_gdb_wait) {
			sleep (1);
		}
	}

	/* Additional runtime arguments passed to mono_jit_parse_options () */
	if (monodroid_get_namespaced_system_property (DEBUG_MONO_RUNTIME_ARGS_PROPERTY, &prop_val) > 0) {
		char **args, **ptr;
		int argc;

		log_info (LOG_DEBUGGER, "passing '%s' as extra arguments to the runtime.\n", prop_val);

		args = monodroid_strsplit (prop_val, " ", -1);
		argc = 0;
		free (prop_val);

		for (ptr = args; *ptr; ptr++)
			argc ++;

		mono.mono_jit_parse_options (argc, args);
	}

	mono.mono_set_signal_chaining (1);
	if (mono.mono_set_crash_chaining)
		mono.mono_set_crash_chaining (1);

	register_gc_hooks ();

	if (mono_mkbundle_initialize_mono_api) {
		BundleMonoAPI bundle_mono_api = {
			.mono_register_bundled_assemblies = mono.mono_register_bundled_assemblies,
			.mono_register_config_for_assembly = mono.mono_register_config_for_assembly,
			.mono_jit_set_aot_mode = mono.mono_jit_set_aot_mode,
			.mono_aot_register_module = mono.mono_aot_register_module,
			.mono_config_parse_memory = mono.mono_config_parse_memory,
			.mono_register_machine_config = mono.mono_register_machine_config,
		};

		/* The initialization function copies the struct */
		mono_mkbundle_initialize_mono_api (&bundle_mono_api);
	}

	if (mono_mkbundle_init)
		mono_mkbundle_init (mono.mono_register_bundled_assemblies, mono.mono_register_config_for_assembly, mono.mono_jit_set_aot_mode);

	/*
	 * Assembly preload hooks are invoked in _reverse_ registration order.
	 * Looking for assemblies from the update dir takes precedence over
	 * everything else, and thus must go LAST.
	 */
	monodroid_embedded_assemblies_install_preload_hook (&mono);
#ifndef RELEASE
	mono.mono_install_assembly_preload_hook (open_from_update_dir, NULL);
#endif
}

static int
GetAndroidSdkVersion (JNIEnv *env, jobject loader)
{
	jclass    lrefVersion = (*env)->FindClass (env, "android/os/Build$VERSION");
	if (lrefVersion == NULL) {
		// Try to load the class from the loader instead.
		// Needed by Android designer that uses dynamic loaders
		(*env)->ExceptionClear (env);
		jclass classLoader = (*env)->FindClass (env, "java/lang/ClassLoader");
		jmethodID classLoader_loadClass = (*env)->GetMethodID (env, classLoader, "loadClass", "(Ljava/lang/String;)Ljava/lang/Class;");
		//(*env)->ExceptionDescribe (env);
		jstring versionClassName = (*env)->NewStringUTF (env, "android.os.Build$VERSION");

		lrefVersion = (jclass)(*env)->CallObjectMethod (env, loader, classLoader_loadClass, versionClassName);

		(*env)->DeleteLocalRef (env, classLoader);
		(*env)->DeleteLocalRef (env, versionClassName);
	}
	jfieldID  SDK_INT     = (*env)->GetStaticFieldID (env, lrefVersion, "SDK_INT", "I");
	int       version     = (*env)->GetStaticIntField (env, lrefVersion, SDK_INT);

	(*env)->DeleteLocalRef (env, lrefVersion);

	return version;
}

static MonoDomain*
create_domain (JNIEnv *env, jobjectArray runtimeApks, jstring assembly, jobject loader, mono_bool is_root_domain)
{
	MonoDomain *domain;
	int user_assemblies_count   = 0;;

	gather_bundled_assemblies (env, runtimeApks, register_debug_symbols, &user_assemblies_count);

	if (!mono_mkbundle_init && user_assemblies_count == 0 && count_override_assemblies () == 0) {
		log_fatal (LOG_DEFAULT, "No assemblies found in '%s' or '%s'. Assuming this is part of Fast Deployment. Exiting...",
				override_dirs [0],
				(MAX_OVERRIDES > 1 && override_dirs [1] != NULL) ? override_dirs [1] : "<unavailable>");
		exit (FATAL_EXIT_NO_ASSEMBLIES);
	}

	if (is_root_domain)
		domain = mono.mono_jit_init_version ("RootDomain", "mobile");
	else {
		MonoDomain* root_domain = mono.mono_get_root_domain ();
		char *domain_name = monodroid_strdup_printf ("MonoAndroidDomain%d", GetAndroidSdkVersion (env, loader));
		domain = monodroid_create_appdomain (&mono, root_domain, domain_name, /*shadow_copy:*/ 1, /*shadow_directory:*/ override_dirs [0]);
		free (domain_name);
	}

	if (is_running_on_desktop && is_root_domain) {
		// Check that our corlib is coherent with the version of Mono we loaded otherwise
		// tell the IDE that the project likely need to be recompiled.
		char* corlib_error_message = mono.mono_check_corlib_version ();
		if (corlib_error_message == NULL) {
			if (!monodroid_get_system_property ("xamarin.studio.fakefaultycorliberrormessage", &corlib_error_message)) {
				free (corlib_error_message);
				corlib_error_message = NULL;
			}
		}
		if (corlib_error_message != NULL) {
			jclass ex_klass = (*env)->FindClass (env, "mono/android/MonoRuntimeException");
			(*env)->ThrowNew (env, ex_klass, corlib_error_message);
			free (corlib_error_message);
			return NULL;
		}

		// Load a basic environment for the RootDomain if run on desktop so that we can unload
		// and reload most assemblies including Mono.Android itself
		MonoAssemblyName *aname = mono.mono_assembly_name_new ("System");
		mono.mono_assembly_load_full (aname, NULL, NULL, 0);
		mono.mono_assembly_name_free (aname);
	} else {
		// Inflate environment from user app assembly
		load_assembly (domain, env, assembly);
	}

	return domain;
}

static void
load_assemblies (MonoDomain *domain, JNIEnv *env, jobjectArray assemblies)
{
	jsize i;
	jsize assembliesLength = (*env)->GetArrayLength (env, assemblies);
	/* skip element 0, as that's loaded in create_domain() */
	for (i = 1; i < assembliesLength; ++i) {
		jstring assembly = (*env)->GetObjectArrayElement (env, assemblies, i);
		load_assembly (domain, env, assembly);
	}
}

static jclass System;
static jmethodID System_identityHashCode;

static int
LocalRefsAreIndirect (JNIEnv *env, int version)
{
	if (version < 14)
		return 0;

	System = (*env)->NewGlobalRef (env, (*env)->FindClass (env, "java/lang/System"));

	System_identityHashCode = (*env)->GetStaticMethodID (env, System,
			"identityHashCode", "(Ljava/lang/Object;)I");

	return 1;
}

MONO_API void*
_monodroid_get_identity_hash_code (JNIEnv *env, void *v)
{
	intptr_t rv = (*env)->CallStaticIntMethod (env, System, System_identityHashCode, v);
	return (void*) rv;
}

MONO_API void*
_monodroid_timezone_get_default_id (void)
{
	JNIEnv *env         = ensure_jnienv ();
	jobject d           = (*env)->CallStaticObjectMethod (env, TimeZone_class, TimeZone_getDefault);
	jstring id          = (*env)->CallObjectMethod (env, d, TimeZone_getID);
	const char *mutf8   = (*env)->GetStringUTFChars (env, id, NULL);

	char *def_id        = monodroid_strdup_printf ("%s", mutf8);

	(*env)->ReleaseStringUTFChars (env, id, mutf8);
	(*env)->DeleteLocalRef (env, id);
	(*env)->DeleteLocalRef (env, d);

	return def_id;
}

MONO_API void
_monodroid_gc_wait_for_bridge_processing (void)
{
	mono.mono_gc_wait_for_bridge_processing ();
}

static MonoMethod* registerType;

struct JnienvInitializeArgs {
	JavaVM         *javaVm;
	JNIEnv         *env;
	jobject         grefLoader;
	jmethodID       Loader_loadClass;
	jclass          grefClass;
	jmethodID       Class_forName;
	unsigned int    logCategories;
	jmethodID       Class_getName;
	int             version;
	int             androidSdkVersion;
	int             localRefsAreIndirect;
	int             grefGcThreshold;
	jobject         grefIGCUserPeer;
	int             isRunningOnDesktop;
};

static int get_gref_gc_threshold (void)
{
	if (max_gref_count == INT_MAX)
		return max_gref_count;
	long long value = max_gref_count;
	value *= 90;
	return (int) (value / 100);
}

#define DEFAULT_X_DPI 120.0f
#define DEFAULT_Y_DPI 120.0f

static MonoMethod *runtime_GetDisplayDPI;

/* !DO NOT REMOVE! Used by libgdiplus.so */
MONO_API int
_monodroid_get_display_dpi (float *x_dpi, float *y_dpi)
{
	void *args[2];
	MonoObject *exc = NULL;

	if (!x_dpi) {
		log_error (LOG_DEFAULT, "Internal error: x_dpi parameter missing in get_display_dpi");
		return -1;
	}

	if (!y_dpi) {
		log_error (LOG_DEFAULT, "Internal error: y_dpi parameter missing in get_display_dpi");
		return -1;
	}

	if (!runtime_GetDisplayDPI) {
		*x_dpi = DEFAULT_X_DPI;
		*y_dpi = DEFAULT_Y_DPI;
		return 0;
	}

	args [0] = x_dpi;
	args [1] = y_dpi;
	monodroid_runtime_invoke (&mono, mono.mono_get_root_domain (), runtime_GetDisplayDPI, NULL, args, &exc);
	if (exc) {
		*x_dpi = DEFAULT_X_DPI;
		*y_dpi = DEFAULT_Y_DPI;
	}

	return 0;
}

static void
lookup_bridge_info (MonoDomain *domain, MonoImage *image, const MonoJavaGCBridgeType *type, MonoJavaGCBridgeInfo *info)
{
	info->klass             = monodroid_get_class_from_image (&mono, domain, image, type->namespace, type->typename);
	info->handle            = mono.mono_class_get_field_from_name (info->klass, "handle");
	info->handle_type       = mono.mono_class_get_field_from_name (info->klass, "handle_type");
	info->refs_added        = mono.mono_class_get_field_from_name (info->klass, "refs_added");
	info->weak_handle       = mono.mono_class_get_field_from_name (info->klass, "weak_handle");
}

static void
init_android_runtime (MonoDomain *domain, JNIEnv *env, jobject loader)
{
	MonoAssembly *assm;
	MonoClass *runtime;
	MonoClass *environment;
	MonoImage *image;
	MonoMethod *method;
	long long start_time, end_time;
	jobject lrefLoaderClass;
	jobject lrefIGCUserPeer;
	int i;

	struct JnienvInitializeArgs init    = {};
	void *args [1];
	args [0] = &init;

	android_api_level = GetAndroidSdkVersion (env, loader);
	init.javaVm                 = jvm;
	init.env                    = env;
	init.logCategories          = log_categories;
	init.version                = (*env)->GetVersion (env);
	init.androidSdkVersion      = android_api_level;
	init.localRefsAreIndirect   = LocalRefsAreIndirect (env, init.androidSdkVersion);
	init.isRunningOnDesktop     = is_running_on_desktop;

	// GC threshold is 90% of the max GREF count
	init.grefGcThreshold        = get_gref_gc_threshold ();

	log_info (LOG_GC, "GREF GC Threshold: %i", init.grefGcThreshold);

	jclass lrefClass = (*env)->FindClass (env, "java/lang/Class");
	init.grefClass = (*env)->NewGlobalRef (env, lrefClass);
	init.Class_getName  = (*env)->GetMethodID (env, lrefClass, "getName", "()Ljava/lang/String;");
	init.Class_forName = (*env)->GetStaticMethodID (env, lrefClass, "forName", "(Ljava/lang/String;ZLjava/lang/ClassLoader;)Ljava/lang/Class;");
	(*env)->DeleteLocalRef (env, lrefClass);

	assm  = monodroid_load_assembly (&mono, domain, "Mono.Android");
	image = mono.mono_assembly_get_image  (assm);

	for (i = 0; i < NUM_GC_BRIDGE_TYPES; ++i) {
		lookup_bridge_info (domain, image, &mono_java_gc_bridge_types [i], &mono_java_gc_bridge_info [i]);
	}

	runtime                             = monodroid_get_class_from_image (&mono, domain, image, "Android.Runtime", "JNIEnv");
	method                              = mono.mono_class_get_method_from_name (runtime, "Initialize", 1);
	environment                         = monodroid_get_class_from_image (&mono, domain, image, "Android.Runtime", "AndroidEnvironment");

	if (method == 0) {
		log_fatal (LOG_DEFAULT, "INTERNAL ERROR: Unable to find Android.Runtime.JNIEnv.Initialize!");
		exit (FATAL_EXIT_MISSING_INIT);
	}
	/* If running on desktop, we may be swapping in a new Mono.Android image when calling this
	 * so always make sure we have the freshest handle to the method.
	 */
	if (registerType == 0 || is_running_on_desktop) {
		registerType = mono.mono_class_get_method_from_name (runtime, "RegisterJniNatives", 5);
	}
	if (registerType == 0) {
		log_fatal (LOG_DEFAULT, "INTERNAL ERROR: Unable to find Android.Runtime.JNIEnv.RegisterJniNatives!");
		exit (FATAL_EXIT_CANNOT_FIND_JNIENV);
	}
	MonoClass *android_runtime_jnienv = runtime;
	MonoClassField *bridge_processing_field = mono.mono_class_get_field_from_name (runtime, "BridgeProcessing");
	runtime_GetDisplayDPI                           = mono.mono_class_get_method_from_name (environment, "GetDisplayDPI", 2);
	if (!android_runtime_jnienv || !bridge_processing_field) {
		log_fatal (LOG_DEFAULT, "INTERNAL_ERROR: Unable to find Android.Runtime.JNIEnv.BridgeProcessing");
		exit (FATAL_EXIT_CANNOT_FIND_JNIENV);
	}

	lrefLoaderClass = (*env)->GetObjectClass (env, loader);
	init.Loader_loadClass = (*env)->GetMethodID (env, lrefLoaderClass, "loadClass", "(Ljava/lang/String;)Ljava/lang/Class;");
	(*env)->DeleteLocalRef (env, lrefLoaderClass);

	init.grefLoader = (*env)->NewGlobalRef (env, loader);

	lrefIGCUserPeer       = (*env)->FindClass (env, "mono/android/IGCUserPeer");
	init.grefIGCUserPeer  = (*env)->NewGlobalRef (env, lrefIGCUserPeer);
	(*env)->DeleteLocalRef (env, lrefIGCUserPeer);

	GCUserPeer_class      = lref_to_gref (env, (*env)->FindClass (env, "mono/android/GCUserPeer"));
	GCUserPeer_ctor       = (*env)->GetMethodID (env, GCUserPeer_class, "<init>", "()V");
	assert ( (GCUserPeer_class && GCUserPeer_ctor) || !"Failed to load mono.android.GCUserPeer!" );

	start_time = current_time_millis ();
	log_info (LOG_TIMING, "Runtime.init: start native-to-managed transition time: %lli ms\n", start_time);
	log_info (LOG_DEFAULT, "Calling into managed runtime init");

	monodroid_runtime_invoke (&mono, domain, method, NULL, args, NULL);

	end_time = current_time_millis ();
	log_info (LOG_TIMING, "Runtime.init: end native-to-managed transition time: %lli [elapsed %lli ms]\n", end_time, end_time - start_time);
}

static MonoClass*
get_android_runtime_class (MonoDomain *domain)
{
	MonoAssembly *assm = monodroid_load_assembly (&mono, domain, "Mono.Android");
	MonoImage *image   = mono.mono_assembly_get_image (assm);
	MonoClass *runtime = monodroid_get_class_from_image (&mono, domain, image, "Android.Runtime", "JNIEnv");

	return runtime;
}

static void
shutdown_android_runtime (MonoDomain *domain)
{
	MonoClass *runtime = get_android_runtime_class (domain);
	MonoMethod *method = mono.mono_class_get_method_from_name (runtime, "Exit", 0);

	monodroid_runtime_invoke (&mono, domain, method, NULL, NULL, NULL);
}

static void
propagate_uncaught_exception (MonoDomain *domain, JNIEnv *env, jobject javaThread, jthrowable javaException)
{
	void *args[3];
	MonoClass *runtime = get_android_runtime_class (domain);
	MonoMethod *method = mono.mono_class_get_method_from_name (runtime, "PropagateUncaughtException", 3);

	args[0] = &env;
	args[1] = &javaThread;
	args[2] = &javaException;
	monodroid_runtime_invoke (&mono, domain, method, NULL, args, NULL);
}

static void
register_packages (MonoDomain *domain, JNIEnv *env, jobjectArray assemblies)
{
	jsize i;
	jsize assembliesLength = (*env)->GetArrayLength (env, assemblies);
	for (i = 0; i < assembliesLength; ++i) {
		const char    *filename;
		char          *basename;
		MonoAssembly  *a;
		MonoImage     *image;
		MonoClass     *c;
		MonoMethod    *m;
		jstring assembly = (*env)->GetObjectArrayElement (env, assemblies, i);

		filename = (*env)->GetStringUTFChars (env, assembly, NULL);
		basename = monodroid_strdup_printf ("%s", filename);
		(*strrchr (basename, '.')) = '\0';
		a = mono.mono_domain_assembly_open (domain, basename);
		if (a == NULL) {
			log_fatal (LOG_ASSEMBLY, "Could not load assembly '%s' during startup registration.", basename);
			log_fatal (LOG_ASSEMBLY, "This might be due to an invalid debug installation.");
			log_fatal (LOG_ASSEMBLY, "A common cause is to 'adb install' the app directly instead of doing from the IDE.");
			exit (FATAL_EXIT_MISSING_ASSEMBLY);
		}


		free (basename);
		(*env)->ReleaseStringUTFChars (env, assembly, filename);

		image = mono.mono_assembly_get_image (a);

		c = monodroid_get_class_from_image (&mono, domain, image, "Java.Interop", "__TypeRegistrations");
		if (c == NULL)
			continue;
		m = mono.mono_class_get_method_from_name (c, "RegisterPackages", 0);
		if (m == NULL)
			continue;
		monodroid_runtime_invoke (&mono, domain, m, NULL, NULL, NULL);
	}
}

#if DEBUG
static void
setup_gc_logging (void)
{
	gc_spew_enabled = monodroid_get_namespaced_system_property (DEBUG_MONO_GC_PROPERTY, NULL) > 0;
	if (gc_spew_enabled) {
		log_categories |= LOG_GC;
	}
}
#endif

static int
convert_dl_flags (int flags)
{
	int lflags = flags & MONO_DL_LOCAL? 0: RTLD_GLOBAL;

	if (flags & MONO_DL_LAZY)
		lflags |= RTLD_LAZY;
	else
		lflags |= RTLD_NOW;
	return lflags;
}

static void*
monodroid_dlopen (const char *name, int flags, char **err, void *user_data)
{
	/* name is NULL when we're P/Invoking __Internal, so remap to libmonodroid */
	char *full_name = path_combine (app_libdir, name ? name : "libmonodroid.so");
	if (!name && !file_exists (full_name)) {
		log_info (LOG_ASSEMBLY, "Trying to load library '%s'", full_name);
		free (full_name);
		full_name = path_combine (SYSTEM_LIB_PATH, "libmonodroid.so");
	}
	int dl_flags = convert_dl_flags (flags);
	void *h = dlopen (full_name, dl_flags);
	log_info (LOG_ASSEMBLY, "Trying to load library '%s'", full_name);

	if (!h && name && (strstr (name, ".dll.so") || strstr (name, ".exe.so"))) {
		char *full_name2;
		const char *basename;

		if (strrchr (name, '/'))
			basename = strrchr (name, '/') + 1;
		else
			basename = name;

		/* Try loading AOT modules from the override dir */
		if (override_dirs [0]) {
			full_name2 = monodroid_strdup_printf ("%s" MONODROID_PATH_SEPARATOR "libaot-%s", override_dirs [0], basename);
			h = dlopen (full_name2, dl_flags);
			free (full_name2);
		}

		/* Try loading AOT modules from the lib dir */
		if (!h) {
			full_name2 = monodroid_strdup_printf ("%s" MONODROID_PATH_SEPARATOR "libaot-%s", app_libdir, basename);
			h = dlopen (full_name2, dl_flags);
			free (full_name2);			
		}

		if (h)
			log_info (LOG_ASSEMBLY, "Loaded AOT image '%s'", full_name2);
	}

	if (!h && err) {
		*err = monodroid_strdup_printf ("Could not load library: Library '%s' not found.", full_name);
	}

	free (full_name);

	return h;
}

static void*
monodroid_dlsym (void *handle, const char *name, char **err, void *user_data)
{
	void *s;
	
	s = dlsym (handle, name);

	if (!s && err) {
		*err = monodroid_strdup_printf ("Could not find symbol '%s'.", name);
	}

	return s;
}

static const unsigned char monodroid_config[];
static const unsigned int monodroid_config_len;
static const unsigned char monodroid_machine_config[];
static const unsigned int monodroid_machine_config_len;

static void
set_environment_variable_for_directory_full (JNIEnv *env, const char *name, jstring value, int createDirectory, int mode )
{
	const char *v;

	v = (*env)->GetStringUTFChars (env, value, NULL);
	if (createDirectory) {
		int rv = create_directory (v, mode);
		if (rv < 0 && errno != EEXIST)
			log_warn (LOG_DEFAULT, "Failed to create directory for environment variable %s. %s", name, strerror (errno));
	}
	setenv (name, v, 1);
	(*env)->ReleaseStringUTFChars (env, value, v);
}

static void
set_environment_variable_for_directory (JNIEnv *env, const char *name, jstring value)
{
	set_environment_variable_for_directory_full (env, name, value, 1, DEFAULT_DIRECTORY_MODE);
}

static void
set_environment_variable (JNIEnv *env, const char *name, jstring value)
{
	set_environment_variable_for_directory_full (env, name, value, 0, 0);
}

static void
create_xdg_directory (const char *home, const char *relativePath, const char *environmentVariableName)
{
	char *dir = monodroid_strdup_printf ("%s/%s", home, relativePath);
	log_info (LOG_DEFAULT, "Creating XDG directory: %s", dir);
	int rv = create_directory (dir, DEFAULT_DIRECTORY_MODE);
	if (rv < 0 && errno != EEXIST)
		log_warn (LOG_DEFAULT, "Failed to create XDG directory %s. %s", dir, strerror (errno));
	if (environmentVariableName)
		setenv (environmentVariableName, dir, 1);
	free (dir);
}

static void
create_xdg_directories_and_environment (JNIEnv *env, jstring homeDir)
{
	const char *home = (*env)->GetStringUTFChars (env, homeDir, NULL);
	create_xdg_directory (home, ".local/share", "XDG_DATA_HOME");
	create_xdg_directory (home, ".config", "XDG_CONFIG_HOME");
	(*env)->ReleaseStringUTFChars (env, homeDir, home);
}

#if DEBUG
static void
set_debug_env_vars (void)
{
	char *value;
	char **args, **ptr;

	if (monodroid_get_namespaced_system_property (DEBUG_MONO_ENV_PROPERTY, &value) == 0)
		return;

	args = monodroid_strsplit (value, "|", -1);
	free (value);

	for (ptr = args; ptr && *ptr; ptr++) {
		char *arg = *ptr;
		char *v = strchr (arg, '=');
		if (v) {
			*v = '\0';
			++v;
		} else
			v = "1";
		setenv (arg, v, 1);
		log_info (LOG_DEFAULT, "Env variable '%s' set to '%s'.", arg, v);
	}
	monodroid_strfreev (args);
}
#endif /* DEBUG */

static void
set_trace_options (void)
{
	char *value;

	if (monodroid_get_namespaced_system_property (DEBUG_MONO_TRACE_PROPERTY, &value) == 0)
		return;

	mono.mono_jit_set_trace_options (value);
	free (value);
}

/* Profiler support cribbed from mono/metadata/profiler.c */

typedef void (*ProfilerInitializer) (const char*);
#define INITIALIZER_NAME "mono_profiler_init"

static mono_bool
load_profiler (void *handle, const char *desc, const char *symbol)
{
	ProfilerInitializer func = dlsym (handle, symbol);
	log_info (LOG_DEFAULT, "Looking for profiler init symbol '%s'? %p", symbol, func);

	if (func) {
		func (desc);
		return 1;
	}
	return 0;
}

static mono_bool
load_embedded_profiler (const char *desc, const char *name)
{
	mono_bool result;

	char *full_name = path_combine (app_libdir, "libmonodroid.so");
	void *h         = dlopen (full_name, RTLD_LAZY);
	const char *e   = dlerror ();

	log_info (LOG_DEFAULT, "looking for embedded profiler within '%s': dlopen=%p error=%s",
			full_name,
			h,
			h != NULL ? "<none>" : e);

	free (full_name);

	if (!h) {
		return 0;
	}

	char *symbol = monodroid_strdup_printf ("%s_%s", INITIALIZER_NAME, name);
	if (!(result = load_profiler (h, desc, symbol)))
		dlclose (h);
	free (symbol);

	return result;
}

static mono_bool
load_profiler_from_directory (const char *directory, const char *libname, const char *desc, const char *name)
{
	char *full_name = path_combine (directory, libname);
	int  exists     = file_exists (full_name);
	void *h         = exists ? dlopen (full_name, RTLD_LAZY) : NULL;
	const char *e   = exists ? dlerror () : "No such file or directory";

	log_info (LOG_DEFAULT, "Trying to load profiler: %s: dlopen=%p error=%s",
			full_name,
			h,
			h != NULL ? "<none>" : e);

	free (full_name);

	if (h) {
		char *symbol = monodroid_strdup_printf ("%s_%s", INITIALIZER_NAME, name);
		mono_bool result = load_profiler (h, desc, symbol);
		free (symbol);
		if (result)
			return 1;
		dlclose (h);
	}
	return 0;
}

static void
monodroid_profiler_load (const char *libmono_path, const char *desc, const char *logfile)
{
	const char* col = strchr (desc, ':');
	char *mname;
	int oi;

	if (col != NULL) {
		mname = xmalloc (col - desc + 1);
		strncpy (mname, desc, col - desc);
		mname [col - desc] = 0;
	} else {
		mname = monodroid_strdup_printf ("%s", desc);
	}

	char *libname = monodroid_strdup_printf ("libmono-profiler-%s.so", mname);

	mono_bool found = 0;

	for (oi = 0; oi < MAX_OVERRIDES; ++oi) {
		if (!directory_exists (override_dirs [oi]))
			continue;
		if ((found = load_profiler_from_directory (override_dirs [oi], libname, desc, mname)))
			break;
	}

	do {
		if (found)
			break;
		if ((found = load_profiler_from_directory (app_libdir, libname, desc, mname)))
			break;
		if ((found = load_embedded_profiler (desc, mname)))
			break;
		if (libmono_path != NULL && (found = load_profiler_from_directory (libmono_path, libname, desc, mname)))
			break;
	} while (0);


	if (found && logfile != NULL)
		set_world_accessable (logfile);

	if (!found)
		log_warn (LOG_DEFAULT,
				"The '%s' profiler wasn't found in the main executable nor could it be loaded from '%s'.",
				mname,
				libname);

	free (mname);
	free (libname);
}

static void
set_profile_options (JNIEnv *env)
{
	char *value;
	char *output;
	char **args, **ptr;

	if (monodroid_get_namespaced_system_property (DEBUG_MONO_PROFILE_PROPERTY, &value) == 0)
		return;

	output = NULL;

	args = monodroid_strsplit (value, ",", -1);
	for (ptr = args; ptr && *ptr; ptr++) {
		const char *arg = *ptr;
		if (!strncmp (arg, "output=", sizeof ("output=")-1)) {
			const char *p = arg + (sizeof ("output=")-1);
			if (strlen (p)) {
				output = monodroid_strdup_printf ("%s", p);
				break;
			}
		}
	}
	monodroid_strfreev (args);

	if (!output) {
		const char* col = strchr (value, ':');
		char *ovalue;
		char *extension;

		if ((col && !strncmp (value, "log:", 4)) || !strcmp (value, "log"))
			extension = monodroid_strdup_printf ("mlpd");
		else {
			int len = col ? col - value - 1 : strlen (value);
			extension = xmalloc (len + 1);
			strncpy (extension, value, len);
			extension [len] = 0;
		}

		char *filename = monodroid_strdup_printf ("profile.%s",
					extension);

		output = path_combine (override_dirs [0], filename);
		ovalue = monodroid_strdup_printf ("%s%soutput=%s",
				value,
				col == NULL ? ":" : ",",
				output);
		free (value);
		free (extension);
		free (filename);
		value = ovalue;
	}

	/*
	 * libmono-profiler-log.so profiler won't overwrite existing files.
	 * Remove it For Great Justice^H^H^H to preserve my sanity!
	 */
	unlink (output);

	log_info (LOG_DEFAULT, "Initializing profiler with options: %s", value);
	monodroid_profiler_load (runtime_libdir, value, output);

	free (value);
	free (output);
}

static FILE* counters;

static void
setup_environment_from_line (const char *line)
{
	char **entry;
	const char *k, *v;

	if (line == NULL || !isprint (line [0]))
		return;

	entry = monodroid_strsplit (line, "=", 2);

	if ((k = entry [0]) && *k &&
			(v = entry [1]) && *v) {
		if (islower (k [0])) {
			add_system_property (k, v);
		} else {
			setenv (k, v, 1);
		}
	}

	monodroid_strfreev (entry);
}

static void
setup_environment_from_file (const char *apk, int index, int apk_count)
{
	unzFile file;
	if ((file = unzOpen (apk)) == NULL)
		return;

	if (unzLocateFile (file, "environment", 0) == UNZ_OK) {
		unz_file_info info;

		if (unzGetCurrentFileInfo (file, &info, NULL, 0, NULL, 0, NULL, 0) == UNZ_OK &&
				unzOpenCurrentFile (file) == UNZ_OK) {
			char *contents = calloc (info.uncompressed_size+1, sizeof (char));
			if (contents != NULL &&
					unzReadCurrentFile (file, contents, info.uncompressed_size) > 0) {

				int i;
				char *line = contents;
				contents [info.uncompressed_size] = '\0';

				for (i = 0; i < info.uncompressed_size; ++i) {
					if (contents [i] != '\n')
						continue;

					contents [i] = '\0';
					setup_environment_from_line (line);
					line = &contents [i+1];
				}

				if (line < (contents + info.uncompressed_size))
					setup_environment_from_line (line);

				free (contents);
			}

			unzCloseCurrentFile (file);
		}
	}

	unzClose (file);
}

static void
for_each_apk (JNIEnv *env, jobjectArray runtimeApks, void (*handler) (const char *apk, int index, int apk_count))
{
	int i;
	jsize apksLength = (*env)->GetArrayLength (env, runtimeApks);
	for (i = 0; i < apksLength; ++i) {
		jstring e       = (*env)->GetObjectArrayElement (env, runtimeApks, i);
		const char *apk = (*env)->GetStringUTFChars (env, e, NULL);

		handler (apk, i, apksLength);
		(*env)->ReleaseStringUTFChars (env, e, apk);
	}
}

static void
setup_environment (JNIEnv *env, jobjectArray runtimeApks)
{
	for_each_apk (env, runtimeApks, setup_environment_from_file);
}

static void
setup_process_args_apk (const char *apk, int index, int apk_count)
{
	if (!apk || index != apk_count - 1)
		return;

	char *args[1] = { (char*) apk };
	mono.mono_runtime_set_main_args (1, args);
}

static void
setup_process_args (JNIEnv *env, jobjectArray runtimeApks)
{
	for_each_apk (env, runtimeApks, setup_process_args_apk);
}

/*
 * process_cmd:
 *
 *   Process a command received from XS through a socket connection.
 * This is called on a separate thread.
 * Return TRUE, if a new connection need to be opened.
 */
int
process_cmd (int fd, char *cmd)
{
	if (!strcmp (cmd, "connect output")) {
		dup2 (fd, 1);
		dup2 (fd, 2);
		return TRUE;
	} else if (!strcmp (cmd, "connect stdout")) {
		dup2 (fd, 1);
		return TRUE;
	} else if (!strcmp (cmd, "connect stderr")) {
		dup2 (fd, 2);
		return TRUE;
	} else if (!strcmp (cmd, "discard")) {
		return TRUE;
	} else if (!strcmp (cmd, "ping")) {
		if (!send_uninterrupted (fd, "pong", 5))
			log_error (LOG_DEFAULT, "Got keepalive request from XS, but could not send response back (%s)\n", strerror (errno));
	} else if (!strcmp (cmd, "exit process")) {
		log_info (LOG_DEFAULT, "XS requested an exit, will exit immediately.\n");
		fflush (stdout);
		fflush (stderr);
		exit (0);
	} else if (!strncmp (cmd, "start debugger: ", 16)) {
		const char *debugger = cmd + 16;
		int use_fd = FALSE;
		if (!strcmp (debugger, "no")) {
			/* disabled */
		} else if (!strcmp (debugger, "sdb")) {
			sdb_fd = fd;
			use_fd = TRUE;
		}
		/* Notify the main thread (start_debugging ()) */
		debugging_configured = TRUE;
		pthread_mutex_lock (&process_cmd_mutex);
		pthread_cond_signal (&process_cmd_cond);
		pthread_mutex_unlock (&process_cmd_mutex);
		if (use_fd)
			return TRUE;
	} else if (!strncmp (cmd, "start profiler: ", 16)) {
		const char *prof = cmd + 16;
		int use_fd = FALSE;

		if (!strcmp (prof, "no")) {
			/* disabled */
		} else if (!strncmp (prof, "log:", 4)) {
			use_fd = TRUE;
			profiler_fd = fd;
			profiler_description = monodroid_strdup_printf ("%s,output=#%i", prof, profiler_fd);
		} else {
			log_error (LOG_DEFAULT, "Unknown profiler: '%s'", prof);
		}
		/* Notify the main thread (start_profiling ()) */
		profiler_configured = TRUE;
		pthread_mutex_lock (&process_cmd_mutex);
		pthread_cond_signal (&process_cmd_cond);
		pthread_mutex_unlock (&process_cmd_mutex);
		if (use_fd)
			return TRUE;
	} else {
		log_error (LOG_DEFAULT, "Unsupported command: '%s'", cmd);
	}

	return FALSE;
}

#ifdef DEBUG

static void
start_debugging (void)
{
	char *debug_arg;
	char *debug_options [2];

	// wait for debugger configuration to finish
	pthread_mutex_lock (&process_cmd_mutex);
	while (!debugging_configured && !config_timedout) {
		if (pthread_cond_timedwait (&process_cmd_cond, &process_cmd_mutex, &wait_ts) == ETIMEDOUT)
			config_timedout = TRUE;
	}
	pthread_mutex_unlock (&process_cmd_mutex);

	if (!sdb_fd)
		return;

	register_debug_symbols = 1;

	debug_arg = monodroid_strdup_printf ("--debugger-agent=transport=socket-fd,address=%d,embedding=1", sdb_fd);
	debug_options[0] = debug_arg;

	log_info (LOG_DEBUGGER, "Trying to initialize the debugger with options: %s", debug_arg);

	if (enable_soft_breakpoints ()) {
		debug_options[1] = "--soft-breakpoints";
		mono.mono_jit_parse_options (2, debug_options);
	} else {
		mono.mono_jit_parse_options (1, debug_options);
	}

	mono.mono_debug_init (MONO_DEBUG_FORMAT_MONO);
}

static void
start_profiling (void)
{
	// wait for profiler configuration to finish
	pthread_mutex_lock (&process_cmd_mutex);
	while (!profiler_configured && !config_timedout) {
		if (pthread_cond_timedwait (&process_cmd_cond, &process_cmd_mutex, &wait_ts) == ETIMEDOUT)
			config_timedout = TRUE;
	}
	pthread_mutex_unlock (&process_cmd_mutex);

	if (!profiler_description)
		return;

	log_info (LOG_DEFAULT, "Loading profiler: '%s'", profiler_description);
	monodroid_profiler_load (runtime_libdir, profiler_description, NULL);
}

#endif  // def DEBUG

/*
Disable LLVM signal handlers.

This happens when RenderScript needs to be compiled. See https://bugzilla.xamarin.com/show_bug.cgi?id=18016

This happens only on first run of the app. LLVM is used to compiled the RenderScript scripts. LLVM, been
a nice and smart library installs a ton of signal handlers and don't chain at all, completely breaking us.

This is a hack to set llvm::DisablePrettyStackTrace to true and avoid this source of signal handlers.

*/
static void
disable_external_signal_handlers (void)
{
	void *llvm  = dlopen ("libLLVM.so", RTLD_LAZY);
	if (llvm) {
		_Bool *disable_signals = dlsym (llvm, "_ZN4llvm23DisablePrettyStackTraceE");
		if (disable_signals) {
			*disable_signals = 1;
			log_info (LOG_DEFAULT, "Disabled LLVM signal trapping");
		}
		//MUST NOT dlclose to ensure we don't lose the hack
	}
}

MONO_API void
_monodroid_counters_dump (const char *format, ...)
{
	va_list args;

	if (counters == NULL)
		return;

	fprintf (counters, "\n");

	va_start (args, format);
	vfprintf (counters, format, args);
	va_end (args);

	fprintf (counters, "\n");

	mono.mono_counters_dump (XA_LOG_COUNTERS, counters);
}

static void
monodroid_Mono_UnhandledException_internal (MonoException *ex)
{
	// Do nothing with it here, we let the exception naturally propagate on the managed side
}

static MonoDomain*
create_and_initialize_domain (JNIEnv* env, jobjectArray runtimeApks, jobjectArray assemblies, jobject loader, mono_bool is_root_domain)
{
	MonoDomain* domain = create_domain (env, runtimeApks, (*env)->GetObjectArrayElement (env, assemblies, 0), loader, is_root_domain);

	// When running on desktop, the root domain is only a dummy so don't initialize it
	if (is_running_on_desktop && is_root_domain)
		return domain;

	load_assemblies (domain, env, assemblies);
	init_android_runtime (domain, env, loader);
	register_packages (domain, env, assemblies);

	add_monodroid_domain (domain);

	return domain;
}

JNIEXPORT void JNICALL
Java_mono_android_Runtime_init (JNIEnv *env, jclass klass, jstring lang, jobjectArray runtimeApks, jstring runtimeNativeLibDir, jobjectArray appDirs, jobject loader, jobjectArray externalStorageDirs, jobjectArray assemblies, jstring packageName)
{
	char *runtime_args = NULL;
	char *connect_args;
	jstring libdir_s;
	const char *libdir, *esd;
	char *libmonosgen_path;
	char *libmonodroid_bundle_app_path;
	char *counters_path;
	const char *pkgName;
	char *aotMode;
	int i;

	pkgName = (*env)->GetStringUTFChars (env, packageName, NULL);
	monodroid_store_package_name (pkgName); /* Will make a copy of the string */
	(*env)->ReleaseStringUTFChars (env, packageName, pkgName);

	disable_external_signal_handlers ();

	log_info (LOG_TIMING, "Runtime.init: start: %lli ms\n", current_time_millis ());

	jstring homeDir = (*env)->GetObjectArrayElement (env, appDirs, 0);
	set_environment_variable (env, "LANG", lang);
	set_environment_variable_for_directory (env, "HOME", homeDir);
	set_environment_variable_for_directory (env, "TMPDIR", (*env)->GetObjectArrayElement (env, appDirs, 1));
	create_xdg_directories_and_environment (env,  homeDir);

	setup_environment (env, runtimeApks);

	primary_override_dir = get_primary_override_dir (env, (*env)->GetObjectArrayElement (env, appDirs, 0));
	esd = (*env)->GetStringUTFChars (env, (*env)->GetObjectArrayElement (env, externalStorageDirs, 0), NULL);
	external_override_dir = monodroid_strdup_printf ("%s", esd);
	(*env)->ReleaseStringUTFChars (env, (*env)->GetObjectArrayElement (env, externalStorageDirs, 0), esd);

	esd = (*env)->GetStringUTFChars (env, (*env)->GetObjectArrayElement (env, externalStorageDirs, 1), NULL);
	external_legacy_override_dir = monodroid_strdup_printf ("%s", esd);
	(*env)->ReleaseStringUTFChars (env, (*env)->GetObjectArrayElement (env, externalStorageDirs, 1), esd);

	init_categories (primary_override_dir);
	create_update_dir (primary_override_dir);

#if DEBUG
	setup_gc_logging ();
	set_debug_env_vars ();
#endif

#ifndef RELEASE
	override_dirs [1] = external_override_dir;
	override_dirs [2] = external_legacy_override_dir;
	for (i = 0; i < MAX_OVERRIDES; ++i) {
		const char *p = override_dirs [i];
		if (!directory_exists (p))
			continue;
		log_info (LOG_DEFAULT, "Using override path: %s", p);
	}
#endif

	jsize appDirsLength = (*env)->GetArrayLength (env, appDirs);

	for (i = 0; i < appDirsLength; ++i) {
		jstring appDir = (*env)->GetObjectArrayElement (env, appDirs, i);
		libmonodroid_bundle_app_path = get_bundled_app (env, appDir);
		if (libmonodroid_bundle_app_path) {
			setup_bundled_app (libmonodroid_bundle_app_path);
			free (libmonodroid_bundle_app_path);
			break;
		}
	}

	libdir_s = (*env)->GetObjectArrayElement (env, appDirs, 2);
	libdir = (*env)->GetStringUTFChars (env, libdir_s, NULL);
	app_libdir = monodroid_strdup_printf ("%s", libdir);
	(*env)->ReleaseStringUTFChars (env, libdir_s, libdir);

	if (runtimeNativeLibDir != NULL) {
		const char *rd;
		rd = (*env)->GetStringUTFChars (env, runtimeNativeLibDir, NULL);
		runtime_libdir = monodroid_strdup_printf ("%s", rd);
		(*env)->ReleaseStringUTFChars (env, runtimeNativeLibDir, rd);
	}

	libmonosgen_path = get_libmonosgen_path ();
	if (!monodroid_dylib_mono_init (&mono, libmonosgen_path)) {
		log_fatal (LOG_DEFAULT, "shared runtime initialization error: %s", dlerror ());
		exit (FATAL_EXIT_CANNOT_FIND_MONO);
	}
	setup_process_args (env, runtimeApks);

	free (libmonosgen_path);
#ifndef WINDOWS
	_monodroid_getifaddrs_init ();
#endif

	if ((log_categories & LOG_TIMING) != 0) {
		mono.mono_counters_enable (XA_LOG_COUNTERS);
		counters_path = path_combine (override_dirs [0], "counters.txt");
		counters = monodroid_fopen (counters_path, "a");
		set_world_accessable (counters_path);
		free (counters_path);
	}

	mono.mono_dl_fallback_register (monodroid_dlopen, monodroid_dlsym, NULL, NULL);

	set_profile_options (env);

	set_trace_options ();

	monodroid_get_namespaced_system_property (DEBUG_MONO_CONNECT_PROPERTY, &connect_args);

#if defined (DEBUG) && !defined (WINDOWS)
	if (connect_args) {
		int res = start_connection (connect_args);
		if (res != 2) {
			if (res) {
				log_fatal (LOG_DEBUGGER, "Could not start a connection to the debugger with connection args '%s'.", connect_args);
				exit (FATAL_EXIT_DEBUGGER_CONNECT);
			}

			/* Wait for XS to configure debugging/profiling */
			gettimeofday(&wait_tv, NULL);
			wait_ts.tv_sec = wait_tv.tv_sec + 2;
			wait_ts.tv_nsec = wait_tv.tv_usec * 1000;
			start_debugging ();
			start_profiling ();
		}
	}
#endif

	mono.mono_config_parse_memory ((const char*) monodroid_config);
	mono.mono_register_machine_config ((const char*) monodroid_machine_config);

	log_info (LOG_DEFAULT, "Probing for mono.aot AOT mode\n");

	if (monodroid_get_system_property ("mono.aot", &aotMode) > 0) {
		MonoAotMode mode = 0;
		if (strcmp (aotMode, "normal") == 0)
			mode = MONO_AOT_MODE_NORMAL;
		else if (strcmp (aotMode, "hybrid") == 0)
			mode = MONO_AOT_MODE_HYBRID;
		else if (strcmp (aotMode, "full") == 0)
			mode = MONO_AOT_MODE_FULL;
		else
			log_warn (LOG_DEFAULT, "Unknown mono.aot property value: %s\n", aotMode);

		if (mode != MONO_AOT_MODE_NORMAL) {
			log_info (LOG_DEFAULT, "Enabling %s AOT mode in Mono\n", aotMode);
			mono.mono_jit_set_aot_mode (mode);
		}
	}		

	log_info (LOG_DEFAULT, "Probing if we should use LLVM\n");

	if (monodroid_get_system_property ("mono.llvm", NULL) > 0) {
		char *args [1];
		args[0] = "--llvm";
		log_info (LOG_DEFAULT, "Found mono.llvm property, enabling LLVM mode in Mono\n");
		mono.mono_jit_parse_options (1,  args);
		*mono.mono_use_llvm = TRUE;
	}	

	monodroid_get_namespaced_system_property (DEBUG_MONO_EXTRA_PROPERTY, &runtime_args);
#if TRACE
	__android_log_print (ANDROID_LOG_INFO, "*jonp*", "debug.mono.extra=%s", runtime_args);
#endif

	mono_runtime_init (runtime_args);

	/* the first assembly is used to initialize the AppDomain name */
	create_and_initialize_domain (env, runtimeApks, assemblies, loader, /*is_root_domain:*/ 1);

	free (runtime_args);

	// Install our dummy exception handler on Desktop
	if (is_running_on_desktop) {
		mono.mono_add_internal_call ("System.Diagnostics.Debugger::Mono_UnhandledException_internal(System.Exception)",
		                             monodroid_Mono_UnhandledException_internal);
	}

	if ((log_categories & LOG_TIMING) != 0) {
		_monodroid_counters_dump ("## Runtime.init: end");
	}
}

JNIEXPORT void
JNICALL Java_mono_android_Runtime_register (JNIEnv *env, jclass klass, jstring managedType, jclass nativeClass, jstring methods)
{
	int managedType_len, methods_len;
	const jchar *managedType_ptr, *methods_ptr;
	void *args [5];
	char *type;
	const char *mt_ptr;
	MonoDomain *domain = mono.mono_domain_get ();

	long long start_time = current_time_millis (), end_time;
	log_info (LOG_TIMING, "Runtime.register: start time: %lli ms\n", start_time);

	managedType_len = (*env)->GetStringLength (env, managedType);
	managedType_ptr = (*env)->GetStringChars (env, managedType, NULL);

	methods_len = (*env)->GetStringLength (env, methods);
	methods_ptr = (*env)->GetStringChars (env, methods, NULL);

	mt_ptr = (*env)->GetStringUTFChars (env, managedType, NULL);
	type = monodroid_strdup_printf ("%s", mt_ptr);
	(*env)->ReleaseStringUTFChars (env, managedType, mt_ptr);

	args [0] = &managedType_ptr,
	args [1] = &managedType_len;
	args [2] = &nativeClass;
	args [3] = &methods_ptr;
	args [4] = &methods_len;

	mono.mono_jit_thread_attach (domain);
	// Refresh current domain as it might have been modified by the above call
	domain = mono.mono_domain_get ();
	monodroid_runtime_invoke (&mono, domain, registerType, NULL, args, NULL);

	(*env)->ReleaseStringChars (env, managedType, managedType_ptr);
	(*env)->ReleaseStringChars (env, methods, methods_ptr);

	end_time = current_time_millis ();
	log_info (LOG_TIMING, "Runtime.register: end time: %lli [elapsed %lli ms]\n", end_time, end_time - start_time);
	if ((log_categories & LOG_TIMING) != 0) {
		_monodroid_counters_dump ("## Runtime.register: type=%s\n", type);
	}
	free (type);
}

// DO NOT USE ON NORMAL X.A
// This function only works with the custom TypeManager embedded with the designer process.
static void
reinitialize_android_runtime_type_manager (JNIEnv *env)
{
	jclass typeManager = (*env)->FindClass (env, "mono/android/TypeManager");
	(*env)->UnregisterNatives (env, typeManager);

	jmethodID resetRegistration = (*env)->GetStaticMethodID (env, typeManager, "resetRegistration", "()V");
	(*env)->CallStaticVoidMethod (env, typeManager, resetRegistration);

	(*env)->DeleteLocalRef (env, typeManager);
}

JNIEXPORT jint
JNICALL Java_mono_android_Runtime_createNewContext (JNIEnv *env, jclass klass, jobjectArray runtimeApks, jobjectArray assemblies, jobject loader)
{
	log_info (LOG_DEFAULT, "CREATING NEW CONTEXT");
	reinitialize_android_runtime_type_manager (env);
	MonoDomain *root_domain = mono.mono_get_root_domain ();
	mono.mono_jit_thread_attach (root_domain);
	MonoDomain *domain = create_and_initialize_domain (env, runtimeApks, assemblies, loader, /*is_root_domain:*/ 0);
	mono.mono_domain_set (domain, FALSE);
	int domain_id = mono.mono_domain_get_id (domain);
	current_context_id = domain_id;
	log_info (LOG_DEFAULT, "Created new context with id %d\n", domain_id);
	return domain_id;
}

JNIEXPORT void
JNICALL Java_mono_android_Runtime_switchToContext (JNIEnv *env, jclass klass, jint contextID)
{
	log_info (LOG_DEFAULT, "SWITCHING CONTEXT");
	MonoDomain *domain = mono.mono_domain_get_by_id ((int)contextID);
	if (current_context_id != (int)contextID) {
		mono.mono_domain_set (domain, TRUE);
		// Reinitialize TypeManager so that its JNI handle goes into the right domain
		reinitialize_android_runtime_type_manager (env);
	}
	current_context_id = (int)contextID;
}

JNIEXPORT void
JNICALL Java_mono_android_Runtime_destroyContexts (JNIEnv *env, jclass klass, jintArray array)
{
	MonoDomain *root_domain = mono.mono_get_root_domain ();
	mono.mono_jit_thread_attach (root_domain);
	current_context_id = -1;

	jint *contextIDs = (*env)->GetIntArrayElements (env, array, NULL);
	jsize count = (*env)->GetArrayLength (env, array);

	log_info (LOG_DEFAULT, "Cleaning %d domains", count);

	int i;
	for (i = 0; i < count; i++) {
		int domain_id = contextIDs[i];
		MonoDomain *domain = mono.mono_domain_get_by_id (domain_id);

		if (domain == NULL)
			continue;
		log_info (LOG_DEFAULT, "Shutting down domain `%d'", contextIDs[i]);
		shutdown_android_runtime (domain);
		remove_monodroid_domain (domain);
	}

	/* If domains_list is now empty, we are about to unload Monodroid.dll.
	 * Clear the global bridge info structure since it's pointing into soon-invalid memory.
	 * FIXME: It is possible for a thread to get into `gc_bridge_class_kind` after this clear
	 *        occurs, but before the stop-the-world during mono_domain_unload. If this happens,
	 *        it can falsely mark a class as transparent. This is considered acceptable because
	 *        this case is *very* rare and the worst case scenario is a resource leak.
	 *        The real solution would be to add a new callback, called while the world is stopped
	 *        during `mono_gc_clear_domain`, and clear the bridge info during that.
	 */
	if (!domains_list)
		clear_mono_java_gc_bridge_info ();

	for (i = 0; i < count; i++) {
		int domain_id = contextIDs[i];
		MonoDomain *domain = mono.mono_domain_get_by_id (domain_id);

		if (domain == NULL)
			continue;
		log_info (LOG_DEFAULT, "Unloading domain `%d'", contextIDs[i]);
		mono.mono_domain_unload (domain);
	}

	(*env)->ReleaseIntArrayElements (env, array, contextIDs, JNI_ABORT);

	reinitialize_android_runtime_type_manager (env);

	log_info (LOG_DEFAULT, "All domain cleaned up");
}

JNIEXPORT void
JNICALL Java_mono_android_Runtime_propagateUncaughtException (JNIEnv *env, jclass klass, jobject javaThread, jthrowable javaException)
{
	MonoDomain *domain = mono.mono_domain_get ();
	propagate_uncaught_exception (domain, env, javaThread, javaException);
}


#include "config.include"
#include "machine.config.include"

