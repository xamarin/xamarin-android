cmake_minimum_required(VERSION 3.18.1)

#
# MUST be included before project()!
#
include("../../build-tools/cmake/xa_common.cmake")

if(NOT DEFINED LIBUNWIND_LIBRARY_NAME)
  message(FATAL_ERROR "Please set the LIBUNWIND_LIBRARY_NAME variable on command line (-DLIBUNWIND_LIBRARY_NAME=base_library_name)")
endif()

if(NOT DEFINED LIBUNWIND_SOURCE_DIR)
  message(FATAL_ERROR "Please set the LIBUNWIND_SOURCE_DIR variable on command line (-DLIBUNWIND_SOURCE_DIR=source_dir_path)")
endif()

if(NOT DEFINED LIBUNWIND_OUTPUT_DIR)
  message(FATAL_ERROR "Please set the LIBUNWIND_OUTPUT_DIR variable on command line (-DLIBUNWIND_OUTPUT_DIR=output_dir_path)")
endif()

if(NOT DEFINED LIBUNWIND_HEADERS_OUTPUT_DIR)
  message(FATAL_ERROR "Please set the LIBUNWIND_HEADERS_OUTPUT_DIR variable on command line (-DLIBUNWIND_HEADERS_OUTPUT_DIR=output_dir_path)")
endif()

#
# Read libunwind version
#
set(CONFIGURE_AC "${LIBUNWIND_SOURCE_DIR}/configure.ac")
file(STRINGS "${CONFIGURE_AC}" UNWIND_MAJOR_VERSION_AC REGEX "^[ \t]*define[ \t]*\\([ \t]*pkg_major,[ \t]*(.*)\\)")
file(STRINGS "${CONFIGURE_AC}" UNWIND_MINOR_VERSION_AC REGEX "^[ \t]*define[ \t]*\\([ \t]*pkg_minor,[ \t]*(.*)\\)")
file(STRINGS "${CONFIGURE_AC}" UNWIND_EXTRA_VERSION_AC REGEX "^[ \t]*define[ \t]*\\([ \t]*pkg_extra,[ \t]*(.*)\\)")
string(REGEX REPLACE "^[ \t]*define[ \t]*\\([ \t]*pkg_major,[ \t]*(.*)\\)" "\\1" UNWIND_MAJOR_VERSION "${UNWIND_MAJOR_VERSION_AC}")
string(REGEX REPLACE "^[ \t]*define[ \t]*\\([ \t]*pkg_minor,[ \t]*(.*)\\)" "\\1" UNWIND_MINOR_VERSION "${UNWIND_MINOR_VERSION_AC}")
string(REGEX REPLACE "^[ \t]*define[ \t]*\\([ \t]*pkg_extra,[ \t]*(.*)\\)" "\\1" UNWIND_EXTRA_VERSION "${UNWIND_EXTRA_VERSION_AC}")

set(PKG_MAJOR "${UNWIND_MAJOR_VERSION}")
set(PKG_MINOR "${UNWIND_MINOR_VERSION}")
set(PKG_EXTRA "${UNWIND_EXTRA_VERSION}")
set(PACKAGE_STRING "libunwind-xamarin")
set(PACKAGE_BUGREPORT "")

message(STATUS "Major: ${PKG_MAJOR}; Minor: ${PKG_MINOR}; Extra: ${PKG_EXTRA}")

project(
  libunwind-xamarin
#  VERSION "${PKG_MAJOR}.${PKG_MINOR}.${PKG_EXTRA}"
  LANGUAGES C ASM
)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

include(CheckCSourceCompiles)
include(CheckIncludeFiles)
include(CheckSymbolExists)
include("../../build-tools/cmake/xa_macros.cmake")

set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${LIBUNWIND_OUTPUT_DIR})
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${LIBUNWIND_OUTPUT_DIR})
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${LIBUNWIND_OUTPUT_DIR})

if(CMAKE_ANDROID_ARCH_ABI STREQUAL arm64-v8a)
  set(TARGET_AARCH64 TRUE)
  set(arch aarch64)
elseif(CMAKE_ANDROID_ARCH_ABI STREQUAL armeabi-v7a)
  set(TARGET_ARM TRUE)
  set(arch arm)
elseif(CMAKE_ANDROID_ARCH_ABI STREQUAL x86_64)
  set(TARGET_AMD64 TRUE)
  set(arch x86_64)
elseif(CMAKE_ANDROID_ARCH_ABI STREQUAL x86)
  set(TARGET_X86 TRUE)
  set(arch x86)
else()
  message(FATAL_ERROR "Unsupported Android ABI ${CMAKE_ANDROID_ARCH_ABI}")
endif()

set(DSO_SYMBOL_VISIBILITY "hidden")
xa_common_prepare()

if(CMAKE_BUILD_TYPE STREQUAL Debug)
  list(APPEND LOCAL_COMPILER_ARGS -g -fno-omit-frame-pointer)
else()
  list(APPEND LOCAL_COMPILER_ARGS -s -fomit-frame-pointer)
  add_compile_definitions(NDEBUG)
endif()
list(APPEND LOCAL_COMPILER_ARGS
  ${XA_DEFAULT_SYMBOL_VISIBILITY}
  -fno-asynchronous-unwind-tables
  -fno-unwind-tables
)

xa_check_c_flags(XA_C_FLAGS "${LOCAL_COMPILER_ARGS}")
xa_check_c_linker_flags(XA_C_LINKER_FLAGS "${LOCAL_COMPILER_ARGS}")

add_compile_options(${XA_C_FLAGS})
add_link_options(${XA_C_LINKER_FLAGS})

add_compile_definitions(HAVE_CONFIG_H)
add_compile_definitions(_GNU_SOURCE)
add_compile_definitions(UNW_LOCAL_ONLY) # we don't need remote unwinding

# Detect include files
set(CMAKE_REQUIRED_DEFINITIONS -D_GNU_SOURCE)

set(HAVE_ASM_VSYSCALL_H True)
#check_include_files(asm/vsyscall.h HAVE_ASM_VSYSCALL_H)
check_include_files(byteswap.h HAVE_BYTESWAP_H)
check_include_files(elf.h HAVE_ELF_H)
check_include_files(endian.h HAVE_ENDIAN_H)
check_include_files(link.h HAVE_LINK_H)
check_include_files(sys/endian.h HAVE_SYS_ENDIAN_H)
check_include_files(sys/link.h HAVE_SYS_LINK_H)
check_include_files(sys/param.h HAVE_SYS_PARAM_H)
check_include_files(sys/syscall.h HAVE_SYS_SYSCALL_H)

# Detect functions
check_symbol_exists(mincore "sys/mman.h" HAVE_MINCORE)
check_symbol_exists(pipe2 "fcntl.h;unistd.h" HAVE_PIPE2)

# TODO: consider enabling zlib

configure_file(config.h.in ${CMAKE_CURRENT_BINARY_DIR}/include/config.h)
configure_file(${LIBUNWIND_SOURCE_DIR}/include/libunwind-common.h.in ${CMAKE_CURRENT_BINARY_DIR}/include/libunwind-common.h)
configure_file(${LIBUNWIND_SOURCE_DIR}/include/libunwind.h.in ${CMAKE_CURRENT_BINARY_DIR}/include/libunwind.h)
configure_file(${LIBUNWIND_SOURCE_DIR}/include/tdep/libunwind_i.h.in ${CMAKE_CURRENT_BINARY_DIR}/include/tdep/libunwind_i.h)

# The files are nearly identical, the only difference is name of the header included when remote-only libunwind is used
# We don't use it, but we still copy files to separate directories in order to avoid locking issues during parallel builds
set(HEADERS_DIR ${LIBUNWIND_HEADERS_OUTPUT_DIR}/${CMAKE_ANDROID_ARCH_ABI})

file(COPY ${CMAKE_CURRENT_BINARY_DIR}/include/libunwind-common.h DESTINATION ${HEADERS_DIR})
file(COPY ${CMAKE_CURRENT_BINARY_DIR}/include/libunwind.h DESTINATION ${HEADERS_DIR})
file(COPY ${CMAKE_CURRENT_BINARY_DIR}/include/tdep/libunwind_i.h DESTINATION ${HEADERS_DIR}/tdep/)

include_directories(${LIBUNWIND_SOURCE_DIR}/include/tdep)
include_directories(${LIBUNWIND_SOURCE_DIR}/include)
include_directories(${LIBUNWIND_SOURCE_DIR}/src)
include_directories(${CMAKE_CURRENT_BINARY_DIR}/include/tdep)
include_directories(${CMAKE_CURRENT_BINARY_DIR}/include)

if(TARGET_ARM)
  # Ensure that the remote and local unwind code can reside in the same binary without name clashing
  add_definitions("-Darm_search_unwind_table=UNW_OBJ(arm_search_unwind_table)")
  # We compile code with -std=c99 and the asm keyword is not recognized as it is a gnu extension
  #TODO: possibly not needed? add_definitions(-Dasm=__asm__)
  # The arm sources include ex_tables.h from include/tdep-arm without going through a redirection
  # in include/tdep like it works for similar files on other architectures. So we need to add
  # the include/tdep-arm to include directories
  include_directories(${LIBUNWIND_SOURCE_DIR}/include/tdep-arm)
elseif(TARGET_AARCH64)
  # We compile code with -std=c99 and the asm keyword is not recognized as it is a gnu extension
  #TODO: possibly not needed? add_definitions(-Dasm=__asm__)
endif()

set(SOURCES_DIR ${LIBUNWIND_SOURCE_DIR}/src)

set(LIBUNWIND_XAMARIN_SOURCES
  ${SOURCES_DIR}/dwarf/Gexpr.c
  ${SOURCES_DIR}/dwarf/Gfde.c
  ${SOURCES_DIR}/dwarf/Gfind_proc_info-lsb.c
  ${SOURCES_DIR}/dwarf/Gfind_unwind_table.c
  ${SOURCES_DIR}/dwarf/Gget_proc_info_in_range.c
  ${SOURCES_DIR}/dwarf/Gparser.c
  ${SOURCES_DIR}/dwarf/Gpe.c
  ${SOURCES_DIR}/dwarf/Lexpr.c
  ${SOURCES_DIR}/dwarf/Lfde.c
  ${SOURCES_DIR}/dwarf/Lfind_proc_info-lsb.c
  ${SOURCES_DIR}/dwarf/Lfind_unwind_table.c
  ${SOURCES_DIR}/dwarf/Lget_proc_info_in_range.c
  ${SOURCES_DIR}/dwarf/Lparser.c
  ${SOURCES_DIR}/dwarf/Lpe.c
  ${SOURCES_DIR}/dwarf/global.c
  ${SOURCES_DIR}/elfxx.c
  ${SOURCES_DIR}/mi/Gaddress_validator.c
  ${SOURCES_DIR}/mi/Gdestroy_addr_space.c
  ${SOURCES_DIR}/mi/Gdyn-extract.c
#  ${SOURCES_DIR}/mi/Gdyn-remote.c
  ${SOURCES_DIR}/mi/Gfind_dynamic_proc_info.c
  ${SOURCES_DIR}/mi/Gget_elf_filename.c
  ${SOURCES_DIR}/mi/Gget_fpreg.c
  ${SOURCES_DIR}/mi/Gget_proc_info_by_ip.c
  ${SOURCES_DIR}/mi/Gget_proc_name.c
  ${SOURCES_DIR}/mi/Gget_reg.c
  ${SOURCES_DIR}/mi/Gput_dynamic_unwind_info.c
  ${SOURCES_DIR}/mi/Gset_cache_size.c
  ${SOURCES_DIR}/mi/Gset_caching_policy.c
  ${SOURCES_DIR}/mi/Gset_fpreg.c
  ${SOURCES_DIR}/mi/Gset_iterate_phdr_function.c
  ${SOURCES_DIR}/mi/Gset_reg.c
  ${SOURCES_DIR}/mi/Ldestroy_addr_space.c
  ${SOURCES_DIR}/mi/Ldyn-extract.c
  ${SOURCES_DIR}/mi/Lfind_dynamic_proc_info.c
  ${SOURCES_DIR}/mi/Lget_accessors.c
  ${SOURCES_DIR}/mi/Lget_elf_filename.c
  ${SOURCES_DIR}/mi/Lget_fpreg.c
  ${SOURCES_DIR}/mi/Lget_proc_info_by_ip.c
  ${SOURCES_DIR}/mi/Lget_proc_name.c
  ${SOURCES_DIR}/mi/Lget_reg.c
  ${SOURCES_DIR}/mi/Lput_dynamic_unwind_info.c
  ${SOURCES_DIR}/mi/Lset_cache_size.c
  ${SOURCES_DIR}/mi/Lset_caching_policy.c
  ${SOURCES_DIR}/mi/Lset_fpreg.c
  ${SOURCES_DIR}/mi/Lset_iterate_phdr_function.c
  ${SOURCES_DIR}/mi/Lset_reg.c
  ${SOURCES_DIR}/mi/backtrace.c
  ${SOURCES_DIR}/mi/dyn-cancel.c
  ${SOURCES_DIR}/mi/dyn-info-list.c
  ${SOURCES_DIR}/mi/dyn-register.c
  ${SOURCES_DIR}/mi/flush_cache.c
  ${SOURCES_DIR}/mi/init.c
  ${SOURCES_DIR}/mi/mempool.c
  ${SOURCES_DIR}/mi/strerror.c
  ${SOURCES_DIR}/os-linux.c
)

if(TARGET_AMD64 OR TARGET_AARCH64)
  list(APPEND LIBUNWIND_XAMARIN_SOURCES
    ${SOURCES_DIR}/elf64.c
  )
endif()

if(TARGET_X86 OR TARGET_ARM)
  list(APPEND LIBUNWIND_XAMARIN_SOURCES
    ${SOURCES_DIR}/elf32.c
  )
endif()

if(TARGET_X86)
  list(APPEND LIBUNWIND_XAMARIN_SOURCES
    ${SOURCES_DIR}/x86/Gapply_reg_state.c
    ${SOURCES_DIR}/x86/Gcreate_addr_space.c
    ${SOURCES_DIR}/x86/Gget_proc_info.c
    ${SOURCES_DIR}/x86/Gget_save_loc.c
    ${SOURCES_DIR}/x86/Gglobal.c
    ${SOURCES_DIR}/x86/Ginit.c
    ${SOURCES_DIR}/x86/Ginit_local.c
    ${SOURCES_DIR}/x86/Ginit_remote.c
    ${SOURCES_DIR}/x86/Gos-linux.c
    ${SOURCES_DIR}/x86/Greg_states_iterate.c
    ${SOURCES_DIR}/x86/Gregs.c
    ${SOURCES_DIR}/x86/Gresume.c
    ${SOURCES_DIR}/x86/Gstep.c
    ${SOURCES_DIR}/x86/Lapply_reg_state.c
    ${SOURCES_DIR}/x86/Lcreate_addr_space.c
    ${SOURCES_DIR}/x86/Lget_proc_info.c
    ${SOURCES_DIR}/x86/Lget_save_loc.c
    ${SOURCES_DIR}/x86/Lglobal.c
    ${SOURCES_DIR}/x86/Linit.c
    ${SOURCES_DIR}/x86/Linit_local.c
    ${SOURCES_DIR}/x86/Linit_remote.c
    ${SOURCES_DIR}/x86/Los-linux.c
    ${SOURCES_DIR}/x86/Lreg_states_iterate.c
    ${SOURCES_DIR}/x86/Lregs.c
    ${SOURCES_DIR}/x86/Lresume.c
    ${SOURCES_DIR}/x86/Lstep.c
    ${SOURCES_DIR}/x86/getcontext-linux.S
    ${SOURCES_DIR}/x86/is_fpreg.c
    ${SOURCES_DIR}/x86/regname.c
  )
endif(TARGET_X86)

if(TARGET_AMD64)
  list(APPEND LIBUNWIND_XAMARIN_SOURCES
    ${SOURCES_DIR}/x86_64/Gapply_reg_state.c
    ${SOURCES_DIR}/x86_64/Gcreate_addr_space.c
    ${SOURCES_DIR}/x86_64/Gget_proc_info.c
    ${SOURCES_DIR}/x86_64/Gget_save_loc.c
    ${SOURCES_DIR}/x86_64/Gglobal.c
    ${SOURCES_DIR}/x86_64/Ginit.c
    ${SOURCES_DIR}/x86_64/Ginit_local.c
    ${SOURCES_DIR}/x86_64/Ginit_remote.c
    ${SOURCES_DIR}/x86_64/Gos-linux.c
    ${SOURCES_DIR}/x86_64/Greg_states_iterate.c
    ${SOURCES_DIR}/x86_64/Gregs.c
    ${SOURCES_DIR}/x86_64/Gresume.c
    ${SOURCES_DIR}/x86_64/Gstash_frame.c
    ${SOURCES_DIR}/x86_64/Gstep.c
    ${SOURCES_DIR}/x86_64/Gtrace.c
    ${SOURCES_DIR}/x86_64/Lapply_reg_state.c
    ${SOURCES_DIR}/x86_64/Lcreate_addr_space.c
    ${SOURCES_DIR}/x86_64/Lget_proc_info.c
    ${SOURCES_DIR}/x86_64/Lget_save_loc.c
    ${SOURCES_DIR}/x86_64/Lglobal.c
    ${SOURCES_DIR}/x86_64/Linit.c
    ${SOURCES_DIR}/x86_64/Linit_local.c
    ${SOURCES_DIR}/x86_64/Linit_remote.c
    ${SOURCES_DIR}/x86_64/Los-linux.c
    ${SOURCES_DIR}/x86_64/Lreg_states_iterate.c
    ${SOURCES_DIR}/x86_64/Lregs.c
    ${SOURCES_DIR}/x86_64/Lresume.c
    ${SOURCES_DIR}/x86_64/Lstash_frame.c
    ${SOURCES_DIR}/x86_64/Lstep.c
    ${SOURCES_DIR}/x86_64/Ltrace.c
    ${SOURCES_DIR}/x86_64/getcontext.S
    ${SOURCES_DIR}/x86_64/is_fpreg.c
    ${SOURCES_DIR}/x86_64/regname.c
    ${SOURCES_DIR}/x86_64/setcontext.S
  )
endif()

if(TARGET_ARM)
  list(APPEND LIBUNWIND_XAMARIN_SOURCES
    ${SOURCES_DIR}/arm/Gapply_reg_state.c
    ${SOURCES_DIR}/arm/Gcreate_addr_space.c
    ${SOURCES_DIR}/arm/Gex_tables.c
    ${SOURCES_DIR}/arm/Gget_proc_info.c
    ${SOURCES_DIR}/arm/Gget_save_loc.c
    ${SOURCES_DIR}/arm/Gglobal.c
    ${SOURCES_DIR}/arm/Ginit.c
    ${SOURCES_DIR}/arm/Ginit_local.c
    ${SOURCES_DIR}/arm/Ginit_remote.c
    ${SOURCES_DIR}/arm/Gos-linux.c
    ${SOURCES_DIR}/arm/Greg_states_iterate.c
    ${SOURCES_DIR}/arm/Gregs.c
    ${SOURCES_DIR}/arm/Gresume.c
    ${SOURCES_DIR}/arm/Gstash_frame.c
    ${SOURCES_DIR}/arm/Gstep.c
    ${SOURCES_DIR}/arm/Gtrace.c
    ${SOURCES_DIR}/arm/Lapply_reg_state.c
    ${SOURCES_DIR}/arm/Lcreate_addr_space.c
    ${SOURCES_DIR}/arm/Lex_tables.c
    ${SOURCES_DIR}/arm/Lget_proc_info.c
    ${SOURCES_DIR}/arm/Lget_save_loc.c
    ${SOURCES_DIR}/arm/Lglobal.c
    ${SOURCES_DIR}/arm/Linit.c
    ${SOURCES_DIR}/arm/Linit_local.c
    ${SOURCES_DIR}/arm/Linit_remote.c
    ${SOURCES_DIR}/arm/Los-linux.c
    ${SOURCES_DIR}/arm/Lreg_states_iterate.c
    ${SOURCES_DIR}/arm/Lregs.c
    ${SOURCES_DIR}/arm/Lresume.c
    ${SOURCES_DIR}/arm/Lstash_frame.c
    ${SOURCES_DIR}/arm/Lstep.c
    ${SOURCES_DIR}/arm/Ltrace.c
    ${SOURCES_DIR}/arm/getcontext.S
    ${SOURCES_DIR}/arm/is_fpreg.c
    ${SOURCES_DIR}/arm/regname.c
  )
endif()

if(TARGET_AARCH64)
  list(APPEND LIBUNWIND_XAMARIN_SOURCES
    ${SOURCES_DIR}/aarch64/Gapply_reg_state.c
    ${SOURCES_DIR}/aarch64/Gcreate_addr_space.c
    ${SOURCES_DIR}/aarch64/Gget_proc_info.c
    ${SOURCES_DIR}/aarch64/Gget_save_loc.c
    ${SOURCES_DIR}/aarch64/Gglobal.c
    ${SOURCES_DIR}/aarch64/Ginit.c
    ${SOURCES_DIR}/aarch64/Ginit_local.c
    ${SOURCES_DIR}/aarch64/Ginit_remote.c
    ${SOURCES_DIR}/aarch64/Gis_signal_frame.c

    # Use local version with partial revert of https://github.com/libunwind/libunwind/pull/503
    # until https://github.com/libunwind/libunwind/issues/702 is fixed
    #    ${SOURCES_DIR}/aarch64/Gos-linux.c
    fixes/aarch64/Gos-linux.c

    ${SOURCES_DIR}/aarch64/Greg_states_iterate.c
    ${SOURCES_DIR}/aarch64/Gregs.c
    ${SOURCES_DIR}/aarch64/Gresume.c
    ${SOURCES_DIR}/aarch64/Gstash_frame.c
    ${SOURCES_DIR}/aarch64/Gstep.c
    ${SOURCES_DIR}/aarch64/Gtrace.c
    ${SOURCES_DIR}/aarch64/Lapply_reg_state.c
    ${SOURCES_DIR}/aarch64/Lcreate_addr_space.c
    ${SOURCES_DIR}/aarch64/Lget_proc_info.c
    ${SOURCES_DIR}/aarch64/Lget_save_loc.c
    ${SOURCES_DIR}/aarch64/Lglobal.c
    ${SOURCES_DIR}/aarch64/Linit.c
    ${SOURCES_DIR}/aarch64/Linit_local.c
    ${SOURCES_DIR}/aarch64/Linit_remote.c
    ${SOURCES_DIR}/aarch64/Lis_signal_frame.c
    ${SOURCES_DIR}/aarch64/Lreg_states_iterate.c
    ${SOURCES_DIR}/aarch64/Lregs.c
    ${SOURCES_DIR}/aarch64/Lresume.c
    ${SOURCES_DIR}/aarch64/Lstash_frame.c
    ${SOURCES_DIR}/aarch64/Lstep.c
    ${SOURCES_DIR}/aarch64/Ltrace.c
    ${SOURCES_DIR}/aarch64/getcontext.S
    ${SOURCES_DIR}/aarch64/is_fpreg.c
    ${SOURCES_DIR}/aarch64/regname.c
  )
endif()

add_library(${LIBUNWIND_LIBRARY_NAME}
  STATIC
  ${LIBUNWIND_XAMARIN_SOURCES}
)

target_link_options(
  ${LIBUNWIND_LIBRARY_NAME}
  PRIVATE ${XA_DEFAULT_SYMBOL_VISIBILITY}
)
