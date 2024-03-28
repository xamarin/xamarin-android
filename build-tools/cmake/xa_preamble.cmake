set(CMAKE_OSX_DEPLOYMENT_TARGET 10.12)

#
# Read product version
#
file(STRINGS "../../Directory.Build.props" XA_PRODUCT_VERSION_XML REGEX "^[ \t]*<ProductVersion>(.*)</ProductVersion>")
string(REGEX REPLACE "^[ \t]*<ProductVersion>(.*)</ProductVersion>" "\\1" XA_VERSION "${XA_PRODUCT_VERSION_XML}")
