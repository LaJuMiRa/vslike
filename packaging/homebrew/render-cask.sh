#!/usr/bin/env bash
# Writes Casks/vslike.rb from the template. Usage: render-cask.sh <version> <sha256> [output]
set -euo pipefail
VERSION="${1:?version}"
SHA="${2:?sha256}"
OUT="${3:-/dev/stdout}"
DIR="$(cd "$(dirname "$0")" && pwd)"
sed -e "s/@VERSION@/$VERSION/" -e "s/@SHA256@/$SHA/" "$DIR/vslike.rb.template" > "$OUT"
