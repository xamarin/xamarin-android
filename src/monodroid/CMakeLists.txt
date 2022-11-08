cmake_minimum_required(VERSION 3.18.1)

#
# MUST be included before project()!
#
include("../../build-tools/cmake/xa_common.cmake")

#
# Read product version
#
file(STRINGS "../../Directory.Build.props" XA_PRODUCT_VERSION_XML REGEX "^[ \t]*<ProductVersion>(.*)</ProductVersion>")
string(REGEX REPLACE "^[ \t]*<ProductVersion>(.*)</ProductVersion>" "\\1" XA_VERSION "${XA_PRODUCT_VERSION_XML}")

project(
  monodroid
  VERSION ${XA_VERSION}
  DESCRIPTION "Xamarin.Android native runtime"
  HOMEPAGE_URL "https://github.com/xamarin/xamarin-android"
  LANGUAGES CXX C
  )

option(COMPILER_DIAG_COLOR "Show compiler diagnostics/errors in color" ON)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

if(WIN32 AND NOT MINGW)
  set(CCACHE_OPTION_DEFAULT OFF)
else()
  set(CCACHE_OPTION_DEFAULT ON)
endif()

option(ENABLE_CLANG_ASAN "Enable the clang AddressSanitizer support" OFF)
option(ENABLE_CLANG_UBSAN "Enable the clang UndefinedBehaviorSanitizer support" OFF)

if(ENABLE_CLANG_ASAN OR ENABLE_CLANG_UBSAN)
  set(STRIP_DEBUG_DEFAULT OFF)
else()
  set(STRIP_DEBUG_DEFAULT ON)
endif()

option(ENABLE_NET "Enable compilation for .NET 6+" OFF)
option(ENABLE_TIMING "Build with timing support" OFF)
option(STRIP_DEBUG "Strip debugging information when linking" ${STRIP_DEBUG_DEFAULT})
option(DISABLE_DEBUG "Disable the built-in debugging code" OFF)
option(USE_CCACHE "Use ccache, if found, to speed up recompilation" ${CCACHE_OPTION_DEFAULT})

if((MINGW OR NOT WIN32) AND USE_CCACHE)
  if(CMAKE_CXX_COMPILER MATCHES "/ccache/")
    message(STATUS "ccache: compiler already uses ccache")
  else()
    find_program(CCACHE ccache)
    if(CCACHE)
      set(CMAKE_CXX_COMPILER_LAUNCHER "${CCACHE}")
      set(CMAKE_C_COMPILER_LAUNCHER "${CCACHE}")
      message(STATUS "ccache: compiler will be lauched with ${CCACHE}")
    endif()
  endif()
endif()

if(CMAKE_BUILD_TYPE STREQUAL Debug)
  set(DEBUG_BUILD True)
else()
  set(DEBUG_BUILD False)
endif()

if(ANDROID)
  if(ANDROID_STL STREQUAL none)
    set(USES_LIBSTDCPP False)
  else()
    set(USES_LIBSTDCPP True)
  endif()
endif()

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

if(NOT ANDROID)
  if (NOT DEFINED JDK_INCLUDE)
    message(FATAL_ERROR "Please set the JDK_INCLUDE variable on command line (-DJDK_INCLUDE)")
  endif()
endif()

if(MINGW AND NOT WIN32)
  if(NOT DEFINED MINGW_DEPENDENCIES_ROOT_DIR)
    message(FATAL_ERROR "Please set the MINGW_DEPENDENCIES_ROOT_DIR variable on command line (-DMINGW_DEPENDENCIES_ROOT_DIR=PATH)")
  endif()

  if(DEFINED MINGW_TARGET_32 AND DEFINED MINGW_TARGET_64)
    message(FATAL_ERROR "Only one of the MINGW_TARGET_32 or MINGW_TARGET_64 variables can be defined")
  endif()
  if(NOT DEFINED MINGW_TARGET_32 AND NOT DEFINED MINGW_TARGET_64)
    message(FATAL_ERROR "Either MINGW_TARGET_32 or MINGW_TARGET_64 variable must be defined")
  endif()
endif()

# Needed modules

include(CheckIncludeFile)
include(CheckCXXSymbolExists)

# General config

if(CMAKE_HOST_SYSTEM_NAME STREQUAL Linux)
  set(IS_LINUX True)
else()
  set(IS_LINUX False)
endif()

if(CMAKE_HOST_SYSTEM_NAME STREQUAL Darwin)
  set(IS_MACOS True)
else()
  set(IS_MACOS False)
endif()

if(NOT ANDROID)
  if(APPLE)
    set(CMAKE_MACOSX_RPATH 1)
    set(HOST_BUILD_NAME "host-Darwin")
  endif()

  if(IS_LINUX AND NOT MINGW)
    set(HOST_BUILD_NAME "host-Linux")
  endif()
endif()

# Paths

set(EXTERNAL_DIR "../../external")
set(JAVA_INTEROP_SRC_PATH "${EXTERNAL_DIR}/Java.Interop/src/java-interop")
set(SOURCES_DIR ${CMAKE_SOURCE_DIR}/jni)
set(BIONIC_SOURCES_DIR "../../src-ThirdParty/bionic")
set(LZ4_SRC_DIR "${EXTERNAL_DIR}/lz4/lib")
set(LZ4_INCLUDE_DIR ${LZ4_SRC_DIR})
set(XA_BIN_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../bin/${XA_BUILD_CONFIGURATION}")
set(XA_BUILD_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../bin/Build${XA_BUILD_CONFIGURATION}")
set(ROBIN_MAP_DIR "${EXTERNAL_DIR}/robin-map")

if(NOT ANDROID)
  if(WIN32 OR MINGW)
    if (MINGW_TARGET_32)
      set(ANDROID_ABI "host-mxe-Win32")
    endif()

    if (MINGW_TARGET_64)
      set(ANDROID_ABI "host-mxe-Win64")
    endif()

    set(XA_LIBRARY_OUTPUT_DIRECTORY "${XA_LIB_TOP_DIR}/lib/${ANDROID_ABI}")
  endif()

  if(DEFINED HOST_BUILD_NAME)
    set(XA_LIBRARY_OUTPUT_DIRECTORY "${XA_LIB_TOP_DIR}/lib/${HOST_BUILD_NAME}")
  endif()
endif()

include("${XA_BUILD_DIR}/xa_build_configuration.cmake")

if(ENABLE_NET)
  if(ANDROID_ABI MATCHES "^arm64-v8a")
    set(NET_RUNTIME_DIR "${NETCORE_APP_RUNTIME_DIR_ARM64}")
  elseif(ANDROID_ABI MATCHES "^armeabi-v7a")
    set(NET_RUNTIME_DIR "${NETCORE_APP_RUNTIME_DIR_ARM}")
  elseif(ANDROID_ABI MATCHES "^x86_64")
    set(NET_RUNTIME_DIR "${NETCORE_APP_RUNTIME_DIR_X86_64}")
  elseif(ANDROID_ABI MATCHES "^x86")
    set(NET_RUNTIME_DIR "${NETCORE_APP_RUNTIME_DIR_X86}")
  else()
    message(FATAL "${ANDROID_ABI} is not supported for .NET 6+ builds")
  endif()
endif()

set(LZ4_SOURCES
  "${LZ4_SRC_DIR}/lz4.c"
  )

# Include directories
include_directories(${CMAKE_CURRENT_BINARY_DIR}/include/ ${CMAKE_SOURCE_DIR}/include)
include_directories(${ROBIN_MAP_DIR}/include)

if(NOT ANDROID)
  string(REPLACE " " ";" JDK_INCLUDE_LIST ${JDK_INCLUDE})
  foreach(inc in ${JDK_INCLUDE_LIST})
    include_directories(${inc})
  endforeach()
endif()

if(ANDROID)
  include_directories(${CMAKE_SYSROOT}/usr/include/c++/v1/)
  include_directories(${LZ4_INCLUDE_DIR})
endif()

if(MINGW)
  if(MINGW_TARGET_32)
    include_directories("${MINGW_DEPENDENCIES_ROOT_DIR}/x86/include")
    if(IS_MACOS)
      include_directories("/usr/local/opt/mingw-zlib/usr/i686-w64-mingw32/include")
    endif()
  endif()

  if (MINGW_TARGET_64)
    include_directories("${MINGW_DEPENDENCIES_ROOT_DIR}/x86_64/include")
    if(IS_MACOS)
      include_directories("/usr/local/opt/mingw-zlib/usr/x86_64-w64-mingw32/include")
    endif()
  endif()
endif()

if(NOT ANDROID AND DEFINED HOST_BUILD_NAME)
  include_directories("${XA_BIN_DIR}/include/${HOST_BUILD_NAME}")
  include_directories("${XA_BIN_DIR}/include/${HOST_BUILD_NAME}/eglib")
  include_directories("../../bin/${CONFIGURATION}/include/${HOST_BUILD_NAME}")
  include_directories("../../bin/${CONFIGURATION}/include/${HOST_BUILD_NAME}/eglib")
endif()

if (WIN32)
  include_directories(BEFORE "jni/win32")
endif()

if(ENABLE_NET)
  include_directories("${NET_RUNTIME_DIR}/native/include/mono-2.0")
else()
  include_directories("${XA_BIN_DIR}/include/mono-2.0")
endif()

include_directories("jni")
include_directories("${XA_BIN_DIR}/include")
include_directories("${XA_BIN_DIR}/include/${ANDROID_ABI}/eglib")

# This is to allow "release" builds with Debug build type and vice versa
include_directories("../../bin/${CONFIGURATION}/include")
include_directories("../../bin/${CONFIGURATION}/include/${ANDROID_ABI}/eglib")
include_directories("${MONO_PATH}/mono/eglib")
include_directories("jni/zip")
include_directories("${JAVA_INTEROP_SRC_PATH}")

# Common preparation code
include("../../build-tools/cmake/xa_macros.cmake")

xa_common_prepare()
xa_macos_prepare_arm64()

# Compiler defines

add_compile_definitions(XA_VERSION="${XA_VERSION}")
add_compile_definitions(TSL_NO_EXCEPTIONS)
add_compile_definitions(HAVE_CONFIG_H)
add_compile_definitions(_REENTRANT)
add_compile_definitions(JI_DLL_EXPORT)
add_compile_definitions(MONO_DLL_EXPORT)

if(ENABLE_NET)
  add_compile_definitions(NET)
  add_compile_definitions(JI_NO_VISIBILITY)
endif()

if(DEBUG_BUILD AND NOT DISABLE_DEBUG)
  add_compile_definitions(DEBUG)
endif()

if (ENABLE_TIMING)
  add_compile_definitions(MONODROID_TIMING)
endif()

if(ANDROID)
  add_compile_definitions(HAVE_LZ4)
  add_compile_definitions(PLATFORM_ANDROID)

  if(ANDROID_ABI MATCHES "^(arm64-v8a|x86_64)")
    add_compile_definitions(ANDROID64)
  endif()

  if (ANDROID_NDK_MAJOR LESS 20)
    add_compile_definitions(__ANDROID_API_Q__=29)
  endif()
endif()

if(NOT ANDROID)
  set(CMAKE_REQUIRED_DEFINITIONS "-D__USE_GNU")
  check_cxx_symbol_exists(gettid unistd.h HAVE_GETTID_IN_UNISTD_H)
  if(HAVE_GETTID_IN_UNISTD_H)
    add_compile_definitions(HAVE_GETTID_IN_UNISTD_H)
  endif()

  # MinGW needs it for {v,a}sprintf
  add_compile_definitions(_GNU_SOURCE)

  if(APPLE)
    add_compile_definitions(APPLE_OS_X)
  endif()

  if(IS_LINUX)
    if(NOT MINGW AND NOT WIN32)
      if(EXISTS "/.flatpak-info")
        add_compile_definitions(LINUX_FLATPAK)
      endif()
    endif()
  endif()
endif()

if(WIN32 OR MINGW)
  add_compile_definitions(WINDOWS NTDDI_VERSION=NTDDI_VISTA _WINDOWS _WIN32_WINNT=_WIN32_WINNT_VISTA)
endif()

# Compiler and linker flags
set(LINK_LIBS "")

set(LOCAL_COMMON_COMPILER_ARGS
  -Wall
  -Wconversion
  -Wdeprecated
  -Wduplicated-branches
  -Wduplicated-cond
  -Werror=format-security
  -Werror=return-type
  -Wextra
  -Wformat
  -Wformat-security
  -Wmisleading-indentation
  -Wmismatched-new-delete
  -Wnull-dereference
  -Wpointer-arith
  -Wshadow
  -Wsign-compare
  -Wuninitialized
  )

if(COMPILER_DIAG_COLOR)
  list(APPEND LOCAL_COMMON_COMPILER_ARGS
    -fdiagnostics-color=always
    )
endif()

set(LOCAL_COMMON_LINKER_ARGS "")
if(ANDROID)
  # We don't need thread-safe initialization of statics on Android, our entire init sequence runs in a single thread
  list(APPEND LOCAL_COMMON_COMPILER_ARGS -fno-threadsafe-statics)

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
endif()

if(WIN32 OR MINGW)
  message(STATUS "Win32 or MinGW")
  add_compile_options(-fomit-frame-pointer)
  list(APPEND LOCAL_COMMON_LINKER_ARGS -static -pthread -dynamic)
  list(APPEND LINK_LIBS -lmman -lkernel32 -lmswsock -lwsock32 -lshlwapi -lpsapi -lwinmm)
endif()

if(UNIX)
  list(APPEND LOCAL_COMMON_LINKER_ARGS -shared -fpic)
endif()

if(STRIP_DEBUG)
  list(APPEND LOCAL_COMMON_LINKER_ARGS LINKER:-S)
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
  if(ANDROID)
    # This is specific to clang, enable only for Android builds
    set(XA_COMPILER_FLAGS_DEBUG "-fno-limit-debug-info -O2")
    set(CMAKE_C_FLAGS_DEBUG ${XA_COMPILER_FLAGS_DEBUG})
    set(CMAKE_CXX_FLAGS_DEBUG ${XA_COMPILER_FLAGS_DEBUG})
  endif()
endif()

# Library directories
if(ANDROID)
  if(ENABLE_NET)
    set(XA_LIBRARY_OUTPUT_DIRECTORY "${XA_LIB_TOP_DIR}/lib/${ANDROID_RID}")
    link_directories("${NET_RUNTIME_DIR}/native")
  else()
    set(XA_LIBRARY_OUTPUT_DIRECTORY "${XA_LIB_TOP_DIR}/lib/${ANDROID_ABI}")
    link_directories("${XA_LIBRARY_OUTPUT_DIRECTORY}")
  endif()
endif()

if(WIN32 OR MINGW)
  if(MINGW_TARGET_32)
    link_directories("${MINGW_DEPENDENCIES_ROOT_DIR}/x86/lib")
  endif()

  if(MINGW_TARGET_64)
    link_directories("${MINGW_DEPENDENCIES_ROOT_DIR}/x86_64/lib")
  endif()

  link_directories("${XA_LIBRARY_OUTPUT_DIRECTORY}")
endif()

if(NOT ANDROID AND DEFINED HOST_BUILD_NAME)
  link_directories("${XA_LIB_TOP_DIR}/lib/${HOST_BUILD_NAME}")
endif()

# Header checks

if(ANDROID AND (ENABLE_CLANG_UBSAN OR ENABLE_CLANG_ASAN))
  set(OLD_CMAKE_REQUIRED_FLAGS "${CMAKE_REQUIRED_FLAGS}")
  set(CMAKE_REQUIRED_FLAGS "${CMAKE_REQUIRED_FLAGS} -llog")
  string(STRIP "${CMAKE_REQUIRED_FLAGS}" CMAKE_REQUIRED_FLAGS)
endif()

check_include_file("linux/netlink.h" HAVE_LINUX_NETLINK_H)
check_include_file("linux/rtnetlink.h" HAVE_LINUX_RTNETLINK_H)
check_include_file("linux/if_arp.h" HAVE_LINUX_IF_ARP_H)

if(ANDROID AND (ENABLE_CLANG_UBSAN OR ENABLE_CLANG_ASAN))
  set(CMAKE_REQUIRED_FLAGS "${OLD_CMAKE_REQUIRED_FLAGS}")
endif()

# Sources

set(XAMARIN_INTERNAL_API_LIB xa-internal-api${CHECKED_BUILD_INFIX})
set(XAMARIN_DEBUG_APP_HELPER_LIB xamarin-debug-app-helper${CHECKED_BUILD_INFIX})
set(XAMARIN_APP_STUB_LIB xamarin-app)

string(TOLOWER ${CMAKE_BUILD_TYPE} XAMARIN_MONO_ANDROID_SUFFIX)
set(XAMARIN_MONO_ANDROID_LIB "mono-android${CHECKED_BUILD_INFIX}.${XAMARIN_MONO_ANDROID_SUFFIX}")

set(XAMARIN_MONODROID_SOURCES
  ${SOURCES_DIR}/new_delete.cc
  ${SOURCES_DIR}/android-system.cc
  ${SOURCES_DIR}/basic-android-system.cc
  ${SOURCES_DIR}/basic-utilities.cc
  ${SOURCES_DIR}/cpu-arch-detect.cc
  ${SOURCES_DIR}/debug.cc
  ${SOURCES_DIR}/debug-constants.cc
  ${SOURCES_DIR}/embedded-assemblies.cc
  ${SOURCES_DIR}/embedded-assemblies-zip.cc
  ${SOURCES_DIR}/globals.cc
  ${SOURCES_DIR}/logger.cc
  ${SOURCES_DIR}/jni-remapping.cc
  ${SOURCES_DIR}/monodroid-glue.cc
  ${SOURCES_DIR}/osbridge.cc
  ${SOURCES_DIR}/shared-constants.cc
  ${SOURCES_DIR}/timezones.cc
  ${SOURCES_DIR}/timing-internal.cc
  ${SOURCES_DIR}/util.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop-mono.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop-util.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop-dlfcn.cc
  )

if(ANDROID)
  list(APPEND XAMARIN_MONODROID_SOURCES
    ${SOURCES_DIR}/mono-log-adapter.cc
    ${LZ4_SOURCES}
    )

  if(NOT DEBUG_BUILD AND ENABLE_NET)
    list(APPEND XAMARIN_MONODROID_SOURCES
      ${SOURCES_DIR}/xamarin-android-app-context.cc
      )
  endif()

  if(NOT USES_LIBSTDCPP)
    list(APPEND XAMARIN_MONODROID_SOURCES
      ${BIONIC_SOURCES_DIR}/cxa_guard.cc
      ${SOURCES_DIR}/cxx-abi/string.cc
      ${SOURCES_DIR}/cxx-abi/terminate.cc
      ${SOURCES_DIR}/cxx-abi/vector.cc
      )
  endif()
else()
  list(APPEND XAMARIN_MONODROID_SOURCES
    ${SOURCES_DIR}/designer-assemblies.cc
    ${SOURCES_DIR}/monodroid-glue-designer.cc
    ${JAVA_INTEROP_SRC_PATH}/java-interop-gc-bridge-mono.cc
    ${JAVA_INTEROP_SRC_PATH}/java-interop-jvm.cc
    )
endif()

if(UNIX)
  list(APPEND XAMARIN_MONODROID_SOURCES
    ${SOURCES_DIR}/monodroid-networkinfo.cc
    ${SOURCES_DIR}/xamarin_getifaddrs.cc
    )
endif()

if(ANDROID AND ENABLE_NET)
  list(APPEND XAMARIN_MONODROID_SOURCES
    ${SOURCES_DIR}/monovm-properties.cc
    ${SOURCES_DIR}/pinvoke-override-api.cc
    ${SOURCES_DIR}/java_interop_api.c
    ${JAVA_INTEROP_SRC_PATH}/java-interop-util.cc
    )
else()
  list(APPEND XAMARIN_MONODROID_SOURCES
    ${SOURCES_DIR}/xa-internal-api.cc
    )

  set(XAMARIN_INTERNAL_API_SOURCES
    ${SOURCES_DIR}/java_interop_api.c
    ${SOURCES_DIR}/internal-pinvoke-api.cc
    ${JAVA_INTEROP_SRC_PATH}/java-interop-util.cc
    )
endif()

set(XAMARIN_APP_STUB_SOURCES
  ${SOURCES_DIR}/application_dso_stub.cc
  )

set(XAMARIN_DEBUG_APP_HELPER_SOURCES
  ${SOURCES_DIR}/basic-android-system.cc
  ${SOURCES_DIR}/basic-utilities.cc
  ${SOURCES_DIR}/cpu-arch-detect.cc
  ${SOURCES_DIR}/debug-app-helper.cc
  ${SOURCES_DIR}/new_delete.cc
  ${SOURCES_DIR}/shared-constants.cc
)

if(ANDROID)
  list(APPEND XAMARIN_DEBUG_APP_HELPER_SOURCES
    ${SOURCES_DIR}/cxx-abi/string.cc
    ${SOURCES_DIR}/cxx-abi/vector.cc
  )
endif()

# Build
configure_file(jni/host-config.h.in ${CMAKE_CURRENT_BINARY_DIR}/include/host-config.h)

if(MINGW OR WIN32)
  list(APPEND MONOSGEN_LIB_LINK -lmonosgen-2.0.dll)
else()
  list(APPEND MONOSGEN_LIB_LINK -lmonosgen-2.0)
endif()

if(NOT (ANDROID AND ENABLE_NET))
  add_library(
    ${XAMARIN_INTERNAL_API_LIB}
    SHARED
    ${XAMARIN_INTERNAL_API_SOURCES}
    )

  target_compile_options(
    ${XAMARIN_INTERNAL_API_LIB}
    PRIVATE -fvisibility=default
    )

  target_link_options(
    ${XAMARIN_INTERNAL_API_LIB}
    PRIVATE -fvisibility=default
    )
endif()

add_library(
  ${XAMARIN_APP_STUB_LIB}
  SHARED
  ${XAMARIN_APP_STUB_SOURCES}
  )

target_link_options(
  ${XAMARIN_APP_STUB_LIB}
  PRIVATE ${XA_DEFAULT_SYMBOL_VISIBILITY}
  )

if(ANDROID)
  # Only Android builds need to go in separate directories, desktop builds have the same ABI
  set_target_properties(
    ${XAMARIN_APP_STUB_LIB}
    PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY "${XA_LIBRARY_OUTPUT_DIRECTORY}/${CMAKE_BUILD_TYPE}"
    )
elseif(APPLE)
  set_target_properties(
    ${XAMARIN_APP_STUB_LIB}
    PROPERTIES
    OSX_ARCHITECTURES "${XA_OSX_ARCHITECTURES}"
    )
endif()

if(NOT WIN32 AND NOT MINGW AND DEBUG_BUILD)
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

  if(ANDROID)
    target_link_libraries(
      ${XAMARIN_DEBUG_APP_HELPER_LIB}
      -llog
      )
  endif()

  target_compile_options(
    ${XAMARIN_DEBUG_APP_HELPER_LIB}
    PRIVATE ${XA_SYMBOL_VISIBILITY}
    )

  target_compile_definitions(
    ${XAMARIN_DEBUG_APP_HELPER_LIB}
    PUBLIC -DDEBUG_APP_HELPER
    )

  if(APPLE)
    set_target_properties(
      ${XAMARIN_DEBUG_APP_HELPER_LIB}
      PROPERTIES
      OSX_ARCHITECTURES "${XA_OSX_ARCHITECTURES}"
      )
  endif()
endif()

add_library(
  ${XAMARIN_MONO_ANDROID_LIB}
  SHARED ${XAMARIN_MONODROID_SOURCES}
  )

if(ANDROID)
  # Ugly, but this is the only way to change LZ4 symbols visibility without modifying lz4.h
  set(LZ4_VISIBILITY_OPTS "-DLZ4LIB_VISIBILITY=__attribute__ ((visibility (\"hidden\")))")
endif()

target_compile_options(
  ${XAMARIN_MONO_ANDROID_LIB}
  PRIVATE ${XA_DEFAULT_SYMBOL_VISIBILITY} "${LZ4_VISIBILITY_OPTS}"
  )

if(APPLE)
  set_target_properties(
    ${XAMARIN_MONO_ANDROID_LIB}
    PROPERTIES
    OSX_ARCHITECTURES "${XA_OSX_ARCHITECTURES}"
    )

  add_custom_command(
    TARGET ${XAMARIN_MONO_ANDROID_LIB}
    POST_BUILD
    COMMAND xcrun install_name_tool -change "@rpath/libxamarin-app.dylib" "@loader_path/libxamarin-app.dylib" $<TARGET_FILE:${XAMARIN_MONO_ANDROID_LIB}>
    )
endif()

list(APPEND LINK_LIBS ${MONOSGEN_LIB_LINK})

if(NOT MINGW AND NOT WIN32)
  set(DEBUG_HELPER_LINK_LIBS "-ldl")
endif()

if(ANDROID)
  list(APPEND LINK_LIBS -llog)
elseif(NOT ANDROID AND NOT MINGW AND NOT WIN32)
  list(APPEND LINK_LIBS -pthread -ldl)
endif()

target_link_options(
  ${XAMARIN_MONO_ANDROID_LIB}
  PRIVATE ${XA_DEFAULT_SYMBOL_VISIBILITY}
  )

target_link_libraries(
  ${XAMARIN_MONO_ANDROID_LIB}
  ${LINK_LIBS} xamarin-app
  )
