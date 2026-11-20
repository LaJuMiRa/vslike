#!/usr/bin/env bash
# Erzeugt Casks/vslike.rb aus der Vorlage. Aufruf: render-cask.sh <version> <sha256-arm64> <sha256-x64> [ausgabe]
set -euo pipefail
VERSION="${1:?version}"; ARM="${2:?sha256 arm64}"; X64="${3:?sha256 x64}"
OUT="${4:-/dev/stdout}"
DIR="$(cd "$(dirname "$0")" && pwd)"
sed -e "s/@VERSION@/$VERSION/" -e "s/@SHA256_ARM64@/$ARM/" -e "s/@SHA256_X64@/$X64/" "$DIR/vslike.rb.template" > "$OUT"
