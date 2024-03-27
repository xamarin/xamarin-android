#!/bin/bash -e
MY_DIR="$(dirname $0)"
HOST="$(uname | tr A-Z a-z)"

MONODROID_SOURCE_DIR="${MY_DIR}/../../src/monodroid/jni"
GENERATOR_SOURCE="${MONODROID_SOURCE_DIR}/generate-pinvoke-tables.cc"
GENERATOR_BINARY="${MONODROID_SOURCE_DIR}/generate-pinvoke-tables"
TARGET_FILE="${MONODROID_SOURCE_DIR}/pinvoke-tables.include"
GENERATED_FILE="${TARGET_FILE}.generated"
DIFF_FILE="${TARGET_FILE}.diff"
EXTERNAL_DIR="${MY_DIR}/../../external/"

function die()
{
	echo "$@"
	exit 1
}

function usage()
{
	cat <<EOF
Usage: ${MY_NAME} [OPTIONS]

where OPTIONS are one or more of:

   -c|--ci                  indicates that the script runs on one of the Xamarin.Android CI build
                            servers. This affects selection of the compiler
   -t|--test-only           indicate that the script should not replace the target file but merely
                            test whether the file is different to the newly generated one
   -h|--help                show this help screen
EOF

	exit 0
}

RUNNING_ON_CI="no"
TEST_ONLY="no"

while (( "$#" )); do
    case "$1" in
		-c|--ci) RUNNING_ON_CI="yes"; shift ;;
		-t|--test-only) TEST_ONLY="yes"; shift ;;
		-h|--help) usage ;;
		*) shift ;;
	esac
done

case ${HOST} in
	linux)
		if [ "${RUNNING_ON_CI}" == "no" ]; then
			COMPILER="g++"
		else
			COMPILER="g++-10"
		fi ;;

	darwin)
		if [ "${RUNNING_ON_CI}" == "no" ]; then
			COMPILER="clang++"
		else
			COMPILER="g++-11"
		fi ;;

	*) die Unsupported OS ;;
esac

${COMPILER} -O2 -std=c++20 -I${EXTERNAL_DIR} -I${EXTERNAL_DIR}/constexpr-xxh3 "${GENERATOR_SOURCE}" -o "${GENERATOR_BINARY}"
"${GENERATOR_BINARY}" "${GENERATED_FILE}"

FILES_DIFFER="no"
cmp "${GENERATED_FILE}" "${TARGET_FILE}" > /dev/null 2>&1 || FILES_DIFFER="yes"

RETVAL=0
if [ "${TEST_ONLY}" == "no" ]; then
	if [ "${FILES_DIFFER}" == "yes" ]; then
		mv "${GENERATED_FILE}" "${TARGET_FILE}"
	else
		rm "${GENERATED_FILE}"
	fi
else
	if [ "${FILES_DIFFER}" == "yes" ]; then
		echo "Generated p/invokes table file differs from the current one"
		diff -U3 -Narp "${TARGET_FILE}" "${GENERATED_FILE}" > "${DIFF_FILE}"

		echo "Diff file saved in: ${DIFF_FILE}"
		echo "------ DIFF START ------"
		cat "${DIFF_FILE}"
		echo "------ DIFF END ------"
		echo
		RETVAL=1
	else
		echo Generated file is identical to the current one
	fi
fi

exit ${RETVAL}
