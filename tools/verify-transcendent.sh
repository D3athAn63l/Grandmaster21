#!/usr/bin/env bash
# Headless checks against real RimWorld/Harmony DLLs and Mono.Cecil 0.11.x.
# No stubs. Does not launch RimWorld or claim to test gameplay/reloading.
# Usage: ./tools/verify-transcendent.sh <Managed> <Harmony.dll> <Mono.Cecil.dll>
# CSC, FRAMEWORK_REFS mirror build.sh. RUNNER defaults to mono; may name dotnet.
set -euo pipefail
cd "$(dirname "$0")/.."
MANAGED="$(realpath "${1:?Managed directory required}")"
HARMONY="$(realpath "${2:?Harmony DLL required}")"
CECIL="$(realpath "${3:?Mono.Cecil DLL required}")"
FRAMEWORK="${FRAMEWORK_REFS:-/usr/lib/mono/4.5}"
COMPILER="${CSC:-mcs}"
RUNNER="${RUNNER:-mono}"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT
cp "$MANAGED"/*.dll "$OUT/"
cp "$HARMONY" "$CECIL" Assemblies/Grandmaster21.dll "$OUT/"
"$COMPILER" -nologo -target:exe -out:"$OUT/transcendent.exe" -nostdlib -noconfig -warn:2 \
  -r:"$FRAMEWORK/mscorlib.dll" -r:"$FRAMEWORK/System.dll" -r:"$FRAMEWORK/System.Core.dll" \
  -r:"$FRAMEWORK/System.Xml.dll" -r:"$FRAMEWORK/System.Xml.Linq.dll" -r:"$FRAMEWORK/Facades/netstandard.dll" \
  -r:"$OUT/Assembly-CSharp.dll" -r:"$OUT/UnityEngine.CoreModule.dll" \
  -r:"$OUT/Mono.Cecil.dll" -r:"$OUT/Grandmaster21.dll" Tests/TranscendentChecks.cs
if [[ "$(basename "$RUNNER")" == dotnet ]]; then
  cat > "$OUT/transcendent.runtimeconfig.json" <<'JSON'
{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"},"rollForward":"LatestMajor"}}
JSON
fi
"$RUNNER" "$OUT/transcendent.exe" "$OUT/Grandmaster21.dll" "$PWD" "$OUT/project-save.xml"
