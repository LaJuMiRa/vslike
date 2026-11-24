#!/usr/bin/env bash
# Builds the Linux version of VsLike as a tarball with the final file system layout (usr/...), which the RPM spec
# and the PKGBUILD install unchanged. Runs on Linux and macOS (cross-publish). Needs the .NET SDK 10 or newer.
#
#   linux/build-linux.sh              architecture of this machine, version from linux/VERSION
#   ARCH=arm64 linux/build-linux.sh   x64 or arm64
#   VERSION=1.2.3 linux/build-linux.sh
#
# Result: linux/dist/vslike-<version>-linux-<arch>.tar.gz (+ .sha256) containing
#   usr/bin/vslike                              starter (runs the app on the installed dotnet)
#   usr/lib/vslike/vslike.dll                   the app (Avalonia, framework-dependent, roll forward to newer .NET)
#   usr/lib/vslike/engine/                      the analysis engine from engine/ (Roslyn, StyleCop, diagram writer)
#   usr/lib/vslike/debughook/                   startup hook for debugging programs started in the terminal
#   usr/lib/vslike/netcoredbg/                  the debugger (Samsung netcoredbg, MIT), checked by SHA-256
#   usr/share/applications, icons, mime, licenses
set -euo pipefail

cd "$(dirname "$0")"
LINUX="$(pwd)"
ROOT="$(cd .. && pwd)"
VERSION="${VERSION:-$(tr -d '[:space:]' < VERSION)}"
WORK="${VSLIKE_LINUX_WORK_DIR:-${TMPDIR:-/tmp}/vslike-linux-build}"
CACHE="${VSLIKE_CACHE_DIR:-${XDG_CACHE_HOME:-$HOME/.cache}/vslike}"
NETCOREDBG_VERSION="3.2.0-1092"

if [[ -z "${ARCH:-}" ]]; then
    case "$(uname -m)" in
        x86_64|amd64) ARCH=x64 ;;
        aarch64|arm64) ARCH=arm64 ;;
        *) echo "error: unsupported machine $(uname -m); set ARCH=x64 or ARCH=arm64" >&2; exit 1 ;;
    esac
fi
case "$ARCH" in
    # Hashes as in the earlier Linux packaging (git history, packaging/linux/build-tarball.sh).
    x64)   NCD_FILE="netcoredbg-linux-amd64.tar.gz"; NCD_SHA256="080eb3b2d2152465f599d3b33d1ee6e747794e11cc0a3773ec689f5e5f2c5afa" ;;
    arm64) NCD_FILE="netcoredbg-linux-arm64.tar.gz"; NCD_SHA256="065ff49badec8a695dbea2de6ab6a330c774a191e426a217ab8cc05250627ccb" ;;
    *) echo "error: ARCH must be x64 or arm64 (got '$ARCH')" >&2; exit 1 ;;
esac
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "error: VERSION must look like 1.2.3 (got '$VERSION')" >&2
    exit 1
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

sha256_check() { # <sha256> <file>
    if command -v sha256sum >/dev/null; then echo "$1  $2" | sha256sum -c - >/dev/null 2>&1
    else echo "$1  $2" | shasum -a 256 -c - >/dev/null 2>&1; fi
}
sha256_of() {
    if command -v sha256sum >/dev/null; then sha256sum "$1" | awk '{print $1}'; else shasum -a 256 "$1" | awk '{print $1}'; fi
}

STAGE="$WORK/stage-$ARCH"
LIB="$STAGE/usr/lib/vslike"
SHARE="$STAGE/usr/share"
rm -rf "$STAGE"
mkdir -p "$LIB" "$STAGE/usr/bin" "$SHARE/applications" "$SHARE/mime/packages" "$SHARE/licenses/vslike"

echo "==> Publishing the app (linux-$ARCH, $VERSION)"
# Framework-dependent: the user has the .NET SDK installed anyway (the IDE needs it); RID-specific for the native
# libraries (Skia, HarfBuzz, pty shim).
dotnet publish "$LINUX/src/VsLike.Desktop/VsLike.Desktop.csproj" -c Release -r "linux-$ARCH" --self-contained false \
    -p:UseAppHost=false -p:Version="$VERSION" -o "$LIB" -nologo -v:quiet
rm -f "$LIB"/*.pdb

echo "==> Publishing the analysis engine and the debug startup hook (unchanged from engine/)"
dotnet publish "$ROOT/engine/src/VsLike.Engine/VsLike.Engine.csproj" -c Release -p:UseAppHost=false \
    -p:Version="$VERSION" -o "$LIB/engine" -nologo -v:quiet
rm -f "$LIB/engine"/*.pdb
dotnet publish "$ROOT/engine/src/VsLike.DebugStartupHook/VsLike.DebugStartupHook.csproj" -c Release \
    -p:Version="$VERSION" -o "$LIB/debughook" -nologo -v:quiet

echo "==> Getting netcoredbg $NETCOREDBG_VERSION ($ARCH)"
ARCHIVE="$CACHE/netcoredbg-$NETCOREDBG_VERSION-$NCD_FILE"
mkdir -p "$CACHE"
if [[ ! -f "$ARCHIVE" ]] || ! sha256_check "$NCD_SHA256" "$ARCHIVE"; then
    curl -fsSL -o "$ARCHIVE.part" "https://github.com/Samsung/netcoredbg/releases/download/$NETCOREDBG_VERSION/$NCD_FILE"
    mv "$ARCHIVE.part" "$ARCHIVE"
fi
sha256_check "$NCD_SHA256" "$ARCHIVE" || { echo "error: netcoredbg checksum mismatch" >&2; exit 1; }
tar -xzf "$ARCHIVE" -C "$LIB"
chmod +x "$LIB/netcoredbg/netcoredbg"
# Also unpack into the cache, where the tests and a development build find it (same layout as on macOS).
if [[ "$(uname -s)" == "Linux" && ! -x "$CACHE/netcoredbg/$NETCOREDBG_VERSION/netcoredbg/netcoredbg" ]]; then
    mkdir -p "$CACHE/netcoredbg/$NETCOREDBG_VERSION"
    tar -xzf "$ARCHIVE" -C "$CACHE/netcoredbg/$NETCOREDBG_VERSION"
fi

echo "==> Starter, desktop entry, file types, icons, license"
cp "$LINUX/packaging/vslike.sh" "$STAGE/usr/bin/vslike"
chmod 755 "$STAGE/usr/bin/vslike"
cp "$LINUX/packaging/vslike.desktop" "$SHARE/applications/vslike.desktop"
cp "$LINUX/packaging/vslike-mime.xml" "$SHARE/mime/packages/vslike.xml"
for size in 16 32 48 64 128 256 512; do
    mkdir -p "$SHARE/icons/hicolor/${size}x${size}/apps"
    cp "$LINUX/packaging/icons/vslike-$size.png" "$SHARE/icons/hicolor/${size}x${size}/apps/vslike.png"
done
cp "$ROOT/LICENSE" "$SHARE/licenses/vslike/LICENSE"
find "$STAGE" -type d -exec chmod 755 {} +
find "$STAGE/usr/share" "$LIB" -type f ! -name netcoredbg -exec chmod 644 {} +
chmod 755 "$LIB/netcoredbg/netcoredbg" "$STAGE/usr/bin/vslike"

echo "==> Packing"
mkdir -p "$LINUX/dist"
TARBALL="$LINUX/dist/vslike-$VERSION-linux-$ARCH.tar.gz"
rm -f "$TARBALL" "$TARBALL.sha256"
# Without macOS metadata (AppleDouble files) when built on a Mac.
COPYFILE_DISABLE=1 tar -czf "$TARBALL" -C "$STAGE" usr
echo "$(sha256_of "$TARBALL")  $(basename "$TARBALL")" > "$TARBALL.sha256"
echo "Done: $TARBALL"
echo "      sha256: $(cut -d' ' -f1 < "$TARBALL.sha256")"
