include(CheckCXXCompilerFlag)
include(CheckCCompilerFlag)
include(CheckLinkerFlag)

macro(_compiler_has_flag _lang _flag)
  string(REGEX REPLACE "-|,|=" "_" _flag_name ${_flag})
  string(TOUPPER "${_lang}" _lang_upper)

  cmake_language(CALL check_${_lang}_compiler_flag "${_flag}" HAS_${_flag_name}_${_lang_upper})
  if(HAS_${_flag_name}_${_lang_upper})
    set(COMPILER_FLAG_FOUND True)
  else()
    set(COMPILER_FLAG_FOUND False)
  endif()
endmacro()

macro(cxx_compiler_has_flag _flag)
  _compiler_has_flag(cxx ${_flag})
endmacro()

macro(c_compiler_has_flag _flag)
  _compiler_has_flag(c ${_flag})
endmacro()

macro(_linker_has_flag _lang _flag)
  string(REGEX REPLACE "-|,|=" "_" _flag_name ${_flag})
  string(TOUPPER "${_lang}" _lang_upper)

  check_linker_flag(${_lang} "${_flag}" HAS_${_flag_name}_LINKER_${_lang_upper})
  if(HAS_${_flag_name}_LINKER_${_lang_upper})
    set(LINKER_FLAG_FOUND True)
  else()
    set(LINKER_FLAG_FOUND False)
  endif()
endmacro()

macro(cxx_linker_has_flag _flag)
  _linker_has_flag(CXX ${_flag})
endmacro()

macro(c_linker_has_flag _flag)
  _linker_has_flag(C ${_flag})
endmacro()

#
# Uses ${XA_COMMON_COMPILER_FLAGS}, if defined
#
# ${EXTRA_C_FLAGS} contains a set of additional C compiler flags to check
#
# Sets VARNAME on exit
#
macro(xa_check_c_flags VARNAME EXTRA_C_FLAGS)
  set(_CHECKED_FLAGS "")
  set(_CHECK_FLAGS ${XA_COMMON_COMPILER_FLAGS} ${EXTRA_C_FLAGS})

  list(LENGTH _CHECK_FLAGS ARGS_LEN)
  if(ARGS_LEN LESS_EQUAL 0)
    return()
  endif()

  foreach(flag ${_CHECK_FLAGS})
    c_compiler_has_flag(${flag})
    if(COMPILER_FLAG_FOUND)
      list(APPEND _CHECKED_FLAGS "${flag}")
    endif()
  endforeach()

  set(${VARNAME} "${_CHECKED_FLAGS}")
endmacro()

#
# Uses ${XA_COMMON_COMPILER_FLAGS}, if defined
#
# ${EXTRA_CXX_FLAGS} contains a set of additional C compiler flags to check
#
# Sets VARNAME on exit
#
macro(xa_check_cxx_flags VARNAME EXTRA_CXX_FLAGS)
  set(_CHECKED_FLAGS "")
  set(_CHECK_FLAGS ${XA_COMMON_COMPILER_FLAGS} ${EXTRA_CXX_FLAGS})

  list(LENGTH _CHECK_FLAGS ARGS_LEN)
  if(ARGS_LEN LESS_EQUAL 0)
    return()
  endif()

  foreach(flag ${_CHECK_FLAGS})
    cxx_compiler_has_flag(${flag})
    if(COMPILER_FLAG_FOUND)
      list(APPEND _CHECKED_FLAGS "${flag}")
    endif()
  endforeach()

  set(${VARNAME} "${_CHECKED_FLAGS}")
endmacro()

#
# Uses ${XA_COMMON_LINKER_FLAGS}, if defined
#
# ${EXTRA_C_LINKER_FLAGS} contains a set of additional C linker flags to check
#
# Sets VARNAME on exit
#
macro(xa_check_c_linker_flags VARNAME EXTRA_C_LINKER_FLAGS)
  set(_CHECKED_FLAGS "")
  set(_CHECK_FLAGS ${XA_COMMON_LINKER_FLAGS} ${EXTRA_C_LINKER_FLAGS})

  list(LENGTH _CHECK_FLAGS ARGS_LEN)
  if(ARGS_LEN LESS_EQUAL 0)
    return()
  endif()

  foreach(flag ${_CHECK_FLAGS})
    c_linker_has_flag(${flag})
    if(LINKER_FLAG_FOUND)
    list(APPEND _CHECKED_FLAGS "${flag}")
    endif()
  endforeach()

  set(${VARNAME} "${_CHECKED_FLAGS}")
endmacro()

#
# Uses ${XA_COMMON_LINKER_FLAGS}, if defined
#
# ${EXTRA_CXX_LINKER_FLAGS} contains a set of additional C++ linker flags to check
#
# Sets VARNAME on exit
#
macro(xa_check_cxx_linker_flags VARNAME EXTRA_CXX_LINKER_FLAGS)
  set(_CHECKED_FLAGS "")
  set(_CHECK_FLAGS ${XA_COMMON_LINKER_FLAGS} ${EXTRA_CXX_LINKER_FLAGS})

  list(LENGTH _CHECK_FLAGS ARGS_LEN)
  if(ARGS_LEN LESS_EQUAL 0)
    return()
  endif()

  foreach(flag ${_CHECK_FLAGS})
    cxx_linker_has_flag(${flag})
    if(LINKER_FLAG_FOUND)
      list(APPEND _CHECKED_FLAGS "${flag}")
    endif()
  endforeach()

  set(${VARNAME} "${_CHECKED_FLAGS}")
endmacro()

#
# Uses ${XA_COMMON_COMPILER_FLAGS}, if defined
#
# ${EXTRA_C_FLAGS} contains a set of additional C compiler flags to check
# ${EXTRA_CXX_FLAGS} contains a set of additional C compiler flags to check
#
# Sets VARNAME_CXX on exit
# Sets VARNAME_C on exit
#
macro(xa_check_compiler_flags VARNAME_CXX VARNAME_C EXTRA_CXX_FLAGS EXTRA_C_FLAGS)
  xa_check_cxx_flags(${VARNAME_CXX} "${EXTRA_CXX_FLAGS}")
  xa_check_c_flags(${VARNAME_C} "${EXTRA_C_FLAGS}")
endmacro()

#
# Uses ${XA_COMMON_LINKER_FLAGS}, if defined
#
# ${EXTRA_C_LINKER_FLAGS} contains a set of additional C linker flags to check
# ${EXTRA_CXX_LINKER_FLAGS} contains a set of additional C++ linker flags to check
#
# Sets VARNAME_C on exit
# Sets VARNAME_CXX on exit
#
macro(xa_check_linker_flags VARNAME_CXX VARNAME_C EXTRA_CXX_LINKER_FLAGS EXTRA_C_LINKER_FLAGS)
  xa_check_cxx_linker_flags(${VARNAME_CXX} "${EXTRA_CXX_LINKER_FLAGS}")
  xa_check_c_linker_flags(${VARNAME_C} "${EXTRA_C_LINKER_FLAGS}")
endmacro()

#
# Sets ${XA_COMMON_COMPILER_FLAGS} to flags that can be shared by C and C++ compilers
# Sets ${XA_COMMON_LINKER_FLAGS} to flags that can be shared by C and C++ linkers
# Sets ${XA_DEFAULT_SYMBOL_VISIBILITY} to the default symbol visibility value
#
# Defines RELEASE and NDEBUG macros for C/C++ builds if the current build type is Release
#
function(xa_common_prepare)
  if(NOT DSO_SYMBOL_VISIBILITY)
    set(DSO_SYMBOL_VISIBILITY "hidden")
  endif()

  #
  # Currently not supported by NDK clang, but worth considering when it is eventually supported:
  #
  #  -fsanitize=safe-stack
  #

  set(XA_DEFAULT_SYMBOL_VISIBILITY
    -fvisibility=${DSO_SYMBOL_VISIBILITY}
    PARENT_SCOPE)

  set(XA_COMMON_COMPILER_FLAGS
    -fstack-protector-strong
    -fstrict-return
    -fno-strict-aliasing
    -fno-function-sections
    -fno-data-sections
    -funswitch-loops
    -Wa,-noexecstack
    -fPIC
    -g
    -O2
    PARENT_SCOPE
    )

  set(XA_COMMON_LINKER_FLAGS
    -fstack-protector-strong
    LINKER:-fstrict-return
    LINKER:-z,now
    LINKER:-z,relro
    LINKER:-z,noexecstack
    LINKER:--no-undefined
    PARENT_SCOPE
    )

  if(MINGW)
    list(APPEND XA_COMMON_LINKER_FLAGS
      LINKER:--export-all-symbols
      )
  else()
    list(APPEND XA_COMMON_LINKER_FLAGS
      LINKER:--export-dynamic
      )
  endif()

  if(CMAKE_BUILD_TYPE STREQUAL Release)
    add_compile_definitions(RELEASE NDEBUG)
  endif()
endfunction()

macro(xa_macos_prepare_arm64)
  if(APPLE)
    set(SDK_SUPPORTS_ARM64 False)
    set(SDK_SUPPORTS_X86_64 False)
    execute_process(
      COMMAND xcode-select -p
      RESULT_VARIABLE XCODE_SELECT_RESULT
      OUTPUT_VARIABLE XCODE_DEVELOPER_PATH
      )
    if(NOT ${XCODE_SELECT_RESULT} EQUAL "0")
      message(WARNING "xcode-select failed with result ${XCODE_SELECT_RESULT}")
    else()
      string(STRIP "${XCODE_DEVELOPER_PATH}" XCODE_DEVELOPER_PATH)
      set(SDKSETTINGS_PATH "${XCODE_DEVELOPER_PATH}/Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk/SDKSettings.plist")

      # CAUTION: do NOT ever remove the '-o -' parameter, without '-o' plutil will overwrite the .plist file
      execute_process(
        COMMAND plutil -extract SupportedTargets.macosx.Archs json -o - "${SDKSETTINGS_PATH}"
        RESULT_VARIABLE PLUTIL_RESULT
        OUTPUT_VARIABLE SDK_ARCHITECTURES
      )
      if(NOT ${PLUTIL_RESULT} EQUAL 0)
        message(WARNING "plutil failed to read ${SDKSETTINGS_PATH}, returned with result ${PLUTIL_RESULT}")
      else()
        string(FIND "${SDK_ARCHITECTURES}" "\"arm64\"" ARCH_POS)
        if(${ARCH_POS} GREATER_EQUAL 0)
          set(SDK_SUPPORTS_ARM64 True)
        endif()

        string(FIND "${SDK_ARCHITECTURES}" "\"x86_64\"" ARCH_POS)
        if(${ARCH_POS} GREATER_EQUAL 0)
          set(SDK_SUPPORTS_X86_64 True)
        endif()
      endif()
    endif()

    unset(XA_OSX_ARCHITECTURES)
    if(SDK_SUPPORTS_ARM64)
      message(STATUS "SDK at ${XCODE_DEVELOPER_PATH} supports creation of ARM64 binaries")
      set(MONOSGEN_DYLIB "${XA_LIB_TOP_DIR}/lib/host-Darwin/libmonosgen-2.0.dylib")
      execute_process(
        COMMAND lipo -archs ${MONOSGEN_DYLIB}
        RESULT_VARIABLE LIPO_RESULT
        OUTPUT_VARIABLE LIPO_OUTPUT
      )
      set(ADD_ARM64 False)
      if(${LIPO_RESULT} EQUAL "0")
        string(FIND "${LIPO_OUTPUT}" "\"arm64\"" ARCH_POS)
        if(${ARCH_POS} GREATER_EQUAL 0)
          set(ADD_ARM64 True)
        else()
          message(WARNING "lipo reported ${MONOSGEN_DYLIB} does not contain the ARM64 image")
        endif()
      else()
        message(WARNING "lipo check on ${MONOSGEN_DYLIB} failed with exit code ${LIPO_RESULT}")
      endif()

      if(ADD_ARM64)
        list(APPEND XA_OSX_ARCHITECTURES "arm64")
      else()
        message(WARNING "Disabling ARM64 build")
      endif()
    endif()
    if(SDK_SUPPORTS_X86_64)
      message(STATUS "SDK at ${XCODE_DEVELOPER_PATH} supports creation of X86_64 binaries")
      list(APPEND XA_OSX_ARCHITECTURES "x86_64")
    endif()
  endif()
endmacro()
