#!/bin/bash

# Passed on command line
ADB_DEVICE=""
ARCH=""

# Values set by the preparation task
SESSION_LOG_DIR="."
SESSION_STDERR_LOG_FILE=""

# Detected during run
DEVICE_API_LEVEL=""
DEVICE_ABI=""
DEVICE_ARCH=""

# Constants
ABI_PROPERTIES=(
	# new properties
	"ro.product.cpu.abilist"

	# old properties
	"ro.product.cpu.abi"
	"ro.product.cpu.abi2"
)

function die()
{
	echo "$@" >&2
	exit 1
}

function die_with_log()
{
	local log_file="${1}"

	shift

	echo "$@" >&2
	if [ -f "${log_file}" ]; then
		echo >&2
		cat "${log_file}" >&2
		echo >&2
	fi

	exit 1
}

function run_adb_nocheck()
{
	local args=""

	if [ -n "${ADB_DEVICE}" ]; then
		args="-s ${ADB_DEVICE}"
	fi

	COMMAND_OUTPUT="$(adb ${args} "$@")"
}

function run_adb()
{
	local command_stderr="${SESSION_LOG_DIR}/adb-cmd-stderr.log"

	run_adb_nocheck "$@" 2> "${command_stderr}"
	if [ $? -ne 0 ]; then
		cat "${command_stderr}" >> "${SESSION_STDERR_LOG_FILE}"
		die_with_log "${command_stderr}" "ADB command failed: " adb "${args}" "$@"
	fi
}

function adb_shell()
{
	run_adb shell "$@"
}

function adb_shell_nocheck()
{
	run_adb_nocheck shell "$@"
}

function adb_get_property()
{
	adb_shell getprop "$@"
}

function get_api_level()
{
	adb_get_property ro.build.version.sdk
	if [ $? -ne 0 ]; then
		die "Unable to determine API level of the connected device"
	fi
	DEVICE_API_LEVEL="${COMMAND_OUTPUT}"
}

function property_is_equal_to()
{
	local prop_name="${1}"
	local expected_value="${2}"

	local prop_value
	adb_get_property "${prop_name}"
	prop_value=${COMMAND_OUTPUT}

	if [ -z "${prop_value}" -o "${prop_value}" != "${expected_value}" ]; then
		false
		return
	fi

	true
}

function warn_old_pixel_c()
{
	adb_shell_nocheck cat /proc/sys/kernel/yama/ptrace_scope "2> /dev/null"
	if [ $? -ne 0 ]; then
		true
		return
	fi

	local yama=${COMMAND_OUTPUT}
	if [ -z "${yama}" -o "${yama}" == "0" ]; then
		true
		return
	fi

	local prop_value
	adb_get_property ro.build.product
	prop_value=${COMMAND_OUTPUT}

	if ! property_is_equal_to "ro.build.product" "dragon"; then
		true
		return
	fi

	if ! property_is_equal_to "ro.product.name" "ryu"; then
		true
		return
	fi

	cat <<EOF >&2

WARNING: The device uses Yama ptrace_scope to restrict debugging. ndk-gdb will
    likely be unable to attach to a process. With root access, the restriction
    can be lifted by writing 0 to /proc/sys/kernel/yama/ptrace_scope. Consider
    upgrading your Pixel C to MXC89L or newer, where Yama is disabled.

EOF
}

if [ ! -d "${SESSION_LOG_DIR}" ]; then
	install -d -m 755 "${SESSION_LOG_DIR}"
fi

SESSION_STDERR_LOG_FILE="${SESSION_LOG_DIR}/adb-stderr.log"
rm -f "${SESSION_STDERR_LOG_FILE}"

warn_old_pixel_c
get_api_level

echo API: ${DEVICE_API_LEVEL}
