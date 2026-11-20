#!/usr/bin/env bash
# Baut VsLike.app (self-contained) samt netcoredbg und Icon, dazu .zip und .dmg.
# Aufruf: packaging/macos/build-app.sh <osx-arm64|osx-x64> <version> [ausgabeordner]
# Optionale Signierung/Notarisierung über Umgebungsvariablen (siehe docs/RELEASE_CHECKLIST.md):
#   MACOS_SIGN_IDENTITY, MACOS_NOTARY_APPLE_ID, MACOS_NOTARY_TEAM_ID, MACOS_NOTARY_PASSWORD
set -euo pipefail

RID="${1:?runtime id (osx-arm64|osx-x64)}"
VERSION="${2:?version}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="${3:-$ROOT/artifacts.nosync/package}"
WORK="$OUT/work-$RID"
APP="$WORK/VsLike.app"

case "$RID" in
  osx-arm64) NCD_URL="https://github.com/Samsung/netcoredbg/releases/download/3.2.0-1092/netcoredbg-osx-arm64.zip"; NCD_SHA="f4fa33b3ff874910cc184b4bb3b9c56d0abdf5c6521cee0b144d7c6e4a6e59ea" ;;
  osx-x64)   NCD_URL="https://github.com/Samsung/netcoredbg/releases/download/3.1.3-1062/netcoredbg-osx-amd64.tar.gz"; NCD_SHA="49459b066836b6a452f418501d7ecab57bcd7e60d8464faac21ff70b496b8634" ;;
  *) echo "unsupported runtime: $RID" >&2; exit 2 ;;
esac

rm -rf "$WORK"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

echo "==> publish ($RID, $VERSION)"
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet publish "$ROOT/src/VsLike.App/VsLike.App.csproj" \
  -c Release -r "$RID" --self-contained true -p:Version="$VERSION" -p:UseAppHost=true \
  -o "$APP/Contents/MacOS" -nologo

echo "==> netcoredbg"
ARCHIVE="$WORK/netcoredbg-archive"
curl -fsSL -o "$ARCHIVE" "$NCD_URL"
echo "$NCD_SHA  $ARCHIVE" | shasum -a 256 -c -
if [[ "$NCD_URL" == *.zip ]]; then
  unzip -q "$ARCHIVE" -d "$WORK/ncd"
else
  mkdir -p "$WORK/ncd" && tar -xzf "$ARCHIVE" -C "$WORK/ncd"
fi
cp -R "$WORK/ncd/netcoredbg" "$APP/Contents/MacOS/netcoredbg"
chmod +x "$APP/Contents/MacOS/netcoredbg/netcoredbg"
rm -rf "$WORK/ncd" "$ARCHIVE"

echo "==> icon + Info.plist"
ICONSET="$WORK/VsLike.iconset"
mkdir -p "$ICONSET"
swift "$ROOT/packaging/macos/make-icon.swift" "$WORK/icon-1024.png" 1024
for size in 16 32 128 256 512; do
  sips -z $size $size "$WORK/icon-1024.png" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z $double $double "$WORK/icon-1024.png" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/VsLike.icns"
sed "s/@VERSION@/$VERSION/g" "$ROOT/packaging/macos/Info.plist.template" > "$APP/Contents/Info.plist"
plutil -lint "$APP/Contents/Info.plist" >/dev/null

echo "==> sign"
# Erweiterte Attribute (Finder-Infos, iCloud) lassen codesign scheitern.
xattr -cr "$APP"
if [[ -n "${MACOS_SIGN_IDENTITY:-}" ]]; then
  codesign --force --deep --options runtime --timestamp --entitlements "$ROOT/packaging/macos/entitlements.plist" -s "$MACOS_SIGN_IDENTITY" "$APP"
else
  # Ad-hoc-Signatur: nötig, damit Apple Silicon das Bundle startet; Gatekeeper-Hinweis siehe README.
  codesign --force --deep -s - "$APP"
fi

ZIP="$OUT/VsLike-$VERSION-$RID.zip"
DMG="$OUT/VsLike-$VERSION-$RID.dmg"
rm -f "$ZIP" "$DMG"
ditto -c -k --keepParent "$APP" "$ZIP"

if [[ -n "${MACOS_SIGN_IDENTITY:-}" && -n "${MACOS_NOTARY_APPLE_ID:-}" ]]; then
  echo "==> notarize"
  xcrun notarytool submit "$ZIP" --apple-id "$MACOS_NOTARY_APPLE_ID" --team-id "$MACOS_NOTARY_TEAM_ID" --password "$MACOS_NOTARY_PASSWORD" --wait
  xcrun stapler staple "$APP"
  rm -f "$ZIP" && ditto -c -k --keepParent "$APP" "$ZIP"
fi

hdiutil create -quiet -volname "VsLike" -srcfolder "$APP" -ov -format UDZO "$DMG"
shasum -a 256 "$ZIP" | tee "$ZIP.sha256"
echo "==> done: $ZIP"
