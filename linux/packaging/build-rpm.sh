#!/usr/bin/env bash
# Builds the Fedora RPM from linux/dist/vslike-<version>-linux-<arch>.tar.gz (run linux/build-linux.sh first).
# On Fedora it uses the local rpmbuild, elsewhere a Fedora container (docker or podman).
#
#   linux/packaging/build-rpm.sh            architecture of this machine
#   ARCH=x64 linux/packaging/build-rpm.sh   x64 -> x86_64, arm64 -> aarch64
#
# Result: linux/dist/vslike-<version>-1.<dist>.<x86_64|aarch64>.rpm
set -euo pipefail

cd "$(dirname "$0")/.."
LINUX="$(pwd)"
VERSION="${VERSION:-$(tr -d '[:space:]' < VERSION)}"
FEDORA_IMAGE="${FEDORA_IMAGE:-registry.fedoraproject.org/fedora:43}"
if [[ -z "${ARCH:-}" ]]; then
    case "$(uname -m)" in x86_64|amd64) ARCH=x64 ;; *) ARCH=arm64 ;; esac
fi
case "$ARCH" in x64) RPM_ARCH=x86_64; PLATFORM=linux/amd64 ;; arm64) RPM_ARCH=aarch64; PLATFORM=linux/arm64 ;;
    *) echo "error: ARCH must be x64 or arm64" >&2; exit 1 ;; esac
TARBALL="dist/vslike-$VERSION-linux-$ARCH.tar.gz"
[[ -f "$TARBALL" ]] || { echo "error: $TARBALL missing; run linux/build-linux.sh first" >&2; exit 1; }

# Runs inside Fedora with linux/ mounted at /linux.
INNER='set -euo pipefail
command -v rpmbuild >/dev/null || dnf install -y -q rpm-build >/dev/null
top="$(mktemp -d)"
mkdir -p "$top/SOURCES"
cp "/linux/'"$TARBALL"'" "$top/SOURCES/"
rpmbuild -bb --target '"$RPM_ARCH"' --define "_topdir $top" --define "vslike_version '"$VERSION"'" /linux/packaging/vslike.spec >/dev/null
cp "$top"/RPMS/*/vslike-*.rpm /linux/dist/
ls /linux/dist/*.rpm'

if [[ -f /etc/fedora-release ]]; then
    bash -c "${INNER//\/linux/$LINUX}"
else
    ENGINE="$(command -v docker || command -v podman)" || { echo "error: needs Fedora, docker or podman" >&2; exit 1; }
    "$ENGINE" run --rm --platform "$PLATFORM" -v "$LINUX:/linux" "$FEDORA_IMAGE" bash -c "$INNER"
fi
