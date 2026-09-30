#!/usr/bin/env bash
# Cooking 21 real-DLL checks. Build first with build.sh. Compiles Tests/CookingChecks.cs against the real
# RimWorld/Unity/Harmony assemblies and runs it: the installed Cooking hooks and the REAL vanilla methods
# they attach to execute (poison, stack merge/split, ingestion, rot, recipe production, Scribe).
#   ./tools/verify-cooking.sh <Managed dir> <0Harmony.dll> <Mono.Cecil.dll>
set -euo pipefail
cd "$(dirname "$0")/.."
MANAGED="$(realpath "${1:?Managed directory required}")"
HARMONY="$(realpath "${2:?Harmony DLL required}")"
CECIL="$(realpath "${3:?Mono.Cecil DLL required}")"
FRAMEWORK="${FRAMEWORK_REFS:-/usr/lib/mono/4.5}"
COMPILER="${CSC:-mcs}"
RUNNER="${RUNNER:-mono}"
[ -f Assemblies/Grandmaster21.dll ] || { echo "build first: ./build.sh <Managed> <0Harmony.dll>" >&2; exit 1; }
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT
cp "$MANAGED"/*.dll "$OUT/"
cp "$HARMONY" "$CECIL" Assemblies/Grandmaster21.dll "$OUT/"
[ -f "$OUT/com.rlabrecque.steamworks.net.dll" ] || \
  "$COMPILER" -nologo -target:library -out:"$OUT/com.rlabrecque.steamworks.net.dll" tools/stubs/SteamworksShim.cs
# The real ReservationManager's static initialiser reaches the shader database, whose asset-bundle fallback
# names UnityEngine.AssetBundle; the game ships that module, the headless Managed/ subset does not.
[ -f "$OUT/UnityEngine.AssetBundleModule.dll" ] || \
  "$COMPILER" -nologo -target:library -out:"$OUT/UnityEngine.AssetBundleModule.dll" \
    -r:"$FRAMEWORK/mscorlib.dll" -r:"$FRAMEWORK/System.dll" -r:"$FRAMEWORK/Facades/netstandard.dll" \
    -r:"$OUT/UnityEngine.CoreModule.dll" tools/stubs/AssetBundleShim.cs
"$COMPILER" -nologo -target:exe -out:"$OUT/cooking.exe" -nostdlib -noconfig -warn:2 \
  -r:"$FRAMEWORK/mscorlib.dll" -r:"$FRAMEWORK/System.dll" -r:"$FRAMEWORK/System.Core.dll" \
  -r:"$FRAMEWORK/System.Xml.dll" -r:"$FRAMEWORK/System.Xml.Linq.dll" -r:"$FRAMEWORK/Facades/netstandard.dll" \
  -r:"$OUT/Assembly-CSharp.dll" -r:"$OUT/UnityEngine.CoreModule.dll" -r:"$OUT/0Harmony.dll" \
  -r:"$OUT/Mono.Cecil.dll" -r:"$OUT/Grandmaster21.dll" Tests/CookingChecks.cs
( cd "$OUT" && "$RUNNER" cooking.exe Assembly-CSharp.dll "$OUT" )
