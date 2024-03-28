set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

include("${CMAKE_ANDROID_NDK}/build/cmake/abis.cmake")

if(CMAKE_BUILD_TYPE STREQUAL Debug)
  set(DEBUG_BUILD True)
else()
  set(DEBUG_BUILD False)
endif()

set(XA_NO_INLINE "$ENV{XA_NO_INLINE}")
if(XA_NO_INLINE)
  set(DONT_INLINE_DEFAULT ON)
else()
  set(DONT_INLINE_DEFAULT OFF)
endif()

set(XA_NO_STRIP "$ENV{XA_NO_STRIP}")
if(XA_NO_STRIP OR DEBUG_BUILD)
  set(STRIP_DEBUG_DEFAULT OFF)
endif()

option(ENABLE_CLANG_ASAN "Enable the clang AddressSanitizer support" OFF)
option(ENABLE_CLANG_UBSAN "Enable the clang UndefinedBehaviorSanitizer support" OFF)

if(ENABLE_CLANG_ASAN OR ENABLE_CLANG_UBSAN)
  set(STRIP_DEBUG_DEFAULT OFF)
  set(ANALYZERS_ENABLED ON)
else()
  if(NOT XA_NO_STRIP)
    set(STRIP_DEBUG_DEFAULT ON)
  endif()
  set(ANALYZERS_ENABLED OFF)
endif()

option(COMPILER_DIAG_COLOR "Show compiler diagnostics/errors in color" ON)
option(STRIP_DEBUG "Strip debugging information when linking" ${STRIP_DEBUG_DEFAULT})
option(DISABLE_DEBUG "Disable the built-in debugging code" OFF)
option(USE_CCACHE "Use ccache, if found, to speed up recompilation" ON)
option(DONT_INLINE "Do not inline any functions which are usually inlined, to get better stack traces" ${DONT_INLINE_DEFAULT})

if(USE_CCACHE)
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

if(ANDROID_STL STREQUAL none)
  set(USES_LIBSTDCPP False)
else()
  set(USES_LIBSTDCPP True)
endif()

#
# General config
#
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

set(XA_BUILD_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../bin/Build${XA_BUILD_CONFIGURATION}")
include("${XA_BUILD_DIR}/xa_build_configuration.cmake")

#
# Paths
#
if(ANDROID_ABI MATCHES "^arm64-v8a")
  set(NET_RUNTIME_DIR "${NETCORE_APP_RUNTIME_DIR_ARM64}")
  set(TOOLCHAIN_TRIPLE "${NDK_ABI_arm64-v8a_TRIPLE}")
elseif(ANDROID_ABI MATCHES "^armeabi-v7a")
  set(NET_RUNTIME_DIR "${NETCORE_APP_RUNTIME_DIR_ARM}")
  set(TOOLCHAIN_TRIPLE "${NDK_ABI_armeabi-v7a_TRIPLE}")
elseif(ANDROID_ABI MATCHES "^x86_64")
  set(NET_RUNTIME_DIR "${NETCORE_APP_RUNTIME_DIR_X86_64}")
  set(TOOLCHAIN_TRIPLE "${NDK_ABI_x86_64_TRIPLE}")
elseif(ANDROID_ABI MATCHES "^x86")
  set(NET_RUNTIME_DIR "${NETCORE_APP_RUNTIME_DIR_X86}")
  set(TOOLCHAIN_TRIPLE "${NDK_ABI_x86_TRIPLE}")
else()
  message(FATAL "${ANDROID_ABI} is not supported for .NET 6+ builds")
endif()

file(REAL_PATH "../../" REPO_ROOT_DIR)
set(EXTERNAL_DIR "${REPO_ROOT_DIR}/external")
set(JAVA_INTEROP_SRC_PATH "${EXTERNAL_DIR}/Java.Interop/src/java-interop")
set(SHARED_SOURCES_DIR "${REPO_ROOT_DIR}/src/native/shared")
set(TRACING_SOURCES_DIR "${REPO_ROOT_DIR}/src/native/tracing")
#
# Include directories
#
include_directories(SYSTEM ${CMAKE_SYSROOT}/usr/include/c++/v1/)
include_directories(SYSTEM "${NET_RUNTIME_DIR}/native/include/mono-2.0")
include_directories("${JAVA_INTEROP_SRC_PATH}")
include_directories("${SHARED_SOURCES_DIR}")
include_directories("${TRACING_SOURCES_DIR}")

#
# Compiler defines
#
add_compile_definitions(XA_VERSION="${XA_VERSION}")
add_compile_definitions(_REENTRANT)
add_compile_definitions(PLATFORM_ANDROID)

if(DEBUG_BUILD AND NOT DISABLE_DEBUG)
  add_compile_definitions(DEBUG)
endif()

if(ANDROID_ABI MATCHES "^(arm64-v8a|x86_64)")
  add_compile_definitions(ANDROID64)
endif()

if (ANDROID_NDK_MAJOR LESS 20)
  add_compile_definitions(__ANDROID_API_Q__=29)
endif()

#
# Shared sources
#
set(XA_SHARED_SOURCES
  ${SHARED_SOURCES_DIR}/helpers.cc
  ${SHARED_SOURCES_DIR}/new_delete.cc
)
