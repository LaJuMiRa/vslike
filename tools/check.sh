#!/bin/sh
# Baut und testet die ganze Solution; bricht beim ersten Fehler mit Exit-Code != 0 ab.
set -e
cd "$(dirname "$0")/.."
dotnet build VsLike.slnx -v q -nologo 2>&1 | grep -E " error |Fehler|Warn" | sort -u
dotnet build VsLike.slnx -v q -nologo > /dev/null
dotnet test VsLike.slnx --no-build 2>&1 | tee /tmp/vslike-test.log | grep -E "Bestanden|Passed!|Fehler!|Failed!|\[FAIL\]"
! grep -qE "Fehler!|Failed!" /tmp/vslike-test.log
