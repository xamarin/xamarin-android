set(LIB_NAME xa-lz4)
set(LIB_ALIAS xa::lz4)

set(LZ4_SRC_DIR "${EXTERNAL_DIR}/lz4/lib")
set(LZ4_INCLUDE_DIR ${LZ4_SRC_DIR})

set(LZ4_SOURCES
  ${LZ4_SRC_DIR}/lz4.c
)

add_library(
  ${LIB_NAME}
  STATIC
  ${LZ4_SOURCES}
)

add_library(${LIB_ALIAS} ALIAS ${LIB_NAME})

target_compile_definitions(
  ${LIB_NAME}
  PRIVATE
  # Ugly, but this is the only way to change LZ4 symbols visibility without modifying lz4.h
  "LZ4LIB_VISIBILITY=__attribute__ ((visibility (\"hidden\")))"
  XXH_NAMESPACE=LZ4_
)

target_include_directories(
  ${LIB_NAME}
  PUBLIC
  "$<BUILD_INTERFACE:${LZ4_INCLUDE_DIR}>"
)

if(DEBUG_BUILD)
  set_target_properties(
    ${LIB_NAME}
    PROPERTIES
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}"
  )
endif()

xa_add_compile_definitions(${LIB_NAME})
