// Dear Emacs, this is a -*- C++ -*- header
#if !defined (__MONO_IMAGE_LOADER_HH)
#define __MONO_IMAGE_LOADER_HH

#include <cstdint>
#include <cstddef>

#include <mono/metadata/image.h>
#include <mono/metadata/mono-private-unstable.h>
#include <mono/metadata/object.h>

#include "platform-compat.hh"
#include "xamarin-app.hh"
#include "xxhash.hh"
#include "search.hh"
#include "strings.hh"

#if defined (RELEASE)
#define USE_CACHE 1
#endif

namespace xamarin::android::internal {
	enum class MonoImageLoaderContext
	{
		ALC,
		AppDomain,
	};

	class MonoImageLoader final
	{
	public:
#if defined (USE_CACHE)
		force_inline static MonoImage* get_from_index (size_t index) noexcept
		{
			if (index >= application_config.number_of_assemblies_in_apk) {
				return nullptr;
			}

			return get_from_index_quick (index);
		}

		force_inline static MonoImage* get_with_hash (hash_t hash) noexcept
		{
			ssize_t index = find_index (hash);
			if (index < 0) {
				return nullptr;
			}

			return get_from_index_quick (static_cast<size_t>(index));
		}
#endif // def USE_CACHE

		force_inline static MonoImage* load (dynamic_local_string<SENSIBLE_PATH_MAX> const& name, MonoAssemblyLoadContextGCHandle alc_gchandle, hash_t name_hash, uint8_t *assembly_data, uint32_t assembly_data_size) noexcept
		{
			MonoImageOpenStatus status;
			MonoImage *image = mono_image_open_from_data_alc (
				alc_gchandle,
				reinterpret_cast<char*>(assembly_data),
				assembly_data_size,
				0 /* need_copy */,
				&status,
				name.get ()
			);

			return stash_and_return (image, status, name_hash);
		}

		force_inline static MonoImage* load (dynamic_local_string<SENSIBLE_PATH_MAX> const& name, MonoAssemblyLoadContextGCHandle alc_gchandle, uint8_t *assembly_data, uint32_t assembly_data_size) noexcept
		{
			return load (name, alc_gchandle, xxhash::hash (name.get (), name.length ()), assembly_data, assembly_data_size);
		}

		force_inline static MonoImage* load (dynamic_local_string<SENSIBLE_PATH_MAX> const& name, bool ref_only, hash_t name_hash, uint8_t *assembly_data, uint32_t assembly_data_size) noexcept
		{
			MonoImageOpenStatus status;
			MonoImage *image = mono_image_open_from_data_with_name (
				reinterpret_cast<char*>(assembly_data),
				assembly_data_size,
				0,
				&status,
				ref_only,
				name.get ()
			);

			return stash_and_return (image, status, name_hash);
		}

		force_inline static MonoImage* load (dynamic_local_string<SENSIBLE_PATH_MAX> const& name, bool ref_only, uint8_t *assembly_data, uint32_t assembly_data_size) noexcept
		{
			return load (name, ref_only, xxhash::hash (name.get (), name.length ()), assembly_data, assembly_data_size);
		}

	private:
#if defined (USE_CACHE)
		// Performs NO BOUNDS CHECKING (intended!)
		force_inline static MonoImage* get_from_index_quick (size_t index) noexcept
		{
			return assembly_image_cache[index];
		}

		force_inline static ssize_t find_index (hash_t hash) noexcept
		{
			ssize_t idx = Search::binary_search (hash, assembly_image_cache_hashes, number_of_cache_index_entries);
			return idx >= 0 ? static_cast<ssize_t>(assembly_image_cache_indices[idx]) : -1;

		}
#endif // def USE_CACHE

		force_inline static MonoImage* stash_and_return (MonoImage *image, MonoImageOpenStatus status, [[maybe_unused]] hash_t hash) noexcept
		{
			if (image == nullptr || status != MonoImageOpenStatus::MONO_IMAGE_OK) {
				log_warn (LOG_ASSEMBLY, "Failed to open assembly image. %s", mono_image_strerror (status));
				return nullptr;
			}

#if defined (USE_CACHE)
			ssize_t index = find_index (hash);
			if (index < 0) {
				log_warn (LOG_ASSEMBLY, "Failed to look up image index for hash 0x%zx", hash);
				return image;
			}

			// We don't need to worry about locking here.  Even if we're overwriting an entry just set by another
			// thread, the image pointer is going to be the same (at least currently, it will change when we have
			// support for unloadable Assembly Load Contexts) and the actual write operation to the destination is
			// atomic
			assembly_image_cache[index] = image;
#endif // def USE_CACHE
			return image;
		}

#if defined (USE_CACHE)
		static inline size_t number_of_cache_index_entries = application_config.number_of_assemblies_in_apk * number_of_assembly_name_forms_in_image_cache;;
#endif // def USE_CACHE
	};
}
#endif // ndef __MONO_IMAGE_LOADER_HH
