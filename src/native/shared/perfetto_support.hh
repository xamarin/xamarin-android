#if !defined(PERFETTO_SUPPORT_HH)
#define PERFETTO_SUPPORT_HH

#if defined(PERFETTO_ENABLED)
#include <string_view>
#include <type_traits>

#include <mono/jit/jit.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/class.h>
#include <mono/metadata/debug-helpers.h>
#include <mono/metadata/mono-config.h>
#include <mono/metadata/mono-debug.h>
#include <mono/metadata/object.h>
#include <mono/utils/mono-dl-fallback.h>
#include <mono/utils/mono-logger.h>

#include <perfetto.h>

#include "cpp-util.hh"

namespace xamarin::android {
	class PerfettoConstants
	{
	public:
		static constexpr std::string_view MonoRuntimeCategory         { "mono-runtime" };
		static constexpr std::string_view MonodroidCategory           { "monodroid" };

		static constexpr std::string_view AssemblyLoadAnnotation      { "Assembly load" };
		static constexpr std::string_view ImageLoadAnnotation         { "Assembly image load" };
		static constexpr std::string_view ClassLoadAnnotation         { "Class load" };
		static constexpr std::string_view VTableLoadAnnotation        { "VTable load" };
		static constexpr std::string_view MethodInvokeAnnotation      { "Method: invoke" };
		static constexpr std::string_view MethodRunTimeAnnotation     { "Method: inner run time" };
		static constexpr std::string_view MonitorContentionAnnotation { "Monitor contention" };
		static constexpr std::string_view MonodroidRuntimeTrack       { "Monodroid" };

		static constexpr std::string_view XAInitInternal              { "InitInternal" };
	};

	enum class PerfettoTrackId : uint64_t
	{
		// We need to start high, so that we don't conflict with the standard Perfetto track IDs
		AssemblyLoadMonoVM = 0xDEADBEEF,
		ClassLoadMonoVM,
		ImageLoadMonoVM,
		MethodInnerMonoVM,
		MethodInvokeMonoVM,
		MonitorContentionMonoVM,
		VTableLoadMonoVM,

		MonodroidRuntime,
	};
}

PERFETTO_DEFINE_CATEGORIES (
	perfetto::Category (xamarin::android::PerfettoConstants::MonoRuntimeCategory.data ()).SetDescription ("Events from the MonoVM runtime"),
	perfetto::Category (xamarin::android::PerfettoConstants::MonodroidCategory.data ()).SetDescription ("Events from the .NET for Android native runtime")
);

namespace xamarin::android {
	namespace detail {
		template<typename TMonoType>
		concept SupportedMonoType = requires {
			std::same_as<MonoAssembly, TMonoType> ||
		    std::same_as<MonoImage, TMonoType> ||
		    std::same_as<MonoClass, TMonoType> ||
		    std::same_as<MonoVTable, TMonoType> ||
		    std::same_as<MonoMethod, TMonoType> ||
		    std::same_as<MonoObject, TMonoType>;
		};
	}

	class PerfettoSupport
	{
		static constexpr std::string_view Unnamed_AnnotationName      { "Unnamed annotation" };
		static constexpr std::string_view AssemblyName_AnnotationName { "Assembly name" };
		static constexpr std::string_view ImageName_AnnotationName    { "Image name" };
		static constexpr std::string_view MethodName_AnnotationName   { "Method name" };
		static constexpr std::string_view ClassName_AnnotationName    { "Class name" };

		static constexpr std::string_view Null_AnnotationContent      { "<NULL>" };
		static constexpr std::string_view MissingMethodName           { "<UNNAMED METHOD>" };

	public:

		force_inline constexpr static perfetto::StaticString get_event_name (std::string_view const& sv)
		{
			return perfetto::StaticString { sv.data () };
		}

		template<xamarin::android::PerfettoTrackId TTrack, bool UseThreadTrack>
		force_inline static perfetto::Track get_name_annotated_track ()
		{
			using TParentTrack = std::conditional_t<UseThreadTrack, perfetto::ThreadTrack, perfetto::ProcessTrack>;
			auto track = perfetto::Track (static_cast<uint64_t>(PerfettoTrackId::MonodroidRuntime), TParentTrack::Current ());
			auto desc = track.Serialize ();

			// if constexpr (TTrack == PerfettoTrackId::AssemblyLoadMonoVM) {
			// 	desc.set_name (PerfettoConstants::AssemblyLoadAnnotation.data ());
			// } else if constexpr (TTrack == PerfettoTrackId::ImageLoadMonoVM) {
			// 	desc.set_name (PerfettoConstants::ImageLoadAnnotation.data ());
			// } else if constexpr (TTrack == PerfettoTrackId::ClassLoadMonoVM) {
			// 	desc.set_name (PerfettoConstants::ClassLoadAnnotation.data ());
			// } else if constexpr (TTrack == PerfettoTrackId::VTableLoadMonoVM) {
			// 	desc.set_name (PerfettoConstants::VTableLoadAnnotation.data ());
			// } else if constexpr (TTrack == PerfettoTrackId::MethodInvokeMonoVM) {
			// 	desc.set_name (PerfettoConstants::MethodInvokeAnnotation.data ());
			// } else if constexpr (TTrack == PerfettoTrackId::MethodInnerMonoVM) {
			// 	desc.set_name (PerfettoConstants::MethodRunTimeAnnotation.data ());
			// } else if constexpr (TTrack == PerfettoTrackId::MonitorContentionMonoVM) {
			// 	desc.set_name (PerfettoConstants::MonitorContentionAnnotation.data ());
			// } else if constexpr (TTrack == PerfettoTrackId::MonodroidRuntime) {
				desc.set_name (PerfettoConstants::MonodroidRuntimeTrack.data ());
			//}

			set_track_event_descriptor (track, desc);
			return track;
		}

		template<xamarin::android::PerfettoTrackId TTrack>
		[[gnu::flatten]]
		force_inline static perfetto::Track get_name_annotated_process_track ()
		{
			return get_name_annotated_track<TTrack, false> ();
		}

		template<xamarin::android::PerfettoTrackId TTrack>
		[[gnu::flatten]]
		force_inline static perfetto::Track get_name_annotated_thread_track ()
		{
			return get_name_annotated_track<TTrack, true> ();
		}

		template<detail::SupportedMonoType TMonoType>
		force_inline static void add_name_annotation (perfetto::EventContext &ctx, TMonoType *data)
		{
			std::string name{};
			const std::string_view *annotation_name = nullptr;

			if constexpr (std::same_as<MonoAssembly, TMonoType>) {
				annotation_name = &AssemblyName_AnnotationName;
				MonoAssemblyName *asm_name = mono_assembly_get_name (data);
				if (asm_name != nullptr) [[likely]] {
					name = mono_assembly_name_get_name (asm_name);
				}
			} else if constexpr (std::same_as<MonoImage, TMonoType>) {
				annotation_name = &ImageName_AnnotationName;
				name = mono_image_get_name (data);
			} else if constexpr (std::same_as<MonoMethod, TMonoType>) {
				annotation_name = &MethodName_AnnotationName;
				append_full_class_name (mono_method_get_class (data), name);
				name.append (".");

				const char *method_name = mono_method_get_name (data);
				if (method_name != nullptr) [[likely]] {
					name.append (method_name);
				} else {
					name.append (MissingMethodName);
				}
			} else if constexpr (std::same_as<MonoClass, TMonoType>) {
				annotation_name = &ClassName_AnnotationName;
				append_full_class_name (data, name);
			} else if constexpr (std::same_as<MonoVTable, TMonoType>) {
				annotation_name = &ClassName_AnnotationName;
				append_full_class_name (mono_vtable_class (data), name);
			} else if constexpr (std::same_as<MonoObject, TMonoType>) {
				annotation_name = &ClassName_AnnotationName;
				append_full_class_name (mono_object_get_class (data), name);
			}

			auto annotation = ctx.event ()->add_debug_annotations ();
			annotation->set_name (annotation_name == nullptr ? Unnamed_AnnotationName.data () : annotation_name->data ());
			annotation->set_string_value (name.empty () ? Null_AnnotationContent.data () : name);
		}
	private:
		static void append_full_class_name (const MonoClass *klass, std::string &name)
		{
			if (klass == nullptr) [[unlikely]] {
				return;
			}

			name.append (mono_class_get_namespace (const_cast<MonoClass*>(klass)));
			name.append (".");
			name.append (mono_class_get_name (const_cast<MonoClass*>(klass)));
		}

	private:
		static void set_track_event_descriptor (perfetto::Track &track, perfetto::protos::gen::TrackDescriptor &desc);
	};
}
#endif // def PERFETTO_ENABLED

#endif // ndef PERFETTO_SUPPORT_HH
