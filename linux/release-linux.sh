#!/usr/bin/env bash
# Tests the Linux version and builds its packages. Counterpart of ./release.sh (macOS), which stays unchanged.
#
#   linux/release-linux.sh                 architecture of this machine, version from linux/VERSION
#   ARCH=x64 linux/release-linux.sh
#   SKIP_TESTS=1 linux/release-linux.sh    package only (not for real releases)
#   SKIP_PACKAGES=1 linux/release-linux.sh tarball only (no RPM / Arch package)
#
# Steps:
#   1. engine tests   dotnet test engine/VsLike.Engine.slnx (the engine is shared with macOS and not modified here)
#   2. Linux tests    dotnet test linux/VsLike.Linux.slnx (Kit, workspace against the real engine, headless UI)
#   3. tarball        linux/build-linux.sh -> linux/dist/vslike-<version>-linux-<arch>.tar.gz
#   4. packages       RPM (Fedora container) and, on x64, the Arch package + rendered PKGBUILD (Arch container)
#
# Used locally and by .github/workflows/linux-release.yml.
set -euo pipefail

cd "$(dirname "$0")"
LINUX="$(pwd)"
ROOT="$(cd .. && pwd)"
export VERSION="${VERSION:-$(tr -d '[:space:]' < VERSION)}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
if [[ -z "${ARCH:-}" ]]; then
    case "$(uname -m)" in x86_64|amd64) ARCH=x64 ;; *) ARCH=arm64 ;; esac
fi
export ARCH

if [[ "${SKIP_TESTS:-0}" != "1" ]]; then
    echo "==> Engine tests"
    dotnet build "$ROOT/engine/VsLike.Engine.slnx" -nologo -v:quiet
    dotnet test "$ROOT/engine/VsLike.Engine.slnx" --no-build -nologo
fi

# Before the Linux tests: build-linux.sh caches netcoredbg, where the debugger tests find it.
./build-linux.sh

if [[ "${SKIP_TESTS:-0}" != "1" ]]; then
    echo "==> Linux tests"
    dotnet build VsLike.Linux.slnx -nologo -v:quiet
    dotnet test VsLike.Linux.slnx --no-build -nologo
fi

if [[ "${SKIP_PACKAGES:-0}" != "1" ]]; then
    echo "==> RPM"
    packaging/build-rpm.sh
    if [[ "$ARCH" == "x64" ]]; then
        echo "==> Arch package"
        packaging/build-arch.sh
    fi
fi

echo "==> linux/dist"
ls -1 dist
