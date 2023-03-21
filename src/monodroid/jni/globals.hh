// This is a -*- C++ -*- header
#ifndef __GLOBALS_H
#define __GLOBALS_H

#include "util.hh"
#include "timing.hh"

#include "debug.hh"
#include "embedded-assemblies.hh"
#include "designer-assemblies.hh"
#include "monodroid-glue-internal.hh"
#include "cppcompat.hh"

extern xamarin::android::Debug debug;
extern xamarin::android::Util utils;
extern xamarin::android::internal::AndroidSystem androidSystem;
extern xamarin::android::internal::OSBridge osBridge;
extern xamarin::android::internal::EmbeddedAssemblies embeddedAssemblies;
extern xamarin::android::internal::MonodroidRuntime monodroidRuntime;
extern xamarin::android::Timing *timing;

#endif // !__GLOBALS_H
