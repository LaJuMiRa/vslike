#!/usr/bin/env sh
# Starter für VsLike: findet das Installationsverzeichnis auch über Symlinks (Homebrew bin/).
SELF="$(readlink -f "$0" 2>/dev/null || echo "$0")"
DIR="$(dirname "$SELF")"
exec "$DIR/lib/VsLike" "$@"
