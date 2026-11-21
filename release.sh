#!/usr/bin/env bash
# Tests everything, builds the app and packages dist/VsLike-<version>-arm64.zip plus its SHA-256.
#
#   ./release.sh               version from the VERSION file
#   VERSION=1.2.3 ./release.sh
#   SKIP_TESTS=1 ./release.sh  package only (not for real releases)
#
# Steps:
#   1. engine tests      dotnet test engine/VsLike.Engine.slnx (Roslyn, StyleCop, diagram writer, JSON-RPC end to end)
#   2. build             ./build.sh  -> build/VsLike.app (also caches netcoredbg for the debugger tests)
#   3. app tests         swift test (library, engine client, dotnet CLI, debugger, app workspace against the real engine)
#   4. smoke test        starts the built engine from the bundle and loads a sample solution
#   5. package           dist/VsLike-<version>-arm64.zip + .sha256
#
# Used locally and by .github/workflows/release.yml.
set -euo pipefail

cd "$(dirname "$0")"
VERSION="${VERSION:-$(tr -d '[:space:]' < VERSION)}"
export VERSION
WORK="${VSLIKE_WORK_DIR:-${TMPDIR:-/tmp}/vslike-build}"
export VSLIKE_WORK_DIR="$WORK"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if [[ "${SKIP_TESTS:-0}" != "1" ]]; then
    echo "==> Engine tests"
    dotnet build engine/VsLike.Engine.slnx -nologo -v:quiet
    dotnet test engine/VsLike.Engine.slnx --no-build -nologo
fi

# Before the app tests: build.sh downloads netcoredbg into the cache, where the debugger tests find it.
./build.sh

if [[ "${SKIP_TESTS:-0}" != "1" ]]; then
    echo "==> App tests"
    # The engine tests built the engine that the Swift integration tests start.
    swift test --scratch-path "$WORK/spm-tests"
fi

echo "==> Smoke test of the bundled engine"
APP="build/VsLike.app"
VSLIKE_ENGINE_DLL="$PWD/$APP/Contents/Resources/engine/VsLikeEngine.dll" \
    VSLIKE_NETCOREDBG="$PWD/$APP/Contents/Resources/netcoredbg/netcoredbg" \
    VSLIKE_DEBUG_HOOK="$PWD/$APP/Contents/Resources/debughook/VsLike.DebugStartupHook.dll" \
    swift test --scratch-path "$WORK/spm-tests" \
    --filter "engine_loadsSolutionAndServesIntelliSenseDiagnosticsAndDiagram|debugger_stopsAtBreakpointAndShowsLocals|debugger_attachesToProgramHeldByStartupHook" >/dev/null
test -x "$APP/Contents/Resources/netcoredbg/netcoredbg"
test -f "$APP/Contents/Resources/debughook/VsLike.DebugStartupHook.dll"
# Verify the staging copy: build/ may sit in iCloud Drive, which adds metadata that breaks the seal of the copy.
codesign --verify --strict "$WORK/stage/VsLike.app"
echo "    bundled engine and debugger OK"

mkdir -p dist
ZIP="dist/VsLike-$VERSION-arm64.zip"
rm -f "$ZIP" "$ZIP.sha256"
# Zip the clean staging copy (build/ may carry iCloud metadata) without extended attributes.
ditto -c -k --norsrc --noextattr --keepParent "$WORK/stage/VsLike.app" "$ZIP"
SHA="$(shasum -a 256 "$ZIP" | awk '{print $1}')"
echo "$SHA  $(basename "$ZIP")" > "$ZIP.sha256"

echo "==> $ZIP"
echo "    sha256: $SHA"
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
    {
        echo "zip=$ZIP"
        echo "sha256=$SHA"
    } >> "$GITHUB_OUTPUT"
fi
