#!/bin/bash -e
MY_DIR="$(dirname $0)"
PROJECT="${MY_DIR}/xat.csproj"

msbuild /r "${PROJECT}"
exec mono --debug "${MY_DIR}/../../bin/Debug/bin/xat.exe" "$@"
#exec dotnet run -p "${MY_DIR}/xat.csproj" -- "$@"
