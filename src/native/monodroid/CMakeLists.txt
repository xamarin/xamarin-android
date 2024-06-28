option(ENABLE_TIMING "Build with timing support" OFF)

# Needed modules

include(CheckIncludeFile)
include(CheckCXXSymbolExists)

# Paths

set(BIONIC_SOURCES_DIR "${REPO_ROOT_DIR}/src-ThirdParty/bionic")
set(ROBIN_MAP_DIR "${EXTERNAL_DIR}/robin-map")

if(DEBUG_BUILD)
  # Convince NDK to really optimize our Debug builds. Without this, NDK's cmake toolchain definition
  # will force a -O0 on us and our "debug" build is not really for debugging of our native code but
  # rather for "debug" builds of user apps - it has extra code but it has to be as fast as possible.
  set(XA_COMPILER_FLAGS_DEBUG "-fno-limit-debug-info -O2")
  set(CMAKE_C_FLAGS_DEBUG ${XA_COMPILER_FLAGS_DEBUG})
  set(CMAKE_CXX_FLAGS_DEBUG ${XA_COMPILER_FLAGS_DEBUG})
endif()

# Library directories
set(XA_LIBRARY_OUTPUT_DIRECTORY "${XA_LIB_TOP_DIR}/lib/${ANDROID_RID}")
set(XA_LIBRARY_STUBS_OUTPUT_DIRECTORY "${XA_LIB_TOP_DIR}/libstubs/${ANDROID_RID}")

# Header checks

if(ENABLE_CLANG_UBSAN OR ENABLE_CLANG_ASAN)
  set(OLD_CMAKE_REQUIRED_FLAGS "${CMAKE_REQUIRED_FLAGS}")
  set(CMAKE_REQUIRED_FLAGS "${CMAKE_REQUIRED_FLAGS} -llog")
  string(STRIP "${CMAKE_REQUIRED_FLAGS}" CMAKE_REQUIRED_FLAGS)
endif()

check_include_file("linux/netlink.h" HAVE_LINUX_NETLINK_H)
check_include_file("linux/rtnetlink.h" HAVE_LINUX_RTNETLINK_H)
check_include_file("linux/if_arp.h" HAVE_LINUX_IF_ARP_H)

if(ENABLE_CLANG_UBSAN OR ENABLE_CLANG_ASAN)
  set(CMAKE_REQUIRED_FLAGS "${OLD_CMAKE_REQUIRED_FLAGS}")
endif()

# Sources
string(TOLOWER ${CMAKE_BUILD_TYPE} XAMARIN_MONO_ANDROID_SUFFIX)
set(XAMARIN_MONO_ANDROID_LIB "mono-android${CHECKED_BUILD_INFIX}.${XAMARIN_MONO_ANDROID_SUFFIX}")
set(XAMARIN_MONO_ANDROID_PERFETTO_LIB "mono-android-perfetto.${XAMARIN_MONO_ANDROID_SUFFIX}")

list(APPEND XAMARIN_MONODROID_COMMON_SOURCES
  debug-constants.cc
  debug.cc
  embedded-assemblies-zip.cc
  embedded-assemblies.cc
  globals.cc
  jni-remapping.cc
  mono-log-adapter.cc
  monodroid-glue.cc
  monodroid-networkinfo.cc
  monodroid-profiling.cc
  monodroid-tracing.cc
  monovm-properties.cc
  osbridge.cc
  pinvoke-override-api.cc
  runtime-util.cc
  timing.cc
  timezones.cc
  timing-internal.cc
  xamarin_getifaddrs.cc
)

list(APPEND LOCAL_CLANG_CHECK_SOURCES
  ${XAMARIN_MONODROID_COMMON_SOURCES}
)

if(NOT DEBUG_BUILD)
  list(APPEND XAMARIN_MONODROID_COMMON_SOURCES
    xamarin-android-app-context.cc
  )

  list(APPEND LOCAL_CLANG_CHECK_SOURCES
    xamarin-android-app-context.cc
  )
endif()

list(APPEND XAMARIN_MONODROID_SOURCES
  ${XAMARIN_MONODROID_COMMON_SOURCES}
)

list(APPEND XAMARIN_MONODROID_PERFETTO_SOURCES
  ${XAMARIN_MONODROID_COMMON_SOURCES}
)

if(NOT USES_LIBSTDCPP)
  list(APPEND XAMARIN_MONODROID_SOURCES
    ${BIONIC_SOURCES_DIR}/cxa_guard.cc
  )
endif()

add_clang_check_sources("${LOCAL_CLANG_CHECK_SOURCES}")

# Build
configure_file(host-config.h.in ${CMAKE_CURRENT_BINARY_DIR}/include/host-config.h)

macro(set_common_monodroid_properties TARGET)
  target_compile_definitions(
    ${TARGET}
    PRIVATE
    HAVE_CONFIG_H
    HAVE_LZ4
    JI_DLL_EXPORT
    JI_NO_VISIBILITY
    MONO_DLL_EXPORT
    NET
    TSL_NO_EXCEPTIONS
  )

  if(DONT_INLINE)
    target_compile_definitions(
      ${TARGET}
      PRIVATE
      NO_INLINE
    )
  endif()

  if(DEBUG_BUILD AND NOT DISABLE_DEBUG)
    target_compile_definitions(
      ${TARGET}
      PRIVATE
      DEBUG
    )
  endif()

  target_compile_options(
    ${TARGET}
    PRIVATE
    ${XA_DEFAULT_SYMBOL_VISIBILITY}
  )

  target_include_directories(
    ${TARGET} BEFORE
    PRIVATE
    ${CMAKE_CURRENT_BINARY_DIR}/include/
    ${NATIVE_TRACING_INCLUDE_DIRS}
    ${EXTERNAL_DIR}
    ${ROBIN_MAP_DIR}/include
  )

  target_include_directories(
    ${TARGET}
    SYSTEM PRIVATE
    ${SYSROOT_CXX_INCLUDE_DIR}
    ${MONO_RUNTIME_INCLUDE_DIR}
    ${LIBUNWIND_INCLUDE_DIRS}
  )

  target_link_directories(
    ${TARGET}
    PRIVATE
    ${NET_RUNTIME_DIR}/native
  )

  target_link_options(
    ${TARGET}
    PRIVATE
    ${XA_DEFAULT_SYMBOL_VISIBILITY}
    ${XA_COMMON_CXX_LINKER_ARGS}
    ${XA_CXX_DSO_LINKER_ARGS}
  )
endmacro()

if(NOT ENABLE_PERFETTO)
  add_library(
    ${XAMARIN_MONO_ANDROID_LIB}
    SHARED ${XAMARIN_MONODROID_SOURCES}
  )

  set_common_monodroid_properties(${XAMARIN_MONO_ANDROID_LIB})
  xa_add_compile_definitions(${XAMARIN_MONO_ANDROID_LIB})

  if (ENABLE_TIMING)
    target_compile_definitions(
      ${XAMARIN_MONO_ANDROID_LIB}
      PRIVATE
      MONODROID_TIMING
    )
  endif()

  target_link_libraries(
    ${XAMARIN_MONO_ANDROID_LIB}
    ${LINK_LIBS}
    xa::xamarin-app
    ${SHARED_LIB_NAME}
    xa::runtime-base
    xa::java-interop
    xa::lz4
    -lmonosgen-2.0
    -llog
  )
else()
  add_library(
    ${XAMARIN_MONO_ANDROID_PERFETTO_LIB}
    SHARED ${XAMARIN_MONODROID_PERFETTO_SOURCES}
  )

  set_common_monodroid_properties(${XAMARIN_MONO_ANDROID_PERFETTO_LIB})
  xa_add_compile_definitions(${XAMARIN_MONO_ANDROID_PERFETTO_LIB})

  target_link_libraries(
    ${XAMARIN_MONO_ANDROID_PERFETTO_LIB}
    ${LINK_LIBS}
    xa::xamarin-app
    xa::shared-no-abi
    xa::runtime-base
    xa::java-interop
    xa::lz4
    -lmonosgen-2.0
    -llog
  )
endif()
