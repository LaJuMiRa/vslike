#!/usr/bin/env bash
# Baut ein self-contained Linux-Paket (tar.gz; App in lib/, Starter vslike oben – kein Namenskonflikt mit VsLike
# auf case-insensitiven Dateisystemen) mit gebündeltem netcoredbg, Starter, .desktop-Datei und Icon.
# Aufruf: packaging/linux/build-tarball.sh <linux-x64|linux-arm64> <version> [ausgabeordner]
set -euo pipefail

RID="${1:?runtime id (linux-x64|linux-arm64)}"
VERSION="${2:?version}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="${3:-$ROOT/artifacts.nosync/package}"
NAME="vslike-$VERSION-$RID"
STAGE="$OUT/work-$RID/$NAME"

case "$RID" in
  linux-x64)   NCD_URL="https://github.com/Samsung/netcoredbg/releases/download/3.2.0-1092/netcoredbg-linux-amd64.tar.gz"; NCD_SHA="080eb3b2d2152465f599d3b33d1ee6e747794e11cc0a3773ec689f5e5f2c5afa" ;;
  linux-arm64) NCD_URL="https://github.com/Samsung/netcoredbg/releases/download/3.2.0-1092/netcoredbg-linux-arm64.tar.gz"; NCD_SHA="065ff49badec8a695dbea2de6ab6a330c774a191e426a217ab8cc05250627ccb" ;;
  *) echo "unsupported runtime: $RID" >&2; exit 2 ;;
esac

rm -rf "$OUT/work-$RID"
mkdir -p "$STAGE"

echo "==> publish ($RID, $VERSION)"
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet publish "$ROOT/src/VsLike.App/VsLike.App.csproj" \
  -c Release -r "$RID" --self-contained true -p:Version="$VERSION" -p:UseAppHost=true -o "$STAGE/lib" -nologo

echo "==> netcoredbg"
ARCHIVE="$OUT/work-$RID/netcoredbg.tar.gz"
curl -fsSL -o "$ARCHIVE" "$NCD_URL"
if command -v sha256sum >/dev/null; then echo "$NCD_SHA  $ARCHIVE" | sha256sum -c -; else echo "$NCD_SHA  $ARCHIVE" | shasum -a 256 -c -; fi
tar -xzf "$ARCHIVE" -C "$STAGE/lib"
chmod +x "$STAGE/lib/netcoredbg/netcoredbg"
rm "$ARCHIVE"

echo "==> launcher, desktop entry, icon"
install -m 755 "$ROOT/packaging/linux/vslike.sh" "$STAGE/vslike"
install -m 644 "$ROOT/packaging/linux/vslike.desktop" "$STAGE/vslike.desktop"
install -m 644 "$ROOT/packaging/linux/vslike.png" "$STAGE/vslike.png"
install -m 644 "$ROOT/LICENSE" "$STAGE/LICENSE"
chmod +x "$STAGE/lib/VsLike"

TARBALL="$OUT/$NAME.tar.gz"
tar -czf "$TARBALL" -C "$OUT/work-$RID" "$NAME"
if command -v sha256sum >/dev/null; then sha256sum "$TARBALL" | tee "$TARBALL.sha256"; else shasum -a 256 "$TARBALL" | tee "$TARBALL.sha256"; fi
echo "==> done: $TARBALL"
