#!/usr/bin/env bash
# Build first with build.sh. Optional fourth argument audits Gun_BeamGraser in installed Data XML.
set -euo pipefail
cd "$(dirname "$0")/.."
MANAGED="$(realpath "${1:?Managed directory required}")"
HARMONY="$(realpath "${2:?Harmony DLL required}")"
CECIL="$(realpath "${3:?Mono.Cecil DLL required}")"
DATA="${4:-}"
[ -z "$DATA" ] || DATA="$(realpath "$DATA")"
FRAMEWORK="${FRAMEWORK_REFS:-/usr/lib/mono/4.5}"
COMPILER="${CSC:-mcs}"
RUNNER="${RUNNER:-mono}"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT
cp "$MANAGED"/*.dll "$OUT/"
cp "$HARMONY" "$CECIL" Assemblies/Grandmaster21.dll "$OUT/"
[ -f "$OUT/com.rlabrecque.steamworks.net.dll" ] || \
  "$COMPILER" -target:library -out:"$OUT/com.rlabrecque.steamworks.net.dll" tools/stubs/SteamworksShim.cs
"$COMPILER" -target:exe -out:"$OUT/beam.exe" -nostdlib -noconfig -warn:2 \
  -r:"$FRAMEWORK/mscorlib.dll" -r:"$FRAMEWORK/System.dll" -r:"$FRAMEWORK/System.Core.dll" \
  -r:"$FRAMEWORK/System.Xml.dll" -r:"$FRAMEWORK/System.Xml.Linq.dll" -r:"$FRAMEWORK/Facades/netstandard.dll" \
  -r:"$OUT/Assembly-CSharp.dll" -r:"$OUT/UnityEngine.CoreModule.dll" -r:"$OUT/0Harmony.dll" \
  -r:"$OUT/Mono.Cecil.dll" -r:"$OUT/Grandmaster21.dll" Tests/BeamParryChecks.cs
( cd "$OUT" && "$RUNNER" beam.exe Assembly-CSharp.dll "$DATA" )
