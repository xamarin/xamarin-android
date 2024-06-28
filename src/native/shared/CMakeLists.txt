set(LIB_NAME xa-shared-bits)
set(LIB_ALIAS xa::shared)

set(LIB_NAME_NO_ABI ${LIB_NAME}-no-abi)
set(LIB_ALIAS_NO_ABI ${LIB_ALIAS}-no-abi)

set(XA_SHARED_CXX_ABI_SOURCES
  cxx-abi/string.cc
  cxx-abi/terminate.cc
)

set(XA_SHARED_SOURCES
  helpers.cc
  log_functions.cc
  new_delete.cc
)

if(ENABLE_PERFETTO)
  list(APPEND XA_SHARED_SOURCES
    perfetto_support.cc
  )
endif()

add_clang_check_sources("${XA_SHARED_SOURCES};${XA_SHARED_CXX_ABI_SOURCES}")

set(XXHASH_DIR "${EXTERNAL_DIR}/xxHash")
set(CONSTEXPR_XXH3_DIR "${EXTERNAL_DIR}/constexpr-xxh3")

add_library(
  ${LIB_NAME}
  STATIC
  ${XA_SHARED_SOURCES}
  ${XA_SHARED_CXX_ABI_SOURCES}
)
add_library(${LIB_ALIAS} ALIAS ${LIB_NAME})

add_library(
  ${LIB_NAME_NO_ABI}
  STATIC
  ${XA_SHARED_SOURCES}
)
add_library(${LIB_ALIAS_NO_ABI} ALIAS ${LIB_NAME_NO_ABI})

macro(lib_target_options TARGET_NAME)
  target_include_directories(
    ${TARGET_NAME}
    PUBLIC
    "$<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}>"
    "$<BUILD_INTERFACE:${JAVA_INTEROP_INCLUDE_DIR}>"
    "$<BUILD_INTERFACE:${EXTERNAL_DIR}>"
    "$<BUILD_INTERFACE:${CONSTEXPR_XXH3_DIR}>"
  )

  if(ENABLE_PERFETTO)
    target_link_libraries(
      ${TARGET_NAME}
      PUBLIC
      xa::perfetto
    )
  endif()

  target_link_libraries(
    ${TARGET_NAME}
    PUBLIC
    xa::java-interop
    -llog
  )

  target_include_directories(
    ${TARGET_NAME}
    SYSTEM PRIVATE
    ${SYSROOT_CXX_INCLUDE_DIR}
    ${MONO_RUNTIME_INCLUDE_DIR}
  )

  target_compile_options(
    ${TARGET_NAME}
    PRIVATE
    ${XA_COMMON_CXX_ARGS}
  )

  set_target_properties(
    ${TARGET_NAME}
    PROPERTIES
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}"
  )

  xa_add_compile_definitions(${TARGET_NAME})
endmacro()

lib_target_options(${LIB_NAME})
lib_target_options(${LIB_NAME_NO_ABI})
