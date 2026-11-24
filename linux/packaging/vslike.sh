#!/bin/sh
# Starts VsLike on the installed .NET (SDK 10 or newer; the app rolls forward to newer runtimes).
if ! command -v dotnet >/dev/null 2>&1; then
    echo "vslike: the .NET SDK 10 or newer is required (Fedora: dnf install dotnet-sdk-10.0, Arch: pacman -S dotnet-sdk)" >&2
    exit 1
fi
exec dotnet /usr/lib/vslike/vslike.dll "$@"
