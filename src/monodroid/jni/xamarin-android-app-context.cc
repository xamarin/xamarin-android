#include <mono/metadata/appdomain.h>
#include <mono/metadata/class.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/threads.h>

#include "monodroid-glue-internal.hh"
#include "mono-image-loader.hh"
#include "marshal-methods-utilities.hh"

using namespace xamarin::android::internal;

const char*
MonodroidRuntime::get_method_name (uint32_t mono_image_index, uint32_t method_token) noexcept
{
	uint64_t id = MarshalMethodsUtilities::get_method_id (mono_image_index, method_token);

	log_debug (LOG_ASSEMBLY, "MM: looking for name of method with id 0x%llx, in mono image at index %u", id, mono_image_index);
	return MarshalMethodsUtilities::get_method_name (id);
}

template<bool NeedsLocking>
force_inline void
MonodroidRuntime::get_function_pointer (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr) noexcept
{
	log_debug (
		LOG_ASSEMBLY,
		"MM: Trying to look up pointer to method '%s' (token 0x%x) in class '%s' (index %u)",
		get_method_name (mono_image_index, method_token), method_token,
		MarshalMethodsUtilities::get_class_name (class_index), class_index
	);

	if (class_index >= marshal_methods_number_of_classes) [[unlikely]] {
		log_fatal (LOG_DEFAULT,
		           "Internal error: invalid index for class cache (expected at most %u, got %u)",
		           marshal_methods_number_of_classes - 1,
		           class_index
		);
		Helpers::abort_application ();
	}

	// We need to do that, as Mono APIs cannot be invoked from threads that aren't attached to the runtime.
	mono_thread_attach (mono_get_root_domain ());

	MonoImage *image = MonoImageLoader::get_from_index (mono_image_index);
	MarshalMethodsManagedClass &klass = marshal_methods_class_cache[class_index];
	if (klass.klass == nullptr) {
		klass.klass = image != nullptr ? mono_class_get (image, klass.token) : nullptr;
	}

	MonoMethod *method = klass.klass != nullptr ? mono_get_method (image, method_token, klass.klass) : nullptr;
	MonoError error;
	void *ret = method != nullptr ? mono_method_get_unmanaged_callers_only_ftnptr (method, &error) : nullptr;

	if (ret != nullptr) [[likely]] {
		if constexpr (NeedsLocking) {
			__atomic_store_n (&target_ptr, ret, __ATOMIC_RELEASE);
		} else {
			target_ptr = ret;
		}

		log_debug (LOG_ASSEMBLY, "Loaded pointer to method %s (%p) (mono_image_index == %u; class_index == %u; method_token == 0x%x)", mono_method_full_name (method, true), ret, mono_image_index, class_index, method_token);
		return;
	}

	log_fatal (
		LOG_DEFAULT,
		"Failed to obtain function pointer to method '%s' in class '%s'",
		get_method_name (mono_image_index, method_token),
		MarshalMethodsUtilities::get_class_name (class_index)
	);

	log_fatal (
		LOG_DEFAULT,
		"Looked for image index %u, class index %u, method token 0x%x",
		mono_image_index,
		class_index,
		method_token
	);

	if (image == nullptr) {
		log_fatal (LOG_DEFAULT, "Failed to load MonoImage for the assembly");
	} else if (method == nullptr) {
		log_fatal (LOG_DEFAULT, "Failed to load class from the assembly");
	}

	if (error.error_code != MONO_ERROR_NONE) {
		const char *msg = mono_error_get_message (&error);
		if (msg != nullptr) {
			log_fatal (LOG_DEFAULT, msg);
		}
	}

	Helpers::abort_application ();
}

void
MonodroidRuntime::get_function_pointer_at_startup (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr) noexcept
{
	get_function_pointer<false> (mono_image_index, class_index, method_token, target_ptr);
}

void
MonodroidRuntime::get_function_pointer_at_runtime (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr) noexcept
{
	get_function_pointer<true> (mono_image_index, class_index, method_token, target_ptr);
}
