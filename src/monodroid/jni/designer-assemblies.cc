#include "designer-assemblies.hh"
#include "globals.hh"

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
	abort_if_invalid_pointer_argument (assembliesBytes);
	DesignerAssemblyEntry *new_entry = new DesignerAssemblyEntry (domain, env, assemblies, assembliesBytes, assembliesPaths);
	add_or_replace_entry (new_entry);
}

MonoAssembly*
DesignerAssemblies::try_load_assembly (MonoDomain *domain, MonoAssemblyName *name)
{
	int domain_id = mono_domain_get_id (domain);
	DesignerAssemblyEntry *entry = find_entry (domain_id);
	if (entry == nullptr)
		return nullptr;

	const char *asm_name = mono_assembly_name_get_name (name);
	unsigned int asm_count = entry->assemblies_count;

	for (unsigned int i = 0; i < asm_count; i++) {
		const char *entry_name = entry->names[i];
		const char *entry_bytes = entry->assemblies_bytes[i];
		const unsigned int entry_bytes_len = entry->assemblies_bytes_len[i];
		const char *entry_path = entry->assemblies_paths[i];

		if (strcmp (asm_name, entry_name) != 0)
			continue;

		/* We use the managed assembly loading API as there is unfortunately no public unmanaged API
		 * to select the loading context to use (it would require access to the MonoAssemblyLoadRequest API)
		 * which mean we can't properly do either loading from memory or call LoadFrom
		 */
		MonoClass *assembly_klass = utils.monodroid_get_class (domain, "mscorlib", "System.Reflection", "Assembly");

		if (entry_bytes_len > 0) {
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
			MonoReflectionAssembly *res = (MonoReflectionAssembly *)utils.monodroid_runtime_invoke (domain, assembly_load_method, nullptr, args, nullptr);
			return mono_reflection_assembly_get_assembly (res);
		} else if (entry_path != nullptr && utils.file_exists (entry_path)) {
			MonoMethod *assembly_load_method = mono_class_get_method_from_name (assembly_klass, "LoadFrom", 1);
			MonoString *asm_path = mono_string_new (domain, entry_path);

			void *args[1];
			args[0] = asm_path;
			MonoReflectionAssembly *res = (MonoReflectionAssembly *)utils.monodroid_runtime_invoke (domain, assembly_load_method, nullptr, args, nullptr);
			return mono_reflection_assembly_get_assembly (res);
		}
	}

	return nullptr;
}

void
DesignerAssemblies::clear_for_domain (MonoDomain *domain)
{
	int domain_id = mono_domain_get_id (domain);
	DesignerAssemblyEntry *entry = remove_entry (domain_id);
	delete entry;
}

DesignerAssemblies::DesignerAssemblyEntry*
DesignerAssemblies::find_entry (int domain_id)
{
	for (unsigned int i = 0; i < length; i++) {
		auto entry = entries[i];
		if (entry->domain_id == domain_id)
			return entry;
	}
	return nullptr;
}

void
DesignerAssemblies::add_or_replace_entry (DesignerAssemblies::DesignerAssemblyEntry *new_entry)
{
	for (unsigned int i = 0; i < length; i++) {
		auto entry = entries[i];
		if (entry->domain_id == new_entry->domain_id) {
			entries[i] = new_entry;
			delete entry;
			return;
		}
	}
	add_entry (new_entry);
}

void
DesignerAssemblies::add_entry (DesignerAssemblies::DesignerAssemblyEntry *entry)
{
	if (length >= capacity) {
		capacity = MULTIPLY_WITH_OVERFLOW_CHECK(unsigned int, capacity, 2);
		DesignerAssemblyEntry **new_entries = new DesignerAssemblyEntry*[capacity];
		memcpy (new_entries, entries, MULTIPLY_WITH_OVERFLOW_CHECK(size_t, sizeof(void*), length));
		DesignerAssemblyEntry **old_entries = entries;
		entries = new_entries;
		delete[] old_entries;
	}
	entries[length++] = entry;
}

DesignerAssemblies::DesignerAssemblyEntry*
DesignerAssemblies::remove_entry (int domain_id)
{
	for (unsigned int i = 0; i < length; i++) {
		DesignerAssemblyEntry *entry = entries[i];
		if (entry->domain_id == domain_id) {
			for (unsigned int j = i; j < length - 1; j++)
				entries[j] = entries[j + 1];
			length--;
			entries[length] = nullptr;
			return entry;
		}
	}
	return nullptr;
}

DesignerAssemblies::DesignerAssemblyEntry::DesignerAssemblyEntry (MonoDomain *domain, JNIEnv *env, jstring_array_wrapper &assemblies, jobjectArray assembliesBytes, jstring_array_wrapper &assembliesPaths)
{
	this->domain_id = mono_domain_get_id (domain);
	this->assemblies_count = static_cast<unsigned int> (assemblies.get_length ());
	this->names = new char*[assemblies_count];
	this->assemblies_bytes = new char*[assemblies_count];
	this->assemblies_paths = new char*[assemblies_count];
	this->assemblies_bytes_len = new unsigned int[assemblies_count];

	for (unsigned int index = 0; index < assemblies_count; index++) {
		jstring_wrapper &assembly = assemblies [index];
		names[index] = utils.strdup_new (assembly.get_cstr ());

		// Copy in-memory assembly bytes if any
		jboolean is_copy;
		jbyteArray assembly_byte_array = reinterpret_cast <jbyteArray> (env->GetObjectArrayElement (assembliesBytes, static_cast<jsize> (index)));
		if (assembly_byte_array != nullptr) {
			unsigned int bytes_len = static_cast<unsigned int> (env->GetArrayLength (assembly_byte_array));
			jbyte *bytes = env->GetByteArrayElements (assembly_byte_array, &is_copy);
			assemblies_bytes_len[index] = bytes_len;
			assemblies_bytes[index] = new char[bytes_len];
			memcpy (assemblies_bytes[index], bytes, bytes_len);
			env->ReleaseByteArrayElements (assembly_byte_array, bytes, JNI_ABORT);
		} else {
			assemblies_bytes_len[index] = 0;
			assemblies_bytes[index] = nullptr;
		}

		// Copy path to the specific assembly if any
		jstring_wrapper &assembly_path = assembliesPaths [index];
		assemblies_paths[index] = assembly_path.hasValue () ? utils.strdup_new (assembly_path.get_cstr ()) : nullptr;
	}
}

DesignerAssemblies::DesignerAssemblyEntry::~DesignerAssemblyEntry ()
{
	for (unsigned int i = 0; i < assemblies_count; ++i) {
		delete[] names [i];
		delete[] assemblies_bytes [i];
		delete[] assemblies_paths [i];
	}
	delete[] names;
	delete[] assemblies_bytes;
	delete[] assemblies_paths;
	delete[] assemblies_bytes_len;
}
