#!/usr/bin/env bash
# Builds build/VsLike.app (arm64, ad-hoc signed). Needs Xcode (or the Command Line Tools) and the .NET SDK 10.
#
#   ./build.sh            release build, version from the VERSION file
#   VERSION=1.2.3 ./build.sh
#
# Contents of the app:
#   Contents/MacOS/VsLike              the native app (Swift)
#   Contents/Resources/engine/         the analysis engine (Roslyn, StyleCop, diagram writer; runs on the installed .NET)
#   Contents/Resources/netcoredbg/     the debugger (Samsung netcoredbg, MIT), downloaded once and checked by SHA-256
#
# The app is assembled and signed in a temporary folder and copied to build/ afterwards: folders synced by iCloud
# Drive add Finder metadata that makes `codesign` fail.
set -euo pipefail

cd "$(dirname "$0")"
ROOT="$(pwd)"
VERSION="${VERSION:-$(tr -d '[:space:]' < VERSION)}"
BUILD_NUMBER="${BUILD_NUMBER:-$(git rev-list --count HEAD 2>/dev/null || echo 1)}"
WORK="${VSLIKE_WORK_DIR:-${TMPDIR:-/tmp}/vslike-build}"
SCRATCH="$WORK/spm"
STAGE="$WORK/stage"
APP_NAME="VsLike.app"
CACHE="${VSLIKE_CACHE_DIR:-$HOME/Library/Caches/VsLike}"
NETCOREDBG_VERSION="3.2.0-1092"
NETCOREDBG_SHA256="f4fa33b3ff874910cc184b4bb3b9c56d0abdf5c6521cee0b144d7c6e4a6e59ea"
NETCOREDBG_URL="https://github.com/Samsung/netcoredbg/releases/download/$NETCOREDBG_VERSION/netcoredbg-osx-arm64.zip"

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "error: VERSION must look like 1.2.3 (got '$VERSION')" >&2
    exit 1
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "==> Building VsLike $VERSION ($BUILD_NUMBER) for arm64"
swift build -c release --arch arm64 --scratch-path "$SCRATCH"
BIN_DIR="$(swift build -c release --arch arm64 --scratch-path "$SCRATCH" --show-bin-path)"

echo "==> Publishing the analysis engine"
rm -rf "$WORK/engine"
dotnet publish engine/src/VsLike.Engine/VsLike.Engine.csproj -c Release -p:UseAppHost=false -p:Version="$VERSION" \
    -o "$WORK/engine" -nologo -v:quiet
rm -f "$WORK/engine"/*.pdb

echo "==> Getting netcoredbg $NETCOREDBG_VERSION"
ARCHIVE="$CACHE/netcoredbg-$NETCOREDBG_VERSION-osx-arm64.zip"
mkdir -p "$CACHE"
if [[ ! -f "$ARCHIVE" ]] || ! echo "$NETCOREDBG_SHA256  $ARCHIVE" | shasum -a 256 -c - >/dev/null 2>&1; then
    curl -fsSL -o "$ARCHIVE.part" "$NETCOREDBG_URL"
    mv "$ARCHIVE.part" "$ARCHIVE"
fi
echo "$NETCOREDBG_SHA256  $ARCHIVE" | shasum -a 256 -c - >/dev/null
# Also unpack into the cache, where the tests and `swift run` find it.
if [[ ! -x "$CACHE/netcoredbg/$NETCOREDBG_VERSION/netcoredbg/netcoredbg" ]]; then
    mkdir -p "$CACHE/netcoredbg/$NETCOREDBG_VERSION"
    unzip -q -o "$ARCHIVE" -d "$CACHE/netcoredbg/$NETCOREDBG_VERSION"
fi

echo "==> Assembling app bundle"
rm -rf "$STAGE"
CONTENTS="$STAGE/$APP_NAME/Contents"
mkdir -p "$CONTENTS/MacOS" "$CONTENTS/Resources"
cp "$BIN_DIR/VsLike" "$CONTENTS/MacOS/VsLike"
# SwiftPM resource bundles of dependencies (if any) belong next to the executable's resources.
for bundle in "$BIN_DIR"/*.bundle; do
    [[ -e "$bundle" ]] && cp -R "$bundle" "$CONTENTS/Resources/"
done
sed -e "s/__VERSION__/$VERSION/" -e "s/__BUILD__/$BUILD_NUMBER/" packaging/Info.plist > "$CONTENTS/Info.plist"
plutil -lint -s "$CONTENTS/Info.plist"
cp -R Sources/VsLike/Resources/*.lproj "$CONTENTS/Resources/"
cp -R "$WORK/engine" "$CONTENTS/Resources/engine"
unzip -q "$ARCHIVE" -d "$CONTENTS/Resources"
chmod +x "$CONTENTS/Resources/netcoredbg/netcoredbg"

echo "==> Drawing app icon"
ICON_TOOL="$WORK/make-icon"
if [[ ! -x "$ICON_TOOL" || scripts/make-icon.swift -nt "$ICON_TOOL" ]]; then
    swiftc -O scripts/make-icon.swift -o "$ICON_TOOL"
fi
rm -rf "$WORK/AppIcon.iconset"
"$ICON_TOOL" "$WORK/AppIcon.iconset"
iconutil -c icns "$WORK/AppIcon.iconset" -o "$CONTENTS/Resources/AppIcon.icns"

echo "==> Signing (ad-hoc)"
xattr -cr "$STAGE/$APP_NAME"
# Nested code first: the debugger and its libraries, then the app with the hardened runtime.
find "$CONTENTS/Resources/netcoredbg" -type f \( -name "*.dylib" -o -name "netcoredbg" \) -exec codesign --force --sign - --timestamp=none {} \;
codesign --force --sign - --options runtime --timestamp=none "$STAGE/$APP_NAME"
codesign --verify --strict --verbose=1 "$STAGE/$APP_NAME"

echo "==> Copying to build/"
mkdir -p "$ROOT/build"
rm -rf "$ROOT/build/$APP_NAME"
ditto "$STAGE/$APP_NAME" "$ROOT/build/$APP_NAME"
echo "Done: build/$APP_NAME"
