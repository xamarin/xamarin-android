set(LIB_NAME xa-perfetto)
set(LIB_ALIAS xa::perfetto)

set(PERFETTO_DIR "${REPO_ROOT_DIR}/src-ThirdParty/perfetto")
set(PERFETTO_SDK_DIR "${PERFETTO_DIR}")
set(PERFETTO_INCLUDE_DIR "${PERFETTO_DIR}")

set(PERFETTO_SOURCES
  ${PERFETTO_SDK_DIR}/perfetto.cc
)

add_library(
  ${LIB_NAME}
  STATIC
  ${PERFETTO_SOURCES}
)

add_library(${LIB_ALIAS} ALIAS ${LIB_NAME})

find_package(Threads)

target_include_directories(
  ${LIB_NAME}
  PUBLIC
  "$<BUILD_INTERFACE:${PERFETTO_INCLUDE_DIR}>"
  "$<BUILD_INTERFACE:${SYSROOT_CXX_INCLUDE_DIR}>"
)

target_link_libraries(
  ${LIB_NAME}
  PUBLIC
  "$<BUILD_INTERFACE:${CMAKE_THREAD_LIBS_INIT}>"
)

set_target_properties(
  ${LIB_NAME}
  PROPERTIES
  ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}"
)

xa_add_compile_definitions(${LIB_NAME})
