#!/bin/sh
# Captures only the VsLike window (no screen recording of other windows): scripts/shot.sh <output.png>
id=$(swift -e 'import CoreGraphics
let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as! [[String: Any]]
if let w = list.first(where: { ($0["kCGWindowOwnerName"] as? String) == "VsLike" && ($0["kCGWindowLayer"] as? Int) == 0 }) { print(w["kCGWindowNumber"]!) }' 2>/dev/null)
[ -n "$id" ] || { echo "VsLike window not found" >&2; exit 1; }
screencapture -x -o -l"$id" "$1"
