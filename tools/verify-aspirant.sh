#!/usr/bin/env bash
# Execute the actual learning/rate/promotion/Scribe paths against real RimWorld and Harmony DLLs.
# Does not launch the Unity player. CSC, FRAMEWORK_REFS and RUNNER mirror verify-medicine.sh.
set -euo pipefail
cd "$(dirname "$0")/.."
MANAGED="$(realpath "${1:?Managed directory required}")"
HARMONY="$(realpath "${2:?Harmony DLL required}")"
FRAMEWORK="${FRAMEWORK_REFS:-/usr/lib/mono/4.5}"
COMPILER="${CSC:-mcs}"
RUNNER="${RUNNER:-mono}"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT
cp "$MANAGED"/*.dll "$HARMONY" Assemblies/Grandmaster21.dll "$OUT/"
[ -f "$OUT/com.rlabrecque.steamworks.net.dll" ] || \
  "$COMPILER" -target:library -out:"$OUT/com.rlabrecque.steamworks.net.dll" tools/stubs/SteamworksShim.cs
"$COMPILER" -target:exe -out:"$OUT/aspirant.exe" -nostdlib -noconfig \
  -r:"$FRAMEWORK/mscorlib.dll" -r:"$FRAMEWORK/System.dll" -r:"$FRAMEWORK/System.Core.dll" \
  -r:"$FRAMEWORK/System.Xml.dll" -r:"$FRAMEWORK/System.Xml.Linq.dll" -r:"$FRAMEWORK/Facades/netstandard.dll" \
  -r:"$OUT/Assembly-CSharp.dll" -r:"$OUT/UnityEngine.CoreModule.dll" -r:"$OUT/0Harmony.dll" \
  -r:"$OUT/Grandmaster21.dll" Tests/AspirantChecks.cs
"$RUNNER" "$OUT/aspirant.exe" "$PWD" "$OUT/save.xml"
