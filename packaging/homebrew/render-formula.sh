#!/usr/bin/env bash
# Erzeugt Formula/vslike.rb aus der Vorlage. Aufruf: render-formula.sh <version> <sha256-x64> <sha256-arm64> [ausgabe]
set -euo pipefail
VERSION="${1:?version}"; X64="${2:?sha256 x64}"; ARM="${3:?sha256 arm64}"
OUT="${4:-/dev/stdout}"
DIR="$(cd "$(dirname "$0")" && pwd)"
sed -e "s/@VERSION@/$VERSION/" -e "s/@SHA256_X64@/$X64/" -e "s/@SHA256_ARM64@/$ARM/" "$DIR/vslike-formula.rb.template" > "$OUT"
