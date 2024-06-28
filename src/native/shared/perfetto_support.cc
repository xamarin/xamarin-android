#include "perfetto_support.hh"

using namespace xamarin::android;

PERFETTO_TRACK_EVENT_STATIC_STORAGE();

void PerfettoSupport::set_track_event_descriptor (perfetto::Track &track, perfetto::protos::gen::TrackDescriptor &desc)
{
	perfetto::TrackEvent::SetTrackDescriptor (track, desc);
}
