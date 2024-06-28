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

#if defined (PERFETTO_ENABLED)
#include <perfetto.h>
#include "perfetto_support.hh"
#endif

#include "monodroid-glue-internal.hh"

using namespace xamarin::android;
using namespace xamarin::android::internal;

void
MonodroidRuntime::prof_assembly_loading ([[maybe_unused]] MonoProfiler *prof, MonoAssembly *assembly) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::AssemblyLoadMonoVM> ();
	TRACE_EVENT_BEGIN (
		PerfettoConstants::MonoRuntimeCategory.data(),
		PerfettoSupport::get_event_name (PerfettoConstants::AssemblyLoadAnnotation), track
	);
#endif
}

void
MonodroidRuntime::prof_assembly_loaded ([[maybe_unused]] MonoProfiler *prof, MonoAssembly *assembly) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::AssemblyLoadMonoVM> ();
	TRACE_EVENT_END (PerfettoConstants::MonoRuntimeCategory.data (), track, [&](perfetto::EventContext ctx) {
		PerfettoSupport::add_name_annotation (ctx, assembly);
	});
#endif
}

void
MonodroidRuntime::prof_image_loading ([[maybe_unused]] MonoProfiler *prof, MonoImage *image) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::ImageLoadMonoVM> ();
	TRACE_EVENT_BEGIN (
		PerfettoConstants::MonoRuntimeCategory.data (),
		PerfettoSupport::get_event_name (PerfettoConstants::ImageLoadAnnotation),
		track
	);
#endif
}

void
MonodroidRuntime::prof_image_loaded ([[maybe_unused]] MonoProfiler *prof, MonoImage *image) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::ImageLoadMonoVM> ();
	TRACE_EVENT_END (PerfettoConstants::MonoRuntimeCategory.data (), track, [&](perfetto::EventContext ctx) {
		PerfettoSupport::add_name_annotation (ctx, image);
	});
#endif
}

void
MonodroidRuntime::prof_class_loading ([[maybe_unused]] MonoProfiler *prof, MonoClass *klass) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::ClassLoadMonoVM> ();
	TRACE_EVENT_BEGIN (
		PerfettoConstants::MonoRuntimeCategory.data (),
		PerfettoSupport::get_event_name (PerfettoConstants::ClassLoadAnnotation),
		track
	);
#endif
}

void
MonodroidRuntime::prof_class_loaded ([[maybe_unused]] MonoProfiler *prof, MonoClass *klass) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::ClassLoadMonoVM> ();
	TRACE_EVENT_END (PerfettoConstants::MonoRuntimeCategory.data (), track, [&](perfetto::EventContext ctx) {
		PerfettoSupport::add_name_annotation (ctx, klass);
	});
#endif
}

void
MonodroidRuntime::prof_vtable_loading ([[maybe_unused]] MonoProfiler *prof, MonoVTable *vtable) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::VTableLoadMonoVM> ();
	TRACE_EVENT_BEGIN (
		PerfettoConstants::MonoRuntimeCategory.data (),
		PerfettoSupport::get_event_name (PerfettoConstants::VTableLoadAnnotation),
		track
	);
#endif
}

void
MonodroidRuntime::prof_vtable_loaded ([[maybe_unused]] MonoProfiler *prof, MonoVTable *vtable) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::VTableLoadMonoVM> ();
	TRACE_EVENT_END (PerfettoConstants::MonoRuntimeCategory.data (), track, [&](perfetto::EventContext ctx) {
		PerfettoSupport::add_name_annotation (ctx, vtable);
	});
#endif
}

void
MonodroidRuntime::prof_method_begin_invoke ([[maybe_unused]] MonoProfiler *prof, MonoMethod *method) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::MethodInvokeMonoVM> ();
	TRACE_EVENT_BEGIN (
		PerfettoConstants::MonoRuntimeCategory.data (),
		PerfettoSupport::get_event_name (PerfettoConstants::MethodInvokeAnnotation),
		track
	);
#endif
}

void
MonodroidRuntime::prof_method_end_invoke ([[maybe_unused]] MonoProfiler *prof, MonoMethod *method) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::MethodInvokeMonoVM> ();
	TRACE_EVENT_END (PerfettoConstants::MonoRuntimeCategory.data (), track, [&](perfetto::EventContext ctx) {
		PerfettoSupport::add_name_annotation (ctx, method);
	});
#endif
}

void
MonodroidRuntime::prof_method_enter ([[maybe_unused]] MonoProfiler *prof, MonoMethod *method, [[maybe_unused]] MonoProfilerCallContext *context) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::MethodInnerMonoVM> ();
	TRACE_EVENT_BEGIN (
		PerfettoConstants::MonoRuntimeCategory.data (),
		PerfettoSupport::get_event_name (PerfettoConstants::MethodRunTimeAnnotation),
		track
	);
#endif
}

void
MonodroidRuntime::prof_method_leave ([[maybe_unused]] MonoProfiler *prof, MonoMethod *method, [[maybe_unused]] MonoProfilerCallContext *context) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::MethodInnerMonoVM> ();
	TRACE_EVENT_END (PerfettoConstants::MonoRuntimeCategory.data (), track, [&](perfetto::EventContext ctx) {
		PerfettoSupport::add_name_annotation (ctx, method);
	});
#endif
}

void
MonodroidRuntime::prof_monitor_contention ([[maybe_unused]] MonoProfiler *prof, MonoObject *object) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::MonitorContentionMonoVM> ();
	TRACE_EVENT_BEGIN (
		PerfettoConstants::MonoRuntimeCategory.data (),
		PerfettoSupport::get_event_name (PerfettoConstants::MonitorContentionAnnotation),
		track
	);
#endif
}

void
MonodroidRuntime::prof_monitor_acquired ([[maybe_unused]] MonoProfiler *prof, MonoObject *object) noexcept
{
#if defined(PERFETTO_ENABLED)
	auto track = PerfettoSupport::get_name_annotated_thread_track<PerfettoTrackId::MonitorContentionMonoVM> ();
	TRACE_EVENT_END (PerfettoConstants::MonoRuntimeCategory.data (), track, [&](perfetto::EventContext ctx) {
		PerfettoSupport::add_name_annotation (ctx, object);
	});
#endif
}
