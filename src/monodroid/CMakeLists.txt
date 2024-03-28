cmake_minimum_required(VERSION 3.19)

#
# MUST be included before project()!
#
include("../../build-tools/cmake/xa_preamble.cmake")

project(
  monodroid
  VERSION ${XA_VERSION}
  DESCRIPTION "Xamarin.Android native runtime"
  HOMEPAGE_URL "https://github.com/xamarin/xamarin-android"
  LANGUAGES CXX C
)

#
# MUST be included after project()!
#
include("../../build-tools/cmake/xa_common.cmake")

option(ENABLE_TIMING "Build with timing support" OFF)

# Environment checks

if(NOT DEFINED MONO_PATH)
  message(FATAL_ERROR "Please set the MONO_PATH variable on command line (-DMONO_PATH=PATH)")
else()
  string(REPLACE "\\" "/" MONO_PATH ${MONO_PATH})
endif()

if(NOT DEFINED CONFIGURATION)
  message(FATAL_ERROR "Please set the CONFIGURATION variable on command line (-DCONFIGURATION=name)")
endif()

if(NOT DEFINED CMAKE_BUILD_TYPE)
  message(FATAL_ERROR "Please set the CMAKE_BUILD_TYPE variable on command line (-DCMAKE_BUILD_TYPE=name)")
endif()

if(NOT DEFINED XA_BUILD_CONFIGURATION)
  message(FATAL_ERROR "Please set the XA_BUILD_CONFIGURATION variable on command line (-DXA_BUILD_CONFIGURATION=name)")
endif()

if(NOT DEFINED XA_LIB_TOP_DIR)
  message(FATAL_ERROR "Please set the XA_LIB_TOP_DIR variable on command line (-DXA_LIB_TOP_DIR=path)")
endif()

if(NOT DEFINED LIBUNWIND_SOURCE_DIR)
  message(FATAL_ERROR "Please set the LIBUNWIND_SOURCE_DIR on command line (-DLIBUNWIND_SOURCE_DIR=path)")
endif()

if(NOT DEFINED LIBUNWIND_HEADERS_DIR)
  message(FATAL_ERROR "Please set the LIBUNWIND_HEADERS_DIR on command line (-DLIBUNWIND_HEADERS_DIR=path)")
endif()

# Needed modules

include(CheckIncludeFile)
include(CheckCXXSymbolExists)

# Paths

set(SOURCES_DIR ${CMAKE_SOURCE_DIR}/jni)
set(BIONIC_SOURCES_DIR "${REPO_ROOT_DIR}/src-ThirdParty/bionic")
set(LZ4_SRC_DIR "${EXTERNAL_DIR}/lz4/lib")
set(LZ4_INCLUDE_DIR ${LZ4_SRC_DIR})
set(XA_BIN_DIR "${REPO_ROOT_DIR}/bin/${XA_BUILD_CONFIGURATION}")
set(ROBIN_MAP_DIR "${EXTERNAL_DIR}/robin-map")

set(LZ4_SOURCES
  "${LZ4_SRC_DIR}/lz4.c"
  )

# Include directories
include_directories(${CMAKE_CURRENT_BINARY_DIR}/include/ ${CMAKE_SOURCE_DIR}/include)

# The SYSTEM which will make clang skip warnings for the headers there. Since we can't do
# much about them, we can just as well avoid cluttered build output.
include_directories(SYSTEM ${ROBIN_MAP_DIR}/include)
include_directories(SYSTEM ${LZ4_INCLUDE_DIR})

include_directories("jni")
include_directories("${XA_BIN_DIR}/include")
include_directories("${XA_BIN_DIR}/include/${ANDROID_ABI}/eglib")

# This is to allow "release" builds with Debug build type and vice versa
include_directories("../../bin/${CONFIGURATION}/include")
include_directories("../../bin/${CONFIGURATION}/include/${ANDROID_ABI}/eglib")
include_directories("${MONO_PATH}/mono/eglib")

# Common preparation code
include("../../build-tools/cmake/xa_macros.cmake")

xa_common_prepare()
xa_macos_prepare_arm64()

# Compiler defines

add_compile_definitions(TSL_NO_EXCEPTIONS)
add_compile_definitions(HAVE_CONFIG_H)
add_compile_definitions(JI_DLL_EXPORT)
add_compile_definitions(MONO_DLL_EXPORT)
add_compile_definitions(NET)
add_compile_definitions(JI_NO_VISIBILITY)

if(DONT_INLINE)
  add_compile_definitions(NO_INLINE)
endif()

if(DEBUG_BUILD AND NOT DISABLE_DEBUG)
  add_compile_definitions(DEBUG)
endif()

if (ENABLE_TIMING)
  add_compile_definitions(MONODROID_TIMING)
endif()

add_compile_definitions(HAVE_LZ4)

# Compiler and linker flags
set(LINK_LIBS
  -lmonosgen-2.0
  -llog
)

#
# -Wformat-nonliteral is disabled as it's not very practical, because we use proxy functions to
# pass formats to the final Android logger functions. The Android functions have attributes that
# cause warnings similar to:
#
#  warning G4FD2E6FD: format string is not a string literal [-Wformat-nonliteral]
#
# The warning is, in general, a good practice because the compiler can verify the printf format
# at compile time, but in our case it's not very useful.
#
set(LOCAL_COMMON_COMPILER_ARGS
  -Wall
  -Wconversion
  -Wdeprecated
  -Wduplicated-branches
  -Wduplicated-cond
  -Werror=format-security
  -Werror=return-type
  -Wextra
  -Wformat-security
  -Wformat=2
  -Wno-format-nonliteral
  -Wimplicit-fallthrough
  -Wmisleading-indentation
  -Wnull-dereference
  -Wpointer-arith
  -Wshadow
  -Wsign-compare
  -Wtrampolines
  -Wuninitialized
  -fstrict-flex-arrays=3
  )

# Add some options to increase security. They may mildly affect performance but they won't be big, because the features are
# assisted by the hardware.
if((CMAKE_ANDROID_ARCH_ABI STREQUAL "x86") OR (CMAKE_ANDROID_ARCH_ABI STREQUAL "x86_64"))
  # -fcf-protection=full: Enable control flow protection to counter Return Oriented Programming (ROP) and Jump Oriented Programming (JOP) attacks on many x86 architectures
  list(APPEND LOCAL_COMMON_COMPILER_ARGS
    -fcf-protection=full
  )
endif()

if(CMAKE_ANDROID_ARCH_ABI STREQUAL "arm64-v8a")
  # -mbranch-protection=standard: Enable branch protection to counter Return Oriented Programming (ROP) and Jump Oriented Programming (JOP) attacks on AArch64
  # In clang -mbranch-protection=standard is equivalent to -mbranch-protection=bti+pac-ret and invokes the AArch64 Branch Target Identification (BTI) and Pointer Authentication using key A (pac-ret)
  list(APPEND LOCAL_COMMON_COMPILER_ARGS
    -mbranch-protection=standard
  )
endif()

if(COMPILER_DIAG_COLOR)
  list(APPEND LOCAL_COMMON_COMPILER_ARGS
    -fdiagnostics-color=always
    -fcolor-diagnostics
    )
endif()

set(LOCAL_COMMON_LINKER_ARGS
  -shared
  -fpic
  -fstack-clash-protection
)

if (ENABLE_CLANG_ASAN OR ENABLE_CLANG_UBSAN)
  list(APPEND LOCAL_COMMON_COMPILER_ARGS
    -fno-omit-frame-pointer
    -fno-optimize-sibling-calls
  )
endif()

unset(SANITIZER_FLAGS)
if (ENABLE_CLANG_ASAN)
  set(SANITIZER_FLAGS -fsanitize=address)
  set(CHECKED_BUILD_INFIX "-checked+asan")
elseif(ENABLE_CLANG_UBSAN)
  set(SANITIZER_FLAGS -fsanitize=undefined)
  set(CHECKED_BUILD_INFIX "-checked+ubsan")
endif()

if(SANITIZER_FLAGS)
  message(STATUS "Got sanitizer: ${SANITIZER_FLAGS}")

  list(APPEND LOCAL_COMMON_COMPILER_ARGS ${SANITIZER_FLAGS})
  list(APPEND LOCAL_COMMON_LINKER_ARGS ${SANITIZER_FLAGS})
  list(APPEND CMAKE_REQUIRED_LINK_OPTIONS ${SANITIZER_FLAGS})
endif()

if(STRIP_DEBUG)
  list(APPEND LOCAL_COMMON_LINKER_ARGS LINKER:-S)
else()
  # When not stripping symbols, we likely want to have precise stack traces, so
  # we won't omit frame pointers
  list(APPEND LOCAL_COMMON_COMPILER_ARGS
    -fno-omit-frame-pointer
    -fno-limit-debug-info
  )
endif()

# Parameters to both functions are (all required):
#
#  <C++ flags variable name> <C flags variable name> <extra C++ flags> <extra C flags>
#
xa_check_compiler_flags(XA_CXX_FLAGS XA_C_FLAGS "${LOCAL_COMMON_COMPILER_ARGS}" "${LOCAL_COMMON_COMPILER_ARGS}")
xa_check_linker_flags(XA_CXX_LINKER_FLAGS XA_C_LINKER_FLAGS "${LOCAL_COMMON_LINKER_ARGS}" "${LOCAL_COMMON_LINKER_ARGS}")

add_compile_options("$<$<COMPILE_LANGUAGE:CXX>:${XA_CXX_FLAGS}>")
add_compile_options("$<$<COMPILE_LANGUAGE:C>:${XA_C_FLAGS}>")

add_link_options("$<$<COMPILE_LANGUAGE:CXX>:${XA_CXX_LINKER_FLAGS}>")
add_link_options("$<$<COMPILE_LANGUAGE:C>:${XA_C_LINKER_FLAGS}>")

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
link_directories("${NET_RUNTIME_DIR}/native")

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

set(XAMARIN_INTERNAL_API_LIB xa-internal-api${CHECKED_BUILD_INFIX})
set(XAMARIN_DEBUG_APP_HELPER_LIB xamarin-debug-app-helper${CHECKED_BUILD_INFIX})
set(XAMARIN_APP_STUB_LIB xamarin-app)
set(XAMARIN_NATIVE_TRACING_LIB xamarin-native-tracing)
set(XAMARIN_MARSHAL_METHODS_TRACING_LIB marshal-methods-tracing)

string(TOLOWER ${CMAKE_BUILD_TYPE} XAMARIN_MONO_ANDROID_SUFFIX)
set(XAMARIN_MONO_ANDROID_LIB "mono-android${CHECKED_BUILD_INFIX}.${XAMARIN_MONO_ANDROID_SUFFIX}")

set(XAMARIN_MONODROID_SOURCES
  ${JAVA_INTEROP_SRC_PATH}/java-interop-dlfcn.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop-mono.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop-util.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop-util.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop.cc
  ${LZ4_SOURCES}
  ${XA_SHARED_SOURCES}
  ${SOURCES_DIR}/android-system.cc
  ${SOURCES_DIR}/basic-android-system.cc
  ${SOURCES_DIR}/basic-utilities.cc
  ${SOURCES_DIR}/cpu-arch-detect.cc
  ${SOURCES_DIR}/debug-constants.cc
  ${SOURCES_DIR}/debug.cc
  ${SOURCES_DIR}/embedded-assemblies-zip.cc
  ${SOURCES_DIR}/embedded-assemblies.cc
  ${SOURCES_DIR}/globals.cc
  ${SOURCES_DIR}/jni-remapping.cc
  ${SOURCES_DIR}/logger.cc
  ${SOURCES_DIR}/mono-log-adapter.cc
  ${SOURCES_DIR}/monodroid-glue.cc
  ${SOURCES_DIR}/monodroid-networkinfo.cc
  ${SOURCES_DIR}/monodroid-tracing.cc
  ${SOURCES_DIR}/monovm-properties.cc
  ${SOURCES_DIR}/osbridge.cc
  ${SOURCES_DIR}/pinvoke-override-api.cc
  ${SOURCES_DIR}/shared-constants.cc
  ${SOURCES_DIR}/timezones.cc
  ${SOURCES_DIR}/timing-internal.cc
  ${SOURCES_DIR}/util.cc
  ${SOURCES_DIR}/xamarin_getifaddrs.cc
)

if(NOT DEBUG_BUILD)
  list(APPEND XAMARIN_MONODROID_SOURCES
    ${SOURCES_DIR}/xamarin-android-app-context.cc
  )
endif()

if(NOT USES_LIBSTDCPP)
  list(APPEND XAMARIN_MONODROID_SOURCES
    ${BIONIC_SOURCES_DIR}/cxa_guard.cc
    ${SHARED_SOURCES_DIR}/cxx-abi/string.cc
    ${SHARED_SOURCES_DIR}/cxx-abi/terminate.cc
  )
endif()

set(NATIVE_TRACING_SOURCES
  ${SHARED_SOURCES_DIR}/cxx-abi/string.cc
# ${SHARED_SOURCES_DIR}/cxx-abi/terminate.cc
  ${TRACING_SOURCES_DIR}/native-tracing.cc
  ${XA_SHARED_SOURCES}
)

set(MARSHAL_METHODS_TRACING_SOURCES
  ${SHARED_SOURCES_DIR}/cxx-abi/string.cc
  ${SHARED_SOURCES_DIR}/cxx-abi/terminate.cc
  ${SOURCES_DIR}/marshal-methods-tracing.cc
  ${XA_SHARED_SOURCES}
)

set(XAMARIN_APP_STUB_SOURCES
  ${SOURCES_DIR}/application_dso_stub.cc
  )

set(XAMARIN_DEBUG_APP_HELPER_SOURCES
  ${SOURCES_DIR}/basic-android-system.cc
  ${SOURCES_DIR}/basic-utilities.cc
  ${SOURCES_DIR}/cpu-arch-detect.cc
  ${SOURCES_DIR}/debug-app-helper.cc
  ${SOURCES_DIR}/shared-constants.cc
  ${XA_SHARED_SOURCES}
  )

set(XAMARIN_STUB_LIB_SOURCES
  libstub/stub.cc
)

# Build
configure_file(jni/host-config.h.in ${CMAKE_CURRENT_BINARY_DIR}/include/host-config.h)

add_library(
  ${XAMARIN_APP_STUB_LIB}
  SHARED
  ${XAMARIN_APP_STUB_SOURCES}
)

target_link_options(
  ${XAMARIN_APP_STUB_LIB}
  PRIVATE ${XA_DEFAULT_SYMBOL_VISIBILITY}
)

set_target_properties(
  ${XAMARIN_APP_STUB_LIB}
  PROPERTIES
  LIBRARY_OUTPUT_DIRECTORY "${XA_LIBRARY_OUTPUT_DIRECTORY}/${CMAKE_BUILD_TYPE}"
)

if(DEBUG_BUILD)
  add_library(
    ${XAMARIN_DEBUG_APP_HELPER_LIB}
    SHARED
    ${XAMARIN_DEBUG_APP_HELPER_SOURCES}
  )

  target_link_options(
    ${XAMARIN_DEBUG_APP_HELPER_LIB}
    PRIVATE ${XA_SYMBOL_VISIBILITY}
  )

  target_link_libraries(
    ${XAMARIN_DEBUG_APP_HELPER_LIB}
    -ldl
  )

  target_link_libraries(
    ${XAMARIN_DEBUG_APP_HELPER_LIB}
    -llog
  )

  target_compile_options(
    ${XAMARIN_DEBUG_APP_HELPER_LIB}
    PRIVATE ${XA_SYMBOL_VISIBILITY}
  )

  target_compile_definitions(
    ${XAMARIN_DEBUG_APP_HELPER_LIB}
    PUBLIC -DDEBUG_APP_HELPER
  )
endif()

add_library(
  ${XAMARIN_MONO_ANDROID_LIB}
  SHARED ${XAMARIN_MONODROID_SOURCES}
)

if(NOT DEBUG_BUILD)
  set(CPP_ABI_PATH ${CMAKE_SYSROOT}/usr/lib/${SYSROOT_ABI_LIB_DIR}/${TOOLCHAIN_TRIPLE}/libc++abi.a)

  add_library(
    ${XAMARIN_NATIVE_TRACING_LIB}
    SHARED ${NATIVE_TRACING_SOURCES}
  )

  target_include_directories(
    ${XAMARIN_NATIVE_TRACING_LIB} BEFORE
    PRIVATE
    ${LIBUNWIND_SOURCE_DIR}/include
    ${LIBUNWIND_HEADERS_DIR}/${CMAKE_ANDROID_ARCH_ABI}
    ${NDK_CXX_LIBCPPABI_SOURCE_PATH}/include
  )

  target_compile_options(
    ${XAMARIN_NATIVE_TRACING_LIB}
    PRIVATE
    # Avoid the 'warning: dynamic exception specifications are deprecated' warning from libc++ headers
    -Wno-deprecated-dynamic-exception-spec
    ${XA_DEFAULT_SYMBOL_VISIBILITY}
    # Prevent genration of the .eh_frame section (we don't use exceptions and don't need it)
    -fno-asynchronous-unwind-tables
  )

  target_link_options(
    ${XAMARIN_NATIVE_TRACING_LIB}
    PRIVATE ${XA_DEFAULT_SYMBOL_VISIBILITY}
  )

  target_link_libraries(
    ${XAMARIN_NATIVE_TRACING_LIB}
    PRIVATE
    -llog
    ${CPP_ABI_PATH}
    ${XA_LIBRARY_OUTPUT_DIRECTORY}/libunwind_xamarin.a
  )

  target_compile_definitions(
    ${XAMARIN_NATIVE_TRACING_LIB}
    PRIVATE
    XAMARIN_TRACING
  )

  add_library(
    ${XAMARIN_MARSHAL_METHODS_TRACING_LIB}
    STATIC ${MARSHAL_METHODS_TRACING_SOURCES}
  )

  target_include_directories(
    ${XAMARIN_MARSHAL_METHODS_TRACING_LIB} BEFORE
    PRIVATE
    ${LIBUNWIND_SOURCE_DIR}/include
    ${LIBUNWIND_HEADERS_DIR}/${CMAKE_ANDROID_ARCH_ABI}
  )

  target_compile_definitions(
    ${XAMARIN_MARSHAL_METHODS_TRACING_LIB}
    PRIVATE
    XAMARIN_TRACING
  )

  target_compile_options(
    ${XAMARIN_MARSHAL_METHODS_TRACING_LIB}
    PRIVATE
    # Prevent genration of the .eh_frame section (we don't use exceptions and don't need it)
    -fno-asynchronous-unwind-tables
  )
endif()

# Ugly, but this is the only way to change LZ4 symbols visibility without modifying lz4.h
set(LZ4_VISIBILITY_OPTS "-DLZ4LIB_VISIBILITY=__attribute__ ((visibility (\"hidden\")))")

target_compile_options(
  ${XAMARIN_MONO_ANDROID_LIB}
  PRIVATE ${XA_DEFAULT_SYMBOL_VISIBILITY} "${LZ4_VISIBILITY_OPTS}"
  )

target_include_directories(
  ${XAMARIN_MONO_ANDROID_LIB} BEFORE
  PRIVATE
  ${LIBUNWIND_SOURCE_DIR}/include
  ${LIBUNWIND_HEADERS_DIR}/${CMAKE_ANDROID_ARCH_ABI}
)

set(DEBUG_HELPER_LINK_LIBS "-ldl")

target_link_options(
  ${XAMARIN_MONO_ANDROID_LIB}
  PRIVATE ${XA_DEFAULT_SYMBOL_VISIBILITY}
  )

target_link_libraries(
  ${XAMARIN_MONO_ANDROID_LIB}
  ${LINK_LIBS} xamarin-app
  )

if(NOT ANALYZERS_ENABLED)
  macro(xa_add_stub_library _libname)
    add_library(
      ${_libname}
      SHARED ${XAMARIN_STUB_LIB_SOURCES}
    )

    string(TOUPPER ${_libname} _libname_uc)
    target_compile_definitions(
      ${_libname}
      PRIVATE STUB_LIB_NAME=lib${_libname} IN_LIB${_libname_uc}
    )

    target_compile_options(
      ${_libname}
      PRIVATE -nostdlib -fno-exceptions -fno-rtti
    )

    target_link_options(
      ${_libname}
      PRIVATE -nostdlib -fno-exceptions -fno-rtti
    )

    set_target_properties(
      ${_libname}
      PROPERTIES
      LIBRARY_OUTPUT_DIRECTORY "${XA_LIBRARY_STUBS_OUTPUT_DIRECTORY}"
    )
  endmacro()

  xa_add_stub_library(c)
  xa_add_stub_library(m)

  # These two are used by the marshal methods tracing library when linking libxamarin-app.so
  xa_add_stub_library(log)
  xa_add_stub_library(dl)
endif()
