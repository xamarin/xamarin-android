set(LIB_NAME xa-java-interop)
set(LIB_ALIAS xa::java-interop)

set(JAVA_INTEROP_SOURCES
  ${JAVA_INTEROP_SRC_PATH}/java-interop-dlfcn.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop-mono.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop-util.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop-util.cc
  ${JAVA_INTEROP_SRC_PATH}/java-interop.cc
)
add_clang_check_sources("${JAVA_INTEROP_SOURCES}")

add_library(
  ${LIB_NAME}
  STATIC
  ${JAVA_INTEROP_SOURCES}
)

add_library(${LIB_ALIAS} ALIAS ${LIB_NAME})

target_include_directories(
  ${LIB_NAME}
  PUBLIC
  "$<BUILD_INTERFACE:${JAVA_INTEROP_INCLUDE_DIR}>"
)

target_include_directories(
  ${LIB_NAME}
  SYSTEM PRIVATE
  ${SYSROOT_CXX_INCLUDE_DIR}
  ${MONO_RUNTIME_INCLUDE_DIR}
)

target_compile_options(
  ${LIB_NAME}
  PRIVATE
  ${XA_COMMON_CXX_ARGS}
)

if(DEBUG_BUILD)
  set_target_properties(
    ${LIB_NAME}
    PROPERTIES
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}"
  )
endif()

xa_add_compile_definitions(${LIB_NAME})
