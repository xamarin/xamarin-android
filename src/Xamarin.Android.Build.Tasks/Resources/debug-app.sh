#!/bin/bash
MY_DIR="$(cd $(dirname $0);pwd)"

ADB_PATH="@ADB_PATH@"
APP_LIBS_DIR="@APP_LIBS_DIR@"
CONFIG_SCRIPT_NAME="@CONFIG_SCRIPT_NAME@"
DEBUG_SESSION_PREP_PATH="@DEBUG_SESSION_PREP_PATH@"
DEFAULT_ACTIVITY_NAME="@ACTIVITY_NAME@"
LLDB_SCRIPT_NAME="@LLDB_SCRIPT_NAME@"
NDK_DIR="@NDK_DIR@"
OUTPUT_DIR="@OUTPUT_DIR@"
PACKAGE_NAME="@PACKAGE_NAME@"
SUPPORTED_ABIS_ARRAY=(@SUPPORTED_ABIS@)
ADB_DEVICE=""

function die()
{
	echo "$@"
	exit 1
}

function die_if_failed()
{
	if [ $? -ne 0 ]; then
		die "$@"
	fi
}

function run_adb_no_echo()
{
	local args=""

	if [ -n "${ADB_DEVICE}" ]; then
		args="-s ${ADB_DEVICE}"
	fi

	adb ${args} "$@"
}

function run_adb()
{
	echo Running: adb ${args} "$@"
	run_adb_no_echo "$@"
}

function run_adb_echo_info()
{
	local log_file="$1"
	shift

	echo Running: adb ${args} "$@"
	echo Logging to: "${log_file}"
	echo
}

function run_adb_with_log()
{
	local log_file="$1"
	shift

	run_adb_echo_info "${log_file}" "$@"
	run_adb_no_echo "$@" > "${log_file}" 2>&1
}

function run_adb_with_log_bg()
{
	local log_file="$1"
	shift

	run_adb_echo_info "${log_file}" "$@"
	run_adb_no_echo "$@" > "${log_file}" 2>&1 &
}

#TODO: APP_LIBS_DIR needs to be appended the abi-specific subdir
#TODO: make NDK_DIR overridable via a parameter
#TOOD: add a parameter to specify the Android device to target
#TODO: detect whether we have dotnet in $PATH and whether it's a compatible version
#TODO: add an option to make XA wait for debugger to connect
#TODO: add a parameter to specify activity to start

ACTIVITY_NAME="${DEFAULT_ACTIVITY_NAME}"

SUPPORTED_ABIS_ARG=""
for sa in "${SUPPORTED_ABIS_ARRAY[@]}"; do
	if [ -z "${SUPPORTED_ABIS_ARG}" ]; then
		SUPPORTED_ABIS_ARG="${sa}"
	else
		SUPPORTED_ABIS_ARG="${SUPPORTED_ABIS_ARG},${sa}"
	fi
done

dotnet "${DEBUG_SESSION_PREP_PATH}" \
	   -s "${SUPPORTED_ABIS_ARG}" \
	   -p "${PACKAGE_NAME}" \
	   -n "${NDK_DIR}" \
	   -o "${OUTPUT_DIR}" \
	   -l "${APP_LIBS_DIR}" \
	   -c "${CONFIG_SCRIPT_NAME}" \
	   -g "${LLDB_SCRIPT_NAME}"

die_if_failed Debug preparation app failed

CONFIG_SCRIPT_PATH="${OUTPUT_DIR}/${CONFIG_SCRIPT_NAME}"
if [ ! -f "${CONFIG_SCRIPT_PATH}" ]; then
	die Config script ${CONFIG_SCRIPT_PATH} not found
fi

source "${CONFIG_SCRIPT_PATH}"

# Determine cross section of supported and device ABIs
ALLOWED_ABIS=()

for dabi in "${DEVICE_AVAILABLE_ABIS[@]}"; do
	for sabi in "${SUPPORTED_ABIS_ARRAY[@]}"; do
		if [ "${dabi}" == "${sabi}" ]; then
			ALLOWED_ABIS+="${dabi}"
		fi
	done
done

if [ ${#ALLOWED_ABIS[@]} -le 0 ]; then
	die Application does not support any ABIs available on device
fi

ADB_DEBUG_SERVER_LOG="${OUTPUT_DIR}/lldb-debug-server.log"
echo Starting debug server on device
echo stdout and stderr will be redirected to: ${ADB_DEBUG_SERVER_LOG}

LLDB_SERVER_PLATFORM_LOG="${DEVICE_LLDB_DIR}/log/platform.log"
LLDB_SERVER_STDOUT_LOG="${DEVICE_LLDB_DIR}/log/platform-stdout.log"
LLDB_SERVER_GDB_LOG="${DEVICE_LLDB_DIR}/log/gdb-server.log"

run_adb shell run-as ${PACKAGE_NAME} kill -9 "\"\`pidof ${DEVICE_DEBUG_SERVER_LAUNCHER}\`\""
run_adb_with_log_bg "${ADB_DEBUG_SERVER_LOG}" shell run-as ${PACKAGE_NAME} ${DEVICE_DEBUG_SERVER_LAUNCHER} ${DEVICE_LLDB_DIR} ${SOCKET_SCHEME} ${SOCKET_DIR} ${SOCKET_NAME} "\"lldb process:gdb-remote packets\""

LAUNCH_SPEC=${PACKAGE_NAME}/${ACTIVITY_NAME}
run_adb shell am start -S -W ${LAUNCH_SPEC}
die_if_failed Failed to start ${LAUNCH_SPEC}

APP_PID=$(run_adb_no_echo shell pidof ${PACKAGE_NAME})
die_if_failed Failed to get ${PACKAGE_NAME} PID on device

LLDB_SCRIPT_PATH="${OUTPUT_DIR}/${LLDB_SCRIPT_NAME}"
echo App PID: ${APP_PID}
echo "attach ${APP_PID}" >> "${LLDB_SCRIPT_PATH}"

#TODO: start the app if not running
#TODO: pass app pid to lldb, with -p or --attach-pid
export TERMINFO=/usr/share/terminfo
"${LLDB_PATH}" --source "${LLDB_SCRIPT_PATH}"

run_adb_with_log "${OUTPUT_DIR}/lldb-platform.log" shell run-as ${PACKAGE_NAME} cat ${LLDB_SERVER_PLATFORM_LOG}
run_adb_with_log "${OUTPUT_DIR}/lldb-platform-stdout.log" shell run-as ${PACKAGE_NAME} cat ${LLDB_SERVER_STDOUT_LOG}
run_adb_with_log "${OUTPUT_DIR}/lldb-gdb-server.log" shell run-as ${PACKAGE_NAME} cat ${LLDB_SERVER_GDB_LOG}
