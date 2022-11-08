#if !defined (ANDROID)
#include "designer-assemblies.hh"
#include "util.hh"

#include <mono/metadata/appdomain.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/reflection.h>

extern "C" {
#include "java-interop-util.h"
}

using namespace xamarin::android::internal;

void
DesignerAssemblies::add_or_update_from_java (MonoDomain *domain, JNIEnv *env, jstring_array_wrapper &assemblies, jobjectArray assembliesBytes, jstring_array_wrapper &assembliesPaths)
{
	LOG_FUNC_ENTER ();

	abort_if_invalid_pointer_argument (assembliesBytes);
	LOG_LOCATION ();
	DesignerAssemblyEntry *new_entry = new DesignerAssemblyEntry (domain, env, assemblies, assembliesBytes, assembliesPaths);
	LOG_LOCATION ();
	add_or_replace_entry (new_entry);

	LOG_FUNC_ENTER ();
}

MonoAssembly*
DesignerAssemblies::try_load_assembly (MonoDomain *domain, MonoAssemblyName *name)
{
	LOG_FUNC_ENTER ();

	int domain_id = mono_domain_get_id (domain);
	LOG_LOCATION ();
	DesignerAssemblyEntry *entry = find_entry (domain_id);
	LOG_LOCATION ();
	if (entry == nullptr) {
		LOG_FUNC_LEAVE ();
		return nullptr;
	}
	LOG_LOCATION ();

	const char *asm_name = mono_assembly_name_get_name (name);
	LOG_LOCATION ();
	unsigned int asm_count = entry->assemblies_count;
	LOG_LOCATION ();

	for (unsigned int i = 0; i < asm_count; i++) {
		LOG_LOCATION ();
		const char *entry_name = entry->names[i];
		LOG_LOCATION ();
		const char *entry_bytes = entry->assemblies_bytes[i];
		LOG_LOCATION ();
		const unsigned int entry_bytes_len = entry->assemblies_bytes_len[i];
		LOG_LOCATION ();
		const char *entry_path = entry->assemblies_paths[i];
		LOG_LOCATION ();

		if (strcmp (asm_name, entry_name) != 0) {
			LOG_LOCATION ();
			continue;
		}
		LOG_LOCATION ();

		/* We use the managed assembly loading API as there is unfortunately no public unmanaged API
		 * to select the loading context to use (it would require access to the MonoAssemblyLoadRequest API)
		 * which mean we can't properly do either loading from memory or call LoadFrom
		 */
		MonoClass *assembly_klass = Util::monodroid_get_class_from_name (domain, "mscorlib", "System.Reflection", "Assembly");

		if (entry_bytes_len > 0) {
			LOG_LOCATION ();
			MonoClass *byte_klass = mono_get_byte_class ();
			// Use the variant with 3 parameters so that we always get the first argument being a byte[]
			// (the two last don't matter since we pass null anyway)
			MonoMethod *assembly_load_method = mono_class_get_method_from_name (assembly_klass, "Load", 3);
			MonoArray *byteArray = mono_array_new (domain, byte_klass, entry_bytes_len);
			mono_value_copy_array (byteArray, 0, const_cast<char*> (entry_bytes), static_cast<int> (entry_bytes_len));

			void *args[3];
			args[0] = byteArray;
			args[1] = nullptr;
			args[2] = nullptr;
			MonoReflectionAssembly *res = (MonoReflectionAssembly *)Util::monodroid_runtime_invoke (domain, assembly_load_method, nullptr, args, nullptr);

			LOG_FUNC_LEAVE ();
			return mono_reflection_assembly_get_assembly (res);
		} else if (entry_path != nullptr && Util::file_exists (entry_path)) {
			LOG_LOCATION ();
			MonoMethod *assembly_load_method = mono_class_get_method_from_name (assembly_klass, "LoadFrom", 1);
			MonoString *asm_path = mono_string_new (domain, entry_path);

			void *args[1];
			args[0] = asm_path;
			MonoReflectionAssembly *res = (MonoReflectionAssembly *)Util::monodroid_runtime_invoke (domain, assembly_load_method, nullptr, args, nullptr);

			LOG_FUNC_LEAVE ();
			return mono_reflection_assembly_get_assembly (res);
		}
	}

	LOG_FUNC_LEAVE ();
	return nullptr;
}

void
DesignerAssemblies::clear_for_domain (MonoDomain *domain)
{
	LOG_FUNC_ENTER ();

	int domain_id = mono_domain_get_id (domain);
	LOG_LOCATION ();
	DesignerAssemblyEntry *entry = remove_entry (domain_id);
	LOG_LOCATION ();
	delete entry;

	LOG_FUNC_LEAVE ();
}

DesignerAssemblies::DesignerAssemblyEntry*
DesignerAssemblies::find_entry (int domain_id)
{
	LOG_FUNC_ENTER ();

	for (unsigned int i = 0; i < length; i++) {
		auto entry = entries[i];
		if (entry->domain_id == domain_id) {
			LOG_FUNC_LEAVE ();
			return entry;
		}
	}

	LOG_FUNC_LEAVE ();
	return nullptr;
}

void
DesignerAssemblies::add_or_replace_entry (DesignerAssemblies::DesignerAssemblyEntry *new_entry)
{
	LOG_FUNC_ENTER ();

	for (unsigned int i = 0; i < length; i++) {
		auto entry = entries[i];
		if (entry->domain_id == new_entry->domain_id) {
			entries[i] = new_entry;
			delete entry;

			LOG_FUNC_LEAVE ();
			return;
		}
	}

	LOG_LOCATION ();
	add_entry (new_entry);

	LOG_FUNC_LEAVE ();
}

void
DesignerAssemblies::add_entry (DesignerAssemblies::DesignerAssemblyEntry *entry)
{
	LOG_FUNC_ENTER ();
	if (length >= capacity) {
		LOG_LOCATION ();
		capacity = MULTIPLY_WITH_OVERFLOW_CHECK(unsigned int, capacity, 2);
		DesignerAssemblyEntry **new_entries = new DesignerAssemblyEntry*[capacity];
		LOG_LOCATION ();
		memcpy (new_entries, entries, MULTIPLY_WITH_OVERFLOW_CHECK(size_t, sizeof(void*), length));
		LOG_LOCATION ();
		DesignerAssemblyEntry **old_entries = entries;
		entries = new_entries;
		LOG_LOCATION ();
		delete[] old_entries;
		LOG_LOCATION ();
	}

	LOG_LOCATION ();
	entries[length++] = entry;

	LOG_FUNC_LEAVE ();
}

DesignerAssemblies::DesignerAssemblyEntry*
DesignerAssemblies::remove_entry (int domain_id)
{
	LOG_FUNC_ENTER ();

	for (unsigned int i = 0; i < length; i++) {
		DesignerAssemblyEntry *entry = entries[i];
		if (entry->domain_id == domain_id) {
			LOG_LOCATION ();
			for (unsigned int j = i; j < length - 1; j++)
				entries[j] = entries[j + 1];
			LOG_LOCATION ();
			length--;
			entries[length] = nullptr;

			LOG_FUNC_LEAVE ();
			return entry;
		}
	}

	LOG_FUNC_LEAVE ();
	return nullptr;
}

DesignerAssemblies::DesignerAssemblyEntry::DesignerAssemblyEntry (MonoDomain *domain, JNIEnv *env, jstring_array_wrapper &assemblies, jobjectArray assembliesBytes, jstring_array_wrapper &assembliesPaths)
{
	LOG_FUNC_ENTER ();

	this->domain_id = mono_domain_get_id (domain);
	this->assemblies_count = static_cast<unsigned int> (assemblies.get_length ());
	this->names = new char*[assemblies_count];
	this->assemblies_bytes = new char*[assemblies_count];
	this->assemblies_paths = new char*[assemblies_count];
	this->assemblies_bytes_len = new unsigned int[assemblies_count];

	LOG_LOCATION ();
	for (unsigned int index = 0; index < assemblies_count; index++) {
		jstring_wrapper &assembly = assemblies [index];
		names[index] = Util::strdup_new (assembly.get_cstr ());

		// Copy in-memory assembly bytes if any
		jboolean is_copy;
		jbyteArray assembly_byte_array = reinterpret_cast <jbyteArray> (env->GetObjectArrayElement (assembliesBytes, static_cast<jsize> (index)));
		if (assembly_byte_array != nullptr) {
			LOG_LOCATION ();
			unsigned int bytes_len = static_cast<unsigned int> (env->GetArrayLength (assembly_byte_array));
			jbyte *bytes = env->GetByteArrayElements (assembly_byte_array, &is_copy);
			assemblies_bytes_len[index] = bytes_len;
			assemblies_bytes[index] = new char[bytes_len];
			memcpy (assemblies_bytes[index], bytes, bytes_len);
			env->ReleaseByteArrayElements (assembly_byte_array, bytes, JNI_ABORT);
			LOG_LOCATION ();
		} else {
			LOG_LOCATION ();
			assemblies_bytes_len[index] = 0;
			assemblies_bytes[index] = nullptr;
			LOG_LOCATION ();
		}

		LOG_LOCATION ();
		// Copy path to the specific assembly if any
		jstring_wrapper &assembly_path = assembliesPaths [index];
		assemblies_paths[index] = assembly_path.hasValue () ? Util::strdup_new (assembly_path.get_cstr ()) : nullptr;
		LOG_LOCATION ();
	}

	LOG_FUNC_LEAVE ();
}

DesignerAssemblies::DesignerAssemblyEntry::~DesignerAssemblyEntry ()
{
	LOG_FUNC_ENTER ();

	for (unsigned int i = 0; i < assemblies_count; ++i) {
		delete[] names [i];
		delete[] assemblies_bytes [i];
		delete[] assemblies_paths [i];
	}
	LOG_LOCATION ();
	delete[] names;
	delete[] assemblies_bytes;
	delete[] assemblies_paths;
	delete[] assemblies_bytes_len;

	LOG_FUNC_LEAVE ();
}
#endif // ndef ANDROID
