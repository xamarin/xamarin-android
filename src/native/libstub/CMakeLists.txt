set(XA_LIBRARY_STUBS_OUTPUT_DIRECTORY "${XA_LIB_TOP_DIR}/libstubs/${ANDROID_RID}")

set(XAMARIN_STUB_LIB_SOURCES
  stub.cc
)

macro(xa_add_stub_library _libname)
  if(NOT ANALYZERS_ENABLED)
    add_library(
      ${_libname}
      SHARED ${XAMARIN_STUB_LIB_SOURCES}
    )

    string(TOUPPER ${_libname} _libname_uc)
    target_compile_definitions(
      ${_libname}
      PRIVATE STUB_LIB_NAME=lib${_libname} IN_LIB${_libname_uc}
    )
    xa_add_compile_definitions(${_libname})

    target_compile_options(
      ${_libname}
      PRIVATE ${XA_COMMON_CXX_ARGS} -nostdlib -fno-exceptions -fno-rtti
    )

    target_link_options(
      ${_libname}
      PRIVATE ${XA_COMMON_CXX_LINKER_ARGS} -nostdlib -fno-exceptions -fno-rtti
    )

    set_target_properties(
      ${_libname}
      PROPERTIES
      LIBRARY_OUTPUT_DIRECTORY "${XA_LIBRARY_STUBS_OUTPUT_DIRECTORY}"
    )
  endif()
endmacro()

xa_add_stub_library(c)
xa_add_stub_library(m)
xa_add_stub_library(z)

# These two are used by the marshal methods tracing library when linking libxamarin-app.so
xa_add_stub_library(log)
xa_add_stub_library(dl)
