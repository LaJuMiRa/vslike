#!/usr/bin/env bash
# Renders the Arch Linux PKGBUILD (for the AUR package vslike-bin) and builds the package from the local tarball.
# Run linux/build-linux.sh first. Arch Linux images exist only for x86_64, so the package is built for x86_64;
# the rendered PKGBUILD covers x86_64 and aarch64 when both tarballs are in linux/dist.
#
#   linux/packaging/build-arch.sh
#
# Result: linux/dist/arch/PKGBUILD (+ .SRCINFO) and linux/dist/vslike-bin-<version>-1-x86_64.pkg.tar.zst
set -euo pipefail

cd "$(dirname "$0")/.."
LINUX="$(pwd)"
VERSION="${VERSION:-$(tr -d '[:space:]' < VERSION)}"
ARCH_IMAGE="${ARCH_IMAGE:-docker.io/library/archlinux:latest}"
sha_of() { local f="dist/vslike-$VERSION-linux-$1.tar.gz.sha256"; [[ -f "$f" ]] && cut -d' ' -f1 < "$f" || echo SKIP; }
SHA_X64="$(sha_of x64)"
SHA_ARM64="$(sha_of arm64)"
[[ "$SHA_X64" != SKIP ]] || { echo "error: dist/vslike-$VERSION-linux-x64.tar.gz missing; run ARCH=x64 linux/build-linux.sh" >&2; exit 1; }
[[ "$SHA_ARM64" != SKIP ]] || echo "warning: no arm64 tarball; the PKGBUILD skips the aarch64 checksum" >&2

mkdir -p dist/arch
sed -e "s/__VERSION__/$VERSION/" -e "s/__SHA256_X64__/$SHA_X64/" -e "s/__SHA256_ARM64__/$SHA_ARM64/" \
    packaging/PKGBUILD.in > dist/arch/PKGBUILD

# makepkg must not run as root; the local tarball has the same name as the release download, so it is not fetched.
INNER='set -euo pipefail
# pacman cannot use its sandbox under emulation (x86_64 container on an arm64 host).
pacman -Sy --disable-sandbox --noconfirm --needed base-devel >/dev/null
useradd -m builder
work=/home/builder/pkg
mkdir -p "$work"
cp /linux/dist/arch/PKGBUILD "$work/"
cp "/linux/dist/vslike-'"$VERSION"'-linux-x64.tar.gz" "$work/"
chown -R builder "$work"
su builder -c "cd $work && makepkg --nodeps --noconfirm >/dev/null && makepkg --printsrcinfo > .SRCINFO"
cp "$work"/vslike-bin-*.pkg.tar.zst /linux/dist/
cp "$work/.SRCINFO" /linux/dist/arch/.SRCINFO
ls /linux/dist/*.pkg.tar.zst'

if [[ -f /etc/arch-release ]] && [[ "$(id -u)" != 0 ]]; then
    work="$(mktemp -d)"
    cp dist/arch/PKGBUILD "dist/vslike-$VERSION-linux-x64.tar.gz" "$work/"
    (cd "$work" && makepkg --nodeps --noconfirm && makepkg --printsrcinfo > .SRCINFO)
    cp "$work"/vslike-bin-*.pkg.tar.zst dist/
    cp "$work/.SRCINFO" dist/arch/.SRCINFO
else
    ENGINE="$(command -v docker || command -v podman)" || { echo "error: needs Arch Linux, docker or podman" >&2; exit 1; }
    "$ENGINE" run --rm --platform linux/amd64 -v "$LINUX:/linux" "$ARCH_IMAGE" bash -c "$INNER"
fi
