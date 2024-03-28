cmake_minimum_required(VERSION 3.19)

#
# MUST be included before project()!
#
include("../../build-tools/cmake/xa_preamble.cmake")

project(
  native-libs
  VERSION ${XA_VERSION}
  LANGUAGES CXX
)

#
# MUST be included after project()!
#
include("../../build-tools/cmake/xa_common.cmake")
